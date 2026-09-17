using Unity.Netcode;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;


public sealed class TableTennisNetworkRacket : NetworkBehaviour
{
    private XRGrabInteractable grabInteractable;
    private Rigidbody body;

    public bool IsPhysicsAuthority => !IsSpawned || IsOwner;
    public bool IsKinematic => body != null && body.isKinematic;

    private void Awake()
    {
        grabInteractable = GetComponent<XRGrabInteractable>();
        body = GetComponent<Rigidbody>();
        body.useGravity = true;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.interpolation = RigidbodyInterpolation.Interpolate;
    }

    private void Start()
    {
        ApplyInteractionAuthority();
        RestoreOfflinePhysics();
    }

    public override void OnNetworkSpawn()
    {
        ApplyInteractionAuthority();
    }

    public override void OnNetworkDespawn()
    {
        ApplyInteractionAuthority();
    }

    private void Update()
    {
        RestoreOfflinePhysics();
    }

    private void ApplyInteractionAuthority()
    {
        if (grabInteractable != null)
        {
            grabInteractable.enabled = !IsSpawned || IsOwner;
        }
    }

    private void RestoreOfflinePhysics()
    {
        if (IsSpawned || (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening))
        {
            return;
        }

        // NetworkRigidbody keeps unspawned instances kinematic. The in-scene
        // racket doubles as the offline racket, so restore its local physics.
        if (body != null && body.isKinematic)
        {
            body.isKinematic = false;
        }

        if (grabInteractable != null && !grabInteractable.enabled)
        {
            grabInteractable.enabled = true;
        }
    }
}
