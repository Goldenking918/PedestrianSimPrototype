using UnityEngine;
using System.IO;

public class ScenarioManager : MonoBehaviour
{
    public ScenarioConfig currentScenario;

    void Start()
    {
        LoadScenario("HighTraffic.json");

        Debug.Log("Speed: " + currentScenario.carSpeedMin);
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
}