using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Unity.XR.CoreUtils;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

/// <summary>
/// Places the virtual table in the real room and keeps the placement state
/// authoritative on the host. AR Foundation is optional in the Editor; the
/// keyboard/controller fallback makes the flow testable without a headset.
/// </summary>
public sealed class TableTennisMRPlacement : NetworkBehaviour
{
    [SerializeField] private Transform tableRoot;
    [SerializeField] private Camera xrCamera;
    [SerializeField] private float tableHeight = 0.76f;
    [SerializeField] private float placementDistance = 2.0f;
    [SerializeField] private float rotationSpeed = 90f;
    [SerializeField] private float rotationSmoothTime = 0.08f;
    [SerializeField] private bool hideTableUntilPlaced = false;

    private readonly List<ARRaycastHit> raycastHits = new();
    private ARRaycastManager raycastManager;
    private ARPlaneManager planeManager;
    private ARCameraManager cameraManager;
    private ARSession arSession;
    private ARAnchor tableAnchor;
    private bool previousTrigger;
    private bool previousCalibrationInput;
    private bool localCalibrationComplete;
    private float targetYaw;
    private float yawVelocity;
    private bool rotationInitialized;
    private Renderer[] tableRenderers;
    private Collider[] tableColliders;

    private readonly NetworkVariable<bool> placementConfirmed = new();
    private readonly NetworkVariable<Vector3> networkPosition = new();
    private readonly NetworkVariable<Quaternion> networkRotation = new();
    private readonly NetworkVariable<int> calibratedPlayers = new();

    public bool IsPlaced => placementConfirmed.Value;
    public bool IsLocallyCalibrated => localCalibrationComplete;
    public bool CanStartMatch => IsPlaced && calibratedPlayers.Value >= ConnectedPlayerCount();

    private void Awake()
    {
        if (tableRoot == null)
        {
            tableRoot = transform;
        }

        if (xrCamera == null)
        {
            xrCamera = Camera.main;
        }

        tableRenderers = tableRoot.GetComponentsInChildren<Renderer>(true);
        tableColliders = tableRoot.GetComponentsInChildren<Collider>(true);
        ConfigureARComponents();

        if (hideTableUntilPlaced)
        {
            SetTableVisible(false);
        }
    }

    public override void OnNetworkSpawn()
    {
        placementConfirmed.OnValueChanged += HandlePlacementChanged;
        networkPosition.OnValueChanged += HandlePositionChanged;
        networkRotation.OnValueChanged += HandleRotationChanged;
        ApplyNetworkPlacement();
    }

    private new void OnDestroy()
    {
        placementConfirmed.OnValueChanged -= HandlePlacementChanged;
        networkPosition.OnValueChanged -= HandlePositionChanged;
        networkRotation.OnValueChanged -= HandleRotationChanged;
    }

    private void Update()
    {
        if (IsSpawned && !IsServer)
        {
            bool calibrationInput = ReadCalibrationInput();
            if (calibrationInput && !previousCalibrationInput)
            {
                MarkCalibrationComplete();
            }
            previousCalibrationInput = calibrationInput;
            return;
        }

        UpdateTableRotation();

        bool triggerPressed = ReadPlacementInput();
        if (triggerPressed && !previousTrigger && !IsPlaced)
        {
            TryPlaceFromView();
        }
        previousTrigger = triggerPressed;
    }

    /// <summary>Can be wired to the MR setup panel's Place/Confirm button.</summary>
    public void ConfirmPlacement()
    {
        TryPlaceFromView();
    }

    /// <summary>Places the table in front of the camera when no plane is available.</summary>
    public void PlaceUsingFallback()
    {
        if (xrCamera == null)
        {
            return;
        }

        Vector3 forward = Vector3.ProjectOnPlane(xrCamera.transform.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.01f)
        {
            forward = Vector3.forward;
        }

        Vector3 position = xrCamera.transform.position + forward * placementDistance;
        position.y = Mathf.Max(0f, position.y - tableHeight);
        ConfirmPlacement(new Pose(position, Quaternion.LookRotation(forward, Vector3.up)));
    }

    /// <summary>Marks this headset ready after the joiner has aligned the calibration marker.</summary>
    public void MarkCalibrationComplete()
    {
        if (localCalibrationComplete || !IsPlaced)
        {
            return;
        }

        localCalibrationComplete = true;
        if (IsSpawned)
        {
            MarkCalibrationCompleteServerRpc();
        }
        else
        {
            calibratedPlayers.Value = 1;
        }
    }

    public void ResetPlacement()
    {
        if (IsSpawned && !IsServer)
        {
            return;
        }

        placementConfirmed.Value = false;
        calibratedPlayers.Value = 0;
        localCalibrationComplete = false;
        rotationInitialized = false;
        SetTableVisible(!hideTableUntilPlaced);
    }

    /// <summary>
    /// Rotates the placed table continuously around its vertical axis. The
    /// input is analog and smoothed, so it does not snap to fixed angles.
    /// </summary>
    public void RotateTable(float input)
    {
        if (!IsPlaced || tableRoot == null || Mathf.Abs(input) < 0.01f)
        {
            return;
        }

        if (!rotationInitialized)
        {
            targetYaw = tableRoot.eulerAngles.y;
            rotationInitialized = true;
        }

        targetYaw += input * rotationSpeed * Time.deltaTime;
        float smoothedYaw = Mathf.SmoothDampAngle(
            tableRoot.eulerAngles.y,
            targetYaw,
            ref yawVelocity,
            rotationSmoothTime);
        Quaternion rotation = Quaternion.Euler(0f, smoothedYaw, 0f);
        tableRoot.rotation = rotation;

        if (IsSpawned && IsServer)
        {
            networkRotation.Value = rotation;
        }
    }

    private void ConfigureARComponents()
    {
        GameObject virtualEnvironment = GameObject.Find("Environment");
        if (virtualEnvironment != null)
        {
            // Keep the template floor colliders active so the player and
            // teleport system still have a safety floor in MR. Hide only the
            // visual meshes; passthrough supplies the real room background.
            foreach (Renderer renderer in virtualEnvironment.GetComponentsInChildren<Renderer>(true))
            {
                renderer.enabled = false;
            }
        }

        GameObject arOriginObject = FindFirstObjectByType<XROrigin>()?.gameObject ?? gameObject;
        arSession = FindFirstObjectByType<ARSession>();
        if (arSession == null)
        {
            GameObject sessionObject = new GameObject("MR AR Session");
            arSession = sessionObject.AddComponent<ARSession>();
        }

        if (raycastManager == null)
        {
            raycastManager = arOriginObject.GetComponent<ARRaycastManager>();
            if (raycastManager == null)
            {
                raycastManager = arOriginObject.AddComponent<ARRaycastManager>();
            }
        }

        if (planeManager == null)
        {
            planeManager = arOriginObject.GetComponent<ARPlaneManager>();
            if (planeManager == null)
            {
                planeManager = arOriginObject.AddComponent<ARPlaneManager>();
            }
            planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal;
        }

        if (xrCamera != null && xrCamera.GetComponent<ARCameraBackground>() == null)
        {
            xrCamera.gameObject.AddComponent<ARCameraBackground>();
        }

        if (xrCamera != null)
        {
            cameraManager = xrCamera.GetComponent<ARCameraManager>();
            if (cameraManager == null)
            {
                cameraManager = xrCamera.gameObject.AddComponent<ARCameraManager>();
            }
            cameraManager.enabled = true;

            // Passthrough is composited behind the camera image. A skybox or
            // opaque clear color hides it even when the Meta layer is running.
            xrCamera.clearFlags = CameraClearFlags.SolidColor;
            Color transparent = xrCamera.backgroundColor;
            transparent.a = 0f;
            xrCamera.backgroundColor = transparent;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        const string scenePermission = "com.oculus.permission.USE_SCENE";
        if (!Permission.HasUserAuthorizedPermission(scenePermission))
        {
            planeManager.enabled = false;
            Permission.RequestUserPermission(scenePermission);
            StartCoroutine(EnablePlaneManagerAfterPermission(scenePermission));
        }
#endif
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private IEnumerator EnablePlaneManagerAfterPermission(string permission)
    {
        while (!Permission.HasUserAuthorizedPermission(permission))
        {
            yield return null;
        }

        if (planeManager != null)
        {
            planeManager.enabled = true;
        }
    }
#endif

    private void TryPlaceFromView()
    {
        if (xrCamera == null)
        {
            return;
        }

        Vector2 screenCenter = new(Screen.width * 0.5f, Screen.height * 0.5f);
        if (raycastManager != null && raycastManager.Raycast(screenCenter, raycastHits, TrackableType.PlaneWithinPolygon))
        {
            Pose hitPose = raycastHits[0].pose;
            Vector3 forward = Vector3.ProjectOnPlane(xrCamera.transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.01f)
            {
                forward = Vector3.forward;
            }
            ConfirmPlacement(new Pose(hitPose.position, Quaternion.LookRotation(forward, Vector3.up)));
            return;
        }

        PlaceUsingFallback();
    }

    private void ConfirmPlacement(Pose pose)
    {
        if (IsSpawned && !IsServer)
        {
            return;
        }

        tableRoot.SetPositionAndRotation(pose.position, pose.rotation);
        tableAnchor = tableRoot.GetComponent<ARAnchor>();
        if (tableAnchor == null)
        {
            tableAnchor = tableRoot.gameObject.AddComponent<ARAnchor>();
        }
        networkPosition.Value = pose.position;
        networkRotation.Value = pose.rotation;
        placementConfirmed.Value = true;
        calibratedPlayers.Value = 1;
        localCalibrationComplete = true;
        SetTableVisible(true);
    }

    private bool ReadPlacementInput()
    {
        bool keyboard = UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.spaceKey.isPressed;
        UnityEngine.XR.InputDevice device = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        bool trigger = false;
        bool controller = device.isValid && device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out trigger) && trigger;
        return keyboard || controller;
    }

    private bool ReadCalibrationInput()
    {
        bool keyboard = UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.cKey.isPressed;
        UnityEngine.XR.InputDevice device = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        bool primary = false;
        bool controller = device.isValid && device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out primary) && primary;
        return keyboard || controller;
    }

    private void UpdateTableRotation()
    {
        if (!IsPlaced)
        {
            return;
        }

        float keyboard = 0f;
        if (UnityEngine.InputSystem.Keyboard.current != null)
        {
            if (UnityEngine.InputSystem.Keyboard.current.aKey.isPressed)
            {
                keyboard -= 1f;
            }
            if (UnityEngine.InputSystem.Keyboard.current.dKey.isPressed)
            {
                keyboard += 1f;
            }
        }

        InputDevice device = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        Vector2 stick = Vector2.zero;
        if (device.isValid)
        {
            device.TryGetFeatureValue(CommonUsages.primary2DAxis, out stick);
        }

        float input = Mathf.Abs(stick.x) > Mathf.Abs(keyboard) ? stick.x : keyboard;
        RotateTable(input);
    }

    private void ApplyNetworkPlacement()
    {
        if (tableRoot == null || !placementConfirmed.Value)
        {
            return;
        }

        tableRoot.SetPositionAndRotation(networkPosition.Value, networkRotation.Value);
        SetTableVisible(true);
    }

    private void HandlePlacementChanged(bool _, bool __)
    {
        ApplyNetworkPlacement();
    }

    private void HandlePositionChanged(Vector3 _, Vector3 __)
    {
        ApplyNetworkPlacement();
    }

    private void HandleRotationChanged(Quaternion _, Quaternion __)
    {
        ApplyNetworkPlacement();
    }

    private void SetTableVisible(bool visible)
    {
        foreach (Renderer item in tableRenderers)
        {
            item.enabled = visible;
        }

        foreach (Collider item in tableColliders)
        {
            item.enabled = visible;
        }
    }

    private int ConnectedPlayerCount()
    {
        return IsSpawned && NetworkManager.Singleton != null ? NetworkManager.Singleton.ConnectedClientsIds.Count : 1;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void MarkCalibrationCompleteServerRpc()
    {
        calibratedPlayers.Value = Mathf.Min(ConnectedPlayerCount(), calibratedPlayers.Value + 1);
    }
}
