using Unity.Netcode;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;


public sealed class TableTennisNetworkRacket : NetworkBehaviour
{
    private const float MaxSweepStep = 0.02f;
    private const float MaxSweepAngle = 5f;
    private const int MaxSweepSubsteps = 8;

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
    private bool ballCollisionsIgnored;
    private Vector3 previousPhysicsPosition;
    private Quaternion previousPhysicsRotation;
    private uint hitSequence;

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
        EnsureNativeBallCollisionsIgnored();

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

        SweepForBall(
            previousPhysicsPosition,
            previousPhysicsRotation,
            currentPosition,
            currentRotation);
        previousPhysicsPosition = currentPosition;
        previousPhysicsRotation = currentRotation;
    }

    private void SweepForBall(
        Vector3 fromPosition,
        Quaternion fromRotation,
        Vector3 toPosition,
        Quaternion toRotation)
    {
        float distance = Vector3.Distance(fromPosition, toPosition);
        float angle = Quaternion.Angle(fromRotation, toRotation);
        int substeps = Mathf.Clamp(
            Mathf.Max(
                Mathf.CeilToInt(distance / MaxSweepStep),
                Mathf.CeilToInt(angle / MaxSweepAngle)),
            1,
            MaxSweepSubsteps);

        float fixedDelta = Mathf.Max(Time.fixedDeltaTime, 0.001f);
        Vector3 paddleVelocity = (toPosition - fromPosition) / fixedDelta;
        Vector3 paddleAngularVelocity = CalculateAngularVelocity(
            fromRotation, toRotation, fixedDelta);
        Vector3 halfExtents = Vector3.Scale(
            paddleCollider.size * 0.5f,
            Abs(transform.lossyScale));

        for (int step = 1; step <= substeps; step++)
        {
            float t = step / (float)substeps;
            Vector3 posePosition = Vector3.Lerp(fromPosition, toPosition, t);
            Quaternion poseRotation = Quaternion.Slerp(fromRotation, toRotation, t);
            Vector3 center = posePosition + poseRotation * Vector3.Scale(
                paddleCollider.center,
                transform.lossyScale);

            Collider[] overlaps = Physics.OverlapBox(
                center,
                halfExtents,
                poseRotation,
                ~0,
                QueryTriggerInteraction.Ignore);

            foreach (Collider overlap in overlaps)
            {
                TableTennisBall ball = overlap.GetComponentInParent<TableTennisBall>();
                if (ball == null || !ball.IsPhysicsAuthority)
                {
                    continue;
                }

                IgnoreNativeBallCollision(overlap);
                Vector3 normal = poseRotation * Vector3.up;
                Vector3 ballOffset = overlap.bounds.center - center;
                if (Vector3.Dot(normal, ballOffset) < 0f)
                {
                    normal = -normal;
                }

                // Use the actual paddle face rather than the ball's current centre.
                // TableTennisBall then places its centre just outside this face, preventing
                // the same overlap from being treated as another hit a few frames later.
                float halfThickness = paddleCollider.size.y * Mathf.Abs(transform.lossyScale.y) * 0.5f;
                Vector3 contactPoint = center + normal * halfThickness;
                ulong hitter = IsSpawned ? OwnerClientId : 0;
                RacketHitSample hit = new(
                    hitter,
                    contactPoint,
                    normal,
                    paddleVelocity,
                    paddleAngularVelocity,
                    ++hitSequence);
                if (ball.TryApplyRacketHit(hit))
                {
                    return;
                }
            }
        }
    }

    private void IgnoreNativeBallCollision(Collider ballCollider)
    {
        foreach (Collider racketCollider in racketColliders)
        {
            if (racketCollider != null && ballCollider != null)
            {
                Physics.IgnoreCollision(racketCollider, ballCollider, true);
            }
        }
    }

    private void EnsureNativeBallCollisionsIgnored()
    {
        if (ballCollisionsIgnored)
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
                IgnoreNativeBallCollision(ballCollider);
            }
        }

        ballCollisionsIgnored = true;
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

    private static Vector3 Abs(Vector3 value)
    {
        return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
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
