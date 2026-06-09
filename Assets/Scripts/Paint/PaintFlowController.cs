using UnityEngine;
using System.Collections.Generic;

public class PaintFlowController : MonoBehaviour
{
    [Header("References")]
    public PendulumSimulator pendulum;
    public CanvasPainter canvasPainter;
    public GameObject dropletPrefab;

    [Header("Paint Properties")]
    public Color paintColor  = Color.red;
    public Color paintColorB = Color.blue;
    // 0 = pure colorA, 0.25/0.5/0.75 = increasing mix toward colorB
    public float colorBlendFactor = 0f;
    public float viscosity = 0.01f;         // η - Pa·s (water ~0.001, thick paint ~0.1)
    public float holeRadius = 0.005f;       // r - radius of exit hole in meters
    public float flowRateMultiplier = 1f;

    [Header("SPH (assign to enable SPH mode; leave empty for Torricelli fallback)")]
    public SPHSimulator sphSimulator;

    [Header("Pool Settings")]
    public int poolSize = 100;

    private List<PaintDroplet> dropletPool = new List<PaintDroplet>();
    private float spawnTimer;
    private float spawnInterval = 0.05f;

    void Start()
    {
        BuildPool();
    }

    // Object pooling: pre-create droplets to avoid runtime instantiation cost
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
        if (pendulum == null) return;

        if (sphSimulator != null)
        {
            // SPH mode: each particle that exits the bucket hole becomes a droplet
            foreach (SPHParticle p in sphSimulator.DrainExitBuffer())
                SpawnFromSPHParticle(p);
        }
        else
        {
            // Torricelli fallback: v_out = sqrt(2gh) * f(viscosity)
            if (!pendulum.IsSimulating || pendulum.PaintMass <= 0f) return;
            spawnTimer += Time.fixedDeltaTime;
            // Spawn interval scales inversely with hole area: larger hole → faster drips
            float dynamicInterval = spawnInterval * Mathf.Pow(0.005f / Mathf.Max(holeRadius, 0.001f), 2f);
            if (spawnTimer < dynamicInterval) return;
            spawnTimer = 0f;
            SpawnDropletTorricelli();
        }
    }

    private void SpawnFromSPHParticle(SPHParticle particle)
    {
        PaintDroplet droplet = GetFromPool();
        if (droplet == null) return;

        Vector3 pos = new Vector3(particle.position.x, particle.position.y, pendulum.BucketCenter.z);
        Vector3 vel = new Vector3(
            particle.velocity.x,
            particle.velocity.y,
            pendulum.BucketVelocity.z * 0.8f);
        float radius = Mathf.Lerp(0.01f, 0.04f, Mathf.Clamp01(viscosity * 10f));

        droplet.Launch(pos, vel, MixedColor(), radius, canvasPainter);
    }

    private void SpawnDropletTorricelli()
    {
        PaintDroplet droplet = GetFromPool();
        if (droplet == null) return;

        float fillRatio       = pendulum.PaintFillRatio;
        float h               = fillRatio * 0.2f;
        float viscosityFactor = 1f / (1f + viscosity * 100f);
        float exitSpeed       = Mathf.Sqrt(2f * pendulum.gravity * h) * viscosityFactor * flowRateMultiplier;

        if (exitSpeed < 0.01f) return;

        Vector3 bucketVel = pendulum.BucketVelocity;
        Vector3 vel = new Vector3(bucketVel.x * 0.8f, -exitSpeed, bucketVel.z * 0.8f);
        float dropletRadius = Mathf.Lerp(0.01f, 0.04f, Mathf.Clamp01(viscosity * 10f));

        droplet.Launch(pendulum.BucketCenter, vel, MixedColor(), dropletRadius, canvasPainter);
    }

    // blend%  of droplets → resultant (A+B mixed equally)
    // rest    of droplets → pure A or pure B as-is
    // single colour (A ≈ B) → blend has no effect
    private Color MixedColor()
    {
        // No blend requested → pure A
        if (colorBlendFactor <= 0f) return paintColor;

        // Only one colour set → blend has no effect
        float diff = Mathf.Abs(paintColor.r - paintColorB.r)
                   + Mathf.Abs(paintColor.g - paintColorB.g)
                   + Mathf.Abs(paintColor.b - paintColorB.b);
        if (diff < 0.05f) return paintColor;

        // The resultant: equal mix of all user-chosen colours
        Color resultant = Color.Lerp(paintColor, paintColorB, 0.5f);

        // 100% blend → all droplets are the resultant
        if (colorBlendFactor >= 1f) return resultant;

        // blend% chance → resultant, remaining% → pure colour (A or B randomly)
        return Random.value < colorBlendFactor
            ? resultant
            : (Random.value < 0.5f ? paintColor : paintColorB);
    }

    public void SetColor(Color c)  => paintColor  = c;
    public void SetColorB(Color c) => paintColorB = c;
    public void SetColorBlend(float f) => colorBlendFactor = Mathf.Clamp01(f);
    public void SetViscosity(float v) => viscosity = v;
    public void SetFlowRate(float rate) => flowRateMultiplier = rate;
}



