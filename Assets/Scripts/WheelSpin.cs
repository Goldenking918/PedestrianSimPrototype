using UnityEngine;

public class WheelSpin : MonoBehaviour
{
    [Tooltip("Wheel mesh transforms to rotate visually.")]
    public Transform[] wheels;

    [Tooltip("Wheel radius in meters.")]
    public float wheelRadius = 0.33f;

    [Tooltip("Local axis the wheel meshes rotate around. Usually X for car wheels.")]
    public Vector3 spinAxis = Vector3.right;

    [Tooltip("Set to true if the wheel rotation direction is backwards for your model.")]
    public bool reverseSpinDirection = false;

    private Vector3 lastPosition;

    void Start()
    {
        lastPosition = transform.position;
    }

    void LateUpdate()
    {
        if (wheels == null || wheels.Length == 0)
        {
            lastPosition = transform.position;
            return;
        }

        Vector3 delta = transform.position - lastPosition;
        lastPosition = transform.position;

        float forwardDistance = Vector3.Dot(delta, transform.forward);
        if (Mathf.Abs(forwardDistance) < 0.0001f)
            return;

        float circumference = 2f * Mathf.PI * Mathf.Max(wheelRadius, 0.0001f);
        float degrees = (forwardDistance / circumference) * 360f;
        if (reverseSpinDirection)
            degrees = -degrees;

        Vector3 axis = spinAxis.sqrMagnitude > 0f ? spinAxis.normalized : Vector3.right;

        for (int i = 0; i < wheels.Length; i++)
        {
            Transform wheel = wheels[i];
            if (wheel != null)
            {
                wheel.Rotate(axis, -degrees, Space.Self);
            }
        }
    }
}