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
            // These collaborators live on OTHER GameObjects (the MRUK QR manager and the UI system), not on
            // this manager object, so a same-GameObject GetComponent resolves to null — which silently broke
            // PointToQRCode (no focus glow because qrManager was null, no directional arrow because statusUI
            // was null). Resolve via the QrCodeManager singleton and scene-wide lookups (include inactive).
            if (qrManager == null)
                qrManager = QrCodeManager.Instance != null
                    ? QrCodeManager.Instance
                    : FindAnyObjectByType<QrCodeManager>(FindObjectsInactive.Include);
            if (statusUI == null) statusUI = FindAnyObjectByType<VrHudController>(FindObjectsInactive.Include);
            if (uiManager == null) uiManager = FindAnyObjectByType<SessionUiController>(FindObjectsInactive.Include);
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
                        var rigComponent = Object.FindAnyObjectByType<OVRCameraRig>();
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

        /// <summary>
        /// Handles a remote "point-to"/"look-at" command from the admin/web app. Mirrors the dropdown
        /// behaviour: whenever the referenced code exists locally, it is pointed at with the directional
        /// arrow + pulsing focus glow on the real code (cross-referenced by QR payload value first, then by
        /// friendly name). Only when the code is NOT represented locally does it fall back to the dedicated
        /// position highlight at the admin-supplied coordinates.
        /// </summary>
        private void OnRemotePointToReceived(string name, string qrCode, Vector3? position, Quaternion? rotation)
        {
            // An empty point-to (no name, no qrCode, no coordinates) means the admin cleared the selection.
            if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(qrCode) && !position.HasValue)
            {
                qrManager?.ClearFocus();
                statusUI?.ClearHighlight();
                UIManager.Instance?.remoteHighlight?.ClearForce();
                statusUI?.ShowMessage("", "");
                return;
            }

            // 1) Preferred: cross-reference a locally-represented QR code and point at it exactly like the
            //    dropdown does (arrow + pulsing focus glow on the real code).
            var match = qrManager != null ? qrManager.FindTrackedQRCode(qrCode, name) : null;
            if (match != null)
            {
                UIManager.Instance?.remoteHighlight?.ClearForce(); // avoid a duplicate position highlight
                PointToQRCode(match);
                string label = !string.IsNullOrEmpty(name) ? name : match.identifierKey;
                statusUI?.ShowMessage($"Admin pointing to: {label}", match.fullPayload);
                return;
            }

            // 2) Not represented locally yet, but the admin supplied coordinates -> indicate at those
            //    real-world coordinates with the dedicated position highlight (outline + billboard label).
            if (position.HasValue)
            {
                qrManager?.ClearFocus();
                string label = !string.IsNullOrEmpty(name) ? name : qrCode;
                UIManager.Instance?.remoteHighlight?.HighlightPosition(
                    label,
                    position.Value,
                    rotation ?? Quaternion.identity
                );
                statusUI?.ShowMessage($"Admin pointing to: {label}", "Visual highlight active.");
                return;
            }

            // 3) Nothing locally and no coordinates -> nothing to point at.
            qrManager?.ClearFocus();
            statusUI?.ClearHighlight();
            string n = !string.IsNullOrEmpty(name) ? name : qrCode;
            statusUI?.ShowMessage($"Admin pointing to: {n}", "(Object not found in room)");
        }

        private IEnumerator InitializationPhase()
        {
            isInitializing = true;
            InitializationComplete = false;

            if (webAppManager == null) yield break;

            // Step 0: Check Credentials (HasCredentials becomes true only after a valid sign-in /
            // headset registration, so this gates everything below behind a legitimate login).
            if (!webAppManager.HasCredentials)
            {
                UIManager.Instance?.SetState(UIManager.UIState.Login);
                while (!webAppManager.HasCredentials)
                    yield return null;
            }

            // Valid sign-in achieved -> leave the login-scan phase and begin the real RoomAnchor +
            // item scan. Clear any leftover login-phase boxes so they don't linger as clutter.
            if (qrManager != null)
            {
                qrManager.ClearAllVisuals();
                qrManager.SetScanMode(QrCodeManager.ScanMode.Full);
                qrManager.StartQRCodeDetection();
                qrManager.EnsureQrTrackingEnabled();
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
            if (webAppManager != null && webAppManager.StartupDataLoaded)
            {
                // Provisioning (sign-in this session) already fetched startup-data and raised
                // OnStartupDataReceived. Don't fetch it a second time here (that re-applied every QR code
                // and double-fired the event). Proceed straight to "ready".
                bootSuccess = true;
            }
            else if (webAppManager != null)
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
            if (qr == null) return;
            // Surround the pointed-at code with the pulsing focus glow until the selection is cleared.
            qrManager?.FocusQRCode(qr);

            // Point the directional arrow at the code's visual object if it has one, otherwise at its live
            // trackable (a physically-tracked code that has not yet been given a placed visual).
            Transform target = qr.visualObject != null ? qr.visualObject.transform
                             : (qr.trackable != null ? qr.trackable.transform : null);
            if (target != null)
            {
                statusUI?.HighlightTarget(target);
                statusUI?.ShowMessage($"Pointing to: {qr.identifierKey}", qr.fullPayload);
            }
        }

        private void OnQRCodeAddedNormal(QrCodeManager.QRCodeInstance qr)
        {
            UIManager.Instance?.RefreshQRCodeDropdown();
        }

        private void OnQRCodeUpdatedNormal(QrCodeManager.QRCodeInstance qr)
        {
            // PERF: OnQRCodeUpdated fires every frame from tracking jitter (position/rotation noise).
            // The dropdown CONTENTS only change on add/remove, not on movement, so rebuilding it here
            // forced a full dropdown rebuild every frame per visible code — a primary cause of the
            // jitter/hitching with many item codes. Intentionally a no-op; refresh happens on add/remove.
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
