using UnityEngine;

public class PendulumSimulator : MonoBehaviour
{
    [Header("Bucket Properties")]
    public float bucketMass = 0.5f;
    public float bucketRadius = 0.1f;
    public float bucketHalfHeight = 0.2f;  // half the visual bucket height (world units)

    public enum HandleAttachPoint { A_Center, B_Left, C_Right }
    [Header("Handle (bail) & rope attachment")]
    // Where the rope attaches on the bucket's bail: A = middle/apex of the arc
    // (balanced — the original behaviour), B = the left join with the bucket,
    // C = the right join. An off-centre attach makes the bucket hang tilted (its
    // centre of mass settles under the attach point), so it pours at an angle.
    public HandleAttachPoint handleAttach = HandleAttachPoint.A_Center;
    public float handleArcHeight = 0.12f;   // how high the bail arcs above the rim (m)

    [Header("Rope Properties")]
    public float ropeLength   = 1.5f;
    public float dampingCoeff = 0.05f;
    // 0 = rigid inextensible rope; >0 = elastic (spring-pendulum)
    public float ropeStiffness = 0f;   // N/m
    public float ropeDamping   = 2f;   // N·s/m (radial oscillation damping)

    [Header("Motion Properties")]
    public float initialAngleDeg = 45f;
    public float initialAngularVelocity = 0f;
    public float initialAngleDegZ = 15f;   // Z-axis initial tilt for 2D floor patterns

    [Header("Initial Force")]
    public float initialForceMagnitude = 0f;   // horizontal impulse kick (N·s)
    public float initialForceAngle = 0f;        // degrees: 0=+X, 90=+Z (horizontal plane)

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

    // Current state — full 3-D vector dynamics of a bob constrained near a sphere.
    // relPos/relVel are the bob position and velocity relative to the pivot.
    private Vector3 relPos = Vector3.down;
    private Vector3 relVel = Vector3.zero;
    private float   currentLength;      // |relPos|  (= ropeLength when rigid & settled)
    private float   currentLengthDot;   // radial speed (m/s), for elastic display/tension
    private float   currentPaintMass;
    private bool    isSimulating;
    private Vector3 ropeDirection = Vector3.down;

    // Derived bucket geometry (accounts for the handle tilt), refreshed each step.
    private Vector3 bucketCenterPos;
    private Vector3 bucketDownDir = Vector3.down;
    private Vector3 bucketHolePos;
    private LineRenderer handleRenderer;
    private const int HandleSegments = 20;

    public float CurrentLength => currentLength;

    // Public read-only properties used by PaintFlowController and others
    public float   Theta           => Vector3.Angle(Vector3.down, relPos) * Mathf.Deg2Rad;
    public float   AngularVelocity => Vector3.ProjectOnPlane(relVel, ropeDirection).magnitude
                                      / Mathf.Max(currentLength, 1e-4f);
    public float   CurrentMass     => bucketMass + currentPaintMass;
    public float   PaintMass       => currentPaintMass;
    public float   PaintFillRatio  => initialPaintMass > 0f ? currentPaintMass / initialPaintMass : 0f;
    public bool    IsSimulating    => isSimulating;
    // Rope attachment point on the bail (rope end / pendulum bob).
    public Vector3 BucketPosition  { get; private set; }
    // Centre of mass, hole position and the bucket's own downward axis. These
    // account for the handle tilt (for B/C the bucket hangs at an angle), so they
    // no longer lie straight along the rope.
    public Vector3 BucketCenter        => bucketCenterPos;
    public Vector3 BucketHolePosition  => bucketHolePos;
    public Vector3 BucketDownDirection => bucketDownDir;
    public Vector3 BucketVelocity  { get; private set; }
    public Vector3 RopeDirection   => ropeDirection;

    void Start()
    {
        EnsureHandleRenderer();
        Initialize();
    }

    public void Initialize()
    {
        currentPaintMass = initialPaintMass;
        isSimulating = false;

        // ── Release position ────────────────────────────────────────────────
        // Tilt the straight-down rope by initialAngleDeg about Z (→ swings in X)
        // then by initialAngleDegZ about X (→ swings in Z). Large angles are fine
        // because we work with the real 3-D direction, not a small-angle projection.
        Quaternion tiltX = Quaternion.AngleAxis(initialAngleDeg,  Vector3.forward);
        Quaternion tiltZ = Quaternion.AngleAxis(initialAngleDegZ, Vector3.right);
        Vector3 dir = (tiltZ * tiltX) * Vector3.down;
        dir = dir.sqrMagnitude > 1e-8f ? dir.normalized : Vector3.down;

        // Elastic rope hangs slightly longer at rest (static spring stretch).
        float L0 = (ropeStiffness > 0f)
            ? ropeLength + CurrentMass * gravity / ropeStiffness
            : ropeLength;
        relPos = dir * L0;

        // ── Release velocity ────────────────────────────────────────────────
        //  • initialAngularVelocity → tangential speed in the natural X-swing plane
        //  • initialForce → a horizontal impulse kick (Δv = J/m); its sideways
        //    (Z) component is what turns a flat back-and-forth line into an
        //    ellipse or, when tuned to ω·amplitude, a circle — exactly as a real
        //    spherical pendulum behaves.
        Vector3 rhat = relPos.normalized;

        Vector3 swingTangent = Vector3.ProjectOnPlane(Vector3.right, rhat);
        swingTangent = swingTangent.sqrMagnitude > 1e-8f ? swingTangent.normalized : Vector3.forward;
        Vector3 v = swingTangent * (initialAngularVelocity * L0);

        float ang = initialForceAngle * Mathf.Deg2Rad;
        Vector3 kickDir = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
        v += kickDir * (initialForceMagnitude / CurrentMass);

        // A rigid rope is inextensible, so the bob can't move radially at t=0.
        if (ropeStiffness <= 0f)
            v -= Vector3.Dot(v, rhat) * rhat;

        relVel = v;

        currentLength    = relPos.magnitude;
        currentLengthDot = 0f;

        UpdateBucketTransform();
        UpdateRopeRenderer();
    }

    public void StartSimulation() => isSimulating = true;
    public void StopSimulation() => isSimulating = false;
    public void ResetSimulation() => Initialize();

    void Update()
    {
        // While idle, refresh the bucket pose so handle / size changes show live.
        if (!isSimulating) UpdateBucketTransform();
        // Always update rope visual so it shows even before simulation starts
        UpdateRopeRenderer();
    }

    void FixedUpdate()
    {
        if (!isSimulating) return;

        float dt = Time.fixedDeltaTime;

        currentPaintMass = Mathf.Max(0f, currentPaintMass - paintFlowRate * dt);

        RK4Step(dt);

        currentLength    = relPos.magnitude;
        currentLengthDot = Vector3.Dot(relVel, relPos.normalized);
        BucketVelocity   = relVel;

        UpdateBucketTransform();
    }

    // RK4 over the coupled 3-D state (relPos, relVel).
    private void RK4Step(float dt)
    {
        float m = CurrentMass;
        Vector3 r = relPos, v = relVel;

        Vector3 k1r = v;
        Vector3 k1v = Acceleration(r, v, m);

        Vector3 k2r = v + 0.5f * dt * k1v;
        Vector3 k2v = Acceleration(r + 0.5f * dt * k1r, v + 0.5f * dt * k1v, m);

        Vector3 k3r = v + 0.5f * dt * k2v;
        Vector3 k3v = Acceleration(r + 0.5f * dt * k2r, v + 0.5f * dt * k2v, m);

        Vector3 k4r = v + dt * k3v;
        Vector3 k4v = Acceleration(r + dt * k3r, v + dt * k3v, m);

        relPos += (dt / 6f) * (k1r + 2f * k2r + 2f * k3r + k4r);
        relVel += (dt / 6f) * (k1v + 2f * k2v + 2f * k3v + k4v);

        // Rigid rope: re-project onto the sphere to kill RK4 numerical drift, and
        // strip any residual radial velocity so the constraint stays satisfied.
        if (ropeStiffness <= 0f && relPos.sqrMagnitude > 1e-8f)
        {
            relPos = relPos.normalized * ropeLength;
            Vector3 rhat = relPos.normalized;
            relVel -= Vector3.Dot(relVel, rhat) * rhat;
        }
    }

    // Bob acceleration (m/s²). External forces are gravity + quadratic air drag +
    // linear internal/pivot damping + wind. The rope then either enforces a rigid
    // spherical constraint (Lagrange tension) or acts as a damped spring.
    private Vector3 Acceleration(Vector3 r, Vector3 v, float mass)
    {
        float L = r.magnitude;
        if (L < 1e-5f) return Vector3.zero;
        Vector3 rhat = r / L;

        float crossSection = Mathf.PI * bucketRadius * bucketRadius;
        float speed        = v.magnitude;

        Vector3 gravityForce = new Vector3(0f, -gravity * mass, 0f);
        Vector3 dragForce    = -0.5f * airDensity * dragCoefficient * crossSection * speed * v;
        Vector3 dampForce    = -dampingCoeff * v;              // linear internal damping
        Vector3 aExt = (gravityForce + dragForce + dampForce + windForce) / mass;

        if (ropeStiffness > 0f)
        {
            // Elastic rope: spring pull toward rest length + radial damping.
            float   stretch     = L - ropeLength;
            float   radialVel   = Vector3.Dot(v, rhat);
            Vector3 ropeForce   = (-ropeStiffness * stretch - ropeDamping * radialVel) * rhat;
            return aExt + ropeForce / mass;
        }

        // Rigid rope: choose the radial tension that keeps |r| constant.
        //   r·a = -|v|²   ⇒   a = aExt + μ·r,  μ = -(|v|² + r·aExt)/L²
        // Clamp μ so the rope can only pull (tension ≥ 0), never push.
        float mu = -(Vector3.Dot(v, v) + Vector3.Dot(r, aExt)) / (L * L);
        mu = Mathf.Min(mu, 0f);
        return aExt + mu * r;
    }

    private void UpdateBucketTransform()
    {
        if (pivotPoint == null) return;

        BucketPosition = pivotPoint.position + relPos;
        ropeDirection  = relPos.sqrMagnitude > 1e-8f ? relPos.normalized : Vector3.down;

        // Orientation: align the bucket's up with the rope, then tilt by the handle
        // offset so an off-centre attach (B/C) makes the bucket hang at an angle.
        Quaternion baseRot   = Quaternion.FromToRotation(Vector3.up, -ropeDirection);
        Quaternion bucketRot = baseRot * Quaternion.AngleAxis(HandleTiltDeg(), Vector3.forward);

        // Geometry from the attach point: centre of mass sits under the attach point,
        // and the hole is at the bucket's bottom — both in the tilted bucket frame.
        bucketDownDir   = bucketRot * Vector3.down;
        Vector2 a       = AttachLocal();
        bucketCenterPos = BucketPosition + bucketRot * new Vector3(-a.x, -a.y, 0f);
        bucketHolePos   = bucketCenterPos + bucketDownDir * bucketHalfHeight;

        if (bucketTransform != null)
        {
            bucketTransform.position = bucketCenterPos;
            bucketTransform.rotation = bucketRot;
        }

        UpdateHandleRenderer(bucketRot);
    }

    // Attachment point on the bucket in local space (x = right, y = up from centre).
    private Vector2 AttachLocal()
    {
        switch (handleAttach)
        {
            case HandleAttachPoint.B_Left:  return new Vector2(-bucketRadius, bucketHalfHeight);
            case HandleAttachPoint.C_Right: return new Vector2( bucketRadius, bucketHalfHeight);
            default:                        return new Vector2(0f, bucketHalfHeight + handleArcHeight);
        }
    }

    // Extra tilt of the bucket relative to the rope: it rotates until its centre of
    // mass hangs directly under the attach point (static equilibrium). 0 for centre.
    private float HandleTiltDeg()
    {
        Vector2 a = AttachLocal();
        return Mathf.Atan2(a.x, a.y) * Mathf.Rad2Deg;
    }

    private void EnsureHandleRenderer()
    {
        if (handleRenderer != null) return;
        var go = new GameObject("BucketHandle");
        handleRenderer = go.AddComponent<LineRenderer>();
        handleRenderer.useWorldSpace  = true;
        handleRenderer.positionCount  = HandleSegments + 1;
        handleRenderer.startWidth     = 0.02f;
        handleRenderer.endWidth       = 0.02f;
        handleRenderer.numCapVertices = 2;
        handleRenderer.sharedMaterial =
            new Material(Shader.Find("Unlit/Color")) { color = new Color(0.85f, 0.85f, 0.9f) };
    }

    // Draws the bail as an arc from the left join (B) up over the apex (A) to the
    // right join (C), in the (possibly tilted) bucket frame.
    private void UpdateHandleRenderer(Quaternion bucketRot)
    {
        if (handleRenderer == null) return;
        float r = bucketRadius, h = bucketHalfHeight, arc = handleArcHeight;
        for (int i = 0; i <= HandleSegments; i++)
        {
            float t  = (float)i / HandleSegments;            // 0 = left join, 1 = right join
            float lx = Mathf.Lerp(-r, r, t);
            float ly = h + arc * Mathf.Sin(t * Mathf.PI);    // apex in the middle, 0 at ends
            handleRenderer.SetPosition(i, bucketCenterPos + bucketRot * new Vector3(lx, ly, 0f));
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
        // Radial balance: T = m(g·cosθ + v_t²/L), θ measured from straight down.
        float L        = Mathf.Max(currentLength, 1e-4f);
        float cosTheta = Vector3.Dot(ropeDirection, Vector3.down);
        float vt       = AngularVelocity * L;
        return CurrentMass * (gravity * cosTheta + vt * vt / L);
    }

    public float GetApproximatePeriod()
    {
        return 2f * Mathf.PI * Mathf.Sqrt(currentLength / gravity);
    }
}
