using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// Participant-free technical validation of the running scene (report Section 5.2, TV2-TV4). Only runs when asked for:
/// Unity started with "-pedValidation" (see ValidationMenu.RunBatch) or the menu Tools > Validation > Run. Normal sessions
/// are not affected.
///
/// 1. Traffic runs: each scenario file is run several times with nobody on the road, at a fixed simulation step
///    (Time.captureDeltaTime) so the simulation does not depend on how fast the PC renders. Every vehicle crossing the
///    pedestrian crossing line is written to traffic_passages.csv. Identical repeats show the traffic is repeatable from
///    its seed; the passages give flow, headways and speeds at the crossing.
/// 2. Scripted walks: the camera rig is moved along the crossing at a known speed after a known delay, so the collider and
///    triggers work exactly as when a participant walks in VR. The waiting time and crossing duration recorded by
///    MeasurementManager are compared with the times this script itself observed (walks.csv).
///
/// Results go to ValidationResults/ next to the project. MeasurementManager's own CSVs for these runs are redirected to
/// ValidationResults/measurement/, so the study's measurement_results.csv and measurement_events.csv are never touched.
/// </summary>
public class ValidationRunner : MonoBehaviour
{
    public const string CommandLineFlag = "-pedValidation";
    public const string RequestPrefKey = "PedValidationRequested";

    static readonly string[] ScenarioFiles = { "LowTraffic.json", "MediumTraffic.json", "HighTraffic.json" };
    const int RepeatsAtFixedStep = 3;     // identical repeats expected
    const float FixedStep = 1f / 60f;
    const float AlternateStep = 1f / 90f; // one extra run per scenario at a headset-like frame rate
    const float TrafficRunSeconds = 300f; // simulated seconds per traffic run (after the spawners' own prefill)

    // Walks: (scenario, delay before walking (s), walking speed (m/s))
    static readonly (string file, float delay, float speed)[] Walks =
    {
        ("LowTraffic.json", 3f, 1.0f), ("LowTraffic.json", 6f, 1.4f), ("LowTraffic.json", 10f, 1.8f),
        ("MediumTraffic.json", 3f, 1.4f), ("MediumTraffic.json", 6f, 1.0f), ("MediumTraffic.json", 10f, 1.8f),
        ("HighTraffic.json", 3f, 1.8f), ("HighTraffic.json", 6f, 1.4f), ("HighTraffic.json", 10f, 1.0f),
    };
    const float WalkTimeout = 120f;

    static string OutputFolder => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "ValidationResults"));

    ScenarioManager scenarioManager;
    CrossingManager crossingManager;
    PlayerMovement player;
    MeasurementManager measurement;
    Collider roadEdge, midpoint, end;
    Vector3 lineStart, lineEnd; // crossing line on the ground (RoadEdge -> End), used for vehicle passages
    readonly StringBuilder log = new StringBuilder();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void MaybeStart()
    {
        bool requested = Environment.GetCommandLineArgs().Contains(CommandLineFlag);
        if (PlayerPrefs.GetInt(RequestPrefKey, 0) == 1)
        {
            requested = true;
            PlayerPrefs.DeleteKey(RequestPrefKey);
            PlayerPrefs.Save();
        }
        if (requested)
            new GameObject("ValidationRunner").AddComponent<ValidationRunner>();
    }

    IEnumerator Start()
    {
        yield return null; // let every Start() run first
        Directory.CreateDirectory(OutputFolder);
        MeasurementManager.resultsFolderOverride = Path.Combine(OutputFolder, "measurement");
        Log($"Validation started {DateTime.Now:yyyy-MM-dd HH:mm:ss}, Unity {Application.unityVersion}, app version {Application.version}");

        bool ok = FindScene();
        if (ok)
        {
            yield return RunTraffic();
            yield return RunWalks();
        }

        Log(ok ? "Validation finished." : "Validation aborted: scene objects not found.");
        File.WriteAllText(Path.Combine(OutputFolder, "validation_log.txt"), log.ToString());
        Time.captureDeltaTime = 0f;
        Quit(ok ? 0 : 1);
    }

    bool FindScene()
    {
        scenarioManager = FindFirstObjectByType<ScenarioManager>();
        crossingManager = FindFirstObjectByType<CrossingManager>();
        player = FindFirstObjectByType<PlayerMovement>();
        measurement = FindFirstObjectByType<MeasurementManager>();
        foreach (CrossingTrigger t in FindObjectsByType<CrossingTrigger>(FindObjectsSortMode.None))
        {
            Collider c = t.GetComponent<Collider>();
            if (t.triggerType == CrossingTrigger.TriggerType.RoadEdge) roadEdge = c;
            else if (t.triggerType == CrossingTrigger.TriggerType.Midpoint) midpoint = c;
            else if (t.triggerType == CrossingTrigger.TriggerType.End) end = c;
        }
        Log($"ScenarioManager {scenarioManager != null}, CrossingManager {crossingManager != null}, PlayerMovement {player != null}, " +
            $"MeasurementManager {measurement != null}, RoadEdge {roadEdge != null}, Midpoint {midpoint != null}, End {end != null}");
        if (scenarioManager == null || crossingManager == null || player == null || roadEdge == null || midpoint == null || end == null)
            return false;

        lineStart = Flat(roadEdge.bounds.center);
        lineEnd = Flat(end.bounds.center);
        Log(F($"Crossing line {lineStart} -> {lineEnd} ({Vector3.Distance(lineStart, lineEnd):F2} m). ") +
              F($"RoadEdge bounds {roadEdge.bounds.center}/{roadEdge.bounds.size}, Midpoint {midpoint.bounds.center}/{midpoint.bounds.size}, ") +
              F($"End {end.bounds.center}/{end.bounds.size}. Player start {player.transform.position}"));
        return true;
    }

    // ------------------------------------------------------------------ traffic runs

    IEnumerator RunTraffic()
    {
        string file = Path.Combine(OutputFolder, "traffic_passages.csv");
        string summaryFile = Path.Combine(OutputFolder, "traffic_runs.csv");
        var rows = new List<string> { "Run,Scenario File,Scenario ID,Seed,Settings Code,Step (s),Frame,Time (s),Vehicle ID,Direction,Offset Along Crossing (m),Speed (m/s),Alternate Route" };
        var summary = new List<string> { "Run,Scenario File,Scenario ID,Seed,Settings Code,Step (s),Simulated Time (s),Passages,Passages +,Passages -,Vehicles Spawned,Max Active,Longest Stop (s),Safety Overrides,Passage Fingerprint" };
        int run = 0;

        // keep the player away from the road for traffic-only runs
        player.SetMovementEnabled(false);

        foreach (string scenarioFile in ScenarioFiles)
        {
            for (int repeat = 0; repeat <= RepeatsAtFixedStep; repeat++)
            {
                float step = repeat < RepeatsAtFixedStep ? FixedStep : AlternateStep;
                run++;
                Time.captureDeltaTime = step;
                yield return null;
                if (!scenarioManager.StartScenario(scenarioFile))
                {
                    Log($"Run {run}: could not start {scenarioFile}");
                    continue;
                }
                player.SetMovementEnabled(false);
                ScenarioConfig config = scenarioManager.currentScenario;
                string code = MeasurementManager.ScenarioSettingsCode(config);
                float start = Time.time;
                int frame = 0;
                var lastSide = new Dictionary<int, float>();
                var stoppedSince = new Dictionary<int, float>();
                var overrides = new Dictionary<int, int>();
                int maxId = -1, minId = int.MaxValue, maxActive = 0, passages = 0, plus = 0, minus = 0;
                float longestStop = 0f;
                var fingerprint = new StringBuilder();

                while (Time.time - start < TrafficRunSeconds)
                {
                    yield return null;
                    frame++;
                    float t = Time.time - start;
                    maxActive = Mathf.Max(maxActive, CarMovement.ActiveVehicles.Count);
                    foreach (CarMovement car in CarMovement.ActiveVehicles)
                    {
                        if (car == null) continue;
                        int id = car.VehicleId;
                        maxId = Mathf.Max(maxId, id);
                        minId = Mathf.Min(minId, id);
                        overrides[id] = car.SafetyOverrideCount;

                        if (car.CurrentSpeed < 0.1f)
                        {
                            if (!stoppedSince.ContainsKey(id)) stoppedSince[id] = t;
                            longestStop = Mathf.Max(longestStop, t - stoppedSince[id]);
                        }
                        else stoppedSince.Remove(id);

                        Vector3 p = Flat(car.transform.position);
                        float side = Side(p);
                        float along = Along(p);
                        bool nearLine = along > -5f && along < Vector3.Distance(lineStart, lineEnd) + 5f;
                        if (lastSide.TryGetValue(id, out float previous) && nearLine && Mathf.Sign(previous) != Mathf.Sign(side) && previous != 0f)
                        {
                            int direction = side > 0f ? 1 : -1;
                            passages++;
                            if (direction > 0) plus++; else minus++;
                            rows.Add(string.Join(",", run, scenarioFile, config.scenarioId, config.randomSeed, code, F($"{step:F4}"), frame,
                                F($"{t:F3}"), id, direction > 0 ? "+" : "-", F($"{along:F2}"), F($"{car.CurrentSpeed:F2}"), car.IsAlternateRoute ? 1 : 0));
                            fingerprint.Append(F($"{id}:{direction}:{t:F2};"));
                        }
                        lastSide[id] = side;
                    }
                }

                int spawned = maxId >= minId ? maxId - minId + 1 : 0;
                string hash = ShortHash(fingerprint.ToString());
                summary.Add(string.Join(",", run, scenarioFile, config.scenarioId, config.randomSeed, code, F($"{step:F4}"),
                    F($"{Time.time - start:F1}"), passages, plus, minus, spawned, maxActive, F($"{longestStop:F1}"), overrides.Values.Sum(), hash));
                Log(F($"Run {run} {config.scenarioId} step {step:F4}: {passages} passages (+{plus}/-{minus}) in {TrafficRunSeconds:F0} s, ") +
                      F($"{spawned} vehicles, max active {maxActive}, longest stop {longestStop:F1} s, fingerprint {hash}"));
                scenarioManager.StopScenario();
                File.WriteAllLines(file, rows);
                File.WriteAllLines(summaryFile, summary);
            }
        }
        Time.captureDeltaTime = 0f;
        player.SetMovementEnabled(true);
    }

    // ------------------------------------------------------------------ scripted walks

    IEnumerator RunWalks()
    {
        string file = Path.Combine(OutputFolder, "walks.csv");
        var rows = new List<string> { "Walk,Scenario File,Scenario ID,Seed,Delay (s),Speed (m/s),Observed Kerb Time (s),Recorded Waiting Time (s),Waiting Difference (s)," +
                                      "Observed Pause (s),Observed Crossing Duration (s),Recorded Crossing Duration (s),Duration Difference (s),Walked Distance Excl. Pause (m)," +
                                      "Expected Duration From Distance (s),Recorded Outcome,Recorded Closest Distance (m),Recorded TTC (s),Midpoint Flip,Note" };
        int index = 0;
        foreach (var w in Walks)
        {
            index++;
            Time.captureDeltaTime = 0f; // real time: the midpoint pause is timed in real seconds
            crossingManager.midpointFlipEnabled = true; // as in the study (field only, the saved researcher choice is not changed)
            if (!scenarioManager.StartScenario(w.file))
            {
                Log($"Walk {index}: could not start {w.file}");
                continue;
            }
            ScenarioConfig config = scenarioManager.currentScenario;
            float start = Time.time;
            yield return null;

            Transform mover = player.cameraRig != null && player.cameraRig != player.transform && !player.cameraRig.IsChildOf(player.transform)
                ? player.cameraRig : null;

            // wait the scripted delay
            while (Time.time - start < w.delay)
                yield return null;

            float kerbTime = float.NaN, endTouch = float.NaN, endTime = float.NaN, pauseStart = float.NaN, pause = 0f, distance = 0f;
            string note = "";
            Vector3[] targets = { Flat(roadEdge.bounds.center), Flat(midpoint.bounds.center), Flat(end.bounds.center) };
            int target = 0;
            while (Time.time - start < w.delay + WalkTimeout)
            {
                float t = Time.time - start;
                // Observed independently of the triggers: first frame the collider's bounds touch the trigger's bounds
                Bounds bodyBounds = player.controller.bounds;
                if (float.IsNaN(kerbTime) && bodyBounds.Intersects(roadEdge.bounds)) kerbTime = t;
                if (float.IsNaN(endTouch) && bodyBounds.Intersects(end.bounds)) endTouch = t;
                var state = crossingManager.currentState;
                if (state == CrossingManager.CrossingState.WaitingForTurn)
                {
                    if (float.IsNaN(pauseStart)) pauseStart = t;
                    yield return null;
                    continue;
                }
                if (!float.IsNaN(pauseStart) && pause == 0f) pause = t - pauseStart;
                if (state == CrossingManager.CrossingState.Completed) { endTime = t; break; }

                Vector3 body = Flat(player.transform.position);
                Vector3 goal = targets[Mathf.Min(target, targets.Length - 1)];
                Vector3 toGoal = goal - body;
                if (toGoal.magnitude < 0.3f && target < targets.Length - 1) { target++; continue; }
                Vector3 dir = toGoal.sqrMagnitude > 1e-6f ? toGoal.normalized : (targets[2] - targets[0]).normalized;
                Vector3 delta = dir * w.speed * Time.deltaTime;
                distance += delta.magnitude;
                if (mover != null) mover.position += delta;  // head moves, PlayerMovement keeps the collider under it
                else player.controller.Move(delta);
                yield return null;
            }
            if (float.IsNaN(endTime)) note = "did not reach end within timeout";

            yield return null; // MeasurementManager finishes the run on the frame the state becomes Completed
            MeasurementResult r = measurement != null ? measurement.LastResult : null;
            bool fresh = r != null && r.scenarioId == config.scenarioId && !float.IsNaN(endTime);
            float recordedWait = fresh ? r.waitingTime : float.NaN;
            float recordedDuration = fresh ? r.crossingDuration : float.NaN;
            float observedDuration = endTouch - kerbTime - pause;
            rows.Add(string.Join(",", index, w.file, config.scenarioId, config.randomSeed, F($"{w.delay:F1}"), F($"{w.speed:F2}"),
                N(kerbTime), N(recordedWait), N(recordedWait - kerbTime), N(pause), N(observedDuration), N(recordedDuration),
                N(recordedDuration - observedDuration), N(distance), N(distance / w.speed), fresh ? r.crossingOutcome : "",
                fresh ? N(r.closestVehicleDistance) : "", fresh ? N(r.timeToCollision) : "", fresh ? r.midpointFlip : "", note));
            Log(F($"Walk {index} {config.scenarioId} delay {w.delay} s speed {w.speed} m/s: kerb {kerbTime:F2}/{recordedWait:F2} s, ") +
                  F($"duration {observedDuration:F2}/{recordedDuration:F2} s, pause {pause:F2} s, outcome {(fresh ? r.crossingOutcome : "-")} {note}"));
            File.WriteAllLines(file, rows);

            scenarioManager.StopScenario();
            yield return null;
            player.ReturnToStart();
            yield return null;
        }
    }

    // ------------------------------------------------------------------ helpers

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    float Side(Vector3 p) { Vector3 d = (lineEnd - lineStart).normalized; Vector3 r = p - lineStart; return d.x * r.z - d.z * r.x; }
    float Along(Vector3 p) => Vector3.Dot(p - lineStart, (lineEnd - lineStart).normalized);
    static string F(FormattableString s) => FormattableString.Invariant(s);
    static string N(float v) => float.IsNaN(v) || float.IsInfinity(v) ? "" : v.ToString("F3", CultureInfo.InvariantCulture);

    static string ShortHash(string s)
    {
        using (var sha = System.Security.Cryptography.SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(s))).Replace("-", "").Substring(0, 12);
    }

    void Log(string line)
    {
        log.AppendLine(line);
        Debug.Log("[Validation] " + line);
    }

    static void Quit(int code)
    {
#if UNITY_EDITOR
        if (Application.isBatchMode)
            UnityEditor.EditorApplication.Exit(code);
        else
            UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit(code);
#endif
    }
}
