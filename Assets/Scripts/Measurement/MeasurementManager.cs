using System;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>
/// Measures one scenario run and adds one row to MeasurementResults/measurement_results.csv when the run ends.
///
///   Scenario started            -> fresh measurements, clock starts at 0
///   every frame                 -> closest vehicle distance, TTC + relative speed, collision check
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

    // A car must be closing in faster than this (m/s) for TTC to be calculated; below it the car is effectively stopped.
    const float MinClosingSpeed = 0.1f;

    MeasurementResult result;
    bool measuring;
    float startTime;
    float roadEdgeTime = float.NaN;   // seconds after scenario start when the pedestrian stepped off the kerb
    float pauseStartTime;
    float pauseTotal;                 // total length of the artificial halfway pause (s)
    bool collided;
    CrossingManager.CrossingState lastState;

    public bool IsMeasuring => measuring;

    public static string ResultsFolder => Application.isEditor
        ? Path.Combine(Application.dataPath, "..", "MeasurementResults")     // next to the Unity project
        : Path.Combine(Application.persistentDataPath, "MeasurementResults"); // headset / built app
    public static string ResultsFile => Path.GetFullPath(Path.Combine(ResultsFolder, "measurement_results.csv"));

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
            FinishMeasuring("Incomplete");
    }

    void OnScenarioStopped()
    {
        if (measuring)
            FinishMeasuring("Incomplete");
    }

    void StartMeasuring(ScenarioConfig scenario)
    {
        result = new MeasurementResult
        {
            scenario = string.IsNullOrWhiteSpace(scenario.scenarioName) ? scenario.scenarioId : scenario.scenarioName,
            participantId = participantId,
            dateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            // Recorded at the start of the run (the researcher panel locks the setting while a run is being measured)
            midpointFlip = crossingManager == null ? "" : crossingManager.midpointFlipEnabled ? "On" : "Off",
        };
        startTime = Time.time;
        roadEdgeTime = float.NaN;
        pauseTotal = 0f;
        collided = false;
        lastState = crossingManager != null ? crossingManager.currentState : CrossingManager.CrossingState.NotStarted;
        measuring = true;
        Debug.Log($"[MeasurementManager] Measuring '{result.scenario}' for participant '{participantId}'.", this);
    }

    void Update()
    {
        if (!measuring || crossingManager == null)
            return;

        float now = Time.time - startTime;

        // --- Waiting time: the moment the pedestrian first steps off the kerb (RoadEdge trigger) ---
        if (float.IsNaN(roadEdgeTime) && crossingManager.HasReachedRoadEdge)
            roadEdgeTime = now;

        // --- Crossing stages: time the halfway pause, and end the run at the End trigger ---
        CrossingManager.CrossingState state = crossingManager.currentState;
        if (state != lastState)
        {
            if (state == CrossingManager.CrossingState.WaitingForTurn)
                pauseStartTime = now;
            if (lastState == CrossingManager.CrossingState.WaitingForTurn)
                pauseTotal += now - pauseStartTime;
            lastState = state;

            if (state == CrossingManager.CrossingState.Completed)
            {
                FinishMeasuring("Completed");
                return;
            }
        }

        // Nothing moves during the artificial halfway pause, so there is nothing to measure.
        if (state == CrossingManager.CrossingState.WaitingForTurn)
            return;

        PedestrianTracker.EnsureSampled();
        if (PedestrianTracker.TryGet(out Vector3 pedestrian, out Vector3 pedestrianVelocity, out float bodyRadius))
            MeasureVehicles(pedestrian, pedestrianVelocity, bodyRadius);
    }

    void MeasureVehicles(Vector3 pedestrian, Vector3 pedestrianVelocity, float bodyRadius)
    {
        foreach (CarMovement car in CarMovement.ActiveVehicles)
        {
            if (car == null)
                continue;

            // Work in the car's own frame, on the ground (2D, height ignored):
            //   along = how far ahead (+) or behind (-) of the car's centre the pedestrian is
            //   side  = how far to the left/right of the car's centre line the pedestrian is
            Vector3 forward = car.transform.forward;
            forward.y = 0f;
            forward.Normalize();
            Vector3 right = new Vector3(forward.z, 0f, -forward.x);
            Vector3 offset = pedestrian - car.transform.position;
            offset.y = 0f;
            float along = Vector3.Dot(offset, forward);
            float side = Vector3.Dot(offset, right);
            VehicleGeometry outline = car.Geometry; // car's rectangular footprint, measured from its colliders

            // --- Closest vehicle distance ---
            // From the centre of the pedestrian's body (the collider under the headset) to the nearest point on the
            // car's outline, measured flat along the ground. 0 = the pedestrian's centre is on the car's edge.
            float outsideSide = Mathf.Max(Mathf.Abs(side) - outline.halfWidth, 0f);
            float outsideEnds = Mathf.Max(Mathf.Max(along - outline.frontExtent, -outline.rearExtent - along), 0f);
            float distance = Mathf.Sqrt(outsideSide * outsideSide + outsideEnds * outsideEnds);
            if (float.IsNaN(result.closestVehicleDistance) || distance < result.closestVehicleDistance)
                result.closestVehicleDistance = distance;

            // --- Collision ---
            // The pedestrian's body (a circle of the collider's radius) touches or overlaps the car's outline.
            if (distance <= bodyRadius)
                collided = true;

            // --- Time-to-collision (TTC) and relative speed ---
            // Only calculated when ALL of these are true:
            //   1. The pedestrian is in the car's path: their body overlaps the strip of road the car will drive over
            //      (car half-width + body radius either side of the car's centre line, assuming the car keeps going straight).
            //   2. The pedestrian is in front of the car (not beside or behind it).
            //   3. The car and pedestrian are getting closer (relative speed above zero).
            //
            // Distance used for TTC: measured along the car's direction of travel, from the car's FRONT BUMPER to the
            // nearest edge of the pedestrian's body (body centre minus body radius). This is the gap the car has to close
            // before it would make contact.
            //
            // Relative (closing) speed: the car's speed minus the part of the pedestrian's walking speed that goes the same
            // way as the car. Walking straight across the road adds ~0, so it is then ~the car's speed. Walking towards
            // the car makes it bigger; walking away from it (in the car's direction) makes it smaller.
            //
            // TTC = gap / relative speed = seconds until the car would reach the pedestrian if both kept their current
            // speed and direction. The lowest TTC of the run (the most dangerous moment) is reported, together with the
            // relative speed at that same moment. If TTC is never calculated, both cells are left empty.
            bool inPath = Mathf.Abs(side) < outline.halfWidth + bodyRadius;
            float gap = along - outline.frontExtent - bodyRadius;
            float relativeSpeed = car.CurrentSpeed - Vector3.Dot(pedestrianVelocity, forward);
            if (inPath && gap > 0f && relativeSpeed > MinClosingSpeed)
            {
                float ttc = gap / relativeSpeed;
                if (float.IsNaN(result.timeToCollision) || ttc < result.timeToCollision)
                {
                    result.timeToCollision = ttc;
                    result.relativeSpeed = relativeSpeed;
                }
            }
        }
    }

    void FinishMeasuring(string outcome)
    {
        measuring = false;
        float endTime = Time.time - startTime;

        // Waiting time = scenario start -> stepped off the kerb. Empty if they never stepped off.
        result.waitingTime = roadEdgeTime;

        // Crossing duration = stepped off the kerb -> reached the End trigger, minus the artificial halfway pause.
        // Only for runs that reached the End trigger.
        if (outcome == "Completed" && !float.IsNaN(roadEdgeTime))
            result.crossingDuration = endTime - roadEdgeTime - pauseTotal;

        result.crossingOutcome = collided ? "Collision" : outcome;
        Save(result);
    }

    static void Save(MeasurementResult result)
    {
        string file = ResultsFile;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            KeepOldFileIfColumnsChanged(file);
            bool isNewFile = !File.Exists(file);
            result.run = isNewFile ? 1 : File.ReadLines(file).Count(); // header line + previous rows => next run number
            using (var writer = new StreamWriter(file, true))
            {
                if (isNewFile)
                    writer.WriteLine(MeasurementResult.CsvHeader);
                writer.WriteLine(result.ToCsvRow());
            }
            Debug.Log($"[MeasurementManager] Run {result.run} saved to {file}\n{MeasurementResult.CsvHeader}\n{result.ToCsvRow()}");
        }
        catch (IOException e)
        {
            // Usually because the CSV is open in Excel. Save the row to a separate file so it is not lost.
            string backup = Path.Combine(Path.GetDirectoryName(file), $"measurement_results_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            File.WriteAllText(backup, MeasurementResult.CsvHeader + Environment.NewLine + result.ToCsvRow() + Environment.NewLine);
            Debug.LogError($"[MeasurementManager] Could not write {file} ({e.Message}). Is it open in Excel? " +
                           $"This run was saved to {backup} instead.");
        }
    }

    /// <summary>
    /// If the existing CSV was written with different columns (e.g. before a column was added), rename it and start a
    /// fresh file, so rows with different columns are never mixed. Nothing is deleted.
    /// </summary>
    static void KeepOldFileIfColumnsChanged(string file)
    {
        if (!File.Exists(file) || File.ReadLines(file).FirstOrDefault() == MeasurementResult.CsvHeader)
            return;
        string kept = Path.Combine(Path.GetDirectoryName(file), $"measurement_results_old_columns_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
        File.Move(file, kept);
        Debug.LogWarning($"[MeasurementManager] The CSV columns have changed, so the existing results were kept as {kept} " +
                         $"and a new {Path.GetFileName(file)} was started.");
    }
}
