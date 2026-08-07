#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;
using System.Collections;
using System.Collections.Generic;

namespace TEVR.Core
{
    [DefaultExecutionOrder(-1000)]
    public class EditorSimulationEnforcer : MonoBehaviour
    {
        private void Awake()
        {
            if (!Application.isPlaying) return;
            StartCoroutine(EnforcementLoop());
            Debug.Log("[SimulationEnforcer] Mega Enforcer Started.");
        }

        private IEnumerator EnforcementLoop()
        {
            while (true)
            {
                Enforce();
                yield return new WaitForSeconds(1.0f);
            }
        }

        private void Enforce()
        {
            // 1. Suppression of Meta & Interacting SDKs
            var allMbs = Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude);
            foreach (var mb in allMbs)
            {
                if (mb == null) continue;
                string typeName = mb.GetType().Name;
                
                // Disable Meta components that fight for control or block mouse
                if (typeName == "OVRCameraRig" || typeName == "OVRManager" || typeName == "PointableCanvasModule")
                {
                    if (mb.enabled)
                    {
                        Debug.Log("[SimulationEnforcer] Disabling Meta component: " + typeName);
                        mb.enabled = false;
                    }
                }
            }

            // 2. EventSystem Health
            var es = Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>();
            if (es != null)
            {
                var inputModule = es.GetComponent<InputSystemUIInputModule>();
                if (inputModule != null && !inputModule.enabled)
                {
                    inputModule.enabled = true;
                    Debug.Log("[SimulationEnforcer] Enabled InputSystemUIInputModule.");
                }
            }

            // 3. Simulator Logic
            var simulator = Object.FindAnyObjectByType<XRInteractionSimulator>();
            var centerCam = GetCenterEyeCamera();
            
            if (simulator != null)
            {
                if (!simulator.enabled) simulator.enabled = true;

                // Force unlock cursor if it's trapped
                if (Cursor.lockState == CursorLockMode.Locked)
                {
                    // Only unlock if the user says they can't click
                    // Actually, let's leave it to the user to toggle with '\'
                }

                // Ensure Simulator has all required assets
                var so = new UnityEditor.SerializedObject(simulator);
                
                // Check if actions are enabled
                var actionsProp = so.FindProperty("m_DeviceSimulatorActionAsset");
                if (actionsProp != null && actionsProp.objectReferenceValue != null)
                {
                    var asset = actionsProp.objectReferenceValue as InputActionAsset;
                    if (asset != null && !asset.enabled) asset.Enable();
                }

                // Link Camera
                if (centerCam != null)
                {
                    var camProp = so.FindProperty("m_CameraTransform");
                    if (camProp != null && camProp.objectReferenceValue != centerCam.transform)
                    {
                        camProp.objectReferenceValue = centerCam.transform;
                        so.ApplyModifiedProperties();
                    }

                    // TrackedPoseDriver check
                    var tpd = centerCam.GetComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
                    if (tpd != null)
                    {
                        var posAction = tpd.positionInput.action;
                        bool needsBinding = posAction == null || posAction.bindings.Count == 0 ||
                                            string.IsNullOrEmpty(posAction.bindings[0].path);
                        if (needsBinding)
                        {
                            tpd.positionInput = new InputActionProperty(new InputAction("Position", binding: "<XRHMD>/centerEyePosition"));
                            tpd.rotationInput = new InputActionProperty(new InputAction("Rotation", binding: "<XRHMD>/centerEyeRotation"));
                        }
                    }
                }
            }

            // 4. World Canvas Fix
            if (centerCam != null)
            {
                var canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include);
                foreach (var canvas in canvases)
                {
                    if (canvas.renderMode == RenderMode.WorldSpace && canvas.worldCamera == null)
                    {
                        canvas.worldCamera = centerCam;
                    }
                }
            }
        }

        private Camera GetCenterEyeCamera()
        {
            var cameras = Object.FindObjectsByType<Camera>(FindObjectsInactive.Include);
            foreach (var cam in cameras)
            {
                if (cam.name == "CenterEyeAnchor") return cam;
            }
            return Camera.main;
        }
    }
}
#endif
