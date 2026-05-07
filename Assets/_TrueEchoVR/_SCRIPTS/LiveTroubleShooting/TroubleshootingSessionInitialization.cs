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

        [Header("Room Anchor Settings")]
        public string roomAnchorPayloadSubstring = "RoomAnchor";
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
                qrManager.OnQRCodeAdded += OnQRCodeAddedForInit;

            StartCoroutine(InitializationPhase());
        }

        private IEnumerator InitializationPhase()
        {
            isInitializing = true;
            InitializationComplete = false;

            if (statusUI != null)
                statusUI.ShowMessage("To begin, please look at the 'RoomAnchor' QR code.", "Find the QR code labelled with 'RoomAnchor'.");

            while (isInitializing)
                yield return null;
        }

        private void OnQRCodeAddedForInit(QRCodeManager.QRCodeInstance qr)
        {
            if (!isInitializing) return;
            if (qr.fullPayload.Contains(roomAnchorPayloadSubstring))
            {
                Debug.Log($"[SessionInitialization] RoomAnchor QR detected: {qr.fullPayload}");
                StartCoroutine(ProcessRoomAnchor(qr));
            }
        }

        private IEnumerator ProcessRoomAnchor(QRCodeManager.QRCodeInstance anchorQR)
        {
            isInitializing = false;

            Vector3 realQRPosition = anchorQR.visualObject.transform.position;
            Vector3 delta = -realQRPosition;
            xrOrigin.position += delta;

            if (roomAnchorObject != null) Destroy(roomAnchorObject);
            roomAnchorObject = new GameObject("RoomAnchor");
            roomAnchorObject.transform.position = Vector3.zero;
            roomAnchorObject.transform.rotation = Quaternion.identity;

            if (statusUI != null)
                statusUI.ShowMessage("Room anchor set. Loading QR codes...", "Please wait.");

            yield return StartCoroutine(LoadQRCodes());

            GenerateQRGameObjects();

            InitializationComplete = true;
            if (statusUI != null)
                statusUI.ShowMessage("Initialization complete", "You can now join a session.");
            if (uiManager != null)
                uiManager.ShowJoinScreen();

            if (qrManager != null)
            {
                qrManager.OnQRCodeAdded -= OnQRCodeAddedForInit;
                qrManager.OnQRCodeAdded += OnQRCodeAddedNormal;
                qrManager.OnQRCodeUpdated += OnQRCodeUpdatedNormal;
                qrManager.OnQRCodeRemoved += OnQRCodeRemovedNormal;

                foreach (var kvp in qrManager.TrackedQRCodes)
                {
                    if (kvp.Value.fullPayload.Contains(roomAnchorPayloadSubstring)) continue;
                    OnQRCodeAddedNormal(kvp.Value);
                }
            }
        }

        private IEnumerator LoadQRCodes()
        {
            bool pullSuccess = false;
            bool timeout = false;

            if (streamingManager != null)
            {
                System.Action<string> pullCallback = null;
                pullCallback = (json) =>
                {
                    pullSuccess = true;
                    streamingManager.OnQRCodesPulled -= pullCallback;
                };
                streamingManager.OnQRCodesPulled += pullCallback;
                streamingManager.PullQRCodes();

                float startTime = Time.time;
                while (!pullSuccess && !timeout)
                {
                    if (Time.time - startTime > pullTimeoutSeconds)
                    {
                        timeout = true;
                        streamingManager.OnQRCodesPulled -= pullCallback;
                        Debug.LogWarning("[SessionInitialization] Pull request timed out.");
                    }
                    yield return null;
                }
            }

            if (!pullSuccess)
            {
                Debug.Log("[SessionInitialization] Falling back to local QR save.");
                qrManager.ManualLoad();
            }
            else
            {
                Debug.Log("[SessionInitialization] QR codes loaded from server.");
            }
        }

        public void GenerateQRGameObjects()
        {
            if (roomAnchorObject == null) return;

            foreach (var item in generatedQRTransforms.Values)
            {
                if (item != null) Destroy(item.gameObject);
            }
            generatedQRTransforms.Clear();

            foreach (var kvp in qrManager.TrackedQRCodes)
            {
                QRCodeManager.QRCodeInstance qr = kvp.Value;
                if (qr.fullPayload.Contains(roomAnchorPayloadSubstring)) continue;

                GameObject qrObj = new GameObject($"QR_{qr.identifierKey}");
                qrObj.transform.SetParent(roomAnchorObject.transform);
                qrObj.transform.position = qr.lastPosition;
                qrObj.transform.rotation = qr.lastRotation;
                generatedQRTransforms[qr.identifierKey] = qrObj.transform;
            }

            if (uiManager != null)
                uiManager.RefreshQRCodeDropdown();
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
            if (qr.fullPayload.Contains(roomAnchorPayloadSubstring)) return;
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
            if (qr.fullPayload.Contains(roomAnchorPayloadSubstring)) return;
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
                qrManager.OnQRCodeAdded -= OnQRCodeAddedForInit;
                qrManager.OnQRCodeAdded -= OnQRCodeAddedNormal;
                qrManager.OnQRCodeUpdated -= OnQRCodeUpdatedNormal;
                qrManager.OnQRCodeRemoved -= OnQRCodeRemovedNormal;
            }
        }
    }
}