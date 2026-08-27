using Unity.Netcode;
using UnityEngine;


public sealed class TableTennisNetworkRacket : NetworkBehaviour
{
    private UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable grabInteractable;
    private Rigidbody body;

    private void Awake()
    {
        grabInteractable = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
        body = GetComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
    }

    public override void OnNetworkSpawn()
    {
        if (grabInteractable != null)
        {
            grabInteractable.enabled = IsOwner;
        }
    }

    private void Update()
    {
        if (!IsSpawned || !IsOwner)
        {
            return;
        }

        SubmitPoseServerRpc(transform.position, transform.rotation);
    }

    [ServerRpc(Delivery = RpcDelivery.Unreliable)]
    private void SubmitPoseServerRpc(Vector3 position, Quaternion rotation)
    {
        transform.SetPositionAndRotation(position, rotation);
    }
}
