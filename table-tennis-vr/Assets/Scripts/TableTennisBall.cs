using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public sealed class TableTennisBall : NetworkBehaviour
{
    private const ulong NoGrabber = ulong.MaxValue;

    [SerializeField] private float maxSpeed = 18f;
    [SerializeField] private BoxCollider hostTransferZone;
    [SerializeField] private BoxCollider joiningPlayerTransferZone;

    private readonly NetworkVariable<ulong> activeGrabber = new(NoGrabber);
    private Rigidbody ballBody;
    private XRGrabInteractable grabInteractable;
    private XRSelectFilterDelegate ownershipFilter;
    private bool locallySelected;
    private bool frozen;
    private bool grabRequestPending;
    private bool authorityHandoffPending;
    private bool handoffSampleValid;
    private Vector3 previousHandoffPosition;
    private Quaternion previousHandoffRotation;
    private Vector3 previousHandoffVelocity;
    private Vector3 previousHandoffAngularVelocity;
    private double previousHandoffServerTime;
    private uint lastAuthoritySequence;

    public Vector3 LinearVelocity => ballBody == null ? Vector3.zero : ballBody.linearVelocity;
    public Vector3 AngularVelocity => ballBody == null ? Vector3.zero : ballBody.angularVelocity;
    public bool IsFrozen => frozen;
    public bool IsPhysicsAuthority => !IsSpawned || IsOwner;
    public bool IsKinematic => ballBody != null && ballBody.isKinematic;
    public ulong ActiveGrabber => activeGrabber.Value;
    public uint LastAuthoritySequence => lastAuthoritySequence;

    private void Awake()
    {
        ballBody = GetComponent<Rigidbody>();
        grabInteractable = GetComponent<XRGrabInteractable>();
        ballBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        ballBody.interpolation = RigidbodyInterpolation.Interpolate;
        ballBody.solverIterations = 10;
        ballBody.solverVelocityIterations = 4;

        if (grabInteractable != null)
        {
            ownershipFilter = new XRSelectFilterDelegate(CanSelect);
            grabInteractable.selectFilters.Add(ownershipFilter);
            grabInteractable.selectEntered.AddListener(HandleSelectEntered);
            grabInteractable.selectExited.AddListener(HandleSelectExited);
        }
    }

    private void Start()
    {
        ApplyInteractionAuthority();
        RestoreOfflinePhysics();
    }

    public override void OnNetworkSpawn()
    {
        activeGrabber.OnValueChanged += HandleGrabberChanged;
        if (IsServer && NetworkManager != null)
        {
            NetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;
        }

        grabRequestPending = false;
        ResetHandoffTracking();
        ApplyInteractionAuthority();
    }

    public override void OnGainedOwnership()
    {
        grabRequestPending = false;
        ResetHandoffTracking();
        ApplyInteractionAuthority();
    }

    public override void OnLostOwnership()
    {
        grabRequestPending = false;
        ResetHandoffTracking();
        if (locallySelected && grabInteractable != null)
        {
            grabInteractable.interactionManager?.CancelInteractableSelection(
                (IXRSelectInteractable)grabInteractable);
            locallySelected = false;
        }

        ApplyInteractionAuthority();
    }

    public override void OnNetworkDespawn()
    {
        activeGrabber.OnValueChanged -= HandleGrabberChanged;
        if (NetworkManager != null)
        {
            NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
        }

        locallySelected = false;
        grabRequestPending = false;
        ResetHandoffTracking();
        ApplyInteractionAuthority();
    }

    public override void OnDestroy()
    {
        if (grabInteractable != null)
        {
            if (ownershipFilter != null)
            {
                grabInteractable.selectFilters.Remove(ownershipFilter);
            }
            grabInteractable.selectEntered.RemoveListener(HandleSelectEntered);
            grabInteractable.selectExited.RemoveListener(HandleSelectExited);
        }

        base.OnDestroy();
    }

    private void Update()
    {
        RestoreOfflinePhysics();
    }

    private void FixedUpdate()
    {
        if (!IsPhysicsAuthority)
        {
            return;
        }

        if (frozen)
        {
            ballBody.linearVelocity = Vector3.zero;
            ballBody.angularVelocity = Vector3.zero;
            return;
        }

        ballBody.linearVelocity = Vector3.ClampMagnitude(ballBody.linearVelocity, maxSpeed);

        if (locallySelected)
        {
            handoffSampleValid = false;
        }
        else
        {
            TryHandoffAcrossNet();
        }
    }

    public void ResetForServe(Vector3 position)
    {
        if (IsSpawned && !IsServer)
        {
            return;
        }

        frozen = false;
        activeGrabber.Value = NoGrabber;
        ResetHandoffTracking();
        if (IsSpawned && NetworkObject.OwnerClientId != NetworkManager.ServerClientId)
        {
            NetworkObject.RemoveOwnership();
        }

        ApplyState(position, Quaternion.identity, Vector3.zero, Vector3.zero, ++lastAuthoritySequence);
    }

    public void DebugLaunch(Vector3 velocity)
    {
        if (IsSpawned)
        {
            return;
        }

        frozen = false;
        ballBody.linearVelocity = Vector3.ClampMagnitude(velocity, maxSpeed);
    }

    public void DebugSetFrozen(bool value)
    {
        if (IsSpawned)
        {
            return;
        }

        frozen = value;
        if (frozen)
        {
            ballBody.linearVelocity = Vector3.zero;
            ballBody.angularVelocity = Vector3.zero;
        }
    }

    private bool CanSelect(
        UnityEngine.XR.Interaction.Toolkit.Interactors.IXRSelectInteractor _,
        IXRSelectInteractable __)
    {
        if (!IsSpawned)
        {
            return true;
        }

        if (NetworkManager == null || !NetworkManager.IsListening)
        {
            return true;
        }

        ulong localClientId = NetworkManager.LocalClientId;
        if (IsOwner && (activeGrabber.Value == NoGrabber || activeGrabber.Value == localClientId))
        {
            return true;
        }

        if (!grabRequestPending && activeGrabber.Value == NoGrabber)
        {
            grabRequestPending = true;
            if (IsServer)
            {
                GiveGrabAuthority(localClientId);
            }
            else
            {
                RequestGrabServerRpc();
            }
        }

        return false;
    }

    private void ApplyInteractionAuthority()
    {
        if (grabInteractable != null)
        {
            // Selection filters pause a remote grab until ownership arrives.
            // Keeping this enabled lets both peers initiate that handshake.
            grabInteractable.enabled = true;
        }
    }

    private void HandleSelectEntered(SelectEnterEventArgs _)
    {
        locallySelected = true;
        grabRequestPending = false;
        ResetHandoffTracking();
        if (!IsSpawned || NetworkManager == null)
        {
            return;
        }

        if (IsServer)
        {
            ConfirmGrab(NetworkManager.LocalClientId);
        }
        else
        {
            ConfirmGrabServerRpc();
        }
    }

    private void HandleSelectExited(SelectExitEventArgs _)
    {
        locallySelected = false;
        ResetHandoffTracking();
        // Synchronize the Rigidbody before physics resumes so release starts at
        // the controller pose and keeps the throw velocity XRI applies afterward.
        ballBody.position = transform.position;
        ballBody.rotation = transform.rotation;
        Physics.SyncTransforms();
        if (IsSpawned)
        {
            StartCoroutine(ClearGrabberAfterDetach());
        }
    }

    private IEnumerator ClearGrabberAfterDetach()
    {
        // XRI writes throw velocity in LateUpdate. Waiting through the end of
        // the frame preserves that velocity and avoids a visible release rewind.
        yield return new WaitForEndOfFrame();

        if (!IsSpawned || NetworkManager == null)
        {
            yield break;
        }

        if (IsServer)
        {
            ClearGrabber(NetworkManager.LocalClientId);
        }
        else
        {
            ClearGrabberServerRpc();
        }
    }

    private void HandleGrabberChanged(ulong _, ulong newGrabber)
    {
        if (!locallySelected || !IsSpawned || NetworkManager == null ||
            newGrabber == NetworkManager.LocalClientId)
        {
            return;
        }

        grabInteractable.interactionManager?.CancelInteractableSelection(
            (IXRSelectInteractable)grabInteractable);
        locallySelected = false;
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        if (!IsServer)
        {
            return;
        }

        if (activeGrabber.Value == clientId)
        {
            activeGrabber.Value = NoGrabber;
        }

        if (NetworkObject.OwnerClientId == clientId)
        {
            NetworkObject.RemoveOwnership();
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestGrabServerRpc(RpcParams rpcParams = default)
    {
        GiveGrabAuthority(rpcParams.Receive.SenderClientId);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void ConfirmGrabServerRpc(RpcParams rpcParams = default)
    {
        ConfirmGrab(rpcParams.Receive.SenderClientId);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void ClearGrabberServerRpc(RpcParams rpcParams = default)
    {
        ClearGrabber(rpcParams.Receive.SenderClientId);
    }

    private void GiveGrabAuthority(ulong clientId)
    {
        if (!IsServer || NetworkObject == null || activeGrabber.Value != NoGrabber)
        {
            return;
        }

        if (NetworkObject.OwnerClientId != clientId)
        {
            NetworkObject.ChangeOwnership(clientId);
        }
    }

    private void ConfirmGrab(ulong clientId)
    {
        if (!IsServer || NetworkObject == null ||
            (activeGrabber.Value != NoGrabber && activeGrabber.Value != clientId))
        {
            return;
        }

        if (NetworkObject.OwnerClientId != clientId)
        {
            NetworkObject.ChangeOwnership(clientId);
        }
        activeGrabber.Value = clientId;
    }

    private void ClearGrabber(ulong clientId)
    {
        if (IsServer && activeGrabber.Value == clientId)
        {
            // Retain ownership after release. The releasing peer remains the
            // physics writer until the ball crosses the net.
            activeGrabber.Value = NoGrabber;
        }
    }

    private void TryHandoffAcrossNet()
    {
        bool releasedByLocalOwner = NetworkManager != null &&
            activeGrabber.Value == NetworkManager.LocalClientId && !locallySelected;
        if (!IsSpawned || authorityHandoffPending ||
            (activeGrabber.Value != NoGrabber && !releasedByLocalOwner))
        {
            return;
        }

        if (hostTransferZone == null || joiningPlayerTransferZone == null)
        {
            return;
        }

        double serverTime = NetworkManager.ServerTime.Time;
        Vector3 position = ballBody.position;
        Quaternion rotation = ballBody.rotation;
        Vector3 velocity = ballBody.linearVelocity;
        Vector3 angularVelocity = ballBody.angularVelocity;

        if (!handoffSampleValid)
        {
            CacheHandoffSample(position, rotation, velocity, angularVelocity, serverTime);

            ulong initialOwner = GetOwnerForPosition(position);
            if (initialOwner != NetworkObject.OwnerClientId)
            {
                RequestAuthorityHandoff(
                    initialOwner, position, rotation, velocity, angularVelocity, serverTime);
            }
            return;
        }

        ulong desiredOwner = GetOwnerForPosition(position);
        if (desiredOwner != NetworkObject.OwnerClientId)
        {
            BoxCollider destinationZone = desiredOwner == NetworkManager.ServerClientId
                ? hostTransferZone
                : joiningPlayerTransferZone;
            float crossingFraction = TryGetZoneEntryFraction(
                destinationZone, previousHandoffPosition, position, out float entryFraction)
                ? entryFraction
                : 1f;

            RequestAuthorityHandoff(
                desiredOwner,
                Vector3.Lerp(previousHandoffPosition, position, crossingFraction),
                Quaternion.Slerp(previousHandoffRotation, rotation, crossingFraction),
                Vector3.Lerp(previousHandoffVelocity, velocity, crossingFraction),
                Vector3.Lerp(previousHandoffAngularVelocity, angularVelocity, crossingFraction),
                previousHandoffServerTime +
                    (serverTime - previousHandoffServerTime) * crossingFraction);
            return;
        }

        CacheHandoffSample(position, rotation, velocity, angularVelocity, serverTime);
    }

    private ulong GetOwnerForPosition(Vector3 position)
    {
        if (Contains(hostTransferZone, position))
        {
            return NetworkManager.ServerClientId;
        }

        if (Contains(joiningPlayerTransferZone, position))
        {
            return GetFirstRemoteClientId();
        }

        return NetworkObject.OwnerClientId;
    }

    private void CacheHandoffSample(
        Vector3 position,
        Quaternion rotation,
        Vector3 velocity,
        Vector3 angularVelocity,
        double serverTime)
    {
        previousHandoffPosition = position;
        previousHandoffRotation = rotation;
        previousHandoffVelocity = velocity;
        previousHandoffAngularVelocity = angularVelocity;
        previousHandoffServerTime = serverTime;
        handoffSampleValid = true;
    }

    private static bool Contains(BoxCollider zone, Vector3 worldPosition)
    {
        Vector3 localPosition = zone.transform.InverseTransformPoint(worldPosition) - zone.center;
        Vector3 halfSize = zone.size * 0.5f;
        return Mathf.Abs(localPosition.x) <= halfSize.x &&
            Mathf.Abs(localPosition.y) <= halfSize.y &&
            Mathf.Abs(localPosition.z) <= halfSize.z;
    }

    private static bool TryGetZoneEntryFraction(
        BoxCollider zone,
        Vector3 worldStart,
        Vector3 worldEnd,
        out float entryFraction)
    {
        Vector3 start = zone.transform.InverseTransformPoint(worldStart) - zone.center;
        Vector3 end = zone.transform.InverseTransformPoint(worldEnd) - zone.center;
        Vector3 delta = end - start;
        Vector3 halfSize = zone.size * 0.5f;
        float enter = 0f;
        float exit = 1f;

        bool intersects = ClipSegmentAxis(start.x, delta.x, -halfSize.x, halfSize.x, ref enter, ref exit) &&
            ClipSegmentAxis(start.y, delta.y, -halfSize.y, halfSize.y, ref enter, ref exit) &&
            ClipSegmentAxis(start.z, delta.z, -halfSize.z, halfSize.z, ref enter, ref exit);
        entryFraction = enter;
        return intersects;
    }

    private static bool ClipSegmentAxis(
        float start,
        float delta,
        float minimum,
        float maximum,
        ref float enter,
        ref float exit)
    {
        if (Mathf.Approximately(delta, 0f))
        {
            return start >= minimum && start <= maximum;
        }

        float first = (minimum - start) / delta;
        float second = (maximum - start) / delta;
        if (first > second)
        {
            (first, second) = (second, first);
        }

        enter = Mathf.Max(enter, first);
        exit = Mathf.Min(exit, second);
        return enter <= exit;
    }

    private void ResetHandoffTracking()
    {
        authorityHandoffPending = false;
        handoffSampleValid = false;
    }

    private ulong GetFirstRemoteClientId()
    {
        if (NetworkManager == null)
        {
            return 0;
        }

        foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
        {
            if (clientId != NetworkManager.ServerClientId)
            {
                return clientId;
            }
        }

        return NetworkManager.ServerClientId;
    }

    private bool IsConnectedClient(ulong clientId)
    {
        if (NetworkManager == null)
        {
            return false;
        }

        foreach (ulong connectedId in NetworkManager.ConnectedClientsIds)
        {
            if (connectedId == clientId)
            {
                return true;
            }
        }

        return false;
    }

    private void RequestAuthorityHandoff(
        ulong destinationOwner,
        Vector3 position,
        Quaternion rotation,
        Vector3 velocity,
        Vector3 angularVelocity,
        double sampleServerTime)
    {
        if (!IsSpawned || NetworkManager == null || authorityHandoffPending ||
            destinationOwner == NetworkObject.OwnerClientId ||
            !IsConnectedClient(destinationOwner))
        {
            return;
        }

        authorityHandoffPending = true;
        uint sequence = ++lastAuthoritySequence;
        if (IsServer)
        {
            PerformAuthorityHandoff(
                NetworkManager.LocalClientId,
                destinationOwner,
                position,
                rotation,
                velocity,
                angularVelocity,
                sampleServerTime,
                sequence);
        }
        else
        {
            RequestAuthorityHandoffServerRpc(
                destinationOwner,
                position,
                rotation,
                velocity,
                angularVelocity,
                sampleServerTime,
                sequence);
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestAuthorityHandoffServerRpc(
        ulong destinationOwner,
        Vector3 position,
        Quaternion rotation,
        Vector3 velocity,
        Vector3 angularVelocity,
        double sampleServerTime,
        uint sequence,
        RpcParams rpcParams = default)
    {
        PerformAuthorityHandoff(
            rpcParams.Receive.SenderClientId,
            destinationOwner,
            position,
            rotation,
            velocity,
            angularVelocity,
            sampleServerTime,
            sequence);
    }

    private void PerformAuthorityHandoff(
        ulong sender,
        ulong destinationOwner,
        Vector3 position,
        Quaternion rotation,
        Vector3 velocity,
        Vector3 angularVelocity,
        double sampleServerTime,
        uint sequence)
    {
        if (!IsServer || sender != NetworkObject.OwnerClientId ||
            destinationOwner == sender || !IsConnectedClient(destinationOwner))
        {
            return;
        }

        if (destinationOwner == NetworkManager.ServerClientId)
        {
            NetworkObject.RemoveOwnership();
            ApplyExtrapolatedState(
                position, rotation, velocity, angularVelocity, sampleServerTime, sequence);
            return;
        }

        NetworkObject.ChangeOwnership(destinationOwner);
        ReceiveAuthorityStateRpc(
            position,
            rotation,
            velocity,
            angularVelocity,
            sampleServerTime,
            sequence,
            RpcTarget.Single(destinationOwner, RpcTargetUse.Temp));
    }

    [Rpc(SendTo.SpecifiedInParams)]
    private void ReceiveAuthorityStateRpc(
        Vector3 position,
        Quaternion rotation,
        Vector3 velocity,
        Vector3 angularVelocity,
        double sampleServerTime,
        uint sequence,
        RpcParams rpcParams = default)
    {
        ApplyExtrapolatedState(
            position, rotation, velocity, angularVelocity, sampleServerTime, sequence);
    }

    private void ApplyExtrapolatedState(
        Vector3 position,
        Quaternion rotation,
        Vector3 velocity,
        Vector3 angularVelocity,
        double sampleServerTime,
        uint sequence)
    {
        float transitTime = NetworkManager == null
            ? 0f
            : Mathf.Clamp((float)(NetworkManager.ServerTime.Time - sampleServerTime), 0f, 0.15f);
        Vector3 extrapolatedPosition =
            position + velocity * transitTime + Physics.gravity * (0.5f * transitTime * transitTime);
        Vector3 extrapolatedVelocity = velocity + Physics.gravity * transitTime;
        float rotationAngle = angularVelocity.magnitude * transitTime * Mathf.Rad2Deg;
        Quaternion extrapolatedRotation = rotationAngle > 0f
            ? Quaternion.AngleAxis(rotationAngle, angularVelocity.normalized) * rotation
            : rotation;
        ApplyState(
            extrapolatedPosition,
            extrapolatedRotation,
            extrapolatedVelocity,
            angularVelocity,
            sequence);
    }

    private void ApplyState(
        Vector3 position,
        Quaternion rotation,
        Vector3 velocity,
        Vector3 angularVelocity,
        uint sequence)
    {
        ballBody.position = position;
        ballBody.rotation = rotation;
        transform.SetPositionAndRotation(position, rotation);
        ballBody.linearVelocity = Vector3.ClampMagnitude(velocity, maxSpeed);
        ballBody.angularVelocity = Vector3.ClampMagnitude(angularVelocity, ballBody.maxAngularVelocity);
        lastAuthoritySequence = sequence;
    }

    private void RestoreOfflinePhysics()
    {
        if (IsSpawned || (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening))
        {
            return;
        }

        if (ballBody != null && ballBody.isKinematic)
        {
            ballBody.isKinematic = false;
        }

        if (grabInteractable != null && !grabInteractable.enabled)
        {
            grabInteractable.enabled = true;
        }
    }
}
