using UnityEngine;

public class SimulationUI : MonoBehaviour
{
    public PendulumSimulator     pendulum;
    public PaintFlowController   flowController;
    public CanvasPainter         canvasPainter;
    public SPHSimulator          sphSimulator;

    private float ropeLength   = 1.5f;
    private float initialAngle = 45f;
    private float forceMag     = 0f;
    private float forceAngle   = 0f;
    private float r = 1f, g = 0f, b = 0f;

    private string forceMagText   = "0";
    private string forceAngleText = "0";

    private bool    isRunning = false;
    private Vector2 scrollPos;

    // Surface type selector
    private readonly string[] surfaceLabels = { "Canvas", "Metal", "Paper", "Wood" };
    private int selectedSurfaceIdx = 0;   // default: Canvas

    // Particle count selector
    private readonly int[]    particleCounts = { 500, 1000, 2000, 10000, 20000 };
    private readonly string[] particleLabels = { "500", "1,000", "2,000", "10,000", "20,000" };
    private int selectedParticleIdx = 0;   // default: 500

    // ── Styles ────────────────────────────────────────────────────────────────
    private GUIStyle titleStyle;
    private GUIStyle labelStyle;
    private GUIStyle panelStyle;
    private GUIStyle listItemStyle;
    private GUIStyle listItemSelectedStyle;
    private bool stylesReady = false;

    void InitStyles()
    {
        if (stylesReady) return;

        Texture2D panelTex = MakeTex(new Color(0.10f, 0.10f, 0.10f, 0.88f));
        Texture2D itemTex  = MakeTex(new Color(0.18f, 0.18f, 0.18f, 1.00f));
        Texture2D selTex   = MakeTex(new Color(0.15f, 0.48f, 0.80f, 1.00f));
        Texture2D hovTex   = MakeTex(new Color(0.22f, 0.38f, 0.55f, 1.00f));

        panelStyle = new GUIStyle(GUI.skin.box);
        panelStyle.normal.background = panelTex;

        titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize  = 14,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal    = { textColor = new Color(0.4f, 0.8f, 1f) }
        };

        labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            normal   = { textColor = Color.white }
        };

        listItemStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize  = 12,
            alignment = TextAnchor.MiddleLeft,
            padding   = new RectOffset(12, 0, 0, 0),
            border    = new RectOffset(2, 2, 2, 2)
        };
        listItemStyle.normal.background  = itemTex;
        listItemStyle.normal.textColor   = new Color(0.85f, 0.85f, 0.85f);
        listItemStyle.hover.background   = hovTex;
        listItemStyle.hover.textColor    = Color.white;
        listItemStyle.active.background  = selTex;

        listItemSelectedStyle = new GUIStyle(listItemStyle)
        {
            fontStyle = FontStyle.Bold
        };
        listItemSelectedStyle.normal.background = selTex;
        listItemSelectedStyle.normal.textColor  = Color.white;
        listItemSelectedStyle.hover.background  = selTex;

        stylesReady = true;
    }

    static Texture2D MakeTex(Color c)
    {
        var t = new Texture2D(1, 1);
        t.SetPixel(0, 0, c);
        t.Apply();
        return t;
    }

    // ── GUI ───────────────────────────────────────────────────────────────────
    void OnGUI()
    {
        InitStyles();

        float panelW    = 270f;
        float panelH    = 700f;
        float visibleH  = Mathf.Min(panelH, Screen.height - 16f);
        GUI.Box(new Rect(8, 8, panelW, visibleH), "", panelStyle);

        GUILayout.BeginArea(new Rect(18, 18, panelW - 20, visibleH - 20));
        scrollPos = GUILayout.BeginScrollView(scrollPos, false, false,
            GUIStyle.none, GUI.skin.verticalScrollbar);

        // ── Title ──────────────────────────────────────────────
        GUILayout.Label("Swinging Paint Bucket", titleStyle);
        GUILayout.Space(6);

        // ── Rope Length ────────────────────────────────────────
        GUILayout.Label("Rope Length: " + ropeLength.ToString("F2") + " m", labelStyle);
        ropeLength = GUILayout.HorizontalSlider(ropeLength, 0.3f, 3f);
        GUILayout.Space(6);

        // ── Initial Angle ──────────────────────────────────────
        GUILayout.Label("Initial Angle: " + initialAngle.ToString("F1") + " deg", labelStyle);
        initialAngle = GUILayout.HorizontalSlider(initialAngle, -85f, 85f);
        GUILayout.Space(10);

        // ── Initial Force ──────────────────────────────────────
        GUILayout.Label("Initial Force", titleStyle);
        GUILayout.Space(4);

        GUILayout.BeginHorizontal();
        GUILayout.Label("Magnitude (N):", labelStyle, GUILayout.Width(115));
        forceMagText = GUILayout.TextField(forceMagText, GUILayout.Width(80));
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Swing Dir (deg):", labelStyle, GUILayout.Width(115));
        forceAngleText = GUILayout.TextField(forceAngleText, GUILayout.Width(80));
        GUILayout.EndHorizontal();

        GUIStyle hintStyle = new GUIStyle(labelStyle)
            { fontSize = 10, normal = { textColor = new Color(0.6f, 0.6f, 0.6f) } };
        GUILayout.Label("0°=horiz  45°=circle  90°=vert", hintStyle);

        GUILayout.Space(10);

        // ── Particle Count ─────────────────────────────────────
        GUILayout.Label("Particle Count", titleStyle);
        GUILayout.Space(4);

        for (int i = 0; i < particleLabels.Length; i++)
        {
            GUIStyle style = (i == selectedParticleIdx) ? listItemSelectedStyle : listItemStyle;
            string   label = (i == selectedParticleIdx)
                ? "✓  " + particleLabels[i]
                : "    " + particleLabels[i];

            if (GUILayout.Button(label, style, GUILayout.Height(26)))
            {
                selectedParticleIdx = i;
                if (sphSimulator != null)
                    sphSimulator.initialParticleCount = particleCounts[selectedParticleIdx];
            }
        }

        if (isRunning)
        {
            GUIStyle noteStyle = new GUIStyle(labelStyle)
            {
                fontSize = 10,
                normal   = { textColor = new Color(1f, 0.8f, 0.3f) }
            };
            GUILayout.Label("Reset to apply new count", noteStyle);
        }

        GUILayout.Space(10);

        // ── Surface Type ───────────────────────────────────────
        GUILayout.Label("Surface Type", titleStyle);
        GUILayout.Space(4);

        for (int i = 0; i < surfaceLabels.Length; i++)
        {
            GUIStyle style = (i == selectedSurfaceIdx) ? listItemSelectedStyle : listItemStyle;
            string   label = (i == selectedSurfaceIdx)
                ? "✓  " + surfaceLabels[i]
                : "    " + surfaceLabels[i];

            if (GUILayout.Button(label, style, GUILayout.Height(26)))
            {
                selectedSurfaceIdx = i;
                canvasPainter?.SetSurfaceType((CanvasPainter.SurfaceType)i);
            }
        }

        GUILayout.Space(10);

        // ── Paint Color ────────────────────────────────────────
        GUILayout.Label("Paint Color", titleStyle);
        GUILayout.Space(4);

        GUILayout.BeginHorizontal();
        GUILayout.Label("R", labelStyle, GUILayout.Width(15));
        r = GUILayout.HorizontalSlider(r, 0f, 1f);
        GUILayout.Label(r.ToString("F2"), labelStyle, GUILayout.Width(35));
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("G", labelStyle, GUILayout.Width(15));
        g = GUILayout.HorizontalSlider(g, 0f, 1f);
        GUILayout.Label(g.ToString("F2"), labelStyle, GUILayout.Width(35));
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("B", labelStyle, GUILayout.Width(15));
        b = GUILayout.HorizontalSlider(b, 0f, 1f);
        GUILayout.Label(b.ToString("F2"), labelStyle, GUILayout.Width(35));
        GUILayout.EndHorizontal();

        GUI.color = new Color(r, g, b);
        GUILayout.Box("", GUILayout.Height(22), GUILayout.Width(60));
        GUI.color = Color.white;

        GUILayout.Space(10);

        // ── Buttons ────────────────────────────────────────────
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(isRunning ? "Stop" : "Start", GUILayout.Height(34)))
            ToggleSimulation();
        if (GUILayout.Button("Reset", GUILayout.Height(34)))
            ResetSim();
        GUILayout.EndHorizontal();

        GUILayout.Space(4);
        if (GUILayout.Button("Save Canvas",  GUILayout.Height(28))) SaveCanvas();
        GUILayout.Space(2);
        if (GUILayout.Button("Clear Canvas", GUILayout.Height(28))) canvasPainter?.ClearCanvas();

        GUILayout.EndScrollView();
        GUILayout.EndArea();

        flowController?.SetColor(new Color(r, g, b));
    }

    // ── Simulation control ────────────────────────────────────────────────────

    private void ToggleSimulation()
    {
        if (isRunning) { StopSim(); return; }

        float.TryParse(forceMagText,   out forceMag);
        float.TryParse(forceAngleText, out forceAngle);

        pendulum.ropeLength            = ropeLength;
        pendulum.initialAngleDeg       = initialAngle;
        pendulum.initialForceMagnitude = forceMag;
        pendulum.initialForceAngle     = forceAngle;
        flowController?.SetColor(new Color(r, g, b));

        if (sphSimulator != null)
            sphSimulator.initialParticleCount = particleCounts[selectedParticleIdx];

        pendulum.Initialize();
        sphSimulator?.Initialize();
        pendulum.StartSimulation();
        sphSimulator?.StartSimulation();
        isRunning = true;
    }

    private void StopSim()
    {
        pendulum?.StopSimulation();
        sphSimulator?.StopSimulation();
        isRunning = false;
    }

    private void ResetSim()
    {
        pendulum?.StopSimulation();
        sphSimulator?.StopSimulation();
        pendulum?.ResetSimulation();

        if (sphSimulator != null)
            sphSimulator.initialParticleCount = particleCounts[selectedParticleIdx];

        sphSimulator?.Initialize();
        isRunning = false;
    }

    private void SaveCanvas()
    {
        string path = Application.persistentDataPath
            + "/canvas_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png";
        canvasPainter?.SaveCanvas(path);
        Debug.Log("Canvas saved: " + path);
    }
}
