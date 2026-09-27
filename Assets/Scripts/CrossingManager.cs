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
    public float midpointWaitDuration = 5f;

    private void Awake()
    {
        if (playerMovement == null)
            playerMovement = FindFirstObjectByType<PlayerMovement>();
    }

    public void StartCrossing()
    {
        currentState = CrossingState.CrossingToMidpoint;
        SetPlayerMovementEnabled(true);

        Debug.Log("Crossing started");
    }

    public void ReachMidpoint()
    {
        if (currentState != CrossingState.CrossingToMidpoint)
            return;

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