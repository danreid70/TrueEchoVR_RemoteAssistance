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
            // Dev-only: bypass the backend and jump straight into an offline demo session once.
            if (bypassInitialization && !_sessionEntered && qrManager != null)
            {
                EnterDemoSession();
            }
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

        private bool _sessionEntered = false;

        private IEnumerator InitializationPhase()
        {
            isInitializing = true;
            InitializationComplete = false;
            _sessionEntered = false;

            // Always start at the Login window. Even with stored credentials the user explicitly presses
            // Sign In (fields are pre-populated), which drives EnterLiveSession via OnSignInPressed ->
            // RegisterAndBoot. This guarantees a clean, predictable entry every launch.
            UIManager.Instance?.SetState(UIManager.UIState.Login);
            yield break;
        }

        /// <summary>
        /// Transitions into the live Session (idempotent). Shows the session window, starts Full-mode QR
        /// detection, subscribes to add/remove for the dropdown, and surfaces a non-blocking calibration hint.
        /// Does NOT wait for a RoomAnchor — discovery is handled separately by OnRoomAnchorDiscovered.
        /// </summary>
        public void EnterLiveSession()
        {
            if (_sessionEntered) return;
            _sessionEntered = true;
            isInitializing = false;
            InitializationComplete = true;

            if (qrManager != null)
            {
                qrManager.SetScanMode(QrCodeManager.ScanMode.Full);
                // Session QR detection DEFAULTS OFF. The operator starts it explicitly via the Detection
                // toggle so the visible state (toggle label + indicator) always matches reality. This also
                // tears down any SignIn-phase detection that was running so the session starts clean.
                qrManager.StopQRCodeDetection();

                // Idempotent subscription (avoid double-add if entered via multiple paths).
                qrManager.OnQRCodeAdded -= OnQRCodeAddedNormal;
                qrManager.OnQRCodeUpdated -= OnQRCodeUpdatedNormal;
                qrManager.OnQRCodeRemoved -= OnQRCodeRemovedNormal;
                qrManager.OnQRCodeAdded += OnQRCodeAddedNormal;
                qrManager.OnQRCodeUpdated += OnQRCodeUpdatedNormal;
                qrManager.OnQRCodeRemoved += OnQRCodeRemovedNormal;
            }

            UIManager.Instance?.SetState(UIManager.UIState.Session);
            UIManager.Instance?.AppendChatMessage("<color=green>[Init]</color> System Ready.");

            // Non-blocking calibration hint. The session is fully usable now; scanning the RoomAnchor
            // simply refines where item codes are placed.
            if (qrManager != null && qrManager.RoomAnchorInstance == null && statusUI != null)
                statusUI.ShowMessage("Scan the Room Anchor QR to calibrate item positions.",
                                     "Optional — the session is ready.", true);
        }

        /// <summary>
        /// Resets the flow so the user can return to the Sign-In window and start fresh (or re-sign in
        /// with stored credentials). Unsubscribes QR callbacks and clears the session-entered latch.
        /// Does NOT wipe stored credentials — Sign In stays available without re-scanning the setup code.
        /// </summary>
        public void ResetForNewSession()
        {
            _sessionEntered = false;
            isInitializing = true;
            InitializationComplete = false;

            if (qrManager != null)
            {
                qrManager.OnQRCodeAdded -= OnQRCodeAddedNormal;
                qrManager.OnQRCodeUpdated -= OnQRCodeUpdatedNormal;
                qrManager.OnQRCodeRemoved -= OnQRCodeRemovedNormal;

                // Back out of the session's Full scan mode into the SignIn phase so the Sign-In/setup
                // QR (which is suppressed during a session) can be detected again to sign in afresh.
                qrManager.SetScanMode(QrCodeManager.ScanMode.LoginOnly);
                if (!qrManager.IsDetecting) qrManager.StartQRCodeDetection();
            }

            // Return to the Login window. Stored credentials remain, so the user can Sign In again
            // (no re-scan needed) — or re-scan a new Login Code, or use Demo Mode.
            UIManager.Instance?.SetState(UIManager.UIState.Login);
        }

        /// <summary>
        /// Enters an offline DEMO session that behaves exactly like a normal session, minus the backend.
        /// REAL QR detection runs: the user scans their RoomAnchor + item codes as usual, and every detected
        /// code is registered and pointable in the dropdown. Because no "legit" list was downloaded, detected
        /// codes are simply colour-coded as "detected but unlisted" — the normal classification path.
        /// </summary>
        public void EnterDemoSession()
        {
            // Demo credentials satisfy the HasCredentials gating without contacting the backend.
            if (webAppManager != null && !webAppManager.HasCredentials)
                webAppManager.EnterDemoCredentials();

            // Enter the live session normally. EnterLiveSession starts Full-mode detection, so real codes
            // are tracked and added to the "Look At" dropdown with their normal colour classification.
            EnterLiveSession();

            if (statusUI != null)
                statusUI.ShowMessage("DEMO MODE (offline)", "Scan your Room Anchor, then item QR codes as usual.", true);
            UIManager.Instance?.AppendChatMessage("<color=orange>[Demo]</color> Offline session — detecting real QR codes (none marked legit without a backend list).");
        }

        /// <summary>
        /// RoomAnchor discovered. NON-blocking: the session is already live; this just confirms calibration,
        /// places any dormant item codes, and clears the optional calibration hint. Never changes UI state.
        /// </summary>
        private void OnRoomAnchorDiscovered(QrCodeManager.QRCodeInstance anchor)
        {
            if (anchor == null) return;

            Debug.Log("[SessionFlowManager] RoomAnchor established.");
            UIManager.Instance?.AppendChatMessage("<color=green>[Calibration]</color> Room Anchor established — item positions calibrated.");

            // Clear the optional "scan the room anchor" hint now that calibration is done.
            if (statusUI != null) statusUI.ShowMessage("Room Anchor calibrated.", "", false);

            // Ensure we are in the live session (covers the case where the anchor is found before sign-in
            // completes in unusual orderings).
            if (!_sessionEntered && webAppManager != null && webAppManager.HasCredentials)
                EnterLiveSession();
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
