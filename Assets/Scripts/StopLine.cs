using UnityEngine;

namespace HealthbarGames
{
    [RequireComponent(typeof(Collider))]
    public class StopLine : MonoBehaviour
    {
        [Tooltip("Traffic light module that controls this stop line")]
        public TrafficLightBase TrafficLightModule;
        [Tooltip("Phases where regular-route cars are allowed to move. Leave empty to allow any phase.")]
        public string[] RegularRouteAllowedPhaseNames;
        [Tooltip("Phases where alternate-route cars are allowed to move. Leave empty to allow any phase.")]
        public string[] AlternateRouteAllowedPhaseNames;
        [Tooltip("Fallback phase list used when route-specific lists are not set. Leave empty to allow any phase.")]
        public string[] AllowedPhaseNames;

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
            var cm = other.GetComponentInParent<CarMovement>();
            string[] allowedPhaseNames = GetAllowedPhaseNames(cm);
            bool shouldStop = (state != TrafficLightBase.State.Go) || !IsAllowedPhase(TrafficLightModule.GetPhaseName(), allowedPhaseNames);

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
            if (cm != null)
            {
                cm.SetStopped(shouldStop);
                return;
            }
        }

        private string[] GetAllowedPhaseNames(CarMovement carMovement)
        {
            if (carMovement != null)
            {
                if (carMovement.IsAlternateRoute)
                {
                    if (AlternateRouteAllowedPhaseNames != null && AlternateRouteAllowedPhaseNames.Length > 0)
                        return AlternateRouteAllowedPhaseNames;
                }
                else
                {
                    if (RegularRouteAllowedPhaseNames != null && RegularRouteAllowedPhaseNames.Length > 0)
                        return RegularRouteAllowedPhaseNames;
                }
            }

            return AllowedPhaseNames;
        }

        private bool IsAllowedPhase(string phaseName, string[] allowedPhaseNames)
        {
            if (allowedPhaseNames == null || allowedPhaseNames.Length == 0)
                return true;

            if (string.IsNullOrEmpty(phaseName))
                return false;

            for (int i = 0; i < allowedPhaseNames.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(allowedPhaseNames[i]) &&
                    string.Equals(allowedPhaseNames[i].Trim(), phaseName.Trim(), System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
