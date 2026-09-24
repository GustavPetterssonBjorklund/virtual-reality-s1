using Unity.Netcode;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[RequireComponent(typeof(SharedNetworkGrabOwnership))]
public sealed class TableTennisBall : NetworkBehaviour
{
    [SerializeField] private float maxSpeed = 18f;
    [SerializeField] private float sideHandoffDeadZone = 0.05f;

    private Rigidbody ballBody;
    private XRGrabInteractable grabInteractable;
    private SharedNetworkGrabOwnership grabOwnership;
    private TableTennisMRPlacement tablePlacement;
    private bool frozen;
    private uint lastAuthoritySequence;

    public Vector3 LinearVelocity => ballBody == null ? Vector3.zero : ballBody.linearVelocity;
    public Vector3 AngularVelocity => ballBody == null ? Vector3.zero : ballBody.angularVelocity;
    public bool IsFrozen => frozen;
    public bool IsPhysicsAuthority => grabOwnership == null || grabOwnership.HasPhysicsAuthority;
    public bool IsKinematic => ballBody != null && ballBody.isKinematic;
    public ulong ActiveGrabber => grabOwnership == null
        ? SharedNetworkGrabOwnership.NoGrabber
        : grabOwnership.ActiveGrabber;
    public uint LastAuthoritySequence => lastAuthoritySequence;

    private void Awake()
    {
        ballBody = GetComponent<Rigidbody>();
        grabInteractable = GetComponent<XRGrabInteractable>();
        grabOwnership = GetComponent<SharedNetworkGrabOwnership>();
        ballBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        ballBody.interpolation = RigidbodyInterpolation.Interpolate;
        ballBody.solverIterations = 10;
        ballBody.solverVelocityIterations = 4;

        if (grabInteractable != null)
        {
            grabInteractable.selectExited.AddListener(HandleSelectExited);
        }
    }

    private void Start()
    {
        RestoreOfflinePhysics();
    }

    public override void OnDestroy()
    {
        if (grabInteractable != null)
        {
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

        if (grabOwnership == null || !grabOwnership.IsLocallySelected)
        {
            TryHandoffAcrossNet();
        }

        ballBody.linearVelocity = Vector3.ClampMagnitude(ballBody.linearVelocity, maxSpeed);
    }

    public void ResetForServe(Vector3 position)
    {
        if (IsSpawned && !IsServer)
        {
            return;
        }

        frozen = false;
        if (IsSpawned && grabOwnership != null)
        {
            grabOwnership.ForceReleaseToServer();
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

    private void HandleSelectExited(SelectExitEventArgs _)
    {
        // Synchronize the Rigidbody before physics resumes so release starts at
        // the controller pose and keeps the throw velocity XRI applies afterward.
        ballBody.position = transform.position;
        ballBody.rotation = transform.rotation;
        Physics.SyncTransforms();
    }

    private void TryHandoffAcrossNet()
    {
        if (!IsSpawned ||
            (grabOwnership != null &&
             grabOwnership.ActiveGrabber != SharedNetworkGrabOwnership.NoGrabber))
        {
            return;
        }

        if (tablePlacement == null)
        {
            tablePlacement = FindFirstObjectByType<TableTennisMRPlacement>();
        }

        Transform tableRoot = tablePlacement == null ? null : tablePlacement.TableRoot;
        if (tableRoot == null)
        {
            return;
        }

        float localX = tableRoot.InverseTransformPoint(ballBody.position).x;
        if (Mathf.Abs(localX) <= sideHandoffDeadZone)
        {
            return;
        }

        ulong desiredOwner = localX > 0f ? NetworkManager.ServerClientId : GetFirstRemoteClientId();
        if (desiredOwner != NetworkObject.OwnerClientId)
        {
            RequestAuthorityHandoff(desiredOwner);
        }
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

    private void RequestAuthorityHandoff(ulong destinationOwner)
    {
        if (!IsSpawned || NetworkManager == null ||
            destinationOwner == NetworkObject.OwnerClientId ||
            !IsConnectedClient(destinationOwner))
        {
            return;
        }

        uint sequence = ++lastAuthoritySequence;
        double serverTime = NetworkManager.ServerTime.Time;
        if (IsServer)
        {
            PerformAuthorityHandoff(
                NetworkManager.LocalClientId,
                destinationOwner,
                ballBody.position,
                ballBody.rotation,
                ballBody.linearVelocity,
                ballBody.angularVelocity,
                serverTime,
                sequence);
        }
        else
        {
            RequestAuthorityHandoffServerRpc(
                destinationOwner,
                ballBody.position,
                ballBody.rotation,
                ballBody.linearVelocity,
                ballBody.angularVelocity,
                serverTime,
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
        ApplyState(
            extrapolatedPosition,
            rotation,
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
