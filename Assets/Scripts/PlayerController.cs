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

    // void Update()
    // {
    //     // Check if character is touching the ground
    //     isGrounded = controller.isGrounded;

    //     if (isGrounded && velocity.y < 0)
    //     {
    //         velocity.y = -2f;
    //     }

    //     // Joystick / keyboard movement
    //     float x = Input.GetAxis("Horizontal");
    //     float z = Input.GetAxis("Vertical");

    //     Vector3 move = transform.right * x + transform.forward * z;

    //     controller.Move(move * speed * Time.deltaTime);

    //     // Jump
    //     if (Input.GetButtonDown("Jump") && isGrounded)
    //     {
    //         velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
    //     }

    //     // Gravity
    //     velocity.y += gravity * Time.deltaTime;
    //     controller.Move(velocity * Time.deltaTime);
    // }

    void LateUpdate()
    {
        // Detect physical movement of the VR headset
        Vector3 cameraMovement = vrCamera.localPosition - previousCameraLocalPosition;

        // Ignore vertical head movement
        cameraMovement.y = 0;

        // Apply physical movement to the player
        controller.Move(cameraMovement);

        previousCameraLocalPosition = vrCamera.localPosition;
    }
}