using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.XR.Management;

/// <summary>
/// Builds the app to run standalone on a Meta Quest (Android, no PC or Quest Link needed).
///
/// Tools > Quest > Build and Install on Headset: switches to Android if needed, sets up XR, builds the APK and installs it
/// on a Quest connected by USB (developer mode on). Tools > Quest > Build APK only builds it, for installing with
/// SideQuest or adb. Results are saved on the headset under Application.persistentDataPath (see MeasurementManager).
/// </summary>
public static class QuestBuild
{
    public const string ApkPath = "Builds/Quest/PedestrianSim.apk";

    const string OpenXRLoaderType = "UnityEngine.XR.OpenXR.OpenXRLoader";
    static readonly string[] QuestFeatureIds =
    {
        "com.unity.openxr.feature.metaquest",             // Meta Quest support (Android manifest, target devices)
        "com.unity.openxr.feature.input.oculustouch",     // Quest 2 / Quest Pro controllers
        "com.unity.openxr.feature.input.metaquestplus",   // Quest 3 / 3S controllers
    };

    [MenuItem("Tools/Quest/Build and Install on Headset")]
    public static void BuildAndInstall() => Build(BuildOptions.AutoRunPlayer);

    [MenuItem("Tools/Quest/Build APK")]
    public static void BuildApk() => Build(BuildOptions.None);

    /// <summary>
    /// Turns on OpenXR with Meta Quest support and the Quest controller profiles for Android. Safe to run repeatedly; the
    /// Windows (Quest Link) settings are not touched.
    /// </summary>
    [MenuItem("Tools/Quest/Set Up XR for Quest (Android)")]
    public static bool ConfigureXR()
    {
        if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget perTarget))
        {
            Debug.LogError("[QuestBuild] XR Plug-in Management settings not found. Open Project Settings > XR Plug-in Management once, then retry.");
            return false;
        }

        if (!perTarget.HasManagerSettingsForBuildTarget(BuildTargetGroup.Android))
            perTarget.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android);
        XRGeneralSettings android = perTarget.SettingsForBuildTarget(BuildTargetGroup.Android);
        android.InitManagerOnStart = true;
        if (!XRPackageMetadataStore.IsLoaderAssigned(OpenXRLoaderType, BuildTargetGroup.Android) &&
            !XRPackageMetadataStore.AssignLoader(android.Manager, OpenXRLoaderType, BuildTargetGroup.Android))
        {
            Debug.LogError("[QuestBuild] Could not turn on OpenXR for Android. Tick OpenXR on the Android tab of Project Settings > XR Plug-in Management.");
            return false;
        }

        FeatureHelpers.RefreshFeatures(BuildTargetGroup.Android);
        bool ok = true;
        foreach (string id in QuestFeatureIds)
        {
            var feature = FeatureHelpers.GetFeatureWithIdForBuildTarget(BuildTargetGroup.Android, id);
            if (feature == null)
            {
                Debug.LogError($"[QuestBuild] OpenXR feature {id} not found for Android.");
                ok = false;
                continue;
            }
            if (!feature.enabled)
            {
                feature.enabled = true;
                EditorUtility.SetDirty(feature);
            }
        }

        EditorUtility.SetDirty(android);
        EditorUtility.SetDirty(perTarget);
        AssetDatabase.SaveAssets();
        Debug.Log("[QuestBuild] XR set up for Quest: OpenXR with Meta Quest support and Quest controller profiles (Android).");
        return ok;
    }

    static void Build(BuildOptions options)
    {
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android &&
            !EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
        {
            Debug.LogError("[QuestBuild] Could not switch to Android. Install Android Build Support (with OpenJDK and Android SDK & NDK) " +
                           "for this Unity version in Unity Hub > Installs > Add modules.");
            return;
        }
        if (!ConfigureXR())
            return;

        EditorUserBuildSettings.buildAppBundle = false; // an .apk to sideload, not a Play Store bundle
        Directory.CreateDirectory(Path.GetDirectoryName(ApkPath));
        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
            locationPathName = ApkPath,
            target = BuildTarget.Android,
            targetGroup = BuildTargetGroup.Android,
            options = options,
        });

        if (report.summary.result == BuildResult.Succeeded)
        {
            Debug.Log($"[QuestBuild] Built {Path.GetFullPath(ApkPath)} ({report.summary.totalSize / (1024 * 1024)} MB)" +
                      ((options & BuildOptions.AutoRunPlayer) != 0 ? " and sent it to the headset." : "."));
            EditorUtility.RevealInFinder(ApkPath);
        }
        else
            Debug.LogError($"[QuestBuild] Build {report.summary.result} with {report.summary.totalErrors} error(s). See the Console above.");
    }

    /// <summary>
    /// Writes StreamingAssets/Scenarios/index.txt listing the scenario files. On Android they are packed inside the APK,
    /// where the folder can't be listed, so ScenarioManager reads this list instead. Runs before every build.
    /// </summary>
    public static void WriteScenarioIndex()
    {
        string folder = Path.Combine(Application.streamingAssetsPath, "Scenarios");
        if (!Directory.Exists(folder))
            return;
        string[] files = Directory.GetFiles(folder, "*.json")
            .Select(Path.GetFileName)
            .OrderBy(f => f, System.StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string index = string.Join("\n", files) + "\n";
        string path = Path.Combine(folder, ScenarioManager.ScenarioIndexFile);
        if (File.Exists(path) && File.ReadAllText(path) == index)
            return;
        File.WriteAllText(path, index); // StreamingAssets are copied straight from disk, so no import is needed
        Debug.Log($"[QuestBuild] Updated {path}: {string.Join(", ", files)}");
    }
}

/// <summary>Keeps the scenario list used by the Android build up to date (see <see cref="QuestBuild.WriteScenarioIndex"/>).</summary>
public class ScenarioIndexBeforeBuild : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;
    public void OnPreprocessBuild(BuildReport report) => QuestBuild.WriteScenarioIndex();
}
