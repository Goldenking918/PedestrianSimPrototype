using UnityEngine;
using HealthbarGames;

public class TrafficController : MonoBehaviour
{
    public static TrafficController Instance { get; private set; }

    [Header("Reproducibility")]
    [Tooltip("Scenario random seed. Each spawner derives an independent stream from it (by its index in Traffic Spawners).")]
    public int randomSeed = 12345;

    [Header("Car behaviour: IDM (base driver)")]
    [Tooltip("Base IDM parameters. Desired speed is replaced per vehicle by the spawn speed range when that range is non-empty.")]
    public IdmParameters idm = IdmParameters.UrbanDefault;
    [Tooltip("Per-vehicle variation around the base IDM parameters.")]
    public DriverVariation driverVariation = new DriverVariation();
    [Tooltip("Physical braking limit of all vehicles (m/s^2).")]
    [Min(0.1f)] public float carMaxDeceleration = 8f;

    [Header("Car behaviour: pedestrian interaction")]
    public PedestrianInteractionSettings pedestrianInteraction = new PedestrianInteractionSettings();

    [Header("Car following")]
    [Min(0f)] public float carLeaderLookahead = 80f;
    [Min(0f)] public float carLeaderLateralTolerance = 1.5f;

    [Header("Spawning")]
    [Range(0f, 1f)] public float alternatePathChance = 0.25f;
    [Tooltip("Fixed headway used when spawnIntervalMax <= spawnIntervalMin (s).")]
    [Min(0f)] public float spawnInterval = 3f;
    [Tooltip("Minimum headway between arrivals at each spawner (s).")]
    [Min(0f)] public float spawnIntervalMin = 1f;
    [Tooltip("Maximum headway (truncation) at each spawner (s).")]
    [Min(0f)] public float spawnIntervalMax = 5f;
    [Tooltip("Mean headway before truncation (s). <= 0 uses the midpoint of min and max.")]
    [Min(0f)] public float spawnIntervalMean = 0f;
    [Tooltip("Minimum free space ahead of a spawn point before a vehicle may enter (m).")]
    [Min(0f)] public float minimumSpawnGap = 5f;
    [Tooltip("Desired speed (v0) range for spawned vehicles (m/s).")]
    [Min(0f)] public float spawnCarMinSpeed = 6f;
    [Min(0f)] public float spawnCarMaxSpeed = 12f;
    public bool logSpawns = false;

    [Header("Rigidbody vehicles")]
    [Min(0f)] public float vehicleMaxSpeed = 5f;
    [Min(0f)] public float vehicleReachThreshold = 0.5f;
    public TrafficSpawner[] trafficSpawners;
    private bool trafficPaused;

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
        spawner.spawnIntervalMin = spawnIntervalMin;
        spawner.spawnIntervalMax = spawnIntervalMax;
        spawner.spawnIntervalMean = spawnIntervalMean;
        spawner.minimumSpawnGap = minimumSpawnGap;
        spawner.spawnCarMinSpeed = spawnCarMinSpeed;
        spawner.spawnCarMaxSpeed = spawnCarMaxSpeed;
        spawner.driverVariation = driverVariation;
        spawner.randomSeed = randomSeed;
        spawner.logSpawns = logSpawns;

        int index = trafficSpawners != null ? System.Array.IndexOf(trafficSpawners, spawner) : -1;
        if (index >= 0)
            spawner.seedStream = index;
    }

    /// <summary>Applies shared vehicle settings. Per-vehicle IDM variation is applied afterwards by the spawner.</summary>
    public void ApplyTo(CarMovement car)
    {
        car.idm = idm;
        car.maxDeceleration = carMaxDeceleration;
        car.pedestrianInteraction = pedestrianInteraction;
        car.leaderLookahead = carLeaderLookahead;
        car.leaderLateralTolerance = carLeaderLateralTolerance;
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
            if (spawner != null)
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

    /// <summary>Destroys every active vehicle (used when a scenario is stopped or restarted). Returns how many were removed.</summary>
    public int ClearAllVehicles()
    {
        var vehicles = new System.Collections.Generic.List<CarMovement>(CarMovement.ActiveVehicles);
        foreach (CarMovement car in vehicles)
            if (car != null)
            {
                car.gameObject.SetActive(false); // leaves ActiveVehicles now, so restarted spawners see a clear road
                Destroy(car.gameObject);
            }
        return vehicles.Count;
    }

    public void SetTrafficPaused(bool paused)
    {
        if (trafficPaused == paused)
            return;

        trafficPaused = paused;

        foreach (TrafficSpawner spawner in trafficSpawners)
        {
            if (spawner != null)
                spawner.SetSpawningPaused(paused);
        }

        foreach (CarMovement car in FindObjectsByType<CarMovement>(FindObjectsSortMode.None))
            car.SetTrafficPaused(paused);

        foreach (VehicleController vehicle in FindObjectsByType<VehicleController>(FindObjectsSortMode.None))
            vehicle.SetTrafficPaused(paused);
    }
}
