using System.Collections.Generic;
using System.Linq;
using HealthbarGames;
using UnityEngine;

/// <summary>
/// Runs the computed fixed-time plan (<see cref="SignalTiming"/>, <see cref="SignalCycle"/>) on the scene's existing
/// traffic lights, replacing the TrafficLightManager's own program. It reads the manager's phases (names and light heads),
/// so no scene changes are needed.
/// - Deterministic: every scenario start restarts the cycle at the first phase, so runs see identical signal timing.
/// - Pausable: the signal clock stops while traffic is paused (midpoint turn-around).
/// Added automatically by <see cref="TrafficController"/> when the scene has a TrafficLightManager.
/// </summary>
public class SignalController : MonoBehaviour
{
    public TrafficLightManager manager;

    public SignalPlan CurrentPlan => mCycle?.Plan;
    /// <summary>Signal time since the plan started (s), excluding paused time.</summary>
    public float ElapsedTime { get; private set; }
    public bool Paused { get; set; }

    readonly List<TrafficLightBase> mLights = new List<TrafficLightBase>();
    readonly List<(string name, bool allRed, int[] lights)> mScenePhases = new List<(string, bool, int[])>();
    TrafficLightPhase[] mHeads; // one single-light phase object per head, so each head can be set independently
    SignalCycle mCycle;
    SignalAspect[] mAspects;
    int[] mPhaseOfLight;
    SignalAspect[] mApplied;
    int[] mAppliedPhase;

    /// <summary>Phase names in the scene's order, and whether each is an all-red (ForceStopOnly) phase.</summary>
    public IReadOnlyList<(string name, bool allRed)> PhaseOrder => mScenePhases.Select(p => (p.name, p.allRed)).ToList();

    public bool HasLights => mLights.Count > 0;

    /// <summary>Takes over the scene's lights from the TrafficLightManager. Call before the manager's Start (i.e. from Awake).</summary>
    public bool TakeControl()
    {
        if (manager == null)
            manager = FindFirstObjectByType<TrafficLightManager>();
        if (manager == null || manager.Phases == null)
            return false;

        manager.StopAllCoroutines();
        manager.enabled = false; // its Start (which launches its own program) will not run

        mLights.Clear();
        mScenePhases.Clear();
        foreach (TrafficLightPhase phase in manager.Phases)
        {
            var indices = new List<int>();
            if (phase.TrafficLights != null)
                foreach (TrafficLightBase light in phase.TrafficLights)
                {
                    if (light == null) continue;
                    int index = mLights.IndexOf(light);
                    if (index < 0) { mLights.Add(light); index = mLights.Count - 1; }
                    indices.Add(index);
                }
            mScenePhases.Add((phase.Name, phase.ForceStopOnly, indices.ToArray()));
        }

        mHeads = new TrafficLightPhase[mLights.Count];
        for (int i = 0; i < mLights.Count; i++)
            mHeads[i] = new TrafficLightPhase { Name = string.Empty, TrafficLights = new[] { mLights[i] } };
        mAspects = new SignalAspect[mLights.Count];
        mPhaseOfLight = new int[mLights.Count];
        mApplied = new SignalAspect[mLights.Count];
        mAppliedPhase = new int[mLights.Count];
        return mLights.Count > 0;
    }

    /// <summary>Starts the plan from the beginning of its first phase.</summary>
    public void StartPlan(SignalPlan plan)
    {
        var lightsPerPhase = plan.phases
            .Select(p => (IEnumerable<int>)mScenePhases.FirstOrDefault(s => s.name == p.name).lights ?? new int[0])
            .ToList();
        mCycle = new SignalCycle(plan, lightsPerPhase, mLights.Count);
        ElapsedTime = 0f;
        Apply(force: true);
        Debug.Log($"[SignalController] Signal plan started: {plan.Describe()}", this);
    }

    /// <summary>The active controller (if any), so stop lines can ask for movement-specific indications.</summary>
    public static SignalController Active { get; private set; }

    void OnEnable() => Active = this;

    void OnDisable()
    {
        if (Active == this)
            Active = null;
    }

    /// <summary>
    /// Indication for a movement controlled by <paramref name="head"/> that may only go in <paramref name="allowedPhaseNames"/>
    /// (empty = any phase in which the head is green). False if this controller does not run that head.
    /// </summary>
    public bool TryGetIndication(TrafficLightBase head, string[] allowedPhaseNames, out SignalIndication indication)
    {
        indication = SignalIndication.None;
        int index = head != null ? mLights.IndexOf(head) : -1;
        if (mCycle == null || index < 0)
            return false;

        bool anyPhase = allowedPhaseNames == null || allowedPhaseNames.Length == 0;
        SignalAspect aspect = mCycle.MovementAspect(ElapsedTime, index, phase =>
            anyPhase || System.Array.Exists(allowedPhaseNames, a => !string.IsNullOrWhiteSpace(a) &&
                string.Equals(a.Trim(), phase, System.StringComparison.OrdinalIgnoreCase)));
        indication = aspect == SignalAspect.Green ? SignalIndication.Green : aspect == SignalAspect.Amber ? SignalIndication.Amber : SignalIndication.Red;
        return true;
    }

    void Update()
    {
        if (mCycle == null || Paused)
            return;
        ElapsedTime += Time.deltaTime;
        Apply(force: false);
    }

    void Apply(bool force)
    {
        mCycle.Evaluate(ElapsedTime, mAspects, mPhaseOfLight);
        for (int i = 0; i < mLights.Count; i++)
        {
            if (!force && mAspects[i] == mApplied[i] && mPhaseOfLight[i] == mAppliedPhase[i])
                continue;
            mApplied[i] = mAspects[i];
            mAppliedPhase[i] = mPhaseOfLight[i];
            // The head's phase name drives StopLine's route rules (e.g. turning traffic only in "SideA").
            mHeads[i].Name = mCycle.Plan.phases.Count > 0 ? mCycle.Plan.phases[mPhaseOfLight[i]].name : string.Empty;
            mHeads[i].SetState(ToLightState(mAspects[i]));
        }
    }

    static TrafficLightBase.State ToLightState(SignalAspect aspect)
    {
        switch (aspect)
        {
            case SignalAspect.Green: return TrafficLightBase.State.Go;
            case SignalAspect.Amber: return TrafficLightBase.State.PrepareToStop;
            default: return TrafficLightBase.State.Stop;
        }
    }
}
