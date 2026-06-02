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

        public SignalingManager webAppManager;
        public QrCodeManager qrManager;
        public VrHudController statusUI;
        public SessionFlowManager sessionInit;

        private string chatHistory = "";
        private List<QrCodeManager.QRCodeInstance> qrCodeList = new List<QrCodeManager.QRCodeInstance>();

        [Header("Login UI (assign in Inspector)")]
        public GameObject loginPanel;
        public TMP_Text apiHostText;
        public TMP_Text customerIdText;
        public TMP_Text locationIdText;
        public Button signInButton;
        public Button scanLoginCodeButton;
        public TMP_Text loginStatusText;

        private bool _isScanningLoginCode = false;

        private void HandleUIStateChanged(UIManager.UIState newState)
        {
            switch (newState)
            {
                case UIManager.UIState.Login:
                    ShowLoginPanel();
                    break;
                case UIManager.UIState.Session:
                    ShowSessionScreen();
                    break;
                default:
                    if (sessionUIPanel != null) sessionUIPanel.SetActive(false);
                    break;
            }
        }

        private void OnEnable()
        {
            if (UIManager.Instance != null)
                UIManager.Instance.OnUIStateChanged += HandleUIStateChanged;
        }

        private void OnDisable()
        {
            if (UIManager.Instance != null)
                UIManager.Instance.OnUIStateChanged -= HandleUIStateChanged;
        }

        private void Start()
        {
            if (webAppManager == null) webAppManager = SignalingManager.Instance;
            if (qrManager == null) qrManager = FindFirstObjectByType<QrCodeManager>();
            if (statusUI == null) statusUI = FindFirstObjectByType<VrHudController>();
            if (sessionInit == null) sessionInit = FindFirstObjectByType<SessionFlowManager>();

            // Auto-discovery for Bootstrap/Prefab pattern
            if (sessionUIPanel == null)
            {
                var found = GameObject.Find("SessionGroup");
                if (found != null) sessionUIPanel = found;
            }
            if (sessionPanel == null)
            {
                var found = GameObject.Find("SessionPanel");
                if (found != null) sessionPanel = found;
            }
            if (loginPanel == null)
            {
                var found = GameObject.Find("LoginPanel");
                if (found != null) loginPanel = found;
            }

            if (sessionUIPanel == null) { Debug.LogWarning("[SessionUiController] No sessionUIPanel found. UI might not be initialized yet."); return; }

            if (joinButton != null) joinButton.onClick.AddListener(OnJoinPressed);
            if (sendButton != null) sendButton.onClick.AddListener(OnSendChat);
            if (leaveButton != null) leaveButton.onClick.AddListener(OnLeaveSession);
            if (toggleDetectionButton != null) toggleDetectionButton.onClick.AddListener(OnToggleDetectQR);
            if (clearQRButton != null) clearQRButton.onClick.AddListener(OnClearQRPressed);
            if (qrCodeDropdown != null) qrCodeDropdown.onValueChanged.AddListener(OnQRCodeSelected);
            
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
                    if (loginStatusText != null) loginStatusText.text = $"<color=red>{err}</color>";
                    if (joinButtonText != null) joinButtonText.text = "Try Again";
                    if (joinButton != null) joinButton.interactable = true;
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

            if (localVideoImage != null && localVideoImage.texture == null) localVideoImage.color = Color.black;
            if (remoteVideoImage != null && remoteVideoImage.texture == null) remoteVideoImage.color = Color.black;

            AppendChatMessage("<color=green>[System]</color> Session UI Initialized.");
            SetupInputFieldKeyboard(roomCodeInput);
            SetupInputFieldKeyboard(chatInputField);

            if (webAppManager != null) webAppManager.StartLocalPreview();

            // Handle initial state manually
            if (UIManager.Instance != null)
                HandleUIStateChanged(UIManager.Instance.GetCurrentState());
        }

        public void ShowLoginPanel()
        {
            if (sessionUIPanel != null) sessionUIPanel.SetActive(true);
            if (loginPanel != null) loginPanel.SetActive(true);
            if (sessionPanel != null) sessionPanel.SetActive(false);
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
            if (sessionUIPanel == null) return;
            
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

        public void ShowSessionScreen()
        {
            if (sessionUIPanel != null) sessionUIPanel.SetActive(true);
            if (loginPanel != null) loginPanel.SetActive(false);
            if (sessionPanel != null) sessionPanel.SetActive(true);
        }

        public void ShowJoinScreen() => ShowSessionScreen();

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
                qrCodeList.Add(kvp.Value);
                string displayName = !string.IsNullOrEmpty(kvp.Value.identifierKey) ? kvp.Value.identifierKey : kvp.Value.fullPayload;
                if (displayName.Length > 30) displayName = displayName.Substring(0, 27) + "...";
                options.Add(new TMP_Dropdown.OptionData(displayName));
            }
            qrCodeDropdown.AddOptions(options);
        }

        public void AddQRListItem(QrCodeManager.QRCodeInstance qr) { }
        public void UpdateQRListItem(QrCodeManager.QRCodeInstance qr) { }
        public void RemoveQRListItem(string key) { }

        private void OnJoinPressed()
        {
            if (sessionInit != null && !sessionInit.InitializationComplete) return;
            if (string.IsNullOrEmpty(roomCodeInput?.text)) return;
            webAppManager?.Login(roomCodeInput.text.ToUpper().Trim());
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
            UIManager.Instance?.SetState(UIManager.UIState.Session);
        }

        private void OnConnected()
        {
            if (connectionStatusText != null) connectionStatusText.text = "Status: LIVE";
            AppendChatMessage("--- Connected ---");
        }

        private void OnDisconnected()
        {
            if (connectionStatusText != null) connectionStatusText.text = "Status: DISCONNECTED";
        }

        private void OnChatReceived(string msg) => AppendChatMessage($"Admin: {msg}");

        private void OnToggleDetectQR()
        {
            if (qrManager == null) return;
            if (qrManager.IsDetecting) qrManager.StopQRCodeDetection();
            else qrManager.StartQRCodeDetection();
        }

        private void OnClearQRPressed()
        {
            qrManager?.ClearQRCodes();
            RefreshQRCodeDropdown();
        }

        private void OnStartupDataReceived(SignalingManager.StartupData data)
        {
            if (qrManager == null) return;
            foreach (var anchor in data.qrCodes)
                qrManager.UpdateQRCodeFromRemote(anchor.qrValue, anchor.position, anchor.rotation);
            RefreshQRCodeDropdown();
        }

        private void OnQRCodeSelected(int index)
        {
            if (index == 0)
            {
                statusUI?.ClearHighlight();
                statusUI?.ShowMessage("", "");
                return;
            }
            int qrIndex = index - 1;
            if (qrIndex >= 0 && qrIndex < qrCodeList.Count)
                sessionInit?.PointToQRCode(qrCodeList[qrIndex]);
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