using System.Collections;
using UnityEngine;

public class CrossingManager : MonoBehaviour
{
    public enum CrossingState
    {
        NotStarted,
        CrossingToMidpoint,
        WaitingForTurn,
        CrossingToEnd,
        Completed
    }

    public CrossingState currentState = CrossingState.NotStarted;

    public PlayerMovement playerMovement;
    [Tooltip("Stop everything at the midpoint and flip the view so the participant can turn around in the room. " +
             "Untick to let the participant walk straight through the midpoint (e.g. when the lab space is long enough).")]
    public bool midpointFlipEnabled = true;
    public float midpointWaitDuration = 5f;

    /// <summary>Seconds left in the midpoint pause (0 when not paused). Used by <see cref="MidpointTurnPrompt"/>.</summary>
    public float MidpointTimeRemaining =>
        currentState == CrossingState.WaitingForTurn ? Mathf.Max(0f, midpointResumeTime - Time.realtimeSinceStartup) : 0f;

    private float midpointResumeTime;

    /// <summary>True once the pedestrian has stepped off the kerb (entered the RoadEdge trigger) in this crossing.</summary>
    public bool HasReachedRoadEdge { get; private set; }

    private void Awake()
    {
        if (playerMovement == null)
            playerMovement = FindFirstObjectByType<PlayerMovement>();
    }

    public void StartCrossing()
    {
        currentState = CrossingState.CrossingToMidpoint;
        HasReachedRoadEdge = false;
        SetPlayerMovementEnabled(true);

        Debug.Log("Crossing started");
    }

    public void ReachRoadEdge()
    {
        if (currentState != CrossingState.CrossingToMidpoint || HasReachedRoadEdge)
            return;

        HasReachedRoadEdge = true;

        Debug.Log("Stepped off the kerb.");
    }

    public void ReachMidpoint()
    {
        if (currentState != CrossingState.CrossingToMidpoint)
            return;

        if (!midpointFlipEnabled)
        {
            currentState = CrossingState.CrossingToEnd; // no stop or flip: carry straight on to the End trigger
            Debug.Log("Reached midpoint (flip disabled). Continuing crossing.");
            return;
        }

        currentState = CrossingState.WaitingForTurn;

        PauseCrossing();

        Debug.Log("Reached midpoint. Waiting for participant to turn around.");
    }

    public void ContinueCrossing()
    {
        if (currentState != CrossingState.WaitingForTurn)
            return;

        currentState = CrossingState.CrossingToEnd;

        ResumeCrossing();

        Debug.Log("Crossing resumed.");
    }

    public void FinishCrossing()
    {
        if (currentState != CrossingState.CrossingToEnd)
            return;

        currentState = CrossingState.Completed;

        Debug.Log("Crossing completed.");
    }

    private void PauseCrossing()
    {
        SetPlayerMovementEnabled(false);
        SetTrafficPaused(true);
        midpointResumeTime = Time.realtimeSinceStartup + midpointWaitDuration;
        StartCoroutine(ResumeAfterMidpointWait());
    }

    private void ResumeCrossing()
    {
        if (playerMovement != null)
            playerMovement.RequestFlipAroundHeadset();

        SetPlayerMovementEnabled(true);
        SetTrafficPaused(false);
    }

    private IEnumerator ResumeAfterMidpointWait()
    {
        yield return new WaitForSecondsRealtime(midpointWaitDuration);
        ContinueCrossing();
    }

    private void SetPlayerMovementEnabled(bool enabled)
    {
        if (playerMovement != null)
            playerMovement.SetMovementEnabled(enabled);
    }

    private void SetTrafficPaused(bool paused)
    {
        TrafficController trafficController = TrafficController.FindController();
        if (trafficController != null)
            trafficController.SetTrafficPaused(paused);
    }
}