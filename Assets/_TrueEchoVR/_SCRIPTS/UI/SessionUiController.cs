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

        [Header("Login Manual Entry (fallback to scanning)")]
        public TMP_InputField loginCustomerIdInput;
        public TMP_InputField loginLocationIdInput;

        [Header("Login QR Extra Values (read-only display)")]
        [Tooltip("Read-only labels showing the additional values captured from the Sign In QR code so " +
                 "the user can verify a single scan stored everything needed to sign in later.")]
        public TMP_Text loginApiUrlText;
        public TMP_Text loginRoomCodeText;
        public TMP_Text loginTokenText;

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
            if (qrManager == null) qrManager = FindAnyObjectByType<QrCodeManager>();
            if (statusUI == null) statusUI = FindAnyObjectByType<VrHudController>();
            if (sessionInit == null) sessionInit = FindAnyObjectByType<SessionFlowManager>();

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

            // Login panel buttons (previously never wired -> clicking did nothing).
            if (signInButton != null) signInButton.onClick.AddListener(OnSignInPressed);
            if (scanLoginCodeButton != null) scanLoginCodeButton.onClick.AddListener(OnScanLoginCodePressed);

            // Calibration persistence buttons (REST push/pull of QR codes).
            if (pushQRButton != null) pushQRButton.onClick.AddListener(OnPushQRPressed);
            if (pullQRButton != null) pullQRButton.onClick.AddListener(OnPullQRPressed);

            // Room code: confirming the field (keyboard "done"/enter) connects to the remote session.
            if (roomCodeInput != null) roomCodeInput.onSubmit.AddListener((_) => OnJoinPressed());

            PopulateLoginConfigTexts();
            PopulateLoginExtraTexts();
            PrefillLoginInputs();
            
            // Start with both video surfaces hidden. They are shown only when a real video texture is
            // applied, so an inactive stream shows nothing instead of an empty black rectangle.
            SetVideoImageVisible(localVideoImage, false);
            SetVideoImageVisible(remoteVideoImage, false);

            if (webAppManager != null)
            {
                webAppManager.OnConnected += OnConnected;
                webAppManager.OnDisconnected += OnDisconnected;
                webAppManager.OnChatMessageReceived += OnChatReceived;
                webAppManager.OnRemoteStreamStarted += (tex) => {
                    if (remoteVideoImage == null) return;
                    remoteVideoImage.texture = tex;
                    remoteVideoImage.color = Color.white;
                    SetVideoImageVisible(remoteVideoImage, tex != null);
                };
                webAppManager.OnLocalStreamStarted += (tex) => {
                    if (localVideoImage == null) return;
                    localVideoImage.texture = tex;
                    localVideoImage.color = Color.white;
                    SetVideoImageVisible(localVideoImage, tex != null);
                };
                webAppManager.OnStartupDataReceived += OnStartupDataReceived;
                webAppManager.OnStatusUpdate += OnBackendStatus;
                webAppManager.OnConnectionError += (err) => {
                    if (loginStatusText != null) loginStatusText.text = $"<color=red>{err}</color>";
                    if (joinButtonText != null) joinButtonText.text = "Try Again";
                    if (joinButton != null) joinButton.interactable = true;
                    AppendChatMessage($"<color=red>Error: {err}</color>");
                };
            }

            if (qrManager != null)
            {
                // Login/setup-code scanning uses the RAW event because a setup QR detected before
                // calibration would otherwise go dormant and never raise OnQRCodeAdded.
                qrManager.OnRawQRDetected += OnRawQRDetected;
                qrManager.OnScenePermissionResult += OnScenePermissionResult;
                qrManager.OnQRCodeAdded += (qr) => {
                    AppendChatMessage($"[QR Added] {GetColoredPayload(qr)}");
                    RefreshQRCodeDropdown();
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

            AppendChatMessage("<color=green>[System]</color> Session UI Initialized.");
            SetupInputFieldKeyboard(roomCodeInput);
            SetupInputFieldKeyboard(chatInputField);
            SetupInputFieldKeyboard(loginCustomerIdInput);
            SetupInputFieldKeyboard(loginLocationIdInput);

            // IMPORTANT: Do NOT open the physical Passthrough Camera here.
            // With videoSource = PassthroughCamera, StartLocalPreview() opens a WebCamTexture on the
            // headset cameras. Doing that at launch (before Camera permission is granted, and while the
            // OVRPassthroughLayer is initializing) contends with the system passthrough and blacks out
            // the user's view. Local capture is now started only when a remote session goes LIVE
            // (see OnConnected), by which point passthrough is rendering and permissions are resolved.

            // Handle initial state manually
            if (UIManager.Instance != null)
                HandleUIStateChanged(UIManager.Instance.GetCurrentState());
        }

        public void ShowLoginPanel()
        {
            if (sessionUIPanel != null) sessionUIPanel.SetActive(true);
            if (loginPanel != null) loginPanel.SetActive(true);
            if (sessionPanel != null) sessionPanel.SetActive(false);

            // No video should be visible on the login screen.
            SetVideoImageVisible(localVideoImage, false);
            SetVideoImageVisible(remoteVideoImage, false);

            // Make the panel ready to sign in again.
            if (signInButton != null) signInButton.interactable = true;
            if (loginStatusText != null) loginStatusText.text = "Ready to Sign In.";
        }

        /// <summary>Shows/hides a video RawImage (used to make empty streams disappear instead of going black).</summary>
        private void SetVideoImageVisible(RawImage img, bool visible)
        {
            if (img == null) return;
            if (img.gameObject.activeSelf != visible) img.gameObject.SetActive(visible);
        }

        private void OnSignInPressed()
        {
            if (webAppManager == null || webAppManager.config == null)
            {
                if (loginStatusText != null) loginStatusText.text = "<color=red>Backend config missing.</color>";
                return;
            }

            // Manual entry fallback: if the user typed Customer/Location IDs, use them (overrides scan/defaults).
            if (loginCustomerIdInput != null && !string.IsNullOrWhiteSpace(loginCustomerIdInput.text))
                webAppManager.config.customerId = loginCustomerIdInput.text.Trim();
            if (loginLocationIdInput != null && !string.IsNullOrWhiteSpace(loginLocationIdInput.text))
                webAppManager.config.locationId = loginLocationIdInput.text.Trim();
            PopulateLoginConfigTexts();

            if (string.IsNullOrEmpty(webAppManager.config.customerId) || string.IsNullOrEmpty(webAppManager.config.locationId))
            {
                if (loginStatusText != null) loginStatusText.text = "<color=orange>Scan a Login Code, or type Customer ID and Location ID, then Sign In.</color>";
                return;
            }

            if (signInButton != null) signInButton.interactable = false;
            if (loginStatusText != null) loginStatusText.text = "Signing in...";
            
            webAppManager.RegisterAndBoot(webAppManager.config.customerId, webAppManager.config.locationId, (success) => {
                if (success) {
                    ShowJoinScreen();
                } else {
                    if (signInButton != null) signInButton.interactable = true;
                    string detail = webAppManager != null && !string.IsNullOrEmpty(webAppManager.LastError)
                        ? webAppManager.LastError : "Unknown error.";
                    if (loginStatusText != null)
                        loginStatusText.text = $"<color=red>Sign in failed:</color> {Truncate(detail, 120)}";
                    Debug.LogError($"[SessionUI] Sign in failed: {detail}");
                }
            });
        }

        private void OnScanLoginCodePressed()
        {
            // Toggle: a second press cancels scanning AND removes all detection/item markers drawn so far.
            if (_isScanningLoginCode)
            {
                _isScanningLoginCode = false;
                if (qrManager != null)
                {
                    qrManager.StopQRCodeDetection();
                    // FIX: Use ClearAllVisuals to ensure EVERYTHING (pips and any accidental instances) 
                    // is removed from the scene when the user clicks "Cancel Scan".
                    qrManager.ClearAllVisuals();
                }
                HideScanHighlight();
                if (loginStatusText != null) loginStatusText.text = "Scan cancelled.";
                UpdateScanButtonLabel(false);
                return;
            }

            // Guard: QR detection is impossible without the Quest scene permission.
            if (qrManager != null && !qrManager.HasScenePermission)
            {
                if (loginStatusText != null)
                    loginStatusText.text = "<color=orange>Requesting camera/scene permission… grant it, then press Scan again.</color>";
                qrManager.RequestScenePermissionPublic();
                return;
            }

            _isScanningLoginCode = true;
            // Login phase: visual-only scanning (no RoomAnchor / item processing yet).
            if (qrManager != null)
            {
                qrManager.SetScanMode(QrCodeManager.ScanMode.LoginOnly);
                qrManager.StartQRCodeDetection();
                qrManager.EnsureQrTrackingEnabled();
            }
            if (loginStatusText != null) loginStatusText.text = "Look at the Setup QR on the admin Locations page...";
            UpdateScanButtonLabel(true);
        }

        private void OnScenePermissionResult(bool granted)
        {
            if (loginStatusText == null) return;
            loginStatusText.text = granted
                ? "<color=#22D3EE>Scene permission granted. Press Scan Login Code.</color>"
                : "<color=red>Scene permission denied — QR scanning is disabled. Grant it in Settings ▸ Apps ▸ Permissions.</color>";
        }

        private void UpdateScanButtonLabel(bool scanning)
        {
            if (scanLoginCodeButton == null) return;
            var label = scanLoginCodeButton.GetComponentInChildren<TMP_Text>(true);
            if (label != null) label.text = scanning ? "Cancel Scan" : "Scan Login Code";
        }

        // Schema the headset expects from the Setup/Login QR code. customerId + locationId are required;
        // the rest are OPTIONAL and let a private backend authenticate the not-yet-logged-in headset:
        //   token      - bearer/access token sent as "Authorization: Bearer <token>" on every REST call.
        //   apiBaseUrl - overrides the backend host (e.g. when moving Replit -> AWS) for this device.
        //   roomCode   - pre-fills the room to join after sign-in.
        [Serializable] public class SetupQR
        {
            public string customerId;
            public string locationId;
            public string token;
            public string apiBaseUrl;
            public string roomCode;
        }

        /// <summary>
        /// Raw QR detection callback (fires for every detected code, even before calibration).
        /// Gives on-headset feedback (a cyan box + the payload text) and attempts the login parse.
        /// </summary>
        private void OnRawQRDetected(string payload, Vector3 pos, Quaternion rot)
        {
            if (_isScanningLoginCode)
            {
                // Login phase: visual confirmation is handled by QrCodeManager's scaled detection box.
                // Show what was actually read (helps diagnose wrong/!valid codes on device).
                if (loginStatusText != null)
                    loginStatusText.text = $"Detected QR: <color=#22D3EE>{Truncate(payload, 48)}</color>";
                HandleLoginQRScan(payload);
                return;
            }

            // Session/calibration phase: surface every detection in the log so the user can confirm the
            // camera is actually seeing/tracking codes (even ones that are dormant until a RoomAnchor is set).
            AppendChatMessage($"<color=#22D3EE>[Detected]</color> {Truncate(payload, 60)}");
            RefreshQRCodeDropdown();
        }

        private void HandleLoginQRScan(string payload)
        {
            if (string.IsNullOrEmpty(payload) || webAppManager == null || webAppManager.config == null) return;
            SetupQR data = null;
            try { data = JsonUtility.FromJson<SetupQR>(payload); }
            catch { data = null; }

            if (data != null && !string.IsNullOrEmpty(data.customerId) && !string.IsNullOrEmpty(data.locationId))
            {
                // Persist immediately so the device remembers this setup and the fields prepopulate
                // on every subsequent launch (no need to re-scan the setup QR).
                webAppManager.SaveConnectionInfo(data.customerId, data.locationId);

                // Optional fields let a protected backend authenticate this not-yet-logged-in headset.
                // Each is PERSISTED so a single Sign In scan is remembered across app restarts and the
                // user never has to re-scan the setup QR before signing in.
                if (!string.IsNullOrEmpty(data.apiBaseUrl))
                    webAppManager.SaveApiHost(data.apiBaseUrl.Trim());
                if (!string.IsNullOrEmpty(data.token))
                    webAppManager.SetAuthToken(data.token.Trim());
                if (!string.IsNullOrEmpty(data.roomCode))
                    webAppManager.SaveRoomCode(data.roomCode.Trim());

                _isScanningLoginCode = false;
                if (qrManager != null)
                {
                    qrManager.StopQRCodeDetection();
                    qrManager.ClearDetectionMarkers(); // clear the visual-reference boxes; next phase is sign-in
                }
                // Reflect the accepted values in the editable input fields and labels.
                if (loginCustomerIdInput != null) loginCustomerIdInput.text = data.customerId;
                if (loginLocationIdInput != null) loginLocationIdInput.text = data.locationId;
                // Pre-fill the room code so the user can join immediately after signing in.
                if (roomCodeInput != null && !string.IsNullOrEmpty(data.roomCode)) roomCodeInput.text = data.roomCode.Trim();
                PopulateLoginConfigTexts();
                PopulateLoginExtraTexts();
                if (loginStatusText != null) loginStatusText.text = "<color=#22D3EE>Setup code accepted. Press Sign In to continue.</color>";
                if (scanLoginCodeButton != null) scanLoginCodeButton.interactable = true;
                UpdateScanButtonLabel(false);
            }
            else
            {
                // A code was seen but it is not a valid setup code -> tell the user explicitly.
                if (loginStatusText != null)
                    loginStatusText.text = $"<color=orange>Not a valid Setup code. Read: {Truncate(payload, 40)}</color>";
            }
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= max ? s : s.Substring(0, max - 1) + "…";
        }

        /// <summary>Routes backend progress messages to the login status line.</summary>
        private void OnBackendStatus(string msg)
        {
            if (loginStatusText != null) loginStatusText.text = msg;
        }

        // ---- On-headset QR detection box (cyan outline) ----
        private GameObject _scanHighlight;

        private void ShowScanHighlight(Vector3 pos, Quaternion rot)
        {
            if (_scanHighlight == null) _scanHighlight = BuildScanHighlight();
            _scanHighlight.SetActive(true);
            _scanHighlight.transform.SetPositionAndRotation(pos, rot * Quaternion.Euler(0f, 180f, 0f));
            CancelInvoke(nameof(HideScanHighlight));
            Invoke(nameof(HideScanHighlight), 2.5f);
        }

        private void HideScanHighlight()
        {
            if (_scanHighlight != null) _scanHighlight.SetActive(false);
        }

        private GameObject BuildScanHighlight()
        {
            const float s = 0.12f;  // ~ a typical printed QR size
            const float t = 0.006f; // bar thickness
            var root = new GameObject("LoginScanHighlight");
            AddBorderBar(root.transform, new Vector3(0f, s / 2f, 0f), new Vector3(s + t, t, t));
            AddBorderBar(root.transform, new Vector3(0f, -s / 2f, 0f), new Vector3(s + t, t, t));
            AddBorderBar(root.transform, new Vector3(-s / 2f, 0f, 0f), new Vector3(t, s + t, t));
            AddBorderBar(root.transform, new Vector3(s / 2f, 0f, 0f), new Vector3(t, s + t, t));
            return root;
        }

        private void AddBorderBar(Transform parent, Vector3 localPos, Vector3 localScale)
        {
            var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bar.name = "Bar";
            bar.transform.SetParent(parent);
            bar.transform.localPosition = localPos;
            bar.transform.localScale = localScale;
            bar.transform.localRotation = Quaternion.identity;
            var col = bar.GetComponent<Collider>();
            if (col != null) Destroy(col);
            var r = bar.GetComponent<Renderer>();
            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            r.material = new Material(sh);
            r.material.color = new Color(0.13f, 0.83f, 0.93f, 1f); // cyan
        }

        private void SetupInputFieldKeyboard(TMP_InputField input)
        {
            if (input == null) return;

            // Meta's PointableCanvasModule does not auto-select input fields, so onSelect would
            // never fire on device. This component supplies the missing selection on pointer click.
            if (input.GetComponent<VrInputFieldActivator>() == null)
                input.gameObject.AddComponent<VrInputFieldActivator>();

            input.onSelect.AddListener((s) => StartCoroutine(OpenKeyboard(input)));
        }

        private IEnumerator OpenKeyboard(TMP_InputField input)
        {
            yield return new WaitForSeconds(0.1f);
            // No software keyboard on this platform (e.g. the Editor without a device). Opening it
            // returns an object whose native handle is invalid, so reading .status throws an NRE.
            if (!TouchScreenKeyboard.isSupported) yield break;
            TouchScreenKeyboard keyboard = TouchScreenKeyboard.Open(input.text, TouchScreenKeyboardType.Default);
            while (keyboard != null && keyboard.status == TouchScreenKeyboard.Status.Visible)
            {
                input.text = keyboard.text;
                yield return null;
            }
            if (keyboard != null && keyboard.status == TouchScreenKeyboard.Status.Done) input.text = keyboard.text;
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

            // The local passthrough preview should ALWAYS stream while the session panel is open so the
            // user can confirm the headset is capturing the passthrough camera, even before a remote
            // expert connects. StartLocalPreview() is a no-op if the local track is already running.
            if (webAppManager != null) webAppManager.StartLocalPreview();

            // Make sure QR detection is actually running (Full mode) while the session panel is open so
            // codes get tracked and added to the "Look At" list.
            if (qrManager != null)
            {
                qrManager.SetScanMode(QrCodeManager.ScanMode.Full);
                if (!qrManager.IsDetecting) qrManager.StartQRCodeDetection();
                qrManager.EnsureQrTrackingEnabled();
                RefreshQRCodeDropdown();
            }
            UpdateDetectionButtonLabel();
        }

        private void UpdateDetectionButtonLabel()
        {
            if (toggleDetectionButtonText == null) return;
            bool detecting = qrManager != null && qrManager.IsDetecting;
            toggleDetectionButtonText.text = detecting ? "Stop Detection" : "Start Detection";
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
            options.Add(NewBlackOption("Stop Pointing"));
            
            qrCodeList.Clear();
            foreach (var kvp in qrManager.TrackedQRCodes)
            {
                if (kvp.Value.fullPayload.Contains("RoomAnchor")) continue;
                qrCodeList.Add(kvp.Value);
                string displayName = !string.IsNullOrEmpty(kvp.Value.identifierKey) ? kvp.Value.identifierKey : kvp.Value.fullPayload;
                if (displayName.Length > 30) displayName = displayName.Substring(0, 27) + "...";
                options.Add(NewBlackOption(displayName));
            }
            qrCodeDropdown.AddOptions(options);

            // The dropdown caption shows the current selection; keep it black too.
            if (qrCodeDropdown.captionText != null) qrCodeDropdown.captionText.color = Color.black;
        }

        // TMP_Dropdown.OptionData.color defaults to white and overrides the item label color, which
        // would make the text invisible on the light dropdown background. Force every option black.
        private static TMP_Dropdown.OptionData NewBlackOption(string text)
        {
            return new TMP_Dropdown.OptionData(text) { color = Color.black };
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
            // Tear down the live session (network + local capture) and return to the Sign In panel,
            // ready to sign in again.
            webAppManager?.Disconnect();
            if (qrManager != null) qrManager.StopQRCodeDetection();
            AppendChatMessage("<color=orange>[Session] Left session — returning to Sign In.</color>");
            UIManager.Instance?.SetState(UIManager.UIState.Login);
        }

        private void OnConnected()
        {
            if (connectionStatusText != null) connectionStatusText.text = "Status: LIVE";
            AppendChatMessage("--- Connected ---");

            // Now that a session is live (passthrough is already rendering and Camera permission has
            // been resolved by the startup permission flow), it is safe to open the Passthrough Camera
            // and begin the local preview / outgoing video stream to the remote expert.
            if (webAppManager != null) webAppManager.StartLocalPreview();
        }

        private void OnDisconnected()
        {
            if (connectionStatusText != null) connectionStatusText.text = "Status: DISCONNECTED";
            // The remote feed is gone -> hide its surface (the local preview keeps running).
            if (remoteVideoImage != null) remoteVideoImage.texture = null;
            SetVideoImageVisible(remoteVideoImage, false);
        }

        private void OnChatReceived(string msg) => AppendChatMessage($"Admin: {msg}");

        private void OnToggleDetectQR()
        {
            if (qrManager == null) return;
            if (qrManager.IsDetecting)
            {
                qrManager.StopQRCodeDetection();
                AppendChatMessage("<color=orange>[Detection] STOPPED.</color>");
            }
            else
            {
                qrManager.SetScanMode(QrCodeManager.ScanMode.Full);
                qrManager.StartQRCodeDetection();
                qrManager.EnsureQrTrackingEnabled();
                AppendChatMessage("<color=green>[Detection] STARTED.</color> Look at the RoomAnchor first, then QR codes.");
            }
            UpdateDetectionButtonLabel();
        }

        private void OnClearQRPressed()
        {
            if (qrManager == null) return;
            int count = qrManager.TrackedQRCodes.Count;
            qrManager.ClearQRCodes();
            RefreshQRCodeDropdown();
            AppendChatMessage($"<color=green>[Clear]</color> Cleared {count} QR code(s) from the list.");
        }

        /// <summary>
        /// Seeds the editable Customer/Location input fields with the current config values
        /// so the user can connect immediately, or click a field to bring up the VR keyboard
        /// and edit them if a setup QR code is not detected.
        /// </summary>
        private void PrefillLoginInputs()
        {
            var cfg = webAppManager != null ? webAppManager.config : null;
            if (cfg == null) return;

            // Only prefill when empty so we never clobber what the user has typed.
            if (loginCustomerIdInput != null && string.IsNullOrEmpty(loginCustomerIdInput.text) && !string.IsNullOrEmpty(cfg.customerId))
                loginCustomerIdInput.text = cfg.customerId;
            if (loginLocationIdInput != null && string.IsNullOrEmpty(loginLocationIdInput.text) && !string.IsNullOrEmpty(cfg.locationId))
                loginLocationIdInput.text = cfg.locationId;

            // The session-screen Location field (used for QR push/pull) is also seeded.
            if (locationIdInput != null && string.IsNullOrEmpty(locationIdInput.text) && !string.IsNullOrEmpty(cfg.locationId))
                locationIdInput.text = cfg.locationId;

            // Seed the room code from the remembered setup-QR value so the user can join immediately.
            if (roomCodeInput != null && string.IsNullOrEmpty(roomCodeInput.text)
                && webAppManager != null && !string.IsNullOrEmpty(webAppManager.currentRoomCode))
                roomCodeInput.text = webAppManager.currentRoomCode;
        }

        /// <summary>Shows the current backend host / customer / location on the Login panel.</summary>
        private void PopulateLoginConfigTexts()
        {
            var cfg = webAppManager != null ? webAppManager.config : null;
            if (cfg == null) return;
            if (apiHostText != null) apiHostText.text = $"Host: {cfg.apiHost}";
            if (customerIdText != null) customerIdText.text = $"Customer: {(string.IsNullOrEmpty(cfg.customerId) ? "(scan or set)" : cfg.customerId)}";
            if (locationIdText != null) locationIdText.text = $"Location: {(string.IsNullOrEmpty(cfg.locationId) ? "(scan or set)" : cfg.locationId)}";
        }

        /// <summary>
        /// Shows the extra values captured from the Sign In QR code (API URL, room code, token) as
        /// read-only labels so the user can confirm a single scan stored everything needed to sign in.
        /// </summary>
        private void PopulateLoginExtraTexts()
        {
            var cfg = webAppManager != null ? webAppManager.config : null;
            string host = cfg != null ? cfg.apiHost : "";
            string room = webAppManager != null ? webAppManager.currentRoomCode : "";
            string token = webAppManager != null ? webAppManager.AuthToken : "";

            if (loginApiUrlText != null)
                loginApiUrlText.text = $"API URL: {(string.IsNullOrEmpty(host) ? "(scan or set)" : host)}";
            if (loginRoomCodeText != null)
                loginRoomCodeText.text = $"Room: {(string.IsNullOrEmpty(room) ? "(scan or set)" : room)}";
            if (loginTokenText != null)
                loginTokenText.text = $"Token: {(string.IsNullOrEmpty(token) ? "(none)" : "stored " + Truncate(token, 16))}";
        }

        /// <summary>Uploads the current local QR calibration to the backend for this location.</summary>
        private void OnPushQRPressed()
        {
            if (qrManager == null || webAppManager == null) return;
            string locId = GetActiveLocationId();
            if (string.IsNullOrEmpty(locId)) { AppendChatMessage("<color=red>[Push] No Location ID set.</color>"); return; }

            int count = qrManager.TrackedQRCodes.Count;
            string json = qrManager.GetQRCodeDataAsJson(webAppManager.tevrHeadsetId);
            AppendChatMessage($"[Push] Uploading {count} detected QR Code(s)...");
            if (pushQRButton != null) pushQRButton.interactable = false;
            webAppManager.PostData($"locations/{locId}/qr-codes", json,
                (res) => { AppendChatMessage($"<color=green>[Push] Pushed {count} detected QR Code(s) to the server.</color>"); if (pushQRButton != null) pushQRButton.interactable = true; },
                (err) => { AppendChatMessage($"<color=red>[Push] Failed: {err}</color>"); if (pushQRButton != null) pushQRButton.interactable = true; });
        }

        /// <summary>Fetches the latest QR calibration for this location from the backend and applies it.</summary>
        private void OnPullQRPressed()
        {
            if (qrManager == null || webAppManager == null) return;
            string locId = GetActiveLocationId();
            if (string.IsNullOrEmpty(locId)) { AppendChatMessage("<color=red>[Pull] No Location ID set.</color>"); return; }

            AppendChatMessage("[Pull] Fetching calibration...");
            if (pullQRButton != null) pullQRButton.interactable = false;
            webAppManager.GetData($"locations/{locId}/qr-codes",
                (res) => {
                    int count = ApplyPulledCalibration(res);
                    RefreshQRCodeDropdown();
                    AppendChatMessage($"<color=green>[Pull] Pulled/downloaded {count} QR code(s) from the server.</color>");
                    if (pullQRButton != null) pullQRButton.interactable = true;
                },
                (err) => { AppendChatMessage($"<color=red>[Pull] Failed: {err}</color>"); if (pullQRButton != null) pullQRButton.interactable = true; });
        }

        private string GetActiveLocationId()
        {
            if (locationIdInput != null && !string.IsNullOrEmpty(locationIdInput.text)) return locationIdInput.text.Trim();
            if (webAppManager != null && !string.IsNullOrEmpty(webAppManager.tevrLocationId)) return webAppManager.tevrLocationId;
            if (webAppManager != null && webAppManager.config != null) return webAppManager.config.locationId;
            return null;
        }

        [Serializable] private class PulledQR { public string qrValue; public Vector3 position; public Quaternion rotation; }
        [Serializable] private class PulledCalibration { public string headsetId; public System.Collections.Generic.List<PulledQR> qrCodes; }

        private int ApplyPulledCalibration(string json)
        {
            if (string.IsNullOrEmpty(json) || qrManager == null) return 0;
            try
            {
                var data = JsonUtility.FromJson<PulledCalibration>(json);
                if (data == null || data.qrCodes == null) return 0;
                var valid = new List<string>();
                foreach (var q in data.qrCodes)
                {
                    qrManager.UpdateQRCodeFromRemote(q.qrValue, q.position, q.rotation);
                    if (!string.IsNullOrEmpty(q.qrValue)) valid.Add(q.qrValue);
                }
                qrManager.AddValidPayloads(valid);
                return data.qrCodes.Count;
            }
            catch (Exception e)
            {
                AppendChatMessage($"<color=red>[Pull] Parse error: {e.Message}</color>");
                return 0;
            }
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
                qrManager?.ClearFocus();
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
                webAppManager.OnStatusUpdate -= OnBackendStatus;
            }
            if (qrManager != null)
            {
                qrManager.OnRawQRDetected -= OnRawQRDetected;
                qrManager.OnScenePermissionResult -= OnScenePermissionResult;
            }
            if (_scanHighlight != null) Destroy(_scanHighlight);
        }
}
}