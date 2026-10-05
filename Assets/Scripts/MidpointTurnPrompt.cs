using TMPro;
using UnityEngine;

/// <summary>
/// Participant-facing prompts, on a world-space canvas parented to the camera so they are visible in the headset
/// (screen-space overlays are not rendered in VR) and on the desktop:
///  - the "turn around" panel while the crossing is paused at the midpoint, with a countdown until the view flips and
///    traffic resumes;
///  - the "walk back to the start" panel once the crossing is complete, until the operator starts the next run.
/// </summary>
public class MidpointTurnPrompt : MonoBehaviour
{
    public CrossingManager crossingManager;
    [Tooltip("Panel shown during the midpoint pause (hidden the rest of the time).")]
    public GameObject panel;
    public TMP_Text countdownText;
    [Tooltip("{0} is replaced by the whole seconds remaining.")]
    public string countdownFormat = "Continuing in {0}";
    [Tooltip("Panel shown once the participant has reached the end, until the operator starts the next run.")]
    public GameObject completePanel;

    void Awake()
    {
        if (crossingManager == null)
            crossingManager = FindFirstObjectByType<CrossingManager>();
        if (panel != null)
            panel.SetActive(false);
        if (completePanel != null)
            completePanel.SetActive(false);
    }

    void Update()
    {
        bool paused = crossingManager != null && crossingManager.currentState == CrossingManager.CrossingState.WaitingForTurn;
        if (panel != null && panel.activeSelf != paused)
            panel.SetActive(paused);

        bool completed = crossingManager != null && crossingManager.currentState == CrossingManager.CrossingState.Completed;
        if (completePanel != null && completePanel.activeSelf != completed)
            completePanel.SetActive(completed);

        if (paused && countdownText != null)
            countdownText.text = string.Format(countdownFormat, Mathf.Max(1, Mathf.CeilToInt(crossingManager.MidpointTimeRemaining)));
    }
}
