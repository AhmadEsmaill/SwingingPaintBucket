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

    // Bucket properties
    private float bucketMass    = 0.5f;    // kg
    private float bucketRadius  = 0.15f;   // m
    private float paintAmount   = 0.3f;    // kg
    private float holeRadius    = 0.005f;  // m
    private float r = 1f, g = 0f, b = 0f;         // Color A
    private float r2 = 0f, g2 = 0f, b2 = 1f;     // Color B

    private string forceMagText   = "0";
    private string forceAngleText = "0";

    private bool    isRunning = false;
    private Vector2 scrollPos;

    // Rope type selector  — (stiffness N/m, radial damping N·s/m)
    private readonly string[] ropeLabels     = { "Rigid", "Steel", "Rope", "Elastic", "Bungee" };
    private readonly float[]  ropeStiffness  = { 0f,      5000f,  400f,   80f,       15f      };
    private readonly float[]  ropeDampCoeff  = { 2f,      8f,     3f,     1.5f,      0.5f     };
    private int selectedRopeIdx = 0;   // default: Rigid

    // Surface type selector
    private readonly string[] surfaceLabels = { "Canvas", "Metal", "Paper", "Wood" };
    private int selectedSurfaceIdx = 0;   // default: Canvas

    // Blend mode selector
    private readonly float[]  blendValues = { 0f, 0.25f, 0.5f, 0.75f, 1f };
    private readonly string[] blendLabels = { "No Blend", "25%", "50%", "75%", "100%" };
    private int selectedBlendIdx = 0;     // default: No Blend

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

        // ── Rope Type ──────────────────────────────────────────
        GUILayout.Label("Rope Type", titleStyle);
        GUILayout.Space(4);
        for (int i = 0; i < ropeLabels.Length; i++)
        {
            GUIStyle rs = (i == selectedRopeIdx) ? listItemSelectedStyle : listItemStyle;
            string   rl = (i == selectedRopeIdx) ? "✓  " + ropeLabels[i] : "    " + ropeLabels[i];
            if (GUILayout.Button(rl, rs, GUILayout.Height(26)))
                selectedRopeIdx = i;
        }
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
        GUILayout.Label("dir 0°=line  90°=sideways → ellipse/circle", hintStyle);

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

        // ── Paint Colors + Bucket Blend ────────────────────────
        GUILayout.Label("Bucket Paint", titleStyle);
        GUILayout.Space(4);

        // Color A
        GUILayout.Label("Color A", labelStyle);
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
        GUILayout.Box("", GUILayout.Height(18), GUILayout.Width(60));
        GUI.color = Color.white;

        GUILayout.Space(6);

        // Blend ratio between A and B
        GUILayout.Label("Mix A → B", labelStyle);
        GUILayout.Space(2);
        for (int i = 0; i < blendLabels.Length; i++)
        {
            GUIStyle bStyle = (i == selectedBlendIdx) ? listItemSelectedStyle : listItemStyle;
            string   bLabel = (i == selectedBlendIdx)
                ? "✓  " + blendLabels[i]
                : "    " + blendLabels[i];

            if (GUILayout.Button(bLabel, bStyle, GUILayout.Height(24)))
            {
                selectedBlendIdx = i;
                flowController?.SetColorBlend(blendValues[selectedBlendIdx]);
            }
        }

        GUILayout.Space(6);

        // Color B
        GUILayout.Label("Color B", labelStyle);
        GUILayout.BeginHorizontal();
        GUILayout.Label("R", labelStyle, GUILayout.Width(15));
        r2 = GUILayout.HorizontalSlider(r2, 0f, 1f);
        GUILayout.Label(r2.ToString("F2"), labelStyle, GUILayout.Width(35));
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        GUILayout.Label("G", labelStyle, GUILayout.Width(15));
        g2 = GUILayout.HorizontalSlider(g2, 0f, 1f);
        GUILayout.Label(g2.ToString("F2"), labelStyle, GUILayout.Width(35));
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        GUILayout.Label("B", labelStyle, GUILayout.Width(15));
        b2 = GUILayout.HorizontalSlider(b2, 0f, 1f);
        GUILayout.Label(b2.ToString("F2"), labelStyle, GUILayout.Width(35));
        GUILayout.EndHorizontal();
        GUI.color = new Color(r2, g2, b2);
        GUILayout.Box("", GUILayout.Height(18), GUILayout.Width(60));
        GUI.color = Color.white;

        GUILayout.Space(10);

        // ── Bucket Properties ──────────────────────────────────
        GUILayout.Label("Bucket", titleStyle);
        GUILayout.Space(4);

        GUILayout.Label("Weight: " + bucketMass.ToString("F2") + " kg", labelStyle);
        bucketMass = GUILayout.HorizontalSlider(bucketMass, 0.1f, 3f);
        GUILayout.Space(4);

        GUILayout.Label("Radius: " + (bucketRadius * 100f).ToString("F0") + " cm", labelStyle);
        bucketRadius = GUILayout.HorizontalSlider(bucketRadius, 0.05f, 0.40f);
        GUILayout.Space(4);

        GUILayout.Label("Paint amount: " + paintAmount.ToString("F2") + " kg", labelStyle);
        paintAmount = GUILayout.HorizontalSlider(paintAmount, 0.05f, 2f);
        GUILayout.Space(4);

        GUILayout.Label("Hole diameter: " + (holeRadius * 2000f).ToString("F1") + " mm", labelStyle);
        holeRadius = GUILayout.HorizontalSlider(holeRadius, 0.001f, 0.015f);

        GUILayout.Space(10);

        // ── Buttons ────────────────────────────────────────────
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(isRunning ? "Stop" : "Start", GUILayout.Height(34)))
            ToggleSimulation();
        if (GUILayout.Button("Reset", GUILayout.Height(34)))
            ResetSim();
        GUILayout.EndHorizontal();

        GUILayout.Space(4);
        if (GUILayout.Button("Clear Canvas", GUILayout.Height(28))) canvasPainter?.ClearCanvas();

        GUILayout.EndScrollView();
        GUILayout.EndArea();

        flowController?.SetColor(new Color(r, g, b));
        flowController?.SetColorB(new Color(r2, g2, b2));
    }

    // ── Simulation control ────────────────────────────────────────────────────

    private void ToggleSimulation()
    {
        if (isRunning) { StopSim(); return; }

        float.TryParse(forceMagText,   out forceMag);
        float.TryParse(forceAngleText, out forceAngle);

        pendulum.ropeLength      = ropeLength;
        pendulum.ropeStiffness   = ropeStiffness[selectedRopeIdx];
        pendulum.ropeDamping     = ropeDampCoeff[selectedRopeIdx];
        pendulum.initialAngleDeg       = initialAngle;
        pendulum.initialForceMagnitude = forceMag;
        pendulum.initialForceAngle     = forceAngle;
        ApplyBucketProperties();
        flowController?.SetColor(new Color(r, g, b));
        flowController?.SetColorB(new Color(r2, g2, b2));
        flowController?.SetColorBlend(blendValues[selectedBlendIdx]);

        if (sphSimulator != null)
            sphSimulator.initialParticleCount = 3000;

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
        canvasPainter?.ResetStroke();
        isRunning = false;
    }

    private void ResetSim()
    {
        pendulum?.StopSimulation();
        sphSimulator?.StopSimulation();
        if (pendulum != null)
        {
            pendulum.ropeLength    = ropeLength;
            pendulum.ropeStiffness = ropeStiffness[selectedRopeIdx];
            pendulum.ropeDamping   = ropeDampCoeff[selectedRopeIdx];
        }
        ApplyBucketProperties();
        pendulum?.ResetSimulation();

        if (sphSimulator != null)
            sphSimulator.initialParticleCount = 3000;

        sphSimulator?.Initialize();
        isRunning = false;
    }

    private void ApplyBucketProperties()
    {
        if (pendulum != null)
        {
            pendulum.bucketMass       = bucketMass;
            pendulum.bucketRadius     = bucketRadius;
            pendulum.initialPaintMass = paintAmount;
            // Paint depletion rate scales with hole area (r²), normalised to default r=0.005m
            pendulum.paintFlowRate    = 0.01f * Mathf.Pow(holeRadius / 0.005f, 2f);
        }
        if (flowController != null)
        {
            flowController.holeRadius = holeRadius;
        }
        if (sphSimulator != null)
        {
            sphSimulator.bucketWidth  = bucketRadius * 2f;
            sphSimulator.holeWidth    = holeRadius   * 2f;
        }
    }

    private void SaveCanvas()
    {
        string path = Application.persistentDataPath
            + "/canvas_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png";
        canvasPainter?.SaveCanvas(path);
        Debug.Log("Canvas saved: " + path);
    }
}
