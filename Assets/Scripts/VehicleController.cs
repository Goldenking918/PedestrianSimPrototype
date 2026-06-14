using UnityEngine;

namespace HealthbarGames
{
    [RequireComponent(typeof(Rigidbody))]
    public class VehicleController : MonoBehaviour
    {
        [Tooltip("Waypoints the vehicle will follow in sequence (loops)")]
        public Transform[] Waypoints;

        [Tooltip("Max forward speed (m/s)")]
        public float MaxSpeed = 5f;

        [Tooltip("Distance at which a waypoint is considered reached")]
        public float ReachThreshold = 0.5f;

        private int mCurrentIndex = 0;
        private bool mStopped = false;
        private Rigidbody mRb;

        void Awake()
        {
            mRb = GetComponent<Rigidbody>();
            if (mRb == null)
                mRb = gameObject.AddComponent<Rigidbody>();
            // prevent rolling; vehicles typically rotate only on Y
            mRb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        }

        void FixedUpdate()
        {
            if (Waypoints == null || Waypoints.Length == 0)
                return;

            if (mStopped)
            {
                // gently brake to stop on XZ plane
                mRb.linearVelocity = Vector3.Lerp(mRb.linearVelocity, Vector3.zero, 5f * Time.fixedDeltaTime);
                return;
            }

            Vector3 target = Waypoints[mCurrentIndex].position;
            Vector3 dir = target - transform.position;
            dir.y = 0f;
            float dist = dir.magnitude;

            if (dist < ReachThreshold)
            {
                mCurrentIndex = (mCurrentIndex + 1) % Waypoints.Length;
                return;
            }

            dir.Normalize();

            // Simple speed control
            Vector3 desiredVel = dir * MaxSpeed;
            Vector3 newVel = Vector3.MoveTowards(new Vector3(mRb.linearVelocity.x, 0f, mRb.linearVelocity.z), desiredVel, MaxSpeed * Time.fixedDeltaTime * 2f);
            mRb.linearVelocity = new Vector3(newVel.x, mRb.linearVelocity.y, newVel.z);

            if (dir.sqrMagnitude > 0.001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(dir);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, 5f * Time.fixedDeltaTime);
            }
        }

        // Called by StopLine or other controllers
        public void SetStopped(bool stop)
        {
            mStopped = stop;
            if (stop)
                mRb.linearVelocity = Vector3.Lerp(mRb.linearVelocity, Vector3.zero, 0.5f);
        }

        public bool IsStopped()
        {
            return mStopped;
        }
    }
}
