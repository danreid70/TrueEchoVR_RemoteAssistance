using UnityEngine;
using Unity.WebRTC;
using Meta.Net.NativeWebSocket;
using System.Collections;
using System;
using UnityEngine.Networking;

namespace TEVR
{
    /// <summary>
    /// Singleton manager for all incoming and outgoing API calls to the Replit backend.
    /// Handles Socket.io signaling, WebRTC streaming, and RESTful data exchange.
    /// </summary>
    public class SignalingManager : MonoBehaviour
    {
        public static SignalingManager Instance { get; private set; }

        [Header("Backend Configuration")]
        public string serverBaseUrl = "https://live-troubleshooting-app.replit.app";
        public string apiPath = "/api";
        
        [Header("Session Info")]
        public string currentLocationId;
        public string currentRoomCode;
        public string headsetId = "quest-3-unit-01"; // Should ideally be unique per device
        public bool IsConnected => _ws != null && _ws.State == WebSocketState.Open;
        public float currentLatency { get; private set; }

        [Header("Reconnection Settings")]
        public bool autoReconnect = true;
        public int maxReconnectAttempts = 5;
        public float reconnectDelay = 3f;

        private int _reconnectCount = 0;
        private Coroutine _pingCoroutine;

        // WebSocket & WebRTC Traffic
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

        // Events for UI and Systems
        public Action OnConnected;
        public Action OnDisconnected;
        public Action<string> OnConnectionError;
        public Action<string> OnChatMessageReceived;
        public Action<string, string, string> OnPointToReceived;
        public Action<string> OnQRCodesPulled;
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
                // WebRTC initialization is handled via Update coroutine or managed by the package
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void Start()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera))
            {
                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Camera);
            }
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
            {
                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone);
            }
#endif
            StartCoroutine(WebRTC.Update());
        }

        #region API Handlers

        /// <summary>
        /// Logs into the Replit server using Location ID and joins a specific room.
        /// </summary>
        public async void Login(string locationId, string roomCode)
        {
            if (Application.internetReachability == NetworkReachability.NotReachable)
            {
                OnConnectionError?.Invoke("No internet connection detected.");
                return;
            }

            currentLocationId = locationId;
            currentRoomCode = roomCode;

            if (_ws != null) await _ws.Close();

            // Socket.io standard connection path
            string wsUrl = serverBaseUrl.Replace("https://", "wss://").Replace("http://", "ws://") + "/socket.io/?EIO=4&transport=websocket";
            _ws = new WebSocket(wsUrl);

            _ws.OnOpen += () => {
                Debug.Log($"[SignalingManager] Socket.io connected to {wsUrl}");
                _reconnectCount = 0;
                SendSocketEvent("join-room", new JoinRoomPayload { 
                    role = "headset", 
                    roomCode = currentRoomCode, 
                    locationId = currentLocationId 
                });
                OnConnected?.Invoke();
                StartPingSequence();
            };

            _ws.OnError += (err) => {
                Debug.LogError($"[SignalingManager] WebSocket error: {err}");
                OnConnectionError?.Invoke(err);
            };

            _ws.OnMessage += (bytes, start, length) => {
                string msg = System.Text.Encoding.UTF8.GetString(bytes, start, length);
                // Handle Socket.io-style framing (42[event, payload])
                if (msg.StartsWith("42")) {
                    ProcessIncomingMessage(msg.Substring(2));
                }
                else if (msg == "3") // Socket.io Pong
                {
                    HandlePong();
                }
            };

            _ws.OnClose += (code) => {
                Debug.Log($"[SignalingManager] Disconnected. Code: {code}");
                OnDisconnected?.Invoke();
                StopPingSequence();
                if (autoReconnect && _reconnectCount < maxReconnectAttempts && code != WebSocketCloseCode.Normal)
                {
                    StartCoroutine(ReconnectSequence());
                }
            };

            await _ws.Connect();
        }

        private IEnumerator ReconnectSequence()
        {
            _reconnectCount++;
            Debug.Log($"[SignalingManager] Attempting reconnect ({_reconnectCount}/{maxReconnectAttempts}) in {reconnectDelay}s...");
            yield return new WaitForSeconds(reconnectDelay);
            Login(currentLocationId, currentRoomCode);
        }

        private void StartPingSequence()
        {
            StopPingSequence();
            _pingCoroutine = StartCoroutine(PingLoop());
        }

        private void StopPingSequence()
        {
            if (_pingCoroutine != null) StopCoroutine(_pingCoroutine);
        }

        private float _pingStartTime;
        private IEnumerator PingLoop()
        {
            while (IsConnected)
            {
                _pingStartTime = Time.time;
                _ws.SendText("2"); // Socket.io Ping
                yield return new WaitForSeconds(5f);
            }
        }

        private void HandlePong()
        {
            currentLatency = (Time.time - _pingStartTime) * 1000f;
        }

        public void Disconnect()
        {
            _ws?.Close();
            _pc?.Close();
            _pc?.Dispose();
            _localStream?.Dispose();
            _videoTrack?.Dispose();
            _audioTrack?.Dispose();
            if (_webcamTexture != null) _webcamTexture.Stop();
            if (_internalCaptureCamera != null) Destroy(_internalCaptureCamera.gameObject);
            if (_captureRT != null) _captureRT.Release();
        }

        private void ProcessIncomingMessage(string json)
        {
            // Socket.io format: ["eventName", {...payload}]
            json = json.Trim();
            if (!json.StartsWith("[")) return;

            int nameStart = json.IndexOf('"') + 1;
            int nameEnd = json.IndexOf('"', nameStart);
            if (nameStart < 0 || nameEnd < 0) return;
            string eventName = json.Substring(nameStart, nameEnd - nameStart);

            int payloadStart = json.IndexOf(',', nameEnd) + 1;
            string payload = payloadStart > 0 ? json.Substring(payloadStart, json.Length - payloadStart - 1).Trim() : "{}";

            switch (eventName)
            {
                case "peer-joined":
                    var peer = JsonUtility.FromJson<PeerJoinedPayload>(payload);
                    _remoteSocketId = peer.socketId;
                    Debug.Log($"[SignalingManager] Admin joined: {peer.socketId}");
                    break;
                case "offer":
                    var offer = JsonUtility.FromJson<OfferPayload>(payload);
                    _remoteSocketId = offer.fromSocketId;
                    StartCoroutine(HandleRemoteOffer(offer.offer));
                    break;
                case "chat-message":
                    var chat = JsonUtility.FromJson<ChatPayload>(payload);
                    OnChatMessageReceived?.Invoke(chat.text);
                    break;
                case "point-to":
                    var pt = JsonUtility.FromJson<PointToPayload>(payload);
                    OnPointToReceived?.Invoke(pt.name, pt.qrCode, pt.pose);
                    break;
                case "pull-qrcodes":
                    OnQRCodesPulled?.Invoke(payload);
                    break;
            }
        }

        #endregion

        #region Outgoing Communications

        public void SendChatMessage(string message)
        {
            SendSocketEvent("chat-message", new ChatPayload { text = message });
        }

        public void PushQRCodes(string qrDataJson)
        {
            // Update to use REST API as preferred by Replit architecture for persistence
            PostData($"/locations/{currentLocationId}/qr-codes", qrDataJson, 
                (res) => Debug.Log("[SignalingManager] Calibration pushed successfully."),
                (err) => Debug.LogError($"[SignalingManager] Calibration push failed: {err}"));
        }

        public void PullQRCodes()
        {
            // Pulling from REST API
            GetData($"/locations/{currentLocationId}/qr-codes", 
                (res) => OnQRCodesPulled?.Invoke(res),
                (err) => Debug.LogError($"[SignalingManager] QR pull failed: {err}"));
        }

        public void FetchStartupData(Action<string> onComplete)
        {
            GetData($"/headsets/{headsetId}/startup-data?locationId={currentLocationId}", 
                onComplete,
                (err) => Debug.LogError($"[SignalingManager] Startup sync failed: {err}"));
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
            string url = $"{serverBaseUrl}{apiPath}/{endpoint.TrimStart('/')}";
            using (UnityWebRequest request = new UnityWebRequest(url, method))
            {
                if (json != null)
                {
                    byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
                    request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                }
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                    onSuccess?.Invoke(request.downloadHandler.text);
                else
                    onError?.Invoke(request.error);
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

        /// <summary>
        /// Starts capturing the local camera feed for preview purposes.
        /// </summary>
        public void StartLocalPreview()
        {
            if (_videoTrack != null) return;
            StartCoroutine(SetupLocalMedia());
        }

        private IEnumerator SetupLocalMedia()
        {
            if (useWebcam)
            {
                var devices = WebCamTexture.devices;
                if (devices.Length > 0)
                {
                    string device = string.IsNullOrEmpty(webcamDeviceName) ? devices[0].name : webcamDeviceName;
                    _webcamTexture = new WebCamTexture(device);
                    _webcamTexture.Play();
                    yield return new WaitUntil(() => _webcamTexture.width > 16);
                    _videoTrack = new VideoStreamTrack(_webcamTexture);
                    OnLocalStreamStarted?.Invoke(_webcamTexture);
                }
            }
            else
            {
                // Robust camera discovery for VR/MR
                float timeout = 10f;
                while (captureCamera == null && Camera.main == null && timeout > 0)
                {
                    timeout -= Time.deltaTime;
                    yield return null;
                }

                if (captureCamera == null) captureCamera = Camera.main;

                if (captureCamera != null)
                {
                    Debug.Log($"[SignalingManager] Starting capture from camera: {captureCamera.name}");
                    if (_internalCaptureCamera == null)
                    {
                        GameObject camObj = new GameObject("WebApp_CaptureCamera");
                        _internalCaptureCamera = camObj.AddComponent<Camera>();
                        _internalCaptureCamera.CopyFrom(captureCamera);
                        camObj.transform.SetParent(captureCamera.transform, false);
                        camObj.transform.localPosition = Vector3.zero;
                        camObj.transform.localRotation = Quaternion.identity;
                        _internalCaptureCamera.clearFlags = CameraClearFlags.Skybox;
                        if (captureCamera.clearFlags == CameraClearFlags.SolidColor)
                        {
                            _internalCaptureCamera.clearFlags = CameraClearFlags.SolidColor;
                            _internalCaptureCamera.backgroundColor = captureCamera.backgroundColor;
                        }

                        _captureRT = new RenderTexture(captureResolution.x, captureResolution.y, 16, UnityEngine.Experimental.Rendering.GraphicsFormat.B8G8R8A8_SRGB);
                        _captureRT.Create();
_internalCaptureCamera.targetTexture = _captureRT;
                    }
                    _videoTrack = new VideoStreamTrack(_captureRT);
                    OnLocalStreamStarted?.Invoke(_captureRT);
                }
                else
                {
                    Debug.LogError("[SignalingManager] No capture camera found. Streaming will be disabled.");
                }
            }
        }

        private IEnumerator HandleRemoteOffer(RTCSessionDescription offer)
        {
            var config = IceConfig;
            _pc = new RTCPeerConnection(ref config);

            _pc.OnTrack = (RTCTrackEvent ev) => {
                if (ev.Track is VideoStreamTrack videoTrack)
                {
                    videoTrack.OnVideoReceived += (Texture tex) => OnRemoteStreamStarted?.Invoke(tex);
                }
            };

            _pc.OnIceCandidate = (candidate) => {
                SendSocketEvent("ice-candidate", new IceCandidatePayload { 
                    candidate = candidate.Candidate,
                    sdpMid = candidate.SdpMid,
                    sdpMLineIndex = candidate.SdpMLineIndex ?? 0,
                    targetSocketId = _remoteSocketId
                });
            };

            // Setup local media if not already started
            if (_videoTrack == null)
            {
                yield return StartCoroutine(SetupLocalMedia());
            }
            
            _audioTrack = new AudioStreamTrack();
            _localStream = new MediaStream();
            if (_videoTrack != null) _localStream.AddTrack(_videoTrack);
            _localStream.AddTrack(_audioTrack);

            foreach (var track in _localStream.GetTracks())
                _pc.AddTrack(track, _localStream);

            var setRemoteOp = _pc.SetRemoteDescription(ref offer);
            yield return setRemoteOp;

            var createAnswerOp = _pc.CreateAnswer();
            yield return createAnswerOp;
            
            var answerDesc = createAnswerOp.Desc;
            var setLocalOp = _pc.SetLocalDescription(ref answerDesc);
            yield return setLocalOp;

            SendSocketEvent("answer", new OfferPayload { 
                offer = answerDesc, 
                fromSocketId = "", // Not needed for outgoing answer
                targetSocketId = _remoteSocketId 
            });
        }

        #endregion

        #region Helpers & Data Structures

        private void OnDestroy() { Disconnect(); }

        [Serializable] public class JoinRoomPayload { public string role; public string roomCode; public string locationId; }
        [Serializable] public class ChatPayload { public string text; }
        [Serializable] public class OfferPayload { public RTCSessionDescription offer; public string fromSocketId; public string targetSocketId; }
        [Serializable] public class IceCandidatePayload { public string candidate; public string sdpMid; public int sdpMLineIndex; public string targetSocketId; }
        [Serializable] public class PeerJoinedPayload { public string role; public string socketId; }
        [Serializable] public class PointToPayload { public string name; public string qrCode; public string pose; }
        #endregion
    }
}