using System;
using System.IO;
using System.Linq;
using UnityEngine;
using TMPro;

/// <summary>
/// Loads scenario JSON files from StreamingAssets/Scenarios and applies them to the traffic system.
/// Scenarios are chosen by the researcher (see <see cref="ScenarioSelectionPanel"/>, shown on the PC monitor only),
/// or automatically via <see cref="autoStartScenario"/> for dev convenience.
/// Starting a scenario always resets traffic first, so every run of a scenario starts from the same seeded state.
/// </summary>
public class ScenarioManager : MonoBehaviour
{
    public ScenarioConfig currentScenario;
    public TrafficController trafficController;
    [Tooltip("Reset to the start of the crossing whenever a scenario starts, so every run can be completed. Found automatically if empty.")]
    public CrossingManager crossingManager;
    [Tooltip("Returned to the start of the crossing whenever a scenario starts. Found automatically if empty.")]
    public PlayerMovement playerMovement;
    [Tooltip("Scenario file to start automatically on play (e.g. MediumTraffic.json). Leave empty to wait for the researcher to pick one.")]
    public string autoStartScenario = "";

    [Header("Legacy in-world UI (optional)")]
    public TMP_Dropdown scenarioDropdown;
    public GameObject scenarioPanel;

    /// <summary>Raised after a scenario has been applied and traffic started (starts a measured run, see MeasurementManager).</summary>
    public event Action<ScenarioConfig> ScenarioStarted;
    /// <summary>Raised after traffic has been stopped and cleared.</summary>
    public event Action ScenarioStopped;

    public string CurrentScenarioFile { get; private set; }
    public bool IsRunning { get; private set; }

    public static string ScenarioDirectory => Path.Combine(Application.streamingAssetsPath, "Scenarios");

    void Awake()
    {
        if (trafficController == null)
            trafficController = TrafficController.FindController();
        if (crossingManager == null)
            crossingManager = FindFirstObjectByType<CrossingManager>();
        if (playerMovement == null)
            playerMovement = FindFirstObjectByType<PlayerMovement>();
        if (FindFirstObjectByType<MeasurementManager>() == null)
            gameObject.AddComponent<MeasurementManager>();
        if (GetComponent<ScenarioSelectionPanel>() == null)
            gameObject.AddComponent<ScenarioSelectionPanel>();
    }

    void Start()
    {
        if (!string.IsNullOrWhiteSpace(autoStartScenario))
            StartScenario(autoStartScenario.Trim());
    }

    public struct ScenarioEntry
    {
        public string fileName;
        public string scenarioId;
        public string scenarioName;
    }

    /// <summary>Valid scenarios in StreamingAssets/Scenarios, sorted by scenario ID (invalid files are skipped with an error).</summary>
    public static ScenarioEntry[] GetAvailableScenarios()
    {
        return GetAvailableScenarioFiles()
            .Select(f => (file: f, config: ReadScenarioFile(f)))
            .Where(x => x.config != null)
            .Select(x => new ScenarioEntry { fileName = x.file, scenarioId = x.config.scenarioId, scenarioName = x.config.scenarioName })
            .OrderBy(e => e.scenarioId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>Scenario file names (e.g. "LowTraffic.json") available in StreamingAssets/Scenarios, sorted by name.</summary>
    public static string[] GetAvailableScenarioFiles()
    {
        if (!Directory.Exists(ScenarioDirectory))
            return Array.Empty<string>();
        return Directory.GetFiles(ScenarioDirectory, "*.json")
            .Select(Path.GetFileName)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>Reads and parses a scenario file. Returns null (and logs an error) if it is missing, empty or invalid.</summary>
    public static ScenarioConfig ReadScenarioFile(string fileName)
    {
        string path = Path.Combine(ScenarioDirectory, fileName);
        try
        {
            string json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
                throw new InvalidDataException("file is empty");
            ScenarioConfig config = JsonUtility.FromJson<ScenarioConfig>(json);
            if (config == null)
                throw new InvalidDataException("could not parse JSON");
            if (string.IsNullOrWhiteSpace(config.scenarioId))
                config.scenarioId = Path.GetFileNameWithoutExtension(fileName);
            return config;
        }
        catch (Exception e)
        {
            Debug.LogError($"[ScenarioManager] Could not load scenario '{fileName}': {e.Message}");
            return null;
        }
    }

    public void StartSelectedScenario()
    {
        if (scenarioDropdown == null)
            return;

        string selectedScenario = scenarioDropdown.options[scenarioDropdown.value].text;
        if (!selectedScenario.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            selectedScenario += ".json";
        StartScenario(selectedScenario);

        if (scenarioPanel != null)
            scenarioPanel.SetActive(false);
    }

    public void LoadScenario(string fileName)
    {
        ScenarioConfig config = ReadScenarioFile(fileName);
        if (config != null)
        {
            currentScenario = config;
            CurrentScenarioFile = fileName;
        }
    }

    public ScenarioConfig GetScenario()
    {
        return currentScenario;
    }

    /// <summary>Stops and clears any running traffic, then applies the scenario and starts it from its seed.</summary>
    public bool StartScenario(string fileName)
    {
        ScenarioConfig config = ReadScenarioFile(fileName);
        if (config == null || trafficController == null)
        {
            if (trafficController == null)
                Debug.LogError("[ScenarioManager] No TrafficController assigned.", this);
            return false;
        }

        StopScenario();

        currentScenario = config;
        CurrentScenarioFile = fileName;
        ApplyScenario();
        if (playerMovement != null)
            playerMovement.ReturnToStart(); // every run starts at the beginning of the crossing
        if (crossingManager != null)
            crossingManager.StartCrossing(); // fresh crossing for every run
        trafficController.ApplyToAllSpawners();
        SignalPlan signals = trafficController.ApplySignalPlan(); // restart the lights at phase 1: identical timing every run
        trafficController.StartAllSpawners();
        IsRunning = true;

        // Full applied configuration, so a run can be traced back to its exact conditions.
        Debug.Log($"[ScenarioManager] Started scenario {currentScenario.scenarioId} '{currentScenario.scenarioName}' " +
                  $"from {fileName} (seed {currentScenario.randomSeed}) at t={Time.time:F2}s\n" +
                  $"Signals: {(signals != null ? signals.Describe() : "scene's own light program")}\n" +
                  $"{JsonUtility.ToJson(currentScenario, true)}", this);
        ScenarioStarted?.Invoke(currentScenario);
        return true;
    }

    /// <summary>Restarts the current scenario from the beginning (same seed, so the same vehicle sequence).</summary>
    public bool RestartScenario()
    {
        return !string.IsNullOrEmpty(CurrentScenarioFile) && StartScenario(CurrentScenarioFile);
    }

    /// <summary>Stops spawning and removes all vehicles.</summary>
    public void StopScenario()
    {
        if (trafficController == null)
            return;

        trafficController.StopAllSpawners();
        trafficController.SetTrafficPaused(false);
        int removed = trafficController.ClearAllVehicles();

        if (IsRunning)
        {
            IsRunning = false;
            Debug.Log($"[ScenarioManager] Stopped scenario {currentScenario?.scenarioId} ({removed} vehicles removed) at t={Time.time:F2}s", this);
            ScenarioStopped?.Invoke();
        }
    }

    private void ApplyScenario()
    {
        trafficController.randomSeed =
            currentScenario.randomSeed;

        trafficController.idm.desiredSpeed = currentScenario.carSpeed;
        trafficController.idm.timeHeadway = currentScenario.carTimeHeadway;
        trafficController.idm.minimumGap = currentScenario.carMinimumGap;
        trafficController.idm.maxAcceleration = currentScenario.carMaxAcceleration;
        trafficController.idm.comfortableDeceleration = currentScenario.carComfortableDeceleration;

        trafficController.spawnIntervalMin =
            currentScenario.spawnIntervalMin;

        trafficController.spawnIntervalMax =
            currentScenario.spawnIntervalMax;

        trafficController.spawnIntervalMean =
            currentScenario.spawnIntervalMean;

        trafficController.spawnCarMinSpeed =
            currentScenario.spawnCarMinSpeed;

        trafficController.spawnCarMaxSpeed =
            currentScenario.spawnCarMaxSpeed;

        trafficController.alternatePathChance =
            currentScenario.alternatePathChance;

        trafficController.prefillSeconds =
            currentScenario.trafficPrefillSeconds;
    }
}
