using UnityEngine;

// Bottom-right inset that visualises the SPH paint sloshing INSIDE the bucket.
// The fluid is drawn in the bucket's own (upright) frame, so the swinging shows
// up as the liquid surface tilting and sloshing against the walls — plus a small
// arrow pointing along real gravity to make the cause of the slosh obvious.
public class BucketFluidInset : MonoBehaviour
{
    [Header("References")]
    public SPHSimulator     sph;
    public PendulumSimulator pendulum;

    [Header("Appearance")]
    public Color fluidColorSlow   = new Color(0.12f, 0.40f, 0.95f); // calm paint
    public Color fluidColorFast   = new Color(0.55f, 0.90f, 1.00f); // fast-moving paint
    public Color fluidColorDeep   = new Color(0.05f, 0.16f, 0.45f); // shaded bottom (volume)
    public Color surfaceHighlight = new Color(0.90f, 0.98f, 1.00f); // bright free-surface band
    public float maxSpeed         = 2.5f;                           // m/s → full "fast" colour

    [Header("Fluid look (metaball fill)")]
    public float splatRadius      = 3.0f;   // px — each particle's influence radius
    public float surfaceThreshold = 1.0f;   // coverage needed to count as liquid body

    [Header("Panel (screen pixels)")]
    // Hidden for now; flip to true to bring the inset back.
    public bool showPanel  = false;
    public int panelWidth  = 190;
    public int panelHeight = 250;
    public int margin      = 14;

    // Off-screen render target for the fluid
    private const int TexW = 150, TexH = 200;
    private Texture2D  tex;
    private Color32[]  pixels;
    private Vector3[]  buffer;
    private float[]    cover;      // per-pixel accumulated fluid density
    private float[]    speedAcc;   // per-pixel coverage-weighted speed sum

    // Interior drawing bounds inside the texture (leave room for walls)
    private int X0, X1, Y0, Y1;      // Y0 = bottom (hole), Y1 = top rim
    private static readonly Color32 BgColor   = new Color32(20, 20, 26, 255);
    private static readonly Color32 WallColor = new Color32(140, 120, 90, 255);
    private static readonly Color32 ArrowColor = new Color32(255, 230, 120, 255);

    private GUIStyle titleStyle;
    private Texture2D boxTex;

    void Start()
    {
        tex = new Texture2D(TexW, TexH, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        pixels   = new Color32[TexW * TexH];
        cover    = new float[TexW * TexH];
        speedAcc = new float[TexW * TexH];

        X0 = 12; X1 = TexW - 12;
        Y0 = 12; Y1 = TexH - 18;

        boxTex = new Texture2D(1, 1);
        boxTex.SetPixel(0, 0, new Color(0.06f, 0.06f, 0.08f, 0.9f));
        boxTex.Apply();
    }

    void Update()
    {
        if (!showPanel) return;
        if (sph == null || tex == null || !sph.HasParticleData) return;
        RenderFluid();
    }

    void RenderFluid()
    {
        // ── Clear ───────────────────────────────────────────────────────────
        for (int i = 0; i < pixels.Length; i++) pixels[i] = BgColor;
        System.Array.Clear(cover,    0, cover.Length);
        System.Array.Clear(speedAcc, 0, speedAcc.Length);

        int cx = (X0 + X1) / 2;
        int iw = X1 - X0;

        // ── Splat particles into a soft density field (metaballs) ───────────
        int need = sph.ParticleCount;
        if (buffer == null || buffer.Length < need) buffer = new Vector3[Mathf.Max(need, 1)];
        int count = sph.CopyLocalParticles(buffer);

        int   R    = Mathf.Max(1, Mathf.CeilToInt(splatRadius));
        float invR = 1f / splatRadius;
        for (int i = 0; i < count; i++)
        {
            Vector3 p = buffer[i];
            LocalToPixel(p.x, p.y, out int px, out int py);
            float spd = p.z;

            for (int dy = -R; dy <= R; dy++)
            for (int dx = -R; dx <= R; dx++)
            {
                int x = px + dx, y = py + dy;
                if (x < X0 || x >= X1 || y < Y0 || y > Y1) continue;
                float d = Mathf.Sqrt(dx * dx + dy * dy) * invR;
                if (d >= 1f) continue;
                float w   = 1f - d;                 // linear falloff
                int   idx = y * TexW + x;
                cover[idx]    += w;
                speedAcc[idx] += w * spd;
            }
        }

        // ── Resolve the density field into a filled liquid body ─────────────
        // In the image, higher y = toward the rim (up), lower y = toward the hole
        // (down/gravity). A fluid pixel whose neighbour toward the rim is empty is
        // a FREE-SURFACE pixel → gets a bright glint. Depth below the surface is
        // shaded darker to give the liquid volume.
        float invH = 1f / Mathf.Max(1, Y1 - Y0);
        for (int y = Y0; y <= Y1; y++)
        for (int x = X0; x < X1; x++)
        {
            int   idx = y * TexW + x;
            if (cover[idx] < surfaceThreshold) continue;

            float avgSpeed = speedAcc[idx] / cover[idx];
            // Base tint by speed, then darken with depth (distance below the rim).
            float depth01 = Mathf.Clamp01((Y1 - y) * invH);          // 0 at rim, 1 at hole
            Color body    = Color.Lerp(fluidColorSlow, fluidColorFast,
                                       Mathf.Clamp01(avgSpeed / maxSpeed));
            body          = Color.Lerp(body, fluidColorDeep, depth01 * 0.55f);

            // Free-surface detection: is the pixel one step toward the rim empty?
            bool atSurface = (y + 1 > Y1) || cover[(y + 1) * TexW + x] < surfaceThreshold;
            if (atSurface)
                body = Color.Lerp(body, surfaceHighlight, 0.6f);     // meniscus glint

            pixels[idx] = body;
        }

        // ── Bucket walls (upright): sides + bottom with a centre hole gap ────
        DrawVLine(X0, Y0, Y1, WallColor);
        DrawVLine(X1 - 1, Y0, Y1, WallColor);
        float holeFrac = sph.bucketWidth > 1e-4f
            ? Mathf.Clamp01(sph.holeWidth / sph.bucketWidth) : 0.3f;
        int holeHalf = Mathf.RoundToInt(iw * holeFrac * 0.5f);
        for (int x = X0; x < X1; x++)
            if (Mathf.Abs(x - cx) > holeHalf) SetPx(x, Y0, WallColor);

        // ── Gravity direction arrow (world-down projected into bucket frame) ─
        if (pendulum != null)
        {
            Vector3 rd = pendulum.RopeDirection;
            Vector2 aY = new Vector2(rd.x, rd.y);
            if (aY.sqrMagnitude > 1e-6f)
            {
                aY.Normalize();
                Vector2 aX = new Vector2(-aY.y, aY.x);
                Vector2 down = new Vector2(0f, -1f);
                float gx = Vector2.Dot(down, aX);
                float gy = Vector2.Dot(down, aY);
                LocalToPixel(0f, 0f, out int ox, out int oy);
                LocalToPixel(gx * 0.55f, gy * 0.55f, out int ex, out int ey);
                DrawLine(ox, oy, ex, ey, ArrowColor);
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply(false);
    }

    // Maps bucket-local coords (x: −1..+1 left→right, y: −1 top .. +1 bottom/hole)
    // to texture pixels. Local +y (toward the hole) maps to the bottom of the image.
    private void LocalToPixel(float lx, float ly, out int px, out int py)
    {
        float u = Mathf.Clamp01((lx + 1f) * 0.5f);
        float v = Mathf.Clamp01((ly + 1f) * 0.5f);   // 0 = top rim, 1 = bottom/hole
        px = Mathf.RoundToInt(X0 + u * (X1 - X0));
        py = Mathf.RoundToInt(Y1 - v * (Y1 - Y0));    // bottom/hole → low pixel row
    }

    private void SetPx(int x, int y, Color32 col)
    {
        if (x < 0 || x >= TexW || y < 0 || y >= TexH) return;
        pixels[y * TexW + x] = col;
    }

    private void DrawVLine(int x, int y0, int y1, Color32 col)
    {
        for (int y = y0; y <= y1; y++) SetPx(x, y, col);
    }

    private void DrawLine(int x0, int y0, int x1, int y1, Color32 col)
    {
        int dx = Mathf.Abs(x1 - x0), dy = -Mathf.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;
        while (true)
        {
            SetPx(x0, y0, col);
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    void OnGUI()
    {
        if (!showPanel || tex == null) return;

        if (titleStyle == null)
            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize  = 12,
                fontStyle = FontStyle.Bold,
                normal    = { textColor = new Color(0.9f, 0.9f, 0.95f) }
            };

        int pw = panelWidth, ph = panelHeight;
        int px = Screen.width  - pw - margin;
        int py = Screen.height - ph - margin;

        GUI.DrawTexture(new Rect(px, py, pw, ph), boxTex, ScaleMode.StretchToFill);
        GUI.Label(new Rect(px + 8, py + 5, pw - 16, 18), "Paint inside bucket", titleStyle);
        GUI.DrawTexture(new Rect(px + 8, py + 26, pw - 16, ph - 34), tex, ScaleMode.ScaleToFit);
    }
}
