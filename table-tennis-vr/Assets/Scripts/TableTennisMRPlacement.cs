using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.OpenXR.Features.Meta;
using Unity.XR.CoreUtils;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

/// <summary>
/// Places the authoritative table and uses one Meta shared spatial anchor to
/// put every colocated headset into the host's Unity world coordinate frame.
/// </summary>
public sealed class TableTennisMRPlacement : NetworkBehaviour
{
    private const int EnhancedSpatialServicesDisabled = -1000169004;
    private const int MaximumAutomaticLoadAttempts = 5;
    private const float AnchorRetryDelay = 2f;

    [SerializeField] private Transform tableRoot;
    [SerializeField] private Camera xrCamera;
    [SerializeField] private float tableHeight = 0.76f;
    [SerializeField] private float placementDistance = 2.0f;
    [SerializeField] private float rotationSpeed = 90f;
    [SerializeField] private float rotationSmoothTime = 0.08f;
    [SerializeField] private bool hideTableUntilPlaced = false;
    [SerializeField] private bool tableStartsLocked = true;

    private readonly List<ARRaycastHit> raycastHits = new();
    private readonly HashSet<ulong> alignedClients = new();
    private readonly List<XRAnchor> loadedAnchors = new();

    private ARRaycastManager raycastManager;
    private ARPlaneManager planeManager;
    private ARCameraManager cameraManager;
    private ARAnchorManager anchorManager;
    private ARSession arSession;
    private XROrigin xrOrigin;
    private ARAnchor localSharedAnchor;
    private Coroutine anchorRetryRoutine;
    private TableTennisNetworkSession networkSession;
    private bool previousTrigger;
    private bool localCalibrationComplete;
    private bool anchorOperationInProgress;
    private bool sessionOriginCaptured;
    private Vector3 sessionOriginPosition;
    private Quaternion sessionOriginRotation;
    private int anchorOperationVersion;
    private int loadAttemptCount;
    private float targetYaw;
    private float yawVelocity;
    private bool rotationInitialized;
    private bool locallySelected;
    private XRGrabInteractable tableGrabInteractable;
    private Rigidbody tableBody;
    private Renderer[] tableRenderers;
    private Collider[] tableColliders;

    private readonly NetworkVariable<bool> placementConfirmed = new();
    private readonly NetworkVariable<Vector3> networkPosition = new();
    private readonly NetworkVariable<Quaternion> networkRotation = new();
    private readonly NetworkVariable<FixedString64Bytes> sharedAnchorGroupId = new();
    private readonly NetworkVariable<int> alignmentRevision = new();
    private readonly NetworkVariable<bool> allPlayersAligned = new();
    private readonly NetworkVariable<bool> tableLocked = new(true);

    public bool IsPlaced => placementConfirmed.Value;
    public bool IsTableLocked => tableLocked.Value;
    public bool IsLocallyCalibrated => localCalibrationComplete;
    public bool CanStartMatch => !IsSpawned ? IsPlaced : IsPlaced && allPlayersAligned.Value;

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

        networkSession = GetComponent<TableTennisNetworkSession>();
        tableRenderers = tableRoot.GetComponentsInChildren<Renderer>(true);
        tableColliders = GetTableColliders();
        ConfigureTableInteraction();
        ConfigureARComponents();
        Application.focusChanged += HandleApplicationFocusChanged;

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
        sharedAnchorGroupId.OnValueChanged += HandleSharedAnchorGroupChanged;
        alignmentRevision.OnValueChanged += HandleAlignmentRevisionChanged;
        tableLocked.OnValueChanged += HandleLockChanged;

        if (NetworkManager != null)
        {
            NetworkManager.OnClientConnectedCallback += HandleClientConnected;
            NetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;
        }

        if (IsServer)
        {
            networkPosition.Value = tableRoot.position;
            networkRotation.Value = tableRoot.rotation;
            RecalculateAllPlayersAligned();
            if (placementConfirmed.Value)
            {
                PublishSharedAnchorAsync();
            }
        }
        else
        {
            CaptureSessionOrigin();
            localCalibrationComplete = false;
        }

        ApplyNetworkPlacement();
        ApplyLockState();
        TryStartClientAlignment();
    }

    public override void OnNetworkDespawn()
    {
        placementConfirmed.OnValueChanged -= HandlePlacementChanged;
        networkPosition.OnValueChanged -= HandlePositionChanged;
        networkRotation.OnValueChanged -= HandleRotationChanged;
        sharedAnchorGroupId.OnValueChanged -= HandleSharedAnchorGroupChanged;
        alignmentRevision.OnValueChanged -= HandleAlignmentRevisionChanged;
        tableLocked.OnValueChanged -= HandleLockChanged;

        if (NetworkManager != null)
        {
            NetworkManager.OnClientConnectedCallback -= HandleClientConnected;
            NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
        }

        CancelAnchorOperations();
        CleanupLocalAnchor();
        RestoreSessionOrigin();
        alignedClients.Clear();
        localCalibrationComplete = false;
        SetTableVisible(!hideTableUntilPlaced);
    }

    private new void OnDestroy()
    {
        Application.focusChanged -= HandleApplicationFocusChanged;
        if (tableGrabInteractable != null)
        {
            tableGrabInteractable.selectEntered.RemoveListener(HandleSelectEntered);
            tableGrabInteractable.selectExited.RemoveListener(HandleSelectExited);
        }

        CancelAnchorOperations();
        CleanupLocalAnchor();
        RestoreSessionOrigin();
    }

    private void Update()
    {
        if (IsSpawned && !IsServer)
        {
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

    public void ConfirmPlacement() => TryPlaceFromView();

    public void ConfirmCurrentPlacement()
    {
        ConfirmPlacement(new Pose(tableRoot.position, tableRoot.rotation));
    }

    public void PlaceUsingFallback()
    {
        if (xrCamera == null || (IsSpawned && !IsServer))
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

    /// <summary>Retries publishing or resolving the current session's shared anchor.</summary>
    public void RetrySharedAlignment()
    {
        if (!IsSpawned)
        {
            return;
        }

        loadAttemptCount = 0;
        if (IsServer)
        {
            if (localCalibrationComplete && !sharedAnchorGroupId.Value.IsEmpty)
            {
                ReportAlignmentStatus(allPlayersAligned.Value
                    ? "Shared table aligned. Ready to start."
                    : "Table anchor shared. Waiting for Player 2 to align...");
                return;
            }
            if (IsPlaced)
            {
                PublishSharedAnchorAsync();
            }
        }
        else
        {
            if (localCalibrationComplete)
            {
                ReportAlignmentStatus("Shared table aligned. Ready to start.");
                return;
            }
            TryStartClientAlignment();
        }
    }

    public void ResetPlacement()
    {
        if (IsSpawned && !IsServer)
        {
            ReportAlignmentStatus("Only the host can reset the shared table.");
            return;
        }

        CancelAnchorOperations();
        CleanupLocalAnchor();
        alignedClients.Clear();
        placementConfirmed.Value = false;
        sharedAnchorGroupId.Value = default;
        allPlayersAligned.Value = false;
        localCalibrationComplete = false;
        rotationInitialized = false;
        tableLocked.Value = tableStartsLocked;
        SetTableVisible(!hideTableUntilPlaced);
        ApplyLockState();
    }

    public void ToggleTableLock()
    {
        if (IsSpawned && !IsServer)
        {
            ReportAlignmentStatus("Only the host can reposition the shared table.");
            return;
        }

        bool nextState = !IsTableLocked;
        TableTennisMatch match = GetComponent<TableTennisMatch>();
        if (!nextState && match != null && match.IsMatchActive)
        {
            RuntimeDiagnostics.LogWarning("Table unlock ignored while a match is active.");
            return;
        }

        tableLocked.Value = nextState;
        if (!nextState)
        {
            InvalidateSharedAlignment();
            if (!IsPlaced)
            {
                ConfirmPlacement(new Pose(tableRoot.position, tableRoot.rotation), false);
            }
        }
        else if (IsSpawned && IsServer && IsPlaced)
        {
            PublishSharedAnchorAsync();
        }
        ApplyLockState();
    }

    public void LockForMatch()
    {
        if (IsTableLocked || (IsSpawned && !IsServer))
        {
            return;
        }
        tableLocked.Value = true;
        ApplyLockState();
    }

    public void RotateTable(float input)
    {
        if (!IsPlaced || IsTableLocked || !locallySelected || tableRoot == null || Mathf.Abs(input) < 0.01f)
        {
            return;
        }

        if (!rotationInitialized)
        {
            targetYaw = tableRoot.eulerAngles.y;
            rotationInitialized = true;
        }

        targetYaw += input * rotationSpeed * Time.deltaTime;
        float smoothedYaw = Mathf.SmoothDampAngle(tableRoot.eulerAngles.y, targetYaw, ref yawVelocity, rotationSmoothTime);
        Quaternion rotation = Quaternion.Euler(0f, smoothedYaw, 0f);
        tableRoot.rotation = rotation;
        PublishTablePose(rotation);
    }

    private void ConfigureARComponents()
    {
        GameObject virtualEnvironment = GameObject.Find("Environment");
        if (virtualEnvironment != null)
        {
            foreach (Renderer renderer in virtualEnvironment.GetComponentsInChildren<Renderer>(true))
            {
                renderer.enabled = false;
            }
            foreach (Collider collider in virtualEnvironment.GetComponentsInChildren<Collider>(true))
            {
                collider.enabled = false;
            }
        }

        xrOrigin = FindFirstObjectByType<XROrigin>();
        GameObject arOriginObject = xrOrigin != null ? xrOrigin.gameObject : gameObject;
        arSession = FindFirstObjectByType<ARSession>();
        if (arSession == null)
        {
            GameObject sessionObject = new("MR AR Session");
            arSession = sessionObject.AddComponent<ARSession>();
        }

        raycastManager = arOriginObject.GetComponent<ARRaycastManager>();
        if (raycastManager == null)
        {
            raycastManager = arOriginObject.AddComponent<ARRaycastManager>();
        }
        planeManager = arOriginObject.GetComponent<ARPlaneManager>();
        if (planeManager == null)
        {
            planeManager = arOriginObject.AddComponent<ARPlaneManager>();
        }
        planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal;
        anchorManager = arOriginObject.GetComponent<ARAnchorManager>();
        if (anchorManager == null)
        {
            anchorManager = arOriginObject.AddComponent<ARAnchorManager>();
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
        if (xrCamera == null || (IsSpawned && !IsServer))
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

    private void ConfirmPlacement(Pose pose, bool publishSharedAnchor = true)
    {
        if (IsSpawned && !IsServer)
        {
            return;
        }

        Quaternion uprightRotation = Quaternion.Euler(0f, pose.rotation.eulerAngles.y, 0f);
        tableRoot.SetPositionAndRotation(pose.position, uprightRotation);
        networkPosition.Value = tableRoot.position;
        networkRotation.Value = tableRoot.rotation;
        placementConfirmed.Value = true;
        SetTableVisible(true);

        if (!IsSpawned)
        {
            localCalibrationComplete = true;
            return;
        }
        if (publishSharedAnchor)
        {
            PublishSharedAnchorAsync();
        }
    }

    private async void PublishSharedAnchorAsync()
    {
        if (!IsSpawned || !IsServer || !IsPlaced || anchorOperationInProgress)
        {
            return;
        }

        int operationVersion = ++anchorOperationVersion;
        anchorOperationInProgress = true;
        InvalidateSharedAlignment(false);
        ReportAlignmentStatus("Creating shared table anchor...");

        try
        {
            if (!TryGetMetaAnchorSubsystem(out MetaOpenXRAnchorSubsystem subsystem))
            {
                ReportAlignmentStatus("Shared anchors are unavailable on this device. Multiplayer is blocked.");
                return;
            }
            if (subsystem.isSharedAnchorsSupported != Supported.Supported)
            {
                ReportAlignmentStatus("This headset does not support Meta shared anchors.");
                return;
            }

            UnityEngine.XR.ARSubsystems.SerializableGuid groupId = new(Guid.NewGuid());
            subsystem.sharedAnchorsGroupId = groupId;
            CleanupLocalAnchor();
            Result<ARAnchor> addResult = await anchorManager.TryAddAnchorAsync(new Pose(networkPosition.Value, networkRotation.Value));
            if (!IsAnchorOperationCurrent(operationVersion))
            {
                if (addResult.value != null)
                {
                    anchorManager.TryRemoveAnchor(addResult.value);
                }
                return;
            }
            if (addResult.status.IsError() || addResult.value == null)
            {
                ReportAnchorError("Could not create the shared table anchor", addResult.status);
                return;
            }

            localSharedAnchor = addResult.value;
            ReportAlignmentStatus("Sharing table anchor with Player 2...");
            XRResultStatus shareStatus = await anchorManager.TryShareAnchorAsync(localSharedAnchor);
            if (!IsAnchorOperationCurrent(operationVersion))
            {
                return;
            }
            if (shareStatus.IsError())
            {
                ReportAnchorError("Could not share the table anchor", shareStatus);
                return;
            }

            alignedClients.Clear();
            alignedClients.Add(NetworkManager.LocalClientId);
            localCalibrationComplete = true;
            alignmentRevision.Value++;
            sharedAnchorGroupId.Value = new FixedString64Bytes(groupId.guid.ToString());
            RecalculateAllPlayersAligned();
            ReportAlignmentStatus(allPlayersAligned.Value
                ? "Shared table aligned. Ready to start."
                : "Table anchor shared. Waiting for Player 2 to align...");
        }
        catch (Exception exception)
        {
            ReportAlignmentStatus($"Shared anchor failed: {exception.Message}. Press Start Match to retry.");
            Debug.LogException(exception, this);
        }
        finally
        {
            if (operationVersion == anchorOperationVersion)
            {
                anchorOperationInProgress = false;
            }
        }
    }

    private void TryStartClientAlignment()
    {
        if (!IsSpawned || IsServer || anchorOperationInProgress || alignmentRevision.Value <= 0 || sharedAnchorGroupId.Value.IsEmpty)
        {
            return;
        }
        LoadSharedAnchorAsync(alignmentRevision.Value, ++anchorOperationVersion);
    }

    private async void LoadSharedAnchorAsync(int requestedRevision, int operationVersion)
    {
        anchorOperationInProgress = true;
        localCalibrationComplete = false;
        ApplyNetworkPlacement();
        loadAttemptCount++;
        ReportAlignmentStatus($"Locating shared table anchor... ({loadAttemptCount}/{MaximumAutomaticLoadAttempts})");

        try
        {
            if (!TryGetMetaAnchorSubsystem(out MetaOpenXRAnchorSubsystem subsystem))
            {
                ReportAlignmentStatus("Shared anchors are unavailable on this device. Multiplayer is blocked.");
                return;
            }
            if (subsystem.isSharedAnchorsSupported != Supported.Supported)
            {
                ReportAlignmentStatus("This headset does not support Meta shared anchors.");
                return;
            }
            if (!Guid.TryParse(sharedAnchorGroupId.Value.ToString(), out Guid groupGuid))
            {
                ReportAlignmentStatus("The shared anchor ID is invalid. Ask the host to replace the table.");
                return;
            }

            subsystem.sharedAnchorsGroupId = new UnityEngine.XR.ARSubsystems.SerializableGuid(groupGuid);
            loadedAnchors.Clear();
            XRResultStatus loadStatus = await anchorManager.TryLoadAllSharedAnchorsAsync(loadedAnchors, null);
            if (!IsClientOperationCurrent(requestedRevision, operationVersion))
            {
                return;
            }
            if (loadStatus.IsError())
            {
                ReportAnchorError("Could not load the shared table anchor", loadStatus);
                if (loadStatus.nativeStatusCode != EnhancedSpatialServicesDisabled)
                {
                    ScheduleAnchorRetry(requestedRevision);
                }
                return;
            }
            if (loadedAnchors.Count == 0)
            {
                ReportAlignmentStatus("The shared anchor is still propagating. Retrying...");
                ScheduleAnchorRetry(requestedRevision);
                return;
            }

            TrackableId anchorId = loadedAnchors[0].trackableId;
            localSharedAnchor = FindAnchor(anchorId);
            if (localSharedAnchor == null)
            {
                await Awaitable.NextFrameAsync();
                localSharedAnchor = FindAnchor(anchorId);
            }
            if (!IsClientOperationCurrent(requestedRevision, operationVersion) || localSharedAnchor == null)
            {
                if (localSharedAnchor == null)
                {
                    ReportAlignmentStatus("The shared anchor loaded without a trackable pose. Retrying...");
                    ScheduleAnchorRetry(requestedRevision);
                }
                return;
            }

            AlignOriginToSharedAnchor(localSharedAnchor.transform);
            localCalibrationComplete = true;
            ApplyNetworkPlacement();
            AcknowledgeAlignmentServerRpc(requestedRevision);
            ReportAlignmentStatus("Shared table aligned. Waiting for the host to start the match.");
        }
        catch (Exception exception)
        {
            ReportAlignmentStatus($"Shared anchor failed: {exception.Message}. Press Start Match to retry.");
            Debug.LogException(exception, this);
        }
        finally
        {
            if (operationVersion == anchorOperationVersion)
            {
                anchorOperationInProgress = false;
            }
        }
    }

    private void AlignOriginToSharedAnchor(Transform anchorTransform)
    {
        if (xrOrigin == null || anchorTransform == null)
        {
            throw new InvalidOperationException("XR Origin or loaded anchor pose is missing.");
        }

        float yawDelta = Mathf.DeltaAngle(anchorTransform.eulerAngles.y, networkRotation.Value.eulerAngles.y);
        Quaternion yawCorrection = Quaternion.Euler(0f, yawDelta, 0f);
        Transform originTransform = xrOrigin.transform;
        Vector3 correctedPosition = networkPosition.Value + yawCorrection * (originTransform.position - anchorTransform.position);
        originTransform.SetPositionAndRotation(correctedPosition, yawCorrection * originTransform.rotation);
    }

    private bool TryGetMetaAnchorSubsystem(out MetaOpenXRAnchorSubsystem subsystem)
    {
        subsystem = anchorManager != null ? anchorManager.subsystem as MetaOpenXRAnchorSubsystem : null;
        return anchorManager != null && anchorManager.enabled && subsystem != null;
    }

    private ARAnchor FindAnchor(TrackableId trackableId)
    {
        if (anchorManager == null)
        {
            return null;
        }
        foreach (ARAnchor anchor in anchorManager.trackables)
        {
            if (anchor.trackableId == trackableId)
            {
                return anchor;
            }
        }
        return null;
    }

    private void ScheduleAnchorRetry(int requestedRevision)
    {
        if (loadAttemptCount >= MaximumAutomaticLoadAttempts || requestedRevision != alignmentRevision.Value)
        {
            ReportAlignmentStatus("Table alignment failed. Press Start Match to retry.");
            return;
        }
        if (anchorRetryRoutine != null)
        {
            StopCoroutine(anchorRetryRoutine);
        }
        anchorRetryRoutine = StartCoroutine(RetryAnchorAfterDelay(requestedRevision));
    }

    private IEnumerator RetryAnchorAfterDelay(int requestedRevision)
    {
        yield return new WaitForSecondsRealtime(AnchorRetryDelay);
        anchorRetryRoutine = null;
        if (IsSpawned && !IsServer && requestedRevision == alignmentRevision.Value)
        {
            anchorOperationInProgress = false;
            TryStartClientAlignment();
        }
    }

    private bool IsAnchorOperationCurrent(int operationVersion)
    {
        return this != null && IsSpawned && IsServer && operationVersion == anchorOperationVersion;
    }

    private bool IsClientOperationCurrent(int requestedRevision, int operationVersion)
    {
        return this != null && IsSpawned && !IsServer && requestedRevision == alignmentRevision.Value && operationVersion == anchorOperationVersion;
    }

    private void InvalidateSharedAlignment(bool cancelCurrentOperation = true)
    {
        if (!IsSpawned || !IsServer)
        {
            return;
        }
        if (cancelCurrentOperation)
        {
            CancelAnchorOperations();
        }
        alignedClients.Clear();
        sharedAnchorGroupId.Value = default;
        allPlayersAligned.Value = false;
        localCalibrationComplete = false;
    }

    private void CancelAnchorOperations()
    {
        anchorOperationVersion++;
        anchorOperationInProgress = false;
        if (anchorRetryRoutine != null)
        {
            StopCoroutine(anchorRetryRoutine);
            anchorRetryRoutine = null;
        }
    }

    private void CleanupLocalAnchor()
    {
        if (localSharedAnchor != null && anchorManager != null)
        {
            anchorManager.TryRemoveAnchor(localSharedAnchor);
        }
        localSharedAnchor = null;
        loadedAnchors.Clear();
    }

    private void CaptureSessionOrigin()
    {
        if (sessionOriginCaptured || xrOrigin == null)
        {
            return;
        }
        sessionOriginPosition = xrOrigin.transform.position;
        sessionOriginRotation = xrOrigin.transform.rotation;
        sessionOriginCaptured = true;
    }

    private void RestoreSessionOrigin()
    {
        if (!sessionOriginCaptured || xrOrigin == null)
        {
            return;
        }
        xrOrigin.transform.SetPositionAndRotation(sessionOriginPosition, sessionOriginRotation);
        sessionOriginCaptured = false;
    }

    private void ReportAnchorError(string prefix, XRResultStatus status)
    {
        if (status.nativeStatusCode == EnhancedSpatialServicesDisabled)
        {
            ReportAlignmentStatus("Enable Enhanced Spatial Services in Quest settings, then press Start Match to retry.");
            return;
        }
        ReportAlignmentStatus($"{prefix} (error {status.nativeStatusCode}). Press Start Match to retry.");
    }

    private void ReportAlignmentStatus(string message)
    {
        if (networkSession != null)
        {
            networkSession.ReportStatus(message);
        }
        else
        {
            RuntimeDiagnostics.Log($"[Alignment] {message}");
        }
    }

    private bool ReadPlacementInput()
    {
        return Application.isEditor && UnityEngine.InputSystem.Keyboard.current != null &&
            UnityEngine.InputSystem.Keyboard.current.spaceKey.isPressed;
    }

    private void UpdateTableRotation()
    {
        if (!IsPlaced || IsTableLocked || !locallySelected)
        {
            return;
        }

        float keyboard = 0f;
        if (UnityEngine.InputSystem.Keyboard.current != null)
        {
            if (UnityEngine.InputSystem.Keyboard.current.aKey.isPressed) keyboard -= 1f;
            if (UnityEngine.InputSystem.Keyboard.current.dKey.isPressed) keyboard += 1f;
        }
        InputDevice device = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        Vector2 stick = Vector2.zero;
        if (device.isValid)
        {
            device.TryGetFeatureValue(CommonUsages.primary2DAxis, out stick);
        }
        RotateTable(Mathf.Abs(stick.x) > Mathf.Abs(keyboard) ? stick.x : keyboard);
    }

    private void LateUpdate()
    {
        if (!locallySelected || IsTableLocked || tableRoot == null || (IsSpawned && !IsServer))
        {
            return;
        }
        Quaternion uprightRotation = Quaternion.Euler(0f, tableRoot.eulerAngles.y, 0f);
        tableRoot.rotation = uprightRotation;
        PublishTablePose(uprightRotation);
    }

    private void ConfigureTableInteraction()
    {
        tableBody = tableRoot.GetComponent<Rigidbody>();
        if (tableBody == null)
        {
            tableBody = tableRoot.gameObject.AddComponent<Rigidbody>();
        }
        tableBody.isKinematic = true;
        tableBody.useGravity = false;
        tableGrabInteractable = tableRoot.GetComponent<XRGrabInteractable>();
        if (tableGrabInteractable == null)
        {
            tableGrabInteractable = tableRoot.gameObject.AddComponent<XRGrabInteractable>();
        }
        tableGrabInteractable.useDynamicAttach = true;
        tableGrabInteractable.snapToColliderVolume = false;
        tableGrabInteractable.attachEaseInTime = 0f;
        tableGrabInteractable.trackPosition = true;
        tableGrabInteractable.trackRotation = false;
        tableGrabInteractable.trackScale = false;
        tableGrabInteractable.throwOnDetach = false;
        tableGrabInteractable.movementType = XRBaseInteractable.MovementType.Instantaneous;
        tableGrabInteractable.colliders.Clear();
        foreach (Collider item in tableColliders)
        {
            tableGrabInteractable.colliders.Add(item);
        }
        tableGrabInteractable.selectEntered.AddListener(HandleSelectEntered);
        tableGrabInteractable.selectExited.AddListener(HandleSelectExited);
        ApplyLockState();
    }

    private Collider[] GetTableColliders()
    {
        List<Collider> result = new();
        foreach (Collider item in tableRoot.GetComponentsInChildren<Collider>(true))
        {
            Transform owner = item.transform;
            bool isTableGeometry = owner == tableRoot ||
                (owner.parent == tableRoot && (owner.name == "Table Top" || owner.name == "Net" || owner.name.StartsWith("Table Leg")));
            if (isTableGeometry)
            {
                result.Add(item);
            }
        }
        return result.ToArray();
    }

    private void HandleSelectEntered(SelectEnterEventArgs _)
    {
        if (IsTableLocked || (IsSpawned && !IsServer))
        {
            return;
        }
        locallySelected = true;
        rotationInitialized = false;
        InvalidateSharedAlignment();
    }

    private void HandleSelectExited(SelectExitEventArgs _)
    {
        if (!locallySelected)
        {
            return;
        }
        locallySelected = false;
        rotationInitialized = false;
        PublishTablePose(tableRoot.rotation);
        if (IsSpawned && IsServer && IsPlaced)
        {
            PublishSharedAnchorAsync();
        }
    }

    private void ApplyLockState()
    {
        if (tableGrabInteractable == null)
        {
            return;
        }
        bool canManipulate = !IsTableLocked && (!IsSpawned || IsServer);
        if (!canManipulate && locallySelected)
        {
            tableGrabInteractable.interactionManager?.CancelInteractableSelection((IXRSelectInteractable)tableGrabInteractable);
            locallySelected = false;
            rotationInitialized = false;
        }
        tableGrabInteractable.enabled = canManipulate;
    }

    private void HandleLockChanged(bool _, bool __) => ApplyLockState();

    private void PublishTablePose(Quaternion rotation)
    {
        if (tableRoot == null || IsTableLocked || (IsSpawned && !IsServer))
        {
            return;
        }
        networkPosition.Value = tableRoot.position;
        networkRotation.Value = rotation;
    }

    private void ApplyNetworkPlacement()
    {
        if (tableRoot == null || !placementConfirmed.Value)
        {
            return;
        }
        if (!locallySelected)
        {
            tableRoot.SetPositionAndRotation(networkPosition.Value, networkRotation.Value);
        }
        SetTableVisible(!IsSpawned || IsServer || localCalibrationComplete);
    }

    private void HandlePlacementChanged(bool _, bool __) => ApplyNetworkPlacement();
    private void HandlePositionChanged(Vector3 _, Vector3 __) => ApplyNetworkPlacement();
    private void HandleRotationChanged(Quaternion _, Quaternion __) => ApplyNetworkPlacement();

    private void HandleSharedAnchorGroupChanged(FixedString64Bytes _, FixedString64Bytes current)
    {
        if (IsServer)
        {
            return;
        }
        CancelAnchorOperations();
        CleanupLocalAnchor();
        localCalibrationComplete = false;
        ApplyNetworkPlacement();
        if (!current.IsEmpty)
        {
            loadAttemptCount = 0;
            TryStartClientAlignment();
        }
    }

    private void HandleAlignmentRevisionChanged(int _, int __)
    {
        if (!IsServer)
        {
            loadAttemptCount = 0;
            TryStartClientAlignment();
        }
    }

    private void SetTableVisible(bool visible)
    {
        foreach (Renderer item in tableRenderers) item.enabled = visible;
        foreach (Collider item in tableColliders) item.enabled = visible;
    }

    private void HandleClientConnected(ulong clientId)
    {
        if (!IsServer)
        {
            return;
        }
        if (clientId != NetworkManager.LocalClientId)
        {
            alignedClients.Remove(clientId);
        }
        RecalculateAllPlayersAligned();
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        if (!IsServer)
        {
            return;
        }
        alignedClients.Remove(clientId);
        RecalculateAllPlayersAligned();
    }

    private void RecalculateAllPlayersAligned()
    {
        if (!IsServer || NetworkManager == null)
        {
            return;
        }
        bool ready = IsPlaced && !sharedAnchorGroupId.Value.IsEmpty;
        foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
        {
            if (!alignedClients.Contains(clientId))
            {
                ready = false;
                break;
            }
        }
        allPlayersAligned.Value = ready;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void AcknowledgeAlignmentServerRpc(int revision, RpcParams rpcParams = default)
    {
        if (revision != alignmentRevision.Value || sharedAnchorGroupId.Value.IsEmpty)
        {
            return;
        }
        alignedClients.Add(rpcParams.Receive.SenderClientId);
        RecalculateAllPlayersAligned();
        if (allPlayersAligned.Value)
        {
            ReportAlignmentStatus("Both headsets are aligned. Ready to start.");
        }
    }

    private void HandleApplicationFocusChanged(bool hasFocus)
    {
        if (hasFocus && IsSpawned && !localCalibrationComplete)
        {
            RetrySharedAlignment();
        }
    }
}
