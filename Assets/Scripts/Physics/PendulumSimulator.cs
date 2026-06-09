using UnityEngine;

public class PendulumSimulator : MonoBehaviour
{
    [Header("Bucket Properties")]
    public float bucketMass = 0.5f;
    public float bucketRadius = 0.1f;
    public float bucketHalfHeight = 0.2f;  // half the visual bucket height (world units)

    [Header("Rope Properties")]
    public float ropeLength = 1.5f;
    public float dampingCoeff = 0.05f;

    [Header("Motion Properties")]
    public float initialAngleDeg = 45f;
    public float initialAngularVelocity = 0f;
    public float initialAngleDegZ = 15f;   // Z-axis initial angle for 2D floor patterns

    [Header("Initial Force")]
    public float initialForceMagnitude = 0f;   // Newtons
    public float initialForceAngle = 0f;        // degrees: 0=right, 90=up

    [Header("Environment")]
    public float gravity = 9.81f;
    public float airDensity = 1.225f;
    public float dragCoefficient = 0.47f;
    public Vector3 windForce = Vector3.zero;

    [Header("Paint Properties")]
    public float initialPaintMass = 0.3f;
    public float paintFlowRate = 0.01f;

    [Header("References")]
    public Transform pivotPoint;
    public Transform bucketTransform;
    public LineRenderer ropeRenderer;

    // Current state
    private float theta;
    private float thetaDot;
    private float thetaZ;       // Z-axis swing angle
    private float thetaZDot;
    private float currentPaintMass;
    private bool isSimulating;
    private Vector3 ropeDirection = Vector3.down;  // cached unit vector from pivot to bucket

    // Public read-only properties used by PaintFlowController and others
    public float   Theta           => theta;
    public float   AngularVelocity => thetaDot;
    public float   CurrentMass     => bucketMass + currentPaintMass;
    public float   PaintMass       => currentPaintMass;
    public float   PaintFillRatio  => initialPaintMass > 0f ? currentPaintMass / initialPaintMass : 0f;
    public bool    IsSimulating    => isSimulating;
    // Rope attachment point (top of bucket)
    public Vector3 BucketPosition  { get; private set; }
    // Visual center: half-height further along the rope direction
    public Vector3 BucketCenter    => BucketPosition + ropeDirection * bucketHalfHeight;
    public Vector3 BucketVelocity  { get; private set; }
    public Vector3 RopeDirection   => ropeDirection;

    void Start()
    {
        Initialize();
    }

    public void Initialize()
    {
        theta    = initialAngleDeg  * Mathf.Deg2Rad;
        thetaZ   = initialAngleDegZ * Mathf.Deg2Rad;
        thetaZDot = 0f;
        currentPaintMass = initialPaintMass;
        isSimulating = false;

        float forceRad   = initialForceAngle * Mathf.Deg2Rad;
        float tangential = initialForceMagnitude * Mathf.Cos(forceRad - theta);
        thetaDot = initialAngularVelocity + tangential / (CurrentMass * ropeLength);

        UpdateBucketTransform();
        UpdateRopeRenderer();
    }

    public void StartSimulation() => isSimulating = true;
    public void StopSimulation() => isSimulating = false;
    public void ResetSimulation() => Initialize();

    void Update()
    {
        // Always update rope visual so it shows even before simulation starts
        UpdateRopeRenderer();
    }

    void FixedUpdate()
    {
        if (!isSimulating) return;

        float dt = Time.fixedDeltaTime;

        currentPaintMass = Mathf.Max(0f, currentPaintMass - paintFlowRate * dt);

        Vector3 prevPos = BucketPosition;
        RK4Step(dt);
        RK4StepZ(dt);
        UpdateBucketTransform();
        BucketVelocity = (BucketPosition - prevPos) / dt;
    }

    // RK4 for X-swing
    private void RK4Step(float dt)
    {
        float m = CurrentMass;

        float k1_t = thetaDot;
        float k1_w = AngularAcceleration(theta, thetaDot, m);

        float k2_t = thetaDot + 0.5f * dt * k1_w;
        float k2_w = AngularAcceleration(theta + 0.5f * dt * k1_t, thetaDot + 0.5f * dt * k1_w, m);

        float k3_t = thetaDot + 0.5f * dt * k2_w;
        float k3_w = AngularAcceleration(theta + 0.5f * dt * k2_t, thetaDot + 0.5f * dt * k2_w, m);

        float k4_t = thetaDot + dt * k3_w;
        float k4_w = AngularAcceleration(theta + dt * k3_t, thetaDot + dt * k3_w, m);

        theta    += (dt / 6f) * (k1_t + 2f * k2_t + 2f * k3_t + k4_t);
        thetaDot += (dt / 6f) * (k1_w + 2f * k2_w + 2f * k3_w + k4_w);
    }

    // RK4 for Z-swing — reuses the same ODE (decoupled planar oscillation)
    private void RK4StepZ(float dt)
    {
        float m = CurrentMass;

        float k1_t = thetaZDot;
        float k1_w = AngularAcceleration(thetaZ, thetaZDot, m);

        float k2_t = thetaZDot + 0.5f * dt * k1_w;
        float k2_w = AngularAcceleration(thetaZ + 0.5f * dt * k1_t, thetaZDot + 0.5f * dt * k1_w, m);

        float k3_t = thetaZDot + 0.5f * dt * k2_w;
        float k3_w = AngularAcceleration(thetaZ + 0.5f * dt * k2_t, thetaZDot + 0.5f * dt * k2_w, m);

        float k4_t = thetaZDot + dt * k3_w;
        float k4_w = AngularAcceleration(thetaZ + dt * k3_t, thetaZDot + dt * k3_w, m);

        thetaZ    += (dt / 6f) * (k1_t + 2f * k2_t + 2f * k3_t + k4_t);
        thetaZDot += (dt / 6f) * (k1_w + 2f * k2_w + 2f * k3_w + k4_w);
    }

    // d²θ/dt² from the full pendulum ODE with damping and air drag
    // d²θ/dt² + b/(mL)·dθ/dt + g/L·sin(θ) = F_wind_tangential / (mL)
    private float AngularAcceleration(float th, float thDot, float mass)
    {
        float linearVel = thDot * ropeLength;
        float crossSection = Mathf.PI * bucketRadius * bucketRadius;

        // Air drag acts opposite to motion direction
        float dragMag = 0.5f * airDensity * dragCoefficient * crossSection * linearVel * Mathf.Abs(linearVel);
        float dragAngular = dragMag / (mass * ropeLength);

        // Damping from rope internal friction
        float damping = (dampingCoeff / (mass * ropeLength)) * thDot;

        // Gravity restoring torque
        float gravityTerm = (gravity / ropeLength) * Mathf.Sin(th);

        // Wind contribution (tangential component only, X axis)
        float windTangential = windForce.x * Mathf.Cos(th) - windForce.y * Mathf.Sin(th);
        float windAngular = windTangential / (mass * ropeLength);

        return -damping - gravityTerm - dragAngular + windAngular;
    }

    private void UpdateBucketTransform()
    {
        if (pivotPoint == null) return;

        // 3-D spherical pendulum position: stays on a sphere of radius L
        float sinX  = Mathf.Sin(theta);
        float sinZ  = Mathf.Sin(thetaZ);
        float cosY  = Mathf.Sqrt(Mathf.Max(0f, 1f - sinX * sinX - sinZ * sinZ));

        BucketPosition = pivotPoint.position + new Vector3(
            ropeLength * sinX,
            -ropeLength * cosY,
            ropeLength * sinZ);

        ropeDirection = (BucketPosition - pivotPoint.position).normalized;

        if (bucketTransform != null)
        {
            bucketTransform.position = BucketCenter;
            // Bucket local-Y aligns with the rope toward the pivot
            bucketTransform.rotation = Quaternion.FromToRotation(Vector3.up, -ropeDirection);
        }
    }

    private void UpdateRopeRenderer()
    {
        if (ropeRenderer == null || pivotPoint == null) return;
        ropeRenderer.SetPosition(0, pivotPoint.position);
        ropeRenderer.SetPosition(1, BucketPosition);
    }

    // Rope tension: T = mg cos(θ) + mv²/L
    public float GetRopeTension()
    {
        float v = thetaDot * ropeLength;
        return CurrentMass * (gravity * Mathf.Cos(theta) + v * v / ropeLength);
    }

    // Approximate period for small angles: T = 2π√(L/g)
    public float GetApproximatePeriod()
    {
        return 2f * Mathf.PI * Mathf.Sqrt(ropeLength / gravity);
    }
}
