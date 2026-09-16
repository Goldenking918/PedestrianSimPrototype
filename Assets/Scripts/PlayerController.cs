using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    public CharacterController controller;
    public float speed = 12f;
    public float gravity = -9.81f;
    public float jumpHeight = 3f;
    public Transform vrCamera;

    Vector3 velocity;
    bool isGrounded;

    private Vector3 previousCameraLocalPosition;

    void Start()
    {
        previousCameraLocalPosition = vrCamera.localPosition;
    }

    void Update()
    {
        // Check if character is touching the ground
        isGrounded = controller.isGrounded;

        if (isGrounded && velocity.y < 0)
        {
            velocity.y = -2f;
        }

        // Joystick / keyboard movement
        float x = Input.GetAxis("Horizontal");
        float z = Input.GetAxis("Vertical");

        Vector3 move = transform.right * x + transform.forward * z;

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
        // Detect physical movement of the VR headset
        Vector3 cameraMovement = vrCamera.localPosition - previousCameraLocalPosition;

        // Ignore vertical head movement
        cameraMovement.y = 0;
        Vector3 worldMovement = vrCamera.parent.TransformVector(cameraMovement);

        // Apply physical movement to the player
        controller.Move(worldMovement);

        previousCameraLocalPosition = vrCamera.localPosition;
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