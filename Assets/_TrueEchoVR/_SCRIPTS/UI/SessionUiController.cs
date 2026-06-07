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
        public GameObject sessionPanel;

        public TMP_InputField roomCodeInput;
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

        [Header("QR Detection Indicator (persistent ON/OFF status)")]
        [Tooltip("Shows whether QR detection is running and in which phase (SignIn/Session) plus a live count. " +
                 "One label on the Login panel, one on the Session panel.")]
        public TMP_Text loginDetectionStatusText;
        public TMP_Text sessionDetectionStatusText;

        [Header("Login Manual Entry (fallback to scanning)")]
        public TMP_InputField loginCustomerIdInput;
        public TMP_InputField loginLocationIdInput;

        [Header("Backend URL (default + editable, stored on device)")]
        [Tooltip("Editable backend base URL (e.g. https://host/api). The Sign In QR no longer carries the " +
                 "URL — it is stored on the device with a default and can be overridden here. It is persisted " +
                 "locally and pre-populated on every launch.")]
        public TMP_InputField loginApiUrlInput;

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

            // Backend URL: editing it overrides + persists the device's backend base URL.
            if (loginApiUrlInput != null)
                loginApiUrlInput.onEndEdit.AddListener((v) =>
                {
                    if (webAppManager != null && !string.IsNullOrWhiteSpace(v))
                    {
                        webAppManager.SaveBackendUrl(v.Trim());
                        PopulateLoginConfigTexts();
                        AppendChatMessage($"<color=#22D3EE>[Backend]</color> URL set to {Truncate(v.Trim(), 60)}");
                    }
                });

            PopulateLoginConfigTexts();
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
                    AppendChatMessage($"<color=red>Error: {err}</color>");
                };
            }

            if (qrManager != null)
            {
                // Login/setup-code scanning uses the RAW event because a setup QR detected before
                // calibration would otherwise go dormant and never raise OnQRCodeAdded.
                qrManager.OnRawQRDetected += OnRawQRDetected;
                qrManager.OnScenePermissionResult += OnScenePermissionResult;
                qrManager.OnDetectionStateChanged += OnDetectionStateChanged;
                qrManager.OnQRCodeAdded += (qr) => {
                    AppendChatMessage($"[QR Added] {GetColoredPayload(qr)}");
                    RefreshQRCodeDropdown();
                    UpdateDetectionIndicator();
                };
                // PERF: do NOT log on OnQRCodeUpdated — it fires every frame from tracking jitter, and
                // AppendChatMessage re-assigns the whole chat string AND calls Canvas.ForceUpdateCanvases()
                // each time. That per-frame canvas rebuild (per visible code) was a primary cause of the
                // jitter/hitching. Add/remove events still log below.
                qrManager.OnQRCodeRemoved += (key) => { AppendChatMessage($"[QR Removed] {key}"); RefreshQRCodeDropdown(); UpdateDetectionIndicator(); };
                qrManager.OnRoomAnchorDiscovered += (qr) => {
                    AppendChatMessage($"[Anchor Discovered] <color=green>{qr.fullPayload}</color>");
                };
            }

            AppendChatMessage("<color=green>[System]</color> Session UI Initialized.");
            UpdateDetectionIndicator();
            SetupInputFieldKeyboard(roomCodeInput);
            SetupInputFieldKeyboard(chatInputField);
            SetupInputFieldKeyboard(loginCustomerIdInput);
            SetupInputFieldKeyboard(loginLocationIdInput);
            SetupInputFieldKeyboard(loginApiUrlInput);

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

            // Re-arm SignIn-phase detection so the Sign In QR is auto-detected on this screen (e.g. after
            // leaving a session). The headset is always looking for the setup code while the login panel
            // is shown, and the indicator reflects it.
            if (qrManager != null && qrManager.autoStartDetection)
            {
                qrManager.SetScanMode(QrCodeManager.ScanMode.LoginOnly);
                if (!qrManager.IsDetecting) qrManager.StartQRCodeDetection();
                qrManager.EnsureQrTrackingEnabled();
            }
            UpdateDetectionIndicator();
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
                    // Route through the central UI state so UIManager.currentState matches the visible panel
                    // (ShowJoinScreen bypassed SetState, leaving state stuck on Login). The session panel is
                    // shown now; SessionFlowManager then drives "wait for RoomAnchor" from here.
                    if (UIManager.Instance != null) UIManager.Instance.SetState(UIManager.UIState.Session);
                    else ShowJoinScreen();
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

        // Schema the headset accepts from the Setup/Login QR code. TWO shapes are supported:
        //   Legacy: customerId + locationId are required (the rest optional).
        //   New:    setupCode + apiBaseUrl are required; the headset resolves customerId/locationId
        //           from the backend (GET {apiBaseUrl}/setup/{setupCode}) after the scan.
        // Optional fields let a private backend authenticate the not-yet-logged-in headset:
        //   token      - bearer/access token sent as "Authorization: Bearer <token>" on every REST call.
        //   apiBaseUrl - overrides the backend base URL (e.g. when moving Replit -> AWS) for this device.
        //   roomCode   - pre-fills the room to join after sign-in.
        [Serializable] public class SetupQR
        {
            public string customerId;
            public string locationId;
            public string token;
            public string apiBaseUrl;
            public string roomCode;
            public string setupCode;
        }

        /// <summary>
        /// Raw QR detection callback (fires for every detected code, even before calibration).
        /// Gives on-headset feedback (a cyan box + the payload text) and attempts the login parse.
        /// </summary>
        private void OnRawQRDetected(string payload, Vector3 pos, Quaternion rot)
        {
            // Keep the live "QR Detection ON (N seen)" indicator current on every detection.
            UpdateDetectionIndicator();

            bool signInPhase = qrManager != null && qrManager.State == QrCodeManager.DetectionState.SignIn;

            // SignIn phase: the headset auto-processes the setup/login code WITHOUT requiring the user to
            // press "Scan Login Code" (detection runs from launch). When the user explicitly pressed Scan
            // (_isScanningLoginCode) we also echo the raw read and announce invalid codes; in passive
            // auto mode we stay quiet on non-setup codes so random QRs don't spam the status line.
            if (_isScanningLoginCode || signInPhase)
            {
                if (_isScanningLoginCode && loginStatusText != null)
                    loginStatusText.text = $"Detected QR: <color=#22D3EE>{Truncate(payload, 48)}</color>";
                HandleLoginQRScan(payload, announceInvalid: _isScanningLoginCode);
                return;
            }

            // Session/calibration phase: surface every detection in the log so the user can confirm the
            // camera is actually seeing/tracking codes (even ones that are dormant until a RoomAnchor is set).
            AppendChatMessage($"<color=#22D3EE>[Detected]</color> {Truncate(payload, 60)}");
            RefreshQRCodeDropdown();
        }

        private void OnDetectionStateChanged(QrCodeManager.DetectionState state)
        {
            UpdateDetectionIndicator();
            AppendChatMessage($"<color=#22D3EE>[Detection]</color> Phase: <b>{state}</b>");
        }

        /// <summary>Refreshes the persistent "QR Detection ON/OFF" indicator on both panels.</summary>
        private void UpdateDetectionIndicator()
        {
            string text;
            if (qrManager == null)
            {
                text = "<color=#A0A0A0>○ QR Detection: unavailable</color>";
            }
            else
            {
                switch (qrManager.State)
                {
                    case QrCodeManager.DetectionState.SignIn:
                        text = $"<color=#22D3EE>● QR Detection: ON</color>  SignIn — looking for Sign In code ({qrManager.DetectionMarkerCount} seen)";
                        break;
                    case QrCodeManager.DetectionState.Session:
                        text = $"<color=#22D3EE>● QR Detection: ON</color>  Session — {qrManager.TrackedQRCodes.Count} tracked";
                        break;
                    default:
                        text = "<color=#A0A0A0>○ QR Detection: OFF</color>";
                        break;
                }
                if (!qrManager.HasScenePermission)
                    text += "  <color=orange>(scene permission needed)</color>";
            }
            if (loginDetectionStatusText != null) loginDetectionStatusText.text = text;
            if (sessionDetectionStatusText != null) sessionDetectionStatusText.text = text;
        }

        private void HandleLoginQRScan(string payload, bool announceInvalid = true)
        {
            if (string.IsNullOrEmpty(payload) || webAppManager == null || webAppManager.config == null) return;
            SetupQR data = null;
            try { data = JsonUtility.FromJson<SetupQR>(payload); }
            catch { data = null; }

            bool legacy = data != null && !string.IsNullOrEmpty(data.customerId) && !string.IsNullOrEmpty(data.locationId);
            bool jsonSetupCode = data != null && !string.IsNullOrEmpty(data.setupCode) && !string.IsNullOrEmpty(data.apiBaseUrl);
            // Smallest payload: a BARE alphanumeric code (no JSON). The backend URL is NOT in the QR — the
            // device uses its stored/default URL to resolve it.
            bool bareSetupCode = qrManager != null && qrManager.IsBareSetupCode(payload);

            if (jsonSetupCode) { AcceptSetupCode(data.setupCode.Trim(), data.apiBaseUrl.Trim(), data.token); return; }
            if (bareSetupCode) { AcceptSetupCode(payload.Trim(), null, null); return; }
            if (legacy) { AcceptLegacySetupScan(data); return; }

            // A code was seen but it is no recognised setup shape. Only announce when the user explicitly
            // initiated a scan (otherwise passive auto-detection would spam this for every QR).
            if (announceInvalid && loginStatusText != null)
                loginStatusText.text = $"<color=orange>Not a valid Setup code. Read: {Truncate(payload, 40)}</color>";
        }

        /// <summary>
        /// Accepts a setup code (bare or JSON). If apiBaseUrl is provided it overrides + persists the
        /// backend URL; otherwise the device's stored/default URL is used (the minimal-QR flow). The setup
        /// code is then resolved against the backend to obtain customerId + locationId before sign-in.
        /// </summary>
        private void AcceptSetupCode(string setupCode, string apiBaseUrlOrNull, string tokenOrNull)
        {
            // Persist setupCode (+ apiBaseUrl only if the QR carried one) so a single scan survives restarts.
            // Does NOT touch BackendConfig.asset on disk.
            webAppManager.SaveSetupProvisioning(apiBaseUrlOrNull, setupCode);
            if (!string.IsNullOrEmpty(tokenOrNull))
                webAppManager.SetAuthToken(tokenOrNull.Trim());

            StopLoginScan();
            PopulateLoginConfigTexts();
            if (loginApiUrlInput != null) loginApiUrlInput.text = webAppManager.GetBackendUrl();
            if (loginStatusText != null)
                loginStatusText.text = $"<color=#22D3EE>Setup code '{Truncate(setupCode, 12)}' accepted. Resolving with server…</color>";
            AppendChatMessage($"<color=#22D3EE>[Setup]</color> Resolving code '{Truncate(setupCode, 12)}' via {Truncate(webAppManager.GetBackendUrl(), 50)}");

            // Resolve the setup code -> customerId + locationId from the backend, then the normal
            // Sign In (RegisterAndBoot) proceeds from there.
            webAppManager.ResolveSetup(setupCode, (ok) =>
            {
                if (ok)
                {
                    var cfg = webAppManager.config;
                    if (loginCustomerIdInput != null) loginCustomerIdInput.text = cfg.customerId;
                    if (loginLocationIdInput != null) loginLocationIdInput.text = cfg.locationId;
                    if (roomCodeInput != null && !string.IsNullOrEmpty(webAppManager.currentRoomCode))
                        roomCodeInput.text = webAppManager.currentRoomCode;
                    PopulateLoginConfigTexts();
                    if (loginStatusText != null)
                        loginStatusText.text = "<color=#22D3EE>Setup resolved. Press Sign In to continue.</color>";
                }
                else
                {
                    string detail = !string.IsNullOrEmpty(webAppManager.LastError) ? webAppManager.LastError : "Unknown error.";
                    if (loginStatusText != null)
                        loginStatusText.text = $"<color=red>Setup resolve failed:</color> {Truncate(detail, 100)}";
                    Debug.LogError($"[SessionUI] Setup resolve failed: {detail}");
                }
            });
        }

        /// <summary>Legacy setup QR ({"customerId","locationId", ...}) — unchanged behavior.</summary>
        private void AcceptLegacySetupScan(SetupQR data)
        {
            // Persist immediately so the device remembers this setup and the fields prepopulate
            // on every subsequent launch (no need to re-scan the setup QR).
            webAppManager.SaveConnectionInfo(data.customerId, data.locationId);

            // Optional fields let a protected backend authenticate this not-yet-logged-in headset.
            if (!string.IsNullOrEmpty(data.apiBaseUrl))
                webAppManager.SaveBackendUrl(data.apiBaseUrl.Trim());
            if (!string.IsNullOrEmpty(data.token))
                webAppManager.SetAuthToken(data.token.Trim());
            if (!string.IsNullOrEmpty(data.roomCode))
                webAppManager.SaveRoomCode(data.roomCode.Trim());

            StopLoginScan();
            // Reflect the accepted values in the editable input fields and labels.
            if (loginCustomerIdInput != null) loginCustomerIdInput.text = data.customerId;
            if (loginLocationIdInput != null) loginLocationIdInput.text = data.locationId;
            if (loginApiUrlInput != null) loginApiUrlInput.text = webAppManager.GetBackendUrl();
            // Pre-fill the room code so the user can join immediately after signing in.
            if (roomCodeInput != null && !string.IsNullOrEmpty(data.roomCode)) roomCodeInput.text = data.roomCode.Trim();
            PopulateLoginConfigTexts();
            if (loginStatusText != null)
                loginStatusText.text = "<color=#22D3EE>Setup code accepted. Press Sign In to continue.</color>";
        }

        /// <summary>Stops the login-phase QR scan and clears the visual-reference markers.</summary>
        private void StopLoginScan()
        {
            _isScanningLoginCode = false;
            if (qrManager != null)
            {
                qrManager.StopQRCodeDetection();
                qrManager.ClearDetectionMarkers(); // clear the visual-reference boxes; next phase is sign-in
            }
            if (scanLoginCodeButton != null) scanLoginCodeButton.interactable = true;
            UpdateScanButtonLabel(false);
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
            options.Add(NewColoredOption("Stop Pointing", Color.black));

            qrCodeList.Clear();
            if (qrManager != null)
            {
                // 1) Locally discovered codes (pointable). Green if in the legit list, red if unlisted.
                var matchedValid = new HashSet<string>();
                foreach (var kvp in qrManager.TrackedQRCodes)
                {
                    var inst = kvp.Value;
                    if (inst == null || string.IsNullOrEmpty(inst.fullPayload)) continue;
                    if (inst.fullPayload.Contains("RoomAnchor")) continue;

                    bool listed = qrManager.IsValidListed(inst.fullPayload);
                    if (listed) matchedValid.Add(inst.fullPayload);

                    string name = !string.IsNullOrEmpty(inst.identifierKey) ? inst.identifierKey : inst.fullPayload;
                    string label = listed ? ShortenLabel(name) : ShortenLabel(name) + "  — unlisted";
                    options.Add(NewColoredOption(label, listed ? QrMatchedColor : QrUnlistedColor));
                    qrCodeList.Add(inst);
                }

                // 2) Legit-list codes that were never discovered locally (orange, not pointable).
                foreach (var payload in qrManager.ValidPayloads)
                {
                    if (string.IsNullOrEmpty(payload) || payload.Contains("RoomAnchor")) continue;
                    if (matchedValid.Contains(payload)) continue;
                    options.Add(NewColoredOption(ShortenLabel(payload) + "  — not visible", QrMissingColor));
                    qrCodeList.Add(null); // listed but not currently tracked → nothing to point at
                }
            }
            qrCodeDropdown.AddOptions(options);

            // The dropdown caption shows the current selection; keep it black too.
            if (qrCodeDropdown.captionText != null) qrCodeDropdown.captionText.color = Color.black;
        }

        // TMP_Dropdown.OptionData.color defaults to white and overrides the item label color, which
        // would make the text invisible on the light dropdown background. Always set an explicit colour.
        private static TMP_Dropdown.OptionData NewColoredOption(string text, Color color)
        {
            return new TMP_Dropdown.OptionData(text) { color = color };
        }

        private static string ShortenLabel(string s)
        {
            if (string.IsNullOrEmpty(s)) return "(unknown)";
            return s.Length > 36 ? s.Substring(0, 33) + "..." : s;
        }

        // QR dropdown legend colours (chosen to stay legible on the light dropdown background):
        //   Green  = in the server "legit" list AND discovered locally (all good).
        //   Orange = in the legit list but NOT currently discovered locally (missing — go find it).
        //   Red    = discovered locally but NOT in the legit list (unexpected / unlisted code).
        // To use blue instead of red for the unlisted state, change QrUnlistedColor below.
        private static readonly Color QrMatchedColor  = new Color(0.05f, 0.55f, 0.12f);
        private static readonly Color QrMissingColor  = new Color(0.85f, 0.45f, 0.00f);
        private static readonly Color QrUnlistedColor = new Color(0.80f, 0.10f, 0.10f);

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

            // Pre-populate the editable Backend URL with the device's stored/default value (the QR no
            // longer carries it). Editing the field overrides + persists it (see onEndEdit wiring).
            if (loginApiUrlInput != null && string.IsNullOrEmpty(loginApiUrlInput.text) && webAppManager != null)
                loginApiUrlInput.text = webAppManager.GetBackendUrl();
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
            if (qrManager == null || data == null || data.qrCodes == null) return;
            var validValues = new List<string>(data.qrCodes.Count);
            foreach (var anchor in data.qrCodes)
            {
                qrManager.UpdateQRCodeFromRemote(anchor.qrValue, anchor.position, anchor.rotation);
                if (!string.IsNullOrEmpty(anchor.qrValue)) validValues.Add(anchor.qrValue);
            }
            // Feed the server's authoritative QR list into the classifier so these item codes show as
            // ValidListed (blue) instead of Unlisted (orange) when detected.
            qrManager.AddValidPayloads(validValues);
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
            {
                var inst = qrCodeList[qrIndex];
                if (inst == null)
                {
                    // Listed in the server "legit" set but not currently discovered locally → nothing to
                    // point at. Inform the user instead of silently doing nothing.
                    qrManager?.ClearFocus();
                    statusUI?.ClearHighlight();
                    AppendChatMessage("<color=#D97200>[Point] That QR code is in the list but not currently visible — scan it to point.</color>");
                    return;
                }
                sessionInit?.PointToQRCode(inst);
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
                webAppManager.OnStatusUpdate -= OnBackendStatus;
            }
            if (qrManager != null)
            {
                qrManager.OnRawQRDetected -= OnRawQRDetected;
                qrManager.OnScenePermissionResult -= OnScenePermissionResult;
                qrManager.OnDetectionStateChanged -= OnDetectionStateChanged;
            }
            if (_scanHighlight != null) Destroy(_scanHighlight);
        }
}
}