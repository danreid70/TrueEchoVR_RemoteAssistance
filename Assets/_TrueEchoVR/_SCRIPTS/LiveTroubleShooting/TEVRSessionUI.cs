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
        [SerializeField] private float leftOffset = 0.85f;
        [SerializeField] private float verticalOffset = 0.2f;
        [SerializeField] private float smoothTime = 0.15f;
        [SerializeField] private float rotationSpeed = 3f;

        [Header("Panel Size (world units)")]
        [SerializeField] private float panelWidth = 1.2f;
        [SerializeField] private float panelHeight = 0.9f;
        [SerializeField] private Color bgColor = new Color(0, 0, 0, 0.85f);

        private GameObject uiRoot;
        private Transform uiTransform;
        private Canvas canvas;

        private GameObject joinPanel;
        private GameObject sessionPanel;

        private InputField roomCodeInput;
        private Button joinButton;
        private Text joinStatusText;

        private Text connectionStatusText;
        private RawImage localVideoImage;
        private RawImage remoteVideoImage;
        private Button detectQRButton;
        private Text detectQRButtonText;
        private Button pushQRButton;
        private Button pullQRButton;
        private Button clearQRButton;

        private Transform qrListContent;
        private GameObject qrListItemPrefab;
        private Dictionary<string, GameObject> qrListItems = new Dictionary<string, GameObject>();

        private ScrollRect chatScrollRect;
        private Text chatDisplayText;
        private InputField chatInputField;
        private Button sendButton;
        private Button leaveButton;

        private Transform camTransform;
        private Vector3 velocity = Vector3.zero;
        private Quaternion targetRot;

        private string chatHistory = "";

        // Cache default font to avoid repeated lookups
        private Font defaultFont;

        private void Start()
        {
            if (streamingManager == null) streamingManager = GetComponent<TEVRStreamingManager>();
            if (hudManager == null) hudManager = VRHUDManager.Instance;
            if (qrManager == null) qrManager = GetComponent<QRCodeManager>();
            if (statusUI == null) statusUI = GetComponent<TaskStatusUI>();

            defaultFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (defaultFont == null) defaultFont = Font.CreateDynamicFontFromOSFont("Arial", 14); // fallback

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

            var scaler = uiRoot.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;
            scaler.dynamicPixelsPerUnit = 200f;

            uiRoot.AddComponent<GraphicRaycaster>();

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
            var title = CreateText(joinPanel.transform, "Title", "Live Session", 60, FontStyle.Bold, Color.white);
            title.rectTransform.anchoredPosition = new Vector2(0, panelHeight * 0.35f);

            var inputObj = CreateUIObject("RoomCodeInput", joinPanel.transform);
            var inputRect = inputObj.GetComponent<RectTransform>();
            inputRect.sizeDelta = new Vector2(panelWidth * 0.5f, 0.1f);
            inputRect.anchoredPosition = new Vector2(0, 0.05f);

            roomCodeInput = inputObj.AddComponent<InputField>();
            var inputImage = inputObj.AddComponent<Image>();
            inputImage.color = new Color(0.1f, 0.1f, 0.1f, 0.9f);
            roomCodeInput.targetGraphic = inputImage;

            var inputText = CreateText(inputObj.transform, "Text", "", 40, FontStyle.Normal, Color.white);
            inputText.rectTransform.anchorMin = Vector2.zero;
            inputText.rectTransform.anchorMax = Vector2.one;
            inputText.rectTransform.sizeDelta = new Vector2(-20, 0);
            inputText.alignment = TextAnchor.MiddleCenter;
            roomCodeInput.textComponent = inputText;
            roomCodeInput.characterLimit = 6;

            var phObj = CreateUIObject("Placeholder", inputObj.transform);
            var phText = phObj.AddComponent<Text>();
            phText.text = "Room Code";
            phText.fontSize = 40;
            phText.color = Color.gray;
            phText.alignment = TextAnchor.MiddleCenter;
            phText.rectTransform.anchorMin = Vector2.zero;
            phText.rectTransform.anchorMax = Vector2.one;
            phText.rectTransform.sizeDelta = Vector2.zero;
            roomCodeInput.placeholder = phText;

            var joinBtnObj = CreateUIObject("JoinButton", joinPanel.transform);
            joinBtnObj.GetComponent<RectTransform>().sizeDelta = new Vector2(panelWidth * 0.4f, 0.12f);
            joinBtnObj.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -0.15f);
            joinButton = joinBtnObj.AddComponent<Button>();
            var joinBtnImg = joinBtnObj.AddComponent<Image>();
            joinBtnImg.color = new Color(0.1f, 0.5f, 1f);
            var joinBtnText = CreateText(joinBtnObj.transform, "Text", "JOIN", 45, FontStyle.Bold, Color.white);
            joinBtnText.rectTransform.anchorMin = Vector2.zero;
            joinBtnText.rectTransform.anchorMax = Vector2.one;
            joinBtnText.rectTransform.sizeDelta = Vector2.zero;
            joinButton.onClick.AddListener(OnJoinPressed);

            joinStatusText = CreateText(joinPanel.transform, "Status", "Enter 6-digit room code", 24, FontStyle.Normal, Color.gray);
            joinStatusText.rectTransform.anchoredPosition = new Vector2(0, -0.3f);

            // ========== SESSION SCREEN ==========
            var sessionTitle = CreateText(sessionPanel.transform, "Title", "Live Session", 40, FontStyle.Bold, Color.white);
            sessionTitle.rectTransform.anchorMin = new Vector2(0.5f, 1);
            sessionTitle.rectTransform.anchorMax = new Vector2(0.5f, 1);
            sessionTitle.rectTransform.anchoredPosition = new Vector2(0, -0.05f);

            var videoContainer = CreateUIObject("Videos", sessionPanel.transform);
            var vcRect = videoContainer.GetComponent<RectTransform>();
            vcRect.anchorMin = new Vector2(0.05f, 0.55f);
            vcRect.anchorMax = new Vector2(0.95f, 0.9f);
            vcRect.sizeDelta = Vector2.zero;
            var vcLayout = videoContainer.AddComponent<HorizontalLayoutGroup>();
            vcLayout.spacing = 10;
            vcLayout.childForceExpandWidth = true;

            localVideoImage = CreateVideoDisplay(videoContainer.transform, "LocalVideo", "Preview");
            remoteVideoImage = CreateVideoDisplay(videoContainer.transform, "RemoteVideo", "Web Stream");

            var qrControls = CreateUIObject("QRControls", sessionPanel.transform);
            var qrRect = qrControls.GetComponent<RectTransform>();
            qrRect.anchorMin = new Vector2(0.05f, 0.48f);
            qrRect.anchorMax = new Vector2(0.95f, 0.52f);
            qrRect.sizeDelta = Vector2.zero;
            var qrHlg = qrControls.AddComponent<HorizontalLayoutGroup>();
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

            var qrListHeader = CreateText(sessionPanel.transform, "QRListHeader", "Detected QR Codes:", 22, FontStyle.Bold, Color.white);
            var headerRect = qrListHeader.rectTransform;
            headerRect.anchorMin = new Vector2(0.05f, 0.45f);
            headerRect.anchorMax = new Vector2(0.95f, 0.47f);
            headerRect.anchoredPosition = Vector2.zero;

            var qrListContainer = CreateUIObject("QRListScroll", sessionPanel.transform);
            var qrListRect = qrListContainer.GetComponent<RectTransform>();
            qrListRect.anchorMin = new Vector2(0.05f, 0.25f);
            qrListRect.anchorMax = new Vector2(0.95f, 0.43f);
            qrListRect.sizeDelta = Vector2.zero;
            var qrScroll = qrListContainer.AddComponent<ScrollRect>();
            qrScroll.horizontal = false;
            var qrViewport = CreateUIObject("Viewport", qrListContainer.transform);
            qrViewport.AddComponent<Mask>().showMaskGraphic = false;
            qrViewport.AddComponent<Image>().color = new Color(0,0,0,0.3f);
            qrScroll.viewport = qrViewport.GetComponent<RectTransform>();
            qrListContent = CreateUIObject("Content", qrViewport.transform).transform;
            qrListContent.gameObject.AddComponent<VerticalLayoutGroup>();
            var contentFitter = qrListContent.gameObject.AddComponent<ContentSizeFitter>();
            contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            qrScroll.content = qrListContent.GetComponent<RectTransform>();
            qrScroll.viewport.anchorMin = Vector2.zero;
            qrScroll.viewport.anchorMax = Vector2.one;
            qrScroll.viewport.sizeDelta = Vector2.zero;

            // Create prefab for QR list item
            qrListItemPrefab = new GameObject("QRListItem", typeof(RectTransform));
            qrListItemPrefab.transform.SetParent(qrListContent, false);
            var itemLayout = qrListItemPrefab.AddComponent<LayoutElement>();
            itemLayout.minHeight = 30;
            var itemText = qrListItemPrefab.AddComponent<Text>();
            itemText.font = defaultFont; // Use cached font
            itemText.fontSize = 16;
            itemText.color = Color.white;
            qrListItemPrefab.SetActive(false);

            connectionStatusText = CreateText(sessionPanel.transform, "ConnStatus", "Status: Connecting...", 20, FontStyle.Italic, Color.cyan);
            connectionStatusText.rectTransform.anchorMin = new Vector2(0, 1);
            connectionStatusText.rectTransform.anchorMax = new Vector2(0, 1);
            connectionStatusText.rectTransform.pivot = new Vector2(0, 1);
            connectionStatusText.rectTransform.anchoredPosition = new Vector2(0.05f, -0.02f);

            var chatContainer = CreateUIObject("Chat", sessionPanel.transform);
            var chatRect = chatContainer.GetComponent<RectTransform>();
            chatRect.anchorMin = new Vector2(0.05f, 0.08f);
            chatRect.anchorMax = new Vector2(0.95f, 0.23f);
            chatRect.sizeDelta = Vector2.zero;

            var srObj = CreateUIObject("Scroll", chatContainer.transform);
            var srRect = srObj.GetComponent<RectTransform>();
            srRect.anchorMin = Vector2.zero;
            srRect.anchorMax = new Vector2(1, 0.7f);
            srRect.sizeDelta = Vector2.zero;
            srObj.AddComponent<Image>().color = new Color(0, 0, 0, 0.3f);
            chatScrollRect = srObj.AddComponent<ScrollRect>();

            var vp = CreateUIObject("Viewport", srObj.transform);
            vp.GetComponent<RectTransform>().anchorMin = Vector2.zero;
            vp.GetComponent<RectTransform>().anchorMax = Vector2.one;
            vp.GetComponent<RectTransform>().sizeDelta = Vector2.zero;
            vp.AddComponent<Mask>().showMaskGraphic = false;
            vp.AddComponent<Image>();

            var content = CreateUIObject("Content", vp.transform);
            var contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0, 1);
            contentRect.anchorMax = new Vector2(1, 1);
            contentRect.pivot = new Vector2(0.5f, 1);
            content.AddComponent<VerticalLayoutGroup>().childControlHeight = true;
            content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            
            chatDisplayText = CreateText(content.transform, "Display", "", 20, FontStyle.Normal, Color.white);
            chatDisplayText.alignment = TextAnchor.UpperLeft;
            chatDisplayText.rectTransform.sizeDelta = new Vector2(0, 100);

            chatScrollRect.viewport = vp.GetComponent<RectTransform>();
            chatScrollRect.content = contentRect;

            var inputRow = CreateUIObject("InputRow", chatContainer.transform);
            var rowRect = inputRow.GetComponent<RectTransform>();
            rowRect.anchorMin = new Vector2(0, 0);
            rowRect.anchorMax = new Vector2(1, 0.25f);
            rowRect.sizeDelta = Vector2.zero;
            inputRow.AddComponent<HorizontalLayoutGroup>().spacing = 5;

            var chatInpObj = CreateUIObject("ChatInput", inputRow.transform);
            chatInputField = chatInpObj.AddComponent<InputField>();
            chatInpObj.AddComponent<Image>().color = new Color(1,1,1,0.1f);
            var chatInpText = CreateText(chatInpObj.transform, "Text", "", 20, FontStyle.Normal, Color.white);
            chatInpText.rectTransform.anchorMin = Vector2.zero;
            chatInpText.rectTransform.anchorMax = Vector2.one;
            chatInpText.rectTransform.sizeDelta = new Vector2(-10, 0);
            chatInpText.alignment = TextAnchor.MiddleLeft;
            chatInputField.textComponent = chatInpText;
            chatInpObj.AddComponent<LayoutElement>().flexibleWidth = 1;

            sendButton = CreateButton(inputRow.transform, "Send", "Send", new Color(0.2f, 0.5f, 0.8f));
            sendButton.GetComponent<LayoutElement>().preferredWidth = 0.2f;
            sendButton.onClick.AddListener(OnSendChat);

            leaveButton = CreateButton(sessionPanel.transform, "Leave", "LEAVE SESSION", new Color(0.6f, 0.2f, 0.2f));
            var leaveRect = leaveButton.GetComponent<RectTransform>();
            leaveRect.anchorMin = new Vector2(0.5f, 0);
            leaveRect.anchorMax = new Vector2(0.5f, 0);
            leaveRect.sizeDelta = new Vector2(panelWidth * 0.4f, 0.08f);
            leaveRect.anchoredPosition = new Vector2(0, 0.04f);
            leaveButton.onClick.AddListener(OnLeaveSession);
        }

        private RawImage CreateVideoDisplay(Transform parent, string name, string label)
        {
            var obj = CreateUIObject(name, parent);
            var img = obj.AddComponent<RawImage>();
            img.color = Color.black;
            var lbl = CreateText(obj.transform, "Label", label, 14, FontStyle.Normal, new Color(1, 1, 1, 0.7f));
            lbl.rectTransform.anchoredPosition = new Vector2(0, -0.08f);
            return img;
        }

        private Button CreateButton(Transform parent, string name, string label, Color color)
        {
            var obj = CreateUIObject(name, parent);
            var btn = obj.AddComponent<Button>();
            var img = obj.AddComponent<Image>();
            img.color = color;
            var txt = CreateText(obj.transform, "Text", label, 20, FontStyle.Bold, Color.white);
            txt.rectTransform.anchorMin = Vector2.zero;
            txt.rectTransform.anchorMax = Vector2.one;
            txt.rectTransform.sizeDelta = Vector2.zero;
            return btn;
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

            if (qrManager != null)
            {
                qrManager.OnQRCodeAdded += OnQRCodeAdded;
                qrManager.OnQRCodeUpdated += OnQRCodeUpdated;
                qrManager.OnQRCodeRemoved += OnQRCodeRemoved;
                foreach (var item in qrManager.TrackedQRCodes)
                    OnQRCodeAdded(item.Value);
            }
        }

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
                if (hudManager != null)
                {
                    hudManager.SetTarget(targetQR.visualObject.transform);
                }
            }
            else
            {
                if (!string.IsNullOrEmpty(poseData))
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
    }
}