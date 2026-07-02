using UnityEngine;

public class PaintDroplet : MonoBehaviour
{
    public Color color  = Color.red;
    public float radius = 0.02f;
    public float mass   = 0.001f;

    private Vector3      velocity;
    private bool         isActive;
    private float        lifetime;
    private const float  maxLifetime = 5f;
    private CanvasPainter canvasPainter;

    public void Launch(Vector3 startPosition, Vector3 initialVelocity, Color paintColor,
                       float dropletRadius, CanvasPainter painter, Vector3 axisDir)
    {
        // Pause trail BEFORE moving — prevents a streak from the old position to the new one
        var trail = GetComponent<TrailRenderer>();
        if (trail != null) trail.emitting = false;

        transform.position = startPosition;
        velocity      = initialVelocity;
        color         = paintColor;
        radius        = dropletRadius;
        canvasPainter = painter;
        isActive      = true;
        lifetime      = 0f;

        // Elongate the droplet along its local Y, then orient that long axis along
        // the rope axis, so the drop's axis stays (roughly) parallel to the rope at
        // any swing angle instead of standing upright to the canvas. This only sets
        // the visual orientation — the flight velocity/direction is unchanged.
        float d = radius * 2f;
        transform.localScale = new Vector3(d * 0.8f, d * 1.8f, d * 0.8f);
        transform.rotation   = axisDir.sqrMagnitude > 1e-6f
            ? Quaternion.FromToRotation(Vector3.up, axisDir.normalized)
            : Quaternion.identity;
        GetComponent<Renderer>().material.color = color;

        if (trail != null)
        {
            trail.startColor = new Color(paintColor.r, paintColor.g, paintColor.b, 0.85f);
            trail.endColor   = new Color(paintColor.r, paintColor.g, paintColor.b, 0f);
            trail.startWidth = Mathf.Max(0.04f, radius * 3f);
            trail.endWidth   = 0f;
            trail.Clear();
            trail.emitting   = true;   // Resume recording from the correct position
        }
    }

    void FixedUpdate()
    {
        if (!isActive) return;

        float dt = Time.fixedDeltaTime;
        lifetime += dt;
        if (lifetime > maxLifetime) { Deactivate(); return; }

        // Gravity + air drag (no Unity physics components)
        float   airDensity  = 1.225f;
        float   dragCoeff   = 0.47f;
        float   crossSection = Mathf.PI * radius * radius;
        float   speed        = velocity.magnitude;
        Vector3 dragForce    = -0.5f * airDensity * dragCoeff * crossSection * speed * velocity;
        Vector3 gravityForce = new Vector3(0f, -9.81f * mass, 0f);

        velocity           += ((gravityForce + dragForce) / mass) * dt;
        transform.position += velocity * dt;

        // Impact against the board's real plane (handles tilt), falling back to the
        // flat floor (y≈0) if no canvas is wired.
        if (canvasPainter != null)
        {
            float sd = Vector3.Dot(transform.position - canvasPainter.SurfacePoint,
                                   canvasPainter.SurfaceNormal);
            if (sd <= 0.01f) HitCanvas();
        }
        else if (transform.position.y <= 0.01f) HitCanvas();
    }

    private void HitCanvas()
    {
        if (canvasPainter != null)
            canvasPainter.PaintAt(transform.position, color, radius, velocity);
        Deactivate();
    }

    public void Deactivate()
    {
        isActive = false;
        var trail = GetComponent<TrailRenderer>();
        if (trail != null)
        {
            trail.emitting = false;   // Stop recording before deactivating
            trail.Clear();
        }
        gameObject.SetActive(false);
    }
}
