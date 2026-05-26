using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace TrueEchoVR
{
    public class TroubleshootingSessionInitialization : MonoBehaviour
    {
        [Header("References (assign manually)")]
        public Transform xrOrigin;
        public QRCodeManager qrManager;
        public MainVRHUDUI statusUI;
        public TroubleshootingSessionUIManager uiManager;
        public TroubleshootingStreamingManager streamingManager;

        [Header("Settings")]
        public float pullTimeoutSeconds = 5f;

        public bool InitializationComplete { get; private set; } = false;

        private GameObject roomAnchorObject;
        private Dictionary<string, Transform> generatedQRTransforms = new Dictionary<string, Transform>();
        private bool isInitializing = true;

        private void Start()
        {
            if (qrManager == null) qrManager = GetComponent<QRCodeManager>();
            if (statusUI == null) statusUI = GetComponent<MainVRHUDUI>();
            if (uiManager == null) uiManager = GetComponent<TroubleshootingSessionUIManager>();
            if (streamingManager == null) streamingManager = GetComponent<TroubleshootingStreamingManager>();
            
            if (xrOrigin == null)
            {
                Debug.LogError("[SessionInitialization] No XR Origin assigned!");
                enabled = false;
                return;
            }

            if (qrManager != null)
            {
                qrManager.SetAnchorEstablished(false);
                qrManager.OnRoomAnchorDiscovered += OnRoomAnchorDiscovered;
            }

            StartCoroutine(InitializationPhase());
        }

        private IEnumerator InitializationPhase()
        {
            isInitializing = true;
            InitializationComplete = false;

            if (statusUI != null)
                statusUI.ShowMessage($"To begin, please look at the '{qrManager.qrRoomAnchorLabel}' QR code.", "Calibration required.");

            while (isInitializing)
                yield return null;
        }

        private void OnRoomAnchorDiscovered(QRCodeManager.QRCodeInstance anchor)
        {
            if (!isInitializing)
            {
                // Drift correction
                if (Vector3.Distance(anchor.visualObject.transform.position, Vector3.zero) > 0.05f)
                {
                    CalibrateOriginToAnchor(anchor);
                }
                return;
            }

            Debug.Log($"[SessionInitialization] RoomAnchor detected: {anchor.fullPayload}");
            CalibrateOriginToAnchor(anchor);
            StartCoroutine(CompleteInitializationAfterAnchor());
        }

        private void CalibrateOriginToAnchor(QRCodeManager.QRCodeInstance anchor)
        {
            // Reset room anchor object
            if (roomAnchorObject != null) Destroy(roomAnchorObject);
            roomAnchorObject = new GameObject("RoomAnchorRoot");
            roomAnchorObject.transform.position = Vector3.zero;
            roomAnchorObject.transform.rotation = Quaternion.identity;

            // Move Origin so Anchor is at World Zero
            Vector3 offset = -anchor.visualObject.transform.position;
            xrOrigin.position += offset;
            
            // Align rotation (Y-axis only)
            Quaternion rotOffset = Quaternion.Inverse(anchor.visualObject.transform.rotation);
            xrOrigin.RotateAround(Vector3.zero, Vector3.up, rotOffset.eulerAngles.y);

            Debug.Log("[SessionInitialization] Origin calibrated to anchor.");
        }

        private IEnumerator CompleteInitializationAfterAnchor()
        {
            isInitializing = false;
            qrManager.SetAnchorEstablished(true);

            if (statusUI != null)
                statusUI.ShowMessage("Anchor established. Syncing with cloud...", "Please wait.");

            yield return StartCoroutine(LoadAndMergeQRCodes());

            InitializationComplete = true;
            if (statusUI != null)
                statusUI.ShowMessage("System Ready", "You can now join a session.");
            
            if (uiManager != null)
                uiManager.ShowJoinScreen();

            // NORMAL TRACKING EVENTS
            qrManager.OnQRCodeAdded += OnQRCodeAddedNormal;
            qrManager.OnQRCodeUpdated += OnQRCodeUpdatedNormal;
            qrManager.OnQRCodeRemoved += OnQRCodeRemovedNormal;
        }

        private IEnumerator LoadAndMergeQRCodes()
        {
            qrManager.ManualLoad();
            
            bool pullSuccess = false;
            if (streamingManager != null)
            {
                System.Action<string> pullCallback = null;
                pullCallback = (json) => {
                    qrManager.ManualLoadFromJson(json);
                    pullSuccess = true;
                    streamingManager.OnQRCodesPulled -= pullCallback;
                };
                streamingManager.OnQRCodesPulled += pullCallback;
                streamingManager.PullQRCodes();

                float start = Time.time;
                while (!pullSuccess && Time.time - start < pullTimeoutSeconds)
                    yield return null;
            }

            if (pullSuccess)
                Debug.Log("[SessionInitialization] Remote sync complete.");
            else
                Debug.LogWarning("[SessionInitialization] Remote sync failed/timed out. Using local data.");
            
            GenerateQRGameObjects();
        }

        public void GenerateQRGameObjects()
        {
            if (roomAnchorObject == null) return;
            foreach (var item in generatedQRTransforms.Values) if (item != null) Destroy(item.gameObject);
            generatedQRTransforms.Clear();

            foreach (var kvp in qrManager.TrackedQRCodes)
            {
                QRCodeManager.QRCodeInstance qr = kvp.Value;
                if (qr.fullPayload.Contains(qrManager.qrRoomAnchorLabel)) continue;

                GameObject qrObj = new GameObject($"QR_{qr.identifierKey}");
                qrObj.transform.SetParent(roomAnchorObject.transform);
                qrObj.transform.position = qr.lastPosition;
                qrObj.transform.rotation = qr.lastRotation;
                generatedQRTransforms[qr.identifierKey] = qrObj.transform;
            }

            if (uiManager != null) uiManager.RefreshQRCodeDropdown();
        }

        public void PointToQRCode(QRCodeManager.QRCodeInstance qr)
        {
            if (!InitializationComplete) return;
            if (generatedQRTransforms.TryGetValue(qr.identifierKey, out var target))
            {
                statusUI?.HighlightTarget(target);
                statusUI?.ShowMessage($"Pointing to: {qr.identifierKey}", qr.fullPayload);
            }
        }

        private void OnQRCodeAddedNormal(QRCodeManager.QRCodeInstance qr)
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

        private void OnQRCodeUpdatedNormal(QRCodeManager.QRCodeInstance qr)
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