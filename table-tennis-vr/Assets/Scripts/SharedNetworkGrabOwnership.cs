using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[RequireComponent(typeof(NetworkObject), typeof(XRGrabInteractable))]
public sealed class SharedNetworkGrabOwnership : NetworkBehaviour
{
    public const ulong NoGrabber = ulong.MaxValue;

    [SerializeField, Min(0.25f)] private float reservationTimeout = 2f;

    private readonly NetworkVariable<ulong> activeGrabber = new(NoGrabber);
    private XRGrabInteractable grabInteractable;
    private XRSelectFilterDelegate ownershipFilter;
    private bool locallySelected;
    private bool grabRequestPending;
    private bool reservationConfirmed;
    private uint reservationVersion;

    public ulong ActiveGrabber => activeGrabber.Value;
    public bool IsLocallySelected => locallySelected;
    public bool HasPhysicsAuthority => !IsSpawned || IsOwner;

    private void Awake()
    {
        grabInteractable = GetComponent<XRGrabInteractable>();
        ownershipFilter = new XRSelectFilterDelegate(CanSelect);
        grabInteractable.selectFilters.Add(ownershipFilter);
        grabInteractable.selectEntered.AddListener(HandleSelectEntered);
        grabInteractable.selectExited.AddListener(HandleSelectExited);
    }

    public override void OnNetworkSpawn()
    {
        activeGrabber.OnValueChanged += HandleGrabberChanged;
        if (IsServer && NetworkManager != null)
        {
            NetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;
        }

        locallySelected = false;
        grabRequestPending = false;
        reservationConfirmed = false;
        grabInteractable.enabled = true;
    }

    public override void OnGainedOwnership()
    {
        grabRequestPending = false;
    }

    public override void OnLostOwnership()
    {
        grabRequestPending = false;
        CancelLocalSelection();
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
        reservationConfirmed = false;
        grabInteractable.enabled = true;
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

    public void ForceReleaseToServer()
    {
        if (!IsSpawned)
        {
            CancelLocalSelection();
            return;
        }

        if (!IsServer)
        {
            Debug.LogWarning("Only the server can force-release network grab ownership.", this);
            return;
        }

        reservationVersion++;
        reservationConfirmed = false;
        activeGrabber.Value = NoGrabber;
        if (NetworkObject.OwnerClientId != NetworkManager.ServerClientId)
        {
            NetworkObject.RemoveOwnership();
        }
    }

    private bool CanSelect(
        UnityEngine.XR.Interaction.Toolkit.Interactors.IXRSelectInteractor _,
        IXRSelectInteractable __)
    {
        if (!IsSpawned || NetworkManager == null || !NetworkManager.IsListening)
        {
            return true;
        }

        ulong localClientId = NetworkManager.LocalClientId;
        if (IsOwner &&
            (activeGrabber.Value == NoGrabber || activeGrabber.Value == localClientId))
        {
            return true;
        }

        if (!grabRequestPending && activeGrabber.Value == NoGrabber)
        {
            grabRequestPending = true;
            if (IsServer)
            {
                ReserveGrab(localClientId);
            }
            else
            {
                RequestGrabServerRpc();
            }
        }

        return false;
    }

    private void HandleSelectEntered(SelectEnterEventArgs _)
    {
        locallySelected = true;
        grabRequestPending = false;
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
        if (IsSpawned)
        {
            StartCoroutine(ClearGrabberAfterDetach());
        }
    }

    private IEnumerator ClearGrabberAfterDetach()
    {
        // XRI applies throw velocity in LateUpdate. Ownership remains with the
        // releasing peer so its NetworkRigidbody can continue the simulation.
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
        if (!IsSpawned || NetworkManager == null)
        {
            return;
        }

        if (newGrabber == NetworkManager.LocalClientId)
        {
            grabRequestPending = false;
            return;
        }

        if (newGrabber == NoGrabber)
        {
            grabRequestPending = false;
        }

        CancelLocalSelection();
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        if (!IsServer)
        {
            return;
        }

        bool clientWasGrabber = activeGrabber.Value == clientId;
        bool clientWasOwner = NetworkObject.OwnerClientId == clientId;
        if (!clientWasGrabber && !clientWasOwner)
        {
            return;
        }

        reservationVersion++;
        reservationConfirmed = false;
        if (clientWasGrabber)
        {
            activeGrabber.Value = NoGrabber;
        }

        if (clientWasOwner)
        {
            NetworkObject.RemoveOwnership();
        }
    }

    private void CancelLocalSelection()
    {
        if (!locallySelected || grabInteractable == null)
        {
            return;
        }

        grabInteractable.interactionManager?.CancelInteractableSelection(
            (IXRSelectInteractable)grabInteractable);
        locallySelected = false;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestGrabServerRpc(RpcParams rpcParams = default)
    {
        ReserveGrab(rpcParams.Receive.SenderClientId);
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

    private void ReserveGrab(ulong clientId)
    {
        if (!IsServer || NetworkObject == null || activeGrabber.Value != NoGrabber ||
            !IsConnectedClient(clientId))
        {
            return;
        }

        uint version = ++reservationVersion;
        reservationConfirmed = false;
        activeGrabber.Value = clientId;
        if (NetworkObject.OwnerClientId != clientId)
        {
            NetworkObject.ChangeOwnership(clientId);
        }

        StartCoroutine(ExpireReservation(clientId, version));
    }

    private IEnumerator ExpireReservation(ulong clientId, uint version)
    {
        yield return new WaitForSecondsRealtime(reservationTimeout);

        if (!IsServer || version != reservationVersion || reservationConfirmed ||
            activeGrabber.Value != clientId)
        {
            yield break;
        }

        activeGrabber.Value = NoGrabber;
        if (NetworkObject.OwnerClientId == clientId &&
            clientId != NetworkManager.ServerClientId)
        {
            NetworkObject.RemoveOwnership();
        }
    }

    private void ConfirmGrab(ulong clientId)
    {
        if (!IsServer || NetworkObject == null || !IsConnectedClient(clientId) ||
            (activeGrabber.Value != NoGrabber && activeGrabber.Value != clientId))
        {
            return;
        }

        reservationVersion++;
        reservationConfirmed = true;
        activeGrabber.Value = clientId;
        if (NetworkObject.OwnerClientId != clientId)
        {
            NetworkObject.ChangeOwnership(clientId);
        }
    }

    private void ClearGrabber(ulong clientId)
    {
        if (!IsServer || activeGrabber.Value != clientId)
        {
            return;
        }

        reservationVersion++;
        reservationConfirmed = false;
        activeGrabber.Value = NoGrabber;
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
}
