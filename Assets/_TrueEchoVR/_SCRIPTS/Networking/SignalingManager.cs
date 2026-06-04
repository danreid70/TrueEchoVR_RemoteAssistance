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

        private WebSocket _ws;
        private RTCPeerConnection _pc;
        private MediaStream _localStream;
        private VideoStreamTrack _videoTrack;
        private AudioStreamTrack _audioTrack;
        private string _remoteSocketId;

        public enum VideoSource
        {
            /// <summary>Capture the headset passthrough camera (real world) via WebCamTexture. Requires CAMERA permission.</summary>
            PassthroughCamera,
            /// <summary>Capture the Unity rendered eye view (virtual content only). No real-world imagery.</summary>
            RenderedCamera
        }

        [Header("Video Settings")]
        [Tooltip("PassthroughCamera streams the real world (requires Camera permission + passthrough camera access). " +
                 "RenderedCamera streams only Unity-rendered content. If passthrough is unavailable at runtime, " +
                 "the system automatically falls back to the rendered camera.")]
        public VideoSource videoSource = VideoSource.PassthroughCamera;

        [Tooltip("Legacy flag kept for compatibility. When true, forces the PassthroughCamera (WebCamTexture) source.")]
        public bool useWebcam = false;

        public Camera captureCamera;
        public Vector2Int captureResolution = new Vector2Int(1280, 720);

        [Tooltip("Optional: substring of the desired WebCamTexture device name. Leave empty to auto-pick the passthrough camera.")]
        public string webcamDeviceName = "";

        [Tooltip("How long (seconds) to wait for Camera permission + the passthrough camera to start before falling back to the rendered view.")]
        public float passthroughStartTimeout = 8f;

        private WebCamTexture _webcamTexture;
        private RenderTexture _captureRT;
        private Camera _internalCaptureCamera;

        public Action OnConnected;
        public Action OnDisconnected;
        public Action<string> OnConnectionError;
        public Action<string> OnChatMessageReceived;
        public Action<string, Vector3?, Quaternion?> OnPointToReceived;
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

                // Restore the last-used connection info so the login fields prepopulate with what the
                // device remembers (from a prior QR setup or manual sign-in) instead of the baked
                // BackendConfig defaults. This is what lets the user skip re-scanning the setup QR.
                if (config != null)
                {
                    string savedCustomer = PlayerPrefs.GetString(PrefCustomerId, "");
                    string savedLocation = PlayerPrefs.GetString("TEVR_LOCATION_ID", "");
                    if (!string.IsNullOrEmpty(savedCustomer)) config.customerId = savedCustomer;
                    if (!string.IsNullOrEmpty(savedLocation)) config.locationId = savedLocation;
                }
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

        public bool HasCredentials => !string.IsNullOrEmpty(tevrHeadsetId) && !string.IsNullOrEmpty(tevrLocationId);

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
                Status($"Registered (headset id: {tevrHeadsetId}). Loading startup data ...");
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

            GetData($"/headsets/{tevrHeadsetId}/startup-data?locationId={tevrLocationId}", (res) => {
                var data = JsonUtility.FromJson<StartupData>(res);
                OnStartupDataReceived?.Invoke(data);
                startupDone = true;
            }, (err) => {
                Debug.LogError($"[SignalingManager] Startup data failed: {err}. Falling back to Demo Mode.");
                failed = true;
                startupDone = true;
            });

            yield return new WaitUntil(() => startupDone);
            
            if (failed)
            {
                EnterDemoMode(onComplete);
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
                    StartCoroutine(ReconnectSequence());
            };

            await _ws.Connect();
        }

        private IEnumerator ReconnectSequence()
        {
            _reconnectCount++;
            yield return new WaitForSeconds(reconnectDelay);
            Login(currentRoomCode);
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
            _pc?.Close(); _pc?.Dispose();
            _localStream?.Dispose();
            _videoTrack?.Dispose();
            _audioTrack?.Dispose();
            if (_webcamTexture != null) _webcamTexture.Stop();
            if (_internalCaptureCamera != null) Destroy(_internalCaptureCamera.gameObject);
            if (_captureRT != null) _captureRT.Release();
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
                    OnPointToReceived?.Invoke(pt.name, pos, rot); 
                    break;
            }
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
                    request.SetRequestHeader("Content-Type", "application/json");

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

                        if (endpoint.Contains("startup-data") && (request.responseCode == 404 || request.responseCode == 403)) {
                            ClearCredentials();
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
            if (_ws == null || _ws.State != WebSocketState.Open) return;
            string json = $"42[\"{eventName}\",{JsonUtility.ToJson(payload)}]";
            if (verboseSocketLogging) Debug.Log("[Signaling] => " + json);
            _ws.SendText(json);
        }

        #endregion

        #region WebRTC Handshake

        public void StartLocalPreview() { if (_videoTrack != null) return; StartCoroutine(SetupLocalMedia()); }

        private IEnumerator SetupLocalMedia()
        {
            if (_videoTrack != null) yield break;

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
        private IEnumerator SetupPassthroughCamera(Action<bool> onComplete)
        {
            float deadline = Time.time + Mathf.Max(2f, passthroughStartTimeout);

            // 1. Wait for the runtime CAMERA permission.
#if UNITY_ANDROID && !UNITY_EDITOR
            while (!UnityEngine.Android.Permission.HasUserAuthorizedPermission("android.permission.CAMERA") && Time.time < deadline)
            {
                yield return null;
            }
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission("android.permission.CAMERA"))
            {
                Status("Camera permission not granted — cannot stream passthrough.");
                onComplete?.Invoke(false);
                yield break;
            }
#endif

            // 2. Wait for a camera device to be enumerated.
            while (WebCamTexture.devices.Length == 0 && Time.time < deadline) yield return null;
            var devices = WebCamTexture.devices;
            if (devices.Length == 0) { onComplete?.Invoke(false); yield break; }

            // 3. Choose the device: explicit name match if provided, otherwise the first available.
            string chosen = devices[0].name;
            if (!string.IsNullOrEmpty(webcamDeviceName))
            {
                foreach (var d in devices)
                {
                    if (d.name.IndexOf(webcamDeviceName, StringComparison.OrdinalIgnoreCase) >= 0) { chosen = d.name; break; }
                }
            }

            // 4. Start the WebCamTexture.
            _webcamTexture = new WebCamTexture(chosen, captureResolution.x, captureResolution.y, 30);
            _webcamTexture.Play();

            // 5. Wait until it actually produces frames (width stays at 16 until the stream is live).
            while (_webcamTexture.width <= 16 && Time.time < deadline) yield return null;
            if (_webcamTexture.width <= 16)
            {
                Status("Passthrough camera failed to start.");
                _webcamTexture.Stop();
                _webcamTexture = null;
                onComplete?.Invoke(false);
                yield break;
            }

            _videoTrack = new VideoStreamTrack(_webcamTexture);
            Status($"Streaming passthrough camera: {chosen} ({_webcamTexture.width}x{_webcamTexture.height}).");
            OnLocalStreamStarted?.Invoke(_webcamTexture);
            onComplete?.Invoke(true);
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

        private IEnumerator HandleRemoteOffer(RTCSessionDescription offer)
        {
            var configIce = IceConfig;
            _pc = new RTCPeerConnection(ref configIce);
            _pc.OnTrack = (RTCTrackEvent ev) => { if (ev.Track is VideoStreamTrack videoTrack) videoTrack.OnVideoReceived += (Texture tex) => OnRemoteStreamStarted?.Invoke(tex); };
            _pc.OnIceCandidate = (candidate) => {
                SendSocketEvent("ice-candidate", new IceCandidatePayload { 
                    roomCode = currentRoomCode,
                    candidate = new IceCandidateData { candidate = candidate.Candidate, sdpMid = candidate.SdpMid, sdpMLineIndex = candidate.SdpMLineIndex ?? 0 },
                    targetSocketId = _remoteSocketId
                });
            };

            if (_videoTrack == null) yield return StartCoroutine(SetupLocalMedia());
            _audioTrack = new AudioStreamTrack();
            _localStream = new MediaStream();
            if (_videoTrack != null) _localStream.AddTrack(_videoTrack);
            _localStream.AddTrack(_audioTrack);

            foreach (var track in _localStream.GetTracks()) _pc.AddTrack(track, _localStream);
            yield return _pc.SetRemoteDescription(ref offer);
            var createAnswerOp = _pc.CreateAnswer();
            yield return createAnswerOp;
            var answerDesc = createAnswerOp.Desc;
            yield return _pc.SetLocalDescription(ref answerDesc);

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
