using System.IO;
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

    [Test]
    public void ScenarioIndex_ListsEveryScenarioFile()
    {
        // The Android (Quest) build can't list the scenario folder inside the APK, so it reads this index instead.
        // It is rewritten before every build; if this fails, run Tools > Quest > Build APK (or any build) once and commit it.
        string index = Path.Combine(ScenarioManager.ScenarioDirectory, ScenarioManager.ScenarioIndexFile);
        Assert.IsTrue(File.Exists(index), $"{index} missing");
        string[] listed = File.ReadAllLines(index).Where(l => l.Trim().Length > 0).OrderBy(f => f).ToArray();
        CollectionAssert.AreEqual(ScenarioManager.GetAvailableScenarioFiles().OrderBy(f => f).ToArray(), listed);
    }

    [TestCase("", 1, "P01")]
    [TestCase("P01", 1, "P02")]
    [TestCase("P09", 1, "P10")]
    [TestCase("P02", -1, "P01")]
    [TestCase("P01", -1, "")]
    [TestCase("", -1, "")]
    [TestCase("pilot3", 1, "P04")]
    public void HeadsetParticipantId_StepsThroughCodes(string current, int direction, string expected)
    {
        Assert.AreEqual(expected, HeadsetResearcherPanel.StepParticipantId(current, direction));
    }
}
