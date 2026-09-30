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

    float xRotation = 0f;
    float nextPollTime;
    bool headsetInUse;
    bool modeInitialised;
    TrackedPoseDriver poseDriver;

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

        if (!DesktopMode || cameraTransform == null || SuppressLook)
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
        }

        if (poseDriver != null)
            poseDriver.enabled = !desktop;

        Cursor.lockState = desktop && !SuppressLook ? CursorLockMode.Locked : CursorLockMode.None;

        if (modeInitialised)
            Debug.Log($"[MouseLook] Switched to {(desktop ? "desktop (mouse + keyboard)" : "headset")} controls.", this);
        modeInitialised = true;
    }

    Transform PitchPivot()
    {
        return cameraTransform.parent != null && cameraTransform.parent != transform ? cameraTransform.parent : cameraTransform;
    }
}
