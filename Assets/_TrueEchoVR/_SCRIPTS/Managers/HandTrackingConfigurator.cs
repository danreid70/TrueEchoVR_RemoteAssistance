using UnityEngine;

namespace TEVR
{
    /// <summary>
    /// Configures the XR Rig to force Hand Tracking only on Quest 3 hardware
    /// while allowing controllers in the Unity Editor for easier development.
    /// </summary>
    public class HandTrackingConfigurator : MonoBehaviour
    {
        [Header("Rig References")]
        public GameObject leftController;
        public GameObject rightController;
        public GameObject leftHand;
        public GameObject rightHand;

        private void Awake()
        {
            // Auto-find if not assigned
            if (leftController == null) leftController = GameObject.Find("Left Controller");
            if (rightController == null) rightController = GameObject.Find("Right Controller");
            if (leftHand == null) leftHand = GameObject.Find("Left Hand");
            if (rightHand == null) rightHand = GameObject.Find("Right Hand");

#if UNITY_ANDROID && !UNITY_EDITOR
            // On Quest 3 device: FORCE HANDS ONLY
            Debug.Log("[HandTrackingConfigurator] Quest 3 detected. Disabling controllers, enabling hands.");
            if (leftController) leftController.SetActive(false);
            if (rightController) rightController.SetActive(false);
            if (leftHand) leftHand.SetActive(true);
            if (rightHand) rightHand.SetActive(true);
#else
            // In Editor or Windows: Support Controllers for dev
            Debug.Log("[HandTrackingConfigurator] Editor/Windows detected. Enabling controllers for development.");
            if (leftController) leftController.SetActive(true);
            if (rightController) rightController.SetActive(true);
            
            // We usually keep hands enabled in editor if using Hand Simulation, 
            // but we can let XRInputModalityManager handle the switch if it's there.
#endif
        }
    }
}
