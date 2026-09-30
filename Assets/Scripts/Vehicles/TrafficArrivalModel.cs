using System;
using UnityEngine;

/// <summary>Modest per-driver variation around the base IDM parameters (uniform, +/- fraction of the base value).</summary>
[Serializable]
public class DriverVariation
{
    [Tooltip("+/- fraction applied to the time headway T.")]
    [Range(0f, 0.5f)] public float timeHeadway = 0.15f;
    [Tooltip("+/- fraction applied to the maximum acceleration a.")]
    [Range(0f, 0.5f)] public float maxAcceleration = 0.15f;
    [Tooltip("+/- fraction applied to the comfortable deceleration b.")]
    [Range(0f, 0.5f)] public float comfortableDeceleration = 0.1f;
}

/// <summary>Everything random about one arriving vehicle, drawn up front so the sequence is reproducible from the seed.</summary>
public struct VehicleArrivalPlan
{
    /// <summary>Time to wait after this vehicle enters before the next arrival (s).</summary>
    public float headwayAfter;
    public int prefabIndex;
    public bool useAlternateRoute;
    public IdmParameters idm;
}

/// <summary>
/// Vehicle arrival process and driver heterogeneity.
///
/// Headways follow a truncated shifted-exponential distribution. This is the usual model for random arrivals
/// (Poisson traffic) with a minimum physical headway, and it gives irregular, bunched arrivals rather than
/// evenly spaced cars. Every random value for a vehicle is drawn in a fixed order from a seeded System.Random.
/// The sequence of vehicles (headway, model, route, driver parameters) is therefore identical for a given seed.
/// </summary>
public static class TrafficArrivalModel
{
    /// <summary>
    /// Inverse-CDF sample of min + Exp(mean - min), truncated at max.
    /// When max &lt;= min, returns <paramref name="fixedHeadway"/> (deterministic, evenly spaced arrivals).
    /// A mean &lt;= min falls back to the midpoint of min and max.
    /// </summary>
    public static float SampleHeadway(double u, float min, float mean, float max, float fixedHeadway)
    {
        if (max <= min)
            return Mathf.Max(fixedHeadway, 0.01f);

        min = Mathf.Max(min, 0f);
        if (mean <= min)
            mean = 0.5f * (min + max);

        double lambda = 1.0 / (mean - min);
        double range = max - min;
        double x = -Math.Log(1.0 - u * (1.0 - Math.Exp(-lambda * range))) / lambda;
        return min + (float)Math.Min(x, range);
    }

    /// <summary>Expected value of <see cref="SampleHeadway"/> (truncation lowers the mean below the nominal value).</summary>
    public static float ExpectedHeadway(float min, float mean, float max, float fixedHeadway)
    {
        if (max <= min)
            return Mathf.Max(fixedHeadway, 0.01f);
        min = Mathf.Max(min, 0f);
        if (mean <= min)
            mean = 0.5f * (min + max);
        double scale = mean - min;
        double range = max - min;
        double tail = Math.Exp(-range / scale);
        return min + (float)(scale - range * tail / (1.0 - tail));
    }

    public static IdmParameters ApplyDriverVariation(IdmParameters baseIdm, DriverVariation variation,
        float desiredSpeedMin, float desiredSpeedMax, double speedRoll, double headwayRoll, double accelRoll, double decelRoll)
    {
        IdmParameters p = baseIdm;
        if (desiredSpeedMax > desiredSpeedMin)
            p.desiredSpeed = Mathf.Lerp(desiredSpeedMin, desiredSpeedMax, (float)speedRoll);
        if (variation != null)
        {
            p.timeHeadway *= 1f + variation.timeHeadway * (2f * (float)headwayRoll - 1f);
            p.maxAcceleration *= 1f + variation.maxAcceleration * (2f * (float)accelRoll - 1f);
            p.comfortableDeceleration *= 1f + variation.comfortableDeceleration * (2f * (float)decelRoll - 1f);
        }
        return p;
    }

    /// <summary>
    /// Speed at which a new vehicle can enter behind a leader <paramref name="gap"/> metres ahead: capped by the
    /// IDM equilibrium speed for that gap, then reduced until IDM would not need more than comfortable braking.
    /// </summary>
    public static float ComputeEntrySpeed(float gap, float leaderSpeed, in IdmParameters p)
    {
        if (float.IsPositiveInfinity(gap))
            return p.desiredSpeed;

        float v = Mathf.Clamp((gap - p.minimumGap) / Mathf.Max(p.timeHeadway, 0.1f), 0f, p.desiredSpeed);
        while (v > 0f && IntelligentDriverModel.Acceleration(v, gap, v - leaderSpeed, p) < -p.comfortableDeceleration)
            v = Mathf.Max(0f, v - 0.25f);
        return v;
    }

    /// <summary>Combines the scenario seed with a per-spawner stream index into an independent, stable seed.</summary>
    public static int DeriveSeed(int seed, int stream)
    {
        unchecked
        {
            uint h = (uint)seed * 2654435761u;
            h ^= (uint)(stream + 1) * 2246822519u;
            h ^= h >> 15;
            h *= 3266489917u;
            h ^= h >> 13;
            return (int)h;
        }
    }

    /// <summary>Draws the next arrival. Always consumes exactly seven random numbers so the sequence never depends on configuration branches.</summary>
    public static VehicleArrivalPlan DrawPlan(System.Random rng, int prefabCount, float alternateRouteChance, bool hasAlternateRoute,
        float headwayMin, float headwayMean, float headwayMax, float fixedHeadway,
        IdmParameters baseIdm, DriverVariation variation, float desiredSpeedMin, float desiredSpeedMax)
    {
        double headwayRoll = rng.NextDouble();
        int prefabIndex = rng.Next(Mathf.Max(prefabCount, 1));
        double routeRoll = rng.NextDouble();
        double speedRoll = rng.NextDouble();
        double tRoll = rng.NextDouble();
        double aRoll = rng.NextDouble();
        double bRoll = rng.NextDouble();

        return new VehicleArrivalPlan
        {
            headwayAfter = SampleHeadway(headwayRoll, headwayMin, headwayMean, headwayMax, fixedHeadway),
            prefabIndex = prefabIndex,
            useAlternateRoute = hasAlternateRoute && routeRoll < alternateRouteChance,
            idm = ApplyDriverVariation(baseIdm, variation, desiredSpeedMin, desiredSpeedMax, speedRoll, tRoll, aRoll, bRoll),
        };
    }
}
