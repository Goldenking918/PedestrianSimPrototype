using UnityEngine;

public class CrossingTrigger : MonoBehaviour
{
    public enum TriggerType
    {
        Midpoint,
        End
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
    }
}