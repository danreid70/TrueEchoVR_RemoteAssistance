using UnityEngine;
using System.Collections;
using System.Collections.Generic;

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
                Debug.LogError("[SessionInitialization] No XR Origin assigned!");
                enabled = false;
                return;
            }

            // Remove CharacterController as locomotion is physical via Passthrough
            CharacterController cc = xrOrigin.GetComponent<CharacterController>();
            if (cc != null) Destroy(cc);

            if (qrManager != null)
            {
                qrManager.SetAnchorEstablished(false);
                qrManager.OnRoomAnchorDiscovered += OnRoomAnchorDiscovered;
            }

            if (webAppManager != null)
            {
                webAppManager.OnPointToReceived += OnRemotePointToReceived;
            }

            StartCoroutine(InitializationPhase());
        }

        private void Update()
        {
            // Fallback for cases where QR is detected but event was missed or disk-loaded anchor is now seen
            if (isInitializing && !InitializationComplete && qrManager != null)
            {
                foreach (var kvp in qrManager.TrackedQRCodes)
                {
                    if (kvp.Value.fullPayload.Contains(qrManager.qrRoomAnchorLabel))
                    {
                        // Anchor is tracked. If it has a visual object and we are still initializing, trigger discovery.
                        if (kvp.Value.visualObject != null && isInitializing)
                        {
                            Debug.Log($"[SessionFlow] Fallback: Found RoomAnchor in tracked list: {kvp.Value.fullPayload}");
                            OnRoomAnchorDiscovered(kvp.Value);
                            break;
                        }
                    }
                }
            }
        }

        private void OnRemotePointToReceived(string name, string payload, string pose)
        {
            // Use payload or name to find the right tracked QR
            foreach (var kvp in qrManager.TrackedQRCodes)
            {
                if (kvp.Value.fullPayload == payload || kvp.Value.identifierKey == name)
                {
                    PointToQRCode(kvp.Value);
                    return;
                }
            }
        }

        private IEnumerator InitializationPhase()
        {
            isInitializing = true;
            InitializationComplete = false;

            string initMsg = "Look at the Room Anchor marker in the room to begin.";
            if (statusUI != null)
                statusUI.ShowMessage(initMsg, "Calibration required.", true);
            
            uiManager?.AppendChatMessage($"<color=cyan>[Init]</color> {initMsg}");

            while (isInitializing)
                yield return null;
        }

        private void OnRoomAnchorDiscovered(QrCodeManager.QRCodeInstance anchor)
        {
            if (!isInitializing)
            {
                // Drift/Movement detection for the Room Anchor
                // Thresholds: 2cm or 1 degree
                float dist = Vector3.Distance(anchor.visualObject.transform.position, Vector3.zero);
                float angle = Quaternion.Angle(anchor.visualObject.transform.rotation, Quaternion.identity);

                if (dist > 0.02f || angle > 1.0f)
                {
                    Debug.Log($"[System] Room Anchor move detected (Dist: {dist:F3}m, Angle: {angle:F1}°). Recalibrating Origin...");
                    uiManager?.AppendChatMessage($"<color=yellow>[System]</color> Room Anchor moved. Recalibrating...");
                    CalibrateOriginToAnchor(anchor);
                }
                return;
            }

            Debug.Log($"[SessionInitialization] RoomAnchor detected: {anchor.fullPayload}");
            uiManager?.AppendChatMessage($"<color=green>[Init]</color> RoomAnchor detected: {anchor.fullPayload}");
            
            // Clear the persistent "Look at Room Anchor" message
            if (statusUI != null)
                statusUI.ShowMessage("", ""); 

            CalibrateOriginToAnchor(anchor);
            StartCoroutine(CompleteInitializationAfterAnchor());
        }

        private void CalibrateOriginToAnchor(QrCodeManager.QRCodeInstance anchor)
        {
            if (xrOrigin == null) return;

            // Use the visual object's transform which represents the last seen QR pose in Unity World Space
            Vector3 qrWorldPos = anchor.visualObject.transform.position;
            Quaternion qrWorldRot = anchor.visualObject.transform.rotation;

            // Shift Rig such that physical anchor maps to virtual (0,0,0)
            xrOrigin.position -= qrWorldPos;

            // Rotate Rig around virtual (0,0,0) to align physical forward with virtual Z-forward
            float yRotationOffset = -qrWorldRot.eulerAngles.y;
            xrOrigin.RotateAround(Vector3.zero, Vector3.up, yRotationOffset);

            string calMsg = $"Rig aligned to anchor. QR is now at {anchor.visualObject.transform.position}";
            Debug.Log($"[Calibration] {calMsg}");
            uiManager?.AppendChatMessage($"<color=green>[Init]</color> {calMsg}");
            
            uiManager?.LogAllQRCodesToChat();
        }

        private IEnumerator CompleteInitializationAfterAnchor()
        {
            isInitializing = false;
            qrManager.SetAnchorEstablished(true);

            string syncMsg = "Anchor established. Syncing with cloud...";
            if (statusUI != null)
                statusUI.ShowMessage(syncMsg, "Please wait.");
            
            uiManager?.AppendChatMessage($"<color=cyan>[Init]</color> {syncMsg}");

            if (webAppManager != null)
            {
                webAppManager.FetchStartupData((json) => {
                    Debug.Log("[SessionInitialization] Received startup data from server.");
                });
            }

            yield return StartCoroutine(LoadAndMergeQRCodes());

            InitializationComplete = true;
            string readyMsg = "System Ready.";
            
            if (Application.internetReachability == NetworkReachability.NotReachable)
            {
                readyMsg += " (Offline Mode)";
                if (statusUI != null) statusUI.ShowMessage("System Ready", "Offline Mode.");
                if (uiManager != null) uiManager.ShowSessionScreen();
            }
            else
            {
                if (statusUI != null) statusUI.ShowMessage("System Ready", "You can now join a session.");
                if (uiManager != null) uiManager.ShowJoinScreen();
            }
            
            uiManager?.AppendChatMessage($"<color=green>[Init]</color> {readyMsg}");

            qrManager.OnQRCodeAdded += OnQRCodeAddedNormal;
            qrManager.OnQRCodeUpdated += OnQRCodeUpdatedNormal;
            qrManager.OnQRCodeRemoved += OnQRCodeRemovedNormal;
        }

        private IEnumerator LoadAndMergeQRCodes()
        {
            uiManager?.AppendChatMessage("<color=cyan>[Init]</color> Loading local QR codes...");
            qrManager.ManualLoad();
            
            bool pullSuccess = false;
            if (webAppManager != null && webAppManager.IsConnected)
            {
                uiManager?.AppendChatMessage("<color=cyan>[Init]</color> Requesting remote QR codes...");
                System.Action<string> pullCallback = null;
                pullCallback = (json) => {
                    qrManager.ManualLoadFromJson(json);
                    pullSuccess = true;
                    webAppManager.OnQRCodesPulled -= pullCallback;
                };
                webAppManager.OnQRCodesPulled += pullCallback;
                webAppManager.PullQRCodes();

                float start = Time.time;
                while (!pullSuccess && Time.time - start < pullTimeoutSeconds)
                    yield return null;
            }

            if (pullSuccess)
            {
                Debug.Log("[SessionInitialization] Remote sync complete.");
                uiManager?.AppendChatMessage("<color=green>[Init]</color> Remote sync complete.");
            }
            else
            {
                Debug.LogWarning("[SessionInitialization] Remote sync failed/timed out. Using local/demo.");
                uiManager?.AppendChatMessage("<color=yellow>[Init]</color> Remote sync unavailable. Adding demo markers.");
                AddDefaultDemoQRCodes();
            }
        }

        private void AddDefaultDemoQRCodes()
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
            uiManager?.AddQRListItem(qr);
            uiManager?.RefreshQRCodeDropdown();
        }

        private void OnQRCodeUpdatedNormal(QrCodeManager.QRCodeInstance qr)
        {
            uiManager?.UpdateQRListItem(qr);
            uiManager?.RefreshQRCodeDropdown();
        }

        private void OnQRCodeRemovedNormal(string identifierKey)
        {
            uiManager?.RemoveQRListItem(identifierKey);
            uiManager?.RefreshQRCodeDropdown();
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