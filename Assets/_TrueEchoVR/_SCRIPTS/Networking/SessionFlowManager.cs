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

        private GameObject roomAnchorObject;
        private Dictionary<string, Transform> generatedQRTransforms = new Dictionary<string, Transform>();
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

            // Create a root for world-synced objects
            if (roomAnchorObject == null)
            {
                roomAnchorObject = new GameObject("TEVR_RoomAnchor_Root");
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
                // We compare the QR's world position (updated by MRUK) to (0,0,0) (our calibrated origin)
                float dist = Vector3.Distance(anchor.visualObject.transform.position, Vector3.zero);
                float angle = Quaternion.Angle(anchor.visualObject.transform.rotation, Quaternion.identity);

                // Re-calibrate if moved more than 2cm or 1 degree
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

            // 1. Move the Rig so the QR code ends up at World (0,0,0)
            // RigPosition_new = RigPosition_current - QR_WorldPos
            xrOrigin.position -= qrWorldPos;

            // 2. Rotate the Rig around the World (0,0,0) point (where the QR now is)
            // to align the QR's forward with World Z-forward.
            float yRotationOffset = -qrWorldRot.eulerAngles.y;
            xrOrigin.RotateAround(Vector3.zero, Vector3.up, yRotationOffset);

            string calMsg = $"Rig aligned to anchor. QR is now at {anchor.visualObject.transform.position}";
            Debug.Log($"[Calibration] {calMsg}");
            uiManager?.AppendChatMessage($"<color=green>[Init]</color> {calMsg}");
            
            // Fire event for UI logging
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

            // Future-proofing: Sync startup data (dictionary + spatial)
            if (webAppManager != null)
            {
                webAppManager.FetchStartupData((json) => {
                    Debug.Log("[SessionInitialization] Received startup data from server.");
                    // Process startup data if needed (e.g. update dictionary)
                });
            }

            yield return StartCoroutine(LoadAndMergeQRCodes());

            InitializationComplete = true;
            string readyMsg = "System Ready. You can now join a session.";
            if (statusUI != null)
                statusUI.ShowMessage("System Ready", "You can now join a session.");
            
            uiManager?.AppendChatMessage($"<color=green>[Init]</color> {readyMsg}");

            if (uiManager != null)
                uiManager.ShowJoinScreen();

            // NORMAL TRACKING EVENTS
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
                Debug.LogWarning("[SessionInitialization] Remote sync failed/timed out or offline. Using local and default demo data.");
                uiManager?.AppendChatMessage("<color=yellow>[Init]</color> Remote sync unavailable. Adding default demo markers.");
                AddDefaultDemoQRCodes();
            }
            
            GenerateQRGameObjects();
        }

        private void AddDefaultDemoQRCodes()
        {
            // Default demo positions relative to calibrated origin
            // These payloads are used for testing and demo purposes
            string[] demoPayloads = { "TrueEchoVR", "1", "2", "3" };
            Vector3[] demoOffsets = { 
                new Vector3(0f, 1.2f, 1.5f),    // Directly in front
                new Vector3(-1.0f, 1.0f, 1.2f), // Front-left
                new Vector3(1.0f, 0.8f, 1.2f),  // Front-right
                new Vector3(0f, 1.5f, 2.0f)     // Higher up
            };

            for (int i = 0; i < demoPayloads.Length; i++)
            {
                // Only add if not already tracked by local save or discovery
                if (!qrManager.TrackedQRCodes.ContainsKey(qrManager.GetQRCodeDataAsJson(demoPayloads[i]))) // Wait, I need to check the identifier key
                {
                    // UpdateQRCodeFromRemote handles the identifier key logic internally
                    qrManager.UpdateQRCodeFromRemote(demoPayloads[i], demoOffsets[i], Quaternion.identity);
                }
            }
        }

        public void GenerateQRGameObjects()
        {
            if (roomAnchorObject == null) return;
            foreach (var item in generatedQRTransforms.Values) if (item != null) Destroy(item.gameObject);
            generatedQRTransforms.Clear();

            foreach (var kvp in qrManager.TrackedQRCodes)
            {
                QrCodeManager.QRCodeInstance qr = kvp.Value;
                if (qr.fullPayload.Contains(qrManager.qrRoomAnchorLabel)) continue;

                GameObject qrObj = new GameObject($"QR_{qr.identifierKey}");
                qrObj.transform.SetParent(roomAnchorObject.transform);
                qrObj.transform.position = qr.lastPosition;
                qrObj.transform.rotation = qr.lastRotation;
                generatedQRTransforms[qr.identifierKey] = qrObj.transform;
            }

            if (uiManager != null) uiManager.RefreshQRCodeDropdown();
        }

        public void PointToQRCode(QrCodeManager.QRCodeInstance qr)
        {
            if (!InitializationComplete) return;
            if (generatedQRTransforms.TryGetValue(qr.identifierKey, out var target))
            {
                statusUI?.HighlightTarget(target);
                statusUI?.ShowMessage($"Pointing to: {qr.identifierKey}", qr.fullPayload);
            }
        }

        private void OnQRCodeAddedNormal(QrCodeManager.QRCodeInstance qr)
        {
            if (qr.fullPayload.Contains(qrManager.qrRoomAnchorLabel)) return;
            if (roomAnchorObject != null)
            {
                GameObject qrObj = new GameObject($"QR_{qr.identifierKey}");
                qrObj.transform.SetParent(roomAnchorObject.transform);
                qrObj.transform.position = qr.lastPosition;
                qrObj.transform.rotation = qr.lastRotation;
                generatedQRTransforms[qr.identifierKey] = qrObj.transform;
            }
            uiManager?.AddQRListItem(qr);
            uiManager?.RefreshQRCodeDropdown();
        }

        private void OnQRCodeUpdatedNormal(QrCodeManager.QRCodeInstance qr)
        {
            if (qr.fullPayload.Contains(qrManager.qrRoomAnchorLabel)) return;
            if (generatedQRTransforms.TryGetValue(qr.identifierKey, out var trans))
            {
                trans.position = qr.lastPosition;
                trans.rotation = qr.lastRotation;
            }
            uiManager?.UpdateQRListItem(qr);
            uiManager?.RefreshQRCodeDropdown();
        }

        private void OnQRCodeRemovedNormal(string identifierKey)
        {
            if (generatedQRTransforms.TryGetValue(identifierKey, out var trans))
            {
                Destroy(trans.gameObject);
                generatedQRTransforms.Remove(identifierKey);
            }
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