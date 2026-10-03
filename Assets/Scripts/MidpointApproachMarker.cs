using UnityEngine;

/// <summary>
/// Faint strip on the road at the midpoint line that fades in as the participant approaches it, as a subtle cue that the
/// turn-around point is near. It slides along the road to stay level with the participant (the midpoint trigger spans
/// the whole road, so a full-length line would look like a real road marking), and is only shown while they are
/// crossing towards the midpoint with the midpoint flip enabled.
///
/// Put this on a flat quad lying on the road at the midpoint, with its local X axis along the road.
/// </summary>
[RequireComponent(typeof(Renderer))]
public class MidpointApproachMarker : MonoBehaviour
{
    public CrossingManager crossingManager;
    [Tooltip("Distance from the midpoint line (m) at which the marker starts to fade in.")]
    [Min(0.1f)] public float showDistance = 3f;
    [Tooltip("Opacity when the participant is standing on the line.")]
    [Range(0f, 1f)] public float maxOpacity = 0.5f;
    public Color color = new Color(0.3f, 0.85f, 1f); // soft cyan: unlike any real road paint, so it reads as a separate cue
    [Tooltip("How far (m) the marker may slide either way along the road from where it is placed.")]
    [Min(0f)] public float slideRange = 25f;

    Renderer mRenderer;
    MaterialPropertyBlock mBlock;
    Vector3 mOrigin, mAlongRoad, mAcrossRoad;

    static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    static readonly int BaseMap = Shader.PropertyToID("_BaseMap");

    void Awake()
    {
        if (crossingManager == null)
            crossingManager = FindFirstObjectByType<CrossingManager>();
        mRenderer = GetComponent<Renderer>();
        mBlock = new MaterialPropertyBlock();
        mOrigin = transform.position;
        mAlongRoad = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;
        mAcrossRoad = Vector3.Cross(mAlongRoad, Vector3.up);

        mRenderer.GetPropertyBlock(mBlock);
        mBlock.SetTexture(BaseMap, MakeSoftEdgeTexture());
        mRenderer.SetPropertyBlock(mBlock);
        mRenderer.enabled = false;
    }

    void Update()
    {
        bool approaching = crossingManager != null && crossingManager.midpointFlipEnabled
            && crossingManager.currentState == CrossingManager.CrossingState.CrossingToMidpoint;
        PedestrianTracker.EnsureSampled();
        if (!approaching || !PedestrianTracker.TryGet(out Vector3 participant, out _, out _))
        {
            mRenderer.enabled = false;
            return;
        }

        Vector3 offset = participant - mOrigin;
        float distanceToLine = Mathf.Abs(Vector3.Dot(offset, mAcrossRoad));
        float opacity = maxOpacity * Mathf.Clamp01(1f - distanceToLine / showDistance); // linear fade-in
        mRenderer.enabled = opacity > 0.001f;
        if (!mRenderer.enabled)
            return;

        // Stay level with the participant along the road
        float along = Mathf.Clamp(Vector3.Dot(offset, mAlongRoad), -slideRange, slideRange);
        transform.position = mOrigin + mAlongRoad * along;

        mRenderer.GetPropertyBlock(mBlock);
        mBlock.SetColor(BaseColor, new Color(color.r, color.g, color.b, opacity));
        mRenderer.SetPropertyBlock(mBlock);
    }

    /// <summary>White texture whose alpha fades out towards the ends and sides, so the strip has no hard edges.</summary>
    static Texture2D MakeSoftEdgeTexture()
    {
        const int width = 64, height = 16;
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float u = Mathf.Abs(x / (width - 1f) * 2f - 1f);  // 0 at centre, 1 at the ends
                float v = Mathf.Abs(y / (height - 1f) * 2f - 1f); // 0 at centre, 1 at the sides
                float alpha = (1f - Mathf.SmoothStep(0.4f, 1f, u)) * (1f - Mathf.SmoothStep(0.3f, 1f, v));
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        texture.Apply();
        return texture;
    }
}
