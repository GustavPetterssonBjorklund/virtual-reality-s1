using Unity.Netcode;
using UnityEngine;

public sealed class TableTennisBall : NetworkBehaviour
{
    [SerializeField] private float maxSpeed = 18f;
    [SerializeField] private float racketImpulseScale = 0.15f;

    private Rigidbody ballBody;
    private bool frozen;

    public Vector3 LinearVelocity => ballBody == null ? Vector3.zero : ballBody.linearVelocity;
    public bool IsFrozen => frozen;

    private void Awake()
    {
        ballBody = GetComponent<Rigidbody>();
        ballBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        ballBody.interpolation = RigidbodyInterpolation.Interpolate;
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

    private void OnCollisionEnter(Collision collision)
    {
        if (IsSpawned && !IsServer)
        {
            return;
        }

        if (frozen)
        {
            return;
        }

        bool hitRacket = false;
        Transform current = collision.collider.transform;
        while (current != null)
        {
            if (current.name.Contains("Racket"))
            {
                hitRacket = true;
                break;
            }

            current = current.parent;
        }

        if (!hitRacket)
        {
            return;
        }

        ContactPoint contact = collision.GetContact(0);
        Vector3 relativeVelocity = ballBody.linearVelocity;
        if (collision.rigidbody != null)
        {
            relativeVelocity -= collision.rigidbody.linearVelocity;
        }

        Vector3 reflectedVelocity = Vector3.Reflect(relativeVelocity, contact.normal);
        float impactSpeed = Mathf.Max(relativeVelocity.magnitude, 1f);
        Vector3 hitVelocity = reflectedVelocity + contact.normal * (impactSpeed * racketImpulseScale);
        ballBody.linearVelocity = Vector3.ClampMagnitude(hitVelocity, maxSpeed);
    }
}
