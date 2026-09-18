using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public class ARFloorCollision : MonoBehaviour
{
    private ARPlane plane;
    private TableTennisRules rules;

    void Awake()
    {
        plane = GetComponent<ARPlane>();
        rules = FindFirstObjectByType<TableTennisRules>();
    }

    private void OnCollisionEnter(Collision collision)
    {
        // Ignore anything that isn't the ball.
        if (!collision.gameObject.CompareTag("Ball"))
            return;

        // Only planes classified as Floor count as ground.
        if ((plane.classifications & PlaneClassifications.Floor) == 0)
            return;

        if (rules == null)
        {
            Debug.LogError("ARFloorCollision could not find TableTennisRules!");
            return;
        }

        Debug.Log("Ball hit detected AR floor.");

        rules.BallEventHappened(
            TableTennisRules.BallEvent.Ground
        );
    }
}