using UnityEngine;

/// <summary>
/// Researcher scenario panel drawn with IMGUI. IMGUI renders to the Game view / desktop window only, never inside the
/// headset, so the researcher can pick and restart scenarios with the mouse or keyboard while the participant is in VR.
/// Also used for desktop dev testing.
///
/// Keys: Tab toggles the panel; while it is open, 1-9 start a scenario, R restarts the current one, X stops traffic
/// (keys are ignored while typing the participant ID).
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
    MeasurementManager mMeasurement;
    ScenarioManager.ScenarioEntry[] mFiles = new ScenarioManager.ScenarioEntry[0];
    bool mVisible;
    bool mWasCompleted;
    Vector2 mScroll;
    GUIStyle mHeader, mSmall;

    public bool IsVisible => mVisible;

    void Awake()
    {
        mManager = GetComponent<ScenarioManager>();
    }

    void Start()
    {
        mMeasurement = FindFirstObjectByType<MeasurementManager>();
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

        // Participant reached the end: open the panel so the operator can reset or pick the next scenario
        bool completed = CrossingCompleted();
        if (completed && !mWasCompleted)
            SetVisible(true);
        mWasCompleted = completed;

        if (!mVisible || GUIUtility.keyboardControl != 0) // typing in the participant ID box
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
        if (!visible)
            GUIUtility.keyboardControl = 0; // stop typing in the participant ID box
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

        float height = Mathf.Min(ReferenceHeight - 20f, 385f + mFiles.Length * 32f);
        GUILayout.BeginArea(new Rect(10, 10, PanelWidth, height), GUI.skin.box);
        GUILayout.Label("Researcher: scenario", mHeader);
        GUILayout.Label(StatusText(), mSmall);
        if (CrossingCompleted())
            GUILayout.Label("Crossing complete. Ask the participant to walk back to the start, then press Restart or pick " +
                            "a scenario (this brings them back to the start).", mSmall);
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
        if (mManager.playerMovement != null && GUILayout.Button("Return participant to start", GUILayout.Height(24)))
            mManager.playerMovement.ReturnToStart();
        GUILayout.Label($"[{toggleKey}] hide · number keys start a scenario", mSmall);

        if (mMeasurement != null)
        {
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Participant ID", GUILayout.Width(95));
            GUI.enabled = !mMeasurement.IsMeasuring; // fixed for the duration of a run
            mMeasurement.participantId = GUILayout.TextField(mMeasurement.participantId ?? "", 32);
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return)
                GUIUtility.keyboardControl = 0;

            // Midpoint pause + view flip (and its prompt and approach marker). Saved for future sessions and
            // recorded in the CSV, so it is locked while a run is being measured.
            CrossingManager crossing = mManager.crossingManager;
            if (crossing != null)
            {
                GUI.enabled = !mMeasurement.IsMeasuring;
                bool flip = GUILayout.Toggle(crossing.midpointFlipEnabled, " Midpoint flip (pause and turn around halfway)");
                if (flip != crossing.midpointFlipEnabled)
                    crossing.SetMidpointFlipEnabled(flip);
                GUI.enabled = true;
            }

            // Night lighting for the next scenario started (any traffic level). Recorded in the CSV, so locked during a run.
            GUI.enabled = !mMeasurement.IsMeasuring;
            mManager.nightMode = GUILayout.Toggle(mManager.nightMode, " Night (applies to the next scenario)");
            GUI.enabled = true;
            GUILayout.Label($"Results: {MeasurementManager.ResultsFile}", mSmall);
        }
        GUILayout.EndArea();
    }

    bool CrossingCompleted() =>
        mManager.crossingManager != null && mManager.crossingManager.currentState == CrossingManager.CrossingState.Completed;

    string StatusText()
    {
        ScenarioConfig s = mManager.currentScenario;
        if (!mManager.IsRunning || s == null)
            return "No scenario running";
        return $"Running {s.scenarioId} '{s.scenarioName}'{(s.night ? " (night)" : "")} · seed {s.randomSeed}" +
               (CrossingCompleted() ? " · crossing complete" : "");
    }
}
