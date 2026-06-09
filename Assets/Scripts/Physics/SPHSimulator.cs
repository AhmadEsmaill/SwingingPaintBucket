using UnityEngine;
using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Burst;
using System.Collections.Generic;

// SPH fluid simulation (Müller 2003) using Unity Job System + Burst.
// All particle data is stored in NativeArrays (no GC); all phases run as
// parallel Burst-compiled jobs across available CPU cores.
// Auto-calibration: h and ρ₀ are derived from particle count + bucket area,
// so the simulation stays physically stable at any particle count.
public class SPHSimulator : MonoBehaviour
{
    [Header("SPH Parameters")]
    public float stiffness            = 200f;    // k — pressure constant
    public float viscosityCoeff       = 0.02f;   // μ — dynamic viscosity
    public float particleMass         = 0.005f;  // mass per particle (kg)
    public int   initialParticleCount = 5000;

    [Header("Auto-calibrated (do not set manually)")]
    public float smoothingRadius;   // h — set by AutoCalibrate()
    public float restDensity;       // ρ₀ — set by AutoCalibrate()

    [Header("Bucket Geometry")]
    public float bucketWidth    = 0.15f;
    public float bucketHeight   = 0.20f;
    public float holeWidth      = 0.05f;
    public float wallRestitution = 0.2f;

    [Header("References")]
    public PendulumSimulator pendulum;

    // Particle data — Struct-of-Arrays layout for cache efficiency
    NativeArray<float2> positions;
    NativeArray<float2> velocities;
    NativeArray<float2> accelerations;
    NativeArray<float>  densities;
    NativeArray<float>  pressures;

    // Exit detection (written per-particle each frame)
    NativeArray<int>    exitFlags;   // 1 = exited through hole this frame
    NativeArray<float2> exitPos;     // world position at exit moment
    NativeArray<float2> exitVel;     // velocity at exit moment

    // Spatial hash: cell key → particle indices
    NativeParallelMultiHashMap<int, int> hashGrid;

    int n;  // total particle count (constant after Initialize)

    // Kernel constants (precomputed from h)
    float h2, h6, h9, kPoly6, kSpikyGrad, kViscLap, invCellSize;

    readonly List<SPHParticle> exitBuffer = new List<SPHParticle>();

    private bool isSimulating = false;

    // Allocate particles on Start but don't run until StartSimulation() is called.
    void Start()     => Initialize();
    void OnDestroy() => DisposeNative();

    public void StartSimulation() => isSimulating = true;
    public void StopSimulation()  => isSimulating = false;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public void Initialize()
    {
        DisposeNative();
        exitBuffer.Clear();

        n = initialParticleCount;
        AutoCalibrate();
        Allocate();
        SpawnParticles();
    }

    void DisposeNative()
    {
        if (positions.IsCreated)     positions.Dispose();
        if (velocities.IsCreated)    velocities.Dispose();
        if (accelerations.IsCreated) accelerations.Dispose();
        if (densities.IsCreated)     densities.Dispose();
        if (pressures.IsCreated)     pressures.Dispose();
        if (exitFlags.IsCreated)     exitFlags.Dispose();
        if (exitPos.IsCreated)       exitPos.Dispose();
        if (exitVel.IsCreated)       exitVel.Dispose();
        if (hashGrid.IsCreated)      hashGrid.Dispose();
    }

    void Allocate()
    {
        positions     = new NativeArray<float2>(n, Allocator.Persistent);
        velocities    = new NativeArray<float2>(n, Allocator.Persistent);
        accelerations = new NativeArray<float2>(n, Allocator.Persistent);
        densities     = new NativeArray<float> (n, Allocator.Persistent);
        pressures     = new NativeArray<float> (n, Allocator.Persistent);
        exitFlags     = new NativeArray<int>   (n, Allocator.Persistent);
        exitPos       = new NativeArray<float2>(n, Allocator.Persistent);
        exitVel       = new NativeArray<float2>(n, Allocator.Persistent);
        hashGrid      = new NativeParallelMultiHashMap<int, int>(n * 4, Allocator.Persistent);
    }

    // ── Parameter auto-calibration ────────────────────────────────────────────

    // Derive h so that particles are separated by ~1.5h/2 at rest, then
    // measure the actual equilibrium density at that spacing to set ρ₀.
    void AutoCalibrate()
    {
        float bucketArea = bucketWidth * bucketHeight;
        float spacing    = math.sqrt(bucketArea / n);   // natural inter-particle spacing
        smoothingRadius  = spacing * 2f;                // h = 2 × spacing

        PrecomputeKernels();

        restDensity = MeasureEquilibriumDensity(spacing) * 0.97f;
    }

    // Simulate the Poly6 density sum for a particle surrounded by a regular grid
    // of neighbors at the given spacing — gives the actual rest density.
    float MeasureEquilibriumDensity(float spacing)
    {
        float rho   = 0f;
        int   range = Mathf.CeilToInt(smoothingRadius / spacing) + 1;
        for (int dx = -range; dx <= range; dx++)
        for (int dy = -range; dy <= range; dy++)
        {
            float r2 = (dx * spacing) * (dx * spacing) + (dy * spacing) * (dy * spacing);
            if (r2 < h2) { float q = h2 - r2; rho += particleMass * kPoly6 * q * q * q; }
        }
        return rho;
    }

    void PrecomputeKernels()
    {
        float h = smoothingRadius;
        h2 = h*h; h6 = h2*h2*h2; h9 = h6*h2*h;
        kPoly6     =  315f / (64f * math.PI * h9);
        kSpikyGrad = -45f  / (math.PI * h6);
        kViscLap   =  45f  / (math.PI * h6);
        invCellSize = 1f / smoothingRadius;
    }

    // ── Spawn ─────────────────────────────────────────────────────────────────

    // Returns the bucket's normalised local axes projected onto the world XY plane.
    // axisY points from the bucket top toward the bucket bottom (along rope direction).
    // axisX is perpendicular to axisY in 2D.
    static void GetBucketAxes(PendulumSimulator p, out float2 axisX, out float2 axisY)
    {
        Vector3 rd   = p.RopeDirection;
        float2  raw  = new float2(rd.x, rd.y);
        float   len  = math.length(raw);
        axisY = len > 0.001f ? raw / len : new float2(0f, -1f);
        axisX = new float2(-axisY.y, axisY.x);
    }

    void SpawnParticles()
    {
        if (pendulum == null) return;

        GetBucketAxes(pendulum, out float2 axisX, out float2 axisY);

        float2 c       = new float2(pendulum.BucketCenter.x, pendulum.BucketCenter.y);
        float  sp      = smoothingRadius * 0.75f;
        int    cols    = math.max(1, (int)(bucketWidth / sp));
        float  startLX = -(cols - 1) * sp * 0.5f;
        float  startLY = -bucketHeight * 0.5f + sp;

        for (int i = 0; i < n; i++)
        {
            int   col = i % cols, row = i / cols;
            float lx  = startLX + col * sp + UnityEngine.Random.Range(-sp * 0.05f, sp * 0.05f);
            float ly  = startLY + row * sp + UnityEngine.Random.Range(-sp * 0.05f, sp * 0.05f);
            positions[i]  = c + axisX * lx + axisY * ly;
            velocities[i] = float2.zero;
        }
    }

    // ── Simulation loop ───────────────────────────────────────────────────────

    void FixedUpdate()
    {
        if (!isSimulating || pendulum == null || n == 0) return;

        // Active particle count follows paint fill ratio.
        // As paint depletes the bucket mass, fewer particles are simulated —
        // which naturally reduces flow until the bucket is empty.
        int activeN = (pendulum != null)
            ? Mathf.Max(0, Mathf.RoundToInt(n * pendulum.PaintFillRatio))
            : n;

        if (activeN == 0) return;

        float  dt     = Time.fixedDeltaTime;
        float2 center = new float2(pendulum.BucketCenter.x, pendulum.BucketCenter.y);

        hashGrid.Clear();

        var jBuild = new BuildGridJob {
            positions   = positions,
            grid        = hashGrid.AsParallelWriter(),
            invCellSize = invCellSize
        }.Schedule(activeN, 64);

        var jDens = new DensityPressureJob {
            positions    = positions,
            densities    = densities,
            pressures    = pressures,
            grid         = hashGrid,
            h2 = h2, kPoly6 = kPoly6,
            particleMass = particleMass,
            stiffness    = stiffness,
            restDensity  = restDensity,
            invCellSize  = invCellSize
        }.Schedule(activeN, 32, jBuild);

        var jForce = new ForceJob {
            positions       = positions,
            velocities      = velocities,
            accelerations   = accelerations,
            densities       = densities,
            pressures       = pressures,
            grid            = hashGrid,
            smoothingRadius = smoothingRadius,
            h2 = h2, kSpikyGrad = kSpikyGrad, kViscLap = kViscLap,
            particleMass    = particleMass,
            viscosityCoeff  = viscosityCoeff,
            gravity         = pendulum.gravity,
            invCellSize     = invCellSize
        }.Schedule(activeN, 32, jDens);

        var jInteg = new IntegrateJob {
            positions     = positions,
            velocities    = velocities,
            accelerations = accelerations,
            dt            = dt
        }.Schedule(activeN, 64, jForce);

        GetBucketAxes(pendulum, out float2 axisX, out float2 axisY);

        var jBound = new BoundaryExitJob {
            positions     = positions,
            velocities    = velocities,
            exitFlags     = exitFlags,
            exitPos       = exitPos,
            exitVel       = exitVel,
            center        = center,
            axisX         = axisX,
            axisY         = axisY,
            halfW         = bucketWidth  * 0.5f,
            halfH         = bucketHeight * 0.5f,
            halfHole      = holeWidth    * 0.5f,
            wallRestitution = wallRestitution
        }.Schedule(activeN, 32, jInteg);

        jBound.Complete();

        for (int i = 0; i < activeN; i++)
        {
            if (exitFlags[i] == 1)
            {
                exitBuffer.Add(new SPHParticle(new Vector2(exitPos[i].x, exitPos[i].y)) {
                    velocity = new Vector2(exitVel[i].x, exitVel[i].y)
                });
            }
        }
    }

    // Called each frame by PaintFlowController
    public List<SPHParticle> DrainExitBuffer()
    {
        var result = new List<SPHParticle>(exitBuffer);
        exitBuffer.Clear();
        return result;
    }

    public float FillRatio     => pendulum != null ? pendulum.PaintFillRatio : 1f;
    public int   ParticleCount => n;

    // ═════════════════════════════════════════════════════════════════════════
    //  Jobs
    // ═════════════════════════════════════════════════════════════════════════

    [BurstCompile]
    struct BuildGridJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float2> positions;
        public NativeParallelMultiHashMap<int, int>.ParallelWriter grid;
        public float invCellSize;

        public void Execute(int i)
        {
            int cx  = (int)math.floor(positions[i].x * invCellSize);
            int cy  = (int)math.floor(positions[i].y * invCellSize);
            int key = unchecked(cx * 73856093 ^ cy * 19349663);
            grid.Add(key, i);
        }
    }

    [BurstCompile]
    struct DensityPressureJob : IJobParallelFor
    {
        [ReadOnly]  public NativeArray<float2> positions;
        [WriteOnly] public NativeArray<float>  densities;
        [WriteOnly] public NativeArray<float>  pressures;
        [ReadOnly]  public NativeParallelMultiHashMap<int, int> grid;
        public float h2, kPoly6, particleMass, stiffness, restDensity, invCellSize;

        public void Execute(int i)
        {
            float2 pi  = positions[i];
            float  rho = 0f;
            int    ocx = (int)math.floor(pi.x * invCellSize);
            int    ocy = (int)math.floor(pi.y * invCellSize);

            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                int key = unchecked((ocx + dx) * 73856093 ^ (ocy + dy) * 19349663);
                if (grid.TryGetFirstValue(key, out int j, out var it))
                do {
                    float r2 = math.lengthsq(pi - positions[j]);
                    if (r2 < h2) { float q = h2 - r2; rho += particleMass * kPoly6 * q * q * q; }
                } while (grid.TryGetNextValue(out j, ref it));
            }

            float d = math.max(rho, 1f);
            densities[i] = d;
            pressures[i] = stiffness * (d - restDensity);
        }
    }

    [BurstCompile]
    struct ForceJob : IJobParallelFor
    {
        [ReadOnly]  public NativeArray<float2> positions;
        [ReadOnly]  public NativeArray<float2> velocities;
        [WriteOnly] public NativeArray<float2> accelerations;
        [ReadOnly]  public NativeArray<float>  densities;
        [ReadOnly]  public NativeArray<float>  pressures;
        [ReadOnly]  public NativeParallelMultiHashMap<int, int> grid;
        public float smoothingRadius, h2, kSpikyGrad, kViscLap;
        public float particleMass, viscosityCoeff, gravity, invCellSize;

        public void Execute(int i)
        {
            float2 pi    = positions[i];
            float  rhoi  = densities[i], presi = pressures[i];
            float2 vi    = velocities[i];
            float2 aPres = float2.zero, aVisc = float2.zero;
            int    ocx   = (int)math.floor(pi.x * invCellSize);
            int    ocy   = (int)math.floor(pi.y * invCellSize);

            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                int key = unchecked((ocx + dx) * 73856093 ^ (ocy + dy) * 19349663);
                if (grid.TryGetFirstValue(key, out int j, out var it))
                do {
                    if (j == i) continue;
                    float2 rij = pi - positions[j];
                    float  r2  = math.lengthsq(rij);
                    if (r2 >= h2 || r2 < 1e-10f) continue;
                    float  r    = math.sqrt(r2);
                    float  diff = smoothingRadius - r;
                    float2 rHat = rij / r;
                    float  pTerm = presi / (rhoi * rhoi) + pressures[j] / (densities[j] * densities[j]);
                    aPres += -particleMass * pTerm * kSpikyGrad * diff * diff * rHat;
                    aVisc += (particleMass / densities[j]) * (velocities[j] - vi) * kViscLap * diff;
                } while (grid.TryGetNextValue(out j, ref it));
            }

            accelerations[i] = aPres + (viscosityCoeff / rhoi) * aVisc + new float2(0f, -gravity);
        }
    }

    [BurstCompile]
    struct IntegrateJob : IJobParallelFor
    {
        public NativeArray<float2> positions;
        public NativeArray<float2> velocities;
        [ReadOnly] public NativeArray<float2> accelerations;
        public float dt;

        public void Execute(int i)
        {
            velocities[i] += accelerations[i] * dt;
            positions[i]  += velocities[i] * dt;
        }
    }

    // Enforces bucket walls in the bucket's LOCAL frame (rope-aligned),
    // detects exits through the bottom-centre hole, and recycles exited particles.
    [BurstCompile]
    struct BoundaryExitJob : IJobParallelFor
    {
        public NativeArray<float2> positions;
        public NativeArray<float2> velocities;
        [WriteOnly] public NativeArray<int>    exitFlags;
        [WriteOnly] public NativeArray<float2> exitPos;
        [WriteOnly] public NativeArray<float2> exitVel;
        public float2 center;
        public float2 axisX;        // perpendicular to rope, in world XY
        public float2 axisY;        // along rope, top → bottom of bucket
        public float  halfW, halfH, halfHole, wallRestitution;

        public void Execute(int i)
        {
            float2 p = positions[i];
            float2 v = velocities[i];
            exitFlags[i] = 0;

            // ── Project into bucket-local frame ───────────────────────────
            float2 offset = p - center;
            float  lx = math.dot(offset, axisX);   // ±halfW
            float  ly = math.dot(offset, axisY);   // -halfH (top) … +halfH (bottom)
            float  vx = math.dot(v, axisX);
            float  vy = math.dot(v, axisY);

            // Left wall
            if (lx < -halfW) { lx = -halfW; vx =  math.abs(vx) * wallRestitution; }
            // Right wall
            if (lx >  halfW) { lx =  halfW; vx = -math.abs(vx) * wallRestitution; }
            // Top rim (paint cannot overflow)
            if (ly < -halfH) { ly = -halfH; vy =  math.abs(vy) * wallRestitution; }
            // Bottom: solid except at centre hole
            if (ly > halfH)
            {
                bool inHole = math.abs(lx) < halfHole;
                if (inHole && ly > halfH + 0.002f)
                {
                    // Exit through hole.
                    // Constrain exit velocity: keep the axisY (downward) component,
                    // strongly dampen lateral so paint doesn't spray sideways.
                    float vyExit = math.max(vy, 0.3f);  // at least 0.3 m/s downward
                    float vxExit = vx * 0.12f;           // only 12% of lateral escapes
                    exitFlags[i] = 1;
                    exitPos[i]   = p;
                    exitVel[i]   = axisX * vxExit + axisY * vyExit;
                    float rx = ((i % 17) / 17f - 0.5f) * halfW * 0.6f;
                    lx = rx;
                    ly = -halfH * 0.3f;
                    vx = 0f;
                    vy = 0f;
                }
                else if (!inHole)
                {
                    ly = halfH;
                    vy = -math.abs(vy) * wallRestitution;
                }
            }

            // ── Transform back to world space ─────────────────────────────
            positions[i]  = center + axisX * lx + axisY * ly;
            velocities[i] = axisX  * vx  + axisY * vy;
        }
    }
}
