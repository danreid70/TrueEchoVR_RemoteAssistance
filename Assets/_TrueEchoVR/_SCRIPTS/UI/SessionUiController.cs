using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using System;

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

        [Header("Login UI (assign in Inspector)")]
        public GameObject loginPanel;
        public TMP_Text apiHostText;
        public TMP_Text customerIdText;
        public TMP_Text locationIdText;
        public Button signInButton;
        public Button scanLoginCodeButton;
        public TMP_Text loginStatusText;

        private bool _isScanningLoginCode = false;

        private void Start()
        {
            if (webAppManager == null) webAppManager = SignalingManager.Instance;
            if (qrManager == null) qrManager = GetComponent<QrCodeManager>();
            if (statusUI == null) statusUI = GetComponent<VrHudController>();
            if (sessionInit == null) sessionInit = GetComponent<SessionFlowManager>();

            camTransform = Camera.main?.transform;
            if (camTransform == null) { Debug.LogError("[SessionUiController] No camera."); enabled = false; return; }
            if (sessionUIPanel == null) { Debug.LogError("[SessionUiController] No sessionUIPanel."); enabled = false; return; }

            panelTransform = sessionUIPanel.transform;

            if (joinButton != null) joinButton.onClick.AddListener(OnJoinPressed);
            if (sendButton != null) sendButton.onClick.AddListener(OnSendChat);
            if (leaveButton != null) leaveButton.onClick.AddListener(OnLeaveSession);
            if (toggleDetectionButton != null) toggleDetectionButton.onClick.AddListener(OnToggleDetectQR);
            if (clearQRButton != null) clearQRButton.onClick.AddListener(OnClearQRPressed);
            if (pushQRButton != null) pushQRButton.onClick.AddListener(OnPushQRPressed);
            if (pullQRButton != null) pullQRButton.onClick.AddListener(OnPullQRPressed);
            if (qrCodeDropdown != null) qrCodeDropdown.onValueChanged.AddListener(OnQRCodeSelected);
            
            // Login UI Listeners
            if (signInButton != null) signInButton.onClick.AddListener(OnSignInPressed);
            if (scanLoginCodeButton != null) scanLoginCodeButton.onClick.AddListener(OnScanLoginCodePressed);

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
                webAppManager.OnRemoteStreamStarted += (tex) => { if (remoteVideoImage != null) remoteVideoImage.texture = tex; };
                webAppManager.OnLocalStreamStarted += (tex) => { if (localVideoImage != null) { localVideoImage.texture = tex; localVideoImage.color = Color.white; } };
                webAppManager.OnStartupDataReceived += OnStartupDataReceived;
                webAppManager.OnConnectionError += (err) => {
                    if (joinStatusText != null) joinStatusText.text = $"<color=red>Error: {err}</color>";
                    if (loginStatusText != null) loginStatusText.text = $"<color=red>{err}</color>";
                    if (joinButtonText != null) joinButtonText.text = "Try Again";
                    if (joinButton != null) joinButton.interactable = true;
                    if (signInButton != null) signInButton.interactable = true;
                    AppendChatMessage($"<color=red>Error: {err}</color>");
                };
            }

            if (qrManager != null)
            {
                qrManager.OnQRCodeAdded += (qr) => {
                    if (_isScanningLoginCode) HandleLoginQRScan(qr);
                    else {
                        AppendChatMessage($"[QR Added] {GetColoredPayload(qr)}");
                        RefreshQRCodeDropdown();
                    }
                };
                qrManager.OnQRCodeUpdated += (qr) => AppendChatMessage($"[QR Updated] {GetColoredPayload(qr)}");
                qrManager.OnQRCodeRemoved += (key) => { AppendChatMessage($"[QR Removed] {key}"); RefreshQRCodeDropdown(); };
                qrManager.OnRoomAnchorDiscovered += (qr) => {
                    AppendChatMessage($"[Anchor Discovered] <color=green>{qr.fullPayload}</color>");
                    if (joinButtonText != null && joinButtonText.text.Contains("Calibration")) {
                        joinButtonText.text = "Connect";
                        if (joinButton != null) joinButton.interactable = true;
                    }
                };
            }

            panelTransform.position = ComputeOptimalHUDPosition();
            panelTransform.rotation = ComputeTargetRotation();

            if (joinStatusText != null) joinStatusText.text = "";
            if (sessionStatusText != null) sessionStatusText.text = "";

            if (localVideoImage != null && localVideoImage.texture == null) localVideoImage.color = Color.black;
            if (remoteVideoImage != null && remoteVideoImage.texture == null) remoteVideoImage.color = Color.black;

            if (locationIdInput != null) { string savedLocation = PlayerPrefs.GetString("SavedLocationID", ""); locationIdInput.text = savedLocation; }

            AppendChatMessage("<color=green>[System]</color> Session UI Initialized.");
            SetupInputFieldKeyboard(roomCodeInput);
            SetupInputFieldKeyboard(locationIdInput);
            SetupInputFieldKeyboard(chatInputField);

            if (webAppManager != null) webAppManager.StartLocalPreview();

            // Initial UI state
            if (webAppManager != null && !webAppManager.HasCredentials) ShowLoginPanel();
            else ShowJoinScreen();
        }

        public void ShowLoginPanel()
        {
            if (sessionUIPanel != null) sessionUIPanel.SetActive(true);
            if (loginPanel != null) loginPanel.SetActive(true);
            if (joinPanel != null) joinPanel.SetActive(false);
            if (sessionPanel != null) sessionPanel.SetActive(false);
            
            if (apiHostText != null && webAppManager.config != null) apiHostText.text = $"Host: {webAppManager.config.apiHost}";
            if (customerIdText != null) customerIdText.text = $"Customer: {webAppManager.config.customerId}";
            if (locationIdText != null) locationIdText.text = $"Location: {webAppManager.config.locationId}";
        }

        private void OnSignInPressed()
        {
            if (signInButton != null) signInButton.interactable = false;
            if (loginStatusText != null) loginStatusText.text = "Signing in...";
            
            webAppManager.RegisterAndBoot(webAppManager.config.customerId, webAppManager.config.locationId, (success) => {
                if (success) {
                    ShowJoinScreen();
                } else {
                    if (signInButton != null) signInButton.interactable = true;
                    if (loginStatusText != null) loginStatusText.text = "<color=red>Sign in failed.</color>";
                }
            });
        }

        private void OnScanLoginCodePressed()
        {
            _isScanningLoginCode = true;
            if (loginStatusText != null) loginStatusText.text = "Scanning Setup QR...";
            if (scanLoginCodeButton != null) scanLoginCodeButton.interactable = false;
        }

        [Serializable] public class SetupQR { public string customerId; public string locationId; }

        private void HandleLoginQRScan(QrCodeManager.QRCodeInstance qr)
        {
            try {
                var data = JsonUtility.FromJson<SetupQR>(qr.fullPayload);
                if (!string.IsNullOrEmpty(data.customerId) && !string.IsNullOrEmpty(data.locationId)) {
                    webAppManager.config.customerId = data.customerId;
                    webAppManager.config.locationId = data.locationId;
                    _isScanningLoginCode = false;
                    if (customerIdText != null) customerIdText.text = $"Customer: {data.customerId}";
                    if (locationIdText != null) locationIdText.text = $"Location: {data.locationId}";
                    if (loginStatusText != null) loginStatusText.text = "<color=green>QR Scanned Successfully.</color>";
                    if (scanLoginCodeButton != null) scanLoginCodeButton.interactable = true;
                }
            } catch {
                // Ignore if not a setup QR
            }
        }

        private void SetupInputFieldKeyboard(TMP_InputField input)
        {
            if (input == null) return;
            input.onSelect.AddListener((s) => StartCoroutine(OpenKeyboard(input)));
        }

        private IEnumerator OpenKeyboard(TMP_InputField input)
        {
            yield return new WaitForSeconds(0.1f);
            TouchScreenKeyboard keyboard = TouchScreenKeyboard.Open(input.text, TouchScreenKeyboardType.Default);
            while (keyboard != null && !keyboard.done && !keyboard.wasCanceled)
            {
                input.text = keyboard.text;
                yield return null;
            }
            if (keyboard != null && keyboard.done) input.text = keyboard.text;
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
            
            if (joinStatusText != null) joinStatusText.text = "Connecting to server...";
            if (joinButtonText != null) joinButtonText.text = "Connecting...";
            if (joinButton != null) joinButton.interactable = false;

            webAppManager?.Login(code);
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
            // Legacy - Removed to align with Replit platform flow
            AppendChatMessage("<color=orange>[System]</color> Manual push disabled. Calibration is managed by the Platform.");
        }

        private void OnPullQRPressed()
        {
            // Legacy - Removed to align with Replit platform flow
            AppendChatMessage("<color=orange>[System]</color> Manual pull disabled. Sync happens at boot.");
        }

        private void OnStartupDataReceived(SignalingManager.StartupData data)
        {
            if (qrManager == null) return;
            try
            {
                foreach (var anchor in data.qrCodes)
                {
                    qrManager.UpdateQRCodeFromRemote(anchor.qrValue, anchor.position, anchor.rotation);
                }
                AppendChatMessage($"<color=green>[Init]</color> Synced {data.qrCodes.Count} anchors from {data.locationName}.");
                RefreshQRCodeDropdown();
            }
            catch (System.Exception e)
            {
                AppendChatMessage("Failed to apply startup data: " + e.Message);
            }
        }

        private void OnQRCodeSelected(int index)
        {
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
                Debug.Log($"[SessionUI] Selected QR from dropdown: {selectedQR.fullPayload}");
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
            if (sessionUIPanel != null) sessionUIPanel.SetActive(true);
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
            if (sessionUIPanel != null) sessionUIPanel.SetActive(true);
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
                webAppManager.OnStartupDataReceived -= OnStartupDataReceived;
            }
        }
}
}