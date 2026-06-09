using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace TEVR.Core
{
    /// <summary>
    /// Requests every Android runtime permission TrueEchoVR needs, up-front, the first time the app
    /// launches. Without an explicit runtime request the user is never prompted on device, which is
    /// why QR scanning (USE_SCENE) and passthrough-camera streaming (CAMERA) silently failed.
    ///
    /// Declaring a permission in the AndroidManifest is necessary but NOT sufficient for the
    /// "dangerous"/special permissions used here — they must also be granted at runtime.
    ///
    /// This runs in the Bootstrap scene before the user reaches the login flow. It batches the
    /// requests into a single OS prompt sequence so the user grants everything once.
    /// </summary>
    public class PermissionsBootstrapper : MonoBehaviour
    {
        public const string ScenePermission = "com.oculus.permission.USE_SCENE";
        public const string CameraPermission = "android.permission.CAMERA";
        // Meta Passthrough Camera Access (PCA) exposes the headset cameras via WebCamTexture, but ONLY if
        // this runtime ("dangerous") permission is granted IN ADDITION to android.permission.CAMERA.
        // Declaring it in the manifest is necessary but NOT sufficient — without the runtime grant the
        // passthrough WebCamTexture delivers no frames (black composite background).
        public const string HeadsetCameraPermission = "horizonos.permission.HEADSET_CAMERA";
        public const string RecordAudioPermission = "android.permission.RECORD_AUDIO";

        [Header("Permissions to request at startup")]
        [Tooltip("Quest scene/spatial-data permission. Required for QR-code & trackable detection (MRUK).")]
        public bool requestScenePermission = true;

        [Tooltip("Camera permission. Required for Meta Passthrough Camera Access (real-world video streaming).")]
        public bool requestCameraPermission = true;

        [Tooltip("Meta HEADSET_CAMERA permission. Required (with CAMERA) for Passthrough Camera Access frames.")]
        public bool requestHeadsetCameraPermission = true;

        [Tooltip("Microphone permission. Only needed if you stream the headset microphone over WebRTC.")]
        public bool requestMicrophonePermission = false;

        [Tooltip("If true, persists across scene loads so the request is not repeated on every scene.")]
        public bool persistAcrossScenes = true;

        /// <summary>Fires once the startup permission flow has resolved (granted-or-denied for all).</summary>
        public static event Action OnPermissionsResolved;

        /// <summary>True after the startup flow has completed (regardless of grant/deny outcome).</summary>
        public static bool PermissionsResolved { get; private set; }

        public static bool HasCameraPermission => HasPermission(CameraPermission);
        public static bool HasScenePermission => HasPermission(ScenePermission);
        public static bool HasHeadsetCameraPermission => HasPermission(HeadsetCameraPermission);

        private static bool _started;

        private void Awake()
        {
            if (persistAcrossScenes)
            {
                // Only one bootstrapper should ever exist.
                if (_started)
                {
                    Destroy(gameObject);
                    return;
                }
                _started = true;
                DontDestroyOnLoad(gameObject);
            }
        }

        private void Start()
        {
            RequestAll();
        }

        /// <summary>Public entry point so UI can re-trigger the prompt if the user initially declined.</summary>
        public void RequestAll()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            // NOTE: Android / Horizon OS runtime permission dialogs CANNOT be programmatically pre-checked
            // or auto-granted — the OS owns the Allow/Deny default for "dangerous"/special permissions
            // (security requirement). The best we can do so the camera grant is not missed is to prompt for
            // the camera permissions FIRST (they appear before the scene/mic prompts), and re-prompt on deny.
            var toRequest = new List<string>();
            if (requestCameraPermission && !Permission.HasUserAuthorizedPermission(CameraPermission)) toRequest.Add(CameraPermission);
            if (requestHeadsetCameraPermission && !Permission.HasUserAuthorizedPermission(HeadsetCameraPermission)) toRequest.Add(HeadsetCameraPermission);
            if (requestScenePermission && !Permission.HasUserAuthorizedPermission(ScenePermission)) toRequest.Add(ScenePermission);
            if (requestMicrophonePermission && !Permission.HasUserAuthorizedPermission(RecordAudioPermission)) toRequest.Add(RecordAudioPermission);

            if (toRequest.Count == 0)
            {
                Debug.Log("[PermissionsBootstrapper] All required permissions already granted.");
                Resolve();
                return;
            }

            Debug.Log("[PermissionsBootstrapper] Requesting permissions: " + string.Join(", ", toRequest));

            var callbacks = new PermissionCallbacks();
            int outstanding = toRequest.Count;
            void One(string _)
            {
                outstanding--;
                if (outstanding <= 0) Resolve();
            }
            callbacks.PermissionGranted += p => { Debug.Log("[PermissionsBootstrapper] GRANTED: " + p); One(p); };
            callbacks.PermissionDenied += p => { Debug.LogWarning("[PermissionsBootstrapper] DENIED: " + p); One(p); };
            callbacks.PermissionDeniedAndDontAskAgain += p => { Debug.LogWarning("[PermissionsBootstrapper] DENIED (don't ask again): " + p); One(p); };

            Permission.RequestUserPermissions(toRequest.ToArray(), callbacks);
#else
            // Editor / non-Android: nothing to request (use Quest Link "Spatial Data" + a webcam for testing).
            Debug.Log("[PermissionsBootstrapper] Editor/non-Android: skipping runtime permission requests.");
            Resolve();
#endif
        }

        private void Resolve()
        {
            PermissionsResolved = true;
            OnPermissionsResolved?.Invoke();
        }

        private static bool HasPermission(string permission)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return Permission.HasUserAuthorizedPermission(permission);
#else
            return true;
#endif
        }
    }
}
