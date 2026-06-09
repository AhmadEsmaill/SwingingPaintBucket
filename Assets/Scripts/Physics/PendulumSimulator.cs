using UnityEngine;

public class PendulumSimulator : MonoBehaviour
{
    [Header("Bucket Properties")]
    public float bucketMass = 0.5f;
    public float bucketRadius = 0.1f;
    public float bucketHalfHeight = 0.2f;  // half the visual bucket height (world units)

    [Header("Rope Properties")]
    public float ropeLength   = 1.5f;
    public float dampingCoeff = 0.05f;
    // 0 = rigid inextensible rope; >0 = elastic (spring-pendulum)
    public float ropeStiffness = 0f;   // N/m
    public float ropeDamping   = 2f;   // N·s/m (radial oscillation damping)

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
    private float thetaZ;
    private float thetaZDot;
    private float currentLength;     // actual rope length (= ropeLength when rigid)
    private float currentLengthDot;  // rate of change of rope length (m/s)
    private float currentPaintMass;
    private bool isSimulating;
    private Vector3 ropeDirection = Vector3.down;

    public float CurrentLength => currentLength;

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
        currentPaintMass = initialPaintMass;
        isSimulating = false;

        // swingDir controls the shape drawn on canvas:
        //   0°  → pure X displacement → horizontal line
        //   90° → pure Z velocity     → vertical line
        //   45° → X displacement + Z velocity in quadrature → perfect circle
        //
        // For circular motion X and Z must be 90° out of phase:
        //   theta(t)  = A·cosD · cos(ωt)        ← displacement sets cosine phase
        //   thetaZ(t) = A·sinD · sin(ωt)        ← velocity sets sine phase (quadrature)
        float dir    = initialForceAngle * Mathf.Deg2Rad;
        float cosD   = Mathf.Cos(dir);
        float sinD   = Mathf.Sin(dir);
        float omega  = Mathf.Sqrt(gravity / ropeLength);
        float angRad = initialAngleDeg * Mathf.Deg2Rad;

        theta  = angRad * cosD;
        thetaZ = 0f;

        float accel = initialForceMagnitude / (CurrentMass * ropeLength);
        thetaDot  = initialAngularVelocity + accel * cosD;
        thetaZDot = angRad * sinD * omega + accel * sinD;

        // Start rope at static equilibrium length so it doesn't lurch on first frame
        currentLength = (ropeStiffness > 0f)
            ? ropeLength + CurrentMass * gravity / ropeStiffness
            : ropeLength;
        currentLengthDot = 0f;

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
        if (ropeStiffness > 0f) RK4StepRadial(dt);
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

    // d²θ/dt² — uses currentLength so elastic rope automatically changes pendulum frequency
    private float AngularAcceleration(float th, float thDot, float mass)
    {
        float L         = currentLength;
        float linearVel = thDot * L;
        float crossSection = Mathf.PI * bucketRadius * bucketRadius;

        float dragMag     = 0.5f * airDensity * dragCoefficient * crossSection * linearVel * Mathf.Abs(linearVel);
        float dragAngular = dragMag / (mass * L);
        float damping     = (dampingCoeff / (mass * L)) * thDot;
        float gravityTerm = (gravity / L) * Mathf.Sin(th);

        // Coriolis coupling: -2*(Ḷ/L)*θ̇  (only present when rope is elastic)
        float coriolis = (ropeStiffness > 0f) ? 2f * (currentLengthDot / L) * thDot : 0f;

        float windTangential = windForce.x * Mathf.Cos(th) - windForce.y * Mathf.Sin(th);
        float windAngular    = windTangential / (mass * L);

        return -damping - gravityTerm - dragAngular + windAngular - coriolis;
    }

    // RK4 for the radial (elastic) DOF: integrates currentLength and currentLengthDot
    private void RK4StepRadial(float dt)
    {
        float m = CurrentMass;

        float k1_l = currentLengthDot;
        float k1_v = RadialAcceleration(currentLength, currentLengthDot, m);

        float k2_l = currentLengthDot + 0.5f * dt * k1_v;
        float k2_v = RadialAcceleration(currentLength + 0.5f * dt * k1_l, k2_l, m);

        float k3_l = currentLengthDot + 0.5f * dt * k2_v;
        float k3_v = RadialAcceleration(currentLength + 0.5f * dt * k2_l, k3_l, m);

        float k4_l = currentLengthDot + dt * k3_v;
        float k4_v = RadialAcceleration(currentLength + dt * k3_l, k4_l, m);

        currentLength    += (dt / 6f) * (k1_l + 2f * k2_l + 2f * k3_l + k4_l);
        currentLengthDot += (dt / 6f) * (k1_v + 2f * k2_v + 2f * k3_v + k4_v);

        currentLength = Mathf.Max(currentLength, 0.1f); // prevent collapse
    }

    // L̈ = L(θ̇²+θ_ż²) + g·cosY - (k/m)(L-L₀) - (c/m)·Ḷ
    private float RadialAcceleration(float L, float Ldot, float mass)
    {
        float sinX = Mathf.Sin(theta);
        float sinZ = Mathf.Sin(thetaZ);
        float cosY = Mathf.Sqrt(Mathf.Max(0f, 1f - sinX * sinX - sinZ * sinZ));

        float centrifugal = L * (thetaDot * thetaDot + thetaZDot * thetaZDot);
        float gravityComp = gravity * cosY;
        float spring      = -(ropeStiffness / mass) * (L - ropeLength);
        float damp        = -(ropeDamping   / mass) * Ldot;

        return centrifugal + gravityComp + spring + damp;
    }

    private void UpdateBucketTransform()
    {
        if (pivotPoint == null) return;

        // 3-D spherical pendulum position: stays on a sphere of radius L
        float sinX  = Mathf.Sin(theta);
        float sinZ  = Mathf.Sin(thetaZ);
        float cosY  = Mathf.Sqrt(Mathf.Max(0f, 1f - sinX * sinX - sinZ * sinZ));

        BucketPosition = pivotPoint.position + new Vector3(
            currentLength * sinX,
            -currentLength * cosY,
            currentLength * sinZ);

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

    public float GetRopeTension()
    {
        float v = thetaDot * currentLength;
        return CurrentMass * (gravity * Mathf.Cos(theta) + v * v / currentLength);
    }

    public float GetApproximatePeriod()
    {
        return 2f * Mathf.PI * Mathf.Sqrt(currentLength / gravity);
    }
}
