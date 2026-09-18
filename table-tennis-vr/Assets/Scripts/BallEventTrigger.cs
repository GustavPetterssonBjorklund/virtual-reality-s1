using UnityEngine;

public class BallEventTrigger : MonoBehaviour
{
    public TableTennisRules rules;
    public TableTennisRules.BallEvent eventType;

    // Ignore duplicate racket events that happen almost instantly.
    private float lastTriggerTime = -1f;
    private const float racketCooldown = 0.05f;

    private void Awake()
    {
        if (rules == null)
        {
            rules = FindFirstObjectByType<TableTennisRules>();
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Ball"))
            return;

        if (rules == null)
        {
            Debug.LogError("BallEventTrigger could not find TableTennisRules!");
            return;
        }

        if (!rules.HasAuthority)
            return;

        bool isRacket =
            eventType == TableTennisRules.BallEvent.P1Racket ||
            eventType == TableTennisRules.BallEvent.P2Racket;

        if (isRacket)
        {
            if (Time.time - lastTriggerTime < racketCooldown)
                return;

            lastTriggerTime = Time.time;
        }

        rules.BallEventHappened(eventType);
    }
}
