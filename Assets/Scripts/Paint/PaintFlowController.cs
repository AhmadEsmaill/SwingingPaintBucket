using UnityEngine;
using System.Collections.Generic;

public class PaintFlowController : MonoBehaviour
{
    [Header("References")]
    public PendulumSimulator pendulum;
    public CanvasPainter canvasPainter;
    public GameObject dropletPrefab;

    [Header("Paint Properties")]
    public Color paintColor = Color.red;
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
            if (spawnTimer < spawnInterval) return;
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

        droplet.Launch(pos, vel, paintColor, radius, canvasPainter);
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

        droplet.Launch(pendulum.BucketCenter, vel, paintColor, dropletRadius, canvasPainter);
    }

    public void SetColor(Color c) => paintColor = c;
    public void SetViscosity(float v) => viscosity = v;
    public void SetFlowRate(float rate) => flowRateMultiplier = rate;
}



