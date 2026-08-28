using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public sealed class TableTennisSoloOpponent : MonoBehaviour
{
    [SerializeField] private float reactionDelay = 0.12f;
    [SerializeField] private float movementSpeed = 4.5f;
    [SerializeField] private float maximumReach = 0.48f;
    [SerializeField, Range(0f, 1f)] private float missChance = 0.12f;

    private TableTennisBall ball;
    private Transform racket;
    private Vector3 homePosition;
    private float reactionTimer;
    private float missTimer;

    public bool IsEnabled { get; private set; }
    public float ReactionDelay { get => reactionDelay; set => reactionDelay = Mathf.Max(0f, value); }
    public float MovementSpeed { get => movementSpeed; set => movementSpeed = Mathf.Max(0.1f, value); }
    public float MaximumReach { get => maximumReach; set => maximumReach = Mathf.Clamp(value, 0.15f, 0.8f); }
    public float MissChance { get => missChance; set => missChance = Mathf.Clamp01(value); }
    public Vector3 RacketPosition => racket == null ? Vector3.zero : racket.position;

    private void Awake()
    {
        ball = FindFirstObjectByType<TableTennisBall>();
        Transform playerRacket = transform.Find("Racket");
        if (playerRacket != null)
        {
            GameObject opponent = Instantiate(playerRacket.gameObject, transform);
            opponent.name = "Solo Opponent Racket";
            racket = opponent.transform;
            homePosition = new Vector3(-1f, 1.15f, -0.25f);
            racket.localPosition = homePosition;
            racket.localRotation = playerRacket.localRotation;

            XRGrabInteractable grab = opponent.GetComponent<XRGrabInteractable>();
            if (grab != null)
            {
                grab.enabled = false;
            }

            TableTennisNetworkRacket networkRacket = opponent.GetComponent<TableTennisNetworkRacket>();
            if (networkRacket != null)
            {
                networkRacket.enabled = false;
            }
        }

        SetEnabled(false);
    }

    private void Update()
    {
        if (!IsEnabled || racket == null || ball == null)
        {
            return;
        }

        if (Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.IsListening)
        {
            SetEnabled(false);
            return;
        }

        reactionTimer -= Time.deltaTime;
        if (reactionTimer > 0f)
        {
            return;
        }

        Vector3 target = homePosition;
        Vector3 localBall = transform.InverseTransformPoint(ball.transform.position);
        Vector3 localVelocity = transform.InverseTransformDirection(ball.LinearVelocity);
        if (localVelocity.x < 0f || localBall.x < 0f)
        {
            target.z = Mathf.Clamp(localBall.z, -maximumReach, maximumReach);
            target.y = Mathf.Clamp(localBall.y, 0.92f, 1.42f);
            if (missTimer <= 0f && Random.value < missChance * Time.deltaTime)
            {
                missTimer = 0.45f;
            }
        }

        missTimer -= Time.deltaTime;
        if (missTimer > 0f)
        {
            target.z += maximumReach * 1.8f;
        }

        racket.localPosition = Vector3.MoveTowards(racket.localPosition, target, movementSpeed * Time.deltaTime);
        reactionTimer = reactionDelay;
    }

    public void SetEnabled(bool enabled)
    {
        IsEnabled = enabled && racket != null;
        if (racket != null)
        {
            racket.gameObject.SetActive(IsEnabled);
            if (!IsEnabled)
            {
                racket.localPosition = homePosition;
            }
        }
        reactionTimer = 0f;
        missTimer = 0f;
    }
}
