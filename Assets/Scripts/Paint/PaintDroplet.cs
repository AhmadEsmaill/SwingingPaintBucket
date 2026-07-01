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
                       float dropletRadius, CanvasPainter painter)
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

        transform.localScale = Vector3.one * radius * 2f;
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

        if (transform.position.y <= 0.01f) HitCanvas();
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
