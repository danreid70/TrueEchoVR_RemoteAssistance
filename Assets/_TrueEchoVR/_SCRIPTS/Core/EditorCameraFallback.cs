using UnityEngine;

namespace TEVR
{
    /// <summary>
    /// Ensures a camera is rendering in the Unity Editor when no XR headset is connected.
    /// This prevents the "Display 1 No cameras rendering" error during development.
    /// </summary>
    public class EditorCameraFallback : MonoBehaviour
    {
        [Header("Settings")]
        public bool forceOnInEditor = true;
        
        private Camera _camera;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            if (_camera == null) _camera = gameObject.AddComponent<Camera>();
        }

        private void LateUpdate()
        {
#if UNITY_EDITOR
            // If we are in the editor and XR is not actively rendering to a headset,
            // ensure this camera is rendering to the Game View.
            if (forceOnInEditor && !UnityEngine.XR.XRSettings.isDeviceActive)
            {
                if (!_camera.enabled)
                {
                    _camera.enabled = true;
                    Debug.Log($"[EditorCameraFallback] Forcing {_camera.name} enabled for Editor preview.");
                }

                // Ensure it targets the main display and renders both eyes (for 2D preview)
                if (_camera.targetDisplay != 0) _camera.targetDisplay = 0;
                if (_camera.stereoTargetEye != StereoTargetEyeMask.Both && _camera.stereoTargetEye != StereoTargetEyeMask.None)
                {
                    _camera.stereoTargetEye = StereoTargetEyeMask.Both;
                }
                
                // Ensure tag is correct for UIManager to find it
                if (!gameObject.CompareTag("MainCamera")) gameObject.tag = "MainCamera";
            }
#endif
        }
    }
}
