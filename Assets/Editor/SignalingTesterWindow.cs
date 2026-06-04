using UnityEditor;
using UnityEngine;

namespace TEVR.EditorTools
{
    /// <summary>
    /// In-editor harness to drive and observe the Replit signaling handshake.
    /// Open via: Tools > TEVR > Signaling Tester. Enter Play Mode, type a room code, press Connect,
    /// and watch the live status here + the raw Engine.IO/Socket.IO packet trace in the Console
    /// (SignalingManager.verboseSocketLogging must be ON).
    /// WebSocket + REST work on desktop, so this validates the handshake WITHOUT a headset.
    /// </summary>
    public class SignalingTesterWindow : EditorWindow
    {
        private string _roomCode = "TEST123";

        [MenuItem("Tools/TEVR/Signaling Tester")]
        public static void Open()
        {
            var w = GetWindow<SignalingTesterWindow>("TEVR Signaling Tester");
            w.minSize = new Vector2(360, 260);
        }

        private void OnInspectorUpdate()
        {
            // Repaint ~10x/sec so live status fields stay current.
            Repaint();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Replit Signaling Tester", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "1) Open the Bootstrap scene.\n2) Enter Play Mode.\n3) Type a room code that the web app is hosting.\n4) Press Connect.\nWatch live status below and the raw packet trace in the Console.",
                MessageType.Info);

            EditorGUILayout.Space();

            var mgr = Application.isPlaying ? TEVR.SignalingManager.Instance : null;

            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                _roomCode = EditorGUILayout.TextField("Room Code", _roomCode);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Connect"))
                    {
                        if (mgr != null) mgr.Login(_roomCode);
                        else Debug.LogWarning("[SignalingTester] SignalingManager.Instance is null. Is the Bootstrap scene loaded?");
                    }
                    if (GUILayout.Button("Disconnect"))
                    {
                        if (mgr != null) mgr.Disconnect();
                    }
                }

                if (GUILayout.Button("Send Test Chat"))
                {
                    if (mgr != null) mgr.SendChatMessage("Hello from Unity editor harness");
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Live Status", EditorStyles.boldLabel);

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode to use the tester.", MessageType.Warning);
                return;
            }
            if (mgr == null)
            {
                EditorGUILayout.HelpBox("SignalingManager.Instance is null. Load the Bootstrap scene.", MessageType.Error);
                return;
            }

            EditorGUILayout.Toggle("WebSocket Open (IsConnected)", mgr.IsConnected);
            EditorGUILayout.Toggle("Socket.IO Connected (40 ack)", mgr.IsSocketConnected);
            EditorGUILayout.LabelField("Latency (ms)", Mathf.RoundToInt(mgr.currentLatency).ToString());
            EditorGUILayout.LabelField("Room Code", string.IsNullOrEmpty(mgr.currentRoomCode) ? "<none>" : mgr.currentRoomCode);
            EditorGUILayout.LabelField("Headset ID", string.IsNullOrEmpty(mgr.tevrHeadsetId) ? "<none>" : mgr.tevrHeadsetId);
            EditorGUILayout.LabelField("Location ID", string.IsNullOrEmpty(mgr.tevrLocationId) ? "<none>" : mgr.tevrLocationId);

            mgr.verboseSocketLogging = EditorGUILayout.Toggle("Verbose Packet Logging", mgr.verboseSocketLogging);
        }
    }
}
