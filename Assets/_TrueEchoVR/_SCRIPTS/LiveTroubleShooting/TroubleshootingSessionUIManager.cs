using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

namespace TrueEchoVR
{
    public class TroubleshootingSessionUIManager : MonoBehaviour
    {
        [Header("UI References (assign in Inspector)")]
        public GameObject sessionUIPanel;
        public GameObject joinPanel;
        public GameObject sessionPanel;

        public TMP_InputField roomCodeInput;
        public Button joinButton;
        public TMP_Text joinStatusText;

        public TMP_Text connectionStatusText;
        public RawImage localVideoImage;
        public RawImage remoteVideoImage;
        public Button toggleDetectionButton;
        public TMP_Text toggleDetectionButtonText;
        public Button pushQRButton;
        public Button pullQRButton;
        public Button clearQRButton;
        public TMP_InputField locationIdInput;

        public TMP_Dropdown qrCodeDropdown;

        public Transform qrListContent;
        public GameObject qrListItemPrefab;

        public ScrollRect chatScrollRect;
        public TMP_Text chatDisplayText;
        public TMP_InputField chatInputField;
        public Button sendButton;
        public Button leaveButton;

        [Header("Positioning (Tag-along)")]
        [SerializeField] private float forwardDistance = 1.2f;
        [SerializeField] private float rightOffset = 0.4f;
        [SerializeField] private float verticalOffset = 0.15f;
        [SerializeField] private float smoothTime = 0.25f;
        [SerializeField] private float rotationSpeed = 5f;
        [SerializeField] private float viewportMargin = 0.15f; 
        [SerializeField] private float collisionOffset = 0.1f;

        public TroubleshootingStreamingManager streamingManager;
        public QRCodeManager qrManager;
        public MainVRHUDUI statusUI;
        public TroubleshootingSessionInitialization sessionInit;

        private Transform camTransform;
        private Vector3 velocity = Vector3.zero;
        private bool isCatchingUp = false;
        private string chatHistory = "";
        private Dictionary<string, GameObject> qrListItems = new Dictionary<string, GameObject>();
        private List<QRCodeManager.QRCodeInstance> qrCodeList = new List<QRCodeManager.QRCodeInstance>();
        private Transform panelTransform;

        private void Start()
        {
            if (streamingManager == null) streamingManager = GetComponent<TroubleshootingStreamingManager>();
            if (qrManager == null) qrManager = GetComponent<QRCodeManager>();
            if (statusUI == null) statusUI = GetComponent<MainVRHUDUI>();
            if (sessionInit == null) sessionInit = GetComponent<TroubleshootingSessionInitialization>();

            camTransform = Camera.main?.transform;
            if (camTransform == null)
            {
                Debug.LogError("[TroubleshootingSessionUIManager] No main camera found.");
                enabled = false;
                return;
            }

            if (sessionUIPanel == null)
            {
                Debug.LogError("[TroubleshootingSessionUIManager] No sessionUIPanel assigned.");
                enabled = false;
                return;
            }

            panelTransform = sessionUIPanel.transform;

            if (joinButton != null) joinButton.onClick.AddListener(OnJoinPressed);
            if (sendButton != null) sendButton.onClick.AddListener(OnSendChat);
            if (leaveButton != null) leaveButton.onClick.AddListener(OnLeaveSession);
            if (toggleDetectionButton != null) toggleDetectionButton.onClick.AddListener(OnToggleDetectQR);
            if (clearQRButton != null) clearQRButton.onClick.AddListener(OnClearQRPressed);
            if (pushQRButton != null) pushQRButton.onClick.AddListener(OnPushQRPressed);
            if (pullQRButton != null) pullQRButton.onClick.AddListener(OnPullQRPressed);
            if (qrCodeDropdown != null) qrCodeDropdown.onValueChanged.AddListener(OnQRCodeSelected);

            if (streamingManager != null)
            {
                streamingManager.OnConnected += OnConnected;
                streamingManager.OnDisconnected += OnDisconnected;
                streamingManager.OnChatMessageReceived += OnChatReceived;
                streamingManager.OnRemoteStreamStarted += (tex) => { 
                    if (remoteVideoImage != null) {
                        remoteVideoImage.texture = tex;
                    }
                };
                streamingManager.OnLocalStreamStarted += (tex) => { if (localVideoImage != null) localVideoImage.texture = tex; };
                streamingManager.OnQRCodesPulled += OnQRCodesPulled;
                streamingManager.OnConnectionError += (err) => AppendChatMessage($"<color=red>Error: {err}</color>");
            }

            if (qrManager != null)
            {
                qrManager.OnQRCodeAdded += (qr) => AppendChatMessage($"[QR Added] {GetColoredPayload(qr)}");
                qrManager.OnQRCodeUpdated += (qr) => AppendChatMessage($"[QR Updated] {GetColoredPayload(qr)}");
                qrManager.OnQRCodeRemoved += (key) => AppendChatMessage($"[QR Removed] {key}");
                qrManager.OnRoomAnchorDiscovered += (qr) => AppendChatMessage($"[Anchor Discovered] <color=green>{qr.fullPayload}</color>");
            }

            // Initial placement
            panelTransform.position = ComputeOptimalHUDPosition();
            panelTransform.rotation = ComputeTargetRotation();
        }

        private string GetColoredPayload(QRCodeManager.QRCodeInstance qr)
        {
            string color = qr.status == QRCodeManager.QRStatus.Official ? "#00FF00" : "#FF0000";
            return $"<color={color}>{qr.fullPayload}</color>";
        }

        public void LogAllQRCodesToChat()
        {
            if (qrManager == null) return;
            AppendChatMessage("--- Current QR Codes ---");
            foreach (var kvp in qrManager.TrackedQRCodes)
            {
                AppendChatMessage(GetColoredPayload(kvp.Value));
            }
            AppendChatMessage("------------------------");
        }

        private void LateUpdate()
        {
            if (camTransform == null || sessionUIPanel == null || sessionInit == null) return;
            
            // 1. Check if UI is currently in a visible "Safe Zone" in the viewport
            Vector3 viewportPos = Camera.main.WorldToViewportPoint(panelTransform.position);
            bool isVisible = viewportPos.z > 0 && 
                             viewportPos.x > viewportMargin && viewportPos.x < (1 - viewportMargin) && 
                             viewportPos.y > viewportMargin && viewportPos.y < (1 - viewportMargin);

            // 2. Trigger catch-up if not visible
            if (!isVisible) isCatchingUp = true;

            // 3. Move UI toward the optimal HUD spot
            if (isCatchingUp)
            {
                Vector3 targetPos = ComputeOptimalHUDPosition();
                
                // --- Raycast Avoidance ---
                // Cast from eyes to the desired HUD position
                Vector3 direction = (targetPos - camTransform.position).normalized;
                float maxDist = Vector3.Distance(camTransform.position, targetPos);
                
                if (Physics.Raycast(camTransform.position, direction, out RaycastHit hit, maxDist))
                {
                    // If we hit an object (VR training part, etc), place UI in front of it
                    targetPos = hit.point - (direction * collisionOffset);
                }

                panelTransform.position = Vector3.SmoothDamp(panelTransform.position, targetPos, ref velocity, smoothTime);

                // Stop catching up when we are close and visible again
                if (Vector3.Distance(panelTransform.position, targetPos) < 0.05f && isVisible)
                {
                    isCatchingUp = false;
                }
            }

            // 4. Always rotate to face the user (Billboard effect)
            panelTransform.rotation = Quaternion.Slerp(panelTransform.rotation, ComputeTargetRotation(), rotationSpeed * Time.deltaTime);
        }

        private Vector3 ComputeOptimalHUDPosition()
        {
            // Positioned relative to the camera view
            return camTransform.position
                   + camTransform.forward * forwardDistance
                   + camTransform.right * rightOffset
                   + camTransform.up * verticalOffset;
        }

        private Quaternion ComputeTargetRotation()
        {
            // Rotate to face the camera directly
            Vector3 toCam = camTransform.position - panelTransform.position;
            if (toCam == Vector3.zero) return Quaternion.identity;
            return Quaternion.LookRotation(-toCam, Vector3.up);
        }

        private void OnJoinPressed()
        {
            if (!sessionInit.InitializationComplete)
            {
                if (joinStatusText != null) joinStatusText.text = "Initialization in progress...";
                return;
            }
            if (string.IsNullOrEmpty(roomCodeInput?.text)) return;
            string code = roomCodeInput.text.ToUpper().Trim();
            streamingManager?.StartSession(code);
            ShowSessionScreen();
        }

        private void OnSendChat()
        {
            if (string.IsNullOrEmpty(chatInputField?.text)) return;
            streamingManager?.SendChatMessage(chatInputField.text);
            AppendChatMessage($"You: {chatInputField.text}");
            chatInputField.text = "";
        }

        private void OnLeaveSession()
        {
            streamingManager?.Disconnect();
            ShowJoinScreen();
        }

        private void OnConnected()
        {
            if (connectionStatusText != null) connectionStatusText.text = "Status: LIVE";
            AppendChatMessage("--- Connected ---");
        }

        private void OnDisconnected()
        {
            ShowJoinScreen();
        }

        private void OnChatReceived(string msg) => AppendChatMessage($"Admin: {msg}");

        private void OnToggleDetectQR()
        {
            if (qrManager == null) return;
            if (qrManager.IsDetecting)
            {
                qrManager.StopQRCodeDetection();
                if (toggleDetectionButtonText != null) toggleDetectionButtonText.text = "Start Detection";
            }
            else
            {
                qrManager.StartQRCodeDetection();
                if (toggleDetectionButtonText != null) toggleDetectionButtonText.text = "Stop Detection";
            }
        }

        private void OnClearQRPressed()
        {
            qrManager?.ClearQRCodes();
            AppendChatMessage("<color=orange>Cleared all local QR Codes.</color>");
        }

        private void OnPushQRPressed()
        {
            if (qrManager == null || streamingManager == null) return;
            string json = qrManager.GetQRCodeDataAsJson();
            streamingManager.PushQRCodes(json);
            AppendChatMessage("<color=yellow>Pushed local QR Codes to server.</color>");
        }

        private void OnPullQRPressed()
        {
            streamingManager?.PullQRCodes();
            AppendChatMessage("<color=yellow>Requested QR Codes from server.</color>");
        }

        private void OnQRCodesPulled(string json)
        {
            if (qrManager == null) return;
            try
            {
                qrManager.ManualLoadFromJson(json);
                AppendChatMessage("Successfully synced QR Codes from server.");
                sessionInit.GenerateQRGameObjects();
            }
            catch (System.Exception e)
            {
                AppendChatMessage("Failed to sync QR Codes: " + e.Message);
            }
        }

        private void OnQRCodeSelected(int index)
        {
            if (!sessionInit.InitializationComplete) return;
            if (index < 0 || index >= qrCodeList.Count) return;
            var selectedQR = qrCodeList[index];
            if (selectedQR != null)
            {
                string displayName = !string.IsNullOrEmpty(selectedQR.identifierKey) ? selectedQR.identifierKey : selectedQR.fullPayload;
                statusUI?.ShowMessage($"Selected QR: {displayName}", $"Payload: {selectedQR.fullPayload}");
                sessionInit.PointToQRCode(selectedQR);
            }
        }

        private void AppendChatMessage(string msg)
        {
            chatHistory += $"{msg}\n";
            if (chatDisplayText != null) chatDisplayText.text = chatHistory;
            if (chatScrollRect != null)
            {
                Canvas.ForceUpdateCanvases();
                chatScrollRect.verticalNormalizedPosition = 0f;
            }
        }

        public void ShowJoinScreen()
        {
            if (joinPanel != null) joinPanel.SetActive(true);
            if (sessionPanel != null) sessionPanel.SetActive(false);
            if (statusUI != null) statusUI.ClearHighlight();
        }

        public void ShowSessionScreen()
        {
            if (joinPanel != null) joinPanel.SetActive(false);
            if (sessionPanel != null) sessionPanel.SetActive(true);
        }

        public void RefreshQRCodeDropdown()
        {
            if (qrCodeDropdown == null) return;
            qrCodeDropdown.ClearOptions();
            List<TMP_Dropdown.OptionData> options = new List<TMP_Dropdown.OptionData>();
            qrCodeList.Clear();
            foreach (var kvp in qrManager.TrackedQRCodes)
            {
                if (kvp.Value.fullPayload.Contains("RoomAnchor")) continue;
                QRCodeManager.QRCodeInstance qr = kvp.Value;
                qrCodeList.Add(qr);
                string displayName = !string.IsNullOrEmpty(qr.identifierKey) ? qr.identifierKey : qr.fullPayload;
                if (displayName.Length > 30) displayName = displayName.Substring(0, 27) + "...";
                options.Add(new TMP_Dropdown.OptionData(displayName));
            }
            qrCodeDropdown.AddOptions(options);
        }

        public void AddQRListItem(QRCodeManager.QRCodeInstance qr)
        {
            if (qrListContent == null || qrListItemPrefab == null) return;
            if (qrListItems.ContainsKey(qr.identifierKey)) return;
            var item = Instantiate(qrListItemPrefab, qrListContent);
            item.SetActive(true);
            var textComp = item.GetComponent<TMP_Text>();
            if (textComp != null)
                textComp.text = $"{qr.fullPayload}\nPos: {qr.lastPosition}";
            qrListItems[qr.identifierKey] = item;
        }

        public void UpdateQRListItem(QRCodeManager.QRCodeInstance qr)
        {
            if (qrListItems.TryGetValue(qr.identifierKey, out var item))
            {
                var textComp = item.GetComponent<TMP_Text>();
                if (textComp != null)
                    textComp.text = $"{qr.fullPayload}\nPos: {qr.lastPosition}";
            }
        }

        public void RemoveQRListItem(string identifierKey)
        {
            if (qrListItems.TryGetValue(identifierKey, out var item))
            {
                Destroy(item);
                qrListItems.Remove(identifierKey);
            }
        }

        private void OnDestroy()
        {
            if (streamingManager != null)
            {
                streamingManager.OnConnected -= OnConnected;
                streamingManager.OnDisconnected -= OnDisconnected;
                streamingManager.OnChatMessageReceived -= OnChatReceived;
                streamingManager.OnQRCodesPulled -= OnQRCodesPulled;
            }
        }
    }
}