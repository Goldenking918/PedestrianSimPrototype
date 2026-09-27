using System;
using UnityEngine;

/// <summary>Driver's current reading of the pedestrian interaction (for debugging / instrumentation).</summary>
public enum PedestrianConflictState
{
    /// <summary>No pedestrian relevant to this vehicle (absent, behind, or beyond look-ahead).</summary>
    None,
    /// <summary>Pedestrian within look-ahead but not predicted to enter the vehicle's path.</summary>
    Monitoring,
    /// <summary>Pedestrian will enter the path, but the vehicle clears the conflict area first with margin.</summary>
    VehiclePassesFirst,
    /// <summary>Pedestrian will clear the path before the vehicle arrives, with margin.</summary>
    PedestrianPassesFirst,
    /// <summary>Arrival times overlap: vehicle is yielding (braking constraint active).</summary>
    Yielding,
    /// <summary>Yielding requires more than comfortable deceleration.</summary>
    CriticalBraking,
}

[Serializable]
public class PedestrianInteractionSettings
{
    [Tooltip("Enable the pedestrian conflict / yielding layer on top of IDM.")]
    public bool enabled = true;

    [Tooltip("Pedestrians further ahead along the vehicle's path than this are ignored (m).")]
    [Min(0f)] public float lookaheadDistance = 60f;

    [Tooltip("Predicted path entries further in the future than this are ignored (s).")]
    [Min(0f)] public float predictionHorizon = 8f;

    [Tooltip("Extra lateral clearance added to vehicle half-width + pedestrian radius to form the conflict corridor (m).")]
    [Min(0f)] public float lateralSafetyMargin = 0.5f;

    [Tooltip("Distance the vehicle aims to stop short of the pedestrian when yielding (m).")]
    [Min(0f)] public float stopBuffer = 2f;

    [Tooltip("Pedestrian lateral speeds below this are treated as standing still (m/s).")]
    [Min(0f)] public float minimumPedestrianSpeed = 0.25f;

    [Tooltip("Vehicle proceeds ahead of the pedestrian only if it clears the conflict area at least this long before the pedestrian arrives (s).")]
    [Min(0f)] public float vehicleFirstMargin = 1.5f;

    [Tooltip("Vehicle keeps going if the pedestrian clears the path at least this long before the vehicle arrives (s).")]
    [Min(0f)] public float pedestrianFirstMargin = 1f;

    [Tooltip("Once yielding, the pedestrian must move this much further outside the corridor before the vehicle resumes (m). Prevents on/off braking.")]
    [Min(0f)] public float releaseHysteresis = 0.3f;

    [Tooltip("Hard safety envelope: the front bumper never moves closer than this to a pedestrian in its path (m). Overrides the behavioural model if ever needed.")]
    [Min(0f)] public float hardSafetyClearance = 0.5f;

    [Tooltip("Log conflict state transitions to the console.")]
    public bool logStateChanges = false;
}

/// <summary>Vehicle footprint relative to its transform pivot, measured along its own axes (m).</summary>
[Serializable]
public struct VehicleGeometry
{
    public float frontExtent;
    public float rearExtent;
    public float halfWidth;

    public float Length => frontExtent + rearExtent;

    public static VehicleGeometry Default => new VehicleGeometry { frontExtent = 2.3f, rearExtent = 2.3f, halfWidth = 0.9f };
}

/// <summary>
/// Pedestrian expressed in the vehicle's path frame, relative to the vehicle's front bumper.
/// </summary>
public struct PedestrianPathObservation
{
    public bool present;
    /// <summary>Distance along the vehicle path from the front bumper to the pedestrian centre (m, positive ahead).</summary>
    public float along;
    /// <summary>Signed lateral offset of the pedestrian centre from the path centreline (m).</summary>
    public float lateral;
    /// <summary>Pedestrian velocity component along the path direction (m/s).</summary>
    public float alongSpeed;
    /// <summary>Pedestrian velocity component across the path, same sign convention as <see cref="lateral"/> (m/s).</summary>
    public float lateralSpeed;
    public float radius;
}

public struct PedestrianConflictAssessment
{
    public PedestrianConflictState state;
    /// <summary>Pedestrian currently inside the conflict corridor ahead of the vehicle.</summary>
    public bool pedestrianInCorridor;
    /// <summary>Predicted time until the pedestrian enters the corridor (s, 0 if inside, +inf if not heading in).</summary>
    public float pedestrianEntryTime;
    /// <summary>Predicted time until the pedestrian leaves the corridor on the far side (s, +inf if standing in it).</summary>
    public float pedestrianExitTime;
    /// <summary>Predicted time until the front bumper reaches the conflict area (s).</summary>
    public float vehicleArrivalTime;
    /// <summary>Predicted time until the rear bumper has left the conflict area at current speed (s).</summary>
    public float vehicleClearTime;
    /// <summary>
    /// Time-to-conflict: time for the front bumper to reach the pedestrian's conflict point at the current speed.
    /// Only reported while yielding (a conflict is predicted); +inf otherwise.
    /// </summary>
    public float timeToConflict;
    /// <summary>Available distance before the yield stop point (m).</summary>
    public float distanceToStopPoint;
    /// <summary>Kinematic deceleration needed to stop at the stop point: v^2 / (2d) (m/s^2, positive).</summary>
    public float requiredDeceleration;
    /// <summary>Acceleration cap imposed by the pedestrian layer (m/s^2); +inf when no constraint.</summary>
    public float constraintAcceleration;

    public static PedestrianConflictAssessment Empty => new PedestrianConflictAssessment
    {
        state = PedestrianConflictState.None,
        pedestrianEntryTime = float.PositiveInfinity,
        pedestrianExitTime = float.PositiveInfinity,
        vehicleArrivalTime = float.PositiveInfinity,
        vehicleClearTime = float.PositiveInfinity,
        timeToConflict = float.PositiveInfinity,
        distanceToStopPoint = float.PositiveInfinity,
        requiredDeceleration = 0f,
        constraintAcceleration = float.PositiveInfinity,
    };
}

/// <summary>
/// Pedestrian conflict / yielding layer. It runs on top of IDM and only ever lowers the vehicle's acceleration.
///
/// Each step:
///   1. Build a conflict corridor along the vehicle path: half-width = vehicle half-width + pedestrian radius + margin.
///   2. Predict when the pedestrian enters and leaves that corridor from their current lateral position and speed.
///   3. Predict when the vehicle reaches and clears the conflict point. Arrival is estimated early (vehicle may speed up)
///      and clearing late (current speed), so both checks lean conservative.
///   4. Gap acceptance: continue with plain IDM if the vehicle clears first with <see cref="PedestrianInteractionSettings.vehicleFirstMargin"/>,
///      or the pedestrian clears first with <see cref="PedestrianInteractionSettings.pedestrianFirstMargin"/>. Otherwise yield.
///   5. Yielding applies the kinematic required deceleration a = -v^2 / (2d), with d the distance to a stop point
///      <see cref="PedestrianInteractionSettings.stopBuffer"/> short of the pedestrian. This is progressive by construction:
///      a conflict far away gives light braking (e.g. 12 m/s, 50 m: ~1.5 m/s^2), a close one gives firm braking, capped at
///      the vehicle's physical limit. The final acceleration is min(IDM, this), so it only takes over when it is more restrictive.
///      (Treating the pedestrian as a virtual standing IDM leader was rejected: the v*T headway term brakes far harder
///      than necessary for a stationary conflict point.)
///   6. Once yielding, the vehicle keeps yielding until the pedestrian has left a slightly widened corridor (hysteresis).
///      Then IDM takes over again and the vehicle pulls away smoothly.
/// </summary>
public sealed class PedestrianConflictModel
{
    const float Epsilon = 0.01f;
    const float CrawlSpeed = 0.5f;

    /// <summary>True while the vehicle has committed to yielding to the pedestrian.</summary>
    public bool IsYielding { get; private set; }

    public void Reset() => IsYielding = false;

    /// <param name="speed">Current vehicle speed (m/s).</param>
    /// <param name="projectedAcceleration">Acceleration the vehicle would otherwise apply (&gt;= 0 used), for arrival-time prediction.</param>
    /// <param name="maxDeceleration">Physical braking limit of the vehicle (m/s^2, positive).</param>
    public PedestrianConflictAssessment Evaluate(
        in PedestrianPathObservation ped,
        float speed,
        float projectedAcceleration,
        in VehicleGeometry geometry,
        in IdmParameters idm,
        PedestrianInteractionSettings settings,
        float maxDeceleration)
    {
        var result = PedestrianConflictAssessment.Empty;

        if (settings == null || !settings.enabled || !ped.present
            || ped.along + ped.radius < 0f                                  // pedestrian is behind the front bumper
            || ped.along - ped.radius > settings.lookaheadDistance)         // too far ahead to matter
        {
            IsYielding = false;
            return result;
        }

        result.state = PedestrianConflictState.Monitoring;
        float v = Mathf.Max(speed, 0f);

        // --- 1-2. Pedestrian timing relative to the conflict corridor ---
        float corridorHalfWidth = geometry.halfWidth + ped.radius + settings.lateralSafetyMargin;
        float absLateral = Mathf.Abs(ped.lateral);
        float lateralSpeed = Mathf.Abs(ped.lateralSpeed);
        bool moving = lateralSpeed >= settings.minimumPedestrianSpeed;
        bool headingIn = moving && ped.lateral * ped.lateralSpeed <= 0f;
        bool inside = absLateral < corridorHalfWidth;
        bool heldByHysteresis = IsYielding && absLateral < corridorHalfWidth + settings.releaseHysteresis;

        float entryTime, exitTime;
        if (inside)
        {
            entryTime = 0f;
            // Distance to the far edge in the direction the pedestrian is walking.
            exitTime = moving ? (corridorHalfWidth - ped.lateral * Mathf.Sign(ped.lateralSpeed)) / lateralSpeed : float.PositiveInfinity;
        }
        else if (headingIn)
        {
            entryTime = (absLateral - corridorHalfWidth) / lateralSpeed;
            exitTime = (absLateral + corridorHalfWidth) / lateralSpeed;
        }
        else
        {
            entryTime = float.PositiveInfinity;
            exitTime = float.PositiveInfinity;
        }

        result.pedestrianInCorridor = inside;
        result.pedestrianEntryTime = entryTime;
        result.pedestrianExitTime = exitTime;

        if (!heldByHysteresis && entryTime > settings.predictionHorizon)
        {
            IsYielding = false;
            return result; // Monitoring: not heading into the path (or not soon enough to matter)
        }

        // --- 3. Vehicle timing to the conflict point ---
        float entryTimeForPoint = float.IsInfinity(entryTime) ? 0f : entryTime;
        float conflictAlong = ped.along + ped.alongSpeed * entryTimeForPoint;
        float zoneStart = conflictAlong - ped.radius;
        float zoneEnd = conflictAlong + ped.radius;

        float arrivalTime = TimeToTravel(zoneStart, v, Mathf.Max(0f, projectedAcceleration));
        float clearTime = v > Epsilon ? (zoneEnd + geometry.Length) / v : float.PositiveInfinity;
        result.vehicleArrivalTime = arrivalTime;
        result.vehicleClearTime = clearTime;

        // --- 4. Gap acceptance ---
        if (!heldByHysteresis)
        {
            if (!inside && zoneStart <= 0f)
            {
                // Front bumper is already past the crossing line; braking cannot help and would only
                // keep the vehicle body in the pedestrian's way longer.
                result.state = PedestrianConflictState.VehiclePassesFirst;
                IsYielding = false;
                return result;
            }
            if (!inside && clearTime + settings.vehicleFirstMargin <= entryTime)
            {
                result.state = PedestrianConflictState.VehiclePassesFirst;
                IsYielding = false;
                return result;
            }
            if (exitTime + settings.pedestrianFirstMargin <= arrivalTime)
            {
                result.state = PedestrianConflictState.PedestrianPassesFirst;
                IsYielding = false;
                return result;
            }
        }

        // --- 5. Yield: decelerate along the kinematic stopping profile to the stop point ---
        IsYielding = true;

        float gapToPedestrian = Mathf.Max(Mathf.Min(ped.along, conflictAlong) - ped.radius, 0f);
        float distanceToStop = gapToPedestrian - settings.stopBuffer;
        // A pedestrian walking along the lane away from the car is treated like a slow leader.
        float approachSpeed = Mathf.Max(0f, v - (inside ? Mathf.Max(0f, ped.alongSpeed) : 0f));

        float requiredDeceleration;
        if (distanceToStop > Epsilon)
            requiredDeceleration = approachSpeed * approachSpeed / (2f * distanceToStop);
        else if (approachSpeed > CrawlSpeed)
            requiredDeceleration = float.PositiveInfinity;
        else
            // At the stop point, crawling: finish at comfortable deceleration (overshoot < 7 cm; the stop buffer and
            // hard envelope still apply) rather than a one-frame spike to the braking limit.
            requiredDeceleration = approachSpeed > 0f ? idm.comfortableDeceleration : 0f;

        // -requiredDeceleration is never positive, so a yielding vehicle never speeds up towards the pedestrian
        // (a stopped vehicle stays stopped).
        float constraint = Mathf.Max(-requiredDeceleration, -maxDeceleration);

        result.distanceToStopPoint = distanceToStop;
        result.requiredDeceleration = requiredDeceleration;
        result.constraintAcceleration = constraint;
        result.timeToConflict = v > Epsilon ? Mathf.Max(zoneStart, 0f) / v : float.PositiveInfinity;
        result.state = constraint < -idm.comfortableDeceleration
            ? PedestrianConflictState.CriticalBraking
            : PedestrianConflictState.Yielding;
        return result;
    }

    /// <summary>Time to cover <paramref name="distance"/> from speed v with constant acceleration a (a &gt;= 0).</summary>
    public static float TimeToTravel(float distance, float v, float a)
    {
        if (distance <= 0f) return 0f;
        if (a > 1e-3f) return (-v + Mathf.Sqrt(v * v + 2f * a * distance)) / a;
        return v > Epsilon ? distance / v : float.PositiveInfinity;
    }
}
