using Unity.Netcode;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;


public sealed class TableTennisNetworkRacket : NetworkBehaviour
{
    private XRGrabInteractable grabInteractable;
    private Rigidbody body;
    private Transform spawnParent;
    private Vector3 spawnLocalPosition;
    private Quaternion spawnLocalRotation;
    private Vector3 spawnLocalScale;
    private Vector3 spawnWorldPosition;
    private Quaternion spawnWorldRotation;
    private bool hasSpawnPose;

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
        if (!IsSpawned)
        {
            CaptureSpawnPose();
        }

        ApplyInteractionAuthority();
        RestoreOfflinePhysics();
    }

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            CaptureSpawnPose();
        }

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

    public void RequestResetToSpawn()
    {
        if (!IsSpawned)
        {
            ResetToSpawnPose();
            return;
        }

        if (IsServer)
        {
            ResetToSpawnRpc();
            return;
        }

        RequestResetToSpawnServerRpc();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestResetToSpawnServerRpc()
    {
        ResetToSpawnRpc();
    }

    [Rpc(SendTo.Everyone)]
    private void ResetToSpawnRpc()
    {
        if (IsOwner)
        {
            ResetToSpawnPose();
        }
    }

    private void CaptureSpawnPose()
    {
        spawnParent = transform.parent;
        spawnLocalPosition = transform.localPosition;
        spawnLocalRotation = transform.localRotation;
        spawnLocalScale = transform.localScale;
        spawnWorldPosition = transform.position;
        spawnWorldRotation = transform.rotation;
        hasSpawnPose = true;
    }

    private void ResetToSpawnPose()
    {
        if (!hasSpawnPose)
        {
            CaptureSpawnPose();
        }

        if (grabInteractable != null)
        {
            grabInteractable.enabled = false;
        }

        transform.SetParent(spawnParent, true);
        if (spawnParent == null)
        {
            transform.SetPositionAndRotation(spawnWorldPosition, spawnWorldRotation);
        }
        else
        {
            transform.SetLocalPositionAndRotation(spawnLocalPosition, spawnLocalRotation);
        }
        transform.localScale = spawnLocalScale;

        if (body != null)
        {
            body.position = transform.position;
            body.rotation = transform.rotation;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        ApplyInteractionAuthority();
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
