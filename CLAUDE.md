# CLAUDE.md: VR digital twin for pedestrian safety research

Developer guide for anyone (person or coding assistant) working on this Unity project. It describes the project as it stands after the Part IV project ended (October 2026). The code, scenes and scenario files are the source of truth; if this file disagrees with them, trust the repository and fix this file.

## 1. What this project is

A Unity VR research platform: a digital twin of an **uncontrolled mid-block crossing on Symonds Street, Auckland** (between Mount Street and St Paul Street, opposite St Paul's Church). A participant in a headset decides when and where to cross real-scale traffic, and the app logs crossing measures to CSV.

University of Auckland Part IV project #98 (2026), Hunter Roberts and Miles Makino-Torrington; supervisor A/Prof Burkhard Wünsche, co-supervisor Dr Subeh Chowdhury.

It is a research instrument first and a visual demo second. Favour controllable, repeatable conditions and interpretable data over visual polish or game mechanics.

## 2. Current state (October 2026)

- **Version used for informal testing:** commit `497dd02` on `main` (app version 0.1.0), run on 9 October 2026 with four classmates (P01-P04, 20 analysed crossings) to get design feedback. This was not an ethics-approved study; its behavioural data is exploratory only.
- **Validation runner:** merged into `main` on 20 October 2026 (PR #22, `Assets/Scripts/Validation/`). It is editor/batch-only and does not change Play mode.
- **Verification results:** 65/65 project EditMode tests pass (TV1). TV2-TV4 (traffic flow, seed repeatability, scripted and conflict walks) were run with the validation runner.
- **Next steps:** the prioritised list of changes before a full behavioural study is in the research compendium (`09_Future_Work/Future_development.docx`). The main known gaps: the midpoint turn-around interrupts the crossing; vehicle speeds (45-55 km/h) are above the posted 30 km/h; cars only (no buses or horns); scenario order needs counterbalancing; no trajectory or head-orientation time series is logged.

## 3. The crossing model (do not change without the team)

The crossing is **informal**: there is no zebra, signal or pedestrian priority. The participant chooses when and where to cross.

- Vehicles do **not** stop just because a pedestrian is near the road.
- Drivers follow the Intelligent Driver Model (IDM) and yield only to a **predicted conflict** between the pedestrian's path and their own (`Vehicles/PedestrianConflictModel.cs`). With enough time and space they pass ahead; otherwise they slow progressively or brake.
- The real crossing is about 18 m (17.96 m in the scene, kerb trigger to end trigger). The lab has about 12 m of floor, so the participant walks to the centre line, turns 180 degrees when prompted (about a 5 s pause), and walks the second half. The scene keeps true scale; it is never shrunk to fit the room.

## 4. Environment and tools

- Unity **6000.3.5f2**, URP 17.3.0, OpenXR 1.16.1 (Meta Quest Touch Plus profile), XR Interaction Toolkit 3.3.2, Input System 1.17.0, Test Framework 1.6.0. Exact versions are pinned in `Packages/packages-lock.json`; do not upgrade packages casually.
- Headset: Meta Quest 2 over Air Link or a Link cable, with the Meta Horizon Link app as the active OpenXR runtime. The app runs in the Editor's Play mode; no standalone Quest build was used.
- Scene: `Assets/Scenes/SampleScene.unity`. Use its existing XR rig.
- Binary assets (`*.fbx`, `*.png`, `*.wav`, `*.psd`, `*.mp4`) are stored with **Git LFS**. Clone with Git LFS installed, or the models and textures will be missing.

## 5. Code map (`Assets/Scripts/`)

| Area | Files |
|---|---|
| Scenario lifecycle and researcher panel | `Scenario/ScenarioManager.cs`, `Scenario/ScenarioConfig.cs`, `Scenario/ScenarioSelectionPanel.cs` |
| Traffic | `TrafficSpawner.cs`, `TrafficController.cs`, `VehicleController.cs`, `CarMovement.cs`, `StopLine.cs`, `TrafficAudio.cs` |
| Driver models | `Vehicles/IntelligentDriverModel.cs`, `Vehicles/PedestrianConflictModel.cs`, `Vehicles/PedestrianTracker.cs`, `Vehicles/SignalResponseModel.cs`, `Vehicles/TrafficArrivalModel.cs`, `Vehicles/VehiclePath.cs` |
| Traffic signals (upstream intersection) | `Signals/SignalController.cs`, `Signals/SignalCycle.cs`, `Signals/SignalTiming.cs` |
| Participant and crossing | `PlayerController.cs`, `CrossingManager.cs`, `CrossingTrigger.cs`, `MidpointTurnPrompt.cs`, `MidpointApproachMarker.cs` |
| Measurement and logging | `Measurement/MeasurementManager.cs` (all file writing), `Measurement/MeasurementResult.cs` (CSV schema) |
| Lighting | `LightingController.cs` (Day/Night) |
| Validation (TV2-TV4) | `Validation/ValidationRunner.cs`, `Validation/Editor/ValidationMenu.cs` |
| Tests (TV1) | `Measurement/Editor/`, `Vehicles/Editor/`, `Signals/Editor/`, `Scenario/Editor/` |

## 6. Scenarios

Scenarios are data, not code: JSON files in `Assets/StreamingAssets/Scenarios/`, loaded by `ScenarioConfig.cs`. Fields not in a file keep the defaults in `ScenarioConfig.cs`.

| ID | File | Density | Seed | Mean headway per lane |
|---|---|---|---|---|
| S1 | `LowTraffic.json` | Low (250 veh/h/lane) | 1001 | 14.5 s |
| S2 | `MediumTraffic.json` | Medium (400 veh/h/lane) | 1002 | 9.01 s |
| S3 | `HighTraffic.json` | High (550 veh/h/lane) | 1003 | 6.55 s |

- All three share the IDM parameters (desired speed 13.9 m/s, T 1.5 s, s0 2.0 m, a 1.5 m/s², b 2.0 m/s²) and spawn speeds of 12.5-15.3 m/s. There is no "fast" scenario.
- Day or Night is chosen in the researcher panel, not in the JSON, and logged in the `Lighting` column.
- Each run logs its seed and a hash of its settings (`Scenario Settings`), so a dataset can be matched to its configuration.
- To add a scenario, add a JSON file. Avoid unseeded randomness.
- Traffic repeats exactly from its seed only at a steady frame rate (see TV3).

## 7. Running a trial

1. Open `SampleScene`, start Link on the headset, press Play.
2. On the PC monitor press **Tab** for the researcher panel: enter the participant code, choose Day/Night, press **1-3** for S1-S3. **R** restarts the run, **X** stops traffic.
3. A run ends when the participant reaches the End trigger (Completed or Collision) or the researcher stops it (Incomplete).

Keep the participant view free of scores, rewards or extra HUD.

## 8. Measures and data files

The app appends to two CSV files in `MeasurementResults/` next to `Assets/` (in a build: `Application.persistentDataPath/MeasurementResults/`):

- `measurement_results.csv`: one row per run. Scenario, run, participant code, time, app version, seed, settings hash, lighting, waiting time, crossing duration, closest vehicle distance, relative speed, minimum time-to-collision (TTC) and outcome.
- `measurement_events.csv`: the run's timestamped events (scenario started, kerb exit, midpoint reached, near miss, collision, crossing completed and so on).

Key definitions (full reference with code line numbers: compendium `05_Data/CSV_Column_Reference.md`):

- Times are Unity game time (`Time.time`) from the run's "Scenario started".
- An empty cell means "not applicable or never happened", never zero.
- **Near miss:** TTC below 1.5 s while the participant is in a vehicle's path.
- If a CSV is open in Excel, rows go to a timestamped backup file and Run is written as 0. If the columns change, the old file is renamed and a new file starts.

Rules:

- Keep the CSV schema stable. Any change to a column or a measure definition changes the research data and must be agreed with the team and recorded.
- Do not add a measure that cannot be detected reliably.
- Keep file writing inside `MeasurementManager`.

## 9. Tests and verification

- **TV1 (unit tests):** Test Runner > EditMode > Run All, or `Unity.exe -batchmode -projectPath <project> -runTests -testPlatform EditMode -testResults results.xml`. Expect 65/65 project tests to pass.
- **TV2-TV4:** `Unity.exe -batchmode -projectPath <project> -executeMethod ValidationMenu.RunBatch -pedValidation`. Output goes to `ValidationResults/`.
- Batch mode needs the Editor closed. Do not run the validation in a folder holding study data, because the runner writes its own measurement CSVs.
- Add or update tests with any change to the driver models, the measures or the scenario loader.

## 10. Working conventions

- Make focused, incremental changes; don't rebuild systems that already work. Check how a component is used before replacing it.
- Prefer configuration over hard-coded experimental values, and keep research definitions (waiting time, crossing duration, TTC) easy to find in the measurement code.
- Watch VR performance: stable frame rate first; avoid per-frame allocations and expensive per-frame searches.
- Don't commit `Library/`, `Logs/`, `Temp/`, builds or participant data.
- Stop and ask the team before changing what participants do, the measures or CSV schema, the scenario set, or anything that could affect ethics approval for a future study.
- Out of scope unless asked: city-scale modelling, live traffic feeds, multiplayer, online services, scoring, machine-learning pedestrian models.
