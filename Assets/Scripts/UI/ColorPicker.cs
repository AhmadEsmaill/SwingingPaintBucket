using UnityEngine;

// Runtime IMGUI RGB colour picker: a saturation/value square plus a hue bar.
// Unity's IMGUI ships no colour picker, so we roll a compact one here. Click or
// drag inside either region to choose. Draw() returns the (possibly) updated
// colour — call it every OnGUI and assign the result back.
public static class ColorPicker
{
    private static Texture2D _hueTex;      // horizontal rainbow bar
    private static Texture2D _svTex;       // saturation × value square
    private static float     _svHue = -1f; // hue the SV square was baked for
    private static Texture2D _white;

    private const int SV_W = 120, SV_H = 96, HUE_W = 120;

    private static Texture2D White
    {
        get
        {
            if (_white == null)
            {
                _white = new Texture2D(1, 1);
                _white.SetPixel(0, 0, Color.white);
                _white.Apply();
            }
            return _white;
        }
    }

    public static Color Draw(Color color, float width)
    {
        Color.RGBToHSV(color, out float h, out float s, out float v);

        // ── Saturation / Value square ──────────────────────────────
        Rect sv = GUILayoutUtility.GetRect(width, width * (SV_H / (float)SV_W));
        EnsureSvTex(h);
        GUI.DrawTexture(sv, _svTex, ScaleMode.StretchToFill, false);

        float mx = sv.x + s * sv.width;
        float my = sv.y + (1f - v) * sv.height;
        DrawMarker(mx, my, v > 0.5f && s < 0.5f ? Color.black : Color.white);

        // ── Hue bar ────────────────────────────────────────────────
        GUILayout.Space(4);
        Rect hue = GUILayoutUtility.GetRect(width, 16);
        EnsureHueTex();
        GUI.DrawTexture(hue, _hueTex, ScaleMode.StretchToFill, false);

        float hx = hue.x + h * hue.width;
        GUI.color = Color.white;
        GUI.DrawTexture(new Rect(hx - 1, hue.y - 1, 2, hue.height + 2), White);

        // ── Input ──────────────────────────────────────────────────
        Event e = Event.current;
        if (e.type == EventType.MouseDown || e.type == EventType.MouseDrag)
        {
            if (sv.Contains(e.mousePosition))
            {
                s = Mathf.Clamp01((e.mousePosition.x - sv.x) / sv.width);
                v = 1f - Mathf.Clamp01((e.mousePosition.y - sv.y) / sv.height);
                e.Use();
            }
            else if (hue.Contains(e.mousePosition))
            {
                h = Mathf.Clamp01((e.mousePosition.x - hue.x) / hue.width);
                e.Use();
            }
        }

        Color result = Color.HSVToRGB(h, s, v);
        result.a = 1f;
        return result;
    }

    private static void DrawMarker(float x, float y, Color c)
    {
        GUI.color = c;
        GUI.DrawTexture(new Rect(x - 4, y - 1, 8, 2), White);
        GUI.DrawTexture(new Rect(x - 1, y - 4, 2, 8), White);
        GUI.color = Color.white;
    }

    private static void EnsureHueTex()
    {
        if (_hueTex != null) return;
        _hueTex = new Texture2D(HUE_W, 1)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        for (int x = 0; x < HUE_W; x++)
            _hueTex.SetPixel(x, 0, Color.HSVToRGB(x / (float)(HUE_W - 1), 1f, 1f));
        _hueTex.Apply();
    }

    // Baked once per hue: rebuilt only when the chosen hue changes.
    private static void EnsureSvTex(float h)
    {
        if (_svTex != null && Mathf.Abs(_svHue - h) < 0.001f) return;
        if (_svTex == null)
            _svTex = new Texture2D(SV_W, SV_H)
                { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < SV_H; y++)
        {
            float v = y / (float)(SV_H - 1);
            for (int x = 0; x < SV_W; x++)
                _svTex.SetPixel(x, y, Color.HSVToRGB(h, x / (float)(SV_W - 1), v));
        }
        _svTex.Apply();
        _svHue = h;
    }
}
