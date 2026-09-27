using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Waypoint-following vehicle. Longitudinal behaviour is layered:
///   1. Intelligent Driver Model (IDM) for free-flow driving and car following (<see cref="IntelligentDriverModel"/>).
///   2. Pedestrian conflict / yielding layer that can only lower the IDM acceleration (<see cref="PedestrianConflictModel"/>).
///   3. Hard safety envelope that stops the front bumper entering the pedestrian's space even if the behavioural layers
///      fail. Each time it engages it is logged as a warning and counted in <see cref="SafetyOverrideCount"/>.
/// Traffic-light stop lines still call <see cref="SetStopped"/> as before.
/// </summary>
public class CarMovement : MonoBehaviour
{
    static readonly List<CarMovement> sActive = new List<CarMovement>();
    static int sNextVehicleId = 1;

    /// <summary>All currently enabled vehicles (for car-following, spawning checks and instrumentation).</summary>
    public static IReadOnlyList<CarMovement> ActiveVehicles => sActive;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        sActive.Clear();
        sNextVehicleId = 1;
    }

    public Transform[] waypoints;
    public bool IsAlternateRoute { get; private set; }

    [Header("Driver (IDM)")]
    public IdmParameters idm = IdmParameters.UrbanDefault;
    [Tooltip("Physical braking limit of the vehicle (m/s^2). Caps both IDM and pedestrian braking.")]
    [Min(0.1f)] public float maxDeceleration = 8f;

    [Header("Car following")]
    [Tooltip("Vehicles further ahead along the path than this are ignored (m).")]
    [Min(0f)] public float leaderLookahead = 80f;
    [Tooltip("Max centre-to-path lateral offset for another vehicle to count as being in this vehicle's lane (m).")]
    [Min(0f)] public float leaderLateralTolerance = 1.5f;
    [Tooltip("Vehicles crossing or oncoming on this vehicle's path are only treated as obstacles within this gap (m).")]
    [Min(0f)] public float crossingVehicleDistance = 8f;

    [Header("Pedestrian interaction")]
    public PedestrianInteractionSettings pedestrianInteraction = new PedestrianInteractionSettings();

    [Header("Path following")]
    [Tooltip("Distance at which the next waypoint becomes the target (m).")]
    [Min(0.01f)] public float waypointReachThreshold = 1f;
    [Tooltip("Visual heading smoothing rate (1/s).")]
    [Min(0f)] public float turnRate = 5f;

    [Header("Debug")]
    public bool drawDebugGizmos = true;

    // ---- Runtime state (read-only, for debugging and future instrumentation) ----
    public int VehicleId { get; private set; }
    public float SpawnTime { get; private set; }
    /// <summary>Set by a spawner that has already applied per-vehicle parameters, so Start does not overwrite them.</summary>
    public bool ConfiguredBySpawner { get; set; }
    public VehicleGeometry Geometry { get; private set; } = VehicleGeometry.Default;
    public float CurrentSpeed { get; private set; }
    /// <summary>IDM car-following acceleration before the pedestrian layer (m/s^2).</summary>
    public float IdmAcceleration { get; private set; }
    /// <summary>Acceleration actually applied this step (m/s^2).</summary>
    public float AppliedAcceleration { get; private set; }
    /// <summary>Bumper-to-bumper gap to the leader (m); +inf if none.</summary>
    public float GapToLeader { get; private set; } = float.PositiveInfinity;
    public float LeaderSpeed { get; private set; }
    public PedestrianConflictAssessment PedestrianConflict { get; private set; } = PedestrianConflictAssessment.Empty;
    /// <summary>True when the pedestrian layer is more restrictive than IDM this step.</summary>
    public bool PedestrianConstraintActive { get; private set; }
    public bool SafetyOverrideActive { get; private set; }
    public int SafetyOverrideCount { get; private set; }
    /// <summary>Unit horizontal direction of travel.</summary>
    public Vector3 TravelDirection { get; private set; }

    readonly PedestrianConflictModel mConflictModel = new PedestrianConflictModel();
    Vector3[] mPath = new Vector3[8];
    int mPathCount;
    int mCurrentWaypoint;
    bool mIsStopped;
    bool mInitialised;
    Vector3 mMoveDirection;
    PedestrianPathObservation mPedestrian;

    void Awake() => EnsureInitialised();

    void OnEnable()
    {
        if (!sActive.Contains(this))
            sActive.Add(this);
    }

    void OnDisable() => sActive.Remove(this);

    void Start()
    {
        if (ConfiguredBySpawner)
            return;
        TrafficController controller = TrafficController.FindController();
        if (controller != null)
            controller.ApplyTo(this);
    }

    void Update() => Step(Time.deltaTime);

    public void SetAlternateRoute(bool isAlternateRoute) => IsAlternateRoute = isAlternateRoute;

    public void SetInitialSpeed(float speed) => CurrentSpeed = Mathf.Max(0f, speed);

    // Called by StopLine or other controllers to pause/resume movement
    public void SetStopped(bool stop)
    {
        mIsStopped = stop;
        if (stop)
            CurrentSpeed = 0f;
    }

    public bool IsStopped() => mIsStopped;

    void EnsureInitialised()
    {
        if (mInitialised)
            return;
        mInitialised = true;
        VehicleId = sNextVehicleId++;
        SpawnTime = Time.time;
        Geometry = MeasureGeometry(transform);
        TravelDirection = Flatten(transform.forward).normalized;
        mMoveDirection = transform.forward;
        if (!sActive.Contains(this))
            sActive.Add(this);
    }

    /// <summary>Advances the vehicle by one simulation step. Public so edit-mode tests can drive it deterministically.</summary>
    public void Step(float dt)
    {
        EnsureInitialised();
        if (dt <= 0f || waypoints == null || waypoints.Length == 0)
            return;

        while (mCurrentWaypoint < waypoints.Length &&
               Vector3.Distance(transform.position, waypoints[mCurrentWaypoint].position) < waypointReachThreshold)
            mCurrentWaypoint++;

        if (mCurrentWaypoint >= waypoints.Length)
        {
            if (Application.isPlaying) Destroy(gameObject);
            else DestroyImmediate(gameObject);
            return;
        }

        if (mIsStopped)
        {
            CurrentSpeed = 0f;
            AppliedAcceleration = 0f;
            return;
        }

        BuildPath();
        float v = CurrentSpeed;

        // 1. IDM car following
        FindLeader(out float gap, out float leaderSpeed);
        GapToLeader = gap;
        LeaderSpeed = leaderSpeed;
        IdmAcceleration = IntelligentDriverModel.Acceleration(v, gap, v - leaderSpeed, idm);

        // 2. Pedestrian conflict layer: may only lower the acceleration
        PedestrianTracker.EnsureSampled();
        mPedestrian = ObservePedestrian();
        PedestrianConflictState previousState = PedestrianConflict.state;
        PedestrianConflict = mConflictModel.Evaluate(mPedestrian, v, Mathf.Max(0f, IdmAcceleration), Geometry, idm, pedestrianInteraction, maxDeceleration);
        float acceleration = IdmAcceleration;
        PedestrianConstraintActive = PedestrianConflict.constraintAcceleration < acceleration;
        if (PedestrianConstraintActive)
            acceleration = PedestrianConflict.constraintAcceleration;
        acceleration = Mathf.Clamp(acceleration, -maxDeceleration, idm.maxAcceleration);

        // Ballistic integration that never reverses (Treiber & Kesting's recommended IDM update)
        float newSpeed = v + acceleration * dt;
        float displacement;
        if (newSpeed < 0f)
        {
            displacement = acceleration < 0f ? v * v / (-2f * acceleration) : 0f;
            newSpeed = 0f;
        }
        else
        {
            displacement = 0.5f * (v + newSpeed) * dt;
        }

        // 3. Hard safety envelope
        bool pedestrianOverride = false;
        float allowed = MaxSafeDisplacement(gap, ref pedestrianOverride);
        if (displacement > allowed)
        {
            displacement = Mathf.Max(0f, allowed);
            newSpeed = Mathf.Min(newSpeed, displacement / dt);
        }
        else
        {
            pedestrianOverride = false;
        }
        UpdateSafetyOverride(pedestrianOverride);

        AppliedAcceleration = (newSpeed - v) / dt;
        CurrentSpeed = newSpeed;
        MoveAlongPath(displacement, dt);

        if (pedestrianInteraction != null && pedestrianInteraction.logStateChanges && previousState != PedestrianConflict.state)
            LogConflictState(previousState);
    }

    void BuildPath()
    {
        int needed = waypoints.Length - mCurrentWaypoint + 1;
        if (mPath.Length < needed)
            mPath = new Vector3[needed];
        mPath[0] = transform.position;
        mPathCount = 1;
        for (int i = mCurrentWaypoint; i < waypoints.Length; i++)
            if (waypoints[i] != null)
                mPath[mPathCount++] = waypoints[i].position;
    }

    void FindLeader(out float gap, out float leaderSpeed)
    {
        gap = IntelligentDriverModel.NoLeader;
        leaderSpeed = 0f;
        float searchLength = leaderLookahead + Geometry.frontExtent + 10f;

        for (int i = 0; i < sActive.Count; i++)
        {
            CarMovement other = sActive[i];
            if (other == null || other == this)
                continue;
            if (!VehiclePath.TryProject(mPath, mPathCount, other.transform.position, searchLength, out var p))
                continue;
            if (p.along <= 0f || Mathf.Abs(p.lateral) > leaderLateralTolerance)
                continue;

            float bumperGap = p.along - Geometry.frontExtent - other.Geometry.rearExtent;
            if (bumperGap > leaderLookahead || bumperGap >= gap)
                continue;

            float alignment = Vector3.Dot(other.TravelDirection, p.tangent);
            if (alignment < 0.5f && bumperGap > crossingVehicleDistance)
                continue; // crossing / oncoming traffic only matters when it is physically in the way

            gap = bumperGap;
            leaderSpeed = Mathf.Max(0f, alignment) * other.CurrentSpeed;
        }
    }

    PedestrianPathObservation ObservePedestrian()
    {
        var obs = new PedestrianPathObservation();
        if (!PedestrianTracker.TryGet(out Vector3 position, out Vector3 velocity, out float radius))
            return obs;

        float searchLength = Geometry.frontExtent + (pedestrianInteraction != null ? pedestrianInteraction.lookaheadDistance : 60f) + radius;
        if (!VehiclePath.TryProject(mPath, mPathCount, position, searchLength, out var p))
            return obs;

        obs.present = true;
        obs.along = p.along - Geometry.frontExtent;
        obs.lateral = p.lateral;
        obs.alongSpeed = Vector3.Dot(velocity, p.tangent);
        obs.lateralSpeed = Vector3.Dot(velocity, p.right);
        obs.radius = radius;
        return obs;
    }

    /// <summary>Largest forward displacement this step that keeps clear of the leader and of a pedestrian in the path.</summary>
    float MaxSafeDisplacement(float gap, ref bool limitedByPedestrian)
    {
        float allowed = float.PositiveInfinity;
        if (!float.IsPositiveInfinity(gap))
            allowed = gap - 0.5f;

        if (mPedestrian.present && pedestrianInteraction != null)
        {
            bool inPhysicalPath = Mathf.Abs(mPedestrian.lateral) < Geometry.halfWidth + mPedestrian.radius;
            bool alongsideOrAhead = mPedestrian.along + mPedestrian.radius >= -Geometry.Length;
            if (inPhysicalPath && alongsideOrAhead)
            {
                float pedestrianLimit = mPedestrian.along - mPedestrian.radius >= 0f
                    ? mPedestrian.along - mPedestrian.radius - pedestrianInteraction.hardSafetyClearance
                    : 0f; // pedestrian is against the front or side of the vehicle: do not move
                if (pedestrianLimit < allowed)
                {
                    allowed = pedestrianLimit;
                    limitedByPedestrian = true;
                }
            }
        }
        return allowed;
    }

    void UpdateSafetyOverride(bool active)
    {
        if (active && !SafetyOverrideActive)
        {
            SafetyOverrideCount++;
            Debug.LogWarning($"[Vehicle {VehicleId}] Safety envelope override: behavioural braking was insufficient; " +
                             $"forced stop {mPedestrian.along:F2} m from pedestrian at {CurrentSpeed:F2} m/s.", this);
        }
        SafetyOverrideActive = active;
    }

    void MoveAlongPath(float displacement, float dt)
    {
        bool moved = false;
        while (displacement > 1e-5f && mCurrentWaypoint < waypoints.Length)
        {
            Vector3 toTarget = waypoints[mCurrentWaypoint].position - transform.position;
            float distance = toTarget.magnitude;
            if (distance < waypointReachThreshold)
            {
                mCurrentWaypoint++;
                continue;
            }

            Vector3 dir = toTarget / distance;
            float step = Mathf.Min(displacement, distance);
            transform.position += dir * step;
            displacement -= step;
            mMoveDirection = dir;
            moved = true;
        }

        Vector3 flat = Flatten(mMoveDirection);
        if (flat.sqrMagnitude > 1e-6f)
            TravelDirection = flat.normalized;

        if (moved && mMoveDirection.sqrMagnitude > 1e-6f)
            transform.rotation = Quaternion.Lerp(transform.rotation, Quaternion.LookRotation(mMoveDirection), turnRate * dt);
    }

    void LogConflictState(PedestrianConflictState previous)
    {
        var c = PedestrianConflict;
        Debug.Log($"[Vehicle {VehicleId}] Pedestrian conflict {previous} -> {c.state} | v={CurrentSpeed:F2} m/s " +
                  $"aIDM={IdmAcceleration:F2} aPed={Format(c.constraintAcceleration)} aApplied={AppliedAcceleration:F2} " +
                  $"TTC={Format(c.timeToConflict)} s aReq={Format(c.requiredDeceleration)} " +
                  $"tPedIn={Format(c.pedestrianEntryTime)} tPedOut={Format(c.pedestrianExitTime)} " +
                  $"tCarIn={Format(c.vehicleArrivalTime)} tCarOut={Format(c.vehicleClearTime)} dStop={Format(c.distanceToStopPoint)}", this);
    }

    static string Format(float value) => float.IsInfinity(value) ? "inf" : value.ToString("F2");

    static Vector3 Flatten(Vector3 v) => new Vector3(v.x, 0f, v.z);

    /// <summary>
    /// Measures the vehicle footprint from its non-trigger colliders, relative to <paramref name="root"/>'s pivot and axes.
    /// Box colliders are measured exactly (oriented box corners). Works on prefab assets as well as instances.
    /// </summary>
    public static VehicleGeometry MeasureGeometry(Transform root)
    {
        float front = float.NegativeInfinity, rear = float.PositiveInfinity, halfWidth = 0f;
        bool any = false;
        Vector3 origin = root.position, forward = root.forward, right = root.right;

        foreach (Collider col in root.GetComponentsInChildren<Collider>(true))
        {
            if (col.isTrigger)
                continue;
            if (col is BoxCollider box)
            {
                Vector3 half = box.size * 0.5f;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = box.center + new Vector3(
                        (i & 1) == 0 ? -half.x : half.x,
                        (i & 2) == 0 ? -half.y : half.y,
                        (i & 4) == 0 ? -half.z : half.z);
                    Accumulate(box.transform.TransformPoint(corner));
                }
            }
            else
            {
                Bounds b = col.bounds;
                for (int i = 0; i < 8; i++)
                    Accumulate(b.center + Vector3.Scale(b.extents, new Vector3(
                        (i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f)));
            }
        }

        if (!any)
            return VehicleGeometry.Default;
        return new VehicleGeometry { frontExtent = Mathf.Max(front, 0.1f), rearExtent = Mathf.Max(-rear, 0.1f), halfWidth = Mathf.Max(halfWidth, 0.1f) };

        void Accumulate(Vector3 worldPoint)
        {
            Vector3 rel = worldPoint - origin;
            float f = Vector3.Dot(rel, forward);
            front = Mathf.Max(front, f);
            rear = Mathf.Min(rear, f);
            halfWidth = Mathf.Max(halfWidth, Mathf.Abs(Vector3.Dot(rel, right)));
            any = true;
        }
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (!drawDebugGizmos || !Application.isPlaying)
            return;

        var c = PedestrianConflict;
        Color color = c.state switch
        {
            PedestrianConflictState.CriticalBraking => Color.red,
            PedestrianConflictState.Yielding => new Color(1f, 0.6f, 0f),
            PedestrianConflictState.VehiclePassesFirst => Color.cyan,
            PedestrianConflictState.PedestrianPassesFirst => Color.green,
            PedestrianConflictState.Monitoring => Color.yellow,
            _ => Color.white,
        };

        Vector3 front = transform.position + TravelDirection * Geometry.frontExtent + Vector3.up * 0.5f;
        Gizmos.color = color;
        float corridorLength = Mathf.Min(pedestrianInteraction != null ? pedestrianInteraction.lookaheadDistance : 30f, 30f);
        Gizmos.DrawLine(front, front + TravelDirection * corridorLength);
        if (!float.IsInfinity(c.distanceToStopPoint))
            Gizmos.DrawWireSphere(front + TravelDirection * Mathf.Max(0f, c.distanceToStopPoint), 0.3f);
        if (!float.IsPositiveInfinity(GapToLeader))
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawLine(front, front + TravelDirection * GapToLeader);
        }

        UnityEditor.Handles.Label(transform.position + Vector3.up * 2.5f,
            $"#{VehicleId} v={CurrentSpeed:F1} v0={idm.desiredSpeed:F1}\n" +
            $"aIDM={IdmAcceleration:F2} a={AppliedAcceleration:F2}\n" +
            $"{c.state} TTC={Format(c.timeToConflict)} aReq={Format(c.requiredDeceleration)}" +
            (SafetyOverrideActive ? "\nSAFETY OVERRIDE" : ""));
    }
#endif
}
