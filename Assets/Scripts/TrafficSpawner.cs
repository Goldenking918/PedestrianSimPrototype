using System.Collections;
using UnityEngine;

/// <summary>
/// Spawns vehicles onto a waypoint route with seeded, irregular arrivals (see <see cref="TrafficArrivalModel"/>)
/// and per-driver IDM variation. An arrival that finds the entry blocked by a vehicle or the pedestrian waits
/// until the entry clears; it is never dropped. The vehicle then enters at a speed that suits the gap ahead,
/// so it does not overlap or brake hard straight away.
/// </summary>
public class TrafficSpawner : MonoBehaviour
{
    [Tooltip("Car prefabs that can be spawned")]
    public GameObject[] carPrefabs;
    public Transform[] waypoints;
    [Tooltip("Optional waypoint path for turning cars")]
    public Transform[] alternateWaypoints;
    [Range(0f, 1f)]
    [Tooltip("Chance that a spawned car uses the alternate path")]
    public float alternatePathChance = 0.25f;

    [Header("Arrivals (headway between spawns)")]
    [Tooltip("Fixed headway used when spawnIntervalMax <= spawnIntervalMin (s).")]
    public float spawnInterval = 3f;
    [Tooltip("Minimum headway between arrivals (s).")]
    public float spawnIntervalMin = 1.0f;
    [Tooltip("Maximum headway (s); the shifted-exponential distribution is truncated here.")]
    public float spawnIntervalMax = 5.0f;
    [Tooltip("Mean headway before truncation (s). <= 0 uses the midpoint of min and max.")]
    public float spawnIntervalMean = 0f;
    [Tooltip("Minimum free bumper-to-bumper space ahead of the spawn point before a vehicle may enter (m).")]
    [Min(0f)] public float minimumSpawnGap = 5f;

    [Header("Driver variation")]
    [Tooltip("Minimum desired speed v0 assigned to spawned cars (m/s)")]
    public float spawnCarMinSpeed = 6f;
    [Tooltip("Maximum desired speed v0 assigned to spawned cars (m/s)")]
    public float spawnCarMaxSpeed = 12f;
    public DriverVariation driverVariation = new DriverVariation();

    [Header("Reproducibility")]
    public int randomSeed = 12345;
    [Tooltip("Mixed into the seed so each spawner has its own reproducible sequence. Set by TrafficController from its spawner list order.")]
    public int seedStream = 0;
    public bool logSpawns = false;

    public int SpawnedCount { get; private set; }

    const float EntryLateralTolerance = 1.5f;

    private Coroutine spawnCoroutine;
    private System.Random rng;
    private VehicleGeometry[] prefabGeometry;
    private Vector3[] pathBuffer = new Vector3[8];

    void Start()
    {
        TrafficController controller = TrafficController.FindController();
        if (controller != null)
            controller.ApplyTo(this);
    }

    public void StartSpawning()
    {
        if (spawnCoroutine != null)
            return;

        int seed = TrafficArrivalModel.DeriveSeed(randomSeed, seedStream);
        rng = new System.Random(seed);
        SpawnedCount = 0;
        CachePrefabGeometry();

        float meanHeadway = TrafficArrivalModel.ExpectedHeadway(spawnIntervalMin, spawnIntervalMean, spawnIntervalMax, spawnInterval);
        Debug.Log($"[TrafficSpawner {name}] seed={randomSeed} stream={seedStream} (derived {seed}); headway min={spawnIntervalMin} " +
                  $"mean={spawnIntervalMean} max={spawnIntervalMax} (expected {meanHeadway:F2} s, ~{3600f / meanHeadway:F0} veh/h); " +
                  $"v0 {spawnCarMinSpeed}-{spawnCarMaxSpeed} m/s", this);

        spawnCoroutine = StartCoroutine(SpawnLoop());
    }

    public void StopSpawning()
    {
        if (spawnCoroutine != null)
        {
            StopCoroutine(spawnCoroutine);
            spawnCoroutine = null;
        }
    }

    private IEnumerator SpawnLoop()
    {
        if (carPrefabs == null || carPrefabs.Length == 0)
            yield break;

        while (true)
        {
            VehicleArrivalPlan plan = TrafficArrivalModel.DrawPlan(rng, carPrefabs.Length, alternatePathChance,
                alternateWaypoints != null && alternateWaypoints.Length > 0,
                spawnIntervalMin, spawnIntervalMean, spawnIntervalMax, spawnInterval,
                BaseIdm(), driverVariation, spawnCarMinSpeed, spawnCarMaxSpeed);

            // The arrival waits (rather than being skipped) until the entry is clear.
            float entrySpeed;
            while (!TryGetEntrySpeed(plan, out entrySpeed))
                yield return null;

            SpawnCar(plan, entrySpeed);
            yield return new WaitForSeconds(plan.headwayAfter);
        }
    }

    IdmParameters BaseIdm()
    {
        TrafficController controller = TrafficController.FindController();
        if (controller != null)
            return controller.idm;
        foreach (GameObject prefab in carPrefabs)
        {
            var cm = prefab != null ? prefab.GetComponent<CarMovement>() : null;
            if (cm != null)
                return cm.idm;
        }
        return IdmParameters.UrbanDefault;
    }

    Transform[] RouteFor(in VehicleArrivalPlan plan) => plan.useAlternateRoute ? alternateWaypoints : waypoints;

    void CachePrefabGeometry()
    {
        prefabGeometry = new VehicleGeometry[carPrefabs != null ? carPrefabs.Length : 0];
        for (int i = 0; i < prefabGeometry.Length; i++)
            prefabGeometry[i] = carPrefabs[i] != null ? CarMovement.MeasureGeometry(carPrefabs[i].transform) : VehicleGeometry.Default;
    }

    /// <summary>
    /// Entry check along the vehicle's route: enough free space ahead, nothing overlapping the spawn footprint,
    /// and the pedestrian not standing in the entry area.
    /// </summary>
    bool TryGetEntrySpeed(in VehicleArrivalPlan plan, out float entrySpeed)
    {
        entrySpeed = 0f;
        if (carPrefabs[plan.prefabIndex] == null)
            return false;

        Transform[] route = RouteFor(plan);
        int count = BuildPath(route);
        VehicleGeometry g = prefabGeometry[plan.prefabIndex];
        float gap = IntelligentDriverModel.NoLeader;
        float leaderSpeed = 0f;

        var active = CarMovement.ActiveVehicles;
        for (int i = 0; i < active.Count; i++)
        {
            CarMovement other = active[i];
            if (other == null || !VehiclePath.TryProject(pathBuffer, count, other.transform.position, 200f, out var p))
                continue;
            if (Mathf.Abs(p.lateral) > EntryLateralTolerance)
                continue;

            if (p.along < 0f)
            {
                // Vehicle behind the spawn point: only matters if it overlaps the new vehicle's footprint.
                if (-p.along - g.rearExtent - other.Geometry.frontExtent < 1f)
                    return false;
                continue;
            }

            float bumperGap = p.along - g.frontExtent - other.Geometry.rearExtent;
            if (bumperGap < gap)
            {
                gap = bumperGap;
                leaderSpeed = Mathf.Max(0f, Vector3.Dot(other.TravelDirection, p.tangent)) * other.CurrentSpeed;
            }
        }

        if (gap < minimumSpawnGap)
            return false;

        PedestrianTracker.EnsureSampled();
        if (PedestrianTracker.TryGet(out Vector3 pedPos, out _, out float radius)
            && VehiclePath.TryProject(pathBuffer, count, pedPos, 200f, out var pp)
            && Mathf.Abs(pp.lateral) < g.halfWidth + radius + 0.5f
            && pp.along > -g.rearExtent - radius
            && pp.along < g.frontExtent + radius + minimumSpawnGap)
            return false;

        entrySpeed = TrafficArrivalModel.ComputeEntrySpeed(gap, leaderSpeed, plan.idm);
        return true;
    }

    int BuildPath(Transform[] route)
    {
        int needed = (route != null ? route.Length : 0) + 1;
        if (pathBuffer.Length < needed)
            pathBuffer = new Vector3[needed];
        pathBuffer[0] = transform.position;
        int count = 1;
        if (route != null)
            foreach (Transform wp in route)
                if (wp != null)
                    pathBuffer[count++] = wp.position;
        if (count == 1)
            pathBuffer[count++] = transform.position + transform.forward * 100f;
        return count;
    }

    void SpawnCar(in VehicleArrivalPlan plan, float entrySpeed)
    {
        GameObject car = Instantiate(carPrefabs[plan.prefabIndex], transform.position, transform.rotation);
        var cm = car.GetComponent<CarMovement>();
        if (cm == null)
            return;

        TrafficController controller = TrafficController.FindController();
        if (controller != null)
            controller.ApplyTo(cm);

        cm.idm = plan.idm;
        cm.waypoints = RouteFor(plan);
        cm.SetAlternateRoute(plan.useAlternateRoute);
        cm.SetInitialSpeed(entrySpeed);
        cm.ConfiguredBySpawner = true;
        SpawnedCount++;

        if (logSpawns)
            Debug.Log($"[TrafficSpawner {name}] vehicle #{cm.VehicleId} ({carPrefabs[plan.prefabIndex].name}) alt={plan.useAlternateRoute} " +
                      $"v0={plan.idm.desiredSpeed:F1} T={plan.idm.timeHeadway:F2} a={plan.idm.maxAcceleration:F2} b={plan.idm.comfortableDeceleration:F2} " +
                      $"entry={entrySpeed:F1} m/s next headway={plan.headwayAfter:F2} s", this);
    }
}
