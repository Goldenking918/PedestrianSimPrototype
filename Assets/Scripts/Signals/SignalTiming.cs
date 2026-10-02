using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>Inputs for computing the fixed-time signal plan. Defaults are NZ urban values; see <see cref="SignalTiming"/>.</summary>
[Serializable]
public class SignalPlanSettings
{
    [Tooltip("Use the computed fixed-time plan. If off, the scene's TrafficLightManager runs its own program as before.")]
    public bool enabled = true;

    [Header("Clearance intervals (ITE)")]
    [Tooltip("Approach design speed, normally the posted speed limit (km/h).")]
    [Min(5f)] public float designSpeedKmh = 50f;
    [Tooltip("Driver perception-reaction time used for the amber interval (s).")]
    [Min(0f)] public float reactionTime = 1f;
    [Tooltip("Deceleration assumed for the amber interval (m/s^2). ITE uses 3.05.")]
    [Min(0.1f)] public float amberDeceleration = 3.05f;
    [Min(0f)] public float minAmber = 3f;
    [Min(0f)] public float maxAmber = 6f;
    [Tooltip("Distance from the stop line to the far side of the junction conflict area (m). Scene: ~22 m between the two stop lines.")]
    [Min(0f)] public float intersectionWidth = 22f;
    [Tooltip("Design vehicle length for the all-red interval (m).")]
    [Min(0f)] public float vehicleLength = 4.8f;
    [Min(0f)] public float minAllRed = 1f;
    [Min(0f)] public float maxAllRed = 4f;

    [Header("Cycle length and splits (Webster 1958)")]
    [Tooltip("Saturation flow of a through lane (veh/h of green). 1400 is the queue discharge rate measured from the IDM vehicle model " +
             "(urban defaults: ~2.5 s between cars leaving a queue); keep it consistent with the vehicle parameters.")]
    [Min(1f)] public float saturationFlow = 1400f;
    [Tooltip("Saturation flow of turning traffic (veh/h of green), ~90% of the through value.")]
    [Min(1f)] public float turnSaturationFlow = 1260f;
    [Tooltip("Minimum displayed green per phase (s).")]
    [Min(0f)] public float minGreen = 6f;
    [Min(1f)] public float minCycle = 40f;
    [Min(1f)] public float maxCycle = 120f;
    [Tooltip("If > 0, use this cycle length instead of Webster's optimum (s).")]
    [Min(0f)] public float fixedCycle = 0f;

    [Header("Phases (names from the scene's TrafficLightManager)")]
    [Tooltip("Phase serving the through movements in both directions.")]
    public string throughPhase = "AllGreen";
    [Tooltip("Phases serving turning traffic (each with its own turning flow).")]
    public string[] turnPhases = { "SideA", "SideB" };
    [Tooltip("Duration of any all-red (ForceStopOnly) phase, e.g. a junction pedestrian phase (s). 0 skips it.")]
    [Min(0f)] public float allRedPhaseDuration = 0f;
}

/// <summary>A fixed-time plan: phase order and greens, plus the amber and all-red intervals used at every phase change.</summary>
public sealed class SignalPlan
{
    public struct Phase
    {
        public string name;
        /// <summary>Displayed green (s); for an all-red phase, its duration.</summary>
        public float green;
        /// <summary>All lights red for the phase (a ForceStopOnly phase).</summary>
        public bool allRed;
        /// <summary>Webster critical flow ratio y = q / s (0 for all-red phases).</summary>
        public float flowRatio;
    }

    public readonly List<Phase> phases = new List<Phase>();
    public float amber;
    public float allRed;
    public float cycleLength;
    /// <summary>Total lost time L used in Webster's formula (s).</summary>
    public float lostTime;
    /// <summary>Sum of critical flow ratios Y.</summary>
    public float flowRatioSum;
    /// <summary>Webster optimum before clamping to [minCycle, maxCycle] (s); +inf when oversaturated.</summary>
    public float websterCycle;

    public float Intergreen => amber + allRed;

    public string Describe()
    {
        string phaseText = string.Join(", ", phases.Select(p => p.allRed ? $"{p.name} all-red {p.green:F1}s" : $"{p.name} {p.green:F1}s (y={p.flowRatio:F2})"));
        return $"cycle {cycleLength:F1}s (Webster {websterCycle:F1}s, L={lostTime:F1}s, Y={flowRatioSum:F2}); amber {amber:F1}s, all-red {allRed:F1}s; {phaseText}";
    }
}

/// <summary>
/// Fixed-time signal timing from standard traffic engineering methods. Every value can be traced to a formula:
///   Amber (ITE kinematic):      Y = t + v / (2a)                     t = reaction time, a = deceleration, v = design speed
///   All-red (ITE clearance):    R = (W + L) / v                      W = junction width, L = vehicle length
///   Cycle (Webster 1958):       C0 = (1.5 L + 5) / (1 - Y)           L = total lost time, Y = sum of critical q/s
///   Green split:                g_i = (C - L) * y_i / Y              with a minimum green per phase
/// Lost time per phase change is taken as the intergreen (amber + all-red), a common approximation.
/// Amber and all-red are rounded up to 0.5 s and clamped to sensible bounds, as practitioners do.
/// </summary>
public static class SignalTiming
{
    public static float AmberInterval(SignalPlanSettings s)
    {
        float v = s.designSpeedKmh / 3.6f;
        return Mathf.Clamp(RoundUp(s.reactionTime + v / (2f * s.amberDeceleration)), s.minAmber, s.maxAmber);
    }

    public static float AllRedInterval(SignalPlanSettings s)
    {
        float v = s.designSpeedKmh / 3.6f;
        return Mathf.Clamp(RoundUp((s.intersectionWidth + s.vehicleLength) / v), s.minAllRed, s.maxAllRed);
    }

    /// <summary>Webster's optimum cycle length; +inf when the junction is oversaturated (Y &gt;= 1).</summary>
    public static float WebsterCycle(float lostTime, float flowRatioSum)
    {
        if (flowRatioSum >= 1f)
            return float.PositiveInfinity;
        return (1.5f * lostTime + 5f) / (1f - flowRatioSum);
    }

    public static float RoundUp(float value, float step = 0.5f) => Mathf.Ceil(value / step - 1e-4f) * step;

    /// <summary>
    /// Builds the plan for the given phase order (names and whether each is an all-red phase) and per-lane flows.
    /// </summary>
    /// <param name="throughFlowPerLane">Critical through flow per lane (veh/h).</param>
    /// <param name="turnFlowPerLane">Turning flow per lane served by each turn phase (veh/h).</param>
    public static SignalPlan BuildPlan(SignalPlanSettings s, IReadOnlyList<(string name, bool allRed)> phaseOrder,
        float throughFlowPerLane, float turnFlowPerLane)
    {
        var plan = new SignalPlan
        {
            amber = AmberInterval(s),
            allRed = AllRedInterval(s),
        };

        foreach (var (name, allRed) in phaseOrder)
        {
            if (allRed)
            {
                if (s.allRedPhaseDuration > 0f)
                    plan.phases.Add(new SignalPlan.Phase { name = name, allRed = true, green = s.allRedPhaseDuration });
                continue;
            }
            float y = 0f;
            if (name == s.throughPhase)
                y = Mathf.Max(0f, throughFlowPerLane) / s.saturationFlow;
            else if (s.turnPhases != null && Array.IndexOf(s.turnPhases, name) >= 0)
                y = Mathf.Max(0f, turnFlowPerLane) / s.turnSaturationFlow;
            plan.phases.Add(new SignalPlan.Phase { name = name, flowRatio = y });
        }

        float fixedRed = plan.phases.Where(p => p.allRed).Sum(p => p.green);
        plan.lostTime = plan.phases.Count * plan.Intergreen + fixedRed;
        plan.flowRatioSum = plan.phases.Sum(p => p.flowRatio);
        plan.websterCycle = WebsterCycle(plan.lostTime, plan.flowRatioSum);

        float cycle = s.fixedCycle > 0f ? s.fixedCycle : Mathf.Clamp(plan.websterCycle, s.minCycle, s.maxCycle);
        if (float.IsInfinity(plan.websterCycle) && s.fixedCycle <= 0f)
            cycle = s.maxCycle;

        AssignGreens(plan, s.minGreen, cycle - plan.lostTime);
        plan.cycleLength = plan.lostTime + plan.phases.Where(p => !p.allRed).Sum(p => p.green);
        return plan;
    }

    /// <summary>Splits the effective green in proportion to flow ratios, raising any phase below the minimum green.</summary>
    static void AssignGreens(SignalPlan plan, float minGreen, float effectiveGreen)
    {
        var signalPhases = Enumerable.Range(0, plan.phases.Count).Where(i => !plan.phases[i].allRed).ToList();
        var fixedAtMin = new HashSet<int>();
        float remaining = effectiveGreen;

        // Iterate: phases whose proportional share is below the minimum get the minimum; the rest share what is left.
        for (int iteration = 0; iteration <= signalPhases.Count; iteration++)
        {
            var free = signalPhases.Where(i => !fixedAtMin.Contains(i)).ToList();
            remaining = effectiveGreen - fixedAtMin.Count * minGreen;
            float freeY = free.Sum(i => plan.phases[i].flowRatio);
            bool changed = false;
            foreach (int i in free)
            {
                float share = freeY > 0f ? remaining * plan.phases[i].flowRatio / freeY : remaining / Mathf.Max(free.Count, 1);
                if (share < minGreen)
                {
                    fixedAtMin.Add(i);
                    changed = true;
                }
            }
            if (!changed)
            {
                foreach (int i in free)
                {
                    var p = plan.phases[i];
                    p.green = freeY > 0f ? remaining * p.flowRatio / freeY : remaining / Mathf.Max(free.Count, 1);
                    plan.phases[i] = p;
                }
                break;
            }
        }

        foreach (int i in fixedAtMin)
        {
            var p = plan.phases[i];
            p.green = minGreen;
            plan.phases[i] = p;
        }
    }
}
