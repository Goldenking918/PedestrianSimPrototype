using UnityEngine;

public class CarMovement : MonoBehaviour
{
    public Transform[] waypoints;
    public float speed = 10f;

    private int currentWaypoint = 0;

    void Update()
    {
        if (currentWaypoint >= waypoints.Length)
        {
            Destroy(gameObject);
            return;
        }

        Transform target = waypoints[currentWaypoint];

        Vector3 direction = (target.position - transform.position).normalized;

        transform.position += direction * speed * Time.deltaTime;

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
}
