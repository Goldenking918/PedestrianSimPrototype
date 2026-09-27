using System;
using UnityEngine;

/// <summary>
/// Per-driver parameters of the Intelligent Driver Model (Treiber, Hennecke &amp; Helbing, 2000).
/// Defaults are urban starting calibration values, not claims about every real driver.
/// </summary>
[Serializable]
public struct IdmParameters
{
    [Tooltip("v0: desired / free-flow speed (m/s).")]
    [Min(0.1f)] public float desiredSpeed;

    [Tooltip("T: desired time headway to the vehicle ahead (s).")]
    [Min(0f)] public float timeHeadway;

    [Tooltip("s0: minimum bumper-to-bumper gap at standstill (m).")]
    [Min(0f)] public float minimumGap;

    [Tooltip("a: maximum acceleration (m/s^2).")]
    [Min(0.01f)] public float maxAcceleration;

    [Tooltip("b: comfortable deceleration (m/s^2, positive).")]
    [Min(0.01f)] public float comfortableDeceleration;

    [Tooltip("delta: acceleration exponent (normally 4).")]
    [Min(1f)] public float accelerationExponent;

    public static IdmParameters UrbanDefault => new IdmParameters
    {
        desiredSpeed = 12f,
        timeHeadway = 1.5f,
        minimumGap = 2f,
        maxAcceleration = 1.5f,
        comfortableDeceleration = 2f,
        accelerationExponent = 4f,
    };
}

/// <summary>
/// Intelligent Driver Model longitudinal acceleration. Pure functions so they can be unit tested
/// and reused (the pedestrian layer uses the same equation with a virtual standing obstacle).
///
///   a_IDM  = a * [1 - (v/v0)^delta - (s*/s)^2]
///   s*     = s0 + max(0, v*T + v*dv / (2*sqrt(a*b)))
///
/// The max(0, ...) on the dynamic term is the standard form from Treiber &amp; Kesting (2013); it stops s*
/// dropping below s0 when the leader is much faster than the follower.
/// </summary>
public static class IntelligentDriverModel
{
    /// <summary>Gap value meaning "no leader".</summary>
    public const float NoLeader = float.PositiveInfinity;

    const float MinGap = 0.01f;

    /// <summary>Acceleration on a free road (no leader): a * [1 - (v/v0)^delta].</summary>
    public static float FreeRoadAcceleration(float speed, in IdmParameters p)
    {
        float v0 = Mathf.Max(p.desiredSpeed, 0.1f);
        return p.maxAcceleration * (1f - Mathf.Pow(Mathf.Max(speed, 0f) / v0, p.accelerationExponent));
    }

    /// <summary>Desired dynamic gap s*(v, dv).</summary>
    public static float DesiredGap(float speed, float approachRate, in IdmParameters p)
    {
        float v = Mathf.Max(speed, 0f);
        float dynamic = v * p.timeHeadway + v * approachRate / (2f * Mathf.Sqrt(p.maxAcceleration * p.comfortableDeceleration));
        return p.minimumGap + Mathf.Max(0f, dynamic);
    }

    /// <summary>
    /// Full IDM acceleration.
    /// </summary>
    /// <param name="speed">v, own speed (m/s).</param>
    /// <param name="gap">s, bumper-to-bumper gap to the leader (m); <see cref="NoLeader"/> for a free road.</param>
    /// <param name="approachRate">dv = v - v_leader (m/s); positive when closing in.</param>
    public static float Acceleration(float speed, float gap, float approachRate, in IdmParameters p)
    {
        float free = FreeRoadAcceleration(speed, p);
        if (float.IsPositiveInfinity(gap))
            return free;

        float sStar = DesiredGap(speed, approachRate, p);
        float ratio = sStar / Mathf.Max(gap, MinGap);
        return free - p.maxAcceleration * ratio * ratio;
    }
}
