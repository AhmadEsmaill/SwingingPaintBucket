using UnityEngine;
using System.Collections.Generic;

// 2-D SPH fluid (Müller et al. 2003) simulating paint inside the swinging bucket.
// Particles live in world-space XY; bucket walls move each frame with the pendulum.
public class SPHSimulator : MonoBehaviour
{
    [Header("SPH Parameters")]
    public float smoothingRadius      = 0.04f;   // h — kernel support radius (m)
    public float restDensity          = 1000f;   // ρ₀ (kg/m³)
    public float stiffness            = 200f;    // k — pressure constant
    public float viscosityCoeff       = 0.08f;   // μ — dynamic viscosity
    public float particleMass         = 0.02f;   // mass per SPH particle (kg)
    public int   initialParticleCount = 120;

    [Header("Bucket Geometry")]
    public float bucketWidth    = 0.15f;   // interior width  (m)
    public float bucketHeight   = 0.20f;   // interior height (m)
    public float holeWidth      = 0.015f;  // exit hole width at bucket bottom (m)
    public float wallRestitution = 0.2f;   // velocity kept after wall bounce

    [Header("References")]
    public PendulumSimulator pendulum;

    private readonly List<SPHParticle> particles  = new List<SPHParticle>();
    private readonly List<SPHParticle> exitBuffer = new List<SPHParticle>();

    // Precomputed kernel constants (Müller 2003)
    private float h2, h6, h9;
    private float kPoly6, kSpikyGrad, kViscLap;

    private Vector2 BucketCenter =>
        new Vector2(pendulum.BucketPosition.x, pendulum.BucketPosition.y);

    void Start() => Initialize();

    // Call this whenever the simulation is reset so particles re-spawn inside the bucket.
    public void Initialize()
    {
        particles.Clear();
        exitBuffer.Clear();
        PrecomputeKernels();
        SpawnParticles();
    }

    private void PrecomputeKernels()
    {
        float h = smoothingRadius;
        h2 = h * h;
        h6 = h2 * h2 * h2;
        h9 = h6 * h2 * h;

        kPoly6     =  315f / (64f * Mathf.PI * h9);
        // Spiky gradient constant is baked with its negative sign
        kSpikyGrad = -45f  / (Mathf.PI * h6);
        kViscLap   =  45f  / (Mathf.PI * h6);
    }

    private void SpawnParticles()
    {
        if (pendulum == null) return;

        Vector2 center  = BucketCenter;
        float   spacing = smoothingRadius * 0.75f;
        int     cols    = Mathf.Max(1, Mathf.FloorToInt(bucketWidth / spacing));
        int     rows    = Mathf.CeilToInt((float)initialParticleCount / cols);

        float startX = center.x - (cols - 1) * spacing * 0.5f;
        float startY = center.y - bucketHeight * 0.5f + spacing;

        int spawned = 0;
        for (int r = 0; r < rows && spawned < initialParticleCount; r++)
        {
            for (int c = 0; c < cols && spawned < initialParticleCount; c++)
            {
                Vector2 pos = new Vector2(startX + c * spacing, startY + r * spacing);
                // Small jitter to break grid symmetry
                pos.x += Random.Range(-spacing * 0.05f, spacing * 0.05f);
                pos.y += Random.Range(-spacing * 0.05f, spacing * 0.05f);
                particles.Add(new SPHParticle(pos));
                spawned++;
            }
        }
    }

    void FixedUpdate()
    {
        if (pendulum == null || particles.Count == 0) return;

        float dt = Time.fixedDeltaTime;
        ComputeDensityPressure();
        ComputeAccelerations();
        Integrate(dt);
        EnforceBoundaries();
        CollectExits();
    }

    // ── Density & pressure ────────────────────────────────────────────────────

    private void ComputeDensityPressure()
    {
        int n = particles.Count;
        for (int i = 0; i < n; i++)
        {
            float   rho = 0f;
            Vector2 pi  = particles[i].position;

            for (int j = 0; j < n; j++)
            {
                float r2 = (pi - particles[j].position).sqrMagnitude;
                if (r2 < h2)
                {
                    float q = h2 - r2;
                    rho += particleMass * kPoly6 * q * q * q;
                }
            }

            particles[i].density  = Mathf.Max(rho, 1f);
            // Tait equation of state: P = k(ρ - ρ₀)
            particles[i].pressure = stiffness * (particles[i].density - restDensity);
        }
    }

    // ── Force → acceleration ──────────────────────────────────────────────────

    private void ComputeAccelerations()
    {
        float g = pendulum != null ? pendulum.gravity : 9.81f;
        int   n = particles.Count;

        for (int i = 0; i < n; i++)
        {
            SPHParticle pi   = particles[i];
            Vector2 aPres    = Vector2.zero;
            Vector2 aViscSum = Vector2.zero;

            for (int j = 0; j < n; j++)
            {
                if (i == j) continue;
                SPHParticle pj = particles[j];

                Vector2 rij = pi.position - pj.position;
                float   r   = rij.magnitude;
                if (r >= smoothingRadius || r < 1e-5f) continue;

                float   diff = smoothingRadius - r;
                Vector2 rHat = rij / r;

                // Pressure: a = -Σⱼ mⱼ (Pᵢ/ρᵢ² + Pⱼ/ρⱼ²) ∇W_spiky
                float pTerm = pi.pressure / (pi.density * pi.density)
                            + pj.pressure / (pj.density * pj.density);
                aPres += -particleMass * pTerm * kSpikyGrad * diff * diff * rHat;

                // Viscosity sum (will be scaled by μ/ρᵢ below)
                // a_visc = (μ/ρᵢ) Σⱼ mⱼ(vⱼ-vᵢ)/ρⱼ ∇²W_visc
                aViscSum += (particleMass / pj.density)
                          * (pj.velocity - pi.velocity)
                          * kViscLap * diff;
            }

            particles[i].acceleration = aPres
                                       + (viscosityCoeff / pi.density) * aViscSum
                                       + new Vector2(0f, -g);
        }
    }

    // ── Integration (symplectic Euler) ────────────────────────────────────────

    private void Integrate(float dt)
    {
        foreach (SPHParticle p in particles)
        {
            p.velocity += p.acceleration * dt;
            p.position += p.velocity * dt;
        }
    }

    // ── Boundary enforcement ──────────────────────────────────────────────────

    private void EnforceBoundaries()
    {
        Vector2 center   = BucketCenter;
        float   halfW    = bucketWidth  * 0.5f;
        float   halfH    = bucketHeight * 0.5f;
        float   halfHole = holeWidth    * 0.5f;
        float   bottom   = center.y - halfH;

        foreach (SPHParticle p in particles)
        {
            // Left wall
            if (p.position.x < center.x - halfW)
            {
                p.position.x = center.x - halfW;
                p.velocity.x = Mathf.Abs(p.velocity.x) * wallRestitution;
            }
            // Right wall
            if (p.position.x > center.x + halfW)
            {
                p.position.x = center.x + halfW;
                p.velocity.x = -Mathf.Abs(p.velocity.x) * wallRestitution;
            }
            // Top rim — paint can't overflow
            if (p.position.y > center.y + halfH)
            {
                p.position.y = center.y + halfH;
                p.velocity.y = -Mathf.Abs(p.velocity.y) * wallRestitution;
            }
            // Bottom wall — solid except at hole
            if (p.position.y < bottom)
            {
                bool inHole = Mathf.Abs(p.position.x - center.x) < halfHole;
                if (!inHole)
                {
                    p.position.y = bottom;
                    p.velocity.y = Mathf.Abs(p.velocity.y) * wallRestitution;
                }
            }
        }
    }

    // ── Exit detection ────────────────────────────────────────────────────────

    private void CollectExits()
    {
        Vector2 center   = BucketCenter;
        float   bottom   = center.y - bucketHeight * 0.5f;
        float   halfHole = holeWidth * 0.5f;

        for (int i = particles.Count - 1; i >= 0; i--)
        {
            SPHParticle p = particles[i];
            // Particle cleared the hole threshold
            if (p.position.y < bottom - 0.002f &&
                Mathf.Abs(p.position.x - center.x) < halfHole)
            {
                exitBuffer.Add(p);
                particles.RemoveAt(i);
            }
        }
    }

    // Called each frame by PaintFlowController to consume exited particles.
    public List<SPHParticle> DrainExitBuffer()
    {
        var result = new List<SPHParticle>(exitBuffer);
        exitBuffer.Clear();
        return result;
    }

    public float FillRatio     => initialParticleCount > 0
                                ? (float)particles.Count / initialParticleCount : 0f;
    public int   ParticleCount => particles.Count;
}
