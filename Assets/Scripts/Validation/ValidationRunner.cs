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
            if (PhaseRequested("traffic")) yield return RunTraffic();
            if (PhaseRequested("walks")) yield return RunWalks();
            if (PhaseRequested("conflict")) yield return RunConflictWalks();
            if (PhaseRequested("screenshots")) yield return RunScreenshots();
            if (PhaseRequested("overhead")) yield return RunOverhead();
            if (PhaseRequested("panel")) yield return RunPanelCapture();
        }

        Log(ok ? "Validation finished." : "Validation aborted: scene objects not found.");
        File.WriteAllText(Path.Combine(OutputFolder, "validation_log.txt"), log.ToString());
        Time.captureDeltaTime = 0f;
        Quit(ok ? 0 : 1);
    }

    // "-pedValidationPhases=conflict,screenshots" runs only those phases; without it every phase runs.
    static bool PhaseRequested(string phase)
    {
        string arg = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith(CommandLineFlag + "Phases="));
        return arg == null || arg.Substring(arg.IndexOf('=') + 1).Split(',').Contains(phase);
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
                var passageKeys = new List<(int id, int direction, float t)>();

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
                            passageKeys.Add((id, direction, t));
                        }
                        lastSide[id] = side;
                    }
                }

                int spawned = maxId >= minId ? maxId - minId + 1 : 0;
                // Vehicle IDs keep counting across runs in one session, so they are rebased to the run's first vehicle
                string hash = ShortHash(string.Concat(passageKeys.Select(k => F($"{k.id - minId}:{k.direction}:{k.t:F2};"))));
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

    // ------------------------------------------------------------------ conflict walks

    // Walks that step off the kerb at fixed times regardless of traffic, so vehicles meet a pedestrian in their path.
    // Tests the conflict-based yielding and the outcome events (near miss, collision, safety override) in the scene.
    static readonly string[] ConflictScenarios = { "LowTraffic.json", "MediumTraffic.json", "HighTraffic.json" };
    const int ConflictDelays = 20;        // delays of 0, 1, ... 19 s before walking
    const float ConflictSpeed = 1.4f;

    IEnumerator RunConflictWalks()
    {
        string file = Path.Combine(OutputFolder, "conflict_walks.csv");
        var rows = new List<string> { "Walk,Scenario File,Scenario ID,Seed,Delay (s),Speed (m/s),Recorded Outcome,Recorded Crossing Duration (s)," +
                                      "Recorded Closest Distance (m),Recorded TTC (s),Near Miss Events,Collision Events,Safety Override Events," +
                                      "Cars Yielding,Hardest Braking (m/s2),Longest Yield Stop (s),Note" };
        int index = 0;
        foreach (string scenarioFile in ConflictScenarios)
        {
            for (int delay = 0; delay < ConflictDelays; delay++)
            {
                index++;
                Time.captureDeltaTime = 0f;
                crossingManager.midpointFlipEnabled = true;
                if (!scenarioManager.StartScenario(scenarioFile))
                {
                    Log($"Conflict walk {index}: could not start {scenarioFile}");
                    continue;
                }
                ScenarioConfig config = scenarioManager.currentScenario;
                float start = Time.time;
                yield return null;
                Transform mover = player.cameraRig != null && player.cameraRig != player.transform && !player.cameraRig.IsChildOf(player.transform)
                    ? player.cameraRig : null;
                while (Time.time - start < delay)
                    yield return null;

                var yielding = new HashSet<int>();
                var stoppedSince = new Dictionary<int, float>();
                float hardestBraking = 0f, longestYieldStop = 0f;
                Vector3[] targets = { Flat(roadEdge.bounds.center), Flat(midpoint.bounds.center), Flat(end.bounds.center) };
                int target = 0;
                bool completed = false;
                while (Time.time - start < delay + WalkTimeout)
                {
                    float t = Time.time - start;
                    foreach (CarMovement car in CarMovement.ActiveVehicles)
                    {
                        if (car == null || !car.PedestrianConstraintActive) { if (car != null) stoppedSince.Remove(car.VehicleId); continue; }
                        yielding.Add(car.VehicleId);
                        hardestBraking = Mathf.Min(hardestBraking, car.AppliedAcceleration);
                        if (car.CurrentSpeed < 0.1f)
                        {
                            if (!stoppedSince.ContainsKey(car.VehicleId)) stoppedSince[car.VehicleId] = t;
                            longestYieldStop = Mathf.Max(longestYieldStop, t - stoppedSince[car.VehicleId]);
                        }
                        else stoppedSince.Remove(car.VehicleId);
                    }
                    var state = crossingManager.currentState;
                    if (state == CrossingManager.CrossingState.Completed) { completed = true; break; }
                    if (state == CrossingManager.CrossingState.WaitingForTurn) { yield return null; continue; }

                    Vector3 body = Flat(player.transform.position);
                    Vector3 goal = targets[Mathf.Min(target, targets.Length - 1)];
                    Vector3 toGoal = goal - body;
                    if (toGoal.magnitude < 0.3f && target < targets.Length - 1) { target++; continue; }
                    Vector3 dir = toGoal.sqrMagnitude > 1e-6f ? toGoal.normalized : (targets[2] - targets[0]).normalized;
                    Vector3 delta = dir * ConflictSpeed * Time.deltaTime;
                    if (mover != null) mover.position += delta;
                    else player.controller.Move(delta);
                    yield return null;
                }
                yield return null;
                MeasurementResult r = measurement != null ? measurement.LastResult : null;
                bool fresh = completed && r != null && r.scenarioId == config.scenarioId;
                int nearMiss = fresh ? r.events.Count(e => e.name == "Near miss") : 0;
                int collisions = fresh ? r.events.Count(e => e.name == "Collision") : 0;
                int overrides = fresh ? r.events.Count(e => e.name == "Safety override") : 0;
                rows.Add(string.Join(",", index, scenarioFile, config.scenarioId, config.randomSeed, delay, F($"{ConflictSpeed:F2}"),
                    fresh ? r.crossingOutcome : "", fresh ? N(r.crossingDuration) : "", fresh ? N(r.closestVehicleDistance) : "",
                    fresh ? N(r.timeToCollision) : "", nearMiss, collisions, overrides, yielding.Count, N(hardestBraking), N(longestYieldStop),
                    completed ? "" : "did not reach end within timeout"));
                Log(F($"Conflict walk {index} {config.scenarioId} delay {delay} s: ") + F($"outcome {(fresh ? r.crossingOutcome : "-")}, ") +
                    F($"near misses {nearMiss}, collisions {collisions}, overrides {overrides}, yielding cars {yielding.Count}, ") +
                    F($"hardest braking {hardestBraking:F2} m/s2"));
                File.WriteAllLines(file, rows);

                scenarioManager.StopScenario();
                yield return null;
                player.ReturnToStart();
                yield return null;
            }
        }
    }

    // ------------------------------------------------------------------ screenshots

    // Stills for the report (site photo beside VR view): from head height at the kerb, along the road both ways and across it,
    // by day and by night, with Medium traffic running. Rendered with a temporary camera, so the VR rig is not changed.
    IEnumerator RunScreenshots()
    {
        string folder = Path.Combine(OutputFolder, "screenshots");
        Directory.CreateDirectory(folder);
        Camera source = player.vrCamera != null ? player.vrCamera.GetComponent<Camera>() : Camera.main;
        Vector3 kerb = Flat(roadEdge.bounds.center) + (Flat(roadEdge.bounds.center) - Flat(end.bounds.center)).normalized * 1.0f + Vector3.up * 1.6f;
        Vector3 across = (Flat(end.bounds.center) - Flat(roadEdge.bounds.center)).normalized;
        Vector3 along = Vector3.Cross(Vector3.up, across);
        var views = new (string name, Vector3 position, Vector3 forward)[]
        {
            ("start", source != null ? source.transform.position : kerb, source != null ? Vector3.ProjectOnPlane(source.transform.forward, Vector3.up) : across),
            ("kerb_across", kerb, across),
            ("kerb_along_a", kerb, (along + across * 0.15f).normalized),
            ("kerb_along_b", kerb, (-along + across * 0.15f).normalized),
        };
        foreach (bool night in new[] { false, true })
        {
            Time.captureDeltaTime = 0f;
            scenarioManager.nightMode = night;
            if (!scenarioManager.StartScenario("MediumTraffic.json"))
                continue;
            player.SetMovementEnabled(false);
            float start = Time.time;
            while (Time.time - start < 20f)
                yield return null;
            // No WaitForEndOfFrame: it never resumes in a batch-mode Editor. cam.Render() below renders on demand anyway.

            var go = new GameObject("ValidationScreenshotCamera");
            Camera cam = go.AddComponent<Camera>();
            if (source != null)
            {
                cam.CopyFrom(source);
                cam.stereoTargetEye = StereoTargetEyeMask.None;
            }
            cam.fieldOfView = 70f;
            var rt = new RenderTexture(1920, 1080, 24);
            cam.targetTexture = rt;
            foreach (var v in views)
            {
                cam.transform.SetPositionAndRotation(v.position, Quaternion.LookRotation(v.forward, Vector3.up));
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                string path = Path.Combine(folder, $"{(night ? "night" : "day")}_{v.name}.png");
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Destroy(tex);
                Log(F($"Screenshot {path}: camera at {v.position}, facing {v.forward}"));
            }
            RenderTexture.active = null;
            cam.targetTexture = null;
            Destroy(rt);
            Destroy(go);
            scenarioManager.StopScenario();
            yield return null;
        }
        scenarioManager.nightMode = false;
        player.SetMovementEnabled(true);
    }

    // ------------------------------------------------------------------ overhead views

    // Plan view (orthographic, straight down) and a high oblique view of the modelled street section by day with Medium
    // traffic running, framed on the crossing with about 45 m of road either side, for comparison with the aerial photo.
    IEnumerator RunOverhead()
    {
        string folder = Path.Combine(OutputFolder, "screenshots");
        Directory.CreateDirectory(folder);
        Camera source = player.vrCamera != null ? player.vrCamera.GetComponent<Camera>() : Camera.main;
        Vector3 across = (Flat(end.bounds.center) - Flat(roadEdge.bounds.center)).normalized;
        Vector3 along = Vector3.Cross(Vector3.up, across);
        Vector3 centre = (Flat(roadEdge.bounds.center) + Flat(end.bounds.center)) * 0.5f;

        Time.captureDeltaTime = 0f;
        scenarioManager.nightMode = false;
        if (!scenarioManager.StartScenario("MediumTraffic.json"))
            yield break;
        player.SetMovementEnabled(false);
        float start = Time.time;
        while (Time.time - start < 20f)
            yield return null;

        var go = new GameObject("ValidationOverheadCamera");
        Camera cam = go.AddComponent<Camera>();
        if (source != null)
        {
            cam.CopyFrom(source);
            cam.stereoTargetEye = StereoTargetEyeMask.None;
        }
        cam.nearClipPlane = 0.3f;
        cam.farClipPlane = 1000f;
        var rt = new RenderTexture(1920, 1080, 24);
        cam.targetTexture = rt;

        // Plan: 1920x1080 covering about 96 m along the road and 54 m across it, road running left-right
        cam.orthographic = true;
        cam.orthographicSize = 27f;
        cam.transform.SetPositionAndRotation(centre + Vector3.up * 200f, Quaternion.LookRotation(Vector3.down, across));
        Capture(cam, rt, Path.Combine(folder, "day_overhead_plan.png"));

        // Oblique: from above the road itself, 55 m along it, looking down the corridor onto the crossing (from the side,
        // the buildings next to the footpath block the view)
        cam.orthographic = false;
        cam.fieldOfView = 60f;
        Vector3 eye = centre + along * 55f + Vector3.up * 30f;
        cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(centre - eye, Vector3.up));
        Capture(cam, rt, Path.Combine(folder, "day_overhead_oblique.png"));

        cam.targetTexture = null;
        Destroy(rt);
        Destroy(go);
        scenarioManager.StopScenario();
        player.SetMovementEnabled(true);
        yield return null;
    }

    void Capture(Camera cam, RenderTexture rt, string path)
    {
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Destroy(tex);
        RenderTexture.active = null;
        Log(F($"Screenshot {path}: camera at {cam.transform.position}, facing {cam.transform.forward}, ") +
            (cam.orthographic ? F($"orthographic size {cam.orthographicSize}") : F($"fov {cam.fieldOfView}")));
    }

    // ------------------------------------------------------------------ researcher panel

    // The researcher scenario panel is IMGUI, drawn only into the Game view, so this needs a windowed Editor (not -batchmode).
    // Starts Medium traffic, opens the panel and captures the Game view as it appears on the researcher's desktop screen.
    IEnumerator RunPanelCapture()
    {
        if (Application.isBatchMode)
        {
            Log("Panel capture skipped: the IMGUI panel is only drawn in a windowed Editor (run without -batchmode).");
            yield break;
        }
        string folder = Path.Combine(OutputFolder, "screenshots");
        Directory.CreateDirectory(folder);
        Time.captureDeltaTime = 0f;
        scenarioManager.nightMode = false;
        if (!scenarioManager.StartScenario("MediumTraffic.json"))
            yield break;
        player.SetMovementEnabled(false);
        float start = Time.time;
        while (Time.time - start < 10f)
            yield return null;

        var panel = FindFirstObjectByType<ScenarioSelectionPanel>();
        if (panel == null)
        {
            Log("Panel capture skipped: ScenarioSelectionPanel not found.");
            yield break;
        }
        typeof(ScenarioSelectionPanel).GetMethod("SetVisible", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?.Invoke(panel, new object[] { true });
        for (int i = 0; i < 30; i++)
            yield return null;

        string path = Path.Combine(folder, "day_researcher_panel.png");
        ScreenCapture.CaptureScreenshot(path);
        for (int i = 0; i < 30 && !File.Exists(path); i++)
            yield return null;
        Log(File.Exists(path) ? F($"Screenshot {path}: Game view {Screen.width}x{Screen.height} with the researcher panel open")
                              : "Panel capture failed: no file written.");
        scenarioManager.StopScenario();
        player.SetMovementEnabled(true);
        yield return null;
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
        File.WriteAllText(Path.Combine(OutputFolder, "validation_log.txt"), log.ToString()); // kept up to date in case a run is cut short
    }

    static void Quit(int code)
    {
#if UNITY_EDITOR
        // "-pedValidationExit" also closes a windowed Editor (used for the researcher panel capture, which needs a Game view)
        if (Application.isBatchMode || Environment.GetCommandLineArgs().Contains(CommandLineFlag + "Exit"))
            UnityEditor.EditorApplication.Exit(code);
        else
            UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit(code);
#endif
    }
}
