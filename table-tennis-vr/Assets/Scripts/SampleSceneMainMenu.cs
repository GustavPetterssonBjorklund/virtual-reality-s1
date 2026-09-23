using UnityEngine;

/// <summary>Reveals the existing scene without loading it again.</summary>
[DefaultExecutionOrder(-1000)]
public sealed class SampleSceneMainMenu : MonoBehaviour
{
    [SerializeField] private GameObject menuCanvas;
    [Tooltip("Objects that should become active when Play is pressed, in activation order.")]
    [SerializeField] private GameObject[] gameplayObjects;
    [Tooltip("XR gameplay features to pause while the menu is open.")]
    [SerializeField] private Behaviour[] gameplayBehaviours;
    [SerializeField] private float menuDistance = 2f;

    private bool gameplayStarted;

    private void Awake()
    {
        foreach (GameObject item in gameplayObjects)
            if (item != null)
                item.SetActive(false);

        foreach (Behaviour item in gameplayBehaviours)
            if (item != null)
                item.enabled = false;

        menuCanvas.SetActive(true);
    }

    private void LateUpdate()
    {
        if (gameplayStarted || Camera.main == null)
            return;

        Transform head = Camera.main.transform;
        Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.0001f)
            return;

        forward.Normalize();
        menuCanvas.transform.SetPositionAndRotation(
            head.position + forward * menuDistance,
            Quaternion.LookRotation(forward, Vector3.up));
    }

    public void Play()
    {
        if (gameplayStarted)
            return;

        gameplayStarted = true;
        foreach (GameObject item in gameplayObjects)
            if (item != null)
                item.SetActive(true);

        foreach (Behaviour item in gameplayBehaviours)
            if (item != null)
                item.enabled = true;

        menuCanvas.SetActive(false);
    }
}
