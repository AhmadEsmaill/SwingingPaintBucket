using UnityEngine;

public class CanvasPainter : MonoBehaviour
{
    [Header("Canvas Settings")]
    public int textureWidth = 1024;
    public int textureHeight = 1024;
    public float canvasWorldWidth = 4f;
    public float canvasWorldHeight = 4f;

    [Header("Surface Type")]
    public SurfaceType surfaceType = SurfaceType.Canvas;
    public enum SurfaceType { Canvas, Metal, Paper, Wood }

    private Texture2D paintTexture;
    private Renderer  canvasRenderer;

    // Stroke continuity — connects consecutive close hits into smooth brush strokes
    private Vector3 lastPaintWorld;
    private float   lastPaintTime   = -1f;
    private const float maxStrokeGap     = 0.07f;  // world-space max gap to bridge (m)
    private const float strokeTimeWindow = 0.12f;  // max seconds between linked hits

    // Batched Apply: collect all SetPixel calls in a frame, apply once in LateUpdate
    private bool pendingApply;

    void Start()
    {
        canvasRenderer = GetComponent<Renderer>();
        InitTexture();
    }

    void LateUpdate()
    {
        if (pendingApply)
        {
            paintTexture.Apply();
            pendingApply = false;
        }
    }

    private void InitTexture()
    {
        paintTexture = new Texture2D(textureWidth, textureHeight, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[textureWidth * textureHeight];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.white;
        paintTexture.SetPixels(pixels);
        paintTexture.Apply();
        canvasRenderer.material.mainTexture = paintTexture;
    }

    public void PaintAt(Vector3 worldPosition, Color color, float dropletRadius, Vector3 impactVelocity)
    {
        Vector2 uv = WorldToUV(worldPosition);
        if (uv.x < 0 || uv.x > 1 || uv.y < 0 || uv.y > 1)
        {
            lastPaintTime = -1f;
            return;
        }

        float pixelsPerMeter = textureWidth / canvasWorldWidth;
        int   cx    = Mathf.RoundToInt(uv.x * textureWidth);
        int   cy    = Mathf.RoundToInt(uv.y * textureHeight);
        int   baseR = Mathf.Max(1, Mathf.RoundToInt(dropletRadius * pixelsPerMeter * 2f));

        float hx     = impactVelocity.x;
        float hz     = impactVelocity.z;
        float hSpeed = Mathf.Sqrt(hx * hx + hz * hz);
        float vSpeed = Mathf.Abs(impactVelocity.y) + 0.01f;
        float elongation = Mathf.Clamp(1f + (hSpeed / vSpeed) * 0.8f, 1f, 2.5f);
        int   ra    = Mathf.RoundToInt(baseR * elongation);
        int   rb    = baseR;
        float angle = hSpeed > 0.05f ? Mathf.Atan2(hz, hx) : 0f;
        float absorption = GetAbsorption();

        // ── Stroke interpolation ────────────────────────────────────────────
        // If the previous hit was close in both space and time, fill the gap
        // between the two positions so the paint forms a continuous stroke.
        float now = Time.time;
        if (lastPaintTime > 0f && (now - lastPaintTime) < strokeTimeWindow)
        {
            float worldDist = Vector3.Distance(worldPosition, lastPaintWorld);
            if (worldDist < maxStrokeGap)
            {
                Vector2 prevUV = WorldToUV(lastPaintWorld);
                float   prevCxF = prevUV.x * textureWidth;
                float   prevCyF = prevUV.y * textureHeight;
                float   pixDist = Vector2.Distance(new Vector2(prevCxF, prevCyF),
                                                    new Vector2(cx, cy));
                int steps = Mathf.Max(1, Mathf.RoundToInt(pixDist / Mathf.Max(1, baseR * 0.5f)));
                float strokeAngle = Mathf.Atan2(cy - prevCyF, cx - prevCxF);
                int   srA = Mathf.Max(1, Mathf.RoundToInt(ra * 0.85f));
                int   srB = Mathf.Max(1, Mathf.RoundToInt(rb * 0.85f));
                for (int s = 1; s < steps; s++)
                {
                    float t  = (float)s / steps;
                    int   ix = Mathf.RoundToInt(Mathf.Lerp(prevCxF, cx, t));
                    int   iy = Mathf.RoundToInt(Mathf.Lerp(prevCyF, cy, t));
                    PaintEllipse(ix, iy, srA, srB, strokeAngle, color, absorption * 0.65f);
                }
            }
        }

        // ── Main splat ──────────────────────────────────────────────────────
        PaintEllipse(cx, cy, ra, rb, angle, color, absorption);

        // Micro-splatters only at genuinely high impact speeds
        float impactSpeed = impactVelocity.magnitude;
        if (impactSpeed > 5f && rb > 2)
        {
            int splatterCount = Mathf.Min(4, Mathf.RoundToInt((impactSpeed - 5f) * 1.5f));
            for (int i = 0; i < splatterCount; i++)
            {
                float dist  = Random.Range(ra * 1.5f, ra * 2.5f);
                float sAngl = Random.Range(0f, Mathf.PI * 2f);
                int   sx    = Mathf.Clamp(cx + Mathf.RoundToInt(dist * Mathf.Cos(sAngl)), 0, textureWidth  - 1);
                int   sy    = Mathf.Clamp(cy + Mathf.RoundToInt(dist * Mathf.Sin(sAngl)), 0, textureHeight - 1);
                PaintEllipse(sx, sy, Mathf.Max(1, rb / 4), Mathf.Max(1, rb / 4), 0f, color, absorption * 0.4f);
            }
        }

        // Mark texture dirty — Apply() called once per frame in LateUpdate
        pendingApply    = true;
        lastPaintWorld  = worldPosition;
        lastPaintTime   = now;
    }

    private void PaintEllipse(int cx, int cy, int ra, int rb, float angle, Color color, float absorption)
    {
        float cosA = Mathf.Cos(angle);
        float sinA = Mathf.Sin(angle);
        int   maxR = Mathf.Max(ra, rb);

        for (int dx = -maxR; dx <= maxR; dx++)
        for (int dy = -maxR; dy <= maxR; dy++)
        {
            int px = cx + dx, py = cy + dy;
            if (px < 0 || px >= textureWidth || py < 0 || py >= textureHeight) continue;

            float ex =  dx * cosA + dy * sinA;
            float ey = -dx * sinA + dy * cosA;
            float d  = (ex * ex) / (float)(ra * ra) + (ey * ey) / (float)(rb * rb);
            if (d > 1f) continue;

            float alpha   = (1f - Mathf.Sqrt(d)) * absorption;
            Color existing = paintTexture.GetPixel(px, py);
            paintTexture.SetPixel(px, py, Color.Lerp(existing, color, alpha));
        }
    }

    private float GetAbsorption()
    {
        switch (surfaceType)
        {
            case SurfaceType.Canvas: return 0.45f;
            case SurfaceType.Metal:  return 0.25f;
            case SurfaceType.Paper:  return 0.55f;
            case SurfaceType.Wood:   return 0.35f;
            default: return 0.40f;
        }
    }

    private Vector2 WorldToUV(Vector3 worldPos)
    {
        Vector3 local = transform.InverseTransformPoint(worldPos);
        return new Vector2(local.x + 0.5f, local.y + 0.5f);
    }

    public void SaveCanvas(string path)
    {
        byte[] bytes = paintTexture.EncodeToPNG();
        System.IO.File.WriteAllBytes(path, bytes);
    }

    public void ClearCanvas()
    {
        lastPaintTime = -1f;
        pendingApply  = false;
        InitTexture();
    }
}
