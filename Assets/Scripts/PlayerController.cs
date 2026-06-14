using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    public CharacterController controller;
    public float speed = 12f;
    public float gravity = -9.81f;
    public float jumpHeight = 3f;

    Vector3 velocity;
    bool isGrounded;

    void Update()
    {
        // Check if character is touching the ground
        isGrounded = controller.isGrounded;

        if (isGrounded && velocity.y < 0)
        {
            velocity.y = -2f; // Reset gravity pull when grounded
        }

        // Get movement input (WASD / Arrow Keys)
        float x = Input.GetAxis("Horizontal");
        float z = Input.GetAxis("Vertical");

        // Calculate direction based on where the player is facing
        Vector3 move = transform.right * x + transform.forward * z;

        // Move the controller
        controller.Move(move * speed * Time.deltaTime);

        // Jump logic
        if (Input.GetButtonDown("Jump") && isGrounded)
        {
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }

        // Apply gravity over time
        velocity.y += gravity * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);
    }
}