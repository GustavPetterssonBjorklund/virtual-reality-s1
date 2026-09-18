using Unity.Netcode;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public sealed class TableTennisBall : NetworkBehaviour
{
    private const ulong NoGrabber = ulong.MaxValue;

    [SerializeField] private float maxSpeed = 18f;
    private readonly NetworkVariable<ulong> activeGrabber = new(NoGrabber);
    private Rigidbody ballBody;
    private XRGrabInteractable grabInteractable;
    private bool locallySelected;
    private bool frozen;

    public Vector3 LinearVelocity => ballBody == null ? Vector3.zero : ballBody.linearVelocity;
    public bool IsFrozen => frozen;
    public bool IsPhysicsAuthority => !IsSpawned || IsOwner;
    public bool IsKinematic => ballBody != null && ballBody.isKinematic;
    public ulong ActiveGrabber => activeGrabber.Value;

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
        ApplyInteractionAuthority();
    }

    public override void OnDestroy()
    {
        if (grabInteractable != null)
        {
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
        if (IsSpawned && !IsPhysicsAuthority)
        {
            return;
        }

        if (frozen)
        {
            ballBody.linearVelocity = Vector3.zero;
            ballBody.angularVelocity = Vector3.zero;
            return;
        }

        if (ballBody.linearVelocity.sqrMagnitude > maxSpeed * maxSpeed)
        {
            ballBody.linearVelocity = ballBody.linearVelocity.normalized * maxSpeed;
        }
    }

    public void ResetForServe(Vector3 position)
    {
        if (IsSpawned && !IsServer)
        {
            return;
        }

        if (IsSpawned)
        {
            ReturnAuthorityToServer(position, Quaternion.identity, Vector3.zero, Vector3.zero);
        }

        frozen = false;
        ballBody.linearVelocity = Vector3.zero;
        ballBody.angularVelocity = Vector3.zero;
        ballBody.position = position;
        ballBody.rotation = Quaternion.identity;
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

    private void ApplyInteractionAuthority()
    {
        if (grabInteractable != null)
        {
            // Every peer may initiate a grab. The server transfers ownership
            // to the latest grabber so NetworkTransform and physics still have
            // exactly one writer while the ball is held.
            grabInteractable.enabled = true;
        }
    }

    private void HandleSelectEntered(SelectEnterEventArgs _)
    {
        locallySelected = true;
        if (!IsSpawned)
        {
            return;
        }

        if (IsServer)
        {
            GiveAuthorityTo(NetworkManager.LocalClientId);
        }
        else
        {
            RequestGrabServerRpc();
        }
    }

    private void HandleSelectExited(SelectExitEventArgs _)
    {
        locallySelected = false;
        if (!IsSpawned || NetworkManager == null)
        {
            return;
        }

        Vector3 position = ballBody.position;
        Quaternion rotation = ballBody.rotation;
        Vector3 linearVelocity = ballBody.linearVelocity;
        Vector3 angularVelocity = ballBody.angularVelocity;

        if (IsServer)
        {
            ReleaseGrab(NetworkManager.LocalClientId, position, rotation, linearVelocity, angularVelocity);
        }
        else
        {
            ReleaseGrabServerRpc(position, rotation, linearVelocity, angularVelocity);
        }
    }

    private void HandleGrabberChanged(ulong _, ulong newGrabber)
    {
        if (!locallySelected || !IsSpawned || NetworkManager == null ||
            newGrabber == NetworkManager.LocalClientId)
        {
            return;
        }

        // A grab from the other player takes over authority. Cancel the old
        // local selection so two XR interactors cannot keep writing the pose.
        grabInteractable.interactionManager?.CancelInteractableSelection(
            (IXRSelectInteractable)grabInteractable);
        locallySelected = false;
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        if (IsServer && activeGrabber.Value == clientId)
        {
            ReturnAuthorityToServer(ballBody.position, ballBody.rotation, Vector3.zero, Vector3.zero);
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestGrabServerRpc(RpcParams rpcParams = default)
    {
        GiveAuthorityTo(rpcParams.Receive.SenderClientId);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void ReleaseGrabServerRpc(
        Vector3 position,
        Quaternion rotation,
        Vector3 linearVelocity,
        Vector3 angularVelocity,
        RpcParams rpcParams = default)
    {
        ReleaseGrab(
            rpcParams.Receive.SenderClientId,
            position,
            rotation,
            linearVelocity,
            angularVelocity);
    }

    private void GiveAuthorityTo(ulong clientId)
    {
        if (!IsServer || NetworkObject == null)
        {
            return;
        }

        activeGrabber.Value = clientId;
        if (OwnerClientId != clientId)
        {
            NetworkObject.ChangeOwnership(clientId);
        }
    }

    private void ReleaseGrab(
        ulong clientId,
        Vector3 position,
        Quaternion rotation,
        Vector3 linearVelocity,
        Vector3 angularVelocity)
    {
        if (!IsServer || activeGrabber.Value != clientId)
        {
            return;
        }

        ReturnAuthorityToServer(position, rotation, linearVelocity, angularVelocity);
    }

    private void ReturnAuthorityToServer(
        Vector3 position,
        Quaternion rotation,
        Vector3 linearVelocity,
        Vector3 angularVelocity)
    {
        if (!IsServer || NetworkObject == null)
        {
            return;
        }

        activeGrabber.Value = NoGrabber;
        if (OwnerClientId != NetworkManager.ServerClientId)
        {
            NetworkObject.RemoveOwnership();
        }

        ballBody.position = position;
        ballBody.rotation = rotation;
        ballBody.linearVelocity = Vector3.ClampMagnitude(linearVelocity, maxSpeed);
        ballBody.angularVelocity = angularVelocity;
    }

    private void RestoreOfflinePhysics()
    {
        if (IsSpawned || (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening))
        {
            return;
        }

        // NetworkRigidbody parks unspawned bodies in kinematic mode. Offline
        // debug play still needs the normal local physics simulation.
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
