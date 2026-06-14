using UnityEngine;

public class TrafficSpawner : MonoBehaviour
{
    public GameObject carPrefab;
    public Transform[] waypoints;

    public float spawnInterval = 3f;
    [Tooltip("Minimum radius around spawner that must be clear to spawn a new car")]
    public float spawnClearRadius = 2f;

    void Start()
    {
        InvokeRepeating(nameof(SpawnCar), 0f, spawnInterval);
    }

    void SpawnCar()
    {
        // check for existing cars near spawn point
        Collider[] hits = Physics.OverlapSphere(transform.position, spawnClearRadius);
        foreach (var c in hits)
        {
            if (c.GetComponentInParent<CarMovement>() != null)
            {
                // skip spawn this time
                return;
            }
        }

        GameObject car = Instantiate(carPrefab, transform.position, transform.rotation);
        var cm = car.GetComponent<CarMovement>();
        if (cm != null)
            cm.waypoints = waypoints;
    }
}