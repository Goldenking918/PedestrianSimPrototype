using UnityEngine;

public class CrossingTrigger : MonoBehaviour
{
    public enum TriggerType
    {
        Midpoint,
        End,
        RoadEdge // place at the kerb line: entering it = the pedestrian steps off the kerb into the road
    }

    public TriggerType triggerType;

    public CrossingManager crossingManager;

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player"))
            return;

        if (triggerType == TriggerType.Midpoint)
        {
            crossingManager.ReachMidpoint();
        }
        else if (triggerType == TriggerType.End)
        {
            crossingManager.FinishCrossing();
        }
        else if (triggerType == TriggerType.RoadEdge)
        {
            crossingManager.ReachRoadEdge();
        }
    }
}