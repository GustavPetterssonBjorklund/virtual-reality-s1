using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;

[DefaultExecutionOrder(-100)]
public sealed class ARWalkingOnlyLocomotion : MonoBehaviour
{
    private void Start()
    {
        DisableContinuousMotion();
    }

    private static void DisableContinuousMotion()
    {
        ControllerInputActionManager[] managers =
            FindObjectsByType<ControllerInputActionManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (ControllerInputActionManager manager in managers)
        {
            manager.smoothMotionEnabled = false;
        }
    }
}
