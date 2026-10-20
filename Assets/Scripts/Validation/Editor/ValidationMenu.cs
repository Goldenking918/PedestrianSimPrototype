using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Starts the participant-free validation run (<see cref="ValidationRunner"/>) from the menu or from the command line:
/// Unity.exe -batchmode -projectPath &lt;project&gt; -executeMethod ValidationMenu.RunBatch -pedValidation -logFile validation.log
/// Results are written to ValidationResults/ next to the project.
/// </summary>
public static class ValidationMenu
{
    const string ScenePath = "Assets/Scenes/SampleScene.unity";

    [MenuItem("Tools/Validation/Run (traffic repeatability + scripted walks)")]
    public static void Run()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;
        PlayerPrefs.SetInt(ValidationRunner.RequestPrefKey, 1);
        PlayerPrefs.Save();
        EditorSceneManager.OpenScene(ScenePath);
        EditorApplication.EnterPlaymode();
    }

    public static void RunBatch()
    {
        EditorSceneManager.OpenScene(ScenePath);
        EditorApplication.EnterPlaymode();
    }
}
