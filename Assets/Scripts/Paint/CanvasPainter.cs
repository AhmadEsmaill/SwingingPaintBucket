using UnityEngine;

public class CanvasPainter : MonoBehaviour
{
    [Header("Canvas Settings")]
    public int   textureWidth      = 1024;
    public int   textureHeight     = 1024;
    public float canvasWorldWidth  = 4f;
    public float canvasWorldHeight = 4f;

    [Header("Surface Type")]
    public SurfaceType surfaceType = SurfaceType.Canvas;

    // Auto-computed from surface type — matches original per-surface absorption values.
    // Not set manually; updated whenever surfaceType changes.
    [HideInInspector] public float blendFactor = 0.55f;

    public enum SurfaceType { Canvas, Metal, Paper, Wood }

    // Per-surface properties ─────────────────────────────────────────────────
    // absorption : how opaque each new layer is (higher = paint sticks more)
    // spotScale  : multiplier on the base spot radius
    // background : the surface colour before any paint

    static readonly float[] Absorption  = { 0.45f, 0.18f, 0.72f, 0.33f };
    static readonly float[] SpotScale   = { 1.00f, 0.60f, 1.45f, 1.00f };
    static readonly Color[] Background  =
    {
        new Color(1.00f, 1.00f, 1.00f),          // Canvas  — white
        new Color(0.52f, 0.54f, 0.56f),          // Metal   — steel grey
        new Color(0.98f, 0.95f, 0.88f),          // Paper   — warm cream
        new Color(0.68f, 0.52f, 0.33f),          // Wood    — warm brown
    };

    private Texture2D  paintTexture;
    private Renderer   canvasRenderer;

    // Drop-based stroke continuity (used by PaintAt)
    private Vector3 lastPaintWorld;
    private float   lastPaintTime   = -1f;
    private const float maxStrokeGap     = 0.22f;
    private const float strokeTimeWindow = 0.28f;

    private bool pendingApply;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    void Start()
    {
        canvasRenderer = GetComponent<Renderer>();

        // Display the painted texture exactly as authored: an unlit shader so scene
        // lighting/ambient can't tint the surface colour away from the real paint
        // colour (the droplet in the air is already unlit — this makes them match).
        if (canvasRenderer != null)
        {
            Shader unlit = Shader.Find("Unlit/Texture");
            if (unlit != null) canvasRenderer.material.shader = unlit;
        }

        SyncBlendFromSurface();
        InitTexture();
    }

    void LateUpdate()
    {
        if (pendingApply) { paintTexture.Apply(); pendingApply = false; }
    }

    private void InitTexture()
    {
        paintTexture = new Texture2D(textureWidth, textureHeight, TextureFormat.RGBA32, false);
        Color bg     = Background[(int)surfaceType];
        Color[] px   = new Color[textureWidth * textureHeight];
        for (int i = 0; i < px.Length; i++) px[i] = bg;
        paintTexture.SetPixels(px);
        paintTexture.Apply();
        if (canvasRenderer != null)
            canvasRenderer.material.mainTexture = paintTexture;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    // Change surface type; clears canvas so the background colour takes effect.
    public void SetSurfaceType(SurfaceType type)
    {
        surfaceType   = type;
        lastPaintTime = -1f;
        SyncBlendFromSurface();
        InitTexture();
    }

    // blendFactor = 1 - absorption so that paintOpacity = absorption (original behaviour)
    private void SyncBlendFromSurface()
    {
        blendFactor = 1f - Absorption[(int)surfaceType];
    }

    public void PaintAt(Vector3 worldPosition, Color color, float dropletRadius,
                        Vector3 impactVelocity)
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

        // Surface-specific spot size — multiplier raised so drops are visible on 8 m canvas
        int   baseR = Mathf.Max(4,
                Mathf.RoundToInt(dropletRadius * pixelsPerMeter * 6f
                                 * SpotScale[(int)surfaceType]));

        float hx     = impactVelocity.x;
        float hz     = impactVelocity.z;
        float hSpeed = Mathf.Sqrt(hx * hx + hz * hz);
        float vSpeed = Mathf.Abs(impactVelocity.y) + 0.01f;

        // Wood adds a subtle grain elongation along the X axis
        float grainBoost = (surfaceType == SurfaceType.Wood) ? 1.25f : 1f;
        float elongation = Mathf.Clamp(1f + (hSpeed / vSpeed) * 0.8f, 1f, 2.5f) * grainBoost;

        int   ra    = Mathf.RoundToInt(baseR * elongation);
        int   rb    = baseR;
        float angle = (surfaceType == SurfaceType.Wood)
                      ? 0f                              // grain always horizontal
                      : (hSpeed > 0.05f ? Mathf.Atan2(hz, hx) : 0f);

        // Core opacity of a fresh splat. Kept high so the mark reads as the actual
        // paint colour instead of a washed-out tint of the surface; the surface's
        // absorption only nudges it (absorbent paper looks a touch more solid than
        // beading metal).
        float paintOpacity = Mathf.Lerp(0.80f, 0.97f, Absorption[(int)surfaceType]);

        // ── Stroke interpolation ────────────────────────────────────────────
        float now = Time.time;
        if (lastPaintTime > 0f && (now - lastPaintTime) < strokeTimeWindow)
        {
            float worldDist = Vector3.Distance(worldPosition, lastPaintWorld);
            if (worldDist < maxStrokeGap)
            {
                Vector2 prevUV  = WorldToUV(lastPaintWorld);
                float   prevCxF = prevUV.x * textureWidth;
                float   prevCyF = prevUV.y * textureHeight;
                float   pixDist = Vector2.Distance(new Vector2(prevCxF, prevCyF),
                                                    new Vector2(cx, cy));
                int     steps   = Mathf.Max(1,
                    Mathf.RoundToInt(pixDist / Mathf.Max(1, baseR * 0.25f)));
                float strokeAngle = Mathf.Atan2(cy - prevCyF, cx - prevCxF);
                int   srA = Mathf.Max(1, Mathf.RoundToInt(ra * 0.90f));
                int   srB = Mathf.Max(1, Mathf.RoundToInt(rb * 0.90f));
                for (int s = 1; s < steps; s++)
                {
                    float t  = (float)s / steps;
                    int   ix = Mathf.RoundToInt(Mathf.Lerp(prevCxF, cx, t));
                    int   iy = Mathf.RoundToInt(Mathf.Lerp(prevCyF, cy, t));
                    PaintEllipse(ix, iy, srA, srB, strokeAngle, color, paintOpacity * 0.65f);
                }
            }
        }

        // ── Main splat ──────────────────────────────────────────────────────
        PaintEllipse(cx, cy, ra, rb, angle, color, paintOpacity);

        // Metal: paint beads — add tiny raised satellite dots nearby
        if (surfaceType == SurfaceType.Metal && rb > 1)
        {
            int beads = Random.Range(2, 5);
            for (int i = 0; i < beads; i++)
            {
                float d    = Random.Range(ra * 1.2f, ra * 2.0f);
                float a    = Random.Range(0f, Mathf.PI * 2f);
                int   bx   = Mathf.Clamp(cx + Mathf.RoundToInt(d * Mathf.Cos(a)), 0, textureWidth  - 1);
                int   by   = Mathf.Clamp(cy + Mathf.RoundToInt(d * Mathf.Sin(a)), 0, textureHeight - 1);
                int   br   = Mathf.Max(1, rb / 3);
                PaintEllipse(bx, by, br, br, 0f, color, paintOpacity * 0.7f);
            }
        }

        // High-speed micro-splatters (all surfaces except Metal where paint beads)
        float impactSpeed = impactVelocity.magnitude;
        if (impactSpeed > 5f && rb > 2 && surfaceType != SurfaceType.Metal)
        {
            int splats = Mathf.Min(4, Mathf.RoundToInt((impactSpeed - 5f) * 1.5f));
            for (int i = 0; i < splats; i++)
            {
                float dist  = Random.Range(ra * 1.5f, ra * 2.5f);
                float sAngl = Random.Range(0f, Mathf.PI * 2f);
                int   sx    = Mathf.Clamp(cx + Mathf.RoundToInt(dist * Mathf.Cos(sAngl)), 0, textureWidth  - 1);
                int   sy    = Mathf.Clamp(cy + Mathf.RoundToInt(dist * Mathf.Sin(sAngl)), 0, textureHeight - 1);
                PaintEllipse(sx, sy, Mathf.Max(1, rb / 4), Mathf.Max(1, rb / 4), 0f, color,
                             paintOpacity * 0.4f);
            }
        }

        pendingApply   = true;
        lastPaintWorld = worldPosition;
        lastPaintTime  = now;
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    private void PaintEllipse(int cx, int cy, int ra, int rb, float angle,
                               Color color, float opacity)
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

            // Solid core out to ~45% of the radius, then a soft fade to the rim —
            // keeps the centre true to the paint colour while edges stay feathered.
            float t     = Mathf.Sqrt(d);                        // 0 = centre, 1 = rim
            float alpha = (1f - Mathf.SmoothStep(0.45f, 1f, t)) * opacity;
            Color existing = paintTexture.GetPixel(px, py);
            paintTexture.SetPixel(px, py, Color.Lerp(existing, color, alpha));
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
