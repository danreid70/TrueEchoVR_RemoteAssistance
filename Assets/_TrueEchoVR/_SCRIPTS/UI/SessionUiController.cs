using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

namespace TEVR
{
    public class SessionUiController : MonoBehaviour
    {
        [Header("UI References (assign in Inspector)")]
        public GameObject sessionUIPanel;
        public GameObject joinPanel;
        public GameObject sessionPanel;

        public TMP_InputField roomCodeInput;
        public Button joinButton;
        public TMP_Text joinButtonText; // Reference to button's label
        public TMP_Text joinStatusText; // Status message in Join Panel
        public TMP_Text sessionStatusText; // Status message in Session Panel

        public TMP_Text connectionStatusText;
        public TMP_Text latencyText;
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

        [Header("Stream Placeholders")]
        public Texture2D noSignalTexture;

        [Header("Positioning (Tag-along)")]
[SerializeField] private float forwardDistance = 1.2f;
        [SerializeField] private float rightOffset = 0.4f;
        [SerializeField] private float verticalOffset = 0.15f;
        [SerializeField] private float smoothTime = 0.25f;
        [SerializeField] private float rotationSpeed = 5f;
        [SerializeField] private float viewportMargin = 0.15f; 
        [SerializeField] private float collisionOffset = 0.1f;

        public SignalingManager webAppManager;
        public QrCodeManager qrManager;
        public VrHudController statusUI;
        public SessionFlowManager sessionInit;

        private Transform camTransform;
        private Vector3 velocity = Vector3.zero;
        private bool isCatchingUp = false;
        private string chatHistory = "";
        private Dictionary<string, GameObject> qrListItems = new Dictionary<string, GameObject>();
        private List<QrCodeManager.QRCodeInstance> qrCodeList = new List<QrCodeManager.QRCodeInstance>();
        private Transform panelTransform;

        private void Start()
        {
            if (webAppManager == null) webAppManager = SignalingManager.Instance;
            if (qrManager == null) qrManager = GetComponent<QrCodeManager>();
            if (statusUI == null) statusUI = GetComponent<VrHudController>();
            if (sessionInit == null) sessionInit = GetComponent<SessionFlowManager>();

            camTransform = Camera.main?.transform;
            if (camTransform == null)
            {
                Debug.LogError("[SessionUiController] No main camera found.");
                enabled = false;
                return;
            }

            if (sessionUIPanel == null)
            {
                Debug.LogError("[SessionUiController] No sessionUIPanel assigned.");
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

            // Set placeholders
            if (noSignalTexture != null)
            {
                if (localVideoImage != null) localVideoImage.texture = noSignalTexture;
                if (remoteVideoImage != null) remoteVideoImage.texture = noSignalTexture;
            }

            if (webAppManager != null)
            {
                webAppManager.OnConnected += OnConnected;
webAppManager.OnDisconnected += OnDisconnected;
                webAppManager.OnChatMessageReceived += OnChatReceived;
                webAppManager.OnRemoteStreamStarted += (tex) => { 
                    if (remoteVideoImage != null) {
                        remoteVideoImage.texture = tex;
                    }
                };
                webAppManager.OnLocalStreamStarted += (tex) => { 
                    if (localVideoImage != null) {
                        localVideoImage.texture = tex;
                        localVideoImage.color = Color.white; // Ensure visibility
                    }
                };
webAppManager.OnQRCodesPulled += OnQRCodesPulled;
                webAppManager.OnConnectionError += (err) => {
                    if (joinStatusText != null) joinStatusText.text = $"<color=red>Error: {err}</color>";
                    if (joinButtonText != null) joinButtonText.text = "Try Again";
                    if (joinButton != null) joinButton.interactable = true;
                    AppendChatMessage($"<color=red>Error: {err}</color>");
                };
            }

            if (qrManager != null)
            {
                qrManager.OnQRCodeAdded += (qr) => {
                    AppendChatMessage($"[QR Added] {GetColoredPayload(qr)}");
                    RefreshQRCodeDropdown();
                };
                qrManager.OnQRCodeUpdated += (qr) => AppendChatMessage($"[QR Updated] {GetColoredPayload(qr)}");
                qrManager.OnQRCodeRemoved += (key) => {
                    AppendChatMessage($"[QR Removed] {key}");
                    RefreshQRCodeDropdown();
                };
                qrManager.OnRoomAnchorDiscovered += (qr) => {
                    AppendChatMessage($"[Anchor Discovered] <color=green>{qr.fullPayload}</color>");
                    // If we were waiting for calibration, reset the join button
                    if (joinButtonText != null && joinButtonText.text.Contains("Calibration"))
                    {
                        joinButtonText.text = "Connect";
                        if (joinButton != null) joinButton.interactable = true;
                    }
                };
            }

            panelTransform.position = ComputeOptimalHUDPosition();
            panelTransform.rotation = ComputeTargetRotation();

            // Clear the placeholder [xxx] text
            if (joinStatusText != null) joinStatusText.text = "";
            if (sessionStatusText != null) sessionStatusText.text = "";

            // Ensure video images are black if no texture is assigned
            if (localVideoImage != null && localVideoImage.texture == null) localVideoImage.color = Color.black;
            if (remoteVideoImage != null && remoteVideoImage.texture == null) remoteVideoImage.color = Color.black;

            // Check initial calibration state
            if (sessionInit != null && !sessionInit.InitializationComplete)
            {
                if (joinButtonText != null) joinButtonText.text = "Waiting for Calibration...";
                if (joinButton != null) joinButton.interactable = false;
            }

            AppendChatMessage("<color=green>[System]</color> Session UI Initialized.");
            LogAllQRCodesToChat();

            if (webAppManager != null)
            {
                webAppManager.StartLocalPreview();
            }
        }

        private string GetColoredPayload(QrCodeManager.QRCodeInstance qr)
        {
            string color = qr.status == QrCodeManager.QRStatus.Official ? "#00FF00" : "#FF0000";
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
            
            // Check if the current camera is still valid
            if (Camera.main != null && camTransform != Camera.main.transform)
            {
                camTransform = Camera.main.transform;
            }

            Vector3 viewportPos = Camera.main.WorldToViewportPoint(panelTransform.position);
            bool isVisible = viewportPos.z > 0 && 
                             viewportPos.x > viewportMargin && viewportPos.x < (1 - viewportMargin) && 
                             viewportPos.y > viewportMargin && viewportPos.y < (1 - viewportMargin);

            if (!isVisible) isCatchingUp = true;

            if (isCatchingUp)
            {
                Vector3 targetPos = ComputeOptimalHUDPosition();
                
                // Safety check for NaN or Infinity
                if (!float.IsFinite(targetPos.x) || !float.IsFinite(targetPos.y) || !float.IsFinite(targetPos.z))
                {
                    return;
                }

                Vector3 direction = (targetPos - camTransform.position).normalized;
                float maxDist = Vector3.Distance(camTransform.position, targetPos);
                
                if (direction.sqrMagnitude > 0.001f && Physics.Raycast(camTransform.position, direction, out RaycastHit hit, maxDist))
                {
                    targetPos = hit.point - (direction * collisionOffset);
                }

                panelTransform.position = Vector3.SmoothDamp(panelTransform.position, targetPos, ref velocity, smoothTime);

                if (Vector3.Distance(panelTransform.position, targetPos) < 0.05f && isVisible)
                {
                    isCatchingUp = false;
                }
            }

            // Fixed Rotation: Lock to Yaw (Y-axis) only to prevent twisting
            Quaternion targetRot = ComputeTargetRotation();
            if (float.IsFinite(targetRot.x))
            {
                panelTransform.rotation = Quaternion.Slerp(panelTransform.rotation, targetRot, rotationSpeed * Time.deltaTime);
            }

            // Update Latency Display
            if (webAppManager != null && webAppManager.IsConnected)
            {
                if (latencyText != null)
                {
                    float lat = webAppManager.currentLatency;
                    string color = lat < 100 ? "#00FF00" : (lat < 250 ? "#FFFF00" : "#FF0000");
                    latencyText.text = $"Ping: <color={color}>{lat:F0}ms</color>";
                }
            }
            else if (latencyText != null)
            {
                latencyText.text = "";
            }
        }

        private Vector3 ComputeOptimalHUDPosition()
        {
            // Calculate flattened forward/right to prevent the panel from 'pitching' or 'rolling' with the head
            Vector3 flatForward = camTransform.forward;
            flatForward.y = 0;
            if (flatForward.sqrMagnitude < 0.001f) flatForward = Vector3.ProjectOnPlane(camTransform.up, Vector3.up).normalized;
            else flatForward.Normalize();

            Vector3 flatRight = Vector3.Cross(Vector3.up, flatForward);
            
            return camTransform.position
                   + flatForward * forwardDistance
                   + flatRight * rightOffset
                   + Vector3.up * verticalOffset;
        }

        private Quaternion ComputeTargetRotation()
        {
            // Calculate a Yaw-only look rotation (looking at the user's horizontal position)
            Vector3 directionToCamera = camTransform.position - panelTransform.position;
            directionToCamera.y = 0;
            
            if (directionToCamera.sqrMagnitude < 0.001f)
            {
                // Fallback: face in the same direction as the camera's flattened forward
                Vector3 camForward = camTransform.forward;
                camForward.y = 0;
                if (camForward.sqrMagnitude < 0.001f) return panelTransform.rotation;
                return Quaternion.LookRotation(camForward, Vector3.up);
            }
            
            // We want the panel to FACE the camera, so we use -directionToCamera
            return Quaternion.LookRotation(-directionToCamera, Vector3.up);
        }

        private void OnJoinPressed()
        {
            if (!sessionInit.InitializationComplete)
            {
                if (joinStatusText != null) joinStatusText.text = "<color=yellow>Calibration required. Look at Room Anchor.</color>";
                if (joinButtonText != null) joinButtonText.text = "Waiting for Calibration...";
                if (joinButton != null) joinButton.interactable = false;
                return;
            }

            if (string.IsNullOrEmpty(roomCodeInput?.text)) return;
            string code = roomCodeInput.text.ToUpper().Trim();
            string locationId = locationIdInput != null ? locationIdInput.text : "Unknown";
            
            if (joinStatusText != null) joinStatusText.text = "Connecting to server...";
            if (joinButtonText != null) joinButtonText.text = "Connecting...";
            if (joinButton != null) joinButton.interactable = false;

            webAppManager?.Login(locationId, code);
        }

        private void OnSendChat()
        {
            if (string.IsNullOrEmpty(chatInputField?.text)) return;
            webAppManager?.SendChatMessage(chatInputField.text);
            AppendChatMessage($"You: {chatInputField.text}");
            chatInputField.text = "";
        }

        private void OnLeaveSession()
        {
            webAppManager?.Disconnect();
            ShowJoinScreen();
        }

        private void OnConnected()
        {
            if (connectionStatusText != null) connectionStatusText.text = "Status: LIVE";
            AppendChatMessage("--- Connected ---");
            ShowSessionScreen();
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
            RefreshQRCodeDropdown();
        }

        private void OnPushQRPressed()
        {
            if (qrManager == null || webAppManager == null) return;
            string json = qrManager.GetQRCodeDataAsJson(webAppManager.headsetId);
            webAppManager.PushQRCodes(json);
            AppendChatMessage("<color=yellow>Pushed local QR Codes to server.</color>");
        }

        private void OnPullQRPressed()
        {
            webAppManager?.PullQRCodes();
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
                RefreshQRCodeDropdown();
            }
            catch (System.Exception e)
            {
                AppendChatMessage("Failed to sync QR Codes: " + e.Message);
            }
        }

        private void OnQRCodeSelected(int index)
        {
            if (!sessionInit.InitializationComplete) return;
            
            if (index == 0)
            {
                statusUI?.ClearHighlight();
                statusUI?.ShowMessage("", ""); // Clear HUD message
                return;
            }

            int qrIndex = index - 1;
            if (qrIndex < 0 || qrIndex >= qrCodeList.Count) return;
            
            var selectedQR = qrCodeList[qrIndex];
            if (selectedQR != null)
            {
                sessionInit.PointToQRCode(selectedQR);
            }
        }

        public void AppendChatMessage(string msg)
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

            // Reset Join Screen state
            if (joinButtonText != null) joinButtonText.text = "Connect";
            if (joinButton != null) joinButton.interactable = true;
            if (joinStatusText != null) joinStatusText.text = "";
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
            options.Add(new TMP_Dropdown.OptionData("Stop Pointing"));
            
            qrCodeList.Clear();
            foreach (var kvp in qrManager.TrackedQRCodes)
            {
                if (kvp.Value.fullPayload.Contains("RoomAnchor")) continue;
                QrCodeManager.QRCodeInstance qr = kvp.Value;
                qrCodeList.Add(qr);
                string displayName = !string.IsNullOrEmpty(qr.identifierKey) ? qr.identifierKey : qr.fullPayload;
                if (displayName.Length > 30) displayName = displayName.Substring(0, 27) + "...";
                options.Add(new TMP_Dropdown.OptionData(displayName));
            }
            qrCodeDropdown.AddOptions(options);
        }

        public void AddQRListItem(QrCodeManager.QRCodeInstance qr)
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

        public void UpdateQRListItem(QrCodeManager.QRCodeInstance qr)
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
            if (webAppManager != null)
            {
                webAppManager.OnConnected -= OnConnected;
                webAppManager.OnDisconnected -= OnDisconnected;
                webAppManager.OnChatMessageReceived -= OnChatReceived;
                webAppManager.OnQRCodesPulled -= OnQRCodesPulled;
            }
        }
    }
}