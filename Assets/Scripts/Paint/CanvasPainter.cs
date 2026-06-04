using UnityEngine;

// Draws paint spots on the canvas using Texture2D pixel manipulation
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
    private Renderer canvasRenderer;

    void Start()
    {
        canvasRenderer = GetComponent<Renderer>();
        InitTexture();
    }

    private void InitTexture()
    {
        paintTexture = new Texture2D(textureWidth, textureHeight, TextureFormat.RGBA32, false);

        // Fill white background
        Color[] pixels = new Color[textureWidth * textureHeight];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = Color.white;

        paintTexture.SetPixels(pixels);
        paintTexture.Apply();

        canvasRenderer.material.mainTexture = paintTexture;
    }

    // Called by each droplet when it hits the canvas
    public void PaintAt(Vector3 worldPosition, Color color, float dropletRadius, float impactSpeed)
    {
        // Convert world position to texture UV coordinates
        Vector2 uv = WorldToUV(worldPosition);
        if (uv.x < 0 || uv.x > 1 || uv.y < 0 || uv.y > 1) return;

        int centerX = Mathf.RoundToInt(uv.x * textureWidth);
        int centerY = Mathf.RoundToInt(uv.y * textureHeight);

        // Spread radius depends on impact speed and surface type
        float spreadFactor = GetSpreadFactor(impactSpeed);
        int pixelRadius = Mathf.RoundToInt((dropletRadius / canvasWorldWidth) * textureWidth * spreadFactor);
        pixelRadius = Mathf.Max(2, pixelRadius);

        // Paint a circular spot
        PaintCircle(centerX, centerY, pixelRadius, color);

        paintTexture.Apply();
    }

    private void PaintCircle(int cx, int cy, int radius, Color color)
    {
        float absorption = GetAbsorption();

        for (int x = cx - radius; x <= cx + radius; x++)
        {
            for (int y = cy - radius; y <= cy + radius; y++)
            {
                if (x < 0 || x >= textureWidth || y < 0 || y >= textureHeight) continue;

                float dist = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                if (dist > radius) continue;

                // Soft edge: opacity decreases near the border
                float alpha = (1f - dist / radius) * absorption;
                Color existing = paintTexture.GetPixel(x, y);
                paintTexture.SetPixel(x, y, Color.Lerp(existing, color, alpha));
            }
        }
    }

    // Surface absorption affects how paint spreads and blends
    private float GetAbsorption()
    {
        switch (surfaceType)
        {
            case SurfaceType.Canvas: return 0.85f;   // high absorption, irregular spread
            case SurfaceType.Metal:  return 0.4f;    // low absorption, sharp edges
            case SurfaceType.Paper:  return 0.9f;    // very high absorption
            case SurfaceType.Wood:   return 0.65f;
            default: return 0.75f;
        }
    }

    private float GetSpreadFactor(float impactSpeed)
    {
        // Faster impact = larger spread
        float base_ = surfaceType == SurfaceType.Metal ? 0.8f : 1.2f;
        return base_ + impactSpeed * 0.05f;
    }

    private Vector2 WorldToUV(Vector3 worldPos)
    {
        Vector3 local = transform.InverseTransformPoint(worldPos);
        float u = (local.x / canvasWorldWidth) + 0.5f;
        float v = (local.z / canvasWorldHeight) + 0.5f;
        return new Vector2(u, v);
    }

    // Save canvas as PNG
    public void SaveCanvas(string path)
    {
        byte[] bytes = paintTexture.EncodeToPNG();
        System.IO.File.WriteAllBytes(path, bytes);
        Debug.Log("Canvas saved to: " + path);
    }

    // Clear the canvas
    public void ClearCanvas()
    {
        InitTexture();
    }
}
