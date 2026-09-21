using Unity.Netcode;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;


public sealed class TableTennisNetworkRacket : NetworkBehaviour
{
    private XRGrabInteractable grabInteractable;
    private Rigidbody body;
    private Collider[] racketColliders;
    private TableTennisMRPlacement tablePlacement;
    private bool ignoresTableCollision;
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
        racketColliders = GetComponentsInChildren<Collider>(true);
        body.useGravity = true;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.interpolation = RigidbodyInterpolation.Interpolate;

        if (grabInteractable != null)
        {
            grabInteractable.selectEntered.AddListener(HandleSelectEntered);
            grabInteractable.selectExited.AddListener(HandleSelectExited);
        }
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
        SetTableCollisionIgnored(false);
        ApplyInteractionAuthority();
    }

    public override void OnDestroy()
    {
        SetTableCollisionIgnored(false);
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

    private void ApplyInteractionAuthority()
    {
        if (grabInteractable != null)
        {
            grabInteractable.enabled = !IsSpawned || IsOwner;
        }
    }

    private void HandleSelectEntered(SelectEnterEventArgs _)
    {
        SetTableCollisionIgnored(true);
    }

    private void HandleSelectExited(SelectExitEventArgs _)
    {
        SetTableCollisionIgnored(false);
    }

    private void SetTableCollisionIgnored(bool ignored)
    {
        if (ignoresTableCollision == ignored)
        {
            return;
        }

        if (tablePlacement == null)
        {
            tablePlacement = FindFirstObjectByType<TableTennisMRPlacement>();
        }

        Collider[] tableColliders = tablePlacement == null ? null : tablePlacement.TableColliders;
        if (tableColliders == null)
        {
            return;
        }

        foreach (Collider racketCollider in racketColliders)
        {
            if (racketCollider == null)
            {
                continue;
            }

            foreach (Collider tableCollider in tableColliders)
            {
                if (tableCollider != null && tableCollider != racketCollider)
                {
                    Physics.IgnoreCollision(racketCollider, tableCollider, ignored);
                }
            }
        }

        ignoresTableCollision = ignored;
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
