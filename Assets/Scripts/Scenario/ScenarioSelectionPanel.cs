using UnityEngine;

/// <summary>
/// Researcher scenario panel drawn with IMGUI. IMGUI renders to the Game view / desktop window only, never inside the
/// headset, so the researcher can pick and restart scenarios with the mouse or keyboard while the participant is in VR.
/// Also used for desktop dev testing.
///
/// Keys: Tab toggles the panel; while it is open, 1-9 start a scenario, R restarts the current one, X stops traffic.
/// Added automatically by <see cref="ScenarioManager"/>.
/// </summary>
[RequireComponent(typeof(ScenarioManager))]
public class ScenarioSelectionPanel : MonoBehaviour
{
    public KeyCode toggleKey = KeyCode.Tab;
    [Tooltip("Hide the panel automatically after a scenario starts.")]
    public bool hideAfterStart = true;

    const float ReferenceHeight = 1080f;
    const float PanelWidth = 360f;

    ScenarioManager mManager;
    ScenarioManager.ScenarioEntry[] mFiles = new ScenarioManager.ScenarioEntry[0];
    bool mVisible;
    Vector2 mScroll;
    GUIStyle mHeader, mSmall;

    public bool IsVisible => mVisible;

    void Awake()
    {
        mManager = GetComponent<ScenarioManager>();
    }

    void Start()
    {
        mFiles = ScenarioManager.GetAvailableScenarios();
        SetVisible(!mManager.IsRunning); // prompt the researcher if nothing was auto-started
    }

    void OnDisable()
    {
        if (mVisible)
            MouseLook.SuppressLook = false;
    }

    void Update()
    {
        if (Input.GetKeyDown(toggleKey))
            SetVisible(!mVisible);

        if (!mVisible)
            return;

        for (int i = 0; i < mFiles.Length && i < 9; i++)
            if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i))
                StartScenario(mFiles[i].fileName);

        if (Input.GetKeyDown(KeyCode.R) && mManager.RestartScenario() && hideAfterStart)
            SetVisible(false);
        if (Input.GetKeyDown(KeyCode.X))
            mManager.StopScenario();
    }

    void SetVisible(bool visible)
    {
        mVisible = visible;
        MouseLook.SuppressLook = visible;
        if (visible)
            mFiles = ScenarioManager.GetAvailableScenarios(); // pick up edited/new files without restarting
    }

    void StartScenario(string file)
    {
        if (mManager.StartScenario(file) && hideAfterStart)
            SetVisible(false);
    }

    void OnGUI()
    {
        float scale = Mathf.Max(0.5f, Screen.height / ReferenceHeight);
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

        if (mHeader == null)
        {
            mHeader = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 16 };
            mSmall = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
        }

        if (!mVisible)
        {
            GUI.Label(new Rect(10, 10, 400, 22), $"{StatusText()}   [{toggleKey}] scenarios", mSmall);
            return;
        }

        float height = Mathf.Min(ReferenceHeight - 20f, 170f + mFiles.Length * 32f);
        GUILayout.BeginArea(new Rect(10, 10, PanelWidth, height), GUI.skin.box);
        GUILayout.Label("Researcher: scenario", mHeader);
        GUILayout.Label(StatusText(), mSmall);
        GUILayout.Space(6);

        mScroll = GUILayout.BeginScrollView(mScroll);
        if (mFiles.Length == 0)
            GUILayout.Label($"No scenario files found in {ScenarioManager.ScenarioDirectory}", mSmall);
        for (int i = 0; i < mFiles.Length; i++)
        {
            var entry = mFiles[i];
            string label = (i < 9 ? $"{i + 1}.  " : "     ") + $"{entry.scenarioId}  {entry.scenarioName}";
            if (entry.fileName == mManager.CurrentScenarioFile && mManager.IsRunning)
                label += "   (running)";
            if (GUILayout.Button(new GUIContent(label, entry.fileName), GUILayout.Height(28)))
                StartScenario(entry.fileName);
        }
        GUILayout.EndScrollView();

        GUILayout.BeginHorizontal();
        GUI.enabled = !string.IsNullOrEmpty(mManager.CurrentScenarioFile);
        if (GUILayout.Button("Restart [R]", GUILayout.Height(28)) && mManager.RestartScenario() && hideAfterStart)
            SetVisible(false);
        GUI.enabled = mManager.IsRunning;
        if (GUILayout.Button("Stop traffic [X]", GUILayout.Height(28)))
            mManager.StopScenario();
        GUI.enabled = true;
        GUILayout.EndHorizontal();
        GUILayout.Label($"[{toggleKey}] hide · number keys start a scenario", mSmall);
        GUILayout.EndArea();
    }

    string StatusText()
    {
        ScenarioConfig s = mManager.currentScenario;
        if (!mManager.IsRunning || s == null)
            return "No scenario running";
        return $"Running {s.scenarioId} '{s.scenarioName}' · seed {s.randomSeed}";
    }
}
