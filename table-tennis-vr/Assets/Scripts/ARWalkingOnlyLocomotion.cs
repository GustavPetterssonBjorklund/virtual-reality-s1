using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;

[DefaultExecutionOrder(-100)]
public sealed class ARWalkingOnlyLocomotion : MonoBehaviour
{
    private void Start()
    {
        ConfigureWalkingOnly();
    }

    private static void ConfigureWalkingOnly()
    {
        ControllerInputActionManager[] managers =
            FindObjectsByType<ControllerInputActionManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (ControllerInputActionManager manager in managers)
        {
            // This option suppresses teleport aiming. The actual movement
            // providers below remain disabled, so it cannot enable movement.
            manager.smoothMotionEnabled = true;
        }

        // Disable all providers so MR uses physical walking
        // and turning, and table rotation cannot also rotate the XR origin.
        foreach (LocomotionProvider provider in
                 FindObjectsByType<LocomotionProvider>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            provider.enabled = false;
        }
    }
}
