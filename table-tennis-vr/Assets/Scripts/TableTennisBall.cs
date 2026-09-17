using Unity.Netcode;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public sealed class TableTennisBall : NetworkBehaviour
{
    [SerializeField] private float maxSpeed = 18f;
    private Rigidbody ballBody;
    private XRGrabInteractable grabInteractable;
    private bool frozen;

    public Vector3 LinearVelocity => ballBody == null ? Vector3.zero : ballBody.linearVelocity;
    public bool IsFrozen => frozen;
    public bool IsPhysicsAuthority => !IsSpawned || IsServer;
    public bool IsKinematic => ballBody != null && ballBody.isKinematic;

    private void Awake()
    {
        ballBody = GetComponent<Rigidbody>();
        grabInteractable = GetComponent<XRGrabInteractable>();
        ballBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        ballBody.interpolation = RigidbodyInterpolation.Interpolate;
        ballBody.solverIterations = 10;
        ballBody.solverVelocityIterations = 4;
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

    private void FixedUpdate()
    {
        if (IsSpawned && !IsServer)
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
            // Network matches serve/reset the ball through the authoritative
            // match flow. Letting either headset grab it creates another
            // transform writer that fights NetworkTransform.
            grabInteractable.enabled = !IsSpawned;
        }
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
