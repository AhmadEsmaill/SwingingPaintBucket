using UnityEngine;
using System.Collections.Generic;

public class SimulationUI : MonoBehaviour
{
    public PendulumSimulator     pendulum;
    public PaintFlowController   flowController;
    public CanvasPainter         canvasPainter;
    public SPHSimulator          sphSimulator;

    private float ropeLength   = 1.5f;
    private float initialAngle = 45f;                 // default release angle
    private string initialAngleText = "45";           // exact typed entry (kept in sync)
    private float forceMag     = 0f;
    private float forceAngle   = 0f;

    // Bucket properties
    private float bucketMass    = 0.5f;    // kg
    private float bucketRadius  = 0.15f;   // m
    private float paintVolume   = 0.5f;    // litres (converted to kg for the physics)
    private float holeRadius    = 0.005f;  // m

    // Typical emulsion/latex paint is ~1.3 kg per litre. The pendulum works in
    // mass (kg), so the litre figure the user enters is scaled by this to get kg.
    private const float PaintDensity = 1.3f;   // kg / L

    // Ordered paint layers: [0] is poured first (bottom → exits first).
    private readonly List<PaintLayer> paintLayers =
        new List<PaintLayer> { new PaintLayer(Color.red, 1f) };
    private int selectedLayer = 0;
    // Palette used for each freshly added layer, cycled by index.
    private static readonly Color[] palette =
        { Color.red, Color.blue, new Color(1f, 0.85f, 0f), Color.green,
          new Color(0.6f, 0.2f, 0.9f), Color.cyan, new Color(1f, 0.4f, 0f), Color.white };

    private string forceMagText   = "0";
    private string forceAngleText = "0";

    private bool    isRunning = false;
    private Vector2 scrollPos;

    // Rope type selector  — (stiffness N/m, radial damping N·s/m)
    private readonly string[] ropeLabels     = { "Rigid", "Steel", "Rope", "Elastic", "Bungee" };
    private readonly float[]  ropeStiffness  = { 0f,      5000f,  400f,   80f,       15f      };
    private readonly float[]  ropeDampCoeff  = { 2f,      8f,     3f,     1.5f,      0.5f     };
    private int selectedRopeIdx = 0;   // default: Rigid

    // Rope attachment on the bucket's handle (bail): A = centre/apex (balanced),
    // B = left join, C = right join. B/C make the bucket hang tilted.
    private readonly string[] handleLabels = { "A — Centre (apex)", "B — Left join", "C — Right join" };
    private int selectedHandleIdx = 0;   // default: A (current behaviour)

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

        // ── Rope Attachment (handle) ───────────────────────────
        GUILayout.Label("Rope Attachment", titleStyle);
        GUILayout.Space(4);
        for (int i = 0; i < handleLabels.Length; i++)
        {
            GUIStyle hs = (i == selectedHandleIdx) ? listItemSelectedStyle : listItemStyle;
            string   hl = (i == selectedHandleIdx) ? "✓  " + handleLabels[i] : "    " + handleLabels[i];
            if (GUILayout.Button(hl, hs, GUILayout.Height(26)))
            {
                selectedHandleIdx = i;
                if (pendulum != null)
                {
                    pendulum.handleAttach = (PendulumSimulator.HandleAttachPoint)i;
                    if (!isRunning) pendulum.Initialize();   // show the new tilt at rest
                }
            }
        }
        GUILayout.Space(6);

        // ── Initial Angle ──────────────────────────────────────
        // Slider for quick setting + a text field for an exact value; the two stay
        // in sync (default stays at 45°).
        GUILayout.Label("Initial Angle (deg)", labelStyle);
        GUILayout.BeginHorizontal();
        float sliderAngle = GUILayout.HorizontalSlider(initialAngle, -85f, 85f);
        if (!Mathf.Approximately(sliderAngle, initialAngle))
        {
            initialAngle     = sliderAngle;
            initialAngleText = initialAngle.ToString("F1");   // slider drag → refresh text
        }
        initialAngleText = GUILayout.TextField(initialAngleText, GUILayout.Width(55));
        if (float.TryParse(initialAngleText, out float typedAngle))   // typed → update value
            initialAngle = Mathf.Clamp(typedAngle, -85f, 85f);
        GUILayout.EndHorizontal();
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

        // ── Paint Colours (ordered layers) ─────────────────────
        GUILayout.Label("Bucket Paint", titleStyle);
        GUILayout.Space(2);
        GUIStyle noteStyle = new GUIStyle(labelStyle)
            { fontSize = 10, normal = { textColor = new Color(0.6f, 0.6f, 0.6f) } };
        GUILayout.Label("Order = pour order. #1 sits at the bottom", noteStyle);
        GUILayout.Label("and (unmixed) exits the hole first.", noteStyle);
        GUILayout.Space(4);

        float weightTotal = 0f;
        foreach (var l in paintLayers) weightTotal += Mathf.Max(0f, l.fraction);
        if (weightTotal <= 0f) weightTotal = 1f;

        int removeIdx = -1;
        for (int i = 0; i < paintLayers.Count; i++)
        {
            PaintLayer layer = paintLayers[i];
            int pct = Mathf.RoundToInt(Mathf.Max(0f, layer.fraction) / weightTotal * 100f);

            GUILayout.BeginHorizontal();

            GUI.color = layer.color;
            GUILayout.Box("", GUILayout.Width(22), GUILayout.Height(22));
            GUI.color = Color.white;

            GUIStyle rowStyle = (i == selectedLayer) ? listItemSelectedStyle : listItemStyle;
            string   rowLabel = string.Format("{0}#{1}  —  {2}%",
                i == selectedLayer ? "✓ " : "   ", i + 1, pct);
            if (GUILayout.Button(rowLabel, rowStyle, GUILayout.Height(22)))
                selectedLayer = i;

            if (paintLayers.Count > 1 &&
                GUILayout.Button("✕", listItemStyle, GUILayout.Width(24), GUILayout.Height(22)))
                removeIdx = i;   // defer removal until after the loop (stable layout)

            GUILayout.EndHorizontal();
        }
        if (removeIdx >= 0 && paintLayers.Count > 1)
        {
            paintLayers.RemoveAt(removeIdx);
            selectedLayer = Mathf.Clamp(selectedLayer, 0, paintLayers.Count - 1);
        }

        GUILayout.Space(4);
        if (GUILayout.Button("＋ Add Colour", GUILayout.Height(26)))
        {
            paintLayers.Add(new PaintLayer(palette[paintLayers.Count % palette.Length], 1f));
            selectedLayer = paintLayers.Count - 1;
        }

        GUILayout.Space(8);

        // Selected layer: palette picker + amount.
        selectedLayer = Mathf.Clamp(selectedLayer, 0, paintLayers.Count - 1);
        PaintLayer sel = paintLayers[selectedLayer];

        GUILayout.Label("Colour #" + (selectedLayer + 1), labelStyle);
        sel.color = ColorPicker.Draw(sel.color, panelW - 60f);

        GUILayout.Space(4);
        int selPct = Mathf.RoundToInt(Mathf.Max(0f, sel.fraction) / weightTotal * 100f);
        GUILayout.Label("Amount of total: " + selPct + " %", labelStyle);
        sel.fraction = GUILayout.HorizontalSlider(sel.fraction, 0.05f, 1f);

        GUILayout.Space(8);

        // Mixing: 0 = clean layers (FIFO), 100% = fully pre-mixed.
        GUILayout.Label("Mixing", labelStyle);
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

        GUILayout.Label("Paint amount: " + paintVolume.ToString("F2") + " L", labelStyle);
        paintVolume = GUILayout.HorizontalSlider(paintVolume, 0.1f, 3f);
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

        flowController?.SetLayers(paintLayers);
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
        flowController?.SetLayers(paintLayers);
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
            pendulum.handleAttach     = (PendulumSimulator.HandleAttachPoint)selectedHandleIdx;
            pendulum.initialPaintMass = paintVolume * PaintDensity;
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
