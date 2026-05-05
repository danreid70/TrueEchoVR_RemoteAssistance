using UnityEngine;
using UnityEngine.UI;
using TrueEchoVR.LiveTroubleShooting;

namespace TrueEchoVR.LiveTroubleShooting
{
    /// <summary>
    /// VR world-space UI – floats in front/left of the camera, 50% transparent background.
    /// Uses Canvas + Legacy UI Text (no font issues). Keyboard pops up on Quest automatically.
    /// Creates a child GameObject for the UI; the manager's transform stays untouched.
    /// </summary>
    public class TEVRSessionUI : MonoBehaviour
    {
        [Header("References (auto‑found if empty)")]
        public TEVRStreamingManager streamingManager;
        public VRHUDManager hudManager;

        [Header("Positioning (same as VRHUDManager)")]
        [SerializeField] private float forwardDistance = 1.6f;
        [SerializeField] private float leftOffset = 0.65f;
        [SerializeField] private float verticalOffset = 0.2f;
        [SerializeField] private float smoothTime = 0.15f;
        [SerializeField] private float rotationSpeed = 3f;

        [Header("Panel Size (world units)")]
        [SerializeField] private float panelWidth = 0.85f;   // 85cm wide
        [SerializeField] private float panelHeight = 0.65f;  // 65cm high
        [SerializeField] private Color bgColor = new Color(0, 0, 0, 0.5f);

        // UI root (child object)
        private GameObject uiRoot;
        private Transform uiTransform;
        private Canvas canvas;

        // Panels
        private GameObject joinPanel;
        private GameObject sessionPanel;

        // Join screen widgets
        private InputField roomCodeInput;
        private Button joinButton;
        private Text joinStatusText;

        // Session screen widgets
        private Text connectionStatusText;
        private Text pointToText;
        private ScrollRect chatScrollRect;
        private Text chatDisplayText;
        private InputField chatInputField;
        private Button sendButton;
        private Button leaveButton;

        // Follow logic
        private Transform camTransform;
        private Vector3 velocity = Vector3.zero;
        private Quaternion targetRot;

        // Chat history
        private string chatHistory = "";

        private void Start()
        {
            // Find required managers
            if (streamingManager == null)
                streamingManager = GetComponent<TEVRStreamingManager>();
            if (streamingManager == null)
                Debug.LogError("[TEVRSessionUI] No TEVRStreamingManager found on this GameObject.");

            if (hudManager == null)
                hudManager = VRHUDManager.Instance;

            camTransform = Camera.main?.transform;
            if (camTransform == null)
            {
                Debug.LogError("[TEVRSessionUI] No main camera found.");
                enabled = false;
                return;
            }

            // Create the UI as a child (manager's transform remains unchanged)
            CreateUIRoot();

            // Build the UI elements
            BuildUI();

            // Hook up streaming events
            RegisterEvents();

            // Start on join screen
            ShowJoinScreen();

            // Initial placement (set after one frame to avoid first-frame glitch)
            uiTransform.position = ComputeTargetPosition();
            uiTransform.rotation = ComputeTargetRotation();
        }

        private void LateUpdate()
        {
            if (camTransform == null || uiTransform == null) return;
            Vector3 targetPos = ComputeTargetPosition();
            uiTransform.position = Vector3.SmoothDamp(uiTransform.position, targetPos, ref velocity, smoothTime);
            targetRot = ComputeTargetRotation();
            uiTransform.rotation = Quaternion.Slerp(uiTransform.rotation, targetRot, rotationSpeed * Time.deltaTime);
        }

        private Vector3 ComputeTargetPosition()
        {
            // left side = subtract right vector * leftOffset
            return camTransform.position
                   + camTransform.forward * forwardDistance
                   - camTransform.right * leftOffset
                   + Vector3.up * verticalOffset;
        }

        private Quaternion ComputeTargetRotation()
        {
            Vector3 toCam = camTransform.position - uiTransform.position;
            return Quaternion.LookRotation(-toCam, Vector3.up);
        }

        #region UI Construction

        private void CreateUIRoot()
        {
            uiRoot = new GameObject("TEVRSessionUI_Panel");
            uiTransform = uiRoot.transform;
            uiTransform.SetParent(transform, false);
            uiTransform.localScale = Vector3.one;

            canvas = uiRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main;
            canvas.GetComponent<RectTransform>().sizeDelta = new Vector2(panelWidth, panelHeight);

            // Make text crisp and readable
            var scaler = uiRoot.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;
            scaler.dynamicPixelsPerUnit = 150f;   // 1 point = 1/150 meter world size

            uiRoot.AddComponent<GraphicRaycaster>();

            // Background image (semi‑transparent)
            var bg = new GameObject("Background", typeof(RectTransform), typeof(Image));
            bg.transform.SetParent(uiRoot.transform, false);
            var bgImg = bg.GetComponent<Image>();
            bgImg.color = bgColor;
            var bgRect = bg.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.sizeDelta = Vector2.zero;
        }

        private void BuildUI()
        {
            joinPanel = CreatePanel("JoinPanel", true);
            sessionPanel = CreatePanel("SessionPanel", false);

            // ========== JOIN SCREEN ==========
            // Title
            var title = CreateText(joinPanel.transform, "Title", "Join Session", 48, FontStyle.Bold, Color.white);
            title.rectTransform.anchoredPosition = new Vector2(0, panelHeight * 0.35f);
            title.alignment = TextAnchor.MiddleCenter;

            // Room code input field
            var inputObj = CreateUIObject("RoomCodeInput", joinPanel.transform);
            var inputRect = inputObj.GetComponent<RectTransform>();
            inputRect.sizeDelta = new Vector2(panelWidth * 0.7f, 0.12f);
            inputRect.anchoredPosition = new Vector2(0, panelHeight * 0.1f);

            roomCodeInput = inputObj.AddComponent<InputField>();
            var inputImage = inputObj.AddComponent<Image>();
            inputImage.color = new Color(0.1f, 0.1f, 0.1f, 0.8f);
            roomCodeInput.targetGraphic = inputImage;

            var inputText = CreateText(inputObj.transform, "Text", "", 36, FontStyle.Normal, Color.white);
            inputText.rectTransform.anchorMin = Vector2.zero;
            inputText.rectTransform.anchorMax = Vector2.one;
            inputText.rectTransform.sizeDelta = Vector2.zero;
            inputText.alignment = TextAnchor.MiddleLeft;
            roomCodeInput.textComponent = inputText;
            roomCodeInput.characterLimit = 6;

            var placeholderObj = CreateUIObject("Placeholder", inputObj.transform);
            var placeholderText = placeholderObj.AddComponent<Text>();
            placeholderText.text = "Room Code";
            placeholderText.fontSize = 36;
            placeholderText.color = Color.gray;
            placeholderText.alignment = TextAnchor.MiddleLeft;
            var phRect = placeholderText.rectTransform;
            phRect.anchorMin = Vector2.zero;
            phRect.anchorMax = Vector2.one;
            phRect.sizeDelta = Vector2.zero;
            roomCodeInput.placeholder = placeholderText;

            // Join button
            var btnObj = CreateUIObject("JoinButton", joinPanel.transform);
            btnObj.GetComponent<RectTransform>().sizeDelta = new Vector2(panelWidth * 0.5f, 0.12f);
            btnObj.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -panelHeight * 0.1f);
            joinButton = btnObj.AddComponent<Button>();
            var btnImg = btnObj.AddComponent<Image>();
            btnImg.color = new Color(0.2f, 0.6f, 1f);
            joinButton.targetGraphic = btnImg;
            var btnText = CreateText(btnObj.transform, "Text", "Join Room", 42, FontStyle.Bold, Color.white);
            btnText.rectTransform.anchorMin = Vector2.zero;
            btnText.rectTransform.anchorMax = Vector2.one;
            btnText.rectTransform.sizeDelta = Vector2.zero;
            joinButton.onClick.AddListener(OnJoinPressed);

            // Status text
            joinStatusText = CreateText(joinPanel.transform, "Status", "Enter 6‑digit code", 28, FontStyle.Normal, new Color(0.8f, 0.8f, 0.8f));
            joinStatusText.rectTransform.anchoredPosition = new Vector2(0, -panelHeight * 0.3f);
            joinStatusText.alignment = TextAnchor.MiddleCenter;

            // ========== SESSION SCREEN ==========
            // Connection status
            connectionStatusText = CreateText(sessionPanel.transform, "ConnectionStatus", "Status: Connecting...", 28, FontStyle.Bold, new Color(0.2f, 0.6f, 1f));
            connectionStatusText.rectTransform.anchoredPosition = new Vector2(-panelWidth * 0.4f, panelHeight * 0.4f);
            connectionStatusText.alignment = TextAnchor.UpperLeft;

            // Point‑to label
            pointToText = CreateText(sessionPanel.transform, "PointTo", "Point to: —", 28, FontStyle.Normal, new Color(1f, 0.8f, 0.2f));
            pointToText.rectTransform.anchoredPosition = new Vector2(-panelWidth * 0.4f, panelHeight * 0.32f);
            pointToText.alignment = TextAnchor.UpperLeft;

            // Chat scroll view
            var scrollObj = CreateUIObject("ChatScrollView", sessionPanel.transform);
            var scrollRect = scrollObj.GetComponent<RectTransform>();
            scrollRect.anchorMin = new Vector2(0.05f, 0.25f);
            scrollRect.anchorMax = new Vector2(0.95f, 0.68f);
            scrollRect.sizeDelta = Vector2.zero;

            var sr = scrollObj.AddComponent<ScrollRect>();
            var scrollBg = scrollObj.AddComponent<Image>();
            scrollBg.color = new Color(0, 0, 0, 0.4f);

            var viewport = CreateUIObject("Viewport", scrollObj.transform);
            var vpRect = viewport.GetComponent<RectTransform>();
            vpRect.anchorMin = Vector2.zero;
            vpRect.anchorMax = Vector2.one;
            vpRect.sizeDelta = Vector2.zero;
            viewport.AddComponent<Mask>().showMaskGraphic = false;
            viewport.AddComponent<Image>().raycastTarget = false;

            var content = CreateUIObject("Content", viewport.transform);
            var contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0, 1);
            contentRect.anchorMax = new Vector2(1, 1);
            contentRect.pivot = new Vector2(0.5f, 1);
            contentRect.sizeDelta = new Vector2(0, 0);
            var vlg = content.AddComponent<VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.childControlHeight = false;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;

            chatDisplayText = CreateText(content.transform, "ChatDisplay", "", 26, FontStyle.Normal, Color.white);
            chatDisplayText.rectTransform.anchorMin = new Vector2(0, 1);
            chatDisplayText.rectTransform.anchorMax = new Vector2(1, 1);
            chatDisplayText.rectTransform.pivot = new Vector2(0.5f, 1);
            chatDisplayText.alignment = TextAnchor.UpperLeft;

            sr.viewport = vpRect;
            sr.content = contentRect;

            // Chat input row
            var chatRow = CreateUIObject("ChatRow", sessionPanel.transform);
            var rowRect = chatRow.GetComponent<RectTransform>();
            rowRect.anchorMin = new Vector2(0.05f, 0.08f);
            rowRect.anchorMax = new Vector2(0.95f, 0.2f);
            rowRect.sizeDelta = Vector2.zero;
            var hlg = chatRow.AddComponent<HorizontalLayoutGroup>();
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.spacing = 10;

            var chatInputObj = CreateUIObject("ChatInput", chatRow.transform);
            chatInputField = chatInputObj.AddComponent<InputField>();
            var chatInputImage = chatInputObj.AddComponent<Image>();
            chatInputImage.color = new Color(0.1f, 0.1f, 0.1f, 0.8f);
            chatInputField.targetGraphic = chatInputImage;
            var chatInputText = CreateText(chatInputObj.transform, "Text", "", 30, FontStyle.Normal, Color.white);
            chatInputText.rectTransform.anchorMin = Vector2.zero;
            chatInputText.rectTransform.anchorMax = Vector2.one;
            chatInputText.rectTransform.sizeDelta = Vector2.zero;
            chatInputText.alignment = TextAnchor.MiddleLeft;
            chatInputField.textComponent = chatInputText;

            var chatPlaceholder = CreateUIObject("Placeholder", chatInputObj.transform);
            var phText2 = chatPlaceholder.AddComponent<Text>();
            phText2.text = "Type message...";
            phText2.fontSize = 30;
            phText2.color = Color.gray;
            phText2.alignment = TextAnchor.MiddleLeft;
            var phRect2 = phText2.rectTransform;
            phRect2.anchorMin = Vector2.zero;
            phRect2.anchorMax = Vector2.one;
            phRect2.sizeDelta = Vector2.zero;
            chatInputField.placeholder = phText2;

            var inputLayout = chatInputObj.AddComponent<LayoutElement>();
            inputLayout.flexibleWidth = 1;

            var sendObj = CreateUIObject("SendButton", chatRow.transform);
            sendButton = sendObj.AddComponent<Button>();
            var sendImg = sendObj.AddComponent<Image>();
            sendImg.color = new Color(0.2f, 0.6f, 1f);
            sendButton.targetGraphic = sendImg;
            var sendText = CreateText(sendObj.transform, "Text", "Send", 30, FontStyle.Bold, Color.white);
            sendText.rectTransform.anchorMin = Vector2.zero;
            sendText.rectTransform.anchorMax = Vector2.one;
            sendText.rectTransform.sizeDelta = Vector2.zero;
            var sendLayout = sendObj.AddComponent<LayoutElement>();
            sendLayout.preferredWidth = 0.12f;
            sendButton.onClick.AddListener(OnSendChat);

            // Leave button
            var leaveObj = CreateUIObject("LeaveButton", sessionPanel.transform);
            var leaveRect = leaveObj.GetComponent<RectTransform>();
            leaveRect.sizeDelta = new Vector2(panelWidth * 0.4f, 0.12f);
            leaveRect.anchoredPosition = new Vector2(0, -panelHeight * 0.4f);
            leaveButton = leaveObj.AddComponent<Button>();
            var leaveImg = leaveObj.AddComponent<Image>();
            leaveImg.color = new Color(0.6f, 0.2f, 0.2f);
            leaveButton.targetGraphic = leaveImg;
            var leaveText = CreateText(leaveObj.transform, "Text", "Leave Session", 42, FontStyle.Bold, Color.white);
            leaveText.rectTransform.anchorMin = Vector2.zero;
            leaveText.rectTransform.anchorMax = Vector2.one;
            leaveText.rectTransform.sizeDelta = Vector2.zero;
            leaveButton.onClick.AddListener(OnLeaveSession);
        }

        private GameObject CreatePanel(string name, bool active)
        {
            var panel = new GameObject(name, typeof(RectTransform));
            panel.transform.SetParent(uiRoot.transform, false);
            var rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.sizeDelta = Vector2.zero;
            panel.SetActive(active);
            return panel;
        }

        private GameObject CreateUIObject(string name, Transform parent)
        {
            var obj = new GameObject(name, typeof(RectTransform));
            obj.transform.SetParent(parent, false);
            return obj;
        }

        private Text CreateText(Transform parent, string name, string text, int fontSize, FontStyle style, Color color)
        {
            var obj = new GameObject(name, typeof(RectTransform));
            obj.transform.SetParent(parent, false);
            var txt = obj.AddComponent<Text>();
            txt.text = text;
            txt.fontSize = fontSize;
            txt.fontStyle = style;
            txt.color = color;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.raycastTarget = false;
            return txt;
        }

        #endregion

        #region UI Logic & Callbacks

        private void RegisterEvents()
        {
            if (streamingManager == null) return;
            streamingManager.OnConnected = OnConnected;
            streamingManager.OnDisconnected = OnDisconnected;
            streamingManager.OnChatMessageReceived = OnChatReceived;
            streamingManager.OnPointToReceived = OnPointToReceived;
        }

        private void OnJoinPressed()
        {
            if (string.IsNullOrEmpty(roomCodeInput?.text))
            {
                SetJoinStatus("Please enter a room code.");
                return;
            }
            SetJoinStatus("Connecting...");
            string code = roomCodeInput.text.ToUpper().Trim();
            streamingManager?.StartSession(code);
            ShowSessionScreen();
        }

        private void OnSendChat()
        {
            if (string.IsNullOrEmpty(chatInputField?.text)) return;
            string msg = chatInputField.text;
            streamingManager?.SendChatMessage(msg);
            AppendChatMessage($"You: {msg}");
            chatInputField.text = "";
        }

        private void OnLeaveSession()
        {
            var method = streamingManager?.GetType().GetMethod("Disconnect");
            method?.Invoke(streamingManager, null);
            ShowJoinScreen();
            OnDisconnected();
        }

        private void OnConnected()
        {
            SetConnectionStatus("LIVE");
            AppendChatMessage("--- Connected to session ---");
        }

        private void OnDisconnected()
        {
            SetConnectionStatus("Disconnected");
            SetJoinStatus("Session ended. Join again.");
            ShowJoinScreen();
        }

        private void OnChatReceived(string message) => AppendChatMessage($"Admin: {message}");

        private void OnPointToReceived(string objectName)
        {
            pointToText.text = $"➡ Point to: {objectName}";
            if (hudManager != null)
            {
                var target = GameObject.Find(objectName);
                if (target != null) hudManager.SetTarget(target.transform);
                else hudManager.ClearHighlight();
            }
        }

        private void SetJoinStatus(string text) { if (joinStatusText) joinStatusText.text = text; }
        private void SetConnectionStatus(string text) { if (connectionStatusText) connectionStatusText.text = $"Status: {text}"; }
        private void AppendChatMessage(string msg)
        {
            chatHistory += $"{msg}\n";
            if (chatDisplayText) chatDisplayText.text = chatHistory;
            // Auto-scroll to bottom
            if (chatScrollRect != null) Canvas.ForceUpdateCanvases();
        }

        private void ShowJoinScreen()
        {
            if (joinPanel) joinPanel.SetActive(true);
            if (sessionPanel) sessionPanel.SetActive(false);
            if (hudManager != null) hudManager.ClearHighlight();
        }

        private void ShowSessionScreen()
        {
            if (joinPanel) joinPanel.SetActive(false);
            if (sessionPanel) sessionPanel.SetActive(true);
        }

        #endregion

        private void OnDestroy()
        {
            if (uiRoot != null) Destroy(uiRoot);
        }
    }
}