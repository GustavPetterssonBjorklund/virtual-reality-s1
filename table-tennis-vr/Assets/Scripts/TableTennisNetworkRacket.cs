using Unity.Netcode;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;


public sealed class TableTennisNetworkRacket : NetworkBehaviour
{
    private XRGrabInteractable grabInteractable;
    private Rigidbody body;
    private Collider[] racketColliders;
    private BoxCollider paddleCollider;
    private TableTennisMRPlacement tablePlacement;
    private bool ignoresTableCollision;
    private Transform spawnParent;
    private Vector3 spawnLocalPosition;
    private Quaternion spawnLocalRotation;
    private Vector3 spawnLocalScale;
    private Vector3 spawnWorldPosition;
    private Quaternion spawnWorldRotation;
    private bool hasSpawnPose;
    private bool hasPreviousPhysicsPose;
    private bool nativeBallCollisionsEnabled;
    private Vector3 previousPhysicsPosition;
    private Quaternion previousPhysicsRotation;
    private Vector3 paddleVelocity;
    private Vector3 paddleAngularVelocity;
    private uint hitSequence;

    public bool IsPhysicsAuthority => !IsSpawned || IsOwner;
    public bool IsKinematic => body != null && body.isKinematic;

    private void Awake()
    {
        grabInteractable = GetComponent<XRGrabInteractable>();
        body = GetComponent<Rigidbody>();
        racketColliders = GetComponentsInChildren<Collider>(true);
        body.useGravity = true;
        // XR drives a grabbed racket as a kinematic Rigidbody. Speculative CCD is
        // Unity's continuous mode that supports kinematic bodies and angular motion.
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        body.interpolation = RigidbodyInterpolation.Interpolate;

        foreach (Collider racketCollider in racketColliders)
        {
            if (racketCollider is BoxCollider box &&
                (paddleCollider == null || box.transform == transform))
            {
                paddleCollider = box;
            }
        }

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

    private void FixedUpdate()
    {
        EnsureNativeBallCollisionsEnabled();

        if (!IsPhysicsAuthority || paddleCollider == null)
        {
            hasPreviousPhysicsPose = false;
            return;
        }

        Vector3 currentPosition = transform.position;
        Quaternion currentRotation = transform.rotation;
        if (!hasPreviousPhysicsPose)
        {
            previousPhysicsPosition = currentPosition;
            previousPhysicsRotation = currentRotation;
            hasPreviousPhysicsPose = true;
            return;
        }

        float fixedDelta = Mathf.Max(Time.fixedDeltaTime, 0.001f);
        paddleVelocity = (currentPosition - previousPhysicsPosition) / fixedDelta;
        paddleAngularVelocity = CalculateAngularVelocity(
            previousPhysicsRotation,
            currentRotation,
            fixedDelta);
        previousPhysicsPosition = currentPosition;
        previousPhysicsRotation = currentRotation;
    }

    private void SetNativeBallCollision(Collider ballCollider, bool ignored)
    {
        foreach (Collider racketCollider in racketColliders)
        {
            if (racketCollider != null && ballCollider != null)
            {
                Physics.IgnoreCollision(racketCollider, ballCollider, ignored);
            }
        }
    }

    private void EnsureNativeBallCollisionsEnabled()
    {
        if (nativeBallCollisionsEnabled)
        {
            return;
        }

        TableTennisBall[] balls = FindObjectsByType<TableTennisBall>(FindObjectsSortMode.None);
        if (balls.Length == 0)
        {
            return;
        }

        foreach (TableTennisBall ball in balls)
        {
            foreach (Collider ballCollider in ball.GetComponentsInChildren<Collider>(true))
            {
                SetNativeBallCollision(ballCollider, false);
            }
        }

        nativeBallCollisionsEnabled = true;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.contactCount == 0)
        {
            return;
        }

        TableTennisBall ball = collision.collider.GetComponentInParent<TableTennisBall>();
        if (ball == null)
        {
            return;
        }

        ContactPoint contact = collision.GetContact(0);
        Vector3 normal = ball.transform.position - contact.point;
        if (normal.sqrMagnitude < 0.0001f)
        {
            normal = transform.up;
        }
        normal.Normalize();

        // Collision.relativeVelocity is sampled at contact, before a scripted
        // rebound is applied. Orient it so negative means the ball is closing.
        Vector3 incomingRelativeVelocity = collision.relativeVelocity;
        if (Vector3.Dot(incomingRelativeVelocity, normal) > 0f)
        {
            incomingRelativeVelocity = -incomingRelativeVelocity;
        }

        ulong hitter = IsSpawned ? OwnerClientId : 0;
        ball.TryApplyRacketHit(new RacketHitSample(
            hitter,
            contact.point,
            normal,
            paddleVelocity,
            paddleAngularVelocity,
            ++hitSequence),
            incomingRelativeVelocity);
    }

    private static Vector3 CalculateAngularVelocity(
        Quaternion from,
        Quaternion to,
        float deltaTime)
    {
        Quaternion delta = to * Quaternion.Inverse(from);
        if (delta.w < 0f)
        {
            delta.x = -delta.x;
            delta.y = -delta.y;
            delta.z = -delta.z;
            delta.w = -delta.w;
        }

        delta.ToAngleAxis(out float angleDegrees, out Vector3 axis);
        if (float.IsInfinity(axis.x) || angleDegrees < 0.001f)
        {
            return Vector3.zero;
        }

        return axis.normalized * (angleDegrees * Mathf.Deg2Rad / deltaTime);
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
