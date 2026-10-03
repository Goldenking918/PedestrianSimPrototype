using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR;
using TrackedPoseDriver = UnityEngine.InputSystem.XR.TrackedPoseDriver;

/// <summary>
/// Switches between desktop (mouse + keyboard) and headset control at runtime.
/// Desktop mode applies whenever a headset isn't in use: none connected, XR not running, or (where the device reports it)
/// the headset isn't being worn. Putting the headset on switches to VR; taking it off or unplugging it switches back.
/// Sits on the XR Origin: in desktop mode, yaw turns the rig around the camera and pitch tilts Camera Offset
/// (the camera's parent). The camera's TrackedPoseDriver is paused in desktop mode so a connected but unworn
/// headset doesn't fight the mouse.
/// With Floor tracking the headset supplies the eye height, so in desktop mode Camera Offset is raised to
/// <see cref="desktopEyeHeight"/> instead (XR Origin sits on the pavement surface).
/// </summary>
public class MouseLook : MonoBehaviour
{
    public float mouseSensitivity = 200f;
    public Transform playerBody; // Player object; its yaw is kept in line with the view
    [Tooltip("Camera being controlled. Defaults to Camera.main.")]
    public Transform cameraTransform;
    [Tooltip("Always use mouse + keyboard, even when a headset is in use.")]
    public bool forceDesktopControls = false;
    [Tooltip("How often the headset state is checked (s).")]
    [Min(0.05f)] public float headsetPollInterval = 0.25f;
    [Tooltip("Camera height above the XR Origin in desktop mode (m). In VR the headset provides the real eye height.")]
    [Min(0f)] public float desktopEyeHeight = 1.6f;

    float xRotation = 0f;
    float nextPollTime;
    bool headsetInUse;
    bool modeInitialised;
    TrackedPoseDriver poseDriver;
    XROrigin xrOrigin;

    public bool DesktopMode => forceDesktopControls || !headsetInUse;

    /// <summary>
    /// Set while an on-screen UI (e.g. the researcher scenario panel) needs the mouse: frees the cursor and stops mouse look.
    /// </summary>
    public static bool SuppressLook
    {
        get => sSuppressLook;
        set
        {
            if (sSuppressLook == value)
                return;
            sSuppressLook = value;
            if (value)
                Cursor.lockState = CursorLockMode.None;
            else if (sInstance != null && sInstance.DesktopMode)
                Cursor.lockState = CursorLockMode.Locked;
        }
    }
    static bool sSuppressLook;
    static MouseLook sInstance;

    void Awake() => sInstance = this;

    void Start()
    {
        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;
        if (cameraTransform != null)
            poseDriver = cameraTransform.GetComponent<TrackedPoseDriver>();
        xrOrigin = GetComponent<XROrigin>();

        headsetInUse = IsHeadsetInUse();
        ApplyMode();
    }

    void Update()
    {
        if (Time.unscaledTime >= nextPollTime)
        {
            nextPollTime = Time.unscaledTime + headsetPollInterval;
            bool inUse = IsHeadsetInUse();
            if (inUse != headsetInUse)
            {
                headsetInUse = inUse;
                ApplyMode();
            }
        }

        if (!DesktopMode || cameraTransform == null)
            return;

        // Re-applied every frame: XR Origin resets the offset height whenever tracking (re)initialises
        SetOffsetHeight(desktopEyeHeight);

        if (SuppressLook)
            return;

        // Esc frees the cursor (e.g. to use the Inspector); click to capture it again
        if (Input.GetKeyDown(KeyCode.Escape))
            Cursor.lockState = CursorLockMode.None;
        else if (Input.GetMouseButtonDown(0))
            Cursor.lockState = CursorLockMode.Locked;

        if (Cursor.lockState != CursorLockMode.Locked)
            return;

        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity * Time.deltaTime;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity * Time.deltaTime;

        // Yaw: turn the whole rig about the camera's position so the view doesn't swing sideways
        transform.RotateAround(cameraTransform.position, Vector3.up, mouseX);

        // Pitch: tilt the camera's parent (Camera Offset), clamped to prevent flipping
        xRotation = Mathf.Clamp(xRotation - mouseY, -90f, 90f);
        PitchPivot().localRotation = Quaternion.Euler(xRotation, 0f, 0f);

        if (playerBody != null)
        {
            Vector3 forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up);
            if (forward.sqrMagnitude > 1e-4f)
                playerBody.rotation = Quaternion.LookRotation(forward);
        }
    }

    /// <summary>True when an XR headset is running and (if the device reports it) being worn.</summary>
    static bool IsHeadsetInUse()
    {
        if (!XRSettings.isDeviceActive)
            return false;
        InputDevice hmd = InputDevices.GetDeviceAtXRNode(XRNode.Head);
        if (!hmd.isValid)
            return false;
        if (hmd.TryGetFeatureValue(CommonUsages.userPresence, out bool worn))
            return worn;
        return true; // device doesn't report presence: connected counts as in use
    }

    void ApplyMode()
    {
        bool desktop = DesktopMode;

        // Clear any desktop pitch so the headset view (or a fresh desktop view) starts level
        xRotation = 0f;
        if (cameraTransform != null)
        {
            PitchPivot().localRotation = Quaternion.identity;
            if (desktop)
                cameraTransform.localRotation = Quaternion.identity; // drop any leftover head rotation
            SetOffsetHeight(desktop ? desktopEyeHeight : HeadsetOffsetHeight());
        }

        if (poseDriver != null)
            poseDriver.enabled = !desktop;

        Cursor.lockState = desktop && !SuppressLook ? CursorLockMode.Locked : CursorLockMode.None;

        if (modeInitialised)
            Debug.Log($"[MouseLook] Switched to {(desktop ? "desktop (mouse + keyboard)" : "headset")} controls.", this);
        modeInitialised = true;
    }

    /// <summary>Offset height XR Origin uses for the headset: 0 with Floor tracking (the headset reports eye height), else Camera Y Offset.</summary>
    float HeadsetOffsetHeight()
    {
        if (xrOrigin == null)
            return 0f;
        bool deviceRelative = xrOrigin.CurrentTrackingOriginMode == TrackingOriginModeFlags.Device ||
                              xrOrigin.CurrentTrackingOriginMode == TrackingOriginModeFlags.Unbounded;
        return deviceRelative ? xrOrigin.CameraYOffset : 0f;
    }

    void SetOffsetHeight(float y)
    {
        Transform offset = PitchPivot();
        if (offset == cameraTransform)
            return; // no Camera Offset object to move
        Vector3 position = offset.localPosition;
        if (!Mathf.Approximately(position.y, y))
            offset.localPosition = new Vector3(position.x, y, position.z);
    }

    Transform PitchPivot()
    {
        return cameraTransform.parent != null && cameraTransform.parent != transform ? cameraTransform.parent : cameraTransform;
    }
}
