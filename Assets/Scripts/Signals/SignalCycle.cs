using System.Collections.Generic;
using UnityEngine;

public enum SignalAspect { Red, Amber, Green }

/// <summary>
/// Turns a <see cref="SignalPlan"/> into the aspect of every signal head at any time in the cycle.
///
/// NZ sequence: red goes straight to green (no red+amber); green -> amber -> red. At each phase change, heads that are green
/// in both the old and the new phase stay green (no needless amber/green flicker). Heads that lose right of way show amber,
/// then red for the all-red interval. Heads that gain right of way turn green when the intergreen ends.
/// Pure logic: <see cref="SignalController"/> applies it to the scene's lights.
/// </summary>
public sealed class SignalCycle
{
    readonly SignalPlan mPlan;
    readonly HashSet<int>[] mLightsByPhase;
    readonly float[] mPhaseStart;
    readonly int mLightCount;

    public float CycleLength { get; }
    public SignalPlan Plan => mPlan;

    /// <param name="lightsPerPhase">For each plan phase (same order), the indices of the heads it shows green. Ignored for all-red phases.</param>
    public SignalCycle(SignalPlan plan, IReadOnlyList<IEnumerable<int>> lightsPerPhase, int lightCount)
    {
        mPlan = plan;
        mLightCount = lightCount;
        int n = plan.phases.Count;
        mLightsByPhase = new HashSet<int>[n];
        mPhaseStart = new float[n + 1];
        float t = 0f;
        for (int i = 0; i < n; i++)
        {
            mLightsByPhase[i] = plan.phases[i].allRed || lightsPerPhase[i] == null ? new HashSet<int>() : new HashSet<int>(lightsPerPhase[i]);
            mPhaseStart[i] = t;
            t += plan.phases[i].green + plan.Intergreen;
        }
        mPhaseStart[n] = t;
        CycleLength = t;
    }

    /// <summary>
    /// Aspect for one movement (signal group) using <paramref name="head"/>, which has right of way only in phases where
    /// <paramref name="allowedInPhase"/> returns true. A head can serve several movements (e.g. through and turning traffic)
    /// with different permissions. A movement losing right of way always gets amber then red, even when the head stays green
    /// for another movement.
    /// </summary>
    public SignalAspect MovementAspect(float time, int head, System.Func<string, bool> allowedInPhase)
    {
        int n = mPlan.phases.Count;
        if (n == 0 || CycleLength <= 0f)
            return SignalAspect.Red;

        float t = Mathf.Repeat(time, CycleLength);
        int k = 0;
        while (k < n - 1 && t >= mPhaseStart[k + 1])
            k++;

        bool hasRightOfWay = mLightsByPhase[k].Contains(head) && allowedInPhase(mPlan.phases[k].name);
        if (!hasRightOfWay)
            return SignalAspect.Red;

        float sinceGreenEnd = t - mPhaseStart[k] - mPlan.phases[k].green;
        if (sinceGreenEnd < 0f)
            return SignalAspect.Green;

        int next = (k + 1) % n;
        bool keepsRightOfWay = mLightsByPhase[next].Contains(head) && allowedInPhase(mPlan.phases[next].name);
        if (keepsRightOfWay)
            return SignalAspect.Green;
        return sinceGreenEnd < mPlan.amber ? SignalAspect.Amber : SignalAspect.Red;
    }

    /// <summary>Fills the aspect of each head and the index of the phase it currently belongs to (its right-of-way phase).</summary>
    public void Evaluate(float time, SignalAspect[] aspects, int[] phaseOfLight)
    {
        int n = mPlan.phases.Count;
        for (int l = 0; l < mLightCount; l++)
        {
            aspects[l] = SignalAspect.Red;
            phaseOfLight[l] = 0;
        }
        if (n == 0 || CycleLength <= 0f)
            return;

        float t = Mathf.Repeat(time, CycleLength);
        int k = 0;
        while (k < n - 1 && t >= mPhaseStart[k + 1])
            k++;

        float sinceStart = t - mPhaseStart[k];
        float green = mPlan.phases[k].green;
        HashSet<int> current = mLightsByPhase[k];

        if (sinceStart < green)
        {
            foreach (int l in current)
            {
                aspects[l] = SignalAspect.Green;
                phaseOfLight[l] = k;
            }
            return;
        }

        // Intergreen towards the next phase
        float sinceGreenEnd = sinceStart - green;
        HashSet<int> next = mLightsByPhase[(k + 1) % n];
        foreach (int l in current)
        {
            phaseOfLight[l] = k;
            if (next.Contains(l))
                aspects[l] = SignalAspect.Green;
            else if (sinceGreenEnd < mPlan.amber)
                aspects[l] = SignalAspect.Amber;
        }
    }
}
