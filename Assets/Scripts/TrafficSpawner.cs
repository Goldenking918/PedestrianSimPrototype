using UnityEngine;

public class TrafficSpawner : MonoBehaviour
{
    public GameObject carPrefab;
    public Transform[] waypoints;

    public float spawnInterval = 3f;
    [Tooltip("Minimum radius around spawner that must be clear to spawn a new car")]
    public float spawnClearRadius = 2f;
    [Tooltip("Minimum spawn interval (seconds)")]
    public float spawnIntervalMin = 1.0f;
    [Tooltip("Maximum spawn interval (seconds)")]
    public float spawnIntervalMax = 5.0f;

    [Tooltip("Minimum random speed assigned to spawned cars")]
    public float spawnCarMinSpeed = 6f;
    [Tooltip("Maximum random speed assigned to spawned cars")]
    public float spawnCarMaxSpeed = 12f;

    void Start()
    {
        StartCoroutine(SpawnLoop());
    }

    private System.Collections.IEnumerator SpawnLoop()
    {
        while (true)
        {
            float interval = spawnInterval;
            if (spawnIntervalMax > spawnIntervalMin)
            {
                interval = Random.Range(spawnIntervalMin, spawnIntervalMax);
            }

            SpawnCar();
            yield return new WaitForSeconds(interval);
        }
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
        {
            cm.waypoints = waypoints;
            // assign randomized speed
            if (spawnCarMaxSpeed > spawnCarMinSpeed)
                cm.speed = Random.Range(spawnCarMinSpeed, spawnCarMaxSpeed);
        }
    }
}