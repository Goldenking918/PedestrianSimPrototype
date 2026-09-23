using System;

[Serializable]
public class ScenarioConfig
{
    public string scenarioName;

    public float spawnIntervalMin;
    public float spawnIntervalMax;

    public float carSpeedMin;
    public float carSpeedMax;

    public float alternatePathChance;

    public bool trafficLightsEnabled;
}