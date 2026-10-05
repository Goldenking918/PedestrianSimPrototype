using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>
/// Dresses the street to look like the real Symonds Street in the reference photos and video taken on site (Aug 2026):
/// textured asphalt; New Zealand road markings as painted there (broken yellow no-stopping lines along the kerbside
/// strips, solid white lane lines, a green BUS LANE patch in each kerbside lane); dark basalt kerbs with a concrete
/// channel and a yellow edge strip; concrete-slab footpaths; a glass bus shelter with poster panels on the far footpath;
/// and a backdrop of high-rise towers like the apartment and university buildings that line the street.
///
/// Visual only. Everything is generated from the numbers below when the scene loads (and in the editor, so it can be
/// seen while editing), and is never saved into the scene. None of it has a collider, and the existing street keeps its
/// colliders, so where the participant can walk, the kerb step, the crossing triggers and the traffic are unchanged.
/// The old road and footpath tiles under the new surfaces are only hidden; disabling this component restores them.
///
/// The street runs along world X. The kerb lines are measured from the existing street's colliders (so the painted kerb
/// sits exactly on the physical kerb step), with the values below as a fallback.
/// </summary>
[ExecuteAlways]
public class SymondsStreetDressing : MonoBehaviour
{
    [Header("Existing scene")]
    [Tooltip("Root of the existing street tiles (\"Streets\"). Their colliders are used to find the kerbs, and their renderers under the new surfaces are hidden.")]
    public Transform existingStreetRoot;
    [Tooltip("The RoadEdge crossing trigger. Only used to warn if the visible kerb and the 'stepped off kerb' line disagree.")]
    public Transform roadEdgeTrigger;
    [Tooltip("A plain URP Lit material; every generated material is a copy of it (keeps the shader in builds).")]
    public Material litTemplate;

    [Header("Street layout (world space)")]
    [Tooltip("Stretches of street to dress, as (start X, end X). The gap between them is the intersection, which keeps its original look.")]
    public Vector2[] segments = { new Vector2(-132.1f, 53.95f), new Vector2(78.1f, 156.6f) };
    [Tooltip("Z of the middle of the road.")]
    public float roadCentreZ = 6.42f;
    [Tooltip("X at which the kerbs are measured (the crossing).")]
    public float probeX = 34.7f;
    public bool measureKerbs = true;
    [Tooltip("Fallback kerb lines (Z), used if they cannot be measured.")]
    public float southKerbZ = -1.7f, northKerbZ = 14.5f;
    [Tooltip("Fallback surface heights (Y).")]
    public float roadY = 0f, footpathY = 0.1f;
    [Tooltip("Width of the new footpath surface behind each kerb (m).")]
    [Min(1f)] public float footpathWidth = 8f;

    [Header("Road markings (Z of each line's centre)")]
    [Tooltip("Broken yellow no-stopping lines at the outer edge of the kerbside lanes, as in the photos.")]
    public float[] noStoppingLineZ = { -0.25f, 13.1f };
    [Tooltip("Solid white lines between the two lanes in each direction.")]
    public float[] laneLineZ = { 3.265f, 9.89f };
    [Tooltip("Solid white centre lines (two, a short distance apart).")]
    public float[] centreLineZ = { 6.58f, 6.88f };
    [Min(0.05f)] public float lineWidth = 0.1f;
    [Min(0.1f)] public float noStoppingDash = 1.5f, noStoppingGap = 3f;
    [Tooltip("Green BUS LANE patches in the kerbside lanes, kept away from the crossing area.")]
    public BusLanePatch[] busLanes =
    {
        new BusLanePatch { laneCentreZ = 1.5f, startX = -28f, endX = -13f, travelTowardsPositiveX = false },
        new BusLanePatch { laneCentreZ = 11.5f, startX = -12f, endX = 3f, travelTowardsPositiveX = true },
    };

    [Header("Street furniture")]
    public bool busShelter = true;
    [Tooltip("X range of the bus shelter on the far (north) footpath. Keep it outside the crossing area.")]
    public Vector2 shelterX = new Vector2(-11f, 3f);

    [Header("Skyline")]
    public bool towers = true;
    public Tower[] towerList =
    {
        new Tower { centre = new Vector2(-20f, 64f), footprint = new Vector2(22f, 18f), height = 55f, style = StreetTextures.FacadeStyle.WhiteApartments },
        new Tower { centre = new Vector2(12f, 72f), footprint = new Vector2(20f, 20f), height = 70f, style = StreetTextures.FacadeStyle.WhiteApartments },
        new Tower { centre = new Vector2(40f, 66f), footprint = new Vector2(26f, 20f), height = 36f, style = StreetTextures.FacadeStyle.RustFins },
        new Tower { centre = new Vector2(72f, 76f), footprint = new Vector2(18f, 18f), height = 48f, style = StreetTextures.FacadeStyle.DarkGlass },
        new Tower { centre = new Vector2(-6f, -44f), footprint = new Vector2(16f, 16f), height = 60f, style = StreetTextures.FacadeStyle.PaintedConcrete },
        new Tower { centre = new Vector2(30f, -50f), footprint = new Vector2(22f, 18f), height = 44f, style = StreetTextures.FacadeStyle.DarkGlass },
        new Tower { centre = new Vector2(62f, -42f), footprint = new Vector2(16f, 16f), height = 52f, style = StreetTextures.FacadeStyle.WhiteApartments },
    };

    [Serializable]
    public struct BusLanePatch
    {
        public float laneCentreZ, startX, endX;
        public bool travelTowardsPositiveX;
    }

    [Serializable]
    public struct Tower
    {
        public Vector2 centre;    // X, Z
        public Vector2 footprint; // along X, along Z
        public float height;
        public StreetTextures.FacadeStyle style;
    }

    // ---- Constants: sizes and colours taken from the reference photos ------------------------------------------------

    const float AsphaltTile = 4f;       // metres covered by one repeat of the asphalt texture
    const float PaverTile = 2.4f;       // ... of the paver texture (4 x 0.6 m slabs)
    const float FacadeBay = 3.6f, FacadeFloor = 3.2f;
    const float ChannelWidth = 0.35f;   // concrete channel in front of the kerb
    const float KerbTopWidth = 0.25f;
    const float YellowStripWidth = 0.12f;
    const float LaneWidth = 3.3f;
    // Small lifts above the old surfaces, so the new ones are never hidden by them where an old tile is only partly
    // covered. The midpoint cue (MidpointApproachMarker) sits 0.015 m above the road, above all of these.
    const float SurfaceLift = 0.003f, ChannelLift = 0.004f, PatchLift = 0.006f, PaintLift = 0.008f;

    static readonly Color AsphaltColour = Color.white;
    static readonly Color PaverColour = new Color(0.97f, 0.97f, 0.96f);
    static readonly Color KerbColour = new Color(0.30f, 0.30f, 0.31f);      // basalt
    static readonly Color ChannelColour = new Color(0.55f, 0.55f, 0.53f);   // concrete
    static readonly Color WhitePaint = new Color(0.90f, 0.90f, 0.88f);
    static readonly Color YellowPaint = new Color(0.95f, 0.74f, 0.12f);
    static readonly Color BusLaneGreen = new Color(0.20f, 0.52f, 0.33f);
    static readonly Color ShelterFrame = new Color(0.78f, 0.80f, 0.80f);
    static readonly Color ShelterGlass = new Color(0.62f, 0.78f, 0.74f);   // pale green-tinted canopy

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
    static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");

    GameObject generated;
    readonly List<Renderer> hiddenTiles = new List<Renderer>();
    readonly List<Object> ownedAssets = new List<Object>();

    struct Layout
    {
        public float roadY, southKerb, northKerb, southFootY, northFootY;
    }

    // ---- Lifecycle ---------------------------------------------------------------------------------------------------

    bool started;

    // In play mode, build in Start: by then every street tile is loaded, so the kerbs can be measured.
    void Start()
    {
        if (!Application.isPlaying)
            return; // the editor preview is built from OnEnable
        started = true;
        Build();
    }

    void OnEnable()
    {
        if (Application.isPlaying)
        {
            if (started)
                Build(); // re-enabled after Start
            return;
        }
#if UNITY_EDITOR
        QueueEditorBuild();
#endif
    }

    void OnDisable() => Clear();

#if UNITY_EDITOR
    // Rebuild after an Inspector change. Deferred: objects cannot be created during OnValidate, and on scene load the
    // rest of the scene may not be ready yet.
    void OnValidate() => QueueEditorBuild();

    void QueueEditorBuild()
    {
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this != null && isActiveAndEnabled && (!Application.isPlaying || started))
                Build();
        };
    }
#endif

    public void Build()
    {
        Clear();
        if (litTemplate == null)
        {
            Debug.LogWarning("[SymondsStreetDressing] No Lit template material assigned; street dressing skipped.", this);
            return;
        }

        Layout layout = MeasureStreet();
        // At the scene root rather than under this object, so it can be destroyed while this object is being disabled.
        generated = new GameObject("Symonds Street dressing (generated)");
        generated.hideFlags = Application.isPlaying ? HideFlags.NotEditable : HideFlags.DontSave | HideFlags.NotEditable;

        HideCoveredTiles(layout);
        BuildStreet(layout);
        if (busShelter)
            BuildBusShelter(layout);
        if (towers)
            BuildTowers();
    }

    void Clear()
    {
        foreach (Renderer r in hiddenTiles)
            if (r != null)
                r.forceRenderingOff = false;
        hiddenTiles.Clear();

        if (generated != null)
            DestroySafe(generated);
        generated = null;
        foreach (Object o in ownedAssets)
            if (o != null)
                DestroySafe(o);
        ownedAssets.Clear();
    }

    static void DestroySafe(Object o)
    {
        if (Application.isPlaying) Destroy(o);
        else DestroyImmediate(o);
    }

    // ---- Measuring the existing street -------------------------------------------------------------------------------

    readonly RaycastHit[] hitBuffer = new RaycastHit[16];

    /// <summary>Top of the existing street surface at (x, z), from its colliders; NaN if there is none.</summary>
    float SurfaceY(float x, float z)
    {
        int n = Physics.RaycastNonAlloc(new Vector3(x, 5f, z), Vector3.down, hitBuffer, 10f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.NaN;
        for (int i = 0; i < n; i++)
        {
            Collider c = hitBuffer[i].collider;
            if (existingStreetRoot != null && !c.transform.IsChildOf(existingStreetRoot))
                continue;
            if (float.IsNaN(best) || hitBuffer[i].point.y > best)
                best = hitBuffer[i].point.y;
        }
        return best;
    }

    /// <summary>Walks out from the middle of the road until the surface steps up: that is the kerb.</summary>
    bool FindKerb(float x, float centreY, int direction, out float kerbZ, out float footY)
    {
        const float step = 0.05f, rise = 0.04f;
        for (float d = 1f; d < 14f; d += step)
        {
            float z = roadCentreZ + direction * d;
            float y = SurfaceY(x, z);
            if (float.IsNaN(y) || y - centreY < rise)
                continue;
            kerbZ = z - direction * step * 0.5f;
            float behind = SurfaceY(x, z + direction * 0.5f);
            footY = float.IsNaN(behind) ? y : behind;
            return true;
        }
        kerbZ = footY = float.NaN;
        return false;
    }

    Layout MeasureStreet()
    {
        var layout = new Layout
        {
            roadY = roadY, southKerb = southKerbZ, northKerb = northKerbZ, southFootY = footpathY, northFootY = footpathY,
        };
        if (!measureKerbs)
            return layout;

        Physics.SyncTransforms();
        var south = new List<float>();
        var north = new List<float>();
        var roads = new List<float>();
        var southFeet = new List<float>();
        var northFeet = new List<float>();
        foreach (float x in new[] { probeX - 6f, probeX, probeX + 6f })
        {
            float centreY = SurfaceY(x, roadCentreZ);
            if (float.IsNaN(centreY))
                continue;
            roads.Add(centreY);
            if (FindKerb(x, centreY, -1, out float s, out float sy)) { south.Add(s); southFeet.Add(sy); }
            if (FindKerb(x, centreY, +1, out float nz, out float ny)) { north.Add(nz); northFeet.Add(ny); }
        }

        if (south.Count == 0 || north.Count == 0)
        {
            Debug.LogWarning("[SymondsStreetDressing] Could not find the kerbs from the street colliders; using the fallback values.", this);
            return layout;
        }
        float southZ = Median(south), northZ = Median(north);
        if (noStoppingLineZ.Length > 0 && (southZ > Mathf.Min(noStoppingLineZ) || northZ < Mathf.Max(noStoppingLineZ)))
        {
            Debug.LogWarning($"[SymondsStreetDressing] Measured kerbs (Z {southZ:F2} and {northZ:F2}) fall inside the painted lanes; " +
                             "using the fallback values.", this);
            return layout;
        }
        layout.roadY = Median(roads);
        layout.southKerb = southZ;
        layout.northKerb = northZ;
        layout.southFootY = Median(southFeet);
        layout.northFootY = Median(northFeet);
        Debug.Log($"[SymondsStreetDressing] Kerbs measured at Z {southZ:F2} and {northZ:F2}.", this);

        if (roadEdgeTrigger != null)
        {
            float z = roadEdgeTrigger.position.z;
            float offset = Mathf.Min(Mathf.Abs(z - layout.southKerb), Mathf.Abs(z - layout.northKerb));
            if (offset > 0.75f)
                Debug.LogWarning($"[SymondsStreetDressing] The RoadEdge trigger ('stepped off kerb') is {offset:F2} m from the kerb you can see. " +
                                 "Consider moving the trigger to the kerb line.", this);
        }
        return layout;
    }

    static float Median(List<float> values)
    {
        values.Sort();
        return values[values.Count / 2];
    }

    void HideCoveredTiles(Layout layout)
    {
        if (existingStreetRoot == null)
            return;
        const float tolerance = 0.05f;
        float zMin = layout.southKerb - footpathWidth - tolerance, zMax = layout.northKerb + footpathWidth + tolerance;
        foreach (MeshRenderer r in existingStreetRoot.GetComponentsInChildren<MeshRenderer>(true))
        {
            Bounds b = r.bounds;
            if (b.size.y > 0.5f || b.min.z < zMin || b.max.z > zMax)
                continue; // only flat ground pieces wholly under the new surfaces
            // ... and no higher than the new surface above them, so anything raised in the road (an island, say) stays
            bool inRoad = b.min.z >= layout.southKerb - tolerance && b.max.z <= layout.northKerb + tolerance;
            float surface = inRoad ? layout.roadY : Mathf.Max(layout.southFootY, layout.northFootY);
            if (b.max.y > surface + 0.02f)
                continue;
            foreach (Vector2 seg in segments)
                if (b.min.x >= seg.x - 0.05f && b.max.x <= seg.y + 0.05f)
                {
                    r.forceRenderingOff = true; // not serialized, so the scene itself is never changed
                    hiddenTiles.Add(r);
                    break;
                }
        }
    }

    // ---- Road, kerbs, footpaths, markings ----------------------------------------------------------------------------

    void BuildStreet(Layout l)
    {
        var asphalt = new MeshBatch();
        var channel = new MeshBatch();
        var kerb = new MeshBatch();
        var pavers = new MeshBatch();
        var white = new MeshBatch();
        var yellow = new MeshBatch();
        var green = new MeshBatch();

        foreach (Vector2 seg in segments)
        {
            float x0 = seg.x, x1 = seg.y;
            asphalt.FlatRect(x0, x1, l.southKerb, l.northKerb, l.roadY + SurfaceLift, AsphaltTile);

            // Each side: channel on the road, kerb face and top, yellow strip, then the slab footpath.
            foreach (int side in new[] { -1, 1 })
            {
                float kerbZ = side < 0 ? l.southKerb : l.northKerb;
                float footY = side < 0 ? l.southFootY : l.northFootY;
                float inward = -side; // towards the road
                channel.FlatRectBetween(x0, x1, kerbZ, kerbZ + inward * ChannelWidth, l.roadY + ChannelLift, 1f);
                kerb.KerbFace(x0, x1, kerbZ, l.roadY, footY + SurfaceLift, inward);
                float top0 = kerbZ, top1 = kerbZ + side * KerbTopWidth;
                kerb.FlatRectBetween(x0, x1, top0, top1, footY + SurfaceLift, 1f);
                yellow.FlatRectBetween(x0, x1, top1, top1 + side * YellowStripWidth, footY + SurfaceLift, 1f);
                pavers.FlatRectBetween(x0, x1, top1 + side * YellowStripWidth, kerbZ + side * footpathWidth, footY + SurfaceLift, PaverTile);
            }

            float paintY = l.roadY + PaintLift;
            foreach (float z in laneLineZ)
                white.FlatRect(x0, x1, z - lineWidth * 0.5f, z + lineWidth * 0.5f, paintY, 1f);
            foreach (float z in centreLineZ)
                white.FlatRect(x0, x1, z - lineWidth * 0.5f, z + lineWidth * 0.5f, paintY, 1f);
            // Dashes are phased from world X = 0, so they stay in step across segments.
            float period = noStoppingDash + noStoppingGap;
            foreach (float z in noStoppingLineZ)
                for (float x = Mathf.Ceil(x0 / period) * period; x + noStoppingDash <= x1; x += period)
                    yellow.FlatRect(x, x + noStoppingDash, z - lineWidth * 0.5f, z + lineWidth * 0.5f, paintY, 1f);
        }

        foreach (BusLanePatch patch in busLanes)
        {
            float half = LaneWidth * 0.5f - 0.15f;
            green.FlatRect(Mathf.Min(patch.startX, patch.endX), Mathf.Max(patch.startX, patch.endX),
                patch.laneCentreZ - half, patch.laneCentreZ + half, l.roadY + PatchLift, 1f);
            PaintBusLaneWords(white, patch, l.roadY + PaintLift);
        }

        Texture2D asphaltTex = Own(StreetTextures.Asphalt(512, 11));
        Texture2D paverTex = Own(StreetTextures.Pavers(256, 4, 23));
        Texture2D speckleTex = Own(StreetTextures.Speckle(128, 37));

        Emit("Asphalt", asphalt, MakeMaterial("Asphalt", AsphaltColour, asphaltTex, 0.12f), false);
        Emit("Channel", channel, MakeMaterial("Concrete channel", ChannelColour, speckleTex, 0.1f), false);
        Emit("Kerbs", kerb, MakeMaterial("Basalt kerb", KerbColour, speckleTex, 0.15f), false);
        Emit("Footpaths", pavers, MakeMaterial("Footpath slabs", PaverColour, paverTex, 0.1f), false);
        Emit("White markings", white, MakeMaterial("White paint", WhitePaint, speckleTex, 0.25f), false);
        Emit("Yellow markings", yellow, MakeMaterial("Yellow paint", YellowPaint, speckleTex, 0.25f), false);
        Emit("Bus lane patches", green, MakeMaterial("Bus lane green", BusLaneGreen, speckleTex, 0.15f), false);
    }

    /// <summary>
    /// "BUS" then "LANE", in tall narrow road letters made of painted strokes, read in order by an approaching driver.
    /// </summary>
    void PaintBusLaneWords(MeshBatch paint, BusLanePatch patch, float y)
    {
        const float letterWidth = 0.6f, letterHeight = 2.0f, letterGap = 0.3f, wordGap = 1.6f, stroke = 0.14f;
        Vector3 up = patch.travelTowardsPositiveX ? Vector3.right : Vector3.left;  // text "up" = direction of travel
        Vector3 right = Vector3.Cross(Vector3.up, up);                               // the driver's right
        float entryX = patch.travelTowardsPositiveX ? Mathf.Min(patch.startX, patch.endX) : Mathf.Max(patch.startX, patch.endX);
        Vector3 origin = new Vector3(entryX, y, patch.laneCentreZ) + up * 2.5f;     // bottom-centre of the first word

        string[] words = { "BUS", "LANE" };
        foreach (string word in words)
        {
            float wordWidth = word.Length * letterWidth + (word.Length - 1) * letterGap;
            for (int i = 0; i < word.Length; i++)
            {
                Vector3 letterOrigin = origin + right * (-wordWidth * 0.5f + i * (letterWidth + letterGap));
                foreach (Vector4 s in Glyph(word[i]))
                {
                    // Glyph strokes are in a 1 x 2 box (x across, y up)
                    Vector3 a = letterOrigin + right * (s.x * letterWidth) + up * (s.y * letterHeight * 0.5f);
                    Vector3 b = letterOrigin + right * (s.z * letterWidth) + up * (s.w * letterHeight * 0.5f);
                    paint.Stroke(a, b, stroke);
                }
            }
            origin += up * (letterHeight + wordGap);
        }
    }

    /// <summary>Strokes (x0, y0, x1, y1) of a blocky road-marking letter in a 1 wide x 2 high box.</summary>
    static IEnumerable<Vector4> Glyph(char c)
    {
        switch (c)
        {
            case 'B':
                return new[] { new Vector4(0, 0, 0, 2), new Vector4(0, 2, 0.8f, 2), new Vector4(0, 1, 0.85f, 1), new Vector4(0, 0, 0.85f, 0),
                               new Vector4(0.85f, 1.1f, 0.85f, 1.9f), new Vector4(1, 0.1f, 1, 0.9f) };
            case 'U':
                return new[] { new Vector4(0, 2, 0, 0), new Vector4(0, 0, 1, 0), new Vector4(1, 0, 1, 2) };
            case 'S':
                return new[] { new Vector4(1, 2, 0, 2), new Vector4(0, 2, 0, 1), new Vector4(0, 1, 1, 1), new Vector4(1, 1, 1, 0), new Vector4(1, 0, 0, 0) };
            case 'L':
                return new[] { new Vector4(0, 2, 0, 0), new Vector4(0, 0, 1, 0) };
            case 'A':
                return new[] { new Vector4(0, 0, 0, 2), new Vector4(0, 2, 1, 2), new Vector4(1, 2, 1, 0), new Vector4(0, 1, 1, 1) };
            case 'N':
                return new[] { new Vector4(0, 0, 0, 2), new Vector4(0, 2, 1, 0), new Vector4(1, 0, 1, 2) };
            case 'E':
                return new[] { new Vector4(0, 0, 0, 2), new Vector4(0, 2, 1, 2), new Vector4(0, 1, 0.75f, 1), new Vector4(0, 0, 1, 0) };
            default:
                return Array.Empty<Vector4>();
        }
    }

    // ---- Bus shelter -------------------------------------------------------------------------------------------------

    /// <summary>
    /// A long glass-roofed shelter like the ones on the far side of the street in the photos: slim posts, a pale green
    /// glass canopy, a row of poster panels along the back, and a bench. Faces the road from the north footpath.
    /// </summary>
    void BuildBusShelter(Layout l)
    {
        var frame = new MeshBatch();
        var glass = new MeshBatch();
        var posters = new MeshBatch();

        float x0 = Mathf.Min(shelterX.x, shelterX.y), x1 = Mathf.Max(shelterX.x, shelterX.y);
        float ground = l.northFootY;
        float front = l.northKerb + 0.9f, back = front + 2.3f;
        const float roofHeight = 2.9f, postSize = 0.1f, panelHeight = 1.9f, panelBottom = 0.45f, panelWidth = 1.4f;

        // Posts along the back, plus the front corners
        int bays = Mathf.Max(1, Mathf.RoundToInt((x1 - x0) / 3f));
        for (int i = 0; i <= bays; i++)
        {
            float x = Mathf.Lerp(x0, x1, (float)i / bays);
            frame.Box(new Vector3(x, ground + roofHeight * 0.5f, back - 0.1f), new Vector3(postSize, roofHeight, postSize), 1f);
        }
        foreach (float x in new[] { x0, x1 })
            frame.Box(new Vector3(x, ground + roofHeight * 0.5f, front + 0.2f), new Vector3(postSize, roofHeight, postSize), 1f);

        // Canopy: glass panes between thin cross beams
        glass.Box(new Vector3((x0 + x1) * 0.5f, ground + roofHeight, (front + back) * 0.5f - 0.1f),
            new Vector3(x1 - x0 + 0.4f, 0.04f, back - front + 0.4f), 1f);
        for (int i = 0; i <= bays * 2; i++)
        {
            float x = Mathf.Lerp(x0, x1, (float)i / (bays * 2));
            frame.Box(new Vector3(x, ground + roofHeight + 0.04f, (front + back) * 0.5f - 0.1f), new Vector3(0.06f, 0.05f, back - front + 0.4f), 1f);
        }
        frame.Box(new Vector3((x0 + x1) * 0.5f, ground + roofHeight + 0.04f, front - 0.1f), new Vector3(x1 - x0 + 0.4f, 0.08f, 0.08f), 1f);

        // Poster panels along the back wall, each showing one of the four posters
        int panels = Mathf.FloorToInt((x1 - x0 - 0.4f) / (panelWidth + 0.15f));
        float startX = (x0 + x1) * 0.5f - panels * (panelWidth + 0.15f) * 0.5f;
        for (int i = 0; i < panels; i++)
        {
            float cx = startX + i * (panelWidth + 0.15f) + panelWidth * 0.5f;
            Vector3 centre = new Vector3(cx, ground + panelBottom + panelHeight * 0.5f, back - 0.1f);
            frame.Box(centre + Vector3.forward * 0.02f, new Vector3(panelWidth + 0.08f, panelHeight + 0.08f, 0.06f), 1f);
            float u0 = (i % 4) * 0.25f;
            posters.VerticalRect(new Vector3(cx - panelWidth * 0.5f, centre.y - panelHeight * 0.5f, back - 0.14f),
                Vector3.right * panelWidth, Vector3.up * panelHeight, Vector3.back,
                new Vector2(u0 + 0.004f, 0f), new Vector2(u0 + 0.246f, 1f));
        }

        // Bench
        frame.Box(new Vector3((x0 + x1) * 0.5f, ground + 0.45f, back - 0.45f), new Vector3(Mathf.Min(4f, x1 - x0 - 1f), 0.06f, 0.4f), 1f);
        foreach (float dx in new[] { -1.5f, 1.5f })
            frame.Box(new Vector3((x0 + x1) * 0.5f + dx, ground + 0.22f, back - 0.45f), new Vector3(0.06f, 0.44f, 0.35f), 1f);

        Texture2D posterTex = Own(StreetTextures.Posters(128, 256, 5));
        Emit("Bus shelter frame", frame, MakeMaterial("Shelter frame", ShelterFrame, null, 0.45f), true);
        Emit("Bus shelter canopy", glass, MakeMaterial("Shelter glass", ShelterGlass, null, 0.85f), true);
        Emit("Bus shelter posters", posters, MakeMaterial("Posters", Color.white, posterTex, 0.6f), false);
    }

    // ---- Skyline -----------------------------------------------------------------------------------------------------

    void BuildTowers()
    {
        var byStyle = new Dictionary<StreetTextures.FacadeStyle, MeshBatch>();
        foreach (Tower t in towerList)
        {
            if (!byStyle.TryGetValue(t.style, out MeshBatch batch))
                byStyle[t.style] = batch = new MeshBatch();
            Vector3 centre = new Vector3(t.centre.x, t.height * 0.5f, t.centre.y);
            batch.Box(centre, new Vector3(t.footprint.x, t.height, t.footprint.y), FacadeBay, FacadeFloor);
        }
        foreach (var pair in byStyle)
        {
            float smoothness = pair.Key == StreetTextures.FacadeStyle.DarkGlass ? 0.75f
                : pair.Key == StreetTextures.FacadeStyle.RustFins ? 0.45f : 0.2f;
            Texture2D tex = Own(StreetTextures.Facade(pair.Key, 128, 41 + (int)pair.Key));
            Emit(pair.Key + " towers", pair.Value, MakeMaterial(pair.Key + " facade", Color.white, tex, smoothness), true);
        }
    }

    // ---- Helpers -----------------------------------------------------------------------------------------------------

    T Own<T>(T asset) where T : Object
    {
        asset.hideFlags = HideFlags.DontSave;
        ownedAssets.Add(asset);
        return asset;
    }

    Material MakeMaterial(string name, Color colour, Texture texture, float smoothness)
    {
        var m = Own(new Material(litTemplate) { name = name + " (generated)" });
        m.SetColor(BaseColorId, colour);
        m.SetTexture(BaseMapId, texture);
        m.SetFloat(SmoothnessId, smoothness);
        return m;
    }

    void Emit(string name, MeshBatch batch, Material material, bool castShadows)
    {
        if (batch.IsEmpty)
            return;
        Mesh mesh = Own(batch.ToMesh(name));
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        go.hideFlags = generated.hideFlags;
        go.transform.SetParent(generated.transform, false);
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
        r.receiveShadows = true;
        r.lightProbeUsage = LightProbeUsage.Off;
        r.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
    }

    /// <summary>Accumulates quads and boxes in world space, with UVs in metres divided by a tile size.</summary>
    class MeshBatch
    {
        readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<Vector3> normals = new List<Vector3>();
        readonly List<Vector2> uvs = new List<Vector2>();
        readonly List<int> triangles = new List<int>();

        public bool IsEmpty => vertices.Count == 0;

        /// <summary>A quad a-b-c-d (in order around it), facing <paramref name="normal"/>.</summary>
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            int i = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
            uvs.Add(ua); uvs.Add(ub); uvs.Add(uc); uvs.Add(ud);
            for (int k = 0; k < 4; k++)
                normals.Add(normal);
            // Unity draws the side from which the corners run clockwise
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), normal) >= 0f)
                triangles.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            else
                triangles.AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
        }

        /// <summary>Horizontal rectangle facing up, with UVs from world X/Z.</summary>
        public void FlatRect(float x0, float x1, float z0, float z1, float y, float tile)
        {
            Quad(new Vector3(x0, y, z0), new Vector3(x0, y, z1), new Vector3(x1, y, z1), new Vector3(x1, y, z0), Vector3.up,
                new Vector2(x0, z0) / tile, new Vector2(x0, z1) / tile, new Vector2(x1, z1) / tile, new Vector2(x1, z0) / tile);
        }

        public void FlatRectBetween(float x0, float x1, float za, float zb, float y, float tile) =>
            FlatRect(x0, x1, Mathf.Min(za, zb), Mathf.Max(za, zb), y, tile);

        /// <summary>The vertical face of a kerb along X at <paramref name="z"/>, facing the road.</summary>
        public void KerbFace(float x0, float x1, float z, float bottom, float top, float facing)
        {
            Quad(new Vector3(x0, bottom, z), new Vector3(x0, top, z), new Vector3(x1, top, z), new Vector3(x1, bottom, z),
                new Vector3(0f, 0f, facing), new Vector2(x0, 0f), new Vector2(x0, top - bottom), new Vector2(x1, top - bottom), new Vector2(x1, 0f));
        }

        /// <summary>A vertical rectangle from <paramref name="origin"/> spanning two edges, with a UV sub-rectangle.</summary>
        public void VerticalRect(Vector3 origin, Vector3 across, Vector3 upward, Vector3 normal, Vector2 uvMin, Vector2 uvMax)
        {
            Quad(origin, origin + upward, origin + upward + across, origin + across, normal,
                uvMin, new Vector2(uvMin.x, uvMax.y), uvMax, new Vector2(uvMax.x, uvMin.y));
        }

        /// <summary>A flat painted stroke from a to b, <paramref name="width"/> wide, squared off past its ends.</summary>
        public void Stroke(Vector3 a, Vector3 b, float width)
        {
            Vector3 along = (b - a).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, along) * (width * 0.5f);
            a -= along * (width * 0.5f);
            b += along * (width * 0.5f);
            Quad(a - side, a + side, b + side, b - side, Vector3.up, Vector2.zero, Vector2.up, Vector2.one, Vector2.right);
        }

        /// <summary>An axis-aligned box with UVs in metres / tile (horizontal) and metres / tileV (vertical).</summary>
        public void Box(Vector3 centre, Vector3 size, float tile, float tileV = 0f)
        {
            if (tileV <= 0f) tileV = tile;
            Vector3 h = size * 0.5f;
            Vector3 p = centre;
            float w = size.x / tile, d = size.z / tile, ht = size.y / tileV;
            // Sides (u around the box, v up)
            Quad(p + new Vector3(-h.x, -h.y, -h.z), p + new Vector3(-h.x, h.y, -h.z), p + new Vector3(h.x, h.y, -h.z), p + new Vector3(h.x, -h.y, -h.z),
                Vector3.back, new Vector2(0, 0), new Vector2(0, ht), new Vector2(w, ht), new Vector2(w, 0));
            Quad(p + new Vector3(h.x, -h.y, h.z), p + new Vector3(h.x, h.y, h.z), p + new Vector3(-h.x, h.y, h.z), p + new Vector3(-h.x, -h.y, h.z),
                Vector3.forward, new Vector2(0, 0), new Vector2(0, ht), new Vector2(w, ht), new Vector2(w, 0));
            Quad(p + new Vector3(h.x, -h.y, -h.z), p + new Vector3(h.x, h.y, -h.z), p + new Vector3(h.x, h.y, h.z), p + new Vector3(h.x, -h.y, h.z),
                Vector3.right, new Vector2(0, 0), new Vector2(0, ht), new Vector2(d, ht), new Vector2(d, 0));
            Quad(p + new Vector3(-h.x, -h.y, h.z), p + new Vector3(-h.x, h.y, h.z), p + new Vector3(-h.x, h.y, -h.z), p + new Vector3(-h.x, -h.y, -h.z),
                Vector3.left, new Vector2(0, 0), new Vector2(0, ht), new Vector2(d, ht), new Vector2(d, 0));
            // Top and bottom (a plain corner of the texture, so roofs show no windows)
            Vector2 c = new Vector2(0.02f, 0.02f);
            Quad(p + new Vector3(-h.x, h.y, -h.z), p + new Vector3(-h.x, h.y, h.z), p + new Vector3(h.x, h.y, h.z), p + new Vector3(h.x, h.y, -h.z),
                Vector3.up, c, c, c, c);
            Quad(p + new Vector3(-h.x, -h.y, -h.z), p + new Vector3(h.x, -h.y, -h.z), p + new Vector3(h.x, -h.y, h.z), p + new Vector3(-h.x, -h.y, h.z),
                Vector3.down, c, c, c, c);
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name + " (generated)" };
            if (vertices.Count > 65000)
                mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            mesh.UploadMeshData(false);
            return mesh;
        }
    }
}
