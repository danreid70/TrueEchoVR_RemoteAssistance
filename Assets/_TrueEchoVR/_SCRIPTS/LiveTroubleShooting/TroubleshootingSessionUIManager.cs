using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

namespace TrueEchoVR
{
    public class TroubleshootingSessionUIManager : MonoBehaviour
    {
        [Header("UI References (assign in Inspector)")]
        public GameObject sessionUIPanel;
        public GameObject joinPanel;
        public GameObject sessionPanel;

        public TMP_InputField roomCodeInput;
        public Button joinButton;
        public TMP_Text joinStatusText;

        public TMP_Text connectionStatusText;
        public RawImage localVideoImage;
        public RawImage remoteVideoImage;
        public Button toggleDetectionButton;
        public TMP_Text toggleDetectionButtonText;
        public Button pushQRButton;
        public Button pullQRButton;
        public Button clearQRButton;
        public TMP_InputField locationIdInput;

        public TMP_Dropdown qrCodeDropdown;

        public Transform qrListContent;
        public GameObject qrListItemPrefab;

        public ScrollRect chatScrollRect;
        public TMP_Text chatDisplayText;
        public TMP_InputField chatInputField;
        public Button sendButton;
        public Button leaveButton;

        [Header("Positioning (relative to camera)")]
        [SerializeField] private float forwardDistance = 1.6f;
        [SerializeField] private float rightOffset = 0.85f;
        [SerializeField] private float verticalOffset = 0.2f;
        [SerializeField] private float smoothTime = 0.15f;
        [SerializeField] private float rotationSpeed = 3f;
        [SerializeField] private float angleThreshold = 30f;
        [SerializeField] private float distanceThreshold = 0.2f;

        public TroubleshootingStreamingManager streamingManager;
        public QRCodeManager qrManager;
        public MainVRHUDUI statusUI;
        public TroubleshootingSessionInitialization sessionInit;

        private Transform camTransform;
        private Vector3 velocity = Vector3.zero;
        private Quaternion targetRot;
        private Vector3 lastCameraPos;
        private Quaternion lastCameraRot;
        private bool isFollowing = true;
        private string chatHistory = "";
        private Dictionary<string, GameObject> qrListItems = new Dictionary<string, GameObject>();
        private List<QRCodeManager.QRCodeInstance> qrCodeList = new List<QRCodeManager.QRCodeInstance>();

        private void Start()
        {
            if (streamingManager == null) streamingManager = GetComponent<TroubleshootingStreamingManager>();
            if (qrManager == null) qrManager = GetComponent<QRCodeManager>();
            if (statusUI == null) statusUI = GetComponent<MainVRHUDUI>();
            if (sessionInit == null) sessionInit = GetComponent<TroubleshootingSessionInitialization>();

            camTransform = Camera.main?.transform;
            if (camTransform == null)
            {
                Debug.LogError("[TroubleshootingSessionUIManager] No main camera found.");
                enabled = false;
                return;
            }

            if (joinButton != null) joinButton.onClick.AddListener(OnJoinPressed);
            if (sendButton != null) sendButton.onClick.AddListener(OnSendChat);
            if (leaveButton != null) leaveButton.onClick.AddListener(OnLeaveSession);
            if (toggleDetectionButton != null) toggleDetectionButton.onClick.AddListener(OnToggleDetectQR);
            if (clearQRButton != null) clearQRButton.onClick.AddListener(OnClearQRPressed);
            if (pushQRButton != null) pushQRButton.onClick.AddListener(OnPushQRPressed);
            if (pullQRButton != null) pullQRButton.onClick.AddListener(OnPullQRPressed);
            if (qrCodeDropdown != null) qrCodeDropdown.onValueChanged.AddListener(OnQRCodeSelected);

            if (streamingManager != null)
            {
                streamingManager.OnConnected += OnConnected;
                streamingManager.OnDisconnected += OnDisconnected;
                streamingManager.OnChatMessageReceived += OnChatReceived;
                streamingManager.OnRemoteStreamStarted += (tex) => { if (remoteVideoImage != null) remoteVideoImage.texture = tex; };
                streamingManager.OnLocalStreamStarted += (tex) => { if (localVideoImage != null) localVideoImage.texture = tex; };
                streamingManager.OnQRCodesPulled += OnQRCodesPulled;
            }

            transform.position = ComputeTargetPosition();
            transform.rotation = ComputeTargetRotation();
            lastCameraPos = camTransform.position;
            lastCameraRot = camTransform.rotation;
            isFollowing = false;
        }

        private void LateUpdate()
        {
            if (camTransform == null || sessionUIPanel == null) return;
            if (!sessionInit.InitializationComplete) return;

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
                transform.position = Vector3.SmoothDamp(transform.position, targetPos, ref velocity, smoothTime);
                targetRot = ComputeTargetRotation();
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * Time.deltaTime);

                if (Vector3.Distance(transform.position, targetPos) < 0.01f &&
                    Quaternion.Angle(transform.rotation, targetRot) < 0.5f)
                {
                    isFollowing = false;
                    transform.position = targetPos;
                    transform.rotation = targetRot;
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
            Vector3 toCam = camTransform.position - transform.position;
            return Quaternion.LookRotation(-toCam, Vector3.up);
        }

        private void OnJoinPressed()
        {
            if (!sessionInit.InitializationComplete)
            {
                if (joinStatusText != null) joinStatusText.text = "Initialization in progress...";
                return;
            }
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
            ShowJoinScreen();
        }

        private void OnConnected()
        {
            if (connectionStatusText != null) connectionStatusText.text = "Status: LIVE";
            AppendChatMessage("--- Connected ---");
        }

        private void OnDisconnected()
        {
            ShowJoinScreen();
        }

        private void OnChatReceived(string msg) => AppendChatMessage($"Admin: {msg}");

        private void OnToggleDetectQR()
        {
            if (qrManager == null) return;
            if (qrManager.IsDetecting)
            {
                qrManager.StopQRCodeDetection();
                if (toggleDetectionButtonText != null) toggleDetectionButtonText.text = "Start Detection";
            }
            else
            {
                qrManager.StartQRCodeDetection();
                if (toggleDetectionButtonText != null) toggleDetectionButtonText.text = "Stop Detection";
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
                sessionInit.GenerateQRGameObjects();
            }
            catch (System.Exception e)
            {
                AppendChatMessage("Failed to sync QR Codes: " + e.Message);
            }
        }

        private void OnQRCodeSelected(int index)
        {
            if (!sessionInit.InitializationComplete) return;
            if (index < 0 || index >= qrCodeList.Count) return;
            var selectedQR = qrCodeList[index];
            if (selectedQR != null)
            {
                string displayName = !string.IsNullOrEmpty(selectedQR.identifierKey) ? selectedQR.identifierKey : selectedQR.fullPayload;
                statusUI?.ShowMessage($"Selected QR: {displayName}", $"Payload: {selectedQR.fullPayload}");
                sessionInit.PointToQRCode(selectedQR);
            }
        }

        private void AppendChatMessage(string msg)
        {
            chatHistory += $"{msg}\n";
            if (chatDisplayText != null) chatDisplayText.text = chatHistory;
            if (chatScrollRect != null)
            {
                Canvas.ForceUpdateCanvases();
                chatScrollRect.verticalNormalizedPosition = 0f;
            }
        }

        public void ShowJoinScreen()
        {
            if (joinPanel != null) joinPanel.SetActive(true);
            if (sessionPanel != null) sessionPanel.SetActive(false);
            if (statusUI != null) statusUI.ClearHighlight();
        }

        public void ShowSessionScreen()
        {
            if (joinPanel != null) joinPanel.SetActive(false);
            if (sessionPanel != null) sessionPanel.SetActive(true);
        }

        public void RefreshQRCodeDropdown()
        {
            if (qrCodeDropdown == null) return;
            qrCodeDropdown.ClearOptions();
            List<TMP_Dropdown.OptionData> options = new List<TMP_Dropdown.OptionData>();
            qrCodeList.Clear();
            foreach (var kvp in qrManager.TrackedQRCodes)
            {
                if (kvp.Value.fullPayload.Contains("RoomAnchor")) continue;
                QRCodeManager.QRCodeInstance qr = kvp.Value;
                qrCodeList.Add(qr);
                string displayName = !string.IsNullOrEmpty(qr.identifierKey) ? qr.identifierKey : qr.fullPayload;
                if (displayName.Length > 30) displayName = displayName.Substring(0, 27) + "...";
                options.Add(new TMP_Dropdown.OptionData(displayName));
            }
            qrCodeDropdown.AddOptions(options);
        }

        public void AddQRListItem(QRCodeManager.QRCodeInstance qr)
        {
            if (qrListContent == null || qrListItemPrefab == null) return;
            if (qrListItems.ContainsKey(qr.identifierKey)) return;
            var item = Instantiate(qrListItemPrefab, qrListContent);
            item.SetActive(true);
            var textComp = item.GetComponent<TMP_Text>();
            if (textComp != null)
                textComp.text = $"{qr.fullPayload}\nPos: {qr.lastPosition}";
            qrListItems[qr.identifierKey] = item;
        }

        public void UpdateQRListItem(QRCodeManager.QRCodeInstance qr)
        {
            if (qrListItems.TryGetValue(qr.identifierKey, out var item))
            {
                var textComp = item.GetComponent<TMP_Text>();
                if (textComp != null)
                    textComp.text = $"{qr.fullPayload}\nPos: {qr.lastPosition}";
            }
        }

        public void RemoveQRListItem(string identifierKey)
        {
            if (qrListItems.TryGetValue(identifierKey, out var item))
            {
                Destroy(item);
                qrListItems.Remove(identifierKey);
            }
        }

        private void OnDestroy()
        {
            if (streamingManager != null)
            {
                streamingManager.OnConnected -= OnConnected;
                streamingManager.OnDisconnected -= OnDisconnected;
                streamingManager.OnChatMessageReceived -= OnChatReceived;
                streamingManager.OnQRCodesPulled -= OnQRCodesPulled;
            }
        }
    }
}