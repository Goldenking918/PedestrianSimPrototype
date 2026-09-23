using System;

[Serializable]
public class ScenarioConfig
{
    public string scenarioName;

    public float carSpeed;
    public float carAcceleration;
    public float carDeceleration;

    public float spawnIntervalMin;
    public float spawnIntervalMax;

    public float spawnCarMinSpeed;
    public float spawnCarMaxSpeed;

    public float alternatePathChance;

    public bool trafficLightsEnabled;
}