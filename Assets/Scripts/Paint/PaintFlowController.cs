using UnityEngine;
using System.Collections.Generic;

// Generates paint droplets from the bucket using Torricelli's law
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

    [Header("Pool Settings")]
    public int poolSize = 100;

    private List<PaintDroplet> dropletPool = new List<PaintDroplet>();
    private float spawnTimer;
    private float spawnInterval = 0.05f;    // seconds between droplet spawns

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
            PaintDroplet d = obj.GetComponent<PaintDroplet>();
            dropletPool.Add(d);
        }
    }

    private PaintDroplet GetFromPool()
    {
        foreach (var d in dropletPool)
        {
            if (!d.gameObject.activeSelf)
            {
                d.gameObject.SetActive(true);
                return d;
            }
        }
        return null; // Pool exhausted
    }

    void FixedUpdate()
    {
        if (pendulum == null || pendulum.PaintMass <= 0f) return;

        spawnTimer += Time.fixedDeltaTime;
        if (spawnTimer < spawnInterval) return;
        spawnTimer = 0f;

        SpawnDroplet();
    }

    private void SpawnDroplet()
    {
        PaintDroplet droplet = GetFromPool();
        if (droplet == null) return;

        // Torricelli (modified): v_out = sqrt(2gh) * f(viscosity)
        // h = paint fill height inside bucket (approximated from fill ratio)
        float fillRatio = pendulum.PaintFillRatio;
        float h = fillRatio * 0.2f;         // max 20cm fill height
        float viscosityFactor = 1f / (1f + viscosity * 100f);
        float exitSpeed = Mathf.Sqrt(2f * pendulum.gravity * h) * viscosityFactor * flowRateMultiplier;

        if (exitSpeed < 0.01f) return;

        // Droplet inherits bucket's horizontal velocity + exit velocity downward
        Vector3 bucketVel = pendulum.BucketVelocity;
        Vector3 dropletVelocity = new Vector3(bucketVel.x * 0.8f, -exitSpeed, bucketVel.z * 0.8f);

        // Droplet size depends on viscosity: high viscosity = bigger drops
        float dropletRadius = Mathf.Lerp(0.01f, 0.04f, Mathf.Clamp01(viscosity * 10f));

        droplet.Launch(
            pendulum.BucketPosition,
            dropletVelocity,
            paintColor,
            dropletRadius,
            canvasPainter);
    }

    public void SetColor(Color c) => paintColor = c;
    public void SetViscosity(float v) => viscosity = v;
    public void SetFlowRate(float rate) => flowRateMultiplier = rate;
}
