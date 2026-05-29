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
            // Fallback: Check if anchor is already tracked but we missed the event
            if (isInitializing && !InitializationComplete && qrManager != null)
            {
                if (qrManager.RoomAnchorInstance != null)
                {
                    OnRoomAnchorDiscovered(qrManager.RoomAnchorInstance);
                }
            }
        }

        private void OnRemotePointToReceived(string name, Vector3? position, Quaternion? rotation)
        {
            if (position.HasValue)
            {
                // Enriched point-to
                statusUI?.ShowMessage($"Pointing to: {name}", "");
                // statusUI?.HighlightTarget requires Transform, so we'd need a temp object if we want a highlight
            }
            else
            {
                // Fallback: search for local QR
                foreach (var kvp in qrManager.TrackedQRCodes)
                {
                    if (kvp.Value.identifierKey == name)
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
                uiManager?.ShowLoginPanel();
                while (!webAppManager.HasCredentials)
                    yield return null;
            }

            // Step 1: Calibration (Room Anchor)
            string initMsg = "Look at the Room Anchor marker in the room to begin.";
            if (statusUI != null)
                statusUI.ShowMessage(initMsg, "Calibration required.", true);
            
            uiManager?.AppendChatMessage($"<color=cyan>[Init]</color> {initMsg}");

            while (isInitializing)
                yield return null;
        }

        private void OnRoomAnchorDiscovered(QrCodeManager.QRCodeInstance anchor)
        {
            if (!isInitializing) return;

            Debug.Log($"[SessionInitialization] RoomAnchor established at {anchor.visualObject.transform.position}");
            uiManager?.AppendChatMessage($"<color=green>[Init]</color> RoomAnchor established.");
            
            if (statusUI != null) statusUI.ShowMessage("", ""); 

            StartCoroutine(CompleteInitializationAfterAnchor());
        }

        private IEnumerator CompleteInitializationAfterAnchor()
        {
            isInitializing = false;

            string syncMsg = "Anchor established. Booting Platform...";
            if (statusUI != null) statusUI.ShowMessage(syncMsg, "Please wait.");
            uiManager?.AppendChatMessage($"<color=cyan>[Init]</color> {syncMsg}");

            bool bootSuccess = false;
            if (webAppManager != null)
            {
                yield return StartCoroutine(webAppManager.EveryBootSequence((success) => bootSuccess = success));
            }

            if (bootSuccess)
            {
                uiManager?.AppendChatMessage("<color=green>[Init]</color> Platform sync complete.");
            }
            else
            {
                uiManager?.AppendChatMessage("<color=red>[Init]</color> Platform sync failed. Check connection.");
                if (!webAppManager.HasCredentials)
                {
                    StartCoroutine(InitializationPhase());
                    yield break;
                }
            }

            InitializationComplete = true;
            string readyMsg = "System Ready.";
            
            if (uiManager != null) uiManager.ShowJoinScreen();
            
            uiManager?.AppendChatMessage($"<color=green>[Init]</color> {readyMsg}");

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
