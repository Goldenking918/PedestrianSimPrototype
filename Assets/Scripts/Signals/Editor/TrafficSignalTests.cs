using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

/// <summary>Signal timing formulas, signal sequencing, and driver behaviour at signals.</summary>
public class TrafficSignalTests
{
    static readonly (string, bool)[] ScenePhases = { ("AllGreen", false), ("SideA", false), ("AllRed", true), ("SideB", false) };

    // ------------------------------------------------------------------ timing (ITE, Webster)

    [Test]
    public void Amber_FollowsIteFormula_RoundedUp()
    {
        var s = new SignalPlanSettings(); // 50 km/h, t = 1 s, a = 3.05 m/s^2 -> 1 + 13.89 / 6.1 = 3.28 -> 3.5 s
        Assert.AreEqual(3.5f, SignalTiming.AmberInterval(s), 1e-4f);
        s.designSpeedKmh = 30f;  // 1 + 8.33 / 6.1 = 2.37 -> clamped to the 3 s minimum
        Assert.AreEqual(3f, SignalTiming.AmberInterval(s), 1e-4f);
    }

    [Test]
    public void AllRed_IsClearanceTime_RoundedUp()
    {
        var s = new SignalPlanSettings(); // (22 + 4.8) / 13.89 = 1.93 -> 2.0 s
        Assert.AreEqual(2f, SignalTiming.AllRedInterval(s), 1e-4f);
    }

    [Test]
    public void Webster_CycleFormula()
    {
        Assert.AreEqual((1.5f * 16.5f + 5f) / (1f - 0.5f), SignalTiming.WebsterCycle(16.5f, 0.5f), 1e-4f);
        Assert.IsTrue(float.IsPositiveInfinity(SignalTiming.WebsterCycle(16.5f, 1f)));
    }

    [Test]
    public void Plan_SplitsGreenByFlowRatio_RespectsMinimumGreen_AndSkipsAllRedPhase()
    {
        var s = new SignalPlanSettings();
        SignalPlan plan = SignalTiming.BuildPlan(s, ScenePhases, 550f, 55f);

        CollectionAssert.AreEqual(new[] { "AllGreen", "SideA", "SideB" }, plan.phases.Select(p => p.name).ToArray());
        Assert.That(plan.cycleLength, Is.InRange(s.minCycle, s.maxCycle));
        Assert.AreEqual(plan.websterCycle, plan.cycleLength, 0.5f);
        Assert.IsTrue(plan.phases.All(p => p.green >= s.minGreen - 1e-3f));
        Assert.Greater(plan.phases[0].green, plan.phases[1].green, "through phase gets the larger share");
        float sumGreens = plan.phases.Sum(p => p.green);
        Assert.AreEqual(plan.cycleLength, sumGreens + plan.lostTime, 1e-3f);
    }

    [Test]
    public void Plan_Oversaturated_UsesMaxCycle_AndFixedCycleOverrides()
    {
        var s = new SignalPlanSettings();
        Assert.AreEqual(s.maxCycle, SignalTiming.BuildPlan(s, ScenePhases, 2000f, 500f).cycleLength, 1e-3f);
        s.fixedCycle = 80f;
        Assert.AreEqual(80f, SignalTiming.BuildPlan(s, ScenePhases, 300f, 30f).cycleLength, 1e-3f);
    }

    [Test]
    public void Plan_AllRedPhase_IncludedWhenDurationSet()
    {
        var s = new SignalPlanSettings { allRedPhaseDuration = 10f };
        SignalPlan plan = SignalTiming.BuildPlan(s, ScenePhases, 400f, 40f);
        Assert.IsTrue(plan.phases.Any(p => p.name == "AllRed" && p.allRed && Mathf.Approximately(p.green, 10f)));
    }

    // ------------------------------------------------------------------ sequencing

    // Heads: 0 = eastbound (AllGreen, SideA), 1 = westbound (AllGreen, SideB)
    static SignalCycle SceneCycle(out SignalPlan plan)
    {
        plan = SignalTiming.BuildPlan(new SignalPlanSettings(), ScenePhases, 400f, 40f);
        var sets = plan.phases.Select(p => (IEnumerable<int>)(p.name == "AllGreen" ? new[] { 0, 1 } : p.name == "SideA" ? new[] { 0 } : new[] { 1 })).ToList();
        return new SignalCycle(plan, sets, 2);
    }

    static List<SignalAspect> Trace(Func<float, SignalAspect> aspectAt, float cycle)
    {
        var sequence = new List<SignalAspect>();
        for (float t = 0f; t < 2f * cycle; t += 0.05f)
        {
            SignalAspect a = aspectAt(t);
            if (sequence.Count == 0 || sequence[sequence.Count - 1] != a)
                sequence.Add(a);
        }
        return sequence;
    }

    [Test]
    public void Cycle_NzSequence_GreenAmberRed_NoRedAmberBeforeGreen()
    {
        SignalCycle cycle = SceneCycle(out _);
        var aspects = new SignalAspect[2];
        var phases = new int[2];
        foreach (int head in new[] { 0, 1 })
        {
            var seq = Trace(t => { cycle.Evaluate(t, aspects, phases); return aspects[head]; }, cycle.CycleLength);
            for (int i = 1; i < seq.Count; i++)
            {
                if (seq[i] == SignalAspect.Green) Assert.AreEqual(SignalAspect.Red, seq[i - 1], "green follows red directly");
                if (seq[i] == SignalAspect.Amber) Assert.AreEqual(SignalAspect.Green, seq[i - 1], "amber follows green");
                if (seq[i] == SignalAspect.Red) Assert.AreEqual(SignalAspect.Amber, seq[i - 1], "red follows amber");
            }
        }
    }

    [Test]
    public void Cycle_HeadGreenInConsecutivePhases_StaysGreen()
    {
        // Eastbound is green in AllGreen then SideA: one continuous green per cycle, no amber between them.
        SignalCycle cycle = SceneCycle(out _);
        var aspects = new SignalAspect[2];
        var phases = new int[2];
        var seq = Trace(t => { cycle.Evaluate(t, aspects, phases); return aspects[0]; }, cycle.CycleLength);
        Assert.LessOrEqual(seq.Count(a => a == SignalAspect.Green), 3, "at most one green per cycle over two cycles (plus wrap)");
    }

    [Test]
    public void Movement_LosingRightOfWay_GetsAmberEvenWhenHeadStaysGreen()
    {
        // Westbound turning traffic only has SideB; the westbound head stays green into AllGreen for through traffic.
        SignalCycle cycle = SceneCycle(out SignalPlan plan);
        Func<string, bool> turnOnly = phase => phase == "SideB";
        Func<string, bool> through = phase => phase == "SideB" || phase == "AllGreen";
        var turnSeq = Trace(t => cycle.MovementAspect(t, 1, turnOnly), cycle.CycleLength);
        for (int i = 1; i < turnSeq.Count; i++)
            if (turnSeq[i] == SignalAspect.Red)
                Assert.AreEqual(SignalAspect.Amber, turnSeq[i - 1], "turning movement never goes green -> red");

        // At the end of SideB's green, through stays green while turning traffic shows amber.
        int sideB = plan.phases.FindIndex(p => p.name == "SideB");
        float sideBGreenEnd = 0f;
        for (int i = 0; i <= sideB; i++) sideBGreenEnd += (i < sideB ? plan.phases[i].green + plan.Intergreen : plan.phases[i].green);
        Assert.AreEqual(SignalAspect.Amber, cycle.MovementAspect(sideBGreenEnd + 0.5f, 1, turnOnly));
        Assert.AreEqual(SignalAspect.Green, cycle.MovementAspect(sideBGreenEnd + 0.5f, 1, through));
    }

    // ------------------------------------------------------------------ driver response (real CarMovement)

    class TestStop : ISignalStop
    {
        public float lineX;
        public SignalIndication indication = SignalIndication.Red;
        public bool TryGetCrossing(Vector3[] points, int count, float maxAlong, out float along)
        {
            along = 0f;
            float cumulative = 0f;
            for (int i = 0; i < count - 1; i++)
            {
                float a = points[i].x, b = points[i + 1].x;
                if ((a - lineX) * (b - lineX) <= 0f && a != b)
                {
                    along = cumulative + Mathf.Abs(lineX - a);
                    return along <= maxAlong;
                }
                cumulative += Mathf.Abs(b - a);
            }
            return false;
        }
        public SignalIndication IndicationFor(CarMovement vehicle) => indication;
    }

    const float Dt = 0.02f;
    const float CarFront = 2.25f;
    readonly List<GameObject> mObjects = new List<GameObject>();
    TestStop mStop;

    [SetUp]
    public void SetUp()
    {
        PedestrianTracker.SetTarget(null, -1f);
        mStop = new TestStop { lineX = 100f };
        SignalStops.Register(mStop);
    }

    [TearDown]
    public void TearDown()
    {
        SignalStops.Unregister(mStop);
        foreach (GameObject go in mObjects)
            if (go != null)
                UnityEngine.Object.DestroyImmediate(go);
        mObjects.Clear();
    }

    CarMovement CreateCar(float x, float speed)
    {
        var go = new GameObject("TestCar");
        mObjects.Add(go);
        go.transform.SetPositionAndRotation(new Vector3(x, 0f, 0f), Quaternion.LookRotation(Vector3.right));
        go.AddComponent<BoxCollider>().size = new Vector3(2f, 1.5f, 4.5f);
        var car = go.AddComponent<CarMovement>();
        car.idm = IdmParameters.UrbanDefault;
        var end = new GameObject("TestWaypoint");
        mObjects.Add(end);
        end.transform.position = new Vector3(1000f, 0f, 0f);
        car.waypoints = new[] { end.transform };
        car.SetInitialSpeed(speed);
        return car;
    }

    float Front(CarMovement car) => car.transform.position.x + CarFront;

    [Test]
    public void Red_CarStopsSmoothlyJustBeforeTheLine_AndNeverCrosses()
    {
        var car = CreateCar(0f, 12f);
        float maxDecel = 0f;
        for (float t = 0f; t < 30f; t += Dt)
        {
            car.Step(Dt);
            maxDecel = Mathf.Max(maxDecel, -car.AppliedAcceleration);
            Assert.LessOrEqual(Front(car), mStop.lineX, "never crosses a red stop line");
        }
        Assert.Less(car.CurrentSpeed, 0.01f);
        Assert.That(mStop.lineX - Front(car), Is.InRange(0f, car.signalResponse.stopLineGap + 0.5f));
        Assert.LessOrEqual(maxDecel, Mathf.Max(car.idm.comfortableDeceleration, 2f) + 0.1f, "planned stop at a red uses comfortable braking");
    }

    [Test]
    public void Green_NoEffect()
    {
        mStop.indication = SignalIndication.Green;
        var car = CreateCar(0f, 12f);
        float minSpeed = float.PositiveInfinity;
        for (float t = 0f; t < 12f; t += Dt)
        {
            car.Step(Dt);
            minSpeed = Mathf.Min(minSpeed, car.CurrentSpeed);
        }
        Assert.Greater(minSpeed, 11.9f);
        Assert.Greater(Front(car), mStop.lineX);
    }

    [Test]
    public void Amber_TooCloseToStopComfortably_Proceeds_EvenIfItTurnsRed()
    {
        var car = CreateCar(100f - CarFront - 15f, 12f); // 15 m out at 12 m/s: stopping needs ~5.2 m/s^2 > 3
        mStop.indication = SignalIndication.Amber;
        car.Step(Dt);
        Assert.AreEqual(SignalDecision.Proceed, car.SignalState.decision);
        mStop.indication = SignalIndication.Red;
        for (float t = 0f; t < 3f; t += Dt)
            car.Step(Dt);
        Assert.Greater(Front(car), mStop.lineX, "driver in the dilemma zone goes through");
    }

    [Test]
    public void Amber_FarEnough_Stops()
    {
        var car = CreateCar(100f - CarFront - 40f, 12f); // 40 m out: needs ~1.9 m/s^2 <= 3
        mStop.indication = SignalIndication.Amber;
        car.Step(Dt);
        Assert.AreEqual(SignalDecision.Stop, car.SignalState.decision);
        mStop.indication = SignalIndication.Red;
        float maxDecel = 0f;
        for (float t = 0f; t < 20f; t += Dt)
        {
            car.Step(Dt);
            maxDecel = Mathf.Max(maxDecel, -car.AppliedAcceleration);
        }
        Assert.Less(car.CurrentSpeed, 0.01f);
        Assert.LessOrEqual(Front(car), mStop.lineX);
        Assert.LessOrEqual(maxDecel, car.signalResponse.amberStopDeceleration + 0.2f, "never brakes harder than needed");
    }

    [Test]
    public void Queue_FormsBehindTheLine_ThenDischargesOnGreen()
    {
        var first = CreateCar(40f, 10f);
        var second = CreateCar(15f, 10f);
        for (float t = 0f; t < 25f; t += Dt)
        {
            first.Step(Dt);
            second.Step(Dt);
        }
        Assert.Less(first.CurrentSpeed, 0.01f);
        Assert.Less(second.CurrentSpeed, 0.01f);
        Assert.That(second.GapToLeader, Is.InRange(1f, 4f), "second car closes up behind the first");

        mStop.indication = SignalIndication.Green;
        for (float t = 0f; t < 15f; t += Dt)
        {
            first.Step(Dt);
            second.Step(Dt);
        }
        Assert.Greater(Front(first), mStop.lineX);
        Assert.Greater(Front(second), mStop.lineX);
        Assert.Greater(second.CurrentSpeed, 5f);
    }
}
