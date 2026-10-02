using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>What a traffic signal is showing to a particular vehicle (its movement / route).</summary>
public enum SignalIndication
{
    /// <summary>No signal control (no stop line ahead, lights off or flashing).</summary>
    None,
    Green,
    Amber,
    Red,
}

/// <summary>A stop line a vehicle may have to stop at (implemented by <c>StopLine</c>).</summary>
public interface ISignalStop
{
    /// <summary>
    /// Finds where the vehicle path (polyline points[0..count)) first crosses this stop line.
    /// Returns the distance along the path from points[0], if it crosses within <paramref name="maxAlong"/>.
    /// </summary>
    bool TryGetCrossing(Vector3[] points, int count, float maxAlong, out float along);

    /// <summary>The indication this stop line currently shows to <paramref name="vehicle"/> (depends on its route).</summary>
    SignalIndication IndicationFor(CarMovement vehicle);
}

/// <summary>Registry of active stop lines, so vehicles can find the next one on their path without physics queries.</summary>
public static class SignalStops
{
    static readonly List<ISignalStop> sActive = new List<ISignalStop>();

    public static IReadOnlyList<ISignalStop> Active => sActive;

    public static void Register(ISignalStop stop)
    {
        if (stop != null && !sActive.Contains(stop))
            sActive.Add(stop);
    }

    public static void Unregister(ISignalStop stop) => sActive.Remove(stop);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => sActive.Clear();
}

[Serializable]
public class SignalResponseSettings
{
    [Tooltip("Stop lines further ahead than this are ignored (m).")]
    [Min(0f)] public float lookaheadDistance = 120f;

    [Tooltip("Dilemma-zone rule: at amber onset the driver stops if the deceleration needed to stop at the line is at most this, otherwise proceeds (m/s^2). 3.0 approximates the ITE design value (3.05).")]
    [Min(0.1f)] public float amberStopDeceleration = 3f;

    [Tooltip("Gap the driver leaves between the front bumper and the stop line when stopped (m).")]
    [Min(0f)] public float stopLineGap = 1f;
}

public enum SignalDecision
{
    /// <summary>No signal constraint (green, no signal, or already past the line).</summary>
    None,
    /// <summary>Stopping for red (or for amber with a comfortable stopping distance).</summary>
    Stop,
    /// <summary>Amber came on too close to stop comfortably: continuing through the junction.</summary>
    Proceed,
}

public struct SignalAssessment
{
    public SignalIndication indication;
    public SignalDecision decision;
    /// <summary>Front bumper to stop line (m); +inf when no stop line ahead.</summary>
    public float distanceToStopLine;
    /// <summary>Deceleration needed to stop <see cref="SignalResponseSettings.stopLineGap"/> short of the line (m/s^2).</summary>
    public float requiredDeceleration;
    /// <summary>Acceleration cap from the signal (m/s^2); +inf when none.</summary>
    public float constraintAcceleration;

    public static SignalAssessment Empty => new SignalAssessment
    {
        indication = SignalIndication.None,
        decision = SignalDecision.None,
        distanceToStopLine = float.PositiveInfinity,
        constraintAcceleration = float.PositiveInfinity,
    };
}

/// <summary>
/// Driver response to traffic signals. Like the pedestrian layer, it can only lower the IDM acceleration.
///
/// - Green, no signal, or front bumper already past the line: no constraint.
/// - Amber (dilemma-zone model, Gazis, Herman &amp; Maradudin 1960): decided once at amber onset. The driver stops if the
///   required deceleration v^2 / 2d is at most <see cref="SignalResponseSettings.amberStopDeceleration"/>, otherwise
///   proceeds and keeps going even if the light turns red before the line.
/// - Red (or amber with a stop decision): the stop line is a virtual standing vehicle in the IDM (Treiber &amp; Kesting 2013),
///   giving a smooth, early approach and letting queued cars close up to the line. Its braking is capped at
///   max(comfortable deceleration, kinematic deceleration needed to stop at the line), so drivers never brake harder than
///   needed (the plain IDM over-brakes when the stop decision is made late, e.g. at amber).
/// </summary>
public sealed class SignalResponseModel
{
    const float Epsilon = 0.01f;
    const float CrawlSpeed = 0.5f;

    object mLatchedStop;
    SignalDecision mLatchedAmberDecision = SignalDecision.None;

    public void Reset()
    {
        mLatchedStop = null;
        mLatchedAmberDecision = SignalDecision.None;
    }

    /// <param name="stop">Identity of the stop line ahead (used to latch the amber decision); null if none.</param>
    /// <param name="distanceToStopLine">Front bumper to stop line along the path (m).</param>
    public SignalAssessment Evaluate(object stop, SignalIndication indication, float distanceToStopLine, float speed,
        in IdmParameters idm, SignalResponseSettings settings, float maxDeceleration)
    {
        var result = SignalAssessment.Empty;
        if (stop == null || settings == null)
        {
            Reset();
            return result;
        }

        if (!ReferenceEquals(stop, mLatchedStop))
        {
            mLatchedStop = stop;
            mLatchedAmberDecision = SignalDecision.None;
        }

        result.indication = indication;
        result.distanceToStopLine = distanceToStopLine;
        float v = Mathf.Max(speed, 0f);
        float stoppingDistance = distanceToStopLine - settings.stopLineGap;
        result.requiredDeceleration = RequiredDeceleration(v, stoppingDistance);

        if (indication == SignalIndication.Green || indication == SignalIndication.None)
        {
            mLatchedAmberDecision = SignalDecision.None;
            return result;
        }
        if (distanceToStopLine < 0f)
            return result; // already over the line: committed

        if (mLatchedAmberDecision == SignalDecision.None && indication == SignalIndication.Amber)
            mLatchedAmberDecision = result.requiredDeceleration <= settings.amberStopDeceleration ? SignalDecision.Stop : SignalDecision.Proceed;

        if (mLatchedAmberDecision == SignalDecision.Proceed)
        {
            result.decision = SignalDecision.Proceed;
            return result;
        }

        // Red, or amber with a stop decision
        result.decision = SignalDecision.Stop;
        var virtualVehicle = idm;
        virtualVehicle.minimumGap = settings.stopLineGap;
        float idmTerm = IntelligentDriverModel.Acceleration(v, Mathf.Max(distanceToStopLine, Epsilon), v, virtualVehicle);
        // Never harsher than needed: the IDM over-brakes in late situations (e.g. a stop decision at amber), so cap its
        // braking at max(comfortable deceleration, kinematic requirement). Allowing the full requirement means the car
        // can always stop; CarMovement's safety envelope additionally never lets a stopping car cross the line.
        float harshest = -Mathf.Max(idm.comfortableDeceleration, result.requiredDeceleration);
        result.constraintAcceleration = Mathf.Max(Mathf.Max(idmTerm, harshest), -maxDeceleration);
        return result;
    }

    static float RequiredDeceleration(float v, float distance)
    {
        if (distance > Epsilon)
            return v * v / (2f * distance);
        if (v > CrawlSpeed)
            return float.PositiveInfinity;
        return v > 0f ? 2f : 0f; // crawling at the line: finish the stop gently
    }
}
