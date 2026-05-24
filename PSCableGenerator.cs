using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Scene component that stores cable endpoints, styling, and editor-generated mesh output.
/// Mesh generation is intended to run in the Unity Editor only (see <c>PSCableEditor</c>).
/// At runtime this behaves as a plain static mesh filter/renderer pair with near-zero CPU cost.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class PSCableGenerator : MonoBehaviour
{
    [Header("Path (sequence)")]
    [Tooltip("Ordered anchors: the cable visits each transform in list order (1 → 2 → 3 …). When 2+ entries are set, this replaces Start/End + Manual knots.")]
    public List<Transform> PathAnchors = new List<Transform>();

    [Header("Anchors (legacy when Path Anchors has fewer than 2 valid entries)")]
    [Tooltip("World-space start of the cable path.")]
    public Transform CableStart;

    [Tooltip("World-space end of the cable path.")]
    public Transform CableEnd;

    [Header("Preset")]
    public PSCablePreset Preset = PSCablePreset.Custom;

    [Header("Shape")]
    [Tooltip("When true, inserts an automatic slack knot between each consecutive pair of anchor positions (each span).")]
    public bool AddAutoMidKnot = true;

    [Tooltip("0 = more slack in automated mid knots before sag; 1 = hugs the chord (straighter), per span.")]
    [Range(0f, 1f)]
    public float CurveTension = 0.5f;

    [Tooltip("Additional world-space knots (legacy mode only: used with Cable Start/End when Path Anchors is empty or has one entry).")]
    public List<Vector3> ManualWorldKnots = new List<Vector3>();

    [Tooltip("Catmull–Rom subdivisions per spline span.")]
    [Range(2, 64)]
    public int PathResolution = 16;

    [Tooltip("Dimensionless sag multiplier relative to anchor separation.")]
    [Range(0f, 1.5f)]
    public float SagStrength = 0.45f;

    [Tooltip("Biases sag so one side hangs slightly lower (cheap imperfection).")]
    [Range(-0.75f, 0.75f)]
    public float SagAsymmetry = 0.1f;

    [Header("Mesh")]
    public bool RibbonMode;

    [Tooltip("Tube: outer diameter in meters. Ribbon: width in meters.")]
    public float CableThickness = 0.04f;

    [Tooltip("Kept in sync for prefabs/tools that still read radius; tube effective radius = CableThickness × 0.5.")]
    [HideInInspector]
    public float CableRadius = 0.02f;

    [Tooltip("Kept in sync with CableThickness when using ribbon.")]
    [HideInInspector]
    public float RibbonWidth = 0.04f;

    [Tooltip("Sides for the tube silhouette (keep low for PSX).")]
    [Range(3, 16)]
    public int RadialSegments = 6;

    [Header("PSX / Imperfections")]
    public bool PsxVertexWobble;

    [Tooltip("World-space jitter amplitude applied in local space during mesh build.")]
    public float WobbleAmplitude = 0.008f;

    [Tooltip("Quantize vertices on a local grid (0 disables).")]
    public float VertexSnapGrid;

    public bool EnableThicknessNoise;

    [Tooltip("Scales wobble contribution from cable thickness.")]
    public float ThicknessJitterPSX = 0.08f;

    [Header("Collision (optional)")]
    public bool AvoidObstacles;

    public LayerMask ObstacleLayers = ~0;

    [Range(0, 16)]
    public int MaxDetourPoints = 4;

    public float ObstacleClearance = 0.05f;

    public float DetourLateralPush = 0.12f;

    [Header("Floor")]
    public bool SnapToFloor;

    public LayerMask FloorLayers = ~0;

    [Tooltip("Raise ray origin above sample points before casting down.")]
    public float FloorRayStartLift = 0.25f;

    [Tooltip("Maximum ray length below the lifted origin.")]
    public float FloorSnapDistance = 3f;

    [Tooltip("Offset above detected floor hit.")]
    public float FloorLift = 0.02f;

    [Header("Random Shape")]
    public int RandomSeed;

    [Range(0f, 1f)]
    public float RandomizeStrength = 0.35f;

    [Header("Asset Output")]
    [Tooltip("Folder under Assets/ (must exist or will be created when possible).")]
    public string SaveFolderRelative = "Assets/_Generated/PSXCables";

    [Tooltip("File name without extension; used as the prefix when saving (see Unique Name Per Save).")]
    public string MeshAssetName = "CableMesh";

    [Tooltip("When true, each Save creates a new asset file: BaseName_xxxxxxxx.asset (never overwrites an existing file).")]
    public bool UniqueNamePerSave = true;

    [Header("Preview")]
    [Tooltip("Regenerates in edit mode when values change (handled by the custom inspector).")]
    public bool AutoPreviewInEditor = true;

    MeshFilter _filter;
    MeshRenderer _renderer;

    public MeshFilter Filter => _filter != null ? _filter : (_filter = GetComponent<MeshFilter>());
    public MeshRenderer Renderer => _renderer != null ? _renderer : (_renderer = GetComponent<MeshRenderer>());

    void OnEnable()
    {
        if (_filter == null) _filter = GetComponent<MeshFilter>();
        if (_renderer == null) _renderer = GetComponent<MeshRenderer>();
        MigrateThicknessIfNeeded();
        SyncLegacyThicknessFields();
    }

    void OnValidate()
    {
        MigrateThicknessIfNeeded();
        SyncLegacyThicknessFields();
    }

    /// <summary>
    /// Old scenes may deserialize CableThickness as 0 before the field existed; infer from radius.
    /// </summary>
    void MigrateThicknessIfNeeded()
    {
        if (CableThickness > 1e-6f)
            return;
        CableThickness = Mathf.Max(0.002f, CableRadius * 2f);
        if (CableThickness <= 1e-6f)
            CableThickness = 0.04f;
    }

    void SyncLegacyThicknessFields()
    {
        CableThickness = Mathf.Max(0.001f, CableThickness);
        CableRadius = CableThickness * 0.5f;
        RibbonWidth = CableThickness;
    }

    /// <summary>
    /// True when <see cref="PathAnchors"/> defines the path (two or more non-null transforms).
    /// </summary>
    public bool UsesAnchorChain(out List<Vector3> chainWorldPositions)
    {
        chainWorldPositions = new List<Vector3>();
        if (PathAnchors == null)
            return false;
        for (int i = 0; i < PathAnchors.Count; i++)
        {
            if (PathAnchors[i] != null)
                chainWorldPositions.Add(PathAnchors[i].position);
        }
        return chainWorldPositions.Count >= 2;
    }

    /// <summary>
    /// Path is valid if anchor chain, or legacy start + end, is usable.
    /// </summary>
    public bool HasValidPathSources()
    {
        if (UsesAnchorChain(out _))
            return true;
        return CableStart != null && CableEnd != null;
    }

    public float GetTubeRadiusForMesh() => RibbonMode ? CableRadius : CableThickness * 0.5f;

    public float GetRibbonWidthForMesh() => RibbonMode ? CableThickness : RibbonWidth;

    /// <summary>
    /// Approximate total length along anchor polyline (for noise scaling).
    /// </summary>
    public float GetReferenceSpanWorld()
    {
        if (UsesAnchorChain(out var chain))
            return PolylineLength(chain);

        if (CableStart != null && CableEnd != null)
            return Vector3.Distance(CableStart.position, CableEnd.position);

        return 0f;
    }

    static float PolylineLength(List<Vector3> pts)
    {
        if (pts == null || pts.Count < 2)
            return 0f;
        float s = 0f;
        for (int i = 1; i < pts.Count; i++)
            s += Vector3.Distance(pts[i - 1], pts[i]);
        return s;
    }

    /// <summary>
    /// Rebuilds the path in world space (smoothing, sag, optional obstacles &amp; floor snap). Does not touch mesh.
    /// </summary>
    public List<Vector3> BuildWorldPath()
    {
        var path = new List<Vector3>();
        if (!HasValidPathSources())
            return path;

        uint rng = RandomSeed != 0 ? (uint)RandomSeed : (uint)UnityEngine.Random.Range(1, int.MaxValue);
        Vector3 down = Vector3.down;

        List<Vector3> knots;
        if (UsesAnchorChain(out var chain))
            knots = BuildKnotsFromChain(chain, ref rng);
        else
            knots = BuildKnotsLegacy(ref rng);

        if (knots.Count < 2)
            return path;

        path = PSCablePathUtility.ResampleCatmullRom(knots, Mathf.Max(2, PathResolution));
        float asym = SagAsymmetry + RandomSymmetry(ref rng) * 0.15f * RandomizeStrength;
        PSCablePathUtility.ApplyCatenaryStyleSag(path, SagStrength * (1f + RandomSag(ref rng) * RandomizeStrength), down, asym);

        if (AvoidObstacles && MaxDetourPoints > 0)
            path = PSCablePathUtility.InsertObstacleDetours(path, ObstacleLayers, ObstacleClearance, MaxDetourPoints, DetourLateralPush, ref rng);

        if (SnapToFloor)
            PSCablePathUtility.ApplyFloorSnap(path, FloorLayers, FloorRayStartLift, FloorSnapDistance, FloorLift);

        return path;
    }

    /// <summary>
    /// Per-span auto mid between consecutive anchor positions, then Catmull–Rom through the result.
    /// </summary>
    List<Vector3> BuildKnotsFromChain(List<Vector3> chain, ref uint rng)
    {
        var knots = new List<Vector3> { chain[0] };

        for (int i = 0; i < chain.Count - 1; i++)
        {
            Vector3 p0 = chain[i];
            Vector3 p1 = chain[i + 1];

            if (AddAutoMidKnot)
                knots.Add(ComputeAutoMidBetween(p0, p1));

            knots.Add(p1);
        }

        return knots;
    }

    /// <summary>
    /// Original behaviour: start, one optional global mid between endpoints, manual knots, end.
    /// </summary>
    List<Vector3> BuildKnotsLegacy(ref uint rng)
    {
        Vector3 a = CableStart.position;
        Vector3 b = CableEnd.position;
        float span = Vector3.Distance(a, b);
        Vector3 chordMid = (a + b) * 0.5f;
        Vector3 deepMid = chordMid + Vector3.down * (span * 0.14f);
        Vector3 autoMid = Vector3.Lerp(deepMid, chordMid, Mathf.Clamp01(CurveTension));

        var knots = new List<Vector3> { a };
        if (AddAutoMidKnot)
            knots.Add(autoMid);
        for (int i = 0; i < ManualWorldKnots.Count; i++)
            knots.Add(ManualWorldKnots[i] + RandomKnotNoise(ref rng));
        knots.Add(b);
        return knots;
    }

    Vector3 ComputeAutoMidBetween(Vector3 p0, Vector3 p1)
    {
        float span = Vector3.Distance(p0, p1);
        Vector3 chordMid = (p0 + p1) * 0.5f;
        Vector3 deepMid = chordMid + Vector3.down * (span * 0.14f);
        return Vector3.Lerp(deepMid, chordMid, Mathf.Clamp01(CurveTension));
    }

    Vector3 RandomKnotNoise(ref uint rng)
    {
        if (RandomizeStrength <= 0f)
            return Vector3.zero;
        float span = Mathf.Max(0.01f, GetReferenceSpanWorld());
        float s = RandomizeStrength * 0.08f * span;
        return new Vector3(NextSigned(ref rng), NextSigned(ref rng), NextSigned(ref rng)) * s;
    }

    float RandomSag(ref uint rng)
    {
        if (RandomizeStrength <= 0f)
            return 0f;
        return (Next01(ref rng) * 2f - 1f) * 0.12f;
    }

    float RandomSymmetry(ref uint rng)
    {
        if (RandomizeStrength <= 0f)
            return 0f;
        return (Next01(ref rng) * 2f - 1f);
    }

    static float Next01(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return (state & 0xFFFFu) / 65535f;
    }

    static float NextSigned(ref uint state) => Next01(ref state) * 2f - 1f;

    /// <summary>
    /// Builds or rebuilds the Unity mesh for the current path and assigns it to the attached <see cref="MeshFilter"/>.
    /// </summary>
    public Mesh BuildMeshAssetData(List<Vector3> worldSpine)
    {
        if (worldSpine == null || worldSpine.Count < 2)
            return null;

        float tubeR = GetTubeRadiusForMesh();
        float ribbonW = GetRibbonWidthForMesh();

        float wobble = WobbleAmplitude;
        if (EnableThicknessNoise)
            wobble += Mathf.Max(tubeR, ribbonW * 0.5f) * ThicknessJitterPSX;

        uint wobbleSeed = RandomSeed != 0 ? (uint)RandomSeed : 0xC0FFEEu;

        return PSCableMeshBuilder.BuildAlongPolyline(
            worldSpine,
            transform.worldToLocalMatrix,
            RibbonMode,
            tubeR,
            RadialSegments,
            ribbonW,
            PsxVertexWobble || EnableThicknessNoise,
            wobble,
            wobbleSeed,
            VertexSnapGrid > 0f,
            VertexSnapGrid);
    }
}
