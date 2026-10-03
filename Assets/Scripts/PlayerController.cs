using Unity.XR.CoreUtils;
using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    public CharacterController controller;
    public float speed = 12f;
    public float gravity = -9.81f;
    public float jumpHeight = 3f;
    public Transform vrCamera;
    [Tooltip("Rig that carries the camera (the XR Origin). Defaults to the camera's XR Origin. It follows the collider: " +
             "horizontally with movement, and vertically to the ground under the collider, so the camera rises and falls with " +
             "the ground (kerbs, steps) in both desktop and VR. Camera Offset only holds the eye height (see MouseLook).")]
    public Transform cameraRig;
    [Tooltip("Smoothing when the rig follows a change in ground height, e.g. stepping onto a kerb (s). 0 = snap.")]
    [Min(0f)] public float rigHeightSmoothTime = 0.08f;

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
        if (cameraRig == null && vrCamera != null)
        {
            XROrigin origin = vrCamera.GetComponentInParent<XROrigin>();
            cameraRig = origin != null ? origin.transform : vrCamera.parent;
        }
        FollowGroundHeight(snap: true);
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

        if (MovementEnabled)
            SyncColliderToHead();
        else
            previousCameraLocalPosition = vrCamera.localPosition;

        FollowGroundHeight(snap: false);
    }

    /// <summary>Height of the ground under the collider: the bottom of the capsule, less the controller's skin width.</summary>
    public float FeetHeight
    {
        get
        {
            float centreY = transform.TransformPoint(controller.center).y;
            float halfHeight = controller.height * 0.5f * Mathf.Abs(transform.lossyScale.y);
            return centreY - halfHeight - controller.skinWidth;
        }
    }

    /// <summary>
    /// Puts the rig's floor at the collider's feet, so the camera goes up and down with the ground. Rig height is set
    /// absolutely each frame (never accumulated from movement deltas), so it cannot drift: walking up then back down a
    /// step returns the camera to its original height.
    /// </summary>
    private void FollowGroundHeight(bool snap)
    {
        if (!HasSeparateRig() || controller == null)
            return;

        float target = FeetHeight;
        Vector3 position = cameraRig.position;
        if (snap || rigHeightSmoothTime <= 0f || Mathf.Abs(target - position.y) > headSyncWarpDistance)
            position.y = target;
        else
            position.y = Mathf.Lerp(position.y, target, 1f - Mathf.Exp(-Time.deltaTime / rigHeightSmoothTime));
        cameraRig.position = position;
    }

    private bool HasSeparateRig()
    {
        return cameraRig != null && cameraRig != transform && !cameraRig.IsChildOf(transform) && !transform.IsChildOf(cameraRig);
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

        // Turn the whole rig about the head (as MouseLook does for yaw), not Camera Offset, whose rotation MouseLook owns
        Transform rig = HasSeparateRig() ? cameraRig : (vrCamera.parent != null ? vrCamera.parent : transform);
        rig.RotateAround(vrCamera.position, Vector3.up, 180f);
        previousCameraLocalPosition = vrCamera.localPosition;
    }

    public void RequestFlipAroundHeadset()
    {
        flipPending = true;
    }

    /// <summary>Carries the rig along with the collider's horizontal movement; height is handled by <see cref="FollowGroundHeight"/>.</summary>
    private void MoveCameraRig(Vector3 movement)
    {
        if (!HasSeparateRig())
            return;

        movement.y = 0f;
        cameraRig.position += movement;
    }
}