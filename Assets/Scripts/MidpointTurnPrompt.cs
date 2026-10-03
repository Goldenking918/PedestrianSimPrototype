using TMPro;
using UnityEngine;

/// <summary>
/// Shows the "turn around" panel while the crossing is paused at the midpoint, with a countdown until the view flips
/// and traffic resumes. The panel is a world-space canvas parented to the camera, so it is visible in the headset
/// (screen-space overlays are not rendered in VR) and on the desktop.
/// </summary>
public class MidpointTurnPrompt : MonoBehaviour
{
    public CrossingManager crossingManager;
    [Tooltip("Panel shown during the midpoint pause (hidden the rest of the time).")]
    public GameObject panel;
    public TMP_Text countdownText;
    [Tooltip("{0} is replaced by the whole seconds remaining.")]
    public string countdownFormat = "Continuing in {0}";

    void Awake()
    {
        if (crossingManager == null)
            crossingManager = FindFirstObjectByType<CrossingManager>();
        if (panel != null)
            panel.SetActive(false);
    }

    void Update()
    {
        bool paused = crossingManager != null && crossingManager.currentState == CrossingManager.CrossingState.WaitingForTurn;
        if (panel != null && panel.activeSelf != paused)
            panel.SetActive(paused);

        if (paused && countdownText != null)
            countdownText.text = string.Format(countdownFormat, Mathf.Max(1, Mathf.CeilToInt(crossingManager.MidpointTimeRemaining)));
    }
}
