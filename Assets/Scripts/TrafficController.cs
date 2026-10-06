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

    [Header("Traffic signals")]
    [Tooltip("Fixed-time signal plan computed from the scenario's flows (ITE clearance intervals, Webster cycle and splits).")]
    public SignalPlanSettings signalPlan = new SignalPlanSettings();
    [Tooltip("How drivers respond to signals (amber dilemma-zone rule, stop-line gap).")]
    public SignalResponseSettings signalResponse = new SignalResponseSettings();

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
    [Tooltip("At scenario start, fill the road with the cars that would be on it after this many seconds of traffic (s).")]
    [Min(0f)] public float prefillSeconds = 90f;
    [Tooltip("Desired speed (v0) range for spawned vehicles (m/s).")]
    [Min(0f)] public float spawnCarMinSpeed = 6f;
    [Min(0f)] public float spawnCarMaxSpeed = 12f;
    public bool logSpawns = false;

    [Header("Rigidbody vehicles")]
    [Min(0f)] public float vehicleMaxSpeed = 5f;
    [Min(0f)] public float vehicleReachThreshold = 0.5f;
    public TrafficSpawner[] trafficSpawners;
    private bool trafficPaused;
    private SignalController signalController;

    /// <summary>Plan currently running on the traffic lights (null if the scene's own light program is used).</summary>
    public SignalPlan CurrentSignalPlan => signalController != null ? signalController.CurrentPlan : null;
    public SignalController Signals => signalController;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("More than one TrafficController exists. Using the first one.", this);
            return;
        }

        Instance = this;

        // Take over the lights before the TrafficLightManager's Start launches its own program.
        if (signalPlan != null && signalPlan.enabled)
        {
            signalController = GetComponent<SignalController>();
            if (signalController == null)
                signalController = gameObject.AddComponent<SignalController>();
            if (!signalController.TakeControl())
            {
                Destroy(signalController);
                signalController = null;
            }
        }
    }

    private void Start()
    {
        // Lights run from the start with the default flows; each scenario restarts them with its own plan.
        ApplySignalPlan();
    }

    /// <summary>Expected arrivals per lane (each spawner feeds one lane) from the current spawn settings (veh/h).</summary>
    public float FlowPerLane => 3600f / TrafficArrivalModel.ExpectedHeadway(spawnIntervalMin, spawnIntervalMean, spawnIntervalMax, spawnInterval);

    /// <summary>
    /// Computes the fixed-time plan for the current flows and restarts the signal cycle at its first phase.
    /// Through flow = flow per lane; turning flow = flow per lane x turning share (only if any spawner has a turning route).
    /// </summary>
    public SignalPlan ApplySignalPlan()
    {
        if (signalController == null || !signalController.HasLights)
            return null;

        float flow = FlowPerLane;
        bool anyTurning = trafficSpawners != null && System.Array.Exists(trafficSpawners,
            sp => sp != null && sp.alternateWaypoints != null && sp.alternateWaypoints.Length > 0);
        float turnFlow = anyTurning ? flow * alternatePathChance : 0f;

        SignalPlan plan = SignalTiming.BuildPlan(signalPlan, signalController.PhaseOrder, flow, turnFlow);
        signalController.StartPlan(plan);
        signalController.Paused = trafficPaused;
        return plan;
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
        spawner.prefillSeconds = prefillSeconds;
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
        car.signalResponse = signalResponse;
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

        if (signalController != null)
            signalController.Paused = paused; // signal timing freezes with the traffic

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
