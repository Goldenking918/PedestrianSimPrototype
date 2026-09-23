using UnityEngine;
using HealthbarGames;

public class TrafficController : MonoBehaviour
{
    public static TrafficController Instance { get; private set; }

    [Header("Car movement")]
    [Min(0f)] public float carSpeed = 10f;
    [Min(0f)] public float carAcceleration = 6f;
    [Min(0f)] public float carDeceleration = 8f;
    [Min(0f)] public float carDetectionDistance = 3f;
    [Min(0f)] public float carFrontProbeOffset = 1.5f;
    [Min(0f)] public float carStopDistance = 1f;
    [Min(0f)] public float carMinimumStopGap = 2f;
    [Min(0f)] public float carMinimumFollowingSpeed = 0f;

    [Header("Spawning")]
    [Range(0f, 1f)] public float alternatePathChance = 0.25f;
    [Min(0f)] public float spawnInterval = 3f;
    [Min(0f)] public float spawnClearRadius = 2f;
    [Min(0f)] public float spawnIntervalMin = 1f;
    [Min(0f)] public float spawnIntervalMax = 5f;
    [Min(0f)] public float spawnCarMinSpeed = 6f;
    [Min(0f)] public float spawnCarMaxSpeed = 12f;

    [Header("Rigidbody vehicles")]
    [Min(0f)] public float vehicleMaxSpeed = 5f;
    [Min(0f)] public float vehicleReachThreshold = 0.5f;
    public TrafficSpawner[] trafficSpawners;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("More than one TrafficController exists. Using the first one.", this);
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void ApplyTo(TrafficSpawner spawner)
    {
        spawner.alternatePathChance = alternatePathChance;
        spawner.spawnInterval = spawnInterval;
        spawner.spawnClearRadius = spawnClearRadius;
        spawner.spawnIntervalMin = spawnIntervalMin;
        spawner.spawnIntervalMax = spawnIntervalMax;
        spawner.spawnCarMinSpeed = spawnCarMinSpeed;
        spawner.spawnCarMaxSpeed = spawnCarMaxSpeed;
    }

    public void ApplyTo(CarMovement car, bool preserveSpeed = false)
    {
        if (!preserveSpeed)
            car.speed = carSpeed;

        car.acceleration = carAcceleration;
        car.deceleration = carDeceleration;
        car.detectionDistance = carDetectionDistance;
        car.frontProbeOffset = carFrontProbeOffset;
        car.stopDistance = carStopDistance;
        car.minimumStopGap = carMinimumStopGap;
        car.minFollowingSpeed = carMinimumFollowingSpeed;
    }

    public void ApplyTo(VehicleController vehicle)
    {
        vehicle.MaxSpeed = vehicleMaxSpeed;
        vehicle.ReachThreshold = vehicleReachThreshold;
    }

    public static TrafficController FindController()
    {
        return Instance != null ? Instance : FindFirstObjectByType<TrafficController>();
    }
    public void ApplyToAllSpawners()
    {
        foreach (TrafficSpawner spawner in trafficSpawners)
        {
            ApplyTo(spawner);
        }

    }
    public void StartAllSpawners()
    {
        foreach (TrafficSpawner spawner in trafficSpawners)
    {
        if (spawner != null)
            spawner.StartSpawning();
    }
    }

    public void StopAllSpawners()
    {
        foreach (TrafficSpawner spawner in trafficSpawners)
        {
            if (spawner != null)
                spawner.StopSpawning();
        }
    }
}
