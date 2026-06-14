using UnityEngine;

public class CarMovement : MonoBehaviour
{
    public Transform[] waypoints;
    public float speed = 10f;
    [Tooltip("Distance ahead to check for vehicles to avoid")]
    public float detectionDistance = 3.0f;
    [Tooltip("Distance at which vehicle will fully stop behind another vehicle")]
    public float stopDistance = 1.0f;
    [Tooltip("Minimum speed when following another vehicle (m/s)")]
    public float minFollowingSpeed = 0f;

    private int currentWaypoint = 0;
    private bool isStopped = false;

    void Update()
    {
        if (isStopped)
            return;

        // simple forward obstacle check to avoid overlapping other cars
        Ray ray = new Ray(transform.position + Vector3.up * 0.5f, transform.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, detectionDistance))
        {
            var ahead = hit.collider.GetComponentInParent<CarMovement>();
            if (ahead != null && ahead != this)
            {
                float dist = Vector3.Distance(transform.position, hit.point);
                if (dist <= stopDistance)
                {
                    // stop immediately
                    return;
                }
                else
                {
                    // slow down proportionally based on distance
                    float t = Mathf.InverseLerp(detectionDistance, stopDistance, dist);
                    float currentSpeed = Mathf.Lerp(minFollowingSpeed, speed, t);
                    MoveTowards(currentSpeed);
                    return;
                }
            }
        }

        if (currentWaypoint >= waypoints.Length)
        {
            Destroy(gameObject);
            return;
        }

        Transform target = waypoints[currentWaypoint];

        Vector3 direction = (target.position - transform.position).normalized;

        MoveTowards(speed);

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
    }

    public bool IsStopped()
    {
        return isStopped;
    }
}
