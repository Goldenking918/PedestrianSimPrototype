using UnityEngine;

namespace HealthbarGames
{
    /// <summary>
    /// Stop line controlled by a traffic light. CarMovement vehicles find it on their route (via <see cref="SignalStops"/>)
    /// and respond to it with their own braking model (<see cref="SignalResponseModel"/>); other actors (NavMeshAgent,
    /// VehicleController) are still stopped directly while inside the trigger.
    /// The line itself is the trigger box's long (local Z) axis.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class StopLine : MonoBehaviour, ISignalStop
    {
        [Tooltip("Traffic light module that controls this stop line")]
        public TrafficLightBase TrafficLightModule;
        [Tooltip("Phases where regular-route cars are allowed to move. Leave empty to allow any phase.")]
        public string[] RegularRouteAllowedPhaseNames;
        [Tooltip("Phases where alternate-route cars are allowed to move. Leave empty to allow any phase.")]
        public string[] AlternateRouteAllowedPhaseNames;
        [Tooltip("Fallback phase list used when route-specific lists are not set. Leave empty to allow any phase.")]
        public string[] AllowedPhaseNames;

        void OnEnable() => SignalStops.Register(this);

        void OnDisable() => SignalStops.Unregister(this);

        /// <summary>The indication shown to this vehicle, applying the route-specific allowed phases.</summary>
        public SignalIndication IndicationFor(CarMovement vehicle)
        {
            if (TrafficLightModule == null)
                return SignalIndication.None;

            // With the computed signal plan running, ask it per movement (gives turning traffic its own amber).
            SignalController controller = SignalController.Active;
            if (controller != null && controller.TryGetIndication(TrafficLightModule, GetAllowedPhaseNames(vehicle), out SignalIndication indication))
                return indication;

            bool allowedPhase = IsAllowedPhase(TrafficLightModule.GetPhaseName(), GetAllowedPhaseNames(vehicle));
            switch (TrafficLightModule.GetState())
            {
                case TrafficLightBase.State.Go:
                    return allowedPhase ? SignalIndication.Green : SignalIndication.Red;
                case TrafficLightBase.State.PrepareToStop:
                    return allowedPhase ? SignalIndication.Amber : SignalIndication.Red;
                case TrafficLightBase.State.Stop:
                case TrafficLightBase.State.PrepareToGo:
                    return SignalIndication.Red;
                default:
                    return SignalIndication.None; // lights off or flashing amber: uncontrolled
            }
        }

        /// <summary>Distance along the path to where it first crosses this stop line (the box's local Z axis, in XZ).</summary>
        public bool TryGetCrossing(Vector3[] points, int count, float maxAlong, out float along)
        {
            along = 0f;
            var box = GetComponent<BoxCollider>();
            Vector3 centre = box != null ? transform.TransformPoint(box.center) : transform.position;
            Vector3 halfLine = transform.TransformVector(0f, 0f, box != null ? box.size.z * 0.5f : 0.5f);
            Vector2 a = new Vector2(centre.x - halfLine.x, centre.z - halfLine.z);
            Vector2 b = new Vector2(centre.x + halfLine.x, centre.z + halfLine.z);

            float cumulative = 0f;
            for (int i = 0; i < count - 1 && cumulative <= maxAlong; i++)
            {
                Vector2 p = new Vector2(points[i].x, points[i].z);
                Vector2 q = new Vector2(points[i + 1].x, points[i + 1].z);
                float length = Vector2.Distance(p, q);
                if (SegmentIntersection(p, q, a, b, out float t))
                {
                    along = cumulative + t * length;
                    return along <= maxAlong;
                }
                cumulative += length;
            }
            return false;
        }

        static bool SegmentIntersection(Vector2 p, Vector2 q, Vector2 a, Vector2 b, out float t)
        {
            t = 0f;
            Vector2 r = q - p, s = b - a;
            float denominator = r.x * s.y - r.y * s.x;
            if (Mathf.Abs(denominator) < 1e-6f)
                return false;
            Vector2 ap = a - p;
            t = (ap.x * s.y - ap.y * s.x) / denominator;
            float u = (ap.x * r.y - ap.y * r.x) / denominator;
            return t >= 0f && t <= 1f && u >= 0f && u <= 1f;
        }

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

            var cm = other.GetComponentInParent<CarMovement>();
            if (cm != null)
                return; // CarMovement vehicles brake for the signal themselves (see IndicationFor)

            var state = TrafficLightModule.GetState();
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
