using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Known-situation tests for the measurement system (<see cref="MeasurementManager"/>, <see cref="MeasurementResult"/>,
/// <see cref="MeasurementEvent"/>): each test sets up a situation with a known answer and checks the measurement.
///
/// Test car: 4.5 m long and 2 m wide (front bumper 2.25 m ahead of its centre, half-width 1 m).
/// Test pedestrian: body radius 0.3 m. Runs are driven frame by frame with a manual clock; results are written to a
/// temporary folder, never to the real MeasurementResults folder.
/// </summary>
public class MeasurementTests
{
    const float Tol = 1e-3f;
    const float BodyRadius = 0.3f;
    const float StartClock = 100f;
    static readonly VehicleGeometry CarOutline = new VehicleGeometry { frontExtent = 2.25f, rearExtent = 2.25f, halfWidth = 1f };

    readonly List<GameObject> mObjects = new List<GameObject>();
    string mFolder;
    float mClock;
    MeasurementManager mManager;
    CrossingManager mCrossing;
    Transform mPedestrian;

    [SetUp]
    public void SetUp()
    {
        mFolder = Path.Combine(Path.GetTempPath(), "PedSimMeasurementTests_" + Guid.NewGuid().ToString("N"));
        MeasurementManager.resultsFolderOverride = mFolder;
        PedestrianTracker.SetTarget(null, -1f);
        mClock = StartClock;
    }

    [TearDown]
    public void TearDown()
    {
        foreach (GameObject go in mObjects)
            if (go != null)
                UnityEngine.Object.DestroyImmediate(go);
        mObjects.Clear();
        PedestrianTracker.SetTarget(null, -1f);
        MeasurementManager.resultsFolderOverride = null;
        if (Directory.Exists(mFolder))
            Directory.Delete(mFolder, true);
    }

    // ------------------------------------------------------------------ rig

    /// <summary>Starts a measured run with the pedestrian standing well away from any car.</summary>
    void StartRun(bool midpointFlip = true, string scenarioId = "S1", int seed = 1001)
    {
        var go = new GameObject("TestMeasurement");
        mObjects.Add(go);
        mCrossing = go.AddComponent<CrossingManager>();
        mCrossing.midpointFlipEnabled = midpointFlip;
        mCrossing.currentState = CrossingManager.CrossingState.CrossingToMidpoint;
        mManager = go.AddComponent<MeasurementManager>();
        mManager.crossingManager = mCrossing;
        mManager.clock = () => mClock;

        var ped = new GameObject("TestPedestrian");
        mObjects.Add(ped);
        mPedestrian = ped.transform;
        mPedestrian.position = new Vector3(0f, 0f, -50f);
        PedestrianTracker.SetTarget(mPedestrian, BodyRadius);

        mClock = StartClock;
        mManager.StartMeasuring(new ScenarioConfig { scenarioId = scenarioId, scenarioName = "Test Traffic", randomSeed = seed });
    }

    /// <summary>One measurement frame at <paramref name="t"/> seconds after the scenario started.</summary>
    void StepAt(float t)
    {
        mClock = StartClock + t;
        PedestrianTracker.Sample(mClock);
        mManager.Step();
    }

    void SetState(CrossingManager.CrossingState state, float t)
    {
        mCrossing.currentState = state;
        StepAt(t);
    }

    /// <summary>A stationary-outline car at <paramref name="position"/> driving along +x at <paramref name="speed"/>.</summary>
    CarMovement CreateCar(Vector3 position, float speed)
    {
        var go = new GameObject("TestCar");
        mObjects.Add(go);
        go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(Vector3.right));
        go.AddComponent<BoxCollider>().size = new Vector3(2f, 1.5f, 4.5f);
        var car = go.AddComponent<CarMovement>();
        car.SetInitialSpeed(speed);
        car.Step(0f); // registers the car and measures its outline without moving it
        return car;
    }

    MeasurementResult Result => mManager.LastResult;

    List<MeasurementEvent> EventsNamed(string name) => Result.events.Where(e => e.name == name).ToList();

    // ------------------------------------------------------------------ closest vehicle distance

    [Test]
    public void Distance_IsMeasuredToTheNearestEdgeOfTheCar_OnTheGround()
    {
        Vector3 car = Vector3.zero, forward = Vector3.right;
        Assert.AreEqual(3f, MeasurementManager.DistanceToOutline(new Vector3(5.25f, 0f, 0f), car, forward, CarOutline), Tol, "ahead of the bumper");
        Assert.AreEqual(3f, MeasurementManager.DistanceToOutline(new Vector3(-5.25f, 0f, 0f), car, forward, CarOutline), Tol, "behind");
        Assert.AreEqual(2f, MeasurementManager.DistanceToOutline(new Vector3(0f, 0f, 3f), car, forward, CarOutline), Tol, "beside");
        Assert.AreEqual(5f, MeasurementManager.DistanceToOutline(new Vector3(5.25f, 0f, 5f), car, forward, CarOutline), Tol, "off the corner (3-4-5)");
        Assert.AreEqual(0f, MeasurementManager.DistanceToOutline(Vector3.zero, car, forward, CarOutline), Tol, "inside the outline");
        Assert.AreEqual(3f, MeasurementManager.DistanceToOutline(new Vector3(5.25f, 1.7f, 0f), car, forward, CarOutline), Tol, "height is ignored");
        Assert.AreEqual(3f, MeasurementManager.DistanceToOutline(new Vector3(0f, 0f, 5.25f), car, Vector3.forward, CarOutline), Tol, "car facing another way");
    }

    // ------------------------------------------------------------------ TTC and relative speed

    [Test]
    public void Ttc_PedestrianStandingInPath_IsGapDividedByCarSpeed()
    {
        // Gap from bumper to body edge: 22.55 - 2.25 - 0.3 = 20 m; at 10 m/s that is 2 s.
        Assert.IsTrue(MeasurementManager.TryTimeToCollision(new Vector3(22.55f, 0f, 0.5f), Vector3.zero, BodyRadius,
            Vector3.zero, Vector3.right, CarOutline, 10f, out float ttc, out float relativeSpeed));
        Assert.AreEqual(2f, ttc, Tol);
        Assert.AreEqual(10f, relativeSpeed, Tol);
    }

    [Test]
    public void RelativeSpeed_DependsOnHowThePedestrianMovesAlongTheCarsDirection()
    {
        Vector3 ped = new Vector3(22.55f, 0f, 0f); // 20 m gap

        MeasurementManager.TryTimeToCollision(ped, new Vector3(0f, 0f, 1.3f), BodyRadius, Vector3.zero, Vector3.right, CarOutline, 10f, out float ttc, out float rel);
        Assert.AreEqual(10f, rel, Tol, "walking straight across adds nothing");
        Assert.AreEqual(2f, ttc, Tol);

        MeasurementManager.TryTimeToCollision(ped, new Vector3(-1.5f, 0f, 0f), BodyRadius, Vector3.zero, Vector3.right, CarOutline, 10f, out ttc, out rel);
        Assert.AreEqual(11.5f, rel, Tol, "walking towards the car");
        Assert.AreEqual(20f / 11.5f, ttc, Tol);

        MeasurementManager.TryTimeToCollision(ped, new Vector3(2f, 0f, 0f), BodyRadius, Vector3.zero, Vector3.right, CarOutline, 10f, out ttc, out rel);
        Assert.AreEqual(8f, rel, Tol, "walking away, in the car's direction");
        Assert.AreEqual(2.5f, ttc, Tol);
    }

    [Test]
    public void Ttc_IsNotCalculated_WhenThereIsNoConflict()
    {
        void AssertNone(Vector3 ped, Vector3 velocity, float carSpeed, string why)
        {
            Assert.IsFalse(MeasurementManager.TryTimeToCollision(ped, velocity, BodyRadius, Vector3.zero, Vector3.right, CarOutline,
                carSpeed, out float ttc, out float rel), why);
            Assert.IsNaN(ttc, why);
            Assert.IsNaN(rel, why);
        }

        AssertNone(new Vector3(20f, 0f, 1.31f), Vector3.zero, 10f, "beside the car's path (half-width 1 + body 0.3 = 1.3 m)");
        AssertNone(new Vector3(-10f, 0f, 0f), Vector3.zero, 10f, "behind the car");
        AssertNone(new Vector3(20f, 0f, 0f), Vector3.zero, 0f, "car stopped");
        AssertNone(new Vector3(2.5f, 0f, 0f), Vector3.zero, 10f, "already touching the bumper");
        AssertNone(new Vector3(20f, 0f, 0f), new Vector3(12f, 0f, 0f), 10f, "moving away faster than the car");
    }

    // ------------------------------------------------------------------ waiting time, crossing duration, outcome

    [Test]
    public void CompletedRun_WaitingTimeAndCrossingDuration_ExcludeTheHalfwayPause()
    {
        StartRun();
        StepAt(0f);
        StepAt(1f);
        mCrossing.ReachRoadEdge();
        StepAt(3f);                                                            // stepped off the kerb at 3 s
        SetState(CrossingManager.CrossingState.WaitingForTurn, 8f);            // pause 8 s ...
        SetState(CrossingManager.CrossingState.CrossingToEnd, 13f);            // ... to 13 s (5 s)
        SetState(CrossingManager.CrossingState.Completed, 17f);                // reached the end at 17 s

        Assert.IsFalse(mManager.IsMeasuring);
        Assert.AreEqual(3f, Result.waitingTime, Tol, "scenario start -> stepped off kerb");
        Assert.AreEqual(17f - 3f - 5f, Result.crossingDuration, Tol, "kerb -> end, minus the 5 s pause");
        Assert.AreEqual("Completed", Result.crossingOutcome);

        CollectionAssert.AreEqual(
            new[] { "Scenario started", "Stepped off kerb", "Midpoint reached", "Crossing resumed", "Reached end" },
            Result.events.Select(e => e.name));
        CollectionAssert.AreEqual(new[] { 0f, 3f, 8f, 13f, 17f }, Result.events.Select(e => e.time), new FloatComparer(Tol));
        Assert.AreEqual("pause started", EventsNamed("Midpoint reached")[0].details);
        Assert.AreEqual("pause lasted 5.00 s", EventsNamed("Crossing resumed")[0].details);
    }

    [Test]
    public void MidpointFlipOff_NoPause_IsLoggedAndNothingIsSubtracted()
    {
        StartRun(midpointFlip: false);
        StepAt(0f);
        mCrossing.ReachRoadEdge();
        StepAt(2f);
        SetState(CrossingManager.CrossingState.CrossingToEnd, 6f); // walked straight through the midpoint
        SetState(CrossingManager.CrossingState.Completed, 10f);

        Assert.AreEqual(8f, Result.crossingDuration, Tol);
        Assert.AreEqual("Off", Result.midpointFlip);
        Assert.AreEqual("midpoint flip off, no pause", EventsNamed("Midpoint reached")[0].details);
        Assert.IsEmpty(EventsNamed("Crossing resumed"));
    }

    [Test]
    public void StoppedRun_IsIncomplete_WithoutCrossingDuration()
    {
        StartRun();
        StepAt(0f);
        mCrossing.ReachRoadEdge();
        StepAt(2f);
        mClock = StartClock + 5f;
        mManager.StopMeasuring("stopped by test");

        Assert.AreEqual("Incomplete", Result.crossingOutcome);
        Assert.AreEqual(2f, Result.waitingTime, Tol, "they did step off the kerb");
        Assert.IsNaN(Result.crossingDuration, "they never reached the end");
        MeasurementEvent last = Result.events.Last();
        Assert.AreEqual("Scenario stopped before end", last.name);
        Assert.AreEqual(5f, last.time, Tol);
        Assert.AreEqual("stopped by test", last.details);
    }

    [Test]
    public void NeverSteppedOffTheKerb_LeavesWaitingTimeEmpty()
    {
        StartRun();
        StepAt(0f);
        StepAt(4f);
        mManager.StopMeasuring("stopped by test");

        Assert.IsNaN(Result.waitingTime);
        Assert.IsNaN(Result.crossingDuration);
        Assert.IsEmpty(EventsNamed("Stepped off kerb"));
    }

    // ------------------------------------------------------------------ vehicles during a run

    [Test]
    public void ClosestVehicleDistance_KeepsTheSmallestValueOfTheRun()
    {
        StartRun();
        CreateCar(Vector3.zero, 0f);
        mPedestrian.position = new Vector3(0f, 0f, 6f); StepAt(0f);    // 5 m from the side of the car
        mPedestrian.position = new Vector3(0f, 0f, 3f); StepAt(0.1f);  // 2 m
        mPedestrian.position = new Vector3(0f, 0f, 5f); StepAt(0.2f);  // 4 m
        mManager.StopMeasuring("stopped by test");

        Assert.AreEqual(2f, Result.closestVehicleDistance, Tol);
    }

    [Test]
    public void LowestTtc_IsReportedWithTheRelativeSpeedAtThatMoment()
    {
        StartRun();
        CarMovement car = CreateCar(Vector3.zero, 10f);
        mPedestrian.position = new Vector3(22.55f, 0f, 0f);
        StepAt(0f);                                  // TTC 20 m / 10 m/s = 2 s
        StepAt(0.1f);
        car.SetInitialSpeed(5f);
        mPedestrian.position = new Vector3(12.55f, 0f, 0f);
        PedestrianTracker.SetTarget(mPedestrian, BodyRadius); // forget the jump, so the pedestrian counts as standing still
        StepAt(0.2f);                                // TTC 10 m / 5 m/s = 2 s: not lower, not recorded
        car.SetInitialSpeed(8f);
        StepAt(0.3f);                                // TTC 10 m / 8 m/s = 1.25 s: lowest
        mManager.StopMeasuring("stopped by test");

        Assert.AreEqual(1.25f, Result.timeToCollision, Tol);
        Assert.AreEqual(8f, Result.relativeSpeed, Tol);
    }

    [Test]
    public void NearMiss_IsLoggedOncePerCarPerEncounter()
    {
        StartRun();
        CarMovement car = CreateCar(Vector3.zero, 10f);

        mPedestrian.position = new Vector3(30f, 0f, 0f);
        StepAt(0f);                                   // TTC (30 - 2.25 - 0.3) / 10 = 2.7 s: not a near miss

        mPedestrian.position = new Vector3(12.05f, 0f, 0f);
        PedestrianTracker.SetTarget(mPedestrian, BodyRadius);
        StepAt(0.1f);                                 // TTC (12.05 - 2.25 - 0.3) / 10 = 0.95 s: near miss
        StepAt(0.2f);
        StepAt(0.3f);                                 // same encounter: still one event

        mPedestrian.position = new Vector3(12.05f, 0f, 5f);
        PedestrianTracker.SetTarget(mPedestrian, BodyRadius);
        StepAt(0.4f);                                 // out of the car's path: encounter over
        mPedestrian.position = new Vector3(12.05f, 0f, 0f);
        PedestrianTracker.SetTarget(mPedestrian, BodyRadius);
        StepAt(0.5f);                                 // back in its path: a new encounter
        mManager.StopMeasuring("stopped by test");

        List<MeasurementEvent> nearMisses = EventsNamed("Near miss");
        Assert.AreEqual(2, nearMisses.Count);
        Assert.AreEqual(car.VehicleId, nearMisses[0].vehicleId);
        Assert.AreEqual(0.1f, nearMisses[0].time, Tol);
        Assert.AreEqual("TTC 0.95 s, relative speed 10.0 m/s", nearMisses[0].details);
        Assert.AreEqual(0.95f, Result.timeToCollision, Tol);
    }

    [Test]
    public void Collision_IsLoggedOncePerContact_AndSetsTheOutcome()
    {
        StartRun();
        CarMovement car = CreateCar(Vector3.zero, 0f);
        mCrossing.ReachRoadEdge();

        mPedestrian.position = new Vector3(0f, 0f, 1.2f); StepAt(0f);    // body edge 0.1 m inside the car: contact
        StepAt(0.1f);                                                     // still the same contact
        mPedestrian.position = new Vector3(0f, 0f, 1.35f); StepAt(0.2f); // 0.05 m clear: not clear enough to end the contact
        mPedestrian.position = new Vector3(0f, 0f, 1.2f); StepAt(0.3f);  // so no new event
        mPedestrian.position = new Vector3(0f, 0f, 1.6f); StepAt(0.4f);  // 0.3 m clear: contact over
        mPedestrian.position = new Vector3(0f, 0f, 1.2f); StepAt(0.5f);  // second contact
        SetState(CrossingManager.CrossingState.CrossingToEnd, 1f);
        SetState(CrossingManager.CrossingState.Completed, 2f);

        List<MeasurementEvent> collisions = EventsNamed("Collision");
        Assert.AreEqual(2, collisions.Count);
        Assert.AreEqual(car.VehicleId, collisions[0].vehicleId);
        CollectionAssert.AreEqual(new[] { 0f, 0.5f }, collisions.Select(e => e.time), new FloatComparer(Tol));
        Assert.AreEqual("Collision", Result.crossingOutcome, "a collision overrides Completed");
    }

    [Test]
    public void SafetyOverride_IsLoggedOnce_AsWouldHaveCollided()
    {
        StartRun();
        CarMovement car = CreateCar(Vector3.zero, 13.9f); // 50 km/h along +x
        var end = new GameObject("TestWaypoint");
        mObjects.Add(end);
        end.transform.position = new Vector3(1000f, 0f, 0f);
        car.waypoints = new[] { end.transform };

        // Pedestrian suddenly 0.9 m in front of the bumper: far too close for braking to stop the car in time,
        // so the car's hard safety envelope forces it to stop instead (in reality it would have hit them)
        mPedestrian.position = new Vector3(2.25f + 0.9f, 0f, 0f);
        StepAt(0f);
        Assert.AreEqual(0, car.SafetyOverrideCount, "no override before the car has moved");
        for (int i = 1; i <= 5; i++)
        {
            car.Step(0.02f);       // the override engages on the first step and stays engaged
            StepAt(i * 0.02f);
        }
        mManager.StopMeasuring("stopped by test");

        Assert.GreaterOrEqual(car.SafetyOverrideCount, 1, "the scenario must trigger the safety envelope");
        List<MeasurementEvent> overrides = EventsNamed("Safety override");
        Assert.AreEqual(1, overrides.Count, "one event for one engagement, however many frames it lasts");
        Assert.AreEqual(car.VehicleId, overrides[0].vehicleId);
        StringAssert.StartsWith("would have collided: car approaching at 13.9 m/s", overrides[0].details,
            "reports the approach speed before the forced stop, not the speed after it");
        Assert.IsEmpty(EventsNamed("Collision"), "the car stopped short, so there was no actual contact");
    }

    [Test]
    public void NothingIsMeasured_DuringTheHalfwayPause()
    {
        StartRun();
        CreateCar(Vector3.zero, 10f);
        mPedestrian.position = new Vector3(0f, 0f, 6f);
        StepAt(0f);                                                       // 5 m away
        mCrossing.currentState = CrossingManager.CrossingState.WaitingForTurn;
        mPedestrian.position = new Vector3(0f, 0f, 1.2f);                 // overlapping the car, but paused
        StepAt(1f);
        StepAt(2f);
        mManager.StopMeasuring("stopped by test");

        Assert.AreEqual(5f, Result.closestVehicleDistance, Tol);
        Assert.IsEmpty(EventsNamed("Collision"));
        Assert.AreEqual("Incomplete", Result.crossingOutcome);
    }

    // ------------------------------------------------------------------ reproducibility columns

    [Test]
    public void ReproducibilityColumns_AreRecorded()
    {
        StartRun(scenarioId: "S2", seed: 1002);
        mManager.StopMeasuring("stopped by test");

        Assert.AreEqual("S2", Result.scenarioId);
        Assert.AreEqual(1002, Result.randomSeed);
        Assert.AreEqual(Application.version, Result.appVersion);
        Assert.AreEqual(MeasurementManager.ScenarioSettingsCode(new ScenarioConfig { scenarioId = "S2", scenarioName = "Test Traffic", randomSeed = 1002 }),
            Result.scenarioSettings);
        Assert.AreEqual("On", Result.midpointFlip);
        Assert.AreEqual("Day", Result.lighting);
    }

    [Test]
    public void NightRun_IsRecordedAsNight_AndChangesTheSettingsCode()
    {
        StartRun(scenarioId: "S2", seed: 1002);
        mManager.StopMeasuring("day run");
        string dayCode = Result.scenarioSettings;

        // As ScenarioManager does when the researcher panel's Night option is ticked
        mManager.StartMeasuring(new ScenarioConfig { scenarioId = "S2", scenarioName = "Test Traffic", randomSeed = 1002, night = true });
        mManager.StopMeasuring("night run");

        Assert.AreEqual("Night", Result.lighting);
        Assert.AreNotEqual(dayCode, Result.scenarioSettings, "same traffic at night must not look identical in the CSV");
        StringAssert.EndsWith(", night", Result.events[0].details);
    }

    [Test]
    public void ScenarioSettingsCode_ChangesWhenAnySettingChanges()
    {
        var a = new ScenarioConfig { scenarioId = "S1", randomSeed = 1001 };
        var same = new ScenarioConfig { scenarioId = "S1", randomSeed = 1001 };
        var otherSeed = new ScenarioConfig { scenarioId = "S1", randomSeed = 1002 };
        var otherSpeed = new ScenarioConfig { scenarioId = "S1", randomSeed = 1001, spawnCarMaxSpeed = 13.1f };

        string code = MeasurementManager.ScenarioSettingsCode(a);
        StringAssert.IsMatch("^[0-9a-f]{6}$", code);
        Assert.AreEqual(code, MeasurementManager.ScenarioSettingsCode(same));
        Assert.AreNotEqual(code, MeasurementManager.ScenarioSettingsCode(otherSeed));
        Assert.AreNotEqual(code, MeasurementManager.ScenarioSettingsCode(otherSpeed));
    }

    // ------------------------------------------------------------------ CSV format

    [Test]
    public void ResultRow_HasOneCellPerColumn_EmptyCellsForNotApplicable_AndDecimalPoints()
    {
        CultureInfo previous = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE"); // uses ',' as the decimal separator
            var r = new MeasurementResult
            {
                scenario = "Low Traffic", scenarioId = "S1", run = 3, participantId = "P01", dateTime = "2026-10-01 14:02:11",
                appVersion = "0.2.0", randomSeed = 1001, scenarioSettings = "a3f9c2", midpointFlip = "On", lighting = "Night",
                waitingTime = 5.2f, crossingDuration = 9.4f, closestVehicleDistance = 2.345f, crossingOutcome = "Completed",
            };
            string row = r.ToCsvRow();

            Assert.AreEqual(MeasurementResult.CsvHeader.Split(',').Length, row.Split(',').Length);
            Assert.AreEqual("Low Traffic,S1,3,P01,2026-10-01 14:02:11,0.2.0,1001,a3f9c2,On,Night,5.20,9.40,2.35,,,Completed", row);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    [Test]
    public void TextWithCommasOrQuotes_StaysInOneCell()
    {
        var r = new MeasurementResult { run = 1, participantId = "P01, retest", scenario = "Say \"hi\"" };
        StringAssert.StartsWith("\"Say \"\"hi\"\"\",,1,\"P01, retest\",", r.ToCsvRow());
    }

    [Test]
    public void EventRow_IsLinkedToItsRun()
    {
        var r = new MeasurementResult { run = 4, participantId = "P02", scenarioId = "S3" };
        var e = new MeasurementEvent { time = 12.345f, name = "Near miss", vehicleId = 7, details = "TTC 1.20 s, relative speed 9.0 m/s" };
        var noVehicle = new MeasurementEvent { time = 3f, name = "Stepped off kerb" };

        Assert.AreEqual(MeasurementEvent.CsvHeader.Split(',').Length, noVehicle.ToCsvRow(r).Split(',').Length);
        Assert.AreEqual("4,P02,S3,12.35,Near miss,7,\"TTC 1.20 s, relative speed 9.0 m/s\"", e.ToCsvRow(r));
        Assert.AreEqual("4,P02,S3,3.00,Stepped off kerb,,", noVehicle.ToCsvRow(r));
    }

    // ------------------------------------------------------------------ files

    [Test]
    public void Files_AreWrittenWithHeaders_AndRunsAreNumbered()
    {
        StartRun();
        StepAt(0f);
        mManager.StopMeasuring("first");
        StartRun();
        StepAt(0f);
        mManager.StopMeasuring("second");

        string[] results = File.ReadAllLines(Path.Combine(mFolder, "measurement_results.csv"));
        Assert.AreEqual(MeasurementResult.CsvHeader, results[0]);
        Assert.AreEqual(3, results.Length, "header + 2 runs");
        StringAssert.StartsWith("Test Traffic,S1,1,", results[1]);
        StringAssert.StartsWith("Test Traffic,S1,2,", results[2]);

        string[] events = File.ReadAllLines(Path.Combine(mFolder, "measurement_events.csv"));
        Assert.AreEqual(MeasurementEvent.CsvHeader, events[0]);
        CollectionAssert.AreEqual(new[] { "1", "1", "2", "2" }, events.Skip(1).Select(l => l.Split(',')[0]),
            "each run's 'Scenario started' and 'Scenario stopped before end', linked by run number");
    }

    [Test]
    public void ExistingFileWithOldColumns_IsKept_AndANewFileIsStarted()
    {
        Directory.CreateDirectory(mFolder);
        string resultsFile = Path.Combine(mFolder, "measurement_results.csv");
        File.WriteAllText(resultsFile, "Scenario,Run,Waiting Time (s)\nLow Traffic,1,3.00\n");

        StartRun();
        mManager.StopMeasuring("stopped by test");

        string[] kept = Directory.GetFiles(mFolder, "measurement_results_old_columns_*.csv");
        Assert.AreEqual(1, kept.Length, "old file renamed, not deleted");
        StringAssert.Contains("Low Traffic,1,3.00", File.ReadAllText(kept[0]));
        string[] current = File.ReadAllLines(resultsFile);
        Assert.AreEqual(MeasurementResult.CsvHeader, current[0]);
        Assert.AreEqual(2, current.Length);
        StringAssert.StartsWith("Test Traffic,S1,1,", current[1], "run numbering restarts in the new file");
    }

    [Test]
    public void FileOpenElsewhere_RowIsSavedToASeparateFile_NotLost()
    {
        StartRun();
        mManager.StopMeasuring("first run");
        string resultsFile = Path.Combine(mFolder, "measurement_results.csv");

        using (new FileStream(resultsFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) // like Excel's lock
        {
            LogAssert.Expect(LogType.Error, new Regex("Could not write .*measurement_results.csv"));
            StartRun();
            mManager.StopMeasuring("second run");
        }

        string[] backups = Directory.GetFiles(mFolder, "measurement_results_2*.csv");
        Assert.AreEqual(1, backups.Length);
        string[] backup = File.ReadAllLines(backups[0]);
        Assert.AreEqual(MeasurementResult.CsvHeader, backup[0]);
        StringAssert.Contains("Incomplete", backup[1]);
        Assert.AreEqual(2, File.ReadAllLines(resultsFile).Length, "locked file left untouched");
        Assert.AreEqual(5, File.ReadAllLines(Path.Combine(mFolder, "measurement_events.csv")).Length,
            "header + 2 events per run: the events file was not locked, so both runs' events are there");
    }

    class FloatComparer : System.Collections.IComparer
    {
        readonly float mTolerance;
        public FloatComparer(float tolerance) => mTolerance = tolerance;
        public int Compare(object x, object y) => Mathf.Abs((float)x - (float)y) <= mTolerance ? 0 : ((float)x).CompareTo((float)y);
    }
}
