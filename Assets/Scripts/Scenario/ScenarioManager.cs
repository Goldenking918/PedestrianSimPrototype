using UnityEngine;
using System.IO;
using TMPro;

public class ScenarioManager : MonoBehaviour
{
    public ScenarioConfig currentScenario;
    public TrafficController trafficController;
    public TMP_Dropdown scenarioDropdown;
    public GameObject scenarioPanel;

    void Start()
    {
        // string selectedScenario = scenarioDropdown.options[scenarioDropdown.value].text;
        // StartScenario(selectedScenario + ".json");
        StartScenario("HighTraffic.json");

    }
    public void StartSelectedScenario()
    {
        string selectedScenario = scenarioDropdown.options[
            scenarioDropdown.value
        ].text;

        Debug.Log("Selected scenario: " + selectedScenario);

        StartScenario(selectedScenario);

        scenarioPanel.SetActive(false);
    }


    public void LoadScenario(string fileName)
    {
        string path = Path.Combine(
            Application.streamingAssetsPath,
            "Scenarios",
            fileName
        );

        string json = File.ReadAllText(path);

        currentScenario = JsonUtility.FromJson<ScenarioConfig>(json);

        Debug.Log("Loaded scenario: " + currentScenario.scenarioName);
    }
    public ScenarioConfig GetScenario()
    {
        return currentScenario;
    }

    public void StartScenario(string fileName)
    {
        LoadScenario(fileName);

        ApplyScenario();
        
        trafficController.ApplyToAllSpawners();

        trafficController.StartAllSpawners();
    }
    private void ApplyScenario()
    {
        trafficController.carSpeed =
            currentScenario.carSpeed;

        trafficController.carAcceleration =
            currentScenario.carAcceleration;

        trafficController.carDeceleration =
            currentScenario.carDeceleration;

        trafficController.spawnIntervalMin =
            currentScenario.spawnIntervalMin;

        trafficController.spawnIntervalMax =
            currentScenario.spawnIntervalMax;

        trafficController.spawnCarMinSpeed =
            currentScenario.spawnCarMinSpeed;

        trafficController.spawnCarMaxSpeed =
            currentScenario.spawnCarMaxSpeed;

        trafficController.alternatePathChance =
            currentScenario.alternatePathChance;
    }
}