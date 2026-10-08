using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Researcher scenario menu drawn inside the headset, for the standalone Quest build where there is no PC monitor to show
/// <see cref="ScenarioSelectionPanel"/> on (IMGUI only draws on a desktop window). Offers the same controls as that panel.
///
/// Hidden while a trial runs. Controls (Quest controllers):
///   hold the LEFT controller's menu button for a second: open (held, so a participant can't open it by accident)
///   thumbstick up/down: choose · trigger or A/X: select · thumbstick left/right: change a value · menu button: close
///
/// The menu appears in the world in front of the headset when opened (not head-locked, which is less comfortable).
/// Added automatically by <see cref="ScenarioManager"/> on Android builds.
/// </summary>
[RequireComponent(typeof(ScenarioManager))]
public class HeadsetResearcherPanel : MonoBehaviour
{
    [Tooltip("How long the left menu button must be held to open the menu (s).")]
    [Min(0f)] public float holdToOpenSeconds = 1f;
    [Tooltip("Distance of the menu in front of the headset when opened (m).")]
    [Min(0.3f)] public float distance = 1.1f;
    [Tooltip("Hide the menu automatically after a scenario starts.")]
    public bool hideAfterStart = true;

    struct Item
    {
        public Func<string> label;
        public Action select;
        public Action<int> adjust;   // thumbstick left (-1) / right (+1); null if the item has no value
        public Func<bool> locked;    // true while it can't be changed (e.g. during a measured run)
    }

    ScenarioManager mManager;
    MeasurementManager mMeasurement;
    TextMeshPro mText;
    readonly List<Item> mItems = new List<Item>();
    int mCursor;
    bool mVisible;
    float mMenuHeld;
    bool mWaitForMenuRelease;
    bool mStickArmed;
    string mNotice = "";

    InputAction mMenuAction, mStickAction, mSelectAction;

    public bool IsVisible => mVisible;

    void Awake()
    {
        mManager = GetComponent<ScenarioManager>();

        mMenuAction = new InputAction("Researcher menu", InputActionType.Button, "<XRController>{LeftHand}/{MenuButton}");
        mStickAction = new InputAction("Researcher navigate", InputActionType.Value, expectedControlType: "Vector2");
        mStickAction.AddBinding("<XRController>{RightHand}/{Primary2DAxis}");
        mStickAction.AddBinding("<XRController>{LeftHand}/{Primary2DAxis}");
        mSelectAction = new InputAction("Researcher select", InputActionType.Button);
        mSelectAction.AddBinding("<XRController>{RightHand}/{TriggerButton}");
        mSelectAction.AddBinding("<XRController>{RightHand}/{PrimaryButton}");
        mSelectAction.AddBinding("<XRController>{LeftHand}/{TriggerButton}");
        mSelectAction.AddBinding("<XRController>{LeftHand}/{PrimaryButton}");
    }

    void OnEnable()
    {
        mMenuAction.Enable();
        mStickAction.Enable();
        mSelectAction.Enable();
    }

    void OnDisable()
    {
        mMenuAction.Disable();
        mStickAction.Disable();
        mSelectAction.Disable();
        SetVisible(false);
    }

    void OnDestroy()
    {
        mMenuAction.Dispose();
        mStickAction.Dispose();
        mSelectAction.Dispose();
        if (mText != null)
            Destroy(mText.gameObject);
    }

    void Start()
    {
        mMeasurement = FindFirstObjectByType<MeasurementManager>();

        var go = new GameObject("Headset researcher menu");
        mText = go.AddComponent<TextMeshPro>();
        mText.rectTransform.sizeDelta = new Vector2(0.9f, 0.75f); // metres
        mText.enableAutoSizing = true;
        mText.fontSizeMin = 0.1f;
        mText.fontSizeMax = 0.35f;
        mText.alignment = TextAlignmentOptions.TopLeft;
        mText.richText = true;
        go.SetActive(false);
    }

    void Update()
    {
        if (!mVisible)
        {
            // Open only after a deliberate hold
            mMenuHeld = mMenuAction.IsPressed() ? mMenuHeld + Time.unscaledDeltaTime : 0f;
            if (mMenuHeld >= holdToOpenSeconds)
            {
                mMenuHeld = 0f;
                mWaitForMenuRelease = true; // the same press must not close it again
                SetVisible(true);
            }
            return;
        }

        if (mWaitForMenuRelease)
            mWaitForMenuRelease = mMenuAction.IsPressed();
        else if (mMenuAction.WasPressedThisFrame())
        {
            SetVisible(false);
            return;
        }

        // One step per push of the thumbstick: act when it leaves the centre, then wait for it to return
        Vector2 stick = mStickAction.ReadValue<Vector2>();
        if (mStickArmed && stick.magnitude > 0.6f)
        {
            mStickArmed = false;
            if (Mathf.Abs(stick.y) >= Mathf.Abs(stick.x))
                mCursor = (mCursor + (stick.y > 0 ? -1 : 1) + mItems.Count) % mItems.Count;
            else
                Adjust(stick.x > 0 ? 1 : -1);
        }
        else if (stick.magnitude < 0.3f)
            mStickArmed = true;

        if (mSelectAction.WasPressedThisFrame())
            Select();

        if (mVisible)
            Redraw();
    }

    void SetVisible(bool visible)
    {
        mVisible = visible;
        if (mText == null)
            return;
        if (visible)
        {
            BuildItems();
            mNotice = "";
            PlaceInFrontOfHead();
            Redraw();
        }
        mText.gameObject.SetActive(visible);
    }

    void PlaceInFrontOfHead()
    {
        Transform head = mManager.playerMovement != null && mManager.playerMovement.vrCamera != null
            ? mManager.playerMovement.vrCamera
            : Camera.main != null ? Camera.main.transform : null;
        if (head == null)
            return;
        Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
        if (forward.sqrMagnitude < 1e-4f)
            forward = Vector3.ProjectOnPlane(head.up, Vector3.up); // looking straight down
        forward.Normalize();
        mText.transform.SetPositionAndRotation(head.position + forward * distance, Quaternion.LookRotation(forward, Vector3.up));
    }

    void BuildItems()
    {
        mItems.Clear();
        foreach (ScenarioManager.ScenarioEntry entry in ScenarioManager.GetAvailableScenarios())
        {
            ScenarioManager.ScenarioEntry e = entry;
            mItems.Add(new Item
            {
                label = () => $"Start {e.scenarioId}  {e.scenarioName}" +
                              (e.fileName == mManager.CurrentScenarioFile && mManager.IsRunning ? "   (running)" : ""),
                select = () => { if (mManager.StartScenario(e.fileName)) AfterStart(); },
            });
        }
        mItems.Add(new Item
        {
            label = () => "Restart current scenario",
            select = () => { if (mManager.RestartScenario()) AfterStart(); },
        });
        mItems.Add(new Item
        {
            label = () => "Stop traffic",
            select = () => { mManager.StopScenario(); mNotice = "Traffic stopped."; },
        });
        if (mManager.playerMovement != null)
            mItems.Add(new Item
            {
                label = () => "Return participant to start",
                select = () => { mManager.playerMovement.ReturnToStart(); mNotice = "Participant returned to the start."; },
            });
        if (mMeasurement != null)
            mItems.Add(new Item
            {
                label = () => $"Participant ID: {(string.IsNullOrEmpty(mMeasurement.participantId) ? "(none)" : mMeasurement.participantId)}",
                adjust = d => mMeasurement.participantId = StepParticipantId(mMeasurement.participantId, d),
                select = () => mMeasurement.participantId = StepParticipantId(mMeasurement.participantId, 1),
                locked = () => mMeasurement.IsMeasuring, // fixed for the duration of a run
            });
        // Midpoint flip and night are recorded in the CSV, so they are locked during a measured run (as on the PC panel)
        if (mManager.crossingManager != null)
            mItems.Add(new Item
            {
                label = () => $"Midpoint flip (pause and turn around halfway): {(mManager.crossingManager.midpointFlipEnabled ? "ON" : "OFF")}",
                adjust = _ => mManager.crossingManager.SetMidpointFlipEnabled(!mManager.crossingManager.midpointFlipEnabled),
                select = () => mManager.crossingManager.SetMidpointFlipEnabled(!mManager.crossingManager.midpointFlipEnabled),
                locked = () => mMeasurement != null && mMeasurement.IsMeasuring,
            });
        mItems.Add(new Item
        {
            label = () => $"Night (applies to the next scenario): {(mManager.nightMode ? "ON" : "OFF")}",
            adjust = _ => mManager.nightMode = !mManager.nightMode,
            select = () => mManager.nightMode = !mManager.nightMode,
            locked = () => mMeasurement != null && mMeasurement.IsMeasuring,
        });
        mItems.Add(new Item { label = () => "Close menu", select = () => SetVisible(false) });
        mCursor = Mathf.Clamp(mCursor, 0, mItems.Count - 1);
    }

    void AfterStart()
    {
        mNotice = "Scenario started.";
        if (hideAfterStart)
            SetVisible(false);
    }

    void Select()
    {
        Item item = mItems[mCursor];
        if (item.locked != null && item.locked())
            mNotice = "Locked while a run is being measured. Stop traffic first.";
        else
            item.select?.Invoke();
    }

    void Adjust(int direction)
    {
        Item item = mItems[mCursor];
        if (item.adjust == null)
            return;
        if (item.locked != null && item.locked())
            mNotice = "Locked while a run is being measured. Stop traffic first.";
        else
            item.adjust(direction);
    }

    /// <summary>Participant codes P01, P02, ... stepped with the thumbstick, since there is no keyboard in the headset.</summary>
    public static string StepParticipantId(string current, int direction)
    {
        string id = current ?? "";
        int digitsStart = id.Length;
        while (digitsStart > 0 && char.IsDigit(id[digitsStart - 1]))
            digitsStart--;
        int.TryParse(id.Substring(digitsStart), out int number);
        number = Mathf.Max(0, number + direction);
        return number == 0 ? "" : $"P{number:00}";
    }

    void Redraw()
    {
        var sb = new StringBuilder();
        sb.Append("<mark=#000000D0><b>Researcher menu</b>\n");
        sb.Append($"<size=75%>{StatusText()}\n");
        sb.Append("Thumbstick: choose / change · Trigger or A/X: select · Menu button: close</size>\n\n");
        for (int i = 0; i < mItems.Count; i++)
        {
            Item item = mItems[i];
            bool locked = item.locked != null && item.locked();
            string text = item.label() + (item.adjust != null ? "   < >" : "");
            if (locked)
                text = $"<color=#9A9A9A>{text}  (locked during run)</color>";
            sb.Append(i == mCursor ? $"<color=#FFD54A>> {text}</color>\n" : $"   {text}\n");
        }
        if (!string.IsNullOrEmpty(mNotice))
            sb.Append($"\n<size=75%>{mNotice}</size>\n");
        sb.Append($"\n<size=60%>Results: {MeasurementManager.ResultsFile}</size></mark>");
        mText.text = sb.ToString();
    }

    string StatusText()
    {
        ScenarioConfig s = mManager.currentScenario;
        if (!mManager.IsRunning || s == null)
            return "No scenario running";
        bool completed = mManager.crossingManager != null &&
                         mManager.crossingManager.currentState == CrossingManager.CrossingState.Completed;
        return $"Running {s.scenarioId} '{s.scenarioName}'{(s.night ? " (night)" : "")} · seed {s.randomSeed}" +
               (completed ? " · crossing complete" : "");
    }
}
