using UnityEngine;

public class TrafficSpawner : MonoBehaviour
{
    
    [Tooltip("Car prefabs that can be randomly spawned")]
    public GameObject[] carPrefabs;
    public Transform[] waypoints;
    [Tooltip("Optional waypoint path for turning cars")]
    public Transform[] alternateWaypoints;
    [Range(0f, 1f)]
    [Tooltip("Chance that a spawned car uses the alternate path")]
    public float alternatePathChance = 0.25f;

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

    private Coroutine spawnCoroutine;

    void Start()
    {
        TrafficController controller = TrafficController.FindController();
        if (controller != null)
            controller.ApplyTo(this);
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
        if (carPrefabs == null || carPrefabs.Length == 0)
        {
            return;
        }

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

        GameObject carPrefab = carPrefabs[Random.Range(0, carPrefabs.Length)];
        if (carPrefab == null)
        {
            return;
        }

        GameObject car = Instantiate(carPrefab, transform.position, transform.rotation);
        var cm = car.GetComponent<CarMovement>();
        if (cm != null)
        {
            TrafficController controller = TrafficController.FindController();
            if (controller != null)
                controller.ApplyTo(cm);

            bool useAlternate = alternateWaypoints != null && alternateWaypoints.Length > 0 && Random.value < alternatePathChance;
            cm.waypoints = useAlternate ? alternateWaypoints : waypoints;
            cm.SetAlternateRoute(useAlternate);
            // assign randomized speed
            if (spawnCarMaxSpeed > spawnCarMinSpeed)
                cm.speed = Random.Range(spawnCarMinSpeed, spawnCarMaxSpeed);
        }
    }

    public void StartSpawning()
{
    if (spawnCoroutine == null)
    {
        spawnCoroutine = StartCoroutine(SpawnLoop());
    }
}

public void StopSpawning()
{
    if (spawnCoroutine != null)
    {
        StopCoroutine(spawnCoroutine);
        spawnCoroutine = null;
    }
}
}