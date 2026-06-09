using UnityEngine;
using UnityEditor;

public class SceneBuilder
{
    [MenuItem("SwingingBucket/Build Scene")]
    public static void BuildScene()
    {
        // --- Clear old objects (including inactive pool objects) ---
        string[] namesToDelete = { "PivotPoint", "Bucket", "PaintCanvas", "DropletPrefab", "SimManager", "MainCamera" };
        var rootObjects = UnityEngine.SceneManagement.SceneManager
                            .GetActiveScene().GetRootGameObjects();
        foreach (var obj in rootObjects)
        {
            foreach (string n in namesToDelete)
            {
                // catches both "DropletPrefab" and "DropletPrefab(Clone)"
                if (obj.name == n || obj.name.StartsWith(n))
                {
                    Object.DestroyImmediate(obj);
                    break;
                }
            }
        }

        // --- Pivot Point ---
        GameObject pivot = new GameObject("PivotPoint");
        pivot.transform.position = new Vector3(0, 3, 0);

        // --- Bucket ---
        GameObject bucket = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        bucket.name = "Bucket";
        bucket.transform.position = new Vector3(0, 1.5f, 0);
        bucket.transform.localScale = new Vector3(0.3f, 0.2f, 0.3f);
        Object.DestroyImmediate(bucket.GetComponent<Collider>());

        Material bucketMat = new Material(Shader.Find("Standard"));
        bucketMat.color = new Color(0.8f, 0.4f, 0.1f);
        bucket.GetComponent<Renderer>().sharedMaterial = bucketMat;

        // LineRenderer for rope
        LineRenderer rope = bucket.AddComponent<LineRenderer>();
        rope.startWidth    = 0.03f;
        rope.endWidth      = 0.03f;
        rope.positionCount = 2;
        rope.useWorldSpace = true;
        Material ropeMat   = new Material(Shader.Find("Unlit/Color"));
        ropeMat.color      = new Color(0.6f, 0.4f, 0.2f);
        rope.sharedMaterial = ropeMat;
        rope.SetPosition(0, pivot.transform.position);
        rope.SetPosition(1, bucket.transform.position);

        // --- Paint Canvas ---
        GameObject canvas = GameObject.CreatePrimitive(PrimitiveType.Quad);
        canvas.name = "PaintCanvas";
        canvas.transform.position   = new Vector3(0, 0, 0);
        canvas.transform.rotation   = Quaternion.Euler(90, 0, 0);
        canvas.transform.localScale = new Vector3(4, 4, 1);
        Object.DestroyImmediate(canvas.GetComponent<Collider>());

        Material canvasMat = new Material(Shader.Find("Standard"));
        canvasMat.color = Color.white;
        canvas.GetComponent<Renderer>().sharedMaterial = canvasMat;

        CanvasPainter painter = canvas.AddComponent<CanvasPainter>();
        painter.canvasWorldWidth  = 4f;
        painter.canvasWorldHeight = 4f;

        // --- Droplet Prefab ---
        GameObject dropletObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        dropletObj.name = "DropletPrefab";
        dropletObj.transform.localScale = Vector3.one * 0.03f;
        Object.DestroyImmediate(dropletObj.GetComponent<Collider>());

        Material dropletMat = new Material(Shader.Find("Standard"));
        dropletMat.color = Color.red;
        dropletObj.GetComponent<Renderer>().sharedMaterial = dropletMat;

        dropletObj.AddComponent<PaintDroplet>();
        dropletObj.SetActive(false);

        // --- SimManager ---
        GameObject simManager = new GameObject("SimManager");

        PendulumSimulator sim = simManager.AddComponent<PendulumSimulator>();
        sim.pivotPoint       = pivot.transform;
        sim.bucketTransform  = bucket.transform;
        sim.ropeRenderer     = rope;
        sim.bucketMass       = 0.5f;
        sim.bucketRadius     = 0.15f;
        sim.ropeLength       = 1.5f;
        sim.dampingCoeff     = 0.05f;
        sim.initialAngleDeg  = 45f;
        sim.gravity          = 9.81f;
        sim.initialPaintMass = 0.3f;
        sim.paintFlowRate    = 0.01f;

        SPHSimulator sph = simManager.AddComponent<SPHSimulator>();
        sph.pendulum              = sim;
        sph.smoothingRadius       = 0.04f;
        sph.restDensity           = 1000f;
        sph.stiffness             = 200f;
        sph.viscosityCoeff        = 0.08f;
        sph.particleMass          = 0.02f;
        sph.initialParticleCount  = 120;
        sph.bucketWidth           = 0.15f;
        sph.bucketHeight          = 0.20f;
        sph.holeWidth             = 0.015f;
        sph.wallRestitution       = 0.2f;

        PaintFlowController flow = simManager.AddComponent<PaintFlowController>();
        flow.pendulum      = sim;
        flow.canvasPainter = painter;
        flow.dropletPrefab = dropletObj;
        flow.paintColor    = Color.red;
        flow.viscosity     = 0.01f;
        flow.holeRadius    = 0.005f;
        flow.poolSize      = 100;
        flow.sphSimulator  = sph;

        // --- Camera ---
        GameObject camObj = new GameObject("MainCamera");
        camObj.tag = "MainCamera";
        Camera cam = camObj.AddComponent<Camera>();
        cam.clearFlags      = CameraClearFlags.SolidColor;   // fixed
        cam.backgroundColor = new Color(0.15f, 0.15f, 0.15f);
        camObj.AddComponent<AudioListener>();
        camObj.transform.position = new Vector3(0, 2, -6);
        camObj.transform.rotation = Quaternion.Euler(15, 0, 0);
        camObj.AddComponent<CameraController>();

        // --- UI ---
        SimulationUI ui = simManager.AddComponent<SimulationUI>();
        ui.pendulum       = sim;
        ui.flowController = flow;
        ui.canvasPainter  = painter;
        ui.sphSimulator   = sph;

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        Debug.Log("Scene built! Press Ctrl+S to save.");
        Selection.activeGameObject = simManager;
    }
}
