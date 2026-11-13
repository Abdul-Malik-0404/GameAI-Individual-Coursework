using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class PlayerController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform cameraTransform;   // drag Main Camera here (or leave empty)

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 6f;        // units per second
    [SerializeField] private float turnSpeed = 720f;      // degrees per second

    private Rigidbody rb;
    private Vector2 moveInput;                            // x = A/D, y = W/S
    private Vector3 lastFlatForward = Vector3.forward;    // fallback if camera looks straight up/down

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();

        // Lock ALL rotation from physics. We set the yaw ourselves via MoveRotation,
        // so collisions can never spin the cube.
        rb.constraints = RigidbodyConstraints.FreezeRotation;

        // Smooths the visual between physics steps (FixedUpdate runs at 50 Hz by default).
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;
    }

    private void Update()
    {
        // Input is read every frame so no key presses are missed.
        moveInput.x = Input.GetAxisRaw("Horizontal");
        moveInput.y = Input.GetAxisRaw("Vertical");

        // Stops diagonals (W+D) from being ~1.41x faster.
        moveInput = Vector2.ClampMagnitude(moveInput, 1f);
    }

    private void FixedUpdate()
    {
        // 1. Camera basis vectors, flattened onto the ground plane
        Vector3 camForward = cameraTransform.forward;
        camForward.y = 0f;

        if (camForward.sqrMagnitude < 0.0001f)
            camForward = lastFlatForward;                 // camera is looking straight up/down
        else
            camForward.Normalize();

        lastFlatForward = camForward;

        // Derived from the flat forward so it is guaranteed perpendicular to it.
        Vector3 camRight = Vector3.Cross(Vector3.up, camForward);

        // 2. Movement (camera-relative)
        Vector3 moveDir = camForward * moveInput.y + camRight * moveInput.x;
        Vector3 velocity = moveDir * moveSpeed;

        // Keep the physics-driven vertical speed so gravity/jumping still work.
        velocity.y = rb.linearVelocity.y;                 // Unity 6: linearVelocity (velocity is deprecated)
        rb.linearVelocity = velocity;

        // 3. Rotation: always face the camera's flat forward, even when pressing S
        Quaternion targetRotation = Quaternion.LookRotation(camForward, Vector3.up);
        Quaternion newRotation = Quaternion.RotateTowards(
            rb.rotation, targetRotation, turnSpeed * Time.fixedDeltaTime);

        rb.MoveRotation(newRotation);
    }
}
