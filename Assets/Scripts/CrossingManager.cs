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

    public GameObject pauseMenu;

    public void StartCrossing()
    {
        currentState = CrossingState.CrossingToMidpoint;

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
        if (pauseMenu != null)
            pauseMenu.SetActive(true);

        // We will add traffic/scenario pausing here.
    }

    private void ResumeCrossing()
    {
        if (pauseMenu != null)
            pauseMenu.SetActive(false);

        // We will add traffic/scenario resuming here.
    }
}