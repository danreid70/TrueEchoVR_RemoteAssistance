using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using TEVR.Core;

namespace TEVR
{
    public class SessionFlowManager : MonoBehaviour
    {
        [Header("References (assign manually)")]
        public Transform xrOrigin;
        public QrCodeManager qrManager;
        public VrHudController statusUI;
        public SessionUiController uiManager;
        public SignalingManager webAppManager;

        [Header("Settings")]
        public float pullTimeoutSeconds = 5f;
        [Tooltip("If true, skips web app login and calibration for testing.")]
        public bool bypassInitialization = false;

        public bool InitializationComplete { get; private set; } = false;
        private bool isInitializing = true;

        private void Start()
        {
            if (qrManager == null) qrManager = GetComponent<QrCodeManager>();
            if (statusUI == null) statusUI = GetComponent<VrHudController>();
            if (uiManager == null) uiManager = GetComponent<SessionUiController>();
            if (webAppManager == null) webAppManager = SignalingManager.Instance;
            
            if (xrOrigin == null)
            {
                // Prioritize the persistent rig instance
                if (PersistentXRRig.Instance != null)
                {
                    xrOrigin = PersistentXRRig.Instance.transform;
                }
                else
                {
                    // Fallback to searching
                    var rig = GameObject.Find("[BuildingBlock] Camera Rig");
                    if (rig == null) rig = GameObject.Find("[[BuildingBlock] Camera Rig]");

                    // Robust fallback: search for the OVRCameraRig component
                    if (rig == null)
                    {
                        var rigComponent = Object.FindObjectOfType<OVRCameraRig>();
                        if (rigComponent != null) rig = rigComponent.gameObject;
                    }

                    if (rig != null)
                    {
                        xrOrigin = rig.transform;
                    }
                }
            }

            if (xrOrigin == null)
            {
                Debug.LogWarning("[SessionInitialization] No XR Origin found yet. Waiting for scene load.");
            }
            else
            {
                InitializeOrigin();
            }

            // ... (rest of Start)
        }

        private void InitializeOrigin()
        {
            if (xrOrigin == null) return;
            
            // Persistence is now handled by PersistentXRRig if present, 
            // but we keep this as a safeguard for standalone rigs.
            if (xrOrigin.GetComponent<PersistentXRRig>() == null)
            {
                DontDestroyOnLoad(xrOrigin.gameObject);
                PurgeLocomotion(xrOrigin.gameObject);
            }

            if (qrManager != null)
{
                qrManager.OnRoomAnchorDiscovered += OnRoomAnchorDiscovered;
            }

            if (webAppManager != null)
            {
                webAppManager.OnPointToReceived += OnRemotePointToReceived;
            }

            StartCoroutine(InitializationPhase());
        }

        private void PurgeLocomotion(GameObject rig)
        {
            Debug.Log("[SessionInitialization] Purging locomotion systems from rig...");

            // List of locomotion GameObjects to destroy (by name patterns)
            string[] locomotionNames = {
                "Locomotion",
                "Teleport",
                "Turner",
                "Step",
                "Slide",
                "Locomotor",
                "Tunneling",
                "SnapTurn"
            };

            Transform[] children = rig.GetComponentsInChildren<Transform>(true);
            foreach (var child in children)
            {
                if (child == null || child == rig.transform) continue;

                bool isLocomotionObj = false;
                foreach (var namePart in locomotionNames)
                {
                    if (child.name.Contains(namePart))
                    {
                        isLocomotionObj = true;
                        break;
                    }
                }

                if (isLocomotionObj)
                {
                    Debug.Log($"[SessionInitialization] Destroying locomotion object: {child.name}");
                    Destroy(child.gameObject);
                }
            }

            // Also explicitly destroy components on the root if they exist
            if (rig.TryGetComponent<CharacterController>(out var cc)) Destroy(cc);
            
            // FirstPersonLocomotor is a custom Meta component usually
            var locomotor = rig.GetComponent("FirstPersonLocomotor");
            if (locomotor != null) Destroy(locomotor);
        }

        private void Update()
        {
            // Fallback: Check if anchor is already tracked but we missed the event
            if (isInitializing && !InitializationComplete && qrManager != null)
            {
                if (qrManager.RoomAnchorInstance != null)
                {
                    OnRoomAnchorDiscovered(qrManager.RoomAnchorInstance);
                }
                else if (bypassInitialization)
                {
                    // Auto-generate a fake anchor for development
                    CompleteOfflineInitialization();
                }
            }
        }

        private void CompleteOfflineInitialization()
        {
            isInitializing = false;
            InitializationComplete = true;
            Debug.Log("[SessionInitialization] Offline Bypass active. Skipping Calibration.");
            if (statusUI != null) statusUI.ShowMessage("System Ready (Offline)", "Debug mode active.");
            UIManager.Instance?.SetState(UIManager.UIState.Session);
            
            qrManager.OnQRCodeAdded += OnQRCodeAddedNormal;
            qrManager.OnQRCodeUpdated += OnQRCodeUpdatedNormal;
            qrManager.OnQRCodeRemoved += OnQRCodeRemovedNormal;
        }

        private void OnRemotePointToReceived(string name, Vector3? position, Quaternion? rotation)
        {
            if (position.HasValue)
            {
                // Enriched point-to with real-world coordinates
                UIManager.Instance?.remoteHighlight?.HighlightPosition(
                    name, 
                    position.Value, 
                    rotation ?? Quaternion.identity
                );
                
                statusUI?.ShowMessage($"Admin pointing to: {name}", "Visual highlight active.");
            }
            else
            {
                // Fallback: search for local QR by name
                foreach (var kvp in qrManager.TrackedQRCodes)
                {
                    if (kvp.Value.identifierKey == name || kvp.Value.fullPayload.Contains(name))
                    {
                        PointToQRCode(kvp.Value);
                        return;
                    }
                }
                statusUI?.ShowMessage($"Admin pointing to: {name}", "(Object not found in room)");
            }
        }

        private IEnumerator InitializationPhase()
        {
            isInitializing = true;
            InitializationComplete = false;

            if (webAppManager == null) yield break;

            // Step 0: Check Credentials
            if (!webAppManager.HasCredentials)
            {
                UIManager.Instance?.SetState(UIManager.UIState.Login);
                while (!webAppManager.HasCredentials)
                    yield return null;
            }

            // Step 1: Calibration (Room Anchor)
            string initMsg = "Look at the Room Anchor marker in the room to begin.";
            if (statusUI != null)
                statusUI.ShowMessage(initMsg, "Calibration required.", true);
            
            UIManager.Instance?.AppendChatMessage($"<color=cyan>[Init]</color> {initMsg}");

            while (isInitializing)
                yield return null;
        }

        private void OnRoomAnchorDiscovered(QrCodeManager.QRCodeInstance anchor)
        {
            if (!isInitializing) return;

            Debug.Log($"[SessionInitialization] RoomAnchor established at {anchor.visualObject.transform.position}");
            UIManager.Instance?.AppendChatMessage($"<color=green>[Init]</color> RoomAnchor established.");
            
            if (statusUI != null) statusUI.ShowMessage("", ""); 

            StartCoroutine(CompleteInitializationAfterAnchor());
        }

        private IEnumerator CompleteInitializationAfterAnchor()
        {
            isInitializing = false;

            string syncMsg = "Anchor established. Booting Platform...";
            if (statusUI != null) statusUI.ShowMessage(syncMsg, "Please wait.");
            UIManager.Instance?.AppendChatMessage($"<color=cyan>[Init]</color> {syncMsg}");

            bool bootSuccess = false;
            if (webAppManager != null)
            {
                // Add a timeout for the boot sequence
                float timeout = 10f;
                bool finished = false;
                StartCoroutine(webAppManager.EveryBootSequence((success) => {
                    bootSuccess = success;
                    finished = true;
                }));

                while (!finished && timeout > 0)
                {
                    timeout -= Time.deltaTime;
                    yield return null;
                }

                if (!finished)
                {
                    Debug.LogWarning("[SessionFlowManager] Boot sequence timed out. Falling back to Demo Mode.");
                    bootSuccess = false;
                }
            }

            if (bootSuccess)
            {
                UIManager.Instance?.AppendChatMessage("<color=green>[Init]</color> Platform sync complete.");
            }
            else
            {
                UIManager.Instance?.AppendChatMessage("<color=orange>[Init]</color> Platform sync unavailable. Entering Demo Mode.");
                // In demo mode, we might want to skip the login block
                InitializationComplete = true;
                UIManager.Instance?.SetState(UIManager.UIState.Session);
                if (statusUI != null) statusUI.ShowMessage("Demo Mode Active", "Offline visualization enabled.");
                
                qrManager.OnQRCodeAdded += OnQRCodeAddedNormal;
                qrManager.OnQRCodeUpdated += OnQRCodeUpdatedNormal;
                qrManager.OnQRCodeRemoved += OnQRCodeRemovedNormal;
                yield break;
            }

            InitializationComplete = true;
            string readyMsg = "System Ready.";
            
            UIManager.Instance?.SetState(UIManager.UIState.Session);
            
            UIManager.Instance?.AppendChatMessage($"<color=green>[Init]</color> {readyMsg}");

            qrManager.OnQRCodeAdded += OnQRCodeAddedNormal;
            qrManager.OnQRCodeUpdated += OnQRCodeUpdatedNormal;
            qrManager.OnQRCodeRemoved += OnQRCodeRemovedNormal;
        }

        public void AddDefaultDemoQRCodes()
        {
            string[] demoPayloads = { "TrueEchoVR", "1", "2", "3" };
            Vector3[] demoOffsets = { 
                new Vector3(0f, 1.2f, 1.5f),
                new Vector3(-1.0f, 1.0f, 1.2f),
                new Vector3(1.0f, 0.8f, 1.2f),
                new Vector3(0f, 1.5f, 2.0f)
            };

            for (int i = 0; i < demoPayloads.Length; i++)
            {
                qrManager.UpdateQRCodeFromRemote(demoPayloads[i], demoOffsets[i], Quaternion.identity);
            }
        }

        public void PointToQRCode(QrCodeManager.QRCodeInstance qr)
        {
            if (qr.visualObject != null)
            {
                statusUI?.HighlightTarget(qr.visualObject.transform);
                statusUI?.ShowMessage($"Pointing to: {qr.identifierKey}", qr.fullPayload);
            }
        }

        private void OnQRCodeAddedNormal(QrCodeManager.QRCodeInstance qr)
        {
            UIManager.Instance?.RefreshQRCodeDropdown();
        }

        private void OnQRCodeUpdatedNormal(QrCodeManager.QRCodeInstance qr)
        {
            UIManager.Instance?.RefreshQRCodeDropdown();
        }

        private void OnQRCodeRemovedNormal(string identifierKey)
        {
            UIManager.Instance?.RefreshQRCodeDropdown();
        }

        private void OnDestroy()
        {
            if (qrManager != null)
            {
                qrManager.OnRoomAnchorDiscovered -= OnRoomAnchorDiscovered;
                qrManager.OnQRCodeAdded -= OnQRCodeAddedNormal;
                qrManager.OnQRCodeUpdated -= OnQRCodeUpdatedNormal;
                qrManager.OnQRCodeRemoved -= OnQRCodeRemovedNormal;
            }
        }
    }
}
