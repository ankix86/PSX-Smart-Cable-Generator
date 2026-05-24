using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lightweight path math for static editor cables: Catmull–Rom, chord sag, simple raycast detours, optional floor snap.
/// No physics simulation — only a few <see cref="Physics.Raycast"/> calls during generation.
/// </summary>
public static class PSCablePathUtility
{
    /// <summary>
    /// Smooth polyline through <paramref name="knots"/> using uniform Catmull–Rom (clamped ends).
    /// </summary>
    public static List<Vector3> ResampleCatmullRom(IList<Vector3> knots, int samplesPerSegment)
    {
        var output = new List<Vector3>(Mathf.Max(8, knots.Count * samplesPerSegment));
        if (knots == null || knots.Count < 2)
            return output;

        samplesPerSegment = Mathf.Max(2, samplesPerSegment);

        var p = new List<Vector3>(knots.Count + 2);
        p.Add(knots[0]);
        for (int i = 0; i < knots.Count; i++)
            p.Add(knots[i]);
        p.Add(knots[knots.Count - 1]);

        for (int i = 0; i < p.Count - 3; i++)
        {
            Vector3 c0 = p[i];
            Vector3 c1 = p[i + 1];
            Vector3 c2 = p[i + 2];
            Vector3 c3 = p[i + 3];

            for (int s = 0; s < samplesPerSegment; s++)
            {
                float t = s / (float)samplesPerSegment;
                output.Add(CatmullRom(c0, c1, c2, c3, t));
            }
        }
        output.Add(p[p.Count - 2]);
        return output;
    }

    static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;
        return 0.5f * (
            (2f * p1) +
            (-p0 + p2) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
            (-p0 + 3f * p1 - 3f * p2 + p3) * t3
        );
    }

    /// <summary>
    /// Adds gravity-style sag along the curve parameter (0..1) relative to endpoint separation.
    /// </summary>
    public static void ApplyCatenaryStyleSag(List<Vector3> path, float sagStrength, Vector3 worldDown, float asymmetry = 0f)
    {
        if (path == null || path.Count < 2 || sagStrength <= 0f)
            return;

        worldDown = worldDown.sqrMagnitude > 1e-8f ? worldDown.normalized : Vector3.down;
        Vector3 first = path[0];
        Vector3 last = path[path.Count - 1];
        float span = Vector3.Distance(first, last);
        if (span < 1e-5f)
            return;

        int n = path.Count;
        for (int i = 0; i < n; i++)
        {
            float t = n == 1 ? 0f : i / (float)(n - 1);
            float bias = t - 0.5f;
            float envelope = Mathf.Sin(Mathf.PI * t);
            float sag = sagStrength * span * envelope * (1f + asymmetry * bias);
            Vector3 pt = path[i];
            path[i] = pt + worldDown * sag;
        }
    }

    /// <summary>
    /// Inserts simple lateral detours when a segment hits geometry on <paramref name="obstacleMask"/>.
    /// Bounded by <paramref name="maxInsertions"/> for performance and artistic control.
    /// </summary>
    public static List<Vector3> InsertObstacleDetours(
        IList<Vector3> path,
        LayerMask obstacleMask,
        float clearance,
        int maxInsertions,
        float lateralPush,
        ref uint rngState)
    {
        if (path == null || path.Count < 2 || maxInsertions <= 0)
            return new List<Vector3>(path);

        var result = new List<Vector3> { path[0] };
        int insertions = 0;
        int safety = 0;
        const int maxSafety = 4096;

        for (int i = 1; i < path.Count && safety < maxSafety; safety++)
        {
            Vector3 a = result[result.Count - 1];
            Vector3 b = path[i];
            Vector3 delta = b - a;
            float dist = delta.magnitude;
            if (dist < 1e-5f)
            {
                i++;
                continue;
            }

            Vector3 dir = delta / dist;
            if (!Physics.Raycast(a, dir, out RaycastHit hit, dist, obstacleMask, QueryTriggerInteraction.Ignore))
            {
                result.Add(b);
                i++;
                continue;
            }

            if (insertions >= maxInsertions)
            {
                result.Add(b);
                i++;
                continue;
            }

            Vector3 outward = hit.normal.sqrMagnitude > 1e-6f ? hit.normal.normalized : Vector3.up;
            Vector3 hitPoint = hit.point;

            Vector3 side = Vector3.Cross(dir, Vector3.up);
            if (side.sqrMagnitude < 1e-4f)
                side = Vector3.Cross(dir, Vector3.right);
            side.Normalize();

            float sign = NextSign(ref rngState);
            Vector3 detour = hitPoint + outward * clearance + side * (lateralPush * sign);

            if ((detour - a).sqrMagnitude > 1e-8f)
            {
                result.Add(detour);
                insertions++;
                continue;
            }

            result.Add(b);
            i++;
        }

        Vector3 tail = path[path.Count - 1];
        if ((tail - result[result.Count - 1]).sqrMagnitude > 1e-8f)
            result.Add(tail);

        return result;
    }

    /// <summary>
    /// Raycasts downward and lowers points onto floor hits within range (useful for long grounded runs).
    /// </summary>
    public static void ApplyFloorSnap(List<Vector3> path, LayerMask floorMask, float rayStartLift, float maxDownDistance, float liftAboveFloor)
    {
        if (path == null || path.Count == 0 || maxDownDistance <= 0f)
            return;

        float rayLen = rayStartLift + maxDownDistance;
        for (int i = 0; i < path.Count; i++)
        {
            Vector3 o = path[i] + Vector3.up * rayStartLift;
            if (Physics.Raycast(o, Vector3.down, out RaycastHit hit, rayLen, floorMask, QueryTriggerInteraction.Ignore))
            {
                Vector3 p = path[i];
                path[i] = new Vector3(p.x, hit.point.y + liftAboveFloor, p.z);
            }
        }
    }

    static float NextSign(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return (state & 1u) == 0u ? -1f : 1f;
    }
}
