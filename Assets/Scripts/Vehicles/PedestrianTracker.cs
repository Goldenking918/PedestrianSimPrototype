using UnityEngine;

/// <summary>
/// Shared, once-per-frame estimate of the participant's position and walking velocity for all vehicles.
/// It finds the existing <see cref="PlayerMovement"/> CharacterController automatically, so no scene changes are needed.
/// Velocity is a finite difference, exponentially smoothed to filter out VR head-tracking jitter.
/// </summary>
public static class PedestrianTracker
{
    /// <summary>Time constant of the velocity smoothing filter (s).</summary>
    public const float VelocitySmoothingTime = 0.25f;

    const float SearchInterval = 1f;
    const float DefaultRadius = 0.3f;

    static Transform sTarget;
    static CharacterController sController;
    static float sRadiusOverride = -1f;
    static bool sHasSample;
    static Vector3 sPosition;
    static Vector3 sVelocity;
    static float sLastSampleTime;
    static int sLastSampleFrame = -1;
    static float sNextSearchTime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        sTarget = null;
        sController = null;
        sRadiusOverride = -1f;
        sHasSample = false;
        sVelocity = Vector3.zero;
        sLastSampleFrame = -1;
        sNextSearchTime = 0f;
    }

    /// <summary>Explicitly sets the tracked pedestrian (for tests or scenes without a PlayerMovement). Pass null to clear.</summary>
    public static void SetTarget(Transform target, float radius)
    {
        sTarget = target;
        sController = target != null ? target.GetComponent<CharacterController>() : null;
        sRadiusOverride = radius;
        sHasSample = false;
        sVelocity = Vector3.zero;
        sLastSampleFrame = -1;
    }

    /// <summary>
    /// Forgets the previous sample, so a teleport (e.g. returning the participant to the start) is not seen as a burst of
    /// walking speed by the vehicles or the measurements.
    /// </summary>
    public static void ResetMotion()
    {
        sHasSample = false;
        sVelocity = Vector3.zero;
        sLastSampleFrame = -1;
    }

    /// <summary>Samples once per rendered frame; safe to call from every vehicle.</summary>
    public static void EnsureSampled()
    {
        if (sLastSampleFrame == Time.frameCount)
            return;
        Sample(Time.time);
    }

    /// <summary>Takes a sample at an explicit time. Used directly by tests.</summary>
    public static void Sample(float now)
    {
        sLastSampleFrame = Time.frameCount;

        if (sTarget == null && !TryFindTarget(now))
        {
            sHasSample = false;
            return;
        }

        Vector3 position = CurrentPosition();
        if (sHasSample)
        {
            float dt = now - sLastSampleTime;
            if (dt > 1e-4f)
            {
                Vector3 raw = (position - sPosition) / dt;
                raw.y = 0f;
                float blend = 1f - Mathf.Exp(-dt / VelocitySmoothingTime);
                sVelocity = Vector3.Lerp(sVelocity, raw, blend);
            }
        }
        else
        {
            sVelocity = Vector3.zero;
        }

        sPosition = position;
        sLastSampleTime = now;
        sHasSample = true;
    }

    public static bool TryGet(out Vector3 position, out Vector3 velocity, out float radius)
    {
        position = sPosition;
        velocity = sVelocity;
        radius = CurrentRadius();
        return sHasSample && sTarget != null;
    }

    static bool TryFindTarget(float now)
    {
        if (now < sNextSearchTime)
            return false;
        sNextSearchTime = now + SearchInterval;

        var player = Object.FindFirstObjectByType<PlayerMovement>();
        if (player == null)
            return false;

        sController = player.controller != null ? player.controller : player.GetComponent<CharacterController>();
        sTarget = sController != null ? sController.transform : player.transform;
        sHasSample = false;
        return true;
    }

    static Vector3 CurrentPosition()
    {
        return sController != null ? sController.transform.TransformPoint(sController.center) : sTarget.position;
    }

    static float CurrentRadius()
    {
        if (sRadiusOverride > 0f)
            return sRadiusOverride;
        if (sController != null)
        {
            Vector3 scale = sController.transform.lossyScale;
            return sController.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        }
        return DefaultRadius;
    }
}
