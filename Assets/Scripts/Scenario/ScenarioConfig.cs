using System;

// Field initialisers are the defaults used when a scenario JSON omits a field (JsonUtility keeps them).
[Serializable]
public class ScenarioConfig
{
    public string scenarioId;                     // e.g. "S1"; defaults to the file name when omitted
    public string scenarioName;

    // Reproducibility
    public int randomSeed = 12345;

    // Base driver (Intelligent Driver Model). Desired speed per vehicle comes from the spawn speed range.
    public float carSpeed = 12f;                  // v0 fallback when spawnCarMinSpeed >= spawnCarMaxSpeed (m/s)
    public float carTimeHeadway = 1.5f;           // T (s)
    public float carMinimumGap = 2f;              // s0 (m)
    public float carMaxAcceleration = 1.5f;       // a (m/s^2)
    public float carComfortableDeceleration = 2f; // b (m/s^2)

    // Arrivals per spawner: truncated shifted-exponential headways
    public float spawnIntervalMin = 1f;
    public float spawnIntervalMax = 5f;
    public float spawnIntervalMean = 0f;          // <= 0: midpoint of min and max

    public float spawnCarMinSpeed = 10f;
    public float spawnCarMaxSpeed = 13f;

    public float alternatePathChance = 0.25f;

    // At scenario start, the road is filled with the cars that would be on it after this many seconds of traffic, so
    // traffic is already flowing at the crossing (the spawn points are ~370 m away). 0 = start with an empty road.
    public float trafficPrefillSeconds = 90f;

    public bool trafficLightsEnabled = true;

    // Environment: night lighting (dark sky, street lamps on, car headlights). See LightingController.
    // Set from the researcher panel's "Night" option when a scenario starts (ScenarioManager.nightMode), so any value in a
    // scenario file is overridden; it is kept here so the applied lighting is part of the scenario settings code.
    public bool night = false;
}
