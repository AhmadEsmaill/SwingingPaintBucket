using UnityEngine;

// Free-fly camera: WASD = move, Q/E = down/up, Right Mouse = look, Scroll = speed
public class CameraController : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed    = 5f;
    public float fastMultiplier = 3f;   // hold Left Shift for fast move
    public float scrollStep   = 1f;

    [Header("Look")]
    public float mouseSensitivity = 2f;

    private float yaw;
    private float pitch;

    void Start()
    {
        // Initialise yaw/pitch from current rotation so camera doesn't snap
        yaw   = transform.eulerAngles.y;
        pitch = transform.eulerAngles.x;
    }

    void Update()
    {
        HandleLook();
        HandleMovement();
        HandleSpeedScroll();
    }

    private void HandleLook()
    {
        // Rotate only while Right Mouse Button is held
        if (!Input.GetMouseButton(1)) return;

        yaw   += Input.GetAxis("Mouse X") * mouseSensitivity;
        pitch -= Input.GetAxis("Mouse Y") * mouseSensitivity;
        pitch  = Mathf.Clamp(pitch, -89f, 89f);

        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
    }

    private void HandleMovement()
    {
        float speed = moveSpeed * (Input.GetKey(KeyCode.LeftShift) ? fastMultiplier : 1f);
        float dt    = Time.deltaTime;

        Vector3 dir = Vector3.zero;

        // Forward / Backward
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))    dir += transform.forward;
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow))  dir -= transform.forward;

        // Left / Right
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) dir += transform.right;
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow))  dir -= transform.right;

        // Up / Down
        if (Input.GetKey(KeyCode.E)) dir += Vector3.up;
        if (Input.GetKey(KeyCode.Q)) dir -= Vector3.up;

        transform.position += dir.normalized * speed * dt;
    }

    private void HandleSpeedScroll()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.001f)
            moveSpeed = Mathf.Max(0.5f, moveSpeed + scroll * scrollStep * 10f);
    }
}
