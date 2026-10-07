using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

/// <summary>
/// Measures one scenario run. When the run ends it adds one row to MeasurementResults/measurement_results.csv and the
/// run's events (with times) to MeasurementResults/measurement_events.csv.
///
///   Scenario started            -> fresh measurements, clock starts at 0
///   every frame                 -> closest vehicle distance, TTC + relative speed, collision, near-miss and safety-override checks
///   pedestrian reaches End      -> run ends, outcome Completed (or Collision)
///   scenario stopped / restarted / app closed before End -> run ends, outcome Incomplete (or Collision)
///
/// Nothing is measured during the artificial halfway pause (traffic and pedestrian are frozen then).
/// Added automatically by <see cref="ScenarioManager"/>.
/// </summary>
public class MeasurementManager : MonoBehaviour
{
    [Tooltip("Participant code written in every row (e.g. P01). Can be typed in the researcher panel.")]
    public string participantId = "";

    public ScenarioManager scenarioManager;
    public CrossingManager crossingManager;

    [Tooltip("A 'Near miss' event is logged when a car's TTC drops below this (s). Logged once per car per encounter.")]
    [Min(0f)] public float nearMissTtc = 1.5f;

    // A car must be closing in faster than this (m/s) for TTC to be calculated; below it the car is effectively stopped.
    const float MinClosingSpeed = 0.1f;
    // Contact ends once the body is this far clear of the car (m), so brushing the edge does not log many collisions.
    const float ContactReleaseMargin = 0.1f;

    MeasurementResult result;
    bool measuring;
    float startTime;
    float roadEdgeTime = float.NaN;   // seconds after scenario start when the pedestrian stepped off the kerb
    float pauseStartTime;
    float pauseTotal;                 // total length of the artificial halfway pause (s)
    bool collided;
    CrossingManager.CrossingState lastState;
    readonly HashSet<int> carsInContact = new HashSet<int>();  // cars currently touching the pedestrian
    readonly HashSet<int> carsInNearMiss = new HashSet<int>(); // cars in an ongoing near-miss encounter
    readonly Dictionary<int, int> safetyOverridesSeen = new Dictionary<int, int>(); // car -> safety overrides already logged
    readonly Dictionary<int, float> lastCarSpeed = new Dictionary<int, float>();    // car -> speed in the previous frame (m/s)

    public bool IsMeasuring => measuring;
    /// <summary>Results of the most recently finished run (also written to the CSV files).</summary>
    public MeasurementResult LastResult { get; private set; }

    /// <summary>Time source (s). Normally Unity's game time; tests replace it to control time frame by frame.</summary>
    public Func<float> clock = () => Time.time;

    /// <summary>Tests only: write the CSV files here instead of the real results folder.</summary>
    public static string resultsFolderOverride;

    public static string ResultsFolder => !string.IsNullOrEmpty(resultsFolderOverride) ? resultsFolderOverride
        : Application.isEditor
            ? Path.Combine(Application.dataPath, "..", "MeasurementResults")     // next to the Unity project
            : Path.Combine(Application.persistentDataPath, "MeasurementResults"); // headset / built app
    public static string ResultsFile => Path.GetFullPath(Path.Combine(ResultsFolder, "measurement_results.csv"));
    public static string EventsFile => Path.GetFullPath(Path.Combine(ResultsFolder, "measurement_events.csv"));

    void OnEnable()
    {
        if (scenarioManager == null)
            scenarioManager = FindFirstObjectByType<ScenarioManager>();
        if (crossingManager == null)
            crossingManager = FindFirstObjectByType<CrossingManager>();

        if (scenarioManager != null)
        {
            scenarioManager.ScenarioStarted += StartMeasuring;
            scenarioManager.ScenarioStopped += OnScenarioStopped;
        }
    }

    void OnDisable()
    {
        if (scenarioManager != null)
        {
            scenarioManager.ScenarioStarted -= StartMeasuring;
            scenarioManager.ScenarioStopped -= OnScenarioStopped;
        }
    }

    void OnApplicationQuit()
    {
        if (measuring)
            FinishMeasuring("Incomplete", "application closed");
    }

    void OnScenarioStopped() => StopMeasuring("scenario stopped or restarted by the researcher");

    /// <summary>Ends the current run as Incomplete (the scenario stopped before the participant reached the end).</summary>
    public void StopMeasuring(string reason)
    {
        if (measuring)
            FinishMeasuring("Incomplete", reason);
    }

    /// <summary>Starts a fresh run. Called when a scenario starts.</summary>
    public void StartMeasuring(ScenarioConfig scenario)
    {
        result = new MeasurementResult
        {
            scenario = string.IsNullOrWhiteSpace(scenario.scenarioName) ? scenario.scenarioId : scenario.scenarioName,
            scenarioId = scenario.scenarioId,
            participantId = participantId,
            dateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            appVersion = Application.version,
            randomSeed = scenario.randomSeed,
            scenarioSettings = ScenarioSettingsCode(scenario),
            // Recorded at the start of the run (the researcher panel locks the setting while a run is being measured)
            midpointFlip = crossingManager == null ? "" : crossingManager.midpointFlipEnabled ? "On" : "Off",
        };
        startTime = clock();
        roadEdgeTime = float.NaN;
        pauseTotal = 0f;
        collided = false;
        carsInContact.Clear();
        carsInNearMiss.Clear();
        safetyOverridesSeen.Clear();
        lastCarSpeed.Clear();
        lastState = crossingManager != null ? crossingManager.currentState : CrossingManager.CrossingState.NotStarted;
        measuring = true;
        LogEvent(0f, "Scenario started", -1, FormattableString.Invariant($"{result.scenario}, seed {result.randomSeed}, midpoint flip {result.midpointFlip}"));
        Debug.Log($"[MeasurementManager] Measuring '{result.scenario}' for participant '{participantId}'.", this);
    }

    void Update() => Step();

    /// <summary>One measurement step (every frame). Public so tests can drive it.</summary>
    public void Step()
    {
        if (!measuring || crossingManager == null)
            return;

        float now = clock() - startTime;

        // --- Waiting time: the moment the pedestrian first steps off the kerb (RoadEdge trigger) ---
        if (float.IsNaN(roadEdgeTime) && crossingManager.HasReachedRoadEdge)
        {
            roadEdgeTime = now;
            LogEvent(now, "Stepped off kerb");
        }

        // --- Crossing stages: time the halfway pause, log the stages, and end the run at the End trigger ---
        CrossingManager.CrossingState state = crossingManager.currentState;
        if (state != lastState)
        {
            if (state == CrossingManager.CrossingState.WaitingForTurn)
            {
                pauseStartTime = now;
                LogEvent(now, "Midpoint reached", -1, "pause started");
            }
            else if (state == CrossingManager.CrossingState.CrossingToEnd && lastState == CrossingManager.CrossingState.CrossingToMidpoint)
            {
                LogEvent(now, "Midpoint reached", -1, "midpoint flip off, no pause");
            }
            if (lastState == CrossingManager.CrossingState.WaitingForTurn)
            {
                pauseTotal += now - pauseStartTime;
                LogEvent(now, "Crossing resumed", -1, FormattableString.Invariant($"pause lasted {now - pauseStartTime:F2} s"));
            }
            lastState = state;

            if (state == CrossingManager.CrossingState.Completed)
            {
                LogEvent(now, "Reached end");
                FinishMeasuring("Completed");
                return;
            }
        }

        // Nothing moves during the artificial halfway pause, so there is nothing to measure.
        if (state == CrossingManager.CrossingState.WaitingForTurn)
            return;

        PedestrianTracker.EnsureSampled();
        if (PedestrianTracker.TryGet(out Vector3 pedestrian, out Vector3 pedestrianVelocity, out float bodyRadius))
            MeasureVehicles(now, pedestrian, pedestrianVelocity, bodyRadius);
    }

    void MeasureVehicles(float now, Vector3 pedestrian, Vector3 pedestrianVelocity, float bodyRadius)
    {
        foreach (CarMovement car in CarMovement.ActiveVehicles)
        {
            if (car == null)
                continue;
            int id = car.VehicleId;
            Vector3 carPosition = car.transform.position;
            Vector3 carForward = car.transform.forward;
            VehicleGeometry outline = car.Geometry; // car's rectangular footprint, measured from its colliders

            // --- Closest vehicle distance (definition: see DistanceToOutline) ---
            float distance = DistanceToOutline(pedestrian, carPosition, carForward, outline);
            if (float.IsNaN(result.closestVehicleDistance) || distance < result.closestVehicleDistance)
                result.closestVehicleDistance = distance;

            // --- Safety override ("would have collided") ---
            // The car's hard safety envelope forced it to stop because its normal (behavioural) braking could not have
            // stopped it in time: a real driver in that situation would most likely have hit the pedestrian. The simulator
            // prevents the collision, so this is logged as the virtual-collision measure. One event each time it engages.
            // The speed reported is the car's speed in the previous frame, i.e. how fast it was approaching before the forced stop.
            int overrides = car.SafetyOverrideCount;
            if (!safetyOverridesSeen.TryGetValue(id, out int seen))
                seen = overrides; // first time this car is seen in the run: only count new overrides
            if (!lastCarSpeed.TryGetValue(id, out float approachSpeed))
                approachSpeed = car.CurrentSpeed;
            if (overrides > seen)
                LogEvent(now, "Safety override", id,
                    FormattableString.Invariant($"would have collided: car approaching at {approachSpeed:F1} m/s, stopped {distance:F2} m from the pedestrian"));
            safetyOverridesSeen[id] = overrides;
            lastCarSpeed[id] = car.CurrentSpeed;

            // --- Collision ---
            // The pedestrian's body (a circle of the collider's radius) touches or overlaps the car's outline.
            // One "Collision" event per car per contact; contact ends once the body is clear of the car again.
            if (distance <= bodyRadius)
            {
                collided = true;
                if (carsInContact.Add(id))
                    LogEvent(now, "Collision", id, FormattableString.Invariant($"car speed {car.CurrentSpeed:F1} m/s"));
            }
            else if (distance > bodyRadius + ContactReleaseMargin)
            {
                carsInContact.Remove(id);
            }

            // --- Time-to-collision (TTC) and relative speed (definitions: see TryTimeToCollision) ---
            // The lowest TTC of the run (the most dangerous moment) is reported, together with the relative speed at that
            // same moment. If TTC is never calculated, both cells are left empty.
            if (TryTimeToCollision(pedestrian, pedestrianVelocity, bodyRadius, carPosition, carForward, outline,
                                   car.CurrentSpeed, out float ttc, out float relativeSpeed))
            {
                if (float.IsNaN(result.timeToCollision) || ttc < result.timeToCollision)
                {
                    result.timeToCollision = ttc;
                    result.relativeSpeed = relativeSpeed;
                }

                // --- Near miss ---
                // TTC below the threshold. Logged once per car per encounter: the encounter lasts until TTC can no longer
                // be calculated for that car (it has stopped, passed, or the pedestrian has left its path).
                if (ttc < nearMissTtc && carsInNearMiss.Add(id))
                    LogEvent(now, "Near miss", id, FormattableString.Invariant($"TTC {ttc:F2} s, relative speed {relativeSpeed:F1} m/s"));
            }
            else
            {
                carsInNearMiss.Remove(id);
            }
        }
    }

    /// <summary>
    /// Closest vehicle distance: from the centre of the pedestrian's body (the collider under the headset) to the nearest
    /// point on the car's outline (its rectangular footprint), measured flat along the ground (2D, height ignored).
    /// 0 = the pedestrian's centre is on or inside the car's outline.
    /// </summary>
    public static float DistanceToOutline(Vector3 pedestrian, Vector3 carPosition, Vector3 carForward, VehicleGeometry outline)
    {
        CarFrame(pedestrian, carPosition, carForward, out float along, out float side, out _);
        float outsideSide = Mathf.Max(Mathf.Abs(side) - outline.halfWidth, 0f);
        float outsideEnds = Mathf.Max(Mathf.Max(along - outline.frontExtent, -outline.rearExtent - along), 0f);
        return Mathf.Sqrt(outsideSide * outsideSide + outsideEnds * outsideEnds);
    }

    /// <summary>
    /// Time-to-collision (TTC) and relative speed. Only calculated (returns true) when ALL of these are true:
    ///   1. The pedestrian is in the car's path: their body overlaps the strip of road the car will drive over
    ///      (car half-width + body radius either side of the car's centre line, assuming the car keeps going straight).
    ///   2. The pedestrian is in front of the car (not beside or behind it).
    ///   3. The car and pedestrian are getting closer (relative speed above zero).
    ///
    /// Distance used for TTC: measured along the car's direction of travel, from the car's FRONT BUMPER to the
    /// nearest edge of the pedestrian's body (body centre minus body radius). This is the gap the car has to close
    /// before it would make contact.
    ///
    /// Relative (closing) speed: the car's speed minus the part of the pedestrian's walking speed that goes the same
    /// way as the car. Walking straight across the road adds ~0, so it is then ~the car's speed. Walking towards
    /// the car makes it bigger; walking away from it (in the car's direction) makes it smaller.
    ///
    /// TTC = gap / relative speed = seconds until the car would reach the pedestrian if both kept their current
    /// speed and direction.
    /// </summary>
    public static bool TryTimeToCollision(Vector3 pedestrian, Vector3 pedestrianVelocity, float bodyRadius,
        Vector3 carPosition, Vector3 carForward, VehicleGeometry outline, float carSpeed,
        out float ttc, out float relativeSpeed)
    {
        CarFrame(pedestrian, carPosition, carForward, out float along, out float side, out Vector3 forward);
        bool inPath = Mathf.Abs(side) < outline.halfWidth + bodyRadius;
        float gap = along - outline.frontExtent - bodyRadius;
        relativeSpeed = carSpeed - Vector3.Dot(pedestrianVelocity, forward);
        if (inPath && gap > 0f && relativeSpeed > MinClosingSpeed)
        {
            ttc = gap / relativeSpeed;
            return true;
        }
        ttc = float.NaN;
        relativeSpeed = float.NaN;
        return false;
    }

    /// <summary>
    /// The pedestrian's position in the car's own frame, on the ground (2D, height ignored):
    ///   along = how far ahead (+) or behind (-) of the car's centre the pedestrian is
    ///   side  = how far to the left/right of the car's centre line the pedestrian is
    /// </summary>
    static void CarFrame(Vector3 pedestrian, Vector3 carPosition, Vector3 carForward, out float along, out float side, out Vector3 forward)
    {
        forward = carForward;
        forward.y = 0f;
        forward.Normalize();
        Vector3 right = new Vector3(forward.z, 0f, -forward.x);
        Vector3 offset = pedestrian - carPosition;
        offset.y = 0f;
        along = Vector3.Dot(offset, forward);
        side = Vector3.Dot(offset, right);
    }

    // Event details use invariant formatting (always '.' as the decimal point), like the CSV numbers.
    void LogEvent(float time, string name, int vehicleId = -1, string details = "")
    {
        result.events.Add(new MeasurementEvent { time = time, name = name, vehicleId = vehicleId, details = details });
    }

    void FinishMeasuring(string outcome, string reason = "")
    {
        measuring = false;
        float endTime = clock() - startTime;
        if (outcome == "Incomplete")
            LogEvent(endTime, "Scenario stopped before end", -1, reason);

        // Waiting time = scenario start -> stepped off the kerb. Empty if they never stepped off.
        result.waitingTime = roadEdgeTime;

        // Crossing duration = stepped off the kerb -> reached the End trigger, minus the artificial halfway pause.
        // Only for runs that reached the End trigger.
        if (outcome == "Completed" && !float.IsNaN(roadEdgeTime))
            result.crossingDuration = endTime - roadEdgeTime - pauseTotal;

        result.crossingOutcome = collided ? "Collision" : outcome;
        LastResult = result;
        Save(result);
    }

    static void Save(MeasurementResult result)
    {
        try
        {
            Directory.CreateDirectory(ResultsFolder);
            KeepOldFileIfColumnsChanged(ResultsFile, MeasurementResult.CsvHeader);
            result.run = File.Exists(ResultsFile) ? File.ReadLines(ResultsFile).Count() : 1; // header + previous rows => next run
        }
        catch (IOException)
        {
            result.run = 0; // results file is locked (open in Excel?), so the run number can't be read
        }

        AppendRows(ResultsFile, MeasurementResult.CsvHeader, new[] { result.ToCsvRow() });
        AppendRows(EventsFile, MeasurementEvent.CsvHeader, result.events.Select(e => e.ToCsvRow(result)).ToArray());
        Debug.Log($"[MeasurementManager] Run {result.run} saved to {ResultsFile} ({result.events.Count} events in " +
                  $"{Path.GetFileName(EventsFile)})\n{MeasurementResult.CsvHeader}\n{result.ToCsvRow()}");
    }

    /// <summary>
    /// Adds rows to a CSV, writing the header first if the file is new. If the file can't be written (usually because it is
    /// open in Excel), the rows go to a separate timestamped file instead, so nothing is lost.
    /// </summary>
    static void AppendRows(string file, string header, string[] rows)
    {
        try
        {
            KeepOldFileIfColumnsChanged(file, header);
            bool isNewFile = !File.Exists(file);
            using (var writer = new StreamWriter(file, true))
            {
                if (isNewFile)
                    writer.WriteLine(header);
                foreach (string row in rows)
                    writer.WriteLine(row);
            }
        }
        catch (IOException e)
        {
            string backup = Path.Combine(ResultsFolder, $"{Path.GetFileNameWithoutExtension(file)}_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            File.WriteAllText(backup, header + Environment.NewLine + string.Join(Environment.NewLine, rows) + Environment.NewLine);
            Debug.LogError($"[MeasurementManager] Could not write {file} ({e.Message}). Is it open in Excel? " +
                           $"These rows were saved to {backup} instead.");
        }
    }

    /// <summary>
    /// If an existing CSV was written with different columns (e.g. before a column was added), rename it and start a
    /// fresh file, so rows with different columns are never mixed. Nothing is deleted.
    /// </summary>
    static void KeepOldFileIfColumnsChanged(string file, string header)
    {
        if (!File.Exists(file) || File.ReadLines(file).FirstOrDefault() == header)
            return;
        string kept = Path.Combine(Path.GetDirectoryName(file),
            $"{Path.GetFileNameWithoutExtension(file)}_old_columns_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
        File.Move(file, kept);
        Debug.LogWarning($"[MeasurementManager] The columns of {Path.GetFileName(file)} have changed, so the existing file " +
                         $"was kept as {kept} and a new one was started.");
    }

    /// <summary>
    /// Short code (6 characters) calculated from every value in the scenario as applied. Any change to any setting gives a
    /// different code; identical settings always give the same code. Used to check that runs had identical traffic settings.
    /// </summary>
    public static string ScenarioSettingsCode(ScenarioConfig scenario)
    {
        string json = scenario != null ? JsonUtility.ToJson(scenario) : "";
        using (var sha = SHA256.Create())
        {
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(json));
            return BitConverter.ToString(hash, 0, 3).Replace("-", "").ToLowerInvariant();
        }
    }
}
