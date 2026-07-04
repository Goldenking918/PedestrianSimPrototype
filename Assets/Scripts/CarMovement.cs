using UnityEngine;

public class CarMovement : MonoBehaviour
{
    public Transform[] waypoints;
    public float speed = 10f;
    [Tooltip("How quickly the car can accelerate back up to cruising speed (m/s^2)")]
    public float acceleration = 6f;
    [Tooltip("How quickly the car can slow down when another vehicle is ahead (m/s^2)")]
    public float deceleration = 8f;
    [Tooltip("Distance ahead to check for vehicles to avoid")]
    public float detectionDistance = 3.0f;
    [Tooltip("Forward offset from the car center to start the obstacle probe")]
    public float frontProbeOffset = 1.5f;
    [Tooltip("Distance at which vehicle will fully stop behind another vehicle")]
    public float stopDistance = 1.0f;
    [Tooltip("Minimum speed when following another vehicle (m/s)")]
    public float minFollowingSpeed = 0f;

    private int currentWaypoint = 0;
    private bool isStopped = false;
    private float currentSpeed;

    void Start()
    {
        currentSpeed = speed;
    }

    void Update()
    {
        if (isStopped)
            return;

        // simple forward obstacle check to avoid overlapping other cars
        float brakingDistance = stopDistance + (currentSpeed * currentSpeed) / (2f * Mathf.Max(deceleration, 0.01f)) + 0.5f;
        float lookaheadDistance = Mathf.Max(detectionDistance, brakingDistance);

        Vector3 probeOrigin = transform.position + Vector3.up * 0.5f + transform.forward * frontProbeOffset;
        Ray ray = new Ray(probeOrigin, transform.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, lookaheadDistance))
        {
            var player = hit.collider.GetComponentInParent<PlayerMovement>();
            if (player != null)
            {
                currentSpeed = Mathf.MoveTowards(currentSpeed, 0f, deceleration * Time.deltaTime);
                MoveTowards(currentSpeed);
                return;
            }

            var ahead = hit.collider.GetComponentInParent<CarMovement>();
            if (ahead != null && ahead != this)
            {
            float gap = Vector3.Distance(probeOrigin, hit.point) - stopDistance;
                float safeSpeed = gap <= 0f ? 0f : Mathf.Sqrt(2f * deceleration * gap);
                safeSpeed = Mathf.Clamp(safeSpeed, minFollowingSpeed, speed);

                currentSpeed = Mathf.MoveTowards(currentSpeed, safeSpeed, deceleration * Time.deltaTime);
                MoveTowards(currentSpeed);
                return;
            }
        }

        currentSpeed = Mathf.MoveTowards(currentSpeed, speed, acceleration * Time.deltaTime);

        if (currentWaypoint >= waypoints.Length)
        {
            Destroy(gameObject);
            return;
        }

        Transform target = waypoints[currentWaypoint];

        Vector3 direction = (target.position - transform.position).normalized;

        MoveTowards(currentSpeed);

        transform.rotation = Quaternion.Lerp(
            transform.rotation,
            Quaternion.LookRotation(direction),
            5f * Time.deltaTime
        );

        if (Vector3.Distance(transform.position, target.position) < 1f)
        {
            currentWaypoint++;
        }
    }

    private void MoveTowards(float currentSpeed)
    {
        Transform target = waypoints[currentWaypoint];
        Vector3 direction = (target.position - transform.position).normalized;
        transform.position += direction * currentSpeed * Time.deltaTime;
        transform.rotation = Quaternion.Lerp(
            transform.rotation,
            Quaternion.LookRotation(direction),
            5f * Time.deltaTime
        );
    }

    // Called by StopLine or other controllers to pause/resume movement
    public void SetStopped(bool stop)
    {
        isStopped = stop;
        if (stop)
            currentSpeed = 0f;
    }

    public bool IsStopped()
    {
        return isStopped;
    }
}
