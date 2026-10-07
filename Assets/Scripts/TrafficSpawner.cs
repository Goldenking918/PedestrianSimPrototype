using System.Collections;
using UnityEngine;

/// <summary>
/// Spawns vehicles onto a waypoint route with seeded, irregular arrivals (see <see cref="TrafficArrivalModel"/>)
/// and per-driver IDM variation. An arrival that finds the entry blocked by a vehicle or the pedestrian waits
/// until the entry clears; it is never dropped. The vehicle then enters at a speed that suits the gap ahead,
/// so it does not overlap or brake hard straight away.
///
/// Pre-fill: when spawning starts, the road is filled with the cars that would already be on it if this spawner had been
/// running for <see cref="prefillSeconds"/>, so traffic is flowing at the crossing from the start of a scenario (the spawn
/// points are far away, hidden in the haze). The pre-filled cars are the first cars of the same seeded plan, and live
/// spawning then carries on with that plan, so the same seed still gives the same traffic.
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
    [Tooltip("At start, fill the road with the cars that would be on it after this many seconds of traffic (s). 0 = start empty.")]
    [Min(0f)] public float prefillSeconds = 0f;

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
    public int PrefilledCount { get; private set; }

    const float EntryLateralTolerance = 1.5f;

    private Coroutine spawnCoroutine;
    private System.Random rng;
    private VehicleGeometry[] prefabGeometry;
    private Vector3[] pathBuffer = new Vector3[8];
    private bool wasSpawningBeforePause;
    private VehicleArrivalPlan currentPlan;   // the arrival the spawn loop is currently handling
    private bool currentPlanEntered;
    private float nextArrivalTime;            // when the next arrival is due (Time.time)
    private float waitLeftAtPause;            // how much of the wait for the next arrival was left when traffic paused

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
        PrefilledCount = 0;
        CachePrefabGeometry();
        if (carPrefabs == null || carPrefabs.Length == 0)
            return;

        float meanHeadway = TrafficArrivalModel.ExpectedHeadway(spawnIntervalMin, spawnIntervalMean, spawnIntervalMax, spawnInterval);
        Debug.Log($"[TrafficSpawner {name}] seed={randomSeed} stream={seedStream} (derived {seed}); headway min={spawnIntervalMin} " +
                  $"mean={spawnIntervalMean} max={spawnIntervalMax} (expected {meanHeadway:F2} s, ~{3600f / meanHeadway:F0} veh/h); " +
                  $"v0 {spawnCarMinSpeed}-{spawnCarMaxSpeed} m/s", this);

        VehicleArrivalPlan next = Prefill(out float firstDelay);
        spawnCoroutine = StartCoroutine(SpawnLoop(next, firstDelay));
    }

    /// <summary>
    /// Continues spawning after a pause (e.g. the midpoint turn-around) where the seeded plan left off. Unlike
    /// <see cref="StartSpawning"/> it does not restart the plan or pre-fill the road again.
    /// </summary>
    public void ResumeSpawning()
    {
        if (spawnCoroutine != null)
            return;
        if (rng == null)
        {
            StartSpawning();
            return;
        }
        // Carry on exactly where the pause interrupted: the arrival that had not entered yet (or, if it had, the next one),
        // after whatever was left of the wait for it, so the pause does not shorten the gap between cars
        spawnCoroutine = StartCoroutine(SpawnLoop(currentPlanEntered ? DrawPlan() : currentPlan, waitLeftAtPause));
    }

    public void StopSpawning()
    {
        if (spawnCoroutine != null)
        {
            StopCoroutine(spawnCoroutine);
            spawnCoroutine = null;
        }
    }

    /// <summary>Spawns <paramref name="plan"/> after <paramref name="delay"/> seconds, then keeps drawing the next arrivals.</summary>
    private IEnumerator SpawnLoop(VehicleArrivalPlan plan, float delay)
    {
        if (carPrefabs == null || carPrefabs.Length == 0)
            yield break;
        currentPlan = plan;          // recorded before any waiting, so a pause during the wait resumes this arrival
        currentPlanEntered = false;
        nextArrivalTime = Time.time + delay;
        if (delay > 0f)
            yield return new WaitForSeconds(delay);

        while (true)
        {
            currentPlan = plan;
            currentPlanEntered = false;

            // The arrival waits (rather than being skipped) until the entry is clear.
            float entrySpeed;
            while (!TryGetEntrySpeed(plan, out entrySpeed))
                yield return null;

            SpawnCar(plan, entrySpeed);
            currentPlanEntered = true;
            nextArrivalTime = Time.time + plan.headwayAfter;
            yield return new WaitForSeconds(plan.headwayAfter);
            plan = DrawPlan();
        }
    }

    VehicleArrivalPlan DrawPlan() => TrafficArrivalModel.DrawPlan(rng, carPrefabs.Length, alternatePathChance,
        alternateWaypoints != null && alternateWaypoints.Length > 0,
        spawnIntervalMin, spawnIntervalMean, spawnIntervalMax, spawnInterval,
        BaseIdm(), driverVariation, spawnCarMinSpeed, spawnCarMaxSpeed);

    /// <summary>
    /// Places the cars that would already be on the road after <see cref="prefillSeconds"/> of traffic, using the first
    /// arrivals of the seeded plan. Arrival k (entering at time t_k of the pre-fill period) has driven for
    /// (prefillSeconds - t_k) at its desired speed, but stays a safe following distance behind the car ahead of it; cars
    /// that would already have reached the end of their route are not placed. Returns the first arrival that falls after
    /// the pre-fill period, with how long to wait before it enters.
    /// </summary>
    VehicleArrivalPlan Prefill(out float firstDelay)
    {
        float t = 0f;
        var placed = new System.Collections.Generic.List<CarMovement>();
        while (true)
        {
            VehicleArrivalPlan plan = DrawPlan();
            if (t >= prefillSeconds)
            {
                firstDelay = t - prefillSeconds;
                if (PrefilledCount > 0)
                    Debug.Log($"[TrafficSpawner {name}] pre-filled {PrefilledCount} vehicles ({prefillSeconds:F0} s of traffic)", this);
                return plan;
            }
            if (carPrefabs[plan.prefabIndex] != null)
                PlacePrefilledCar(plan, prefillSeconds - t, placed);
            t += plan.headwayAfter;
        }
    }

    void PlacePrefilledCar(in VehicleArrivalPlan plan, float drivingTime, System.Collections.Generic.List<CarMovement> placed)
    {
        Transform[] route = RouteFor(plan);
        int count = BuildPath(route);
        var path = new Vector3[count];
        System.Array.Copy(pathBuffer, path, count);
        VehicleGeometry g = prefabGeometry[plan.prefabIndex];
        float speed = plan.idm.desiredSpeed;
        float distance = drivingTime * speed;

        // Stay a safe following distance (the IDM equilibrium gap) behind any earlier car ahead on this route
        foreach (CarMovement leader in placed)
        {
            if (leader == null || !VehiclePath.TryProject(path, count, leader.transform.position, float.PositiveInfinity, out var p))
                continue;
            if (Mathf.Abs(p.lateral) > EntryLateralTolerance || p.along <= 0f)
                continue;
            float followSpeed = Mathf.Min(speed, leader.CurrentSpeed);
            float maxDistance = p.along - leader.Geometry.rearExtent - g.frontExtent
                                - (plan.idm.minimumGap + followSpeed * plan.idm.timeHeadway);
            if (distance > maxDistance)
            {
                distance = maxDistance;
                speed = followSpeed;
            }
        }
        if (distance < 0f)
            return; // would still have been queuing to get onto the road

        // The point at that distance along the route
        for (int i = 0; i < count - 1; i++)
        {
            Vector3 segment = path[i + 1] - path[i];
            segment.y = 0f;
            float length = segment.magnitude;
            if (distance > length)
            {
                distance -= length;
                continue;
            }
            Vector3 position = path[i] + segment.normalized * distance;
            position.y = transform.position.y;
            GameObject car = Instantiate(carPrefabs[plan.prefabIndex], position, Quaternion.LookRotation(segment));
            var cm = car.GetComponent<CarMovement>();
            if (cm == null)
                return;
            TrafficController controller = TrafficController.FindController();
            if (controller != null)
                controller.ApplyTo(cm);
            cm.idm = plan.idm;
            cm.waypoints = route;
            cm.SetNextWaypoint(i); // path[i + 1] is route[i]
            cm.SetAlternateRoute(plan.useAlternateRoute);
            cm.SetInitialSpeed(speed);
            cm.ConfiguredBySpawner = true;
            placed.Add(cm);
            PrefilledCount++;
            return;
        }
        // Beyond the end of its route: this car would already have left the road
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
        // Face along the route (the spawner's own rotation does not necessarily point down the road)
        Quaternion rotation = transform.rotation;
        int count = BuildPath(RouteFor(plan));
        Vector3 heading = pathBuffer[1] - pathBuffer[0];
        heading.y = 0f;
        if (count > 1 && heading.sqrMagnitude > 1e-4f)
            rotation = Quaternion.LookRotation(heading);

        GameObject car = Instantiate(carPrefabs[plan.prefabIndex], transform.position, rotation);
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
    public void SetSpawningPaused(bool paused)
    {
        if (paused)
        {
            waitLeftAtPause = Mathf.Max(0f, nextArrivalTime - Time.time);
            wasSpawningBeforePause = spawnCoroutine != null;
            StopSpawning();
        }
        else if (wasSpawningBeforePause)
        {
            wasSpawningBeforePause = false;
            ResumeSpawning(); // continue the seeded plan; do not restart it or pre-fill again
        }
    }
}
