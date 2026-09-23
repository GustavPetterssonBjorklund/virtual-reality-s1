using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Interaction.Toolkit.UI;

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
    private bool menuPlaced;
    private Camera menuCamera;

    private void Awake()
    {
        foreach (GameObject item in gameplayObjects)
            if (item != null)
                item.SetActive(false);

        foreach (Behaviour item in gameplayBehaviours)
            if (item != null)
                item.enabled = false;

        menuCanvas.SetActive(true);
        InitializeARCamera();

        // These services must run before Play, while the table is inactive.
        if (GetComponent<ARWalkingOnlyLocomotion>() == null)
            gameObject.AddComponent<ARWalkingOnlyLocomotion>();
    }

    private void Start()
    {
        foreach (InputActionManager manager in FindObjectsByType<InputActionManager>(FindObjectsSortMode.None))
            manager.EnableInput();

        foreach (XRUIInputModule module in FindObjectsByType<XRUIInputModule>(FindObjectsSortMode.None))
            module.enableXRInput = true;

        Canvas canvas = menuCanvas.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        if (canvas.GetComponent<TrackedDeviceGraphicRaycaster>() == null)
            canvas.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
    }

    private bool InitializeARCamera()
    {
        menuCamera = Camera.main;
        if (menuCamera == null)
            return false;

        ARCameraManager manager = menuCamera.GetComponent<ARCameraManager>();
        if (manager == null)
            manager = menuCamera.gameObject.AddComponent<ARCameraManager>();
        manager.enabled = true;

        ARCameraBackground background = menuCamera.GetComponent<ARCameraBackground>();
        if (background == null)
            background = menuCamera.gameObject.AddComponent<ARCameraBackground>();
        background.enabled = true;

        menuCamera.clearFlags = CameraClearFlags.SolidColor;
        menuCamera.backgroundColor = Color.clear;
        menuCanvas.GetComponent<Canvas>().worldCamera = menuCamera;
        return true;
    }

    private void LateUpdate()
    {
        if (gameplayStarted || menuPlaced)
            return;
        if (menuCamera == null && !InitializeARCamera())
            return;

        // Wait for the headset pose before placing a stable panel in the room.
        if (XRSettings.isDeviceActive &&
            (!InputDevices.GetDeviceAtXRNode(XRNode.Head).TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) || !tracked))
            return;

        Transform head = menuCamera.transform;
        Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.0001f)
            return;

        forward.Normalize();
        menuCanvas.transform.SetPositionAndRotation(
            head.position + forward * menuDistance,
            Quaternion.LookRotation(forward, Vector3.up));
        menuPlaced = true;
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
