using UnityEngine;

namespace HealthbarGames
{
    [RequireComponent(typeof(Collider))]
    public class StopLine : MonoBehaviour
    {
        [Tooltip("Traffic light module that controls this stop line")]
        public TrafficLightBase TrafficLightModule;

        void Reset()
        {
            // ensure collider is a trigger by default
            var col = GetComponent<Collider>();
            if (col != null)
                col.isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            Debug.Log("StopLine: TriggerEnter by " + other.name, this);
            UpdateActor(other);
        }

        private void OnTriggerStay(Collider other)
        {
            UpdateActor(other);
        }

        private void OnTriggerExit(Collider other)
        {
            Debug.Log("StopLine: TriggerExit by " + other.name, this);
            var nav = other.GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (nav != null)
                nav.isStopped = false;

            var vc = other.GetComponentInParent<VehicleController>();
            if (vc != null)
                vc.SetStopped(false);

            var cm = other.GetComponentInParent<CarMovement>();
            if (cm != null)
                cm.SetStopped(false);
        }

        private void UpdateActor(Collider other)
        {
            if (TrafficLightModule == null)
                return;

            var state = TrafficLightModule.GetState();
            bool shouldStop = (state != TrafficLightBase.State.Go);

            var nav = other.GetComponentInParent<UnityEngine.AI.NavMeshAgent>();
            if (nav != null)
            {
                nav.isStopped = shouldStop;
                return;
            }

            var vc = other.GetComponentInParent<VehicleController>();
            if (vc != null)
            {
                vc.SetStopped(shouldStop);
                return;
            }

            // support existing CarMovement script
            var cm = other.GetComponentInParent<CarMovement>();
            if (cm != null)
            {
                cm.SetStopped(shouldStop);
                return;
            }
        }
    }
}
