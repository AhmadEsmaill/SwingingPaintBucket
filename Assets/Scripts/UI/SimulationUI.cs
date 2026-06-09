using UnityEngine;

public class SimulationUI : MonoBehaviour
{
    public PendulumSimulator pendulum;
    public PaintFlowController flowController;
    public CanvasPainter canvasPainter;
    public SPHSimulator sphSimulator;

    private float ropeLength   = 1.5f;
    private float initialAngle = 45f;
    private float forceMag     = 0f;
    private float forceAngle   = 0f;
    private float r = 1f, g = 0f, b = 0f;

    private string forceMagText   = "0";
    private string forceAngleText = "0";

    private bool isRunning = false;

    private GUIStyle titleStyle;
    private GUIStyle labelStyle;
    private GUIStyle panelStyle;
    private bool stylesReady = false;

    void InitStyles()
    {
        if (stylesReady) return;

        Texture2D panelTex = new Texture2D(1, 1);
        panelTex.SetPixel(0, 0, new Color(0.1f, 0.1f, 0.1f, 0.88f));
        panelTex.Apply();

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

        stylesReady = true;
    }

    void OnGUI()
    {
        InitStyles();

        float panelW = 270f;
        float panelH = 530f;
        GUI.Box(new Rect(8, 8, panelW, panelH), "", panelStyle);

        GUILayout.BeginArea(new Rect(18, 18, panelW - 20, panelH - 20));

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
        GUILayout.Label("Direction  (deg):", labelStyle, GUILayout.Width(115));
        forceAngleText = GUILayout.TextField(forceAngleText, GUILayout.Width(80));
        GUILayout.EndHorizontal();

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

        // Color preview
        GUI.color = new Color(r, g, b);
        GUILayout.Box("", GUILayout.Height(22), GUILayout.Width(60));
        GUI.color = Color.white;

        GUILayout.Space(10);

        // ── Buttons ────────────────────────────────────────────
        GUILayout.BeginHorizontal();
        string startLabel = isRunning ? "Stop" : "Start";
        if (GUILayout.Button(startLabel, GUILayout.Height(34)))
            ToggleSimulation();

        if (GUILayout.Button("Reset", GUILayout.Height(34)))
            ResetSim();
        GUILayout.EndHorizontal();

        GUILayout.Space(4);
        if (GUILayout.Button("Save Canvas", GUILayout.Height(28)))
            SaveCanvas();
        GUILayout.Space(2);
        if (GUILayout.Button("Clear Canvas", GUILayout.Height(28)))
            canvasPainter?.ClearCanvas();

        GUILayout.EndArea();

        // Live-update color while sliders move
        flowController?.SetColor(new Color(r, g, b));
    }

    private void ToggleSimulation()
    {
        if (isRunning) { StopSim(); return; }

        float.TryParse(forceMagText,   out forceMag);
        float.TryParse(forceAngleText, out forceAngle);

        pendulum.ropeLength             = ropeLength;
        pendulum.initialAngleDeg        = initialAngle;
        pendulum.initialForceMagnitude  = forceMag;
        pendulum.initialForceAngle      = forceAngle;
        flowController?.SetColor(new Color(r, g, b));

        pendulum.Initialize();
        sphSimulator?.Initialize();
        pendulum.StartSimulation();
        isRunning = true;
    }

    private void StopSim()
    {
        pendulum?.StopSimulation();
        isRunning = false;
    }

    private void ResetSim()
    {
        pendulum?.StopSimulation();
        pendulum?.ResetSimulation();
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
