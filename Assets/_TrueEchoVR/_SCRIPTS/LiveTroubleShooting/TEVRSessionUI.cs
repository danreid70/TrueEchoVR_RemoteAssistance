using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

namespace TrueEchoVR
{
    public class TEVRSessionUI : MonoBehaviour
    {
        [Header("References (auto‑found if empty)")]
        public TEVRStreamingManager streamingManager;
        public VRHUDManager hudManager;
        public QRCodeManager qrManager;
        public TaskStatusUI statusUI;

        [Header("Positioning (relative to camera)")]
        [SerializeField] private float forwardDistance = 1.6f;
        [SerializeField] private float rightOffset = 0.85f;
        [SerializeField] private float verticalOffset = 0.2f;
        [SerializeField] private float smoothTime = 0.15f;
        [SerializeField] private float rotationSpeed = 3f;
        [Tooltip("Degrees of head rotation before panel starts moving again.")]
        [SerializeField] private float angleThreshold = 30f;
        [Tooltip("Distance moved before panel starts moving again.")]
        [SerializeField] private float distanceThreshold = 0.2f;

        [Header("Panel Size & Scale")]
        [SerializeField] private float panelWorldScale = 0.02f;      // overall scale of the panel (0.02 = 2%)
        [SerializeField] private float panelPixelWidth = 1200f;     // width in pixels
        [SerializeField] private float panelPixelHeight = 900f;     // height in pixels
        [SerializeField] private Color bgColor = new Color(0, 0, 0, 0.85f);

        // UI root (named "VR_Session_Panel")
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
        private RawImage localVideoImage;
        private RawImage remoteVideoImage;
        private Button detectQRButton;
        private Text detectQRButtonText;
        private Button pushQRButton;
        private Button pullQRButton;
        private Button clearQRButton;

        // Location ID input
        private InputField locationIdInput;
        private Text locationIdLabel;

        // QR list
        private Transform qrListContent;
        private GameObject qrListItemPrefab;
        private Dictionary<string, GameObject> qrListItems = new Dictionary<string, GameObject>();

        // Chat
        private ScrollRect chatScrollRect;
        private Text chatDisplayText;
        private InputField chatInputField;
        private Button sendButton;
        private Button leaveButton;

        // Follow logic
        private Transform camTransform;
        private Vector3 velocity = Vector3.zero;
        private Quaternion targetRot;
        private Vector3 lastCameraPos;
        private Quaternion lastCameraRot;
        private bool isFollowing = true;

        private string chatHistory = "";
        private Font defaultFont;
        private float fontScale = 1f;

        private void Awake()
        {
            Debug.Log("[TEVRSessionUI] Awake");
            fontScale = panelWorldScale * 100f; // because referencePixelsPerUnit = 100
            if (fontScale < 0.5f) fontScale = 0.5f;
        }

        private void Start()
        {
            Debug.Log("[TEVRSessionUI] Start");
            if (streamingManager == null) streamingManager = GetComponent<TEVRStreamingManager>();
            if (hudManager == null) hudManager = VRHUDManager.Instance;
            if (qrManager == null) qrManager = GetComponent<QRCodeManager>();
            if (statusUI == null) statusUI = GetComponent<TaskStatusUI>();

            // Load font with scaled size
            defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (defaultFont == null)
            {
                defaultFont = Font.CreateDynamicFontFromOSFont("Arial", Mathf.RoundToInt(14 * fontScale));
            }

            camTransform = Camera.main?.transform;
            if (camTransform == null)
            {
                Debug.LogError("[TEVRSessionUI] No main camera found.");
                enabled = false;
                return;
            }

            CreateUIRoot();
            BuildUI();
            RegisterEvents();
            ShowJoinScreen();

            Vector3 targetPos = ComputeTargetPosition();
            uiTransform.position = targetPos;
            uiTransform.rotation = ComputeTargetRotation();
            lastCameraPos = camTransform.position;
            lastCameraRot = camTransform.rotation;
            isFollowing = false;

            Debug.Log("[TEVRSessionUI] Panel created: " + uiRoot.name + " with scale " + panelWorldScale);
        }

        private void LateUpdate()
        {
            if (camTransform == null || uiTransform == null) return;

            float angle = Quaternion.Angle(lastCameraRot, camTransform.rotation);
            float distance = Vector3.Distance(lastCameraPos, camTransform.position);
            if (angle > angleThreshold || distance > distanceThreshold)
            {
                isFollowing = true;
                lastCameraPos = camTransform.position;
                lastCameraRot = camTransform.rotation;
            }

            if (isFollowing)
            {
                Vector3 targetPos = ComputeTargetPosition();
                uiTransform.position = Vector3.SmoothDamp(uiTransform.position, targetPos, ref velocity, smoothTime);
                targetRot = ComputeTargetRotation();
                uiTransform.rotation = Quaternion.Slerp(uiTransform.rotation, targetRot, rotationSpeed * Time.deltaTime);

                if (Vector3.Distance(uiTransform.position, targetPos) < 0.005f &&
                    Quaternion.Angle(uiTransform.rotation, targetRot) < 0.5f)
                {
                    isFollowing = false;
                    uiTransform.position = targetPos;
                    uiTransform.rotation = targetRot;
                }
            }
        }

        private Vector3 ComputeTargetPosition()
        {
            return camTransform.position
                   + camTransform.forward * forwardDistance
                   + camTransform.right * rightOffset
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
            uiRoot = new GameObject("VR_Session_Panel");
            uiTransform = uiRoot.transform;
            uiTransform.SetParent(transform, false);
            uiTransform.localScale = Vector3.one * panelWorldScale;

            canvas = uiRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main;
            RectTransform rect = canvas.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(panelPixelWidth, panelPixelHeight);

            CanvasScaler scaler = uiRoot.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;
            scaler.referencePixelsPerUnit = 100f;

            uiRoot.AddComponent<GraphicRaycaster>();

            // Background
            GameObject bg = new GameObject("Background", typeof(RectTransform), typeof(Image));
            bg.transform.SetParent(uiRoot.transform, false);
            Image bgImg = bg.GetComponent<Image>();
            bgImg.color = bgColor;
            RectTransform bgRect = bg.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.sizeDelta = Vector2.zero;
        }

        private void BuildUI()
        {
            joinPanel = CreatePanel("JoinPanel", true);
            sessionPanel = CreatePanel("SessionPanel", false);

            // ========== JOIN SCREEN ==========
            Text title = CreateText(joinPanel.transform, "Title", "Live Session", 60, FontStyle.Bold, Color.white);
            title.rectTransform.anchoredPosition = new Vector2(0, panelPixelHeight * 0.35f);

            // Room code input
            GameObject inputObj = CreateUIObject("RoomCodeInput", joinPanel.transform);
            RectTransform inputRect = inputObj.GetComponent<RectTransform>();
            inputRect.sizeDelta = new Vector2(panelPixelWidth * 0.5f, 100f);
            inputRect.anchoredPosition = new Vector2(0, 50f);

            roomCodeInput = inputObj.AddComponent<InputField>();
            Image inputImage = inputObj.AddComponent<Image>();
            inputImage.color = new Color(0.1f, 0.1f, 0.1f, 0.9f);
            roomCodeInput.targetGraphic = inputImage;

            Text inputText = CreateText(inputObj.transform, "Text", "", 40, FontStyle.Normal, Color.white);
            inputText.rectTransform.anchorMin = Vector2.zero;
            inputText.rectTransform.anchorMax = Vector2.one;
            inputText.rectTransform.sizeDelta = new Vector2(-20, 0);
            inputText.alignment = TextAnchor.MiddleCenter;
            roomCodeInput.textComponent = inputText;
            roomCodeInput.characterLimit = 6;

            GameObject phObj = CreateUIObject("Placeholder", inputObj.transform);
            Text phText = phObj.AddComponent<Text>();
            phText.text = "Room Code";
            phText.fontSize = 40;
            phText.color = Color.gray;
            phText.alignment = TextAnchor.MiddleCenter;
            phText.rectTransform.anchorMin = Vector2.zero;
            phText.rectTransform.anchorMax = Vector2.one;
            phText.rectTransform.sizeDelta = Vector2.zero;
            roomCodeInput.placeholder = phText;

            // Join button
            GameObject joinBtnObj = CreateUIObject("JoinButton", joinPanel.transform);
            joinBtnObj.GetComponent<RectTransform>().sizeDelta = new Vector2(panelPixelWidth * 0.4f, 120f);
            joinBtnObj.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -150f);
            joinButton = joinBtnObj.AddComponent<Button>();
            Image joinBtnImg = joinBtnObj.AddComponent<Image>();
            joinBtnImg.color = new Color(0.1f, 0.5f, 1f);
            Text joinBtnText = CreateText(joinBtnObj.transform, "Text", "JOIN", 45, FontStyle.Bold, Color.white);
            joinBtnText.rectTransform.anchorMin = Vector2.zero;
            joinBtnText.rectTransform.anchorMax = Vector2.one;
            joinBtnText.rectTransform.sizeDelta = Vector2.zero;
            joinButton.onClick.AddListener(OnJoinPressed);

            joinStatusText = CreateText(joinPanel.transform, "Status", "Enter 6-digit room code", 24, FontStyle.Normal, Color.gray);
            joinStatusText.rectTransform.anchoredPosition = new Vector2(0, -300f);

            // ========== SESSION SCREEN ==========
            Text sessionTitle = CreateText(sessionPanel.transform, "Title", "Live Session", 40, FontStyle.Bold, Color.white);
            sessionTitle.rectTransform.anchorMin = new Vector2(0.5f, 1);
            sessionTitle.rectTransform.anchorMax = new Vector2(0.5f, 1);
            sessionTitle.rectTransform.anchoredPosition = new Vector2(0, -50f);

            // Videos
            GameObject videoContainer = CreateUIObject("Videos", sessionPanel.transform);
            RectTransform vcRect = videoContainer.GetComponent<RectTransform>();
            vcRect.anchorMin = new Vector2(0.05f, 0.55f);
            vcRect.anchorMax = new Vector2(0.95f, 0.9f);
            vcRect.sizeDelta = Vector2.zero;
            HorizontalLayoutGroup vcLayout = videoContainer.AddComponent<HorizontalLayoutGroup>();
            vcLayout.spacing = 10;
            vcLayout.childForceExpandWidth = true;

            localVideoImage = CreateVideoDisplay(videoContainer.transform, "LocalVideo", "Preview");
            remoteVideoImage = CreateVideoDisplay(videoContainer.transform, "RemoteVideo", "Web Stream");

            // Location ID row
            GameObject locationRow = CreateUIObject("LocationRow", sessionPanel.transform);
            RectTransform locRect = locationRow.GetComponent<RectTransform>();
            locRect.anchorMin = new Vector2(0.05f, 0.5f);
            locRect.anchorMax = new Vector2(0.95f, 0.53f);
            locRect.sizeDelta = Vector2.zero;
            HorizontalLayoutGroup locLayout = locationRow.AddComponent<HorizontalLayoutGroup>();
            locLayout.spacing = 10;
            locLayout.childForceExpandWidth = false;

            locationIdLabel = CreateText(locationRow.transform, "Label", "Location ID:", 18, FontStyle.Bold, Color.white);
            locationIdLabel.rectTransform.sizeDelta = new Vector2(200f, 0);
            GameObject locInputObj = CreateUIObject("LocationInput", locationRow.transform);
            locInputObj.AddComponent<LayoutElement>().flexibleWidth = 1;
            locationIdInput = locInputObj.AddComponent<InputField>();
            locInputObj.AddComponent<Image>().color = new Color(0.2f, 0.2f, 0.2f, 0.8f);
            Text locInputText = CreateText(locInputObj.transform, "Text", "", 18, FontStyle.Normal, Color.white);
            locInputText.rectTransform.anchorMin = Vector2.zero;
            locInputText.rectTransform.anchorMax = Vector2.one;
            locInputText.rectTransform.sizeDelta = new Vector2(-10, 0);
            locInputText.alignment = TextAnchor.MiddleLeft;
            locationIdInput.textComponent = locInputText;
            locationIdInput.placeholder = CreateText(locInputObj.transform, "Placeholder", "Room name", 18, FontStyle.Italic, Color.gray);

            // QR Controls
            GameObject qrControls = CreateUIObject("QRControls", sessionPanel.transform);
            RectTransform qrRect = qrControls.GetComponent<RectTransform>();
            qrRect.anchorMin = new Vector2(0.05f, 0.45f);
            qrRect.anchorMax = new Vector2(0.95f, 0.48f);
            qrRect.sizeDelta = Vector2.zero;
            HorizontalLayoutGroup qrHlg = qrControls.AddComponent<HorizontalLayoutGroup>();
            qrHlg.spacing = 10;
            qrHlg.childForceExpandWidth = true;

            detectQRButton = CreateButton(qrControls.transform, "ToggleDetect", "Stop Detection", new Color(0.2f, 0.2f, 0.2f));
            detectQRButtonText = detectQRButton.GetComponentInChildren<Text>();
            detectQRButton.onClick.AddListener(OnToggleDetectQR);

            clearQRButton = CreateButton(qrControls.transform, "ClearQR", "Clear QR Codes", new Color(0.5f, 0.1f, 0.1f));
            clearQRButton.onClick.AddListener(OnClearQRPressed);

            pushQRButton = CreateButton(qrControls.transform, "PushQR", "Push QR Codes", new Color(0.1f, 0.6f, 0.3f));
            pushQRButton.onClick.AddListener(OnPushQRPressed);

            pullQRButton = CreateButton(qrControls.transform, "PullQR", "Pull QR Codes", new Color(0.1f, 0.4f, 0.6f));
            pullQRButton.onClick.AddListener(OnPullQRPressed);

            // QR list header
            Text qrListHeader = CreateText(sessionPanel.transform, "QRListHeader", "Detected QR Codes:", 20, FontStyle.Bold, Color.white);
            RectTransform headerRect = qrListHeader.rectTransform;
            headerRect.anchorMin = new Vector2(0.05f, 0.42f);
            headerRect.anchorMax = new Vector2(0.95f, 0.44f);
            headerRect.anchoredPosition = Vector2.zero;

            // QR scroll list
            GameObject qrListContainer = CreateUIObject("QRListScroll", sessionPanel.transform);
            RectTransform qrListRect = qrListContainer.GetComponent<RectTransform>();
            qrListRect.anchorMin = new Vector2(0.05f, 0.22f);
            qrListRect.anchorMax = new Vector2(0.95f, 0.4f);
            qrListRect.sizeDelta = Vector2.zero;
            ScrollRect qrScroll = qrListContainer.AddComponent<ScrollRect>();
            qrScroll.horizontal = false;
            GameObject qrViewport = CreateUIObject("Viewport", qrListContainer.transform);
            qrViewport.AddComponent<Mask>().showMaskGraphic = false;
            qrViewport.AddComponent<Image>().color = new Color(0, 0, 0, 0.3f);
            qrScroll.viewport = qrViewport.GetComponent<RectTransform>();
            qrListContent = CreateUIObject("Content", qrViewport.transform).transform;
            qrListContent.gameObject.AddComponent<VerticalLayoutGroup>();
            ContentSizeFitter contentFitter = qrListContent.gameObject.AddComponent<ContentSizeFitter>();
            contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            qrScroll.content = qrListContent.GetComponent<RectTransform>();
            qrScroll.viewport.anchorMin = Vector2.zero;
            qrScroll.viewport.anchorMax = Vector2.one;
            qrScroll.viewport.sizeDelta = Vector2.zero;

            // QR list item prefab
            qrListItemPrefab = new GameObject("QRListItem", typeof(RectTransform));
            qrListItemPrefab.transform.SetParent(qrListContent, false);
            LayoutElement itemLayout = qrListItemPrefab.AddComponent<LayoutElement>();
            itemLayout.minHeight = 30;
            Text itemText = qrListItemPrefab.AddComponent<Text>();
            itemText.font = defaultFont;
            itemText.fontSize = Mathf.RoundToInt(14 * fontScale);
            itemText.color = Color.white;
            qrListItemPrefab.SetActive(false);

            // Connection status
            connectionStatusText = CreateText(sessionPanel.transform, "ConnStatus", "Status: Disconnected", 16, FontStyle.Italic, Color.cyan);
            connectionStatusText.rectTransform.anchorMin = new Vector2(0, 1);
            connectionStatusText.rectTransform.anchorMax = new Vector2(0, 1);
            connectionStatusText.rectTransform.pivot = new Vector2(0, 1);
            connectionStatusText.rectTransform.anchoredPosition = new Vector2(50f, -20f);

            // Chat area
            GameObject chatContainer = CreateUIObject("Chat", sessionPanel.transform);
            RectTransform chatRect = chatContainer.GetComponent<RectTransform>();
            chatRect.anchorMin = new Vector2(0.05f, 0.05f);
            chatRect.anchorMax = new Vector2(0.95f, 0.2f);
            chatRect.sizeDelta = Vector2.zero;

            GameObject srObj = CreateUIObject("Scroll", chatContainer.transform);
            RectTransform srRect = srObj.GetComponent<RectTransform>();
            srRect.anchorMin = Vector2.zero;
            srRect.anchorMax = new Vector2(1, 0.7f);
            srRect.sizeDelta = Vector2.zero;
            srObj.AddComponent<Image>().color = new Color(0, 0, 0, 0.3f);
            chatScrollRect = srObj.AddComponent<ScrollRect>();

            GameObject vp = CreateUIObject("Viewport", srObj.transform);
            vp.GetComponent<RectTransform>().anchorMin = Vector2.zero;
            vp.GetComponent<RectTransform>().anchorMax = Vector2.one;
            vp.GetComponent<RectTransform>().sizeDelta = Vector2.zero;
            vp.AddComponent<Mask>().showMaskGraphic = false;
            vp.AddComponent<Image>();

            GameObject content = CreateUIObject("Content", vp.transform);
            RectTransform contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0, 1);
            contentRect.anchorMax = new Vector2(1, 1);
            contentRect.pivot = new Vector2(0.5f, 1);
            content.AddComponent<VerticalLayoutGroup>().childControlHeight = true;
            content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            chatDisplayText = CreateText(content.transform, "Display", "", 16, FontStyle.Normal, Color.white);
            chatDisplayText.alignment = TextAnchor.UpperLeft;
            chatDisplayText.rectTransform.sizeDelta = new Vector2(0, 100);

            chatScrollRect.viewport = vp.GetComponent<RectTransform>();
            chatScrollRect.content = contentRect;

            GameObject inputRow = CreateUIObject("InputRow", chatContainer.transform);
            RectTransform rowRect = inputRow.GetComponent<RectTransform>();
            rowRect.anchorMin = new Vector2(0, 0);
            rowRect.anchorMax = new Vector2(1, 0.25f);
            rowRect.sizeDelta = Vector2.zero;
            inputRow.AddComponent<HorizontalLayoutGroup>().spacing = 5;

            GameObject chatInpObj = CreateUIObject("ChatInput", inputRow.transform);
            chatInputField = chatInpObj.AddComponent<InputField>();
            chatInpObj.AddComponent<Image>().color = new Color(1, 1, 1, 0.1f);
            Text chatInpText = CreateText(chatInpObj.transform, "Text", "", 16, FontStyle.Normal, Color.white);
            chatInpText.rectTransform.anchorMin = Vector2.zero;
            chatInpText.rectTransform.anchorMax = Vector2.one;
            chatInpText.rectTransform.sizeDelta = new Vector2(-10, 0);
            chatInpText.alignment = TextAnchor.MiddleLeft;
            chatInputField.textComponent = chatInpText;
            chatInpObj.AddComponent<LayoutElement>().flexibleWidth = 1;

            sendButton = CreateButton(inputRow.transform, "Send", "Send", new Color(0.2f, 0.5f, 0.8f));
            sendButton.GetComponent<LayoutElement>().preferredWidth = 0.2f;
            sendButton.onClick.AddListener(OnSendChat);

            // Leave button
            leaveButton = CreateButton(sessionPanel.transform, "Leave", "LEAVE SESSION", new Color(0.6f, 0.2f, 0.2f));
            RectTransform leaveRect = leaveButton.GetComponent<RectTransform>();
            leaveRect.anchorMin = new Vector2(0.5f, 0);
            leaveRect.anchorMax = new Vector2(0.5f, 0);
            leaveRect.sizeDelta = new Vector2(panelPixelWidth * 0.4f, 60f);
            leaveRect.anchoredPosition = new Vector2(0, 20f);
            leaveButton.onClick.AddListener(OnLeaveSession);
        }

        private RawImage CreateVideoDisplay(Transform parent, string name, string label)
        {
            GameObject obj = CreateUIObject(name, parent);
            RawImage img = obj.AddComponent<RawImage>();
            img.color = Color.black;
            Text lbl = CreateText(obj.transform, "Label", label, Mathf.RoundToInt(12 * fontScale), FontStyle.Normal, new Color(1, 1, 1, 0.7f));
            lbl.rectTransform.anchoredPosition = new Vector2(0, -60f);
            return img;
        }

        private Button CreateButton(Transform parent, string name, string label, Color color)
        {
            GameObject obj = CreateUIObject(name, parent);
            Button btn = obj.AddComponent<Button>();
            Image img = obj.AddComponent<Image>();
            img.color = color;
            Text txt = CreateText(obj.transform, "Text", label, Mathf.RoundToInt(16 * fontScale), FontStyle.Bold, Color.white);
            txt.rectTransform.anchorMin = Vector2.zero;
            txt.rectTransform.anchorMax = Vector2.one;
            txt.rectTransform.sizeDelta = Vector2.zero;
            return btn;
        }

        private GameObject CreatePanel(string name, bool active)
        {
            GameObject panel = new GameObject(name, typeof(RectTransform));
            panel.transform.SetParent(uiRoot.transform, false);
            RectTransform rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.sizeDelta = Vector2.zero;
            panel.SetActive(active);
            return panel;
        }

        private GameObject CreateUIObject(string name, Transform parent)
        {
            GameObject obj = new GameObject(name, typeof(RectTransform));
            obj.transform.SetParent(parent, false);
            return obj;
        }

        private Text CreateText(Transform parent, string name, string text, int fontSize, FontStyle style, Color color)
        {
            GameObject obj = new GameObject(name, typeof(RectTransform));
            obj.transform.SetParent(parent, false);
            Text txt = obj.AddComponent<Text>();
            txt.text = text;
            txt.font = defaultFont;
            txt.fontSize = fontSize;
            txt.fontStyle = style;
            txt.color = color;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.raycastTarget = false;
            return txt;
        }

        #endregion

        #region Logic & Callbacks

        private void RegisterEvents()
        {
            if (streamingManager == null) return;
            streamingManager.OnConnected += OnConnected;
            streamingManager.OnDisconnected += OnDisconnected;
            streamingManager.OnChatMessageReceived += OnChatReceived;
            streamingManager.OnPointToReceived += PointToQRCode;
            streamingManager.OnRemoteStreamStarted += (tex) => { if (remoteVideoImage) remoteVideoImage.texture = tex; };
            streamingManager.OnLocalStreamStarted += (tex) => { if (localVideoImage) localVideoImage.texture = tex; };
            streamingManager.OnQRCodesPulled += OnQRCodesPulled;
        }

        private void OnJoinPressed()
        {
            if (string.IsNullOrEmpty(roomCodeInput?.text)) return;
            string code = roomCodeInput.text.ToUpper().Trim();
            streamingManager?.StartSession(code);
            ShowSessionScreen();
        }

        private void OnSendChat()
        {
            if (string.IsNullOrEmpty(chatInputField?.text)) return;
            streamingManager?.SendChatMessage(chatInputField.text);
            AppendChatMessage($"You: {chatInputField.text}");
            chatInputField.text = "";
        }

        private void OnLeaveSession()
        {
            streamingManager?.Disconnect();
            OnDisconnected();
        }

        private void OnConnected()
        {
            connectionStatusText.text = "Status: LIVE";
            AppendChatMessage("--- Connected ---");
        }

        private void OnDisconnected()
        {
            ShowJoinScreen();
        }

        private void OnChatReceived(string msg) => AppendChatMessage($"Admin: {msg}");

        public void PointToQRCode(string name, string qrCodePayload, string poseData)
        {
            if (qrManager == null) return;
            QRCodeManager.QRCodeInstance targetQR = null;
            foreach (var qr in qrManager.TrackedQRCodes.Values)
            {
                if (qr.fullPayload.Contains(qrCodePayload) || qrCodePayload.Contains(qr.fullPayload))
                {
                    targetQR = qr;
                    break;
                }
            }

            if (targetQR != null)
            {
                string displayName = string.IsNullOrEmpty(name) ? "Remote Target" : name;
                statusUI?.ShowMessage(displayName, $"Payload: {qrCodePayload}");
                hudManager?.SetTarget(targetQR.visualObject.transform);
            }
            else if (!string.IsNullOrEmpty(poseData))
            {
                try
                {
                    string[] parts = poseData.Split(',');
                    if (parts.Length >= 3)
                    {
                        Vector3 pos = new Vector3(float.Parse(parts[0]), float.Parse(parts[1]), float.Parse(parts[2]));
                        Quaternion rot = Quaternion.identity;
                        if (parts.Length >= 7)
                        {
                            rot = new Quaternion(float.Parse(parts[3]), float.Parse(parts[4]), float.Parse(parts[5]), float.Parse(parts[6]));
                        }
                        Vector3 targetPos = pos + Vector3.up * 0.2f;
                        GameObject target = GameObject.Find("RemoteTargetPointer");
                        if (target == null) target = new GameObject("RemoteTargetPointer");
                        target.transform.SetPositionAndRotation(targetPos, rot);
                        hudManager?.SetTarget(target.transform);
                        statusUI?.ShowMessage(name ?? "Remote Point", $"Payload: {qrCodePayload}");
                    }
                }
                catch { }
            }
        }

        private void OnToggleDetectQR()
        {
            if (qrManager == null) return;
            if (qrManager.IsDetecting)
            {
                qrManager.StopQRCodeDetection();
                detectQRButtonText.text = "Start Detection";
            }
            else
            {
                qrManager.StartQRCodeDetection();
                detectQRButtonText.text = "Stop Detection";
            }
        }

        private void OnClearQRPressed() => qrManager?.ClearQRCodes();

        private void OnPushQRPressed()
        {
            if (qrManager == null || streamingManager == null) return;
            string json = qrManager.GetQRCodeDataAsJson();
            streamingManager.PushQRCodes(json);
            AppendChatMessage("Pushed QR Codes to server.");
        }

        private void OnPullQRPressed()
        {
            streamingManager?.PullQRCodes();
            AppendChatMessage("Requested QR Codes from server.");
        }

        private void OnQRCodesPulled(string json)
        {
            if (qrManager == null) return;
            try
            {
                qrManager.ManualLoadFromJson(json);
                AppendChatMessage("Successfully synced QR Codes from server.");
            }
            catch (System.Exception e)
            {
                AppendChatMessage("Failed to sync QR Codes: " + e.Message);
            }
        }

        private void AppendChatMessage(string msg)
        {
            chatHistory += $"{msg}\n";
            if (chatDisplayText) chatDisplayText.text = chatHistory;
            if (chatScrollRect) Canvas.ForceUpdateCanvases();
            chatScrollRect.verticalNormalizedPosition = 0f;
        }

        private void ShowJoinScreen()
        {
            joinPanel.SetActive(true);
            sessionPanel.SetActive(false);
            if (hudManager) hudManager.ClearHighlight();
        }

        private void ShowSessionScreen()
        {
            joinPanel.SetActive(false);
            sessionPanel.SetActive(true);
        }

        #endregion

        private void OnDestroy()
        {
            if (uiRoot != null) Destroy(uiRoot);
            if (streamingManager != null)
            {
                streamingManager.OnConnected -= OnConnected;
                streamingManager.OnDisconnected -= OnDisconnected;
                streamingManager.OnChatMessageReceived -= OnChatReceived;
                streamingManager.OnPointToReceived -= PointToQRCode;
                streamingManager.OnQRCodesPulled -= OnQRCodesPulled;
            }
            if (qrManager != null)
            {
                qrManager.OnQRCodeAdded -= OnQRCodeAdded;
                qrManager.OnQRCodeUpdated -= OnQRCodeUpdated;
                qrManager.OnQRCodeRemoved -= OnQRCodeRemoved;
            }
        }

        // These are required because we subscribed to QR events above.
        // If you haven't added these methods, add them now:
        private void OnQRCodeAdded(QRCodeManager.QRCodeInstance qr)
        {
            if (qrListItems.ContainsKey(qr.identifierKey)) return;
            var item = Instantiate(qrListItemPrefab, qrListContent);
            item.SetActive(true);
            var textComp = item.GetComponent<Text>();
            textComp.text = $"{qr.fullPayload}\nPos: {qr.lastPosition}";
            qrListItems[qr.identifierKey] = item;
        }

        private void OnQRCodeUpdated(QRCodeManager.QRCodeInstance qr)
        {
            if (qrListItems.TryGetValue(qr.identifierKey, out var item))
            {
                var textComp = item.GetComponent<Text>();
                textComp.text = $"{qr.fullPayload}\nPos: {qr.lastPosition}";
            }
        }

        private void OnQRCodeRemoved(string identifierKey)
        {
            if (qrListItems.TryGetValue(identifierKey, out var item))
            {
                Destroy(item);
                qrListItems.Remove(identifierKey);
            }
        }
    }
}