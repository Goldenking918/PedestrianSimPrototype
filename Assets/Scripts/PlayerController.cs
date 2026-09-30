using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    public CharacterController controller;
    public float speed = 12f;
    public float gravity = -9.81f;
    public float jumpHeight = 3f;
    public Transform vrCamera;

    public bool MovementEnabled { get; private set; } = true;

    Vector3 velocity;
    bool isGrounded;

    [Tooltip("If the collider falls this far behind the headset (e.g. after being blocked), jump it straight to the headset (m).")]
    [Min(0.1f)] public float headSyncWarpDistance = 1.5f;

    private Vector3 previousCameraLocalPosition;
    private bool flipPending;

    void Start()
    {
        if (vrCamera != null)
            previousCameraLocalPosition = vrCamera.localPosition;
    }

    void Update()
    {
        if (!MovementEnabled)
            return;

        // Check if character is touching the ground
        isGrounded = controller.isGrounded;

        if (isGrounded && velocity.y < 0)
        {
            velocity.y = -2f;
        }

        // Joystick / keyboard movement
        float x = Input.GetAxis("Horizontal");
        float z = Input.GetAxis("Vertical");

        // Move relative to where the camera is facing (mouse look on desktop, head direction in VR)
        Vector3 forward = transform.forward;
        if (vrCamera != null)
        {
            Vector3 cameraForward = Vector3.ProjectOnPlane(vrCamera.forward, Vector3.up);
            if (cameraForward.sqrMagnitude > 1e-4f)
                forward = cameraForward.normalized;
        }
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        Vector3 move = Vector3.ClampMagnitude(right * x + forward * z, 1f);

        Vector3 joystickMovement = move * speed * Time.deltaTime;
        Vector3 positionBeforeMove = transform.position;
        controller.Move(joystickMovement);
        MoveCameraRig(transform.position - positionBeforeMove);

        // Jump
        if (Input.GetButtonDown("Jump") && isGrounded)
        {
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }

        // Gravity
        velocity.y += gravity * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);
    }

    void LateUpdate()
    {
        if (flipPending)
        {
            flipPending = false;
            FlipAroundHeadset();
        }

        if (vrCamera == null)
            return;

        if (!MovementEnabled)
        {
            previousCameraLocalPosition = vrCamera.localPosition;
            return;
        }

        SyncColliderToHead();
    }

    /// <summary>
    /// Keeps the collider directly under the camera (horizontal position only), so physically walking in VR moves the
    /// collider and it always matches where the participant really is. Absolute rather than frame-to-frame deltas, so the
    /// initial play-space offset and any movement the collider was blocked from making never build up into a permanent
    /// gap. In desktop mode the camera already follows the collider, so this is a no-op there.
    /// </summary>
    private void SyncColliderToHead()
    {
        if (vrCamera == null || vrCamera.IsChildOf(transform))
            return;

        Vector3 offset = vrCamera.position - transform.position;
        offset.y = 0f;
        if (offset.sqrMagnitude < 1e-8f)
            return;

        if (offset.magnitude > headSyncWarpDistance)
        {
            // Too far to sweep (start-up, or the head walked through an obstacle): place the collider directly
            controller.enabled = false;
            transform.position += offset;
            controller.enabled = true;
        }
        else
        {
            controller.Move(offset); // sweeps, so it respects colliders and catches up once clear
        }
    }

    public void SetMovementEnabled(bool enabled)
    {
        MovementEnabled = enabled;

        if (!enabled)
            velocity = Vector3.zero;
    }

    public void FlipAroundHeadset()
    {
        if (vrCamera == null)
            return;

        Transform cameraRig = vrCamera.parent != null ? vrCamera.parent : transform;
        cameraRig.RotateAround(vrCamera.position, Vector3.up, 180f);
        previousCameraLocalPosition = vrCamera.localPosition;
    }

    public void RequestFlipAroundHeadset()
    {
        flipPending = true;
    }

    private void MoveCameraRig(Vector3 movement)
    {
        if (vrCamera == null || vrCamera.parent == null || vrCamera.parent == transform || vrCamera.parent.IsChildOf(transform))
        {
            return;
        }

        vrCamera.parent.position += movement;
    }
}