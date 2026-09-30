using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Controlled-case tests for the vehicle behaviour model. Real CarMovement components are stepped deterministically on a
/// straight road running along +x (car length 4.5 m, width 2 m; pedestrian radius 0.3 m, walking 1.3 m/s).
/// </summary>
public class VehicleBehaviourTests
{
    const float Dt = 0.02f;
    const float PedRadius = 0.3f;
    const float WalkSpeed = 1.3f;
    const float CarHalfWidth = 1f;
    const float CarFront = 2.25f;

    readonly List<GameObject> mObjects = new List<GameObject>();
    readonly List<CarMovement> mCars = new List<CarMovement>();
    PedestrianInteractionSettings mSettings;

    [SetUp]
    public void SetUp()
    {
        mSettings = new PedestrianInteractionSettings();
        PedestrianTracker.SetTarget(null, -1f);
    }

    [TearDown]
    public void TearDown()
    {
        foreach (GameObject go in mObjects)
            if (go != null)
                UnityEngine.Object.DestroyImmediate(go);
        mObjects.Clear();
        mCars.Clear();
        PedestrianTracker.SetTarget(null, -1f);
    }

    // ------------------------------------------------------------------ rig

    CarMovement CreateCar(float x, float speed, float desiredSpeed = 12f, float endX = 1000f)
    {
        var go = new GameObject("TestCar");
        mObjects.Add(go);
        go.transform.SetPositionAndRotation(new Vector3(x, 0f, 0f), Quaternion.LookRotation(Vector3.right));
        go.AddComponent<BoxCollider>().size = new Vector3(2f, 1.5f, 4.5f);
        var car = go.AddComponent<CarMovement>();
        car.idm = IdmParameters.UrbanDefault;
        car.idm.desiredSpeed = desiredSpeed;
        car.pedestrianInteraction = mSettings;
        car.maxDeceleration = 8f;
        var end = new GameObject("TestWaypoint");
        mObjects.Add(end);
        end.transform.position = new Vector3(endX, 0f, 0f);
        car.waypoints = new[] { end.transform };
        car.SetInitialSpeed(speed);
        mCars.Add(car);
        return car;
    }

    Transform CreatePedestrian(Vector3 position)
    {
        var go = new GameObject("TestPedestrian");
        mObjects.Add(go);
        go.transform.position = position;
        PedestrianTracker.SetTarget(go.transform, PedRadius);
        return go.transform;
    }

    class Trace
    {
        public float minSpeed = float.PositiveInfinity;
        public float maxDeceleration;
        public float maxAcceleration = float.NegativeInfinity;
        public float minClearance = float.PositiveInfinity; // front bumper to pedestrian edge, while pedestrian is in the car's physical path ahead
        public bool collided;
        public float finalSpeed;
        public readonly HashSet<PedestrianConflictState> states = new HashSet<PedestrianConflictState>();
        public int safetyOverrides;
    }

    /// <summary>
    /// Runs the simulation for one car (others are stepped too); pedestrianAt(t) returns the pedestrian position.
    /// During the pre-roll (t &lt; 0) only the pedestrian tracker is sampled, so its velocity estimate is settled at t = 0.
    /// </summary>
    Trace Run(CarMovement car, float duration, Transform ped = null, Func<float, Vector3> pedestrianAt = null, float preroll = 1f)
    {
        var trace = new Trace();
        for (float t = -preroll; t < duration; t += Dt)
        {
            if (ped != null && pedestrianAt != null)
                ped.position = pedestrianAt(t);
            PedestrianTracker.Sample(t);
            if (t < 0f)
                continue;

            foreach (CarMovement c in mCars.ToArray())
                if (c != null)
                    c.Step(Dt);

            if (car == null)
                break;

            trace.minSpeed = Mathf.Min(trace.minSpeed, car.CurrentSpeed);
            trace.maxDeceleration = Mathf.Max(trace.maxDeceleration, -car.AppliedAcceleration);
            trace.maxAcceleration = Mathf.Max(trace.maxAcceleration, car.AppliedAcceleration);
            trace.states.Add(car.PedestrianConflict.state);
            trace.finalSpeed = car.CurrentSpeed;
            trace.safetyOverrides = car.SafetyOverrideCount;

            if (ped != null)
            {
                Vector3 p = ped.position;
                float carX = car.transform.position.x;
                bool inPhysicalPath = Mathf.Abs(p.z) < CarHalfWidth + PedRadius;
                if (inPhysicalPath && p.x > carX)
                {
                    float clearance = p.x - PedRadius - (carX + CarFront);
                    trace.minClearance = Mathf.Min(trace.minClearance, clearance);
                    if (clearance < 0f)
                        trace.collided = true;
                }
            }
        }
        return trace;
    }

    /// <summary>Pedestrian crossing the road at x = crossX from lateral z0 towards -z at WalkSpeed, starting at startTime.</summary>
    static Func<float, Vector3> Crossing(float crossX, float z0, float startTime = 0f, float speed = WalkSpeed, float zEnd = -8f)
    {
        return t => new Vector3(crossX, 0f, Mathf.Max(zEnd, z0 - speed * Mathf.Max(0f, t - startTime)));
    }

    // ------------------------------------------------------------------ IDM

    [Test]
    public void Idm_FreeRoad_MaxAccelerationAtRest_ZeroAtDesiredSpeed()
    {
        var p = IdmParameters.UrbanDefault;
        Assert.AreEqual(p.maxAcceleration, IntelligentDriverModel.Acceleration(0f, IntelligentDriverModel.NoLeader, 0f, p), 1e-5f);
        Assert.AreEqual(0f, IntelligentDriverModel.Acceleration(p.desiredSpeed, IntelligentDriverModel.NoLeader, 0f, p), 1e-5f);
    }

    [Test]
    public void Idm_EquilibriumGap_GivesZeroAcceleration()
    {
        var p = IdmParameters.UrbanDefault;
        float v = 8f;
        float sEq = (p.minimumGap + v * p.timeHeadway) / Mathf.Sqrt(1f - Mathf.Pow(v / p.desiredSpeed, p.accelerationExponent));
        Assert.AreEqual(0f, IntelligentDriverModel.Acceleration(v, sEq, 0f, p), 1e-4f);
    }

    [Test]
    public void Idm_ClosingOnStoppedVehicle_BrakesHarderThanComfortable()
    {
        var p = IdmParameters.UrbanDefault;
        float a = IntelligentDriverModel.Acceleration(12f, 20f, 12f, p);
        Assert.Less(a, -p.comfortableDeceleration);
    }

    // ------------------------------------------------------------------ path projection

    [Test]
    public void Path_ProjectsAlongAndLateral_IncludingPastACorner()
    {
        var path = new[] { Vector3.zero, new Vector3(20f, 0f, 0f), new Vector3(20f, 0f, 30f) };
        Assert.IsTrue(VehiclePath.TryProject(path, 3, new Vector3(10f, 0f, -2f), 100f, out var a));
        Assert.AreEqual(10f, a.along, 1e-4f);
        Assert.AreEqual(2f, Mathf.Abs(a.lateral), 1e-4f);

        Assert.IsTrue(VehiclePath.TryProject(path, 3, new Vector3(18f, 0f, 10f), 100f, out var b));
        Assert.AreEqual(30f, b.along, 1e-4f);
        Assert.AreEqual(2f, Mathf.Abs(b.lateral), 1e-4f);

        // Straight ahead of the car's initial heading but 8 m off the turning route: must not look like "in path".
        Assert.IsTrue(VehiclePath.TryProject(path, 3, new Vector3(28f, 0f, 0f), 100f, out var c));
        Assert.AreEqual(8f, Mathf.Abs(c.lateral), 1e-4f);

        Assert.IsTrue(VehiclePath.TryProject(path, 3, new Vector3(-5f, 0f, 0f), 100f, out var d));
        Assert.AreEqual(-5f, d.along, 1e-4f);
    }

    // ------------------------------------------------------------------ car following / free flow

    [Test]
    public void FreeFlow_NoPedestrian_AcceleratesSmoothlyToDesiredSpeed()
    {
        var car = CreateCar(0f, 0f);
        var trace = Run(car, 45f);
        Assert.Greater(trace.finalSpeed, 0.95f * 12f);
        Assert.LessOrEqual(trace.finalSpeed, 12f + 1e-3f);
        Assert.LessOrEqual(trace.maxAcceleration, car.idm.maxAcceleration + 1e-3f);
        CollectionAssert.AreEquivalent(new[] { PedestrianConflictState.None }, trace.states);
    }

    [Test]
    public void Following_SlowerLeader_ConvergesToIdmEquilibriumWithoutCollision()
    {
        var leader = CreateCar(40f, 8f, desiredSpeed: 8f);
        var follower = CreateCar(0f, 12f, desiredSpeed: 12f);
        float minGap = float.PositiveInfinity;
        for (float t = 0f; t < 90f; t += Dt)
        {
            leader.Step(Dt);
            follower.Step(Dt);
            minGap = Mathf.Min(minGap, follower.GapToLeader);
        }

        var p = follower.idm;
        float sEq = (p.minimumGap + 8f * p.timeHeadway) / Mathf.Sqrt(1f - Mathf.Pow(8f / 12f, 4f));
        Assert.AreEqual(8f, follower.CurrentSpeed, 0.2f);
        Assert.AreEqual(sEq, follower.GapToLeader, 1f);
        Assert.Greater(minGap, p.minimumGap);
    }

    [Test]
    public void Following_LeaderStopsSuddenly_FollowerStopsBehindIt()
    {
        var leader = CreateCar(40f, 8f, desiredSpeed: 8f);
        var follower = CreateCar(0f, 8f, desiredSpeed: 12f);
        float minGap = float.PositiveInfinity;
        for (float t = 0f; t < 60f; t += Dt)
        {
            if (Mathf.Abs(t - 20f) < Dt * 0.5f)
                leader.SetStopped(true); // like a traffic-light stop line
            leader.Step(Dt);
            follower.Step(Dt);
            minGap = Mathf.Min(minGap, follower.GapToLeader);
        }
        Assert.Less(follower.CurrentSpeed, 0.05f);
        Assert.Greater(minGap, 1f);
    }

    // ------------------------------------------------------------------ pedestrian cases

    [Test]
    public void A_PedestrianAbsent_NormalIdm()
    {
        var car = CreateCar(0f, 12f);
        var trace = Run(car, 10f);
        Assert.Greater(trace.minSpeed, 11.9f);
        CollectionAssert.AreEquivalent(new[] { PedestrianConflictState.None }, trace.states);
    }

    [Test]
    public void B_PedestrianFarFromRoad_OrWaitingAtKerb_CarContinues()
    {
        var car = CreateCar(0f, 12f);
        var ped = CreatePedestrian(new Vector3(50f, 0f, 2.5f)); // standing at the kerb, just outside the corridor
        var trace = Run(car, 8f, ped, t => new Vector3(50f, 0f, 2.5f));
        Assert.Greater(trace.minSpeed, 11.9f);
        Assert.IsFalse(trace.states.Contains(PedestrianConflictState.Yielding));
        Assert.IsFalse(trace.states.Contains(PedestrianConflictState.CriticalBraking));
    }

    [Test]
    public void B_PedestrianApproachingButCarClearsFirst_CarContinues()
    {
        // Car front 30 m from the crossing at 12 m/s; pedestrian 8 m from the lane centre walking in: car clears ~2.9 s, pedestrian arrives ~4.8 s.
        var car = CreateCar(0f, 12f);
        var ped = CreatePedestrian(new Vector3(32.25f, 0f, 8f));
        var trace = Run(car, 6f, ped, Crossing(32.25f, 8f));
        Assert.Greater(trace.minSpeed, 11.9f, "car should not brake when it can safely pass first");
        Assert.IsTrue(trace.states.Contains(PedestrianConflictState.VehiclePassesFirst));
        Assert.IsFalse(trace.states.Contains(PedestrianConflictState.Yielding));
        Assert.IsFalse(trace.collided);
    }

    [Test]
    public void PedestrianAlmostAcross_CarFarAway_CarContinues()
    {
        // Pedestrian is already walking out of the lane (at z = -1.2 m when t = 0) and the car is ~55 m away.
        var car = CreateCar(0f, 12f);
        var ped = CreatePedestrian(new Vector3(57.25f, 0f, 0.1f));
        var trace = Run(car, 6f, ped, Crossing(57.25f, 0.1f, startTime: -1f));
        Assert.Greater(trace.minSpeed, 11.5f);
        Assert.IsTrue(trace.states.Contains(PedestrianConflictState.PedestrianPassesFirst));
        Assert.IsFalse(trace.states.Contains(PedestrianConflictState.CriticalBraking));
    }

    [Test]
    public void C_PedestrianStartsCrossing_PlentyOfDistance_ProgressiveComfortableBraking()
    {
        var car = CreateCar(0f, 12f);
        var ped = CreatePedestrian(new Vector3(57.25f, 0f, 3f)); // car front 55 m from the crossing
        var trace = Run(car, 20f, ped, Crossing(57.25f, 3f));
        Assert.IsTrue(trace.states.Contains(PedestrianConflictState.Yielding), "car should respond to the conflict");
        Assert.IsFalse(trace.states.Contains(PedestrianConflictState.CriticalBraking));
        Assert.LessOrEqual(trace.maxDeceleration, car.idm.comfortableDeceleration + 0.05f, "no harsh braking needed");
        Assert.Greater(trace.minSpeed, 2f, "a light slow-down is enough; no need to stop");
        Assert.Greater(trace.finalSpeed, 11f, "resumes after the pedestrian has cleared");
        Assert.IsFalse(trace.collided);
        Assert.AreEqual(0, trace.safetyOverrides);
    }

    [Test]
    public void D_SimultaneousArrival_CarYieldsAndPedestrianCrossesSafely()
    {
        // Car front 30 m away at 12 m/s (arrives ~2.5 s); pedestrian 3.5 m out enters the corridor at ~1.3 s and leaves at ~4 s.
        var car = CreateCar(0f, 12f);
        var ped = CreatePedestrian(new Vector3(32.25f, 0f, 3.5f));
        var trace = Run(car, 25f, ped, Crossing(32.25f, 3.5f));
        Assert.IsTrue(trace.states.Contains(PedestrianConflictState.Yielding) || trace.states.Contains(PedestrianConflictState.CriticalBraking));
        Assert.Greater(trace.maxDeceleration, 1.5f, "must brake noticeably");
        Assert.LessOrEqual(trace.maxDeceleration, 5f, "yielding with 30 m available should not need emergency braking");
        Assert.Greater(trace.minClearance, 1f);
        Assert.IsFalse(trace.collided);
        Assert.AreEqual(0, trace.safetyOverrides);
        Assert.Greater(trace.finalSpeed, 11f, "F: resumes normal IDM driving afterwards");
    }

    [Test]
    public void E_PedestrianSuddenlyEntersPath_StrongBrakingStopsBeforePedestrian_ThenF_Recovers()
    {
        // Pedestrian at the kerb 20 m ahead steps briskly into the lane, stands there for 4 s, then finishes crossing.
        var car = CreateCar(0f, 12f);
        var ped = CreatePedestrian(new Vector3(22.25f, 0f, 2.2f));
        Func<float, Vector3> motion = t =>
        {
            float z;
            if (t < 1.4f) z = Mathf.Max(0f, 2.2f - 1.6f * t);    // steps in at 1.6 m/s, stops at the lane centre
            else if (t < 5.4f) z = 0f;                           // stands in the path
            else z = Mathf.Max(-8f, -WalkSpeed * (t - 5.4f));   // walks on and clears
            return new Vector3(22.25f, 0f, z);
        };
        float speedWhileStanding = float.PositiveInfinity;
        var trace = new Trace();
        for (float t = 0f; t < 30f; t += Dt)
        {
            ped.position = motion(t);
            PedestrianTracker.Sample(t);
            car.Step(Dt);
            trace.maxDeceleration = Mathf.Max(trace.maxDeceleration, -car.AppliedAcceleration);
            trace.states.Add(car.PedestrianConflict.state);
            if (t > 4f && t < 5.4f) speedWhileStanding = Mathf.Min(speedWhileStanding, car.CurrentSpeed);
            if (Mathf.Abs(ped.position.z) < CarHalfWidth + PedRadius && ped.position.x > car.transform.position.x)
                trace.minClearance = Mathf.Min(trace.minClearance, ped.position.x - PedRadius - (car.transform.position.x + CarFront));
        }

        Assert.IsTrue(trace.states.Contains(PedestrianConflictState.CriticalBraking));
        Assert.Greater(trace.maxDeceleration, car.idm.comfortableDeceleration, "stronger than comfortable braking");
        Assert.LessOrEqual(trace.maxDeceleration, car.maxDeceleration + 1e-3f);
        Assert.Less(speedWhileStanding, 0.01f, "car is stopped while the pedestrian stands in the path");
        Assert.Greater(trace.minClearance, mSettings.hardSafetyClearance, "stops before reaching the pedestrian");
        Assert.AreEqual(0, car.SafetyOverrideCount, "behavioural braking alone should suffice here");
        Assert.Greater(car.CurrentSpeed, 10f, "F: pulls away again once the pedestrian clears");
    }

    [Test]
    public void PedestrianStandingInLane_CarWaitsIndefinitely_ThenResumes()
    {
        var car = CreateCar(0f, 12f);
        var ped = CreatePedestrian(new Vector3(45f, 0f, 0.4f));
        var trace = Run(car, 40f, ped, t => t < 25f ? new Vector3(45f, 0f, 0.4f) : new Vector3(45f, 0f, Mathf.Max(-8f, 0.4f - WalkSpeed * (t - 25f))));
        Assert.IsFalse(trace.collided);
        Assert.Less(trace.minSpeed, 0.01f);
        Assert.Greater(trace.minClearance, 1f);
        Assert.Greater(trace.finalSpeed, 8f);
    }

    [Test]
    public void PhysicallyUnavoidableEntry_SafetyEnvelopePreventsContact()
    {
        // Pedestrian appears 6 m ahead at 12 m/s: stopping needs 9 m even at 8 m/s^2, so behavioural braking cannot prevent contact.
        var car = CreateCar(0f, 12f);
        var ped = CreatePedestrian(new Vector3(50f, 0f, 30f)); // well away from the road until it appears
        bool placed = false;
        var trace = new Trace();
        for (float t = 0f; t < 10f; t += Dt)
        {
            if (!placed && car.transform.position.x > 20f)
            {
                ped.position = new Vector3(car.transform.position.x + CarFront + 6f + PedRadius, 0f, 0f);
                placed = true;
            }
            PedestrianTracker.Sample(t);
            car.Step(Dt);
            if (placed)
                trace.minClearance = Mathf.Min(trace.minClearance, ped.position.x - PedRadius - (car.transform.position.x + CarFront));
        }
        Assert.Greater(trace.minClearance, 0f, "vehicle must never make contact with the pedestrian");
        Assert.GreaterOrEqual(car.SafetyOverrideCount, 1, "override should be flagged for researchers");
    }

    [Test]
    public void TurningCar_IgnoresPedestrianStraightAheadButOffItsRoute()
    {
        var car = CreateCar(0f, 8f, desiredSpeed: 8f);
        var corner = new GameObject("Corner"); mObjects.Add(corner);
        corner.transform.position = new Vector3(20f, 0f, 0f);
        var end = new GameObject("End"); mObjects.Add(end);
        end.transform.position = new Vector3(20f, 0f, 200f);
        car.waypoints = new[] { corner.transform, end.transform };
        var ped = CreatePedestrian(new Vector3(30f, 0f, 0f)); // on the footpath beyond the corner
        var trace = Run(car, 2f, ped, t => new Vector3(30f, 0f, 0f));
        Assert.Greater(trace.minSpeed, 7.9f);
        Assert.IsFalse(trace.states.Contains(PedestrianConflictState.Yielding));
    }

    // ------------------------------------------------------------------ arrivals, variation, densities

    [Test]
    public void Headways_RespectBounds_MatchAnalyticMean_AndAreIrregular()
    {
        var rng = new System.Random(42);
        var samples = Enumerable.Range(0, 20000).Select(_ => TrafficArrivalModel.SampleHeadway(rng.NextDouble(), 2f, 3.5f, 5f, 3f)).ToArray();
        Assert.GreaterOrEqual(samples.Min(), 2f);
        Assert.LessOrEqual(samples.Max(), 5f);
        Assert.AreEqual(TrafficArrivalModel.ExpectedHeadway(2f, 3.5f, 5f, 3f), samples.Average(), 0.03f);
        Assert.Greater(samples.Select(s => (s - samples.Average()) * (s - samples.Average())).Average(), 0.3f, "not evenly spaced");
        Assert.AreEqual(3f, TrafficArrivalModel.SampleHeadway(0.7, 5f, 0f, 5f, 3f), 1e-6f, "degenerate range = fixed headway");
    }

    [Test]
    public void ArrivalPlans_AreReproducibleFromSeed_AndDifferAcrossSeedsAndStreams()
    {
        List<VehicleArrivalPlan> Draw(int seed, int stream)
        {
            var rng = new System.Random(TrafficArrivalModel.DeriveSeed(seed, stream));
            return Enumerable.Range(0, 50).Select(_ => TrafficArrivalModel.DrawPlan(rng, 4, 0.3f, true, 1f, 3f, 5f, 3f,
                IdmParameters.UrbanDefault, new DriverVariation(), 10f, 13f)).ToList();
        }

        var a = Draw(7, 0);
        var b = Draw(7, 0);
        for (int i = 0; i < a.Count; i++)
        {
            Assert.AreEqual(a[i].headwayAfter, b[i].headwayAfter);
            Assert.AreEqual(a[i].prefabIndex, b[i].prefabIndex);
            Assert.AreEqual(a[i].useAlternateRoute, b[i].useAlternateRoute);
            Assert.AreEqual(a[i].idm.desiredSpeed, b[i].idm.desiredSpeed);
            Assert.AreEqual(a[i].idm.timeHeadway, b[i].idm.timeHeadway);
        }
        Assert.AreNotEqual(a.Select(p => p.headwayAfter), Draw(8, 0).Select(p => p.headwayAfter));
        Assert.AreNotEqual(a.Select(p => p.headwayAfter), Draw(7, 1).Select(p => p.headwayAfter));
    }

    [Test]
    public void DriverVariation_IsModestAndWithinConfiguredBounds()
    {
        var baseIdm = IdmParameters.UrbanDefault;
        var variation = new DriverVariation();
        var rng = new System.Random(1);
        for (int i = 0; i < 1000; i++)
        {
            var p = TrafficArrivalModel.ApplyDriverVariation(baseIdm, variation, 10f, 13f, rng.NextDouble(), rng.NextDouble(), rng.NextDouble(), rng.NextDouble());
            Assert.That(p.desiredSpeed, Is.InRange(10f, 13f));
            Assert.That(p.timeHeadway, Is.InRange(baseIdm.timeHeadway * 0.85f - 1e-4f, baseIdm.timeHeadway * 1.15f + 1e-4f));
            Assert.That(p.maxAcceleration, Is.InRange(baseIdm.maxAcceleration * 0.85f - 1e-4f, baseIdm.maxAcceleration * 1.15f + 1e-4f));
            Assert.That(p.comfortableDeceleration, Is.InRange(baseIdm.comfortableDeceleration * 0.9f - 1e-4f, baseIdm.comfortableDeceleration * 1.1f + 1e-4f));
        }
    }

    [Test]
    public void EntrySpeed_ReducedBehindCloseLeader_FullOnEmptyRoad()
    {
        var p = IdmParameters.UrbanDefault;
        Assert.AreEqual(p.desiredSpeed, TrafficArrivalModel.ComputeEntrySpeed(IntelligentDriverModel.NoLeader, 0f, p));
        float v = TrafficArrivalModel.ComputeEntrySpeed(6f, 0f, p);
        Assert.Less(v, 3f);
        Assert.GreaterOrEqual(IntelligentDriverModel.Acceleration(v, 6f, v, p), -p.comfortableDeceleration - 1e-3f);
    }

    [TestCase(4f, 5.5f, 7f, 6f, 9f, TestName = "Density_Low")]
    [TestCase(2f, 3.5f, 5f, 8f, 12f, TestName = "Density_Medium")]
    [TestCase(1f, 1.75f, 2.5f, 8f, 12f, TestName = "Density_High")]
    public void TrafficDensity_SingleLane_NoOverlaps_NoGridlock_FlowScalesWithDemand(float hMin, float hMean, float hMax, float vMin, float vMax)
    {
        // Mirrors TrafficSpawner: seeded arrivals wait for a clear entry, then enter at the IDM-safe speed. A pedestrian-free 400 m lane.
        var rng = new System.Random(TrafficArrivalModel.DeriveSeed(123, 0));
        const float minimumSpawnGap = 5f, duration = 300f;
        float nextArrival = 0f;
        VehicleArrivalPlan? pending = null;
        int spawned = 0, exited = 0;
        float minGap = float.PositiveInfinity;

        for (float t = 0f; t < duration; t += 0.05f)
        {
            if (pending == null && t >= nextArrival)
                pending = TrafficArrivalModel.DrawPlan(rng, 1, 0f, false, hMin, hMean, hMax, 3f, IdmParameters.UrbanDefault, new DriverVariation(), vMin, vMax);

            if (pending != null)
            {
                var live = mCars.Where(c => c != null).ToList();
                CarMovement last = live.OrderBy(c => c.transform.position.x).FirstOrDefault();
                float gap = last == null ? IntelligentDriverModel.NoLeader : last.transform.position.x - last.Geometry.rearExtent - CarFront;
                if (gap >= minimumSpawnGap)
                {
                    var plan = pending.Value;
                    var car = CreateCar(0f, 0f, endX: 400f);
                    car.idm = plan.idm;
                    car.SetInitialSpeed(TrafficArrivalModel.ComputeEntrySpeed(gap, last == null ? 0f : last.CurrentSpeed, plan.idm));
                    spawned++;
                    nextArrival = t + plan.headwayAfter;
                    pending = null;
                }
            }

            foreach (CarMovement c in mCars.ToArray())
            {
                if (c == null) continue;
                c.Step(0.05f);
                if (c == null) { exited++; continue; }
                if (!float.IsPositiveInfinity(c.GapToLeader))
                    minGap = Mathf.Min(minGap, c.GapToLeader);
            }
            mCars.RemoveAll(c => c == null);
        }

        // Demand above lane capacity cannot be served: arrivals queue at the entry. Rough IDM capacity: one vehicle per T + (s0 + L) / v.
        var idm = IdmParameters.UrbanDefault;
        float laneCapacity = duration / (idm.timeHeadway + (idm.minimumGap + 4.5f) / (0.5f * (vMin + vMax)));
        float expectedArrivals = duration / TrafficArrivalModel.ExpectedHeadway(hMin, hMean, hMax, 3f);
        Debug.Log($"[Density {hMean}s] spawned={spawned} (demand ~{expectedArrivals:F0}, lane capacity ~{laneCapacity:F0}) exited={exited} minGap={minGap:F2} m");
        Assert.Greater(minGap, 1f, "vehicles never overlap");
        Assert.Greater(exited, spawned * 0.7f, "traffic keeps flowing (no gridlock)");
        Assert.Greater(spawned, Mathf.Min(expectedArrivals, laneCapacity) * 0.7f);
        Assert.LessOrEqual(spawned, expectedArrivals * 1.2f + 2f);
    }

    [Test]
    public void ScenarioConfig_MissingJsonFields_KeepDefaults()
    {
        var config = JsonUtility.FromJson<ScenarioConfig>("{\"scenarioName\":\"x\",\"spawnIntervalMin\":4.0}");
        Assert.AreEqual(4f, config.spawnIntervalMin);
        Assert.AreEqual(1.5f, config.carTimeHeadway);
        Assert.AreEqual(12345, config.randomSeed);
        Assert.AreEqual(12f, config.carSpeed);
    }
}
