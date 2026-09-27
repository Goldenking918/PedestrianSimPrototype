using UnityEngine;

/// <summary>
/// Projects world positions onto a vehicle's remaining waypoint polyline (in the XZ plane), giving
/// distance along the path and signed lateral offset. This lets car-following and pedestrian
/// reasoning follow the actual route, including turns, rather than a straight forward ray.
/// </summary>
public static class VehiclePath
{
    public struct Projection
    {
        /// <summary>Arc length from the first path point to the projected point (m). Negative = behind the first point.</summary>
        public float along;
        /// <summary>Signed distance from the path (m); positive = to the right of the direction of travel.</summary>
        public float lateral;
        /// <summary>Unit path direction (XZ) at the projected point.</summary>
        public Vector3 tangent;
        /// <summary>Unit right-hand normal (XZ) at the projected point.</summary>
        public Vector3 right;
    }

    /// <summary>
    /// Projects <paramref name="world"/> onto the polyline points[0..count). The first segment extends backwards and the last
    /// segment extends forwards without limit, so points behind the vehicle or beyond the final waypoint still project.
    /// Segments starting beyond <paramref name="maxAlong"/> are ignored.
    /// </summary>
    public static bool TryProject(Vector3[] points, int count, Vector3 world, float maxAlong, out Projection projection)
    {
        projection = default;
        if (points == null || count < 2)
            return false;

        float bestDistanceSq = float.PositiveInfinity;
        float cumulative = 0f;
        int lastSegment = count - 2;
        bool found = false;

        for (int i = 0; i <= lastSegment; i++)
        {
            Vector3 a = Flatten(points[i]);
            Vector3 segment = Flatten(points[i + 1]) - a;
            float length = segment.magnitude;
            if (length < 1e-4f)
                continue;

            Vector3 dir = segment / length;
            Vector3 toPoint = Flatten(world) - a;
            float t = Vector3.Dot(toPoint, dir);
            float tClamped = t;
            if (i > 0) tClamped = Mathf.Max(tClamped, 0f);
            if (i < lastSegment) tClamped = Mathf.Min(tClamped, length);

            Vector3 offset = toPoint - dir * tClamped;
            float distanceSq = offset.sqrMagnitude;
            if (distanceSq < bestDistanceSq - 1e-6f)
            {
                bestDistanceSq = distanceSq;
                Vector3 right = new Vector3(dir.z, 0f, -dir.x);
                projection.along = cumulative + tClamped;
                // True distance from the path. Using only the perpendicular component would report 0 for a point
                // straight past the end of a segment at a corner.
                projection.lateral = Mathf.Sqrt(distanceSq) * (Vector3.Dot(offset, right) >= 0f ? 1f : -1f);
                projection.tangent = dir;
                projection.right = right;
                found = true;
            }

            cumulative += length;
            if (cumulative > maxAlong)
                break;
        }

        return found;
    }

    static Vector3 Flatten(Vector3 v) => new Vector3(v.x, 0f, v.z);
}
