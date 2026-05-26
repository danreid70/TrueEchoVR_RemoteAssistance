using UnityEngine;
using Unity.WebRTC;
using Meta.Net.NativeWebSocket;
using System.Collections;
using System;

namespace TrueEchoVR
{
    public class TroubleshootingStreamingManager : MonoBehaviour
    {
        [Header("Server")] 
        public string ServerUrl = "wss://live-troubleshooting-app.replit.app";
        private WebSocket _ws;

        // UI Events
        public Action OnConnected;
        public Action OnDisconnected;
        public Action<string> OnConnectionError; 
        public Action<string> OnChatMessageReceived;
        public Action<string, string, string> OnPointToReceived; // name, qrCodePayload, poseData
        public Action<string> OnQRCodesPulled;
        public Action<Texture> OnRemoteStreamStarted;
        public Action<Texture> OnLocalStreamStarted;

        // WebRTC Components
        private RTCPeerConnection _pc;
        private MediaStream _localStream;
        private VideoStreamTrack _videoTrack;
        private AudioStreamTrack _audioTrack;

        [Header("Video Capture (Outgoing)")]
        [Tooltip("Note: Meta Quest 3 hardware prevents raw camera pixel access. This will stream virtual overlays only.")]
        public bool useWebcam = false;
        public Camera captureCamera;
        public Vector2Int captureResolution = new Vector2Int(1280, 720);
        public string webcamDeviceName = "";

        private WebCamTexture _webcamTexture;
        private RenderTexture _captureRT;
        private Camera _internalCaptureCamera;

        private string _currentRoomCode;

        void Start()
        {
            #if UNITY_ANDROID && !UNITY_EDITOR
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera))
            {
                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Camera);
            }
            #endif
            StartCoroutine(WebRTC.Update());
        }

        public void StartSession(string roomCode)
        {
            _currentRoomCode = roomCode;
            ConnectToSignalingServer();
        }

        public void Disconnect()
        {
            _ws?.Close();
            _pc?.Close();
            if (_webcamTexture != null) _webcamTexture.Stop();
            if (_internalCaptureCamera != null) Destroy(_internalCaptureCamera.gameObject);
            if (_captureRT != null) _captureRT.Release();
        }

        async void ConnectToSignalingServer()
        {
            if (_ws != null) await _ws.Close();

            _ws = new WebSocket(ServerUrl);

            _ws.OnOpen += () => {
                Debug.Log("WebSocket connected");
                SendSocketEvent("join-room", new JoinRoomPayload { role = "headset", roomCode = _currentRoomCode });
                OnConnected?.Invoke();
            };

            _ws.OnError += (err) => {
                Debug.LogError($"WebSocket error: {err}");
                OnConnectionError?.Invoke(err);
            };

            _ws.OnMessage += (bytes, start, length) => {
                string msg = System.Text.Encoding.UTF8.GetString(bytes, start, length);
                if (msg.StartsWith("42")) {
                    int startIdx = msg.IndexOf('[');
                    if (startIdx >= 0)
                    {
                        string dataStr = msg.Substring(startIdx);
                        if (dataStr.Contains("\"offer\"")) StartCoroutine(SetupPeerConnection(dataStr));
                        if (dataStr.Contains("\"chat\"")) HandleChat(dataStr);
                        if (dataStr.Contains("\"point-to\"")) HandlePointTo(dataStr);
                        if (dataStr.Contains("\"pull-qrcodes\"")) HandleQRCodesPulled(dataStr);
                    }
                }
            };

            _ws.OnClose += (code) => {
                OnDisconnected?.Invoke();
            };

            await _ws.Connect();
        }

        private void HandleChat(string json) {
            try {
                string msg = ParseJsonValue(json, "text");
                OnChatMessageReceived?.Invoke(msg);
            } catch { }
        }

        private void HandlePointTo(string json) {
            try {
                string name = ParseJsonValue(json, "name");
                string qrCode = ParseJsonValue(json, "qrCode");
                string pose = ParseJsonValue(json, "pose");
                OnPointToReceived?.Invoke(name, qrCode, pose);
            } catch { }
        }

        private void HandleQRCodesPulled(string json) {
            int dataStart = json.IndexOf(",{");
            if (dataStart > 0) {
                string data = json.Substring(dataStart + 1);
                data = data.Substring(0, data.Length - 1);
                OnQRCodesPulled?.Invoke(data);
            }
        }

        private string ParseJsonValue(string json, string key) {
            string search = $"\"{key}\":\"";
            int idx = json.IndexOf(search);
            if (idx < 0) return null;
            string val = json.Substring(idx + search.Length);
            int endIdx = val.IndexOf("\"");
            if (endIdx < 0) return null;
            return val.Substring(0, endIdx);
        }

        IEnumerator SetupPeerConnection(string offerSdpJson)
        {
            string sdp = ParseJsonValue(offerSdpJson, "sdp");
            if (string.IsNullOrEmpty(sdp)) yield break;

            _pc = new RTCPeerConnection();

            _pc.OnTrack = (RTCTrackEvent ev) => {
                if (ev.Track is VideoStreamTrack videoTrack)
                {
                    videoTrack.OnVideoReceived += (Texture tex) => {
                        OnRemoteStreamStarted?.Invoke(tex);
                    };
                }
            };

            if (useWebcam)
            {
                var devices = WebCamTexture.devices;
                Debug.Log($"[WebRTC] Found {devices.Length} webcam devices.");
                foreach(var d in devices) Debug.Log($"[WebRTC] Device: {d.name}");

                if (devices.Length > 0)
                {
                    string device = string.IsNullOrEmpty(webcamDeviceName) ? devices[0].name : webcamDeviceName;
                    _webcamTexture = new WebCamTexture(device);
                    _webcamTexture.Play();
                    yield return new WaitUntil(() => _webcamTexture.width > 16);
                    _videoTrack = new VideoStreamTrack(_webcamTexture);
                    OnLocalStreamStarted?.Invoke(_webcamTexture);
                    Debug.Log($"[WebRTC] Started local stream using webcam: {device}");
                }
                else
                {
                    Debug.LogError("[WebRTC] No webcam devices found for streaming.");
                }
            }
            else
            {
                if (captureCamera == null) captureCamera = Camera.main;
                if (captureCamera != null)
                {
                    if (_internalCaptureCamera == null)
                    {
                        GameObject camObj = new GameObject("WebRTC_CaptureCamera");
                        _internalCaptureCamera = camObj.AddComponent<Camera>();
                        _internalCaptureCamera.CopyFrom(captureCamera);
                        camObj.transform.SetParent(captureCamera.transform, false);
                        camObj.transform.localPosition = Vector3.zero;
                        camObj.transform.localRotation = Quaternion.identity;

                        _captureRT = new RenderTexture(captureResolution.x, captureResolution.y, 16, RenderTextureFormat.ARGB32);
                        _captureRT.name = "WebRTC_CaptureRT";
                        _captureRT.Create();
                        _internalCaptureCamera.targetTexture = _captureRT;
                    }
                    _videoTrack = new VideoStreamTrack(_captureRT);
                    OnLocalStreamStarted?.Invoke(_captureRT);
                }
            }
            _audioTrack = new AudioStreamTrack();

            _localStream = new MediaStream();
            if (_videoTrack != null) _localStream.AddTrack(_videoTrack);
            _localStream.AddTrack(_audioTrack);

            foreach (var track in _localStream.GetTracks())
                _pc.AddTrack(track, _localStream);

            var offer = new RTCSessionDescription { type = RTCSdpType.Offer, sdp = sdp };
            var setRemoteOp = _pc.SetRemoteDescription(ref offer);
            yield return setRemoteOp;

            var createAnswerOp = _pc.CreateAnswer();
            yield return createAnswerOp;
            
            var answerDesc = createAnswerOp.Desc;
            var setLocalOp = _pc.SetLocalDescription(ref answerDesc);
            yield return setLocalOp;

            SendSocketEvent("answer", new AnswerPayload { sdp = answerDesc.sdp, type = "answer" });

            _pc.OnIceCandidate = (candidate) => {
                SendSocketEvent("ice-candidate", new IceCandidatePayload { candidate = candidate.Candidate });
            };
        }

        public void SendChatMessage(string message) {
            SendSocketEvent("chat", new ChatPayload { text = message });
        }

        public void PushQRCodes(string qrDataJson) {
            _ws?.SendText($"42[\"push-qrcodes\",{qrDataJson}]");
        }

        public void PullQRCodes() {
            SendSocketEvent("pull-qrcodes", new EmptyPayload { });
        }

        void SendSocketEvent(string eventName, object payload) {
            string json = $"42[\"{eventName}\",{JsonUtility.ToJson(payload)}]";
            _ws?.SendText(json);
        }

        void Update() 
        { 
            // _ws.Receive() is not needed here as the WebSocket library handles it in a task.
        }

        void OnDestroy() { Disconnect(); }
        }

        [Serializable] public class JoinRoomPayload { public string role; public string roomCode; }
        [Serializable] public class ChatPayload { public string text; }
        [Serializable] public class AnswerPayload { public string sdp; public string type; }
        [Serializable] public class IceCandidatePayload { public string candidate; }
        [Serializable] public class EmptyPayload { }
        }