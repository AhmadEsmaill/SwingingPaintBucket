using UnityEngine;

// Each droplet is an independent particle with its own physics (no Rigidbody)
public class PaintDroplet : MonoBehaviour
{
    public Color color = Color.red;
    public float radius = 0.02f;
    public float mass = 0.001f;

    private Vector3 velocity;
    private bool isActive;
    private float lifetime;
    private float maxLifetime = 5f;

    private CanvasPainter canvasPainter;

    public void Launch(Vector3 startPosition, Vector3 initialVelocity, Color paintColor,
                       float dropletRadius, CanvasPainter painter)
    {
        transform.position = startPosition;
        velocity = initialVelocity;
        color = paintColor;
        radius = dropletRadius;
        canvasPainter = painter;
        isActive = true;
        lifetime = 0f;

        // Visual size
        transform.localScale = Vector3.one * radius * 2f;
        GetComponent<Renderer>().material.color = color;
    }

    void FixedUpdate()
    {
        if (!isActive) return;

        float dt = Time.fixedDeltaTime;
        lifetime += dt;

        if (lifetime > maxLifetime)
        {
            Deactivate();
            return;
        }

        // Forces: gravity + air drag
        // my'' = -mg - F_drag_y  |  mx'' = -F_drag_x
        float airDensity = 1.225f;
        float dragCoeff = 0.47f;
        float crossSection = Mathf.PI * radius * radius;
        float speed = velocity.magnitude;

        Vector3 dragForce = -0.5f * airDensity * dragCoeff * crossSection * speed * velocity;
        Vector3 gravityForce = new Vector3(0, -9.81f * mass, 0);
        Vector3 totalForce = gravityForce + dragForce;

        // Euler integration for droplets (fast, acceptable for small dt)
        velocity += (totalForce / mass) * dt;
        transform.position += velocity * dt;

        // Check if droplet reached the canvas (Y <= canvas Y)
        if (transform.position.y <= 0.01f)
        {
            HitCanvas();
        }
    }

    private void HitCanvas()
    {
        if (canvasPainter != null)
        {
            float impactSpeed = velocity.magnitude;
            canvasPainter.PaintAt(transform.position, color, radius, impactSpeed);
        }
        Deactivate();
    }

    public void Deactivate()
    {
        isActive = false;
        gameObject.SetActive(false);
    }
}
