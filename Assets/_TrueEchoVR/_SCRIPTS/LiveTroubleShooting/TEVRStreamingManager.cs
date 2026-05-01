using UnityEngine;
using Unity.WebRTC;
using Meta.Net.NativeWebSocket;
using System.Collections;
using System;

namespace TrueEchoVR.LiveTroubleShooting
{
    public class TEVRStreamingManager : MonoBehaviour
    {
        [Header("Server")] public string ServerUrl = "wss://server-url";
        private WebSocket _ws;

        // UI Events
        public Action OnConnected;
        public Action OnDisconnected;
        public Action<string> OnChatMessageReceived;
        public Action<string> OnPointToReceived;

        // WebRTC Components
        private RTCPeerConnection _pc;
        private MediaStream _localStream;
        private VideoStreamTrack _videoTrack;
        private AudioStreamTrack _audioTrack;

        // WebCam Fallback
        [Header("Video")] public string webcamDeviceName = "";
        private WebCamTexture _webcamTexture;

        private string _currentRoomCode;

        void Start()
        {
            // Initializing WebRTC
            StartCoroutine(WebRTC.Update());
        }

        public void StartSession(string roomCode)
        {
            _currentRoomCode = roomCode;
            ConnectToSignalingServer();
        }

        async void ConnectToSignalingServer()
        {
            if (_ws != null) await _ws.Close();

            _ws = new WebSocket(ServerUrl);

            _ws.OnOpen += () => {
                Debug.Log("WebSocket connected");
                SendSocketEvent("join-room", new { role = "headset", roomCode = _currentRoomCode });
                OnConnected?.Invoke();
            };

            _ws.OnMessage += (bytes, start, length) => {
                string msg = System.Text.Encoding.UTF8.GetString(bytes, start, length);
                if (msg.StartsWith("42")) {
                    // Extract payload from "42[ ... ]"
                    int startIdx = msg.IndexOf('[');
                    if (startIdx >= 0)
                    {
                        string dataStr = msg.Substring(startIdx);
                        // Very basic manual parsing for demonstration, real app should use a proper Socket.io library
                        if (dataStr.Contains("offer")) StartCoroutine(SetupPeerConnection(dataStr));
                        if (dataStr.Contains("chat")) HandleChat(dataStr);
                        if (dataStr.Contains("point-to")) HandlePointTo(dataStr);
                    }
                }
            };

            _ws.OnClose += (code) => {
                OnDisconnected?.Invoke();
            };

            await _ws.Connect();
        }

        private void HandleChat(string json) {
            // Placeholder for chat parsing
            OnChatMessageReceived?.Invoke("New message");
        }

        private void HandlePointTo(string json) {
            // Placeholder for point-to parsing
            OnPointToReceived?.Invoke("Target Object");
        }

        IEnumerator SetupPeerConnection(string offerSdp)
        {
            _pc = new RTCPeerConnection();

            // --- Setup Local Stream from Webcam ---
            var devices = WebCamTexture.devices;
            if (devices.Length > 0)
            {
                string device = string.IsNullOrEmpty(webcamDeviceName) ? devices[0].name : webcamDeviceName;
                _webcamTexture = new WebCamTexture(device);
                _webcamTexture.Play();
                yield return new WaitUntil(() => _webcamTexture.width > 100);
                _videoTrack = new VideoStreamTrack(_webcamTexture);
            }
            // Add audio track
            _audioTrack = new AudioStreamTrack();

            _localStream = new MediaStream();
            _localStream.AddTrack(_videoTrack);
            _localStream.AddTrack(_audioTrack);

            foreach (var track in _localStream.GetTracks())
                _pc.AddTrack(track, _localStream);

            // --- Handle the 'offer' from admin ---
            var offer = new RTCSessionDescription { type = RTCSdpType.Offer, sdp = offerSdp };
            var setRemoteOp = _pc.SetRemoteDescription(ref offer);
            yield return setRemoteOp;

            var createAnswerOp = _pc.CreateAnswer();
            yield return createAnswerOp;
            
            var answerDesc = createAnswerOp.Desc;
            var setLocalOp = _pc.SetLocalDescription(ref answerDesc);
            yield return setLocalOp;

            // Send 'answer' back to admin via WebSocket
            SendSocketEvent("answer", new { sdp = answerDesc.sdp, type = "answer" });

            _pc.OnIceCandidate = (candidate) => {
                SendSocketEvent("ice-candidate", new { candidate = candidate.Candidate });
            };
        }

        public void SendChatMessage(string message) {
            SendSocketEvent("chat", new { text = message });
        }

        void SendSocketEvent(string eventName, object payload) {
            string json = $"42[\"{eventName}\",{JsonUtility.ToJson(payload)}]";
            _ws?.SendText(json);
        }

        void Update() { _ws?.Receive(); }
        void OnDestroy() { _ws?.Close(); }
    }
}