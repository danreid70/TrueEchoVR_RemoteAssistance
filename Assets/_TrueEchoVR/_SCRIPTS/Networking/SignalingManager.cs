using UnityEngine;
using Unity.WebRTC;
using Meta.Net.NativeWebSocket;
using System.Collections;
using System;
using UnityEngine.Networking;
using System.Collections.Generic;

namespace TEVR
{
    public class SignalingManager : MonoBehaviour
    {
        public static SignalingManager Instance { get; private set; }

        [Header("Backend Configuration")]
        public BackendConfig config;
        
        [Header("Session Info")]
        public string tevrHeadsetId; 
        public string tevrLocationId;
        public string currentRoomCode;

        // Optional bearer/access token used to authenticate the headset against a protected backend.
        // Supplied by the setup QR code (see SessionUiController.SetupQR.token) or set programmatically.
        [SerializeField] private string authToken = "";
        public bool HasAuthToken => !string.IsNullOrEmpty(authToken);
        /// <summary>The stored bearer token (for read-only display/verification on the login panel).</summary>
        public string AuthToken => authToken;
        public void SetAuthToken(string token)
        {
            authToken = token ?? "";
            if (!string.IsNullOrEmpty(authToken)) PlayerPrefs.SetString("TEVR_AUTH_TOKEN", authToken);
            PlayerPrefs.Save();
        }

        public bool IsConnected => _ws != null && _ws.State == WebSocketState.Open;
        public float currentLatency { get; private set; }
        
        [Header("Reconnection Settings")]
        public bool autoReconnect = true;
        public int maxReconnectAttempts = 5;
        public float reconnectDelay = 3f;

        [Header("Debugging")]
        [Tooltip("Logs every raw Engine.IO/Socket.IO packet sent (=>) and received (<=). Invaluable for diagnosing the Replit handshake.")]
        public bool verboseSocketLogging = true;

        // True only after the Socket.IO namespace CONNECT ('40') has been acknowledged by the server.
        public bool IsSocketConnected { get; private set; }

        private int _reconnectCount = 0;
        private Coroutine _batteryCoroutine;
        // Set when a REST call returns an auth failure (token/headset expired). When true, the boot flow
        // must surface a re-scan prompt instead of dropping to Demo Mode.
        private bool _credentialsExpired = false;

        private WebSocket _ws;
        private RTCPeerConnection _pc;
        private MediaStream _localStream;
        private VideoStreamTrack _videoTrack;
        private AudioStreamTrack _audioTrack;
        private string _remoteSocketId;

        public enum VideoSource
        {
            /// <summary>
            /// EXACTLY what the user sees: the real-world passthrough camera as the background
            /// COMPOSITED with the Unity-rendered virtual content (UI panels, QR markers, etc.) on top.
            /// This is what should be streamed/recorded for remote assistance.
            /// </summary>
            Composite,
            /// <summary>Capture the headset passthrough camera (real world only) via WebCamTexture. Requires CAMERA permission.</summary>
            PassthroughCamera,
            /// <summary>Capture the Unity rendered eye view (virtual content only). No real-world imagery.</summary>
            RenderedCamera
        }

        // Layer used exclusively by the composite-capture background quad so it is rendered into the
        // streamed RenderTexture but never shown to the user's own eyes.
        private const string CompositeLayerName = "MRCapture";

        [Header("Video Settings")]
        [Tooltip("PassthroughCamera streams the real world (requires Camera permission + passthrough camera access). " +
                 "RenderedCamera streams only Unity-rendered content. If passthrough is unavailable at runtime, " +
                 "the system automatically falls back to the rendered camera.")]
        public VideoSource videoSource = VideoSource.Composite;

        [Tooltip("Legacy flag kept for compatibility. When true, forces the PassthroughCamera (WebCamTexture) source.")]
        public bool useWebcam = false;

        public Camera captureCamera;
        public Vector2Int captureResolution = new Vector2Int(1280, 720);

        [Tooltip("Optional: substring of the desired WebCamTexture device name. Leave empty to auto-pick the passthrough camera.")]
        public string webcamDeviceName = "";

        [Tooltip("How long (seconds) to wait for Camera permission + the passthrough camera to start before falling back to the rendered view.")]
        public float passthroughStartTimeout = 8f;

        [Header("Composite Alignment (Quest passthrough camera)")]
        [Tooltip("Horizontal field of view (degrees) of the Quest passthrough camera. The VR overlay in the " +
                 "streamed/preview composite is rendered at THIS fov (not the eye fov) so virtual content lines " +
                 "up with the real-world passthrough background as you turn your head. Quest 3 default ~82°. " +
                 "Tune on-device if the overlay drifts from the real world. Only affects the composite stream.")]
        [Range(40f, 140f)]
        public float passthroughHorizontalFovDeg = 82f;

        private WebCamTexture _webcamTexture;
        private RenderTexture _captureRT;
        private Camera _internalCaptureCamera;

        // Composite capture (real-world passthrough + virtual content).
        private Camera _compositeCamera;
        private RenderTexture _compositeRT;
        private GameObject _compositeQuad;
        private Material _compositeBgMaterial;
        private Camera _compositeEye;        // the user's eye camera we mirror
        private int _compositeRestoreMask;   // eye cullingMask to restore on teardown
        private bool _compositeMaskModified;
        private Coroutine _compositeMaintainCo; // keeps the passthrough background bound/oriented + recovers it

        public Action OnConnected;
        public Action OnDisconnected;
        public Action<string> OnConnectionError;
        /// <summary>Fired at the start of each automatic reconnect attempt: (attemptNumber, maxAttempts).</summary>
        public Action<int, int> OnReconnecting;
        /// <summary>Fired when automatic reconnection is abandoned (attempts exhausted or auto-reconnect off
        /// after an abnormal close). The UI should offer recovery (retry / re-sign-in / demo).</summary>
        public Action OnReconnectFailed;
        /// <summary>Fired when the backend rejects stored credentials (HTTP 401, or 403/404 on startup-data).
        /// Credentials are cleared; the UI should prompt the user to re-scan a Login Code (NOT silently demo).</summary>
        public Action OnCredentialsExpired;
        public Action<string> OnChatMessageReceived;
        // (name, qrCode, position, rotation). qrCode is the QR payload value — the most reliable id for
        // cross-referencing a locally-tracked code; name is the human-friendly label. position/rotation are
        // null unless the admin's "point-to"/"look-at" command included real-world coordinates.
        public Action<string, string, Vector3?, Quaternion?> OnPointToReceived;
        public Action<StartupData> OnStartupDataReceived;
        public Action<Texture> OnRemoteStreamStarted;
        public Action<Texture> OnLocalStreamStarted;

        /// <summary>Human-readable progress messages for the UI (e.g. "Registering headset...").</summary>
        public Action<string> OnStatusUpdate;
        /// <summary>The most recent failure detail (HTTP code, URL, message). Read this when a flow reports failure.</summary>
        public string LastError { get; private set; }

        private void Status(string msg)
        {
            if (verboseSocketLogging) Debug.Log("[Signaling] " + msg);
            OnStatusUpdate?.Invoke(msg);
        }

        private static readonly RTCConfiguration IceConfig = new RTCConfiguration
        {
            iceServers = new RTCIceServer[]
            {
                new RTCIceServer { urls = new string[] { "stun:stun.l.google.com:19302" } },
                new RTCIceServer { urls = new string[] { "stun:stun1.l.google.com:19302" } },
            }
        };

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
                tevrHeadsetId = PlayerPrefs.GetString("TEVR_HEADSET_ID", "");
                tevrLocationId = PlayerPrefs.GetString("TEVR_LOCATION_ID", "");
                if (string.IsNullOrEmpty(authToken))
                    authToken = PlayerPrefs.GetString("TEVR_AUTH_TOKEN", "");

                // Restore the last-used connection info so the login fields prepopulate with what the
                // device remembers (from a prior QR setup or manual sign-in) instead of the baked
                // BackendConfig defaults. This is what lets the user skip re-scanning the setup QR.
                if (config != null)
                {
                    string savedCustomer = PlayerPrefs.GetString(PrefCustomerId, "");
                    string savedLocation = PlayerPrefs.GetString("TEVR_LOCATION_ID", "");
                    if (!string.IsNullOrEmpty(savedCustomer)) config.customerId = savedCustomer;
                    if (!string.IsNullOrEmpty(savedLocation)) config.locationId = savedLocation;

                    // Also restore the optional setup-QR values so a single scan persists across
                    // restarts and the user never has to re-scan the Sign In code.
                    // The NEW setup-code format (apiBaseUrl includes /api) takes precedence; fall back to
                    // the legacy apiHost key so previously-provisioned headsets keep working.
                    string savedApiBaseUrl = PlayerPrefs.GetString(PrefApiBaseUrl, "");
                    string savedHost = PlayerPrefs.GetString(PrefApiHost, "");
                    if (!string.IsNullOrEmpty(savedApiBaseUrl)) SetBackendUrl(savedApiBaseUrl);
                    else if (!string.IsNullOrEmpty(savedHost)) config.apiHost = savedHost;
                }
                SetupCode = PlayerPrefs.GetString(PrefSetupCode, "");
                string savedRoom = PlayerPrefs.GetString(PrefRoomCode, "");
                if (!string.IsNullOrEmpty(savedRoom)) currentRoomCode = savedRoom;
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void Start()
        {
            StartCoroutine(WebRTC.Update());
        }

        #region Phase 1 — Provisioning & Boot

        // PlayerPrefs key for the remembered customer id (headset & location reuse their existing keys).
        private const string PrefCustomerId = "TEVR_CUSTOMER_ID";
        // Optional setup-QR values persisted so a single Sign In scan survives app restarts.
        private const string PrefApiHost = "TEVR_API_HOST";
        private const string PrefRoomCode = "TEVR_ROOM_CODE";

        /// <summary>Persists the backend API host (from the setup QR's apiBaseUrl) and applies it to config.</summary>
        public void SaveApiHost(string host)
        {
            if (string.IsNullOrWhiteSpace(host)) return;
            host = host.Trim();
            PlayerPrefs.SetString(PrefApiHost, host);
            if (config != null) config.apiHost = host;
            PlayerPrefs.Save();
        }

        /// <summary>Persists the room code (from the setup QR) so it pre-fills the join field on every launch.</summary>
        public void SaveRoomCode(string roomCode)
        {
            currentRoomCode = roomCode == null ? "" : roomCode.Trim();
            if (!string.IsNullOrEmpty(currentRoomCode)) PlayerPrefs.SetString(PrefRoomCode, currentRoomCode);
            PlayerPrefs.Save();
        }

        // ---- New setup-code provisioning ({"setupCode","apiBaseUrl"} QR) ----

        // PlayerPrefs keys for the new setup-QR format. Loaded on launch so a single scan persists.
        private const string PrefApiBaseUrl = "tevr_apiBaseUrl";
        private const string PrefSetupCode = "tevr_setupCode";

        /// <summary>The setup code from the most recent setup QR (used to resolve customer/location).</summary>
        public string SetupCode { get; private set; }

        /// <summary>
        /// Runtime override of the backend base URL (from the setup QR's apiBaseUrl), e.g.
        /// "https://host/api". A trailing "/api" is split into apiPath so REST calls
        /// ({apiHost}{apiPath}/endpoint) build correctly while the websocket (socket.io lives at the
        /// host root, derived from apiHost) stays valid. Does NOT modify BackendConfig.asset on disk —
        /// only the in-memory config for this session.
        /// </summary>
        public void SetBackendUrl(string apiBaseUrl)
        {
            if (string.IsNullOrWhiteSpace(apiBaseUrl) || config == null) return;
            string full = apiBaseUrl.Trim().TrimEnd('/');
            string host = full;
            string path = string.IsNullOrEmpty(config.apiPath) ? "/api" : config.apiPath;

            if (full.EndsWith("/api", StringComparison.OrdinalIgnoreCase))
            {
                host = full.Substring(0, full.Length - 4).TrimEnd('/');
                path = "/api";
            }
            config.apiHost = host;
            config.apiPath = path;
        }

        /// <summary>Stores the setup code in memory for later REST use.</summary>
        public void SetSetupCode(string setupCode)
        {
            SetupCode = string.IsNullOrWhiteSpace(setupCode) ? "" : setupCode.Trim();
        }

        /// <summary>
        /// The effective backend base URL (apiHost + apiPath, e.g. "https://host/api"). Used to
        /// pre-populate the editable Backend URL field on the login panel.
        /// </summary>
        public string GetBackendUrl()
        {
            if (config == null) return "";
            string host = config.apiHost == null ? "" : config.apiHost.TrimEnd('/');
            string path = string.IsNullOrEmpty(config.apiPath) ? "" : config.apiPath;
            return host + path;
        }

        /// <summary>
        /// Applies AND persists a backend base URL typed by the user on the headset (default value can be
        /// overridden here). Splits a trailing "/api" into apiPath (see SetBackendUrl) and remembers it in
        /// PlayerPrefs so it pre-populates on every launch. Does NOT modify BackendConfig.asset on disk.
        /// </summary>
        public void SaveBackendUrl(string apiBaseUrl)
        {
            if (string.IsNullOrWhiteSpace(apiBaseUrl)) return;
            SetBackendUrl(apiBaseUrl);
            PlayerPrefs.SetString(PrefApiBaseUrl, apiBaseUrl.Trim());
            PlayerPrefs.Save();
        }

        /// <summary>
        /// Applies AND persists the new setup-QR provisioning (apiBaseUrl + setupCode) so it survives
        /// app restarts — the setup QR only needs to be scanned once. The full apiBaseUrl (as scanned,
        /// including /api) is stored so reload reproduces the exact backend.
        /// </summary>
        public void SaveSetupProvisioning(string apiBaseUrl, string setupCode)
        {
            if (!string.IsNullOrWhiteSpace(apiBaseUrl))
            {
                SetBackendUrl(apiBaseUrl);
                PlayerPrefs.SetString(PrefApiBaseUrl, apiBaseUrl.Trim());
            }
            if (!string.IsNullOrWhiteSpace(setupCode))
            {
                SetSetupCode(setupCode);
                PlayerPrefs.SetString(PrefSetupCode, SetupCode);
            }
            PlayerPrefs.Save();
        }

        /// <summary>
        /// Resolves a scanned setupCode against the backend (GET {apiBaseUrl}/setup/{setupCode}) to
        /// retrieve this headset's customerId + locationId, then stores them like a normal sign-in
        /// (via SaveConnectionInfo). Call SetBackendUrl/SaveSetupProvisioning first so the request
        /// targets the correct backend.
        /// </summary>
        public void ResolveSetup(string setupCode, Action<bool> onComplete)
        {
            if (string.IsNullOrWhiteSpace(setupCode))
            {
                LastError = "No setup code to resolve.";
                onComplete?.Invoke(false);
                return;
            }
            if (Application.internetReachability == NetworkReachability.NotReachable)
            {
                LastError = "No internet connection.";
                onComplete?.Invoke(false);
                return;
            }

            Status($"Resolving setup code '{setupCode}' ...");
            GetData($"setup/{setupCode.Trim()}", (res) =>
            {
                SetupResolveResponse r = null;
                try { r = JsonUtility.FromJson<SetupResolveResponse>(res); } catch { r = null; }
                if (r != null && !string.IsNullOrEmpty(r.customerId) && !string.IsNullOrEmpty(r.locationId))
                {
                    // CRITICAL: the backend returns a bearer token here that BOTH /headsets/register and
                    // /headsets/{id}/startup-data require in the Authorization header. Store it now (the
                    // recommended bare-code QR carries no token, so this resolve is the only place we get it).
                    if (!string.IsNullOrEmpty(r.token)) SetAuthToken(r.token.Trim());
                    SaveConnectionInfo(r.customerId, r.locationId);
                    if (!string.IsNullOrEmpty(r.roomCode)) SaveRoomCode(r.roomCode);
                    Status("Setup resolved.");
                    onComplete?.Invoke(true);
                }
                else
                {
                    LastError = "Setup response missing customerId/locationId.";
                    onComplete?.Invoke(false);
                }
            }, (err) =>
            {
                LastError = err;
                onComplete?.Invoke(false);
            });
        }

        [Serializable]
        public class SetupResolveResponse
        {
            public string customerId;
            public string locationId;
            public string roomCode;
            // Bearer token required by /headsets/register and /headsets/{id}/startup-data.
            public string token;
        }

        public bool HasCredentials => !string.IsNullOrEmpty(tevrHeadsetId) && !string.IsNullOrEmpty(tevrLocationId);

        /// <summary>True once StartupData has been delivered this session (real boot or demo). Lets the
        /// post-RoomAnchor flow skip a redundant second startup-data fetch when provisioning already ran.</summary>
        public bool StartupDataLoaded { get; private set; }

        /// <summary>
        /// Persists the connection info (customer + location) so it is remembered across app restarts
        /// and used to prepopulate the login fields. Call this whenever the IDs are established
        /// (QR setup scan or manual sign-in). Also updates the in-memory config immediately.
        /// </summary>
        public void SaveConnectionInfo(string customerId, string locationId)
        {
            if (!string.IsNullOrEmpty(customerId))
            {
                PlayerPrefs.SetString(PrefCustomerId, customerId);
                if (config != null) config.customerId = customerId;
            }
            if (!string.IsNullOrEmpty(locationId))
            {
                PlayerPrefs.SetString("TEVR_LOCATION_ID", locationId);
                if (config != null) config.locationId = locationId;
            }
            PlayerPrefs.Save();
        }

        public void RegisterAndBoot(string customerId, string locationId, Action<bool> onComplete)
        {
            if (Application.internetReachability == NetworkReachability.NotReachable)
            {
                Debug.LogWarning("[SignalingManager] No internet detected. Entering Demo Mode.");
                EnterDemoMode(onComplete);
                return;
            }
            StartCoroutine(ProvisioningSequence(customerId, locationId, onComplete));
        }

        /// <summary>Sets in-memory demo credentials so HasCredentials is satisfied for an offline demo
        /// session. Does NOT persist to PlayerPrefs (the demo is transient).</summary>
        public void EnterDemoCredentials()
        {
            tevrHeadsetId = "DEMO_HEADSET";
            tevrLocationId = "DEMO_LOCATION";
            StartupDataLoaded = true;
        }

        private void EnterDemoMode(Action<bool> onComplete)
        {
            tevrHeadsetId = "DEMO_HEADSET";
            tevrLocationId = "DEMO_LOCATION";
            
            StartupData demoData = new StartupData
            {
                locationId = tevrLocationId,
                locationName = "Offline Demo Location",
                version = "1.0-demo",
                qrCodes = new List<QRAnchorData>(),
                nameDictionary = new List<NameMapping>()
            };
            
            StartupDataLoaded = true;
            OnStartupDataReceived?.Invoke(demoData);
            onComplete?.Invoke(true);
        }

        private IEnumerator ProvisioningSequence(string customerId, string locationId, Action<bool> onComplete)
        {
            bool registerDone = false;
            bool registerFailed = false;
            string serial = SystemInfo.deviceUniqueIdentifier;
            RegisterHeadsetPayload regPayload = new RegisterHeadsetPayload
            {
                serialNumber = serial,
                customerId = customerId,
                firmwareVersion = Application.version,
                label = $"Quest {serial.Substring(Math.Max(0, serial.Length - 6))}"
            };

            Status($"Registering headset for customer '{customerId}' ...");

            PostData("/headsets/register", JsonUtility.ToJson(regPayload), (res) => {
                var headset = JsonUtility.FromJson<HeadsetResponse>(res);
                tevrHeadsetId = headset.id;
                tevrLocationId = locationId;
                PlayerPrefs.SetString("TEVR_HEADSET_ID", tevrHeadsetId);
                PlayerPrefs.SetString("TEVR_LOCATION_ID", tevrLocationId);
                PlayerPrefs.Save();
                Status("Headset registered. Downloading location data...");
                registerDone = true;
            }, (err) => {
LastError = err;
                Status($"Registration failed: {err}");
                Debug.LogError($"[SignalingManager] Registration failed: {err}");
                registerFailed = true;
                registerDone = true;
            });

            // Wait for the register call to resolve either way (no more infinite hang on failure).
            yield return new WaitUntil(() => registerDone);
            if (registerFailed)
            {
                onComplete?.Invoke(false);
                yield break;
            }

            yield return StartCoroutine(EveryBootSequence(onComplete));
        }

        public IEnumerator EveryBootSequence(Action<bool> onComplete)
        {
            bool startupDone = false;
            bool failed = false;

            Status("Fetching startup data from server...");
            GetData($"/headsets/{tevrHeadsetId}/startup-data?locationId={tevrLocationId}", (res) => {
                try {
                    var data = JsonUtility.FromJson<StartupData>(res);
                    StartupDataLoaded = true;
                    OnStartupDataReceived?.Invoke(data);
                    Status("Startup data applied.");
                    startupDone = true;
                } catch (Exception e) {
                    Debug.LogError($"[SignalingManager] Error parsing startup data: {e.Message}");
                    failed = true;
                    startupDone = true;
                }
            }, (err) => {
                Debug.LogError($"[SignalingManager] Startup data request failed: {err}. Falling back to Demo Mode.");
                failed = true;
                startupDone = true;
            });

            // Wait for data or error. 
            float timeout = 10f;
            while (!startupDone && timeout > 0)
            {
                timeout -= Time.deltaTime;
                yield return null;
            }
            
            if (!startupDone) 
            {
                Debug.LogWarning("[SignalingManager] Startup data request timed out.");
                failed = true;
            }

            if (failed)
            {
                if (_credentialsExpired)
                {
                    // Expired/invalid credentials: do NOT silently demo. OnCredentialsExpired has already
                    // fired so the UI can prompt a re-scan. Report failure to the caller.
                    Status("Sign-in credentials expired — please re-scan your Login Code.");
                    onComplete?.Invoke(false);
                }
                else
                {
                    Status("Entering demo mode (offline).");
                    EnterDemoMode(onComplete);
                }
            }
            else
            {
                onComplete?.Invoke(true);
            }
        }

        public void ClearCredentials()
        {
            tevrHeadsetId = "";
            tevrLocationId = "";
            PlayerPrefs.DeleteKey("TEVR_HEADSET_ID");
            PlayerPrefs.DeleteKey("TEVR_LOCATION_ID");
            PlayerPrefs.DeleteKey(PrefCustomerId);
            PlayerPrefs.Save();
        }

        #endregion

        #region Socket Communications

        public async void Login(string roomCode)
        {
            currentRoomCode = roomCode;
            if (_ws != null) await _ws.Close();

            string baseUrl = config != null ? config.apiHost : "https://live-troubleshooting-app.replit.app";
            string wsUrl = baseUrl.Replace("https://", "wss://").Replace("http://", "ws://") + "/socket.io/?EIO=4&transport=websocket";
            _ws = new WebSocket(wsUrl);

            IsSocketConnected = false;

            // NOTE: We do NOT emit any Socket.IO event on raw WebSocket open.
            // Engine.IO v4 requires waiting for the server OPEN packet ('0'), then sending
            // the Socket.IO CONNECT packet ('40'), and only emitting events after the
            // server acknowledges the namespace connection (also '40').
            _ws.OnOpen += () => {
                _reconnectCount = 0;
                if (verboseSocketLogging) Debug.Log("[Signaling] WebSocket open. Awaiting Engine.IO handshake ('0').");
            };

            _ws.OnError += (err) => {
                if (verboseSocketLogging) Debug.LogError("[Signaling] WS error: " + err);
                OnConnectionError?.Invoke(err);
            };

            _ws.OnMessage += (bytes, start, length) => {
                string msg = System.Text.Encoding.UTF8.GetString(bytes, start, length);
                HandleEngineIoPacket(msg);
            };

            _ws.OnClose += (code) => {
                IsSocketConnected = false;
                OnDisconnected?.Invoke();
                StopBatterySequence();
                if (verboseSocketLogging) Debug.LogWarning("[Signaling] WS closed: " + code);
                if (autoReconnect && _reconnectCount < maxReconnectAttempts && code != WebSocketCloseCode.Normal)
                {
                    StartCoroutine(ReconnectSequence());
                }
                else if (code != WebSocketCloseCode.Normal)
                {
                    // Abnormal drop and we will NOT auto-reconnect (off or attempts exhausted).
                    Debug.LogWarning("[Signaling] Reconnect abandoned (code=" + code + ", attempts=" + _reconnectCount + "/" + maxReconnectAttempts + ").");
                    OnReconnectFailed?.Invoke();
                }
            };

            await _ws.Connect();
        }

        private IEnumerator ReconnectSequence()
        {
            _reconnectCount++;
            OnReconnecting?.Invoke(_reconnectCount, maxReconnectAttempts);
            yield return new WaitForSeconds(reconnectDelay);
            Login(currentRoomCode);
        }

        /// <summary>Manual reconnect: resets the attempt counter and re-opens the socket for the current room.</summary>
        public void Reconnect()
        {
            _reconnectCount = 0;
            if (!string.IsNullOrEmpty(currentRoomCode)) Login(currentRoomCode);
        }

        /// <summary>
        /// Engine.IO v4 / Socket.IO v4 packet router.
        /// Engine.IO packet type = first char: '0'=OPEN, '2'=PING, '3'=PONG, '4'=MESSAGE.
        /// For MESSAGE ('4'), the next char is the Socket.IO type: '0'=CONNECT(ack), '1'=DISCONNECT, '2'=EVENT.
        /// </summary>
        private void HandleEngineIoPacket(string msg)
        {
            if (string.IsNullOrEmpty(msg)) return;
            if (verboseSocketLogging) Debug.Log("[Signaling] <= " + msg);

            switch (msg[0])
            {
                case '0': // Engine.IO OPEN -> respond with Socket.IO CONNECT to the default namespace
                    _pingStartTime = Time.time;
                    SendRaw("40");
                    break;

                case '2': // Engine.IO PING (server-initiated in v4) -> must reply PONG
                    SendRaw("3");
                    currentLatency = (Time.time - _pingStartTime) * 1000f;
                    _pingStartTime = Time.time;
                    break;

                case '3': // Engine.IO PONG (not expected; client does not initiate ping in v4)
                    break;

                case '4': // Engine.IO MESSAGE -> Socket.IO packet
                    if (msg.Length < 2) return;
                    switch (msg[1])
                    {
                        case '0': // Socket.IO CONNECT acknowledged
                            OnSocketConnected();
                            break;
                        case '1': // Socket.IO DISCONNECT
                            IsSocketConnected = false;
                            break;
                        case '2': // Socket.IO EVENT -> payload is everything after "42"
                            ProcessIncomingMessage(msg.Substring(2));
                            break;
                    }
                    break;
            }
        }

        /// <summary>Called once the server acknowledges the Socket.IO namespace connection ('40').</summary>
        private void OnSocketConnected()
        {
            IsSocketConnected = true;
            if (verboseSocketLogging) Debug.Log("[Signaling] Socket.IO connected. Emitting join-room for room '" + currentRoomCode + "'.");

            SendSocketEvent("join-room", new JoinRoomPayload {
                role = "headset",
                roomCode = currentRoomCode,
                locationId = tevrLocationId
            });
            OnConnected?.Invoke();
            StartBatterySequence();
            // Heartbeat is server-driven in Engine.IO v4 (server sends '2', we reply '3' in HandleEngineIoPacket).
        }

        private float _pingStartTime;

        private void SendRaw(string raw)
        {
            if (_ws == null || _ws.State != WebSocketState.Open) return;
            if (verboseSocketLogging) Debug.Log("[Signaling] => " + raw);
            _ws.SendText(raw);
        }

        private void StartBatterySequence() { StopBatterySequence(); _batteryCoroutine = StartCoroutine(HealthLoop()); }
        private void StopBatterySequence() { if (_batteryCoroutine != null) StopCoroutine(_batteryCoroutine); }

        private IEnumerator HealthLoop()
        {
            while (IsConnected) {
                float battery = SystemInfo.batteryLevel * 100f;
                bool isCalibrated = QrCodeManager.Instance != null && QrCodeManager.Instance.RoomAnchorInstance != null;
                
                // Send health update
                SendSocketEvent("health-update", new { 
                    roomCode = currentRoomCode, 
                    batteryLevel = Mathf.RoundToInt(battery),
                    calibrated = isCalibrated,
                    headsetId = tevrHeadsetId,
                    locationId = tevrLocationId,
                    timestamp = DateTime.UtcNow.ToString("O")
                });

                yield return new WaitForSeconds(60f);
            }
        }

        public void Disconnect()
        {
            _ws?.Close();
            _pc?.Close(); _pc?.Dispose(); _pc = null;
            _localStream?.Dispose(); _localStream = null;
            _audioTrack?.Dispose(); _audioTrack = null;
            _remoteDescriptionSet = false;
            _pendingRemoteCandidates.Clear();

            // Fully tear down + NULL the local capture so StartLocalPreview() can start fresh next time
            // (previously these were disposed but left non-null, which permanently blocked re-preview).
            TearDownLocalVideo();
        }

        private void ProcessIncomingMessage(string json)
        {
            json = json.Trim();
            if (!json.StartsWith("[")) return;

            int nameStart = json.IndexOf('\"') + 1;
            int nameEnd = json.IndexOf('\"', nameStart);
            string eventName = json.Substring(nameStart, nameEnd - nameStart);

            int payloadStart = json.IndexOf(',', nameEnd) + 1;
            string payload = payloadStart > 0 ? json.Substring(payloadStart, json.Length - payloadStart - 1).Trim() : "{}";

            switch (eventName)
            {
                case "peer-joined":
                    var peer = JsonUtility.FromJson<PeerJoinedPayload>(payload);
                    _remoteSocketId = peer.socketId;
                    break;
                case "offer":
                    var offer = JsonUtility.FromJson<OfferPayload>(payload);
                    _remoteSocketId = offer.fromSocketId;
                    StartCoroutine(HandleRemoteOffer(offer.offer));
                    break;
                case "chat-message":
                    var chat = JsonUtility.FromJson<ChatPayload>(payload);
                    OnChatMessageReceived?.Invoke(chat.message);
                    break;
                case "point-to":
                    var pt = JsonUtility.FromJson<PointToPayload>(payload);
                    Vector3? pos = null; Quaternion? rot = null;
                    if (pt.pose != null && pt.pose.position != Vector3.zero) { pos = pt.pose.position; rot = pt.pose.rotation; }
                    OnPointToReceived?.Invoke(pt.name, pt.qrCode, pos, rot); 
                    break;
                case "ice-candidate":
                    // The expert (offerer) trickles ICE candidates which the backend relays to us.
                    // Without applying these, the media path can never complete (we only ever sent OUR
                    // candidates before). Parse defensively: accept both the nested { candidate: {...} }
                    // shape (mirrors our outgoing) and a flat { candidate, sdpMid, sdpMLineIndex } shape.
                    var iceData = ParseIncomingCandidate(payload);
                    if (iceData != null && !string.IsNullOrEmpty(iceData.candidate))
                        EnqueueOrAddRemoteCandidate(iceData);
                    break;
            }
        }

        /// <summary>Parses an incoming ice-candidate payload, tolerating both nested and flat shapes.</summary>
        private IceCandidateData ParseIncomingCandidate(string payload)
        {
            if (string.IsNullOrEmpty(payload)) return null;
            try
            {
                var wrapped = JsonUtility.FromJson<IceCandidatePayload>(payload);
                if (wrapped != null && wrapped.candidate != null && !string.IsNullOrEmpty(wrapped.candidate.candidate))
                    return wrapped.candidate;
            }
            catch { /* fall through to flat shape */ }
            try
            {
                var flat = JsonUtility.FromJson<IceCandidateData>(payload);
                if (flat != null && !string.IsNullOrEmpty(flat.candidate))
                    return flat;
            }
            catch { /* unparseable */ }
            return null;
        }

        #endregion

        #region Outgoing Communications

        public void SendChatMessage(string message)
        {
            SendSocketEvent("chat-message", new ChatPayload { 
                roomCode = currentRoomCode, message = message, senderRole = "headset" 
            });
        }

        #endregion

        #region REST Implementation

        public void PostData(string endpoint, string jsonData, Action<string> onSuccess = null, Action<string> onError = null)
        {
            StartCoroutine(SendRequest(endpoint, "POST", jsonData, onSuccess, onError));
        }

        public void GetData(string endpoint, Action<string> onSuccess = null, Action<string> onError = null)
        {
            StartCoroutine(SendRequest(endpoint, "GET", null, onSuccess, onError));
        }

        private IEnumerator SendRequest(string endpoint, string method, string json, Action<string> onSuccess, Action<string> onError)
        {
            string baseUrl = config != null ? config.apiHost : "https://live-troubleshooting-app.replit.app";
            string apiP = config != null ? config.apiPath : "/api";
            string url = $"{baseUrl}{apiP}/{endpoint.TrimStart('/')}";
            
            int attempts = 0;
            while (attempts < 3) {
                attempts++;
                using (UnityWebRequest request = new UnityWebRequest(url, method)) {
                    if (json != null) {
                        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
                        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                    }
                    request.downloadHandler = new DownloadHandlerBuffer();
                    // CRITICAL: without a timeout a stalled server (open socket, no response) hangs the
                    // entire sign-in handshake forever. 15s per attempt lets the retry/fallback logic run.
                    request.timeout = 15;
                    request.SetRequestHeader("Content-Type", "application/json");
                    // Backend enforces an AJAX/CSRF guard and rejects requests without this header (HTTP 403).
                    request.SetRequestHeader("X-Requested-With", "XMLHttpRequest");
                    // If a setup QR (or prior session) provided an access token, authenticate with it.
                    if (!string.IsNullOrEmpty(authToken))
                        request.SetRequestHeader("Authorization", "Bearer " + authToken);

                    yield return request.SendWebRequest();

                    if (request.result == UnityWebRequest.Result.Success) {
                        onSuccess?.Invoke(request.downloadHandler.text);
                        yield break;
                    } else {
                        // Build a detailed, human-readable diagnostic.
                        string body = request.downloadHandler != null ? request.downloadHandler.text : "";
                        if (!string.IsNullOrEmpty(body) && body.Length > 140) body = body.Substring(0, 140) + "…";
                        string detail = $"{request.result} (HTTP {request.responseCode}) {method} {url}" +
                                        (string.IsNullOrEmpty(request.error) ? "" : $" — {request.error}") +
                                        (string.IsNullOrEmpty(body) ? "" : $" | {body}");

                        // Credential expiry: a 401 anywhere, or a 403/404 on startup-data, means the stored
                        // token/headset is no longer valid. Clear creds and signal the UI to prompt a re-scan
                        // (do NOT silently fall to Demo Mode — that hides the real problem from the operator).
                        bool authFailure = request.responseCode == 401 ||
                            (endpoint.Contains("startup-data") && (request.responseCode == 404 || request.responseCode == 403));
                        if (authFailure) {
                            ClearCredentials();
                            _credentialsExpired = true;
                            LastError = $"HTTP {request.responseCode}: credentials invalid/expired.";
                            OnCredentialsExpired?.Invoke();
                            onError?.Invoke($"HTTP {request.responseCode}: credentials invalid/expired. {url}");
                            yield break;
                        }
                        if (attempts < 3) {
                            Debug.LogWarning($"[SignalingManager] Request attempt {attempts}/3 failed: {detail}");
                            yield return new WaitForSeconds(2f);
                        }
                        else onError?.Invoke(detail);
                    }
                }
            }
        }

        private void SendSocketEvent(string eventName, object payload)
        {
#if UNITY_EDITOR
            // Observe emit INTENT before the open-socket check so contract smoke tests can verify outbound
            // events (e.g. an "answer" produced from an "offer") without a live connection.
            Debug_OnSocketEmit?.Invoke(eventName, JsonUtility.ToJson(payload));
#endif
            if (_ws == null || _ws.State != WebSocketState.Open) return;
            string json = $"42[\"{eventName}\",{JsonUtility.ToJson(payload)}]";
            if (verboseSocketLogging) Debug.Log("[Signaling] => " + json);
            _ws.SendText(json);
        }

#if UNITY_EDITOR
        /// <summary>EDITOR-ONLY: fired for every outbound Socket.IO event BEFORE the open-socket check, so a
        /// smoke test can observe emit intent (eventName, payloadJson) without a live connection.</summary>
        public Action<string, string> Debug_OnSocketEmit;
        /// <summary>EDITOR-ONLY: the last remote socket id captured from peer-joined / offer.</summary>
        public string Debug_RemoteSocketId => _remoteSocketId;
        /// <summary>EDITOR-ONLY: feed a raw Socket.IO event payload (e.g. ["chat-message",{...}]) through the
        /// same parser the live socket uses, for contract/regression testing without a server.</summary>
        public void Debug_FeedSocketEvent(string socketIoEventJson) => ProcessIncomingMessage(socketIoEventJson);
#endif

        #endregion

        #region WebRTC Handshake

        public void StartLocalPreview() { if (_videoTrack != null) return; StartCoroutine(SetupLocalMedia()); }

        private IEnumerator SetupLocalMedia()
        {
            if (_videoTrack != null) yield break;

            // Preferred: composite of real-world passthrough + virtual content (what the user sees).
            if (!useWebcam && videoSource == VideoSource.Composite)
            {
                bool composed = false;
                yield return StartCoroutine(SetupCompositeCamera(success => composed = success));
                if (composed) yield break;
                Debug.LogWarning("[SignalingManager] Composite capture unavailable — falling back.");
            }

            bool wantPassthrough = useWebcam || videoSource == VideoSource.PassthroughCamera;

            if (wantPassthrough)
            {
                bool ok = false;
                yield return StartCoroutine(SetupPassthroughCamera(success => ok = success));
                if (ok) yield break; // passthrough track created; done.
                Debug.LogWarning("[SignalingManager] Passthrough camera unavailable — falling back to rendered camera stream.");
            }

            yield return StartCoroutine(SetupRenderedCamera());
        }

        /// <summary>
        /// Streams the real-world view using Meta's Passthrough Camera Access, which exposes the
        /// headset cameras as standard Android camera devices through Unity's WebCamTexture.
        /// Requires the CAMERA + HEADSET_CAMERA permissions and "Passthrough Camera Access" enabled
        /// in the Oculus project config (both are configured in this project).
        /// </summary>
        private IEnumerator SetupPassthroughCamera(Action<bool> onComplete, bool createTrack = true)
        {
            float deadline = Time.time + Mathf.Max(2f, passthroughStartTimeout);

            // 1. Wait for the runtime camera permissions. Meta Passthrough Camera Access (PCA) needs BOTH
            //    android.permission.CAMERA AND horizonos.permission.HEADSET_CAMERA granted at runtime;
            //    without HEADSET_CAMERA the WebCamTexture delivers no frames (black composite background).
#if UNITY_ANDROID && !UNITY_EDITOR
            const string camPerm = "android.permission.CAMERA";
            const string headsetCamPerm = "horizonos.permission.HEADSET_CAMERA";
            while ((!UnityEngine.Android.Permission.HasUserAuthorizedPermission(camPerm)
                    || !UnityEngine.Android.Permission.HasUserAuthorizedPermission(headsetCamPerm))
                   && Time.time < deadline)
            {
                yield return null;
            }
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(camPerm))
            {
                Status("Camera permission not granted — cannot stream passthrough.");
                onComplete?.Invoke(false);
                yield break;
            }
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(headsetCamPerm))
            {
                Status("HEADSET_CAMERA permission not granted — passthrough camera frames unavailable.");
                onComplete?.Invoke(false);
                yield break;
            }
#endif

            // 2. Wait for a camera device to be enumerated.
            while (WebCamTexture.devices.Length == 0 && Time.time < deadline) yield return null;
            var devices = WebCamTexture.devices;
            if (devices.Length == 0)
            {
                Status("No camera devices enumerated — Passthrough Camera Access (PCA) is not exposing the headset cameras.");
                onComplete?.Invoke(false);
                yield break;
            }

            // Log every enumerated device so the root cause is visible in on-device logcat.
            for (int i = 0; i < devices.Length; i++)
                Debug.Log($"[Signaling] WebCamTexture device[{i}]: '{devices[i].name}' frontFacing={devices[i].isFrontFacing}");

            // 3. Choose the device: explicit name match if provided; otherwise prefer a world-facing
            //    (non-front-facing) device, falling back to the first available.
            string chosen = devices[0].name;
            if (!string.IsNullOrEmpty(webcamDeviceName))
            {
                foreach (var d in devices)
                    if (d.name.IndexOf(webcamDeviceName, StringComparison.OrdinalIgnoreCase) >= 0) { chosen = d.name; break; }
            }
            else
            {
                foreach (var d in devices)
                    if (!d.isFrontFacing) { chosen = d.name; break; }
            }
            Debug.Log($"[Signaling] Selected passthrough device: '{chosen}' (requesting {captureResolution.x}x{captureResolution.y}@30).");

            // 4. Start the WebCamTexture.
            _webcamTexture = new WebCamTexture(chosen, captureResolution.x, captureResolution.y, 30);
            _webcamTexture.Play();

            // 5. Wait until it actually produces frames (width/height stay tiny until the stream is live).
            while ((!_webcamTexture.isPlaying || _webcamTexture.width <= 16) && Time.time < deadline) yield return null;
            if (!_webcamTexture.isPlaying || _webcamTexture.width <= 16)
            {
                Status($"Passthrough camera '{chosen}' produced no frames within {passthroughStartTimeout:0}s " +
                       $"(isPlaying={_webcamTexture.isPlaying}, {_webcamTexture.width}x{_webcamTexture.height}). " +
                       "Most likely HEADSET_CAMERA was not granted, or the device is busy.");
                _webcamTexture.Stop();
                _webcamTexture = null;
                onComplete?.Invoke(false);
                yield break;
            }
            Debug.Log($"[Signaling] Passthrough camera frames live: '{chosen}' {_webcamTexture.width}x{_webcamTexture.height} " +
                      $"(rotation={_webcamTexture.videoRotationAngle}, mirrored={_webcamTexture.videoVerticallyMirrored}).");

            if (createTrack)
            {
                _videoTrack = new VideoStreamTrack(_webcamTexture);
                Status($"Streaming passthrough camera: {chosen} ({_webcamTexture.width}x{_webcamTexture.height}).");
                OnLocalStreamStarted?.Invoke(_webcamTexture);
            }
            else
            {
                Status($"Passthrough camera live: {chosen} ({_webcamTexture.width}x{_webcamTexture.height}).");
            }
            onComplete?.Invoke(true);
        }

        /// <summary>
        /// Builds the COMPOSITE local capture = real-world passthrough (background) + the Unity-rendered
        /// virtual content (UI panels, QR markers, highlights) on top — i.e. exactly what the user sees.
        /// A dedicated capture camera mirrors the user's eye every frame and renders a full-screen quad
        /// (showing the live passthrough WebCamTexture, on the MRCapture layer) behind all world content
        /// into a RenderTexture. The MRCapture layer is removed from the eye's culling mask so the quad
        /// is only visible in the stream, never doubled-up in the headset view.
        /// </summary>
        private IEnumerator SetupCompositeCamera(Action<bool> onComplete)
        {
            // 1. Start the passthrough webcam as the real-world background (no track of its own).
            bool camOk = false;
            yield return StartCoroutine(SetupPassthroughCamera(ok => camOk = ok, createTrack: false));

            // 2. Resolve the user's eye camera.
            float timeout = Time.time + 10f;
            while (captureCamera == null && Camera.main == null && Time.time < timeout) yield return null;
            Camera eye = captureCamera != null ? captureCamera : Camera.main;
            if (eye == null)
            {
                Status("No eye camera available for composite capture.");
                onComplete?.Invoke(false);
                yield break;
            }
            _compositeEye = eye;

            int mrLayer = LayerMask.NameToLayer(CompositeLayerName);
            if (mrLayer < 0) mrLayer = 31; // last-resort fallback

            // 3. Composite camera that mirrors the eye.
            if (_compositeCamera == null)
            {
                var camObj = new GameObject("TEVR_CompositeCamera");
                _compositeCamera = camObj.AddComponent<Camera>();
            }
            _compositeCamera.CopyFrom(eye);
            _compositeCamera.stereoTargetEye = StereoTargetEyeMask.None;
            _compositeCamera.clearFlags = CameraClearFlags.SolidColor;
            _compositeCamera.backgroundColor = Color.black;
            _compositeCamera.cullingMask = eye.cullingMask | (1 << mrLayer);
            _compositeCamera.depth = eye.depth - 1;
            _compositeCamera.transform.SetParent(eye.transform, false);
            _compositeCamera.transform.localPosition = Vector3.zero;
            _compositeCamera.transform.localRotation = Quaternion.identity;

            // CopyFrom(eye) inherits the EYE's FOV/aspect (and possibly an XR projection matrix). Override
            // with the PASSTHROUGH camera's projection so the VR overlay matches the real-world background's
            // angular scale — otherwise the two diverge as the head turns (the reported misalignment).
            ApplyCompositeProjection();

            // Keep the background quad out of the user's own eyes.
            _compositeRestoreMask = eye.cullingMask;
            eye.cullingMask &= ~(1 << mrLayer);
            _compositeMaskModified = true;

            // 4. Output RenderTexture.
            if (_compositeRT == null)
            {
                _compositeRT = new RenderTexture(captureResolution.x, captureResolution.y, 24,
                    UnityEngine.Experimental.Rendering.GraphicsFormat.B8G8R8A8_SRGB);
                _compositeRT.Create();
            }
            _compositeCamera.targetTexture = _compositeRT;

            // 5. Full-screen background quad showing the live passthrough.
            if (_compositeQuad == null)
            {
                _compositeQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                _compositeQuad.name = "TEVR_PassthroughBG";
                if (_compositeQuad.TryGetComponent<Collider>(out var col)) Destroy(col);
                var sh = Shader.Find("Universal Render Pipeline/Unlit");
                if (sh == null) sh = Shader.Find("Unlit/Texture");
                _compositeBgMaterial = new Material(sh);
                _compositeQuad.GetComponent<Renderer>().material = _compositeBgMaterial;
                _compositeQuad.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            _compositeQuad.layer = mrLayer;

            if (_webcamTexture != null)
            {
                if (_compositeBgMaterial.HasProperty("_BaseMap")) _compositeBgMaterial.SetTexture("_BaseMap", _webcamTexture);
                _compositeBgMaterial.mainTexture = _webcamTexture;
            }

            // Parent the quad to the composite camera; sizing/orientation handled by UpdateCompositeQuadTransform().
            _compositeQuad.transform.SetParent(_compositeCamera.transform, false);
            UpdateCompositeQuadTransform();
            _compositeQuad.SetActive(IsWebcamLive());

            // 6. Stream the composite.
            _videoTrack = new VideoStreamTrack(_compositeRT);
            OnLocalStreamStarted?.Invoke(_compositeRT);
            Status(camOk
                ? "Streaming composite view (passthrough + UI)."
                : "Streaming composite view (UI only — passthrough not live yet; will recover automatically if it starts).");

            // Keep the passthrough background fresh: bind frames, correct orientation changes, and recover
            // the camera if it starts late (e.g. HEADSET_CAMERA granted after the initial timeout).
            if (_compositeMaintainCo != null) StopCoroutine(_compositeMaintainCo);
            _compositeMaintainCo = StartCoroutine(MaintainCompositeBackground());

            onComplete?.Invoke(true);
        }

        private bool IsWebcamLive() => _webcamTexture != null && _webcamTexture.isPlaying && _webcamTexture.width > 16;

        /// <summary>
        /// Sets the composite camera's projection to the PASSTHROUGH camera's field of view (converted to the
        /// render-texture aspect), so the rendered VR overlay shares the same angular scale as the real-world
        /// passthrough image on the background quad. This is what makes the overlay track the room correctly
        /// as the head turns. FOV is tunable (Quest 3 default ~82° horizontal).
        /// </summary>
        private void ApplyCompositeProjection()
        {
            if (_compositeCamera == null) return;
            float aspect = (float)captureResolution.x / Mathf.Max(1, captureResolution.y);
            float hFov = Mathf.Clamp(passthroughHorizontalFovDeg, 40f, 140f);
            // Unity's Camera.fieldOfView is VERTICAL; convert from horizontal at our RT aspect.
            float vFov = 2f * Mathf.Atan(Mathf.Tan(hFov * 0.5f * Mathf.Deg2Rad) / Mathf.Max(0.1f, aspect)) * Mathf.Rad2Deg;
            _compositeCamera.usePhysicalProperties = false;
            _compositeCamera.ResetProjectionMatrix(); // discard any XR projection inherited via CopyFrom
            _compositeCamera.aspect = aspect;
            _compositeCamera.fieldOfView = vFov;
        }

        /// <summary>Sizes/orients the passthrough background quad to exactly fill the COMPOSITE camera's
        /// frustum (which now uses the passthrough FOV), compensating for the WebCamTexture's rotation and
        /// vertical mirroring (both can change after start).</summary>
        private void UpdateCompositeQuadTransform()
        {
            if (_compositeQuad == null || _compositeCamera == null) return;
            Camera cam = _compositeCamera;
            float dist = Mathf.Clamp(cam.farClipPlane * 0.5f, 5f, 100f);
            float h = 2f * dist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float w = h * Mathf.Max(0.1f, cam.aspect);
            _compositeQuad.transform.localPosition = new Vector3(0f, 0f, dist);
            float rot = _webcamTexture != null ? -_webcamTexture.videoRotationAngle : 0f;
            _compositeQuad.transform.localRotation = Quaternion.Euler(0f, 0f, rot);
            float flipY = (_webcamTexture != null && _webcamTexture.videoVerticallyMirrored) ? -1f : 1f;
            _compositeQuad.transform.localScale = new Vector3(w, h * flipY, 1f);
        }

        /// <summary>While the composite is active: binds live passthrough frames to the background quad,
        /// keeps it oriented, and periodically retries acquiring the passthrough camera if it isn't live
        /// (so a late permission grant or freed camera recovers without restarting the session).</summary>
        private IEnumerator MaintainCompositeBackground()
        {
            float nextReacquire = 0f;
            while (_compositeQuad != null && _compositeRT != null)
            {
                if (IsWebcamLive())
                {
                    if (_compositeBgMaterial != null)
                    {
                        if (_compositeBgMaterial.HasProperty("_BaseMap")) _compositeBgMaterial.SetTexture("_BaseMap", _webcamTexture);
                        if (_compositeBgMaterial.mainTexture != _webcamTexture) _compositeBgMaterial.mainTexture = _webcamTexture;
                    }
                    UpdateCompositeQuadTransform();
                    if (!_compositeQuad.activeSelf) { _compositeQuad.SetActive(true); Status("Passthrough background now live in the stream."); }
                }
                else
                {
                    if (_compositeQuad.activeSelf) _compositeQuad.SetActive(false);
                    if (_webcamTexture == null && Time.time >= nextReacquire)
                    {
                        nextReacquire = Time.time + 5f;
                        yield return StartCoroutine(SetupPassthroughCamera(_ => { }, createTrack: false));
                    }
                }
                yield return null;
            }
        }

        /// <summary>Stops the local preview/capture and releases all camera/texture resources so it can restart cleanly.</summary>
        public void StopLocalPreview()
        {
            TearDownLocalVideo();
        }

        private void TearDownLocalVideo()
        {
            if (_compositeMaintainCo != null) { StopCoroutine(_compositeMaintainCo); _compositeMaintainCo = null; }

            _videoTrack?.Dispose();
            _videoTrack = null;

            if (_webcamTexture != null) { _webcamTexture.Stop(); _webcamTexture = null; }

            if (_compositeMaskModified && _compositeEye != null)
            {
                _compositeEye.cullingMask = _compositeRestoreMask;
                _compositeMaskModified = false;
            }
            _compositeEye = null;
            if (_compositeQuad != null) { Destroy(_compositeQuad); _compositeQuad = null; }
            if (_compositeCamera != null) { Destroy(_compositeCamera.gameObject); _compositeCamera = null; }
            if (_compositeRT != null) { _compositeRT.Release(); _compositeRT = null; }

            if (_internalCaptureCamera != null) { Destroy(_internalCaptureCamera.gameObject); _internalCaptureCamera = null; }
            if (_captureRT != null) { _captureRT.Release(); _captureRT = null; }
        }

        /// <summary>
        /// Fallback path: streams the Unity-rendered eye view into a RenderTexture. Note that on a
        /// passthrough MR app this contains only the virtual content, not the real world.
        /// </summary>
        private IEnumerator SetupRenderedCamera()
        {
            float timeout = 10f;
            while (captureCamera == null && Camera.main == null && timeout > 0) { timeout -= Time.deltaTime; yield return null; }
            if (captureCamera == null) captureCamera = Camera.main;

            if (captureCamera != null) {
                if (_internalCaptureCamera == null) {
                    GameObject camObj = new GameObject("WebApp_CaptureCamera");
                    _internalCaptureCamera = camObj.AddComponent<Camera>();
                    _internalCaptureCamera.CopyFrom(captureCamera);
                    camObj.transform.SetParent(captureCamera.transform, false);
                    camObj.transform.localPosition = Vector3.zero;
                    camObj.transform.localRotation = Quaternion.identity;
                    _internalCaptureCamera.clearFlags = CameraClearFlags.SolidColor;
                    _internalCaptureCamera.backgroundColor = new Color(0, 0, 0, 0);
                    _captureRT = new RenderTexture(captureResolution.x, captureResolution.y, 16, UnityEngine.Experimental.Rendering.GraphicsFormat.B8G8R8A8_SRGB);
                    _captureRT.Create();
                    _internalCaptureCamera.targetTexture = _captureRT;
                }
                _videoTrack = new VideoStreamTrack(_captureRT);
                OnLocalStreamStarted?.Invoke(_captureRT);
            }
        }

        // Remote ICE candidates can arrive (trickle) before the peer connection exists or before the
        // remote description is set. AddIceCandidate requires a remote description first, so we queue
        // early candidates and flush them once the remote description is applied.
        private readonly List<IceCandidateData> _pendingRemoteCandidates = new List<IceCandidateData>();
        private bool _remoteDescriptionSet;

        private void EnqueueOrAddRemoteCandidate(IceCandidateData data)
        {
            if (_pc != null && _remoteDescriptionSet) AddRemoteCandidate(data);
            else _pendingRemoteCandidates.Add(data);
        }

        private void AddRemoteCandidate(IceCandidateData data)
        {
            if (_pc == null || data == null || string.IsNullOrEmpty(data.candidate)) return;
            try
            {
                var init = new RTCIceCandidateInit
                {
                    candidate = data.candidate,
                    sdpMid = data.sdpMid,
                    sdpMLineIndex = data.sdpMLineIndex
                };
                _pc.AddIceCandidate(new RTCIceCandidate(init));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Signaling] Failed to add remote ICE candidate: " + e.Message);
            }
        }

        private void FlushPendingRemoteCandidates()
        {
            if (_pc == null) return;
            for (int i = 0; i < _pendingRemoteCandidates.Count; i++) AddRemoteCandidate(_pendingRemoteCandidates[i]);
            _pendingRemoteCandidates.Clear();
        }

        private IEnumerator HandleRemoteOffer(RTCSessionDescription offer)
        {
            var configIce = IceConfig;
            _remoteDescriptionSet = false;
            _pc = new RTCPeerConnection(ref configIce);
            _pc.OnTrack = (RTCTrackEvent ev) => { if (ev.Track is VideoStreamTrack videoTrack) videoTrack.OnVideoReceived += (Texture tex) => OnRemoteStreamStarted?.Invoke(tex); };
            _pc.OnIceCandidate = (candidate) => {
                SendSocketEvent("ice-candidate", new IceCandidatePayload { 
                    roomCode = currentRoomCode,
                    candidate = new IceCandidateData { candidate = candidate.Candidate, sdpMid = candidate.SdpMid, sdpMLineIndex = candidate.SdpMLineIndex ?? 0 },
                    targetSocketId = _remoteSocketId
                });
            };

            // Prepare local media BEFORE negotiation so the tracks are ready to attach.
            if (_videoTrack == null) yield return StartCoroutine(SetupLocalMedia());
            _audioTrack = new AudioStreamTrack();
            _localStream = new MediaStream();
            if (_videoTrack != null) _localStream.AddTrack(_videoTrack);
            _localStream.AddTrack(_audioTrack);

            // CORRECT ANSWERER ORDER (this is why the web app saw no video):
            // 1) SetRemoteDescription(offer) FIRST — this creates the transceivers described by the offer
            //    (the web app offers to RECEIVE our video, i.e. a recvonly m-line on its side).
            // 2) AddTrack AFTER — our outgoing track reuses that negotiated transceiver and flips it to
            //    sending, instead of creating an extra, unnegotiated m-line that the answer wouldn't carry.
            // 3) CreateAnswer LAST — the answer now advertises our video/audio as sendable.
            yield return _pc.SetRemoteDescription(ref offer);
            _remoteDescriptionSet = true;
            FlushPendingRemoteCandidates();

            foreach (var track in _localStream.GetTracks()) _pc.AddTrack(track, _localStream);

            var createAnswerOp = _pc.CreateAnswer();
            yield return createAnswerOp;
            if (createAnswerOp.IsError)
            {
                Debug.LogError("[Signaling] CreateAnswer failed: " + createAnswerOp.Error.message);
                yield break;
            }
            var answerDesc = createAnswerOp.Desc;
            yield return _pc.SetLocalDescription(ref answerDesc);

            Debug.Log("[Signaling] Sending answer (video track " + (_videoTrack != null ? "present" : "MISSING") + ") to " + _remoteSocketId);
            SendSocketEvent("answer", new AnswerPayload { roomCode = currentRoomCode, answer = answerDesc, targetSocketId = _remoteSocketId });
        }

        #endregion

        #region Helpers & Data Structures

        private void OnDestroy() { Disconnect(); }

        [Serializable] public class RegisterHeadsetPayload { public string serialNumber; public string customerId; public string firmwareVersion; public string label; }
        [Serializable] public class HeadsetResponse { public string id; public string serialNumber; public string label; public string customerId; public string customerName; }
        [Serializable] public class StartupData 
        { 
            public string locationId; 
            public string locationName; 
            public string version; // Added versioning for context isolation
            public List<QRAnchorData> qrCodes; 
            public List<NameMapping> nameDictionary; 
        }
        [Serializable] public class QRAnchorData 
        { 
            public string qrValue; 
            public string name; 
            public Vector3 position; 
            public Quaternion rotation; 
            public string metadata; // Flexible field for future expansion
        }
[Serializable] public class PoseData { public Vector3 position; public Quaternion rotation; }
        [Serializable] public class NameMapping { public string qrValue; public string name; }
        [Serializable] public class JoinRoomPayload { public string role; public string roomCode; public string locationId; }
        [Serializable] public class ChatPayload { public string roomCode; public string message; public string senderRole; }
        [Serializable] public class OfferPayload { public RTCSessionDescription offer; public string fromSocketId; public string targetSocketId; }
        [Serializable] public class AnswerPayload { public string roomCode; public RTCSessionDescription answer; public string targetSocketId; }
        [Serializable] public class IceCandidatePayload { public string roomCode; public IceCandidateData candidate; public string targetSocketId; }
        [Serializable] public class IceCandidateData { public string candidate; public string sdpMid; public int sdpMLineIndex; }
        [Serializable] public class PeerJoinedPayload { public string role; public string socketId; }
        [Serializable] public class PointToPayload { public string name; public string qrCode; public PoseData pose; }
        #endregion
    }
}
