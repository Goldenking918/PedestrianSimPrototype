using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Switches the scene between day and night whenever a scenario starts (<see cref="ScenarioConfig.night"/>).
///
/// Night: the sun becomes dim, bluish moonlight; ambient light and the sky go dark; the street lamps switch on; and every
/// car gets a headlight as it spawns (one spotlight lighting the road, plus glowing headlamps and tail lights, which are
/// what a pedestrian actually sees of an approaching car). Day: the scene's own lighting, recorded at start-up, is
/// restored exactly. Lamp lights and headlights cast no shadows, because shadowed realtime lights are expensive in VR.
/// </summary>
public class LightingController : MonoBehaviour
{
    public ScenarioManager scenarioManager;
    [Tooltip("The scene's sun. Found automatically (first directional light) if empty.")]
    public Light sun;
    [Tooltip("The lights inside the street lamps (with their glowing bulbs as children). Switched on at night only.")]
    public Light[] streetLights = new Light[0];

    [Header("Night sky and ambient light")]
    public Color moonColor = new Color(0.55f, 0.65f, 0.9f);
    [Min(0f)] public float moonIntensity = 0.08f;
    public Color nightAmbientColor = new Color(0.03f, 0.035f, 0.05f);
    [Range(0f, 1f)] public float nightSkyExposure = 0.03f;

    [Header("Car headlights (night only)")]
    public Color headlightColor = new Color(1f, 0.95f, 0.85f);
    [Min(0f)] public float headlightIntensity = 30f;
    [Min(0f)] public float headlightRange = 40f;
    [Range(1f, 179f)] public float headlightAngle = 70f;
    [Tooltip("Height of the headlight above the bottom of the car (m).")]
    [Min(0f)] public float headlightHeight = 0.7f;
    [Tooltip("How far the headlight points below horizontal (degrees).")]
    [Range(0f, 30f)] public float headlightTilt = 3f;
    [Tooltip("Glowing material for the headlamps seen from in front of the car.")]
    public Material headlampMaterial;
    [Tooltip("Glowing material for the tail lights seen from behind the car.")]
    public Material taillampMaterial;
    [Tooltip("Size of each glowing headlamp / tail light (m).")]
    public Vector2 lampGlowSize = new Vector2(0.25f, 0.12f);

    public bool IsNight { get; private set; }

    const string HeadlightName = "Headlight";

    // Day look, recorded from the scene so it can be restored exactly
    float daySunIntensity;
    Color daySunColor;
    LightShadows daySunShadows;
    AmbientMode dayAmbientMode;
    Color dayAmbientColor;
    Material daySky;
    float dayReflectionIntensity;
    Material nightSky;

    void Awake()
    {
        if (scenarioManager == null)
            scenarioManager = FindFirstObjectByType<ScenarioManager>();
        if (sun == null)
            foreach (Light l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional) { sun = l; break; }

        if (sun != null)
        {
            daySunIntensity = sun.intensity;
            daySunColor = sun.color;
            daySunShadows = sun.shadows;
        }
        dayAmbientMode = RenderSettings.ambientMode;
        dayAmbientColor = RenderSettings.ambientLight;
        daySky = RenderSettings.skybox;
        dayReflectionIntensity = RenderSettings.reflectionIntensity;
        SetStreetLights(false);
    }

    void OnEnable()
    {
        if (scenarioManager != null)
            scenarioManager.ScenarioStarted += OnScenarioStarted;
    }

    void OnDisable()
    {
        if (scenarioManager != null)
            scenarioManager.ScenarioStarted -= OnScenarioStarted;
    }

    void OnDestroy()
    {
        if (nightSky != null)
            Destroy(nightSky);
    }

    void OnScenarioStarted(ScenarioConfig scenario) => SetNight(scenario.night);

    public void SetNight(bool night)
    {
        IsNight = night;

        if (sun != null)
        {
            sun.intensity = night ? moonIntensity : daySunIntensity;
            sun.color = night ? moonColor : daySunColor;
            sun.shadows = night ? LightShadows.None : daySunShadows; // moonlight shadows are barely visible; save the cost
        }
        RenderSettings.ambientMode = night ? AmbientMode.Flat : dayAmbientMode;
        RenderSettings.ambientLight = night ? nightAmbientColor : dayAmbientColor;
        RenderSettings.skybox = night ? NightSky() : daySky;
        RenderSettings.reflectionIntensity = night ? 0.1f : dayReflectionIntensity;
        DynamicGI.UpdateEnvironment();

        SetStreetLights(night);
        foreach (CarMovement car in CarMovement.ActiveVehicles)
            if (car != null)
                SetHeadlight(car, night);

        Debug.Log($"[LightingController] {(night ? "Night" : "Day")} lighting.", this);
    }

    // Cars spawn during the run, so give each new car its headlight as it appears.
    void Update()
    {
        if (!IsNight)
            return;
        foreach (CarMovement car in CarMovement.ActiveVehicles)
            if (car != null)
                SetHeadlight(car, true);
    }

    void SetStreetLights(bool on)
    {
        foreach (Light l in streetLights)
            if (l != null)
            {
                l.enabled = true;
                l.gameObject.SetActive(on); // includes the glowing bulb
            }
    }

    void SetHeadlight(CarMovement car, bool on)
    {
        Transform existing = car.transform.Find(HeadlightName);
        if (existing != null)
        {
            existing.gameObject.SetActive(on);
            return;
        }
        if (!on)
            return;

        Vector3 forward = Vector3.ProjectOnPlane(car.transform.forward, Vector3.up).normalized;
        var go = new GameObject(HeadlightName);
        go.transform.SetParent(car.transform, false);

        // Lamp positions. Each car model has its lamps in a different place, so the car prefabs carry marker points
        // (HeadlampL/R, TaillampL/R) on their actual lamps; a marker's scale is the glow size in metres. Car models
        // without markers fall back to a generic placement at the corners.
        Transform headL = car.transform.Find("HeadlampL"), headR = car.transform.Find("HeadlampR");
        Transform tailL = car.transform.Find("TaillampL"), tailR = car.transform.Find("TaillampR");
        bool hasMarkers = headL != null && headR != null;

        // Generic fallback positions (front and rear corners at a typical lamp height)
        float bottom = car.transform.position.y;
        foreach (Renderer r in car.GetComponentsInChildren<Renderer>())
            bottom = Mathf.Min(bottom, r.bounds.min.y);
        Vector3 front = car.transform.position + forward * car.Geometry.frontExtent;
        Vector3 rear = car.transform.position - forward * car.Geometry.rearExtent;
        front.y = rear.y = bottom + headlightHeight;

        // One centred spotlight between the headlamps lights the road (looks almost the same as two, at half the cost).
        // Placed before the glows are added, because the glows are its children and would move with it.
        Vector3 lightPosition = hasMarkers ? (headL.position + headR.position) * 0.5f : front;
        go.transform.SetPositionAndRotation(lightPosition + forward * 0.05f,
            Quaternion.LookRotation(forward) * Quaternion.Euler(headlightTilt, 0f, 0f));

        if (hasMarkers)
        {
            foreach (Transform m in new[] { headL, headR })
                AddGlow(go.transform, headlampMaterial, m.position + forward * 0.01f, forward, m.lossyScale);
            foreach (Transform m in new[] { tailL, tailR })
                if (m != null)
                    AddGlow(go.transform, taillampMaterial, m.position - forward * 0.01f, -forward, m.lossyScale);
        }
        else
        {
            float side = Mathf.Max(0.1f, car.Geometry.halfWidth - 0.3f);
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            foreach (float s in new[] { -side, side })
            {
                AddGlow(go.transform, headlampMaterial, front + right * s + forward * 0.03f, forward, lampGlowSize);
                AddGlow(go.transform, taillampMaterial, rear + right * s - forward * 0.03f, -forward, lampGlowSize);
            }
        }

        var light = go.AddComponent<Light>();
        light.type = LightType.Spot;
        light.color = headlightColor;
        light.intensity = headlightIntensity;
        light.range = headlightRange;
        light.spotAngle = headlightAngle;
        light.innerSpotAngle = headlightAngle * 0.5f;
        light.shadows = LightShadows.None;
    }

    /// <summary>
    /// A small glowing rectangle (width x height in metres) at <paramref name="position"/>, visible from the
    /// <paramref name="facing"/> side. Flat shapes with no light of their own, so they cost almost nothing; the scene's
    /// bloom gives them their glare.
    /// </summary>
    void AddGlow(Transform parent, Material material, Vector3 position, Vector3 facing, Vector2 size)
    {
        if (material == null)
            return;
        var glow = new GameObject("Glow", typeof(MeshFilter), typeof(MeshRenderer));
        glow.transform.SetParent(parent, false);
        // A quad is visible from its -Z side, so point its +Z away from the viewer
        glow.transform.SetPositionAndRotation(position, Quaternion.LookRotation(-facing));
        Vector3 parentScale = parent.lossyScale;
        glow.transform.localScale = new Vector3(size.x / parentScale.x, size.y / parentScale.y, 1f);
        glow.GetComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        var r = glow.GetComponent<MeshRenderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
    }

    /// <summary>A darkened copy of the day sky (the original sky material asset is never changed).</summary>
    Material NightSky()
    {
        if (daySky == null)
            return null;
        if (nightSky == null)
        {
            nightSky = new Material(daySky) { name = daySky.name + " (Night)" };
            if (nightSky.HasProperty("_Exposure"))
                nightSky.SetFloat("_Exposure", nightSkyExposure);
            if (nightSky.HasProperty("_SkyTint"))
                nightSky.SetColor("_SkyTint", new Color(0.2f, 0.25f, 0.4f));
        }
        return nightSky;
    }
}
