using UnityEngine;
using UnityEngine.UI;
using TMPro;
using TrueEchoVR.LiveTroubleShooting;

namespace TrueEchoVR.LiveTroubleShooting
{
    /// <summary>
    /// Example UI controller that wires up the TEVRStreamingManager to Unity UI elements.
    /// Customize this for your specific Unity scene layout.
    /// </summary>
    public class TEVRSessionUI : MonoBehaviour
    {
        [Header("References")]
        public TEVRStreamingManager StreamingManager;

        [Header("Join Screen")]
        public GameObject JoinPanel;
        public TMP_InputField RoomCodeInput;
        public Button JoinButton;
        public TMP_Text StatusText;

        [Header("Session Screen")]
        public GameObject SessionPanel;
        public TMP_Text ConnectionStatusText;
        public TMP_Text ChatDisplay;
        public TMP_InputField ChatInput;
        public Button SendChatButton;
        public TMP_Text PointToText;

        private void Start()
        {
            ShowJoinScreen();

            if (JoinButton != null)
                JoinButton.onClick.AddListener(OnJoinPressed);

            if (SendChatButton != null)
                SendChatButton.onClick.AddListener(OnSendChat);

            if (StreamingManager != null)
            {
                StreamingManager.OnConnected = OnConnected;
                StreamingManager.OnDisconnected = OnDisconnected;
                StreamingManager.OnChatMessageReceived = OnChatReceived;
                StreamingManager.OnPointToReceived = OnPointToReceived;
            }
        }

        private void OnJoinPressed()
        {
            if (RoomCodeInput == null || string.IsNullOrEmpty(RoomCodeInput.text)) return;

            SetStatus("Connecting...");
            ShowSessionScreen();

            StreamingManager?.StartSession(RoomCodeInput.text.ToUpper().Trim());
        }

        private void OnConnected()
        {
            SetConnectionStatus("LIVE");
        }

        private void OnDisconnected()
        {
            SetConnectionStatus("Disconnected");
        }

        private void OnChatReceived(string message)
        {
            if (ChatDisplay == null) return;
            ChatDisplay.text += $"\n[Admin]: {message}";
        }

        private void OnPointToReceived(string objectName)
        {
            if (PointToText == null) return;
            PointToText.text = $"Point to: {objectName}";

            // TODO: Trigger AR arrow/highlight on objectName in your scene
            Debug.Log($"[TEVR] Admin wants to point to: {objectName}");
        }

        private void OnSendChat()
        {
            if (ChatInput == null || string.IsNullOrEmpty(ChatInput.text)) return;
            string msg = ChatInput.text;
            StreamingManager?.SendChatMessage(msg);
            if (ChatDisplay != null) ChatDisplay.text += $"\n[You]: {msg}";
            ChatInput.text = "";
        }

        private void ShowJoinScreen()
        {
            if (JoinPanel != null) JoinPanel.SetActive(true);
            if (SessionPanel != null) SessionPanel.SetActive(false);
        }

        private void ShowSessionScreen()
        {
            if (JoinPanel != null) JoinPanel.SetActive(false);
            if (SessionPanel != null) SessionPanel.SetActive(true);
        }

        private void SetStatus(string text)
        {
            if (StatusText != null) StatusText.text = text;
        }

        private void SetConnectionStatus(string text)
        {
            if (ConnectionStatusText != null) ConnectionStatusText.text = text;
        }
    }
}