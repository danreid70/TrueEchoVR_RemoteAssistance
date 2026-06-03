using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using System.Collections;

namespace TEVR.Core
{
    /// <summary>
    /// Ensures the XR Rig persists across scenes and remains clean of locomotion systems.
    /// Also handles Editor-specific setup for the XR Device Simulator and UI interaction.
    /// </summary>
    public class PersistentXRRig : MonoBehaviour
    {
        public static PersistentXRRig Instance { get; private set; }

        [Header("Locomotion Purging")]
        [Tooltip("GameObjects with these names (or containing these strings) will be destroyed.")]
        public string[] locomotionNamePatterns = {
            "Locomotion",
            "Teleport",
            "SnapTurn",
            "Snap Turn",
            "Turner",
            "Step",
            "Slide",
            "Locomotor",
            "Tunneling",
            "CharacterController",
            "FirstPersonLocomotor"
        };

        [Header("Simulator Support")]
        public InputActionAsset simulatorActions;
        public InputActionAsset xriDefaultActions;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
                
                SetupRigForEnvironment();
                PurgeLocomotion();
                MakeOtherBlocksPersistent();
                
                SceneManager.sceneLoaded += OnSceneLoaded;
            }
            else
            {
                Destroy(gameObject);
                return;
            }
        }

        private void Start()
        {
            SetupRigForEnvironment();
            PurgeLocomotion();
            StartCoroutine(EnsureInputModuleActive());
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Debug.Log($"[PersistentXRRig] Scene loaded: {scene.name}. Verifying rig and UI setup...");
            SetupRigForEnvironment();
            LinkCanvasesToCamera();
            StartCoroutine(EnsureInputModuleActive());
        }

        private void SetupRigForEnvironment()
        {
            Camera mainCam = EnableCamerasForRendering();

#if UNITY_EDITOR
            // 1. Disable Meta's OVRCameraRig in Editor to allow Simulator/TrackedPoseDriver to work
            var ovrRig = GetComponentInChildren(System.Type.GetType("OVRCameraRig, Oculus.VR"));
            if (ovrRig == null) ovrRig = GetComponentInChildren(System.Type.GetType("OVRCameraRig"));
            if (ovrRig != null && ovrRig is MonoBehaviour mb)
            {
                if (mb.enabled)
                {
                    Debug.Log("[PersistentXRRig] Disabling OVRCameraRig in Editor to allow Simulator control.");
                    mb.enabled = false;
                }
            }

            // 2. Ensure TrackedPoseDriver is on CenterEyeAnchor and configured
            if (mainCam != null)
            {
                var tpd = mainCam.GetComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
                if (tpd == null) tpd = mainCam.gameObject.AddComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
                
                // Configure bindings for HMD
                tpd.positionInput = new InputActionProperty(new InputAction("Position", binding: "<HMD>/centerEyePosition"));
                tpd.rotationInput = new InputActionProperty(new InputAction("Rotation", binding: "<HMD>/centerEyeRotation"));
                
                // Ensure update type is correct
                tpd.updateType = UnityEngine.InputSystem.XR.TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
            }

            // 3. Ensure InputActionManager is present and active
            var inputManager = GetComponent<InputActionManager>();
            if (inputManager == null) inputManager = gameObject.AddComponent<InputActionManager>();
            
            if (xriDefaultActions != null)
            {
                if (inputManager.actionAssets == null) inputManager.actionAssets = new System.Collections.Generic.List<InputActionAsset>();
                if (!inputManager.actionAssets.Contains(xriDefaultActions))
                {
                    inputManager.actionAssets.Add(xriDefaultActions);
                    Debug.Log("[PersistentXRRig] Added XRI Default Actions to InputActionManager.");
                }
            }

            // 4. Force Enable Simulator Actions if provided
            if (simulatorActions != null)
            {
                simulatorActions.Enable();
                Debug.Log("[PersistentXRRig] Enabled Simulator Actions.");
            }

            // 5. Setup Simulator link
            GameObject simObj = GameObject.Find("XR Device Simulator");
            if (simObj != null && mainCam != null)
            {
                var sim = simObj.GetComponent("UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation.XRDeviceSimulator");
                if (sim != null)
                {
                    var camProp = sim.GetType().GetField("m_CameraTransform", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    if (camProp != null) camProp.SetValue(sim, mainCam.transform);
                    
                    // Boost mouse sensitivity in Editor
                    var xSens = sim.GetType().GetField("m_MouseXRotateSensitivity", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    var ySens = sim.GetType().GetField("m_MouseYRotateSensitivity", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    if (xSens != null) xSens.SetValue(sim, 1.0f);
                    if (ySens != null) ySens.SetValue(sim, 1.0f);
                    
                    // Set Desired Cursor Lock Mode to None initially so the user can see the mouse
                    var lockProp = sim.GetType().GetField("m_DesiredCursorLockMode", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    if (lockProp != null) lockProp.SetValue(sim, CursorLockMode.None);
                }
            }
#endif
        }

        private IEnumerator EnsureInputModuleActive()
        {
            // Wait for other modules to initialize and potentially disable this one
            yield return new WaitForSeconds(0.5f);

            GameObject esObj = GameObject.Find("EventSystem");
            if (esObj != null)
            {
                var inputModule = esObj.GetComponent<InputSystemUIInputModule>();
                if (inputModule != null && !inputModule.enabled)
                {
                    Debug.Log("[PersistentXRRig] Force-enabling InputSystemUIInputModule for Editor mouse support.");
                    inputModule.enabled = true;
                }
            }
        }

        private Camera EnableCamerasForRendering()
        {
            Camera centerCam = null;
            Camera[] cams = GetComponentsInChildren<Camera>(true);
            foreach (var cam in cams)
            {
                if (cam.name == "CenterEyeAnchor")
                {
                    centerCam = cam;
                    cam.enabled = true;
                    cam.gameObject.SetActive(true);
                    
#if UNITY_EDITOR
                    if (cam.clearFlags == CameraClearFlags.Skybox)
                    {
                        cam.clearFlags = CameraClearFlags.SolidColor;
                        cam.backgroundColor = new Color(0.1f, 0.1f, 0.1f, 1.0f);
                    }
#endif
                }
            }
            return centerCam;
        }

        private void LinkCanvasesToCamera()
        {
            Camera mainCam = null;
            Camera[] cams = GetComponentsInChildren<Camera>(true);
            foreach (var cam in cams)
            {
                if (cam.name == "CenterEyeAnchor") { mainCam = cam; break; }
            }

            if (mainCam == null) return;

            Canvas[] canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var canvas in canvases)
            {
                if (canvas.renderMode == RenderMode.WorldSpace && canvas.worldCamera == null)
                {
                    canvas.worldCamera = mainCam;
                    Debug.Log($"[PersistentXRRig] Linked canvas {canvas.name} to {mainCam.name}");
                }
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                SceneManager.sceneLoaded -= OnSceneLoaded;
            }
        }

        private void MakeOtherBlocksPersistent()
        {
            GameObject[] rootObjects = SceneManager.GetActiveScene().GetRootGameObjects();
            foreach (var go in rootObjects)
            {
                if (go == gameObject) continue;
                if (go.name.Contains("[BuildingBlock]") || go.name.Contains("Simulator") || go.name == "EventSystem")
                {
                    Debug.Log($"[PersistentXRRig] Making {go.name} persistent.");
                    DontDestroyOnLoad(go);
                }
            }
        }

        public void PurgeLocomotion()
        {
            System.Action<Object> smartDestroy = (obj) => {
                if (obj == null) return;
                if (Application.isPlaying) Destroy(obj);
                else DestroyImmediate(obj);
            };

            if (TryGetComponent<CharacterController>(out var cc)) smartDestroy(cc);

            var allComponents = GetComponents<Component>();
            foreach (var comp in allComponents)
            {
                if (comp == null || comp is Transform || comp is PersistentXRRig || comp is InputActionManager) continue;
                
                string typeName = comp.GetType().Name;
                foreach (var pattern in locomotionNamePatterns)
                {
                    if (typeName.Contains(pattern, System.StringComparison.OrdinalIgnoreCase))
                    {
                        smartDestroy(comp);
                        break;
                    }
                }
            }

            Transform[] children = GetComponentsInChildren<Transform>(true);
            for (int i = children.Length - 1; i >= 0; i--)
            {
                Transform child = children[i];
                if (child == null || child == transform) continue;

                foreach (var pattern in locomotionNamePatterns)
                {
                    if (child.name.Contains(pattern, System.StringComparison.OrdinalIgnoreCase))
                    {
                        smartDestroy(child.gameObject);
                        break;
                    }
                }
            }
        }
    }
}
