using System.Linq;
using NUnit.Framework;

/// <summary>Checks every scenario file in StreamingAssets/Scenarios loads and describes a usable traffic condition.</summary>
public class ScenarioFileTests
{
    [Test]
    public void ScenarioFolder_ContainsScenarios()
    {
        Assert.IsNotEmpty(ScenarioManager.GetAvailableScenarioFiles());
    }

    [Test]
    public void EveryScenarioFile_LoadsWithValidValues()
    {
        foreach (string file in ScenarioManager.GetAvailableScenarioFiles())
        {
            ScenarioConfig s = ScenarioManager.ReadScenarioFile(file);
            Assert.IsNotNull(s, $"{file} failed to load");
            Assert.IsFalse(string.IsNullOrWhiteSpace(s.scenarioName), $"{file}: scenarioName missing");
            Assert.Greater(s.carSpeed, 0f, $"{file}: carSpeed");
            Assert.Greater(s.carTimeHeadway, 0f, $"{file}: carTimeHeadway");
            Assert.Greater(s.carMaxAcceleration, 0f, $"{file}: carMaxAcceleration");
            Assert.Greater(s.carComfortableDeceleration, 0f, $"{file}: carComfortableDeceleration");
            Assert.Greater(s.spawnIntervalMin, 0f, $"{file}: spawnIntervalMin");
            Assert.GreaterOrEqual(s.spawnIntervalMax, s.spawnIntervalMin, $"{file}: spawnIntervalMax < spawnIntervalMin");
            Assert.Greater(s.spawnCarMinSpeed, 0f, $"{file}: spawnCarMinSpeed");
            Assert.GreaterOrEqual(s.spawnCarMaxSpeed, s.spawnCarMinSpeed, $"{file}: spawnCarMaxSpeed < spawnCarMinSpeed");
            Assert.That(s.alternatePathChance, Is.InRange(0f, 1f), $"{file}: alternatePathChance");
        }
    }

    [Test]
    public void ScenarioIds_AreUnique()
    {
        var ids = ScenarioManager.GetAvailableScenarios().Select(e => e.scenarioId).ToList();
        CollectionAssert.AllItemsAreUnique(ids);
        Assert.AreEqual(ScenarioManager.GetAvailableScenarioFiles().Length, ids.Count, "a scenario file failed to load");
    }
}
