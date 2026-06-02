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

        private int _reconnectCount = 0;
        private Coroutine _pingCoroutine;
        private Coroutine _batteryCoroutine;

        private WebSocket _ws;
        private RTCPeerConnection _pc;
        private MediaStream _localStream;
        private VideoStreamTrack _videoTrack;
        private AudioStreamTrack _audioTrack;
        private string _remoteSocketId;

        [Header("Video Settings")]
        public bool useWebcam = false;
        public Camera captureCamera;
        public Vector2Int captureResolution = new Vector2Int(1280, 720);
        public string webcamDeviceName = "";

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

        public bool HasCredentials => !string.IsNullOrEmpty(tevrHeadsetId) && !string.IsNullOrEmpty(tevrLocationId);

        public void RegisterAndBoot(string customerId, string locationId, Action<bool> onComplete)
        {
            StartCoroutine(ProvisioningSequence(customerId, locationId, onComplete));
        }

        private IEnumerator ProvisioningSequence(string customerId, string locationId, Action<bool> onComplete)
        {
            bool registerDone = false;
            string serial = SystemInfo.deviceUniqueIdentifier;
            RegisterHeadsetPayload regPayload = new RegisterHeadsetPayload
            {
                serialNumber = serial,
                customerId = customerId,
                firmwareVersion = Application.version,
                label = $"Quest {serial.Substring(Math.Max(0, serial.Length - 6))}"
            };

            PostData("/headsets/register", JsonUtility.ToJson(regPayload), (res) => {
                var headset = JsonUtility.FromJson<HeadsetResponse>(res);
                tevrHeadsetId = headset.id;
                tevrLocationId = locationId;
                PlayerPrefs.SetString("TEVR_HEADSET_ID", tevrHeadsetId);
                PlayerPrefs.SetString("TEVR_LOCATION_ID", tevrLocationId);
                PlayerPrefs.Save();
                registerDone = true;
            }, (err) => {
                Debug.LogError($"[SignalingManager] Registration failed: {err}");
                onComplete?.Invoke(false);
            });

            yield return new WaitUntil(() => registerDone);
            yield return StartCoroutine(EveryBootSequence(onComplete));
        }

        public IEnumerator EveryBootSequence(Action<bool> onComplete)
        {
            bool startupDone = false;
            GetData($"/headsets/{tevrHeadsetId}/startup-data?locationId={tevrLocationId}", (res) => {
                var data = JsonUtility.FromJson<StartupData>(res);
                OnStartupDataReceived?.Invoke(data);
                startupDone = true;
            }, (err) => {
                Debug.LogError($"[SignalingManager] Startup data failed: {err}");
                onComplete?.Invoke(false);
            });

            yield return new WaitUntil(() => startupDone);
            onComplete?.Invoke(true);
        }

        public void ClearCredentials()
        {
            tevrHeadsetId = "";
            tevrLocationId = "";
            PlayerPrefs.DeleteKey("TEVR_HEADSET_ID");
            PlayerPrefs.DeleteKey("TEVR_LOCATION_ID");
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

            _ws.OnOpen += () => {
                _reconnectCount = 0;
                SendSocketEvent("join-room", new JoinRoomPayload { 
                    role = "headset", 
                    roomCode = currentRoomCode, 
                    locationId = tevrLocationId 
                });
                OnConnected?.Invoke();
                StartPingSequence();
                StartBatterySequence();
            };

            _ws.OnError += (err) => OnConnectionError?.Invoke(err);

            _ws.OnMessage += (bytes, start, length) => {
                string msg = System.Text.Encoding.UTF8.GetString(bytes, start, length);
                if (msg.StartsWith("42")) ProcessIncomingMessage(msg.Substring(2));
                else if (msg == "3") HandlePong();
            };

            _ws.OnClose += (code) => {
                OnDisconnected?.Invoke();
                StopPingSequence();
                StopBatterySequence();
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

        private void StartPingSequence() { StopPingSequence(); _pingCoroutine = StartCoroutine(PingLoop()); }
        private void StopPingSequence() { if (_pingCoroutine != null) StopCoroutine(_pingCoroutine); }

        private float _pingStartTime;
        private IEnumerator PingLoop()
        {
            while (IsConnected) {
                _pingStartTime = Time.time;
                _ws.SendText("2"); 
                yield return new WaitForSeconds(5f);
            }
        }

        private void HandlePong() { currentLatency = (Time.time - _pingStartTime) * 1000f; }

        private void StartBatterySequence() { StopBatterySequence(); _batteryCoroutine = StartCoroutine(HealthLoop()); }
        private void StopBatterySequence() { if (_batteryCoroutine != null) StopCoroutine(_batteryCoroutine); }

        private IEnumerator HealthLoop()
        {
            float lastSentBattery = -1f;
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
                        if (endpoint.Contains("startup-data") && (request.responseCode == 404 || request.responseCode == 403)) {
                            ClearCredentials();
                            onError?.Invoke($"Error {request.responseCode}: Credentials invalidated.");
                            yield break;
                        }
                        if (attempts < 3) yield return new WaitForSeconds(2f);
                        else onError?.Invoke(request.error);
                    }
                }
            }
        }

        private void SendSocketEvent(string eventName, object payload)
        {
            if (_ws == null || _ws.State != WebSocketState.Open) return;
            string json = $"42[\"{eventName}\",{JsonUtility.ToJson(payload)}]";
            _ws.SendText(json);
        }

        #endregion

        #region WebRTC Handshake

        public void StartLocalPreview() { if (_videoTrack != null) return; StartCoroutine(SetupLocalMedia()); }

        private IEnumerator SetupLocalMedia()
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
