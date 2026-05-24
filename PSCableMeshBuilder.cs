using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds extremely low-poly tube or ribbon meshes along a 3D polyline. Intended for one-shot editor generation.
/// </summary>
public static class PSCableMeshBuilder
{
    public static Mesh BuildAlongPolyline(
        IList<Vector3> worldSpine,
        Matrix4x4 worldToLocal,
        bool ribbonMode,
        float tubeRadius,
        int radialSegments,
        float ribbonWidth,
        bool psxVertexWobble,
        float wobbleAmplitudeWorld,
        uint wobbleSeed,
        bool snapVertices,
        float snapGrid)
    {
        if (worldSpine == null || worldSpine.Count < 2)
            return null;

        radialSegments = Mathf.Clamp(radialSegments, 3, 32);

        var locals = new List<Vector3>(worldSpine.Count);
        for (int i = 0; i < worldSpine.Count; i++)
            locals.Add(worldToLocal.MultiplyPoint3x4(worldSpine[i]));

        if (ribbonMode)
            return BuildRibbon(locals, ribbonWidth, psxVertexWobble, wobbleAmplitudeWorld, wobbleSeed, snapVertices, snapGrid);

        return BuildTube(locals, tubeRadius, radialSegments, psxVertexWobble, wobbleAmplitudeWorld, wobbleSeed, snapVertices, snapGrid);
    }

    static Mesh BuildTube(
        List<Vector3> spine,
        float radius,
        int radialSegments,
        bool wobble,
        float wobbleAmp,
        uint seed,
        bool snap,
        float grid)
    {
        int rings = spine.Count;
        radius = Mathf.Max(0.0005f, radius);

        var frames = ComputeParallelFrames(spine);
        var verts = new List<Vector3>(rings * radialSegments);
        var norms = new List<Vector3>(verts.Capacity);
        var uvs = new List<Vector2>(verts.Capacity);
        var tris = new List<int>((rings - 1) * radialSegments * 6);

        float cumLen = 0f;
        for (int r = 0; r < rings; r++)
        {
            if (r > 0)
                cumLen += Vector3.Distance(spine[r - 1], spine[r]);

            Vector3 right = frames[r].right;
            Vector3 up = frames[r].up;

            for (int s = 0; s < radialSegments; s++)
            {
                float ang = (s / (float)radialSegments) * Mathf.PI * 2f;
                float c = Mathf.Cos(ang);
                float sn = Mathf.Sin(ang);
                Vector3 radial = (right * c + up * sn).normalized;
                Vector3 ringPos = spine[r] + radial * radius;

                if (wobble)
                    ringPos += PsxWobbleOffset(ringPos, wobbleAmp, ref seed);
                if (snap && grid > 1e-6f)
                    ringPos = SnapGrid(ringPos, grid);

                verts.Add(ringPos);
                norms.Add(radial);
                uvs.Add(new Vector2(s / (float)radialSegments, cumLen));
            }
        }

        for (int r = 0; r < rings - 1; r++)
        {
            int baseA = r * radialSegments;
            int baseB = (r + 1) * radialSegments;
            for (int s = 0; s < radialSegments; s++)
            {
                int sNext = (s + 1) % radialSegments;
                int i0 = baseA + s;
                int i1 = baseA + sNext;
                int i2 = baseB + s;
                int i3 = baseB + sNext;
                tris.Add(i0);
                tris.Add(i2);
                tris.Add(i1);
                tris.Add(i1);
                tris.Add(i2);
                tris.Add(i3);
            }
        }

        var mesh = new Mesh { name = "PSXCableTube" };
        mesh.indexFormat = verts.Count > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    static Mesh BuildRibbon(
        List<Vector3> spine,
        float width,
        bool wobble,
        float wobbleAmp,
        uint seed,
        bool snap,
        float grid)
    {
        width = Mathf.Max(0.0005f, width);
        var frames = ComputeParallelFrames(spine);
        var verts = new List<Vector3>(spine.Count * 2);
        var norms = new List<Vector3>(verts.Capacity);
        var uvs = new List<Vector2>(verts.Capacity);
        var tris = new List<int>((spine.Count - 1) * 6);

        float cumLen = 0f;
        for (int i = 0; i < spine.Count; i++)
        {
            if (i > 0)
                cumLen += Vector3.Distance(spine[i - 1], spine[i]);

            Vector3 right = frames[i].right;
            Vector3 p = spine[i];
            Vector3 a = p - right * (width * 0.5f);
            Vector3 b = p + right * (width * 0.5f);

            if (wobble)
            {
                a += PsxWobbleOffset(a, wobbleAmp, ref seed);
                b += PsxWobbleOffset(b, wobbleAmp, ref seed);
            }
            if (snap && grid > 1e-6f)
            {
                a = SnapGrid(a, grid);
                b = SnapGrid(b, grid);
            }

            Vector3 tan = frames[i].forward;
            Vector3 n = Vector3.Cross(right, tan);
            if (n.sqrMagnitude < 1e-6f)
                n = Vector3.up;
            n.Normalize();

            verts.Add(a);
            norms.Add(n);
            uvs.Add(new Vector2(0f, cumLen));

            verts.Add(b);
            norms.Add(n);
            uvs.Add(new Vector2(1f, cumLen));
        }

        for (int i = 0; i < spine.Count - 1; i++)
        {
            int i0 = i * 2;
            int i1 = i * 2 + 1;
            int i2 = i0 + 2;
            int i3 = i1 + 2;
            tris.Add(i0);
            tris.Add(i2);
            tris.Add(i1);
            tris.Add(i1);
            tris.Add(i2);
            tris.Add(i3);
        }

        var mesh = new Mesh { name = "PSXCableRibbon" };
        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    static List<(Vector3 forward, Vector3 right, Vector3 up)> ComputeParallelFrames(List<Vector3> spine)
    {
        int n = spine.Count;
        var tangents = new List<Vector3>(n);
        for (int i = 0; i < n; i++)
        {
            Vector3 t;
            if (i == 0)
                t = spine[1] - spine[0];
            else if (i == n - 1)
                t = spine[n - 1] - spine[n - 2];
            else
                t = spine[i + 1] - spine[i - 1];

            if (t.sqrMagnitude < 1e-8f)
                t = Vector3.forward;
            tangents.Add(t.normalized);
        }

        var frames = new List<(Vector3, Vector3, Vector3)>(n);
        Vector3 carry = Vector3.Cross(tangents[0], Vector3.up).normalized;
        if (carry.sqrMagnitude < 1e-4f)
            carry = Vector3.Cross(tangents[0], Vector3.right).normalized;

        for (int i = 0; i < n; i++)
        {
            Vector3 f = tangents[i];
            Vector3 right = Vector3.Cross(Vector3.up, f).normalized;
            if (right.sqrMagnitude < 1e-4f)
                right = carry;
            carry = right;
            Vector3 up = Vector3.Cross(f, right).normalized;
            frames.Add((f, right, up));
        }

        return frames;
    }

    static Vector3 PsxWobbleOffset(Vector3 localPoint, float amplitude, ref uint seed)
    {
        if (amplitude <= 0f)
            return Vector3.zero;

        uint h = Hash(localPoint, seed);
        float nx = (h & 1023u) / 1023f * 2f - 1f;
        uint h2 = Hash(localPoint * 1.031f, seed ^ 0xA5A5u);
        float ny = (h2 & 1023u) / 1023f * 2f - 1f;
        uint h3 = Hash(localPoint * 1.077f, seed ^ 0x5C5Cu);
        float nz = (h3 & 1023u) / 1023f * 2f - 1f;
        return new Vector3(nx, ny, nz) * amplitude;
    }

    static uint Hash(Vector3 p, uint salt)
    {
        uint x = (uint)Mathf.Abs(p.x * 73856093f) ^ salt;
        uint y = (uint)Mathf.Abs(p.y * 19349663f) ^ (salt << 1);
        uint z = (uint)Mathf.Abs(p.z * 83492791f) ^ (salt << 2);
        uint h = x ^ y ^ z;
        h ^= h << 13;
        h ^= h >> 17;
        h ^= h << 5;
        return h;
    }

    static Vector3 SnapGrid(Vector3 v, float grid)
    {
        return new Vector3(
            Mathf.Round(v.x / grid) * grid,
            Mathf.Round(v.y / grid) * grid,
            Mathf.Round(v.z / grid) * grid);
    }
}
