using UnityEngine;
using System.Collections.Generic;

// One colour poured into the bucket. Layers are ordered: index 0 was poured
// first, so it sits at the bottom and — with no mixing — exits the hole first.
[System.Serializable]
public class PaintLayer
{
    public Color color;
    public float fraction;   // relative share of the total paint (normalised on use)

    public PaintLayer(Color c, float f) { color = c; fraction = f; }
}

public class PaintFlowController : MonoBehaviour
{
    [Header("References")]
    public PendulumSimulator pendulum;
    public CanvasPainter canvasPainter;
    public GameObject dropletPrefab;

    [Header("Paint Colours (index 0 = poured first = bottom = exits first)")]
    public List<PaintLayer> layers = new List<PaintLayer> { new PaintLayer(Color.red, 1f) };
    // 0 = sharp layers (FIFO, no mixing); 1 = fully pre-mixed single colour.
    public float colorBlendFactor = 0f;
    public float viscosity = 0.01f;
    public float holeRadius = 0.005f;
    public float flowRateMultiplier = 1f;

    [Header("SPH (assign to enable SPH mode; leave empty for Torricelli fallback)")]
    public SPHSimulator sphSimulator;

    [Header("Pool Settings")]
    public int poolSize = 100;

    [Header("SPH stream smoothing")]
    // SPH can spit many particles through the hole in a single physics step. To
    // avoid one big clump every step, exits are queued and released a few per
    // frame — many small groups with tiny gaps read as a continuous stream.
    public int releaseSmoothing    = 4;   // spread each exit burst over ~N frames
    public int maxDropletsPerFrame = 8;   // safety cap on releases per frame
    // Fraction of the bucket's swing velocity the exiting paint keeps. The arc on
    // the canvas comes mostly from the moving hole itself, so full (1.0) inheritance
    // flings the paint too far ahead of the bucket; ~0.5 looks natural.
    [Range(0f, 1f)] public float swingCarry = 0.5f;

    private List<PaintDroplet> dropletPool = new List<PaintDroplet>();
    private readonly Queue<SPHParticle> pendingExits = new Queue<SPHParticle>();

    // Continuous-jet emission (continuity: Q = A·v). We accumulate a fractional
    // droplet count each step and release whole droplets as a burst.
    private float emitAccum = 0f;
    private const float OrificeCd          = 0.65f;  // orifice discharge coefficient
    private const int   MaxDropletsPerStep = 16;     // perf guard per FixedUpdate

    void Start()
    {
        BuildPool();
    }

    private void BuildPool()
    {
        for (int i = 0; i < poolSize; i++)
        {
            GameObject obj = Instantiate(dropletPrefab);
            obj.SetActive(false);
            dropletPool.Add(obj.GetComponent<PaintDroplet>());
        }
    }

    private PaintDroplet GetFromPool()
    {
        foreach (var d in dropletPool)
            if (!d.gameObject.activeSelf) { d.gameObject.SetActive(true); return d; }
        return null;
    }

    void FixedUpdate()
    {
        if (pendulum == null || sphSimulator != null) return;   // SPH is metered in Update

        // Torricelli fallback (no SPH assigned).
        if (!pendulum.IsSimulating || pendulum.PaintMass <= 0f)
        {
            emitAccum = 0f;
            return;
        }
        EmitTorricelliJet(Time.fixedDeltaTime);
    }

    void Update()
    {
        if (pendulum == null || sphSimulator == null) return;

        // Drop any backlog when the sim is stopped so nothing leaks out after.
        if (!pendulum.IsSimulating)
        {
            if (pendingExits.Count > 0) pendingExits.Clear();
            return;
        }

        // Queue every particle that exited the hole this frame.
        foreach (SPHParticle p in sphSimulator.DrainExitBuffer())
            pendingExits.Enqueue(p);

        if (pendingExits.Count == 0) return;

        // Release only a fraction of the backlog each frame → a burst of N exits
        // is spread over ~releaseSmoothing frames, forming many small closely
        // spaced groups instead of one clump. Self-regulating: a bigger backlog
        // releases proportionally more, so the queue stays a few frames deep.
        int release = Mathf.Clamp(
            Mathf.CeilToInt(pendingExits.Count / (float)Mathf.Max(1, releaseSmoothing)),
            1, maxDropletsPerFrame);

        Vector3 hole = pendulum.BucketHolePosition;
        Vector3 bVel = pendulum.BucketVelocity;
        float   dt   = Time.deltaTime;
        float   radius = Mathf.Lerp(0.01f, 0.04f, Mathf.Clamp01(viscosity * 10f));

        int spawned = 0;
        while (spawned < release && pendingExits.Count > 0)
        {
            PaintDroplet droplet = GetFromPool();
            if (droplet == null) break;             // pool exhausted → natural cap

            SPHParticle p = pendingExits.Dequeue();

            // Vertical: a clean downward Torricelli exit (never upward — paint pours
            // from a hole at the bucket bottom).
            float downExit = Mathf.Sqrt(2f * pendulum.gravity
                             * Mathf.Max(0f, pendulum.PaintFillRatio) * 0.2f);
            downExit = Mathf.Max(0.5f, downExit * 0.6f);

            // Horizontal: keep a fraction of the bucket's swing velocity (swingCarry).
            // The arc itself comes from the moving hole; full inheritance overshoots.
            // A small random scatter gives the stream width, not a razor-thin line.
            float scatter = 0.06f;
            Vector3 vel = new Vector3(
                bVel.x * swingCarry + Random.Range(-scatter, scatter),
                -downExit,
                bVel.z * swingCarry + Random.Range(-scatter, scatter));

            // Spread the frame's group along the fall path so they don't stack.
            float   frac = release > 1 ? (float)spawned / release : 0f;
            Vector3 pos  = hole + vel * (frac * dt);
            float   jit  = holeRadius * 0.5f;
            pos += new Vector3(Random.Range(-jit, jit), 0f, Random.Range(-jit, jit));

            droplet.Launch(pos, vel, CurrentColor(), radius, canvasPainter);
            spawned++;
        }
    }

    // Emits the paint as a continuous jet: a dense thread of small droplets whose
    // count follows the continuity equation Q = A·v, so a thin hole gives a thin
    // continuous stream (not sparse drips) and a wide hole a heavier one.
    private void EmitTorricelliJet(float dt)
    {
        float g = pendulum.gravity;
        float h = pendulum.PaintFillRatio * 0.2f;   // paint column height (m)

        // Torricelli exit speed with an orifice discharge coefficient. A thin,
        // low-viscosity paint is only mildly throttled by viscosity.
        float viscosityFactor = 1f / (1f + viscosity * 10f);
        float exitSpeed = Mathf.Sqrt(2f * g * h) * OrificeCd * viscosityFactor * flowRateMultiplier;
        if (exitSpeed < 0.05f) return;

        // Volumetric flow through the hole (continuity).
        float area = Mathf.PI * holeRadius * holeRadius;
        float Q    = area * exitSpeed;              // m³/s

        // Jet thread thickness ≈ hole size; viscous paint clumps a bit larger.
        float rDrop = Mathf.Clamp(holeRadius, 0.004f, 0.02f)
                    * Mathf.Lerp(1f, 1.6f, Mathf.Clamp01(viscosity * 10f));
        float vDrop = (4f / 3f) * Mathf.PI * rDrop * rDrop * rDrop;

        // Droplets needed this step to carry volume Q·dt.
        emitAccum += (Q / Mathf.Max(vDrop, 1e-9f)) * dt;
        int count  = Mathf.FloorToInt(emitAccum);
        if (count <= 0) return;
        emitAccum -= count;
        count = Mathf.Min(count, MaxDropletsPerStep);

        Vector3 holePos   = pendulum.BucketHolePosition;
        Vector3 bucketVel = pendulum.BucketVelocity;
        // Jet inherits the bucket's horizontal velocity + downward exit speed.
        Vector3 jetVel = new Vector3(bucketVel.x, -exitSpeed, bucketVel.z);

        for (int i = 0; i < count; i++)
        {
            PaintDroplet droplet = GetFromPool();
            if (droplet == null) break;             // pool exhausted → natural cap

            // Spread the burst along the exit path within this step so the drops
            // form a continuous thread instead of stacking at a single point.
            float   frac = count > 1 ? (float)i / count : 0f;
            Vector3 pos  = holePos + jetVel * (frac * dt);
            // Tiny lateral jitter → jet break-up, avoids a ruler-straight line.
            pos += new Vector3(Random.Range(-rDrop, rDrop), 0f, Random.Range(-rDrop, rDrop)) * 0.5f;

            droplet.Launch(pos, jetVel, CurrentColor(), rDrop, canvasPainter);
        }
    }

    // Colour of the paint exiting the hole right now. Bottom layer (index 0)
    // drains first; as the bucket empties we walk up the stack. With blending
    // the exiting colour is pulled toward the fully-mixed average.
    private Color CurrentColor()
    {
        if (layers == null || layers.Count == 0) return Color.white;
        if (layers.Count == 1) return layers[0].color;

        // Total weight + fully-mixed (weight-averaged) colour.
        float total = 0f;
        foreach (var l in layers) total += Mathf.Max(0f, l.fraction);
        if (total <= 0f) return layers[0].color;

        Color mixed = new Color(0f, 0f, 0f, 0f);
        foreach (var l in layers)
            mixed += l.color * (Mathf.Max(0f, l.fraction) / total);

        // Portion of the total that has already poured out. The exiting layer is
        // the one whose cumulative band contains `emitted`.
        float fill    = pendulum != null ? Mathf.Clamp01(pendulum.PaintFillRatio) : 1f;
        float emitted = (1f - fill) * total;

        Color layered = layers[layers.Count - 1].color;
        float cum = 0f;
        for (int i = 0; i < layers.Count; i++)
        {
            float w = Mathf.Max(0f, layers[i].fraction);
            if (emitted <= cum + w || i == layers.Count - 1)
            {
                layered = layers[i].color;
                break;
            }
            cum += w;
        }

        if (colorBlendFactor <= 0f) return layered;
        if (colorBlendFactor >= 1f) return mixed;
        return Random.value < colorBlendFactor ? mixed : layered;
    }

    public void SetLayers(List<PaintLayer> src)
    {
        if (src != null && src.Count > 0) layers = src;
    }
    public void SetColorBlend(float f)  => colorBlendFactor  = Mathf.Clamp01(f);
    public void SetViscosity(float v)   => viscosity         = v;
    public void SetFlowRate(float rate) => flowRateMultiplier = rate;
}
