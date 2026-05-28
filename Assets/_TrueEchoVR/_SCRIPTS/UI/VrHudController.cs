using UnityEngine;
using TMPro;
using System.Collections;

namespace TEVR
{
    /// <summary>
    /// Manages the world-space HUD that follows the user and displays training tasks, hints, and feedback.
    /// Features lazy-following, auto-fading, and target highlighting.
    /// </summary>
    /// <summary>
    /// Manages the world-space HUD that follows the user and displays training tasks, hints, and feedback.
    /// Features lazy-following, auto-fading, and target highlighting.
    /// </summary>
    public class VrHudController : MonoBehaviour
    {
        [Header("UI References")]
        [Tooltip("The root object of the HUD to be positioned.")]
        public GameObject hudPanel;
        public TMP_Text statusText;
        public TMP_Text hintText;
        public TMP_Text completionText;
        
        [Tooltip("Visual indicator that points toward the current task objective.")]
        public GameObject pointerArrow;

        [Header("Following & Positioning")]
        [SerializeField] private float forwardDistance = 1.5f;
        [SerializeField] private float horizontalOffset = 0f;
        [SerializeField] private float verticalOffset = 0.3f;
        [SerializeField] private float smoothTime = 0.15f;
        [SerializeField] private float rotationSpeed = 3f;
        
        [Tooltip("Angle threshold before the HUD starts catching up to the user's view.")]
        [SerializeField] private float angleThreshold = 30f;
        [SerializeField] private float distanceThreshold = 0.2f;

        [Header("Fading Settings")]
        [SerializeField] private float fadeDelay = 2f;
        [SerializeField] private float fadeDuration = 0.5f;

        private Transform _camTransform;
        private CanvasGroup _canvasGroup;
        private Coroutine _fadeCoroutine;
        private bool _hasActiveText = false;
        private bool _isPersistent = false;
        private Transform _currentTarget;
        private Vector3 _lastCameraPos;
        private Quaternion _lastCameraRot;
        private bool _isFollowing = true;
        private Vector3 _velocity = Vector3.zero;
        private Transform _panelTransform;
        private SessionUiController _uiManager;

        private void Start()
        {
            _camTransform = Camera.main?.transform;
            if (_camTransform == null)
            {
                Debug.LogWarning("[VrHudController] No main camera found at Start. This is common in VR/MR setups; will attempt to find camera during updates.");
            }
            else
            {
                _lastCameraPos = _camTransform.position;
                _lastCameraRot = _camTransform.rotation;
            }

            if (hudPanel == null)
            {
                Debug.LogError("[VrHudController] No hudPanel assigned.", this);
                enabled = false;
                return;
            }

            _panelTransform = hudPanel.transform;

            // Target the CanvasGroup on the panel itself for correct fading
            _canvasGroup = hudPanel.GetComponent<CanvasGroup>();
            if (_canvasGroup == null)
            {
                _canvasGroup = hudPanel.AddComponent<CanvasGroup>();
            }

            // Initialization state
            if (statusText != null) statusText.gameObject.SetActive(false);
            if (hintText != null) hintText.gameObject.SetActive(false);
            if (completionText != null) completionText.gameObject.SetActive(false);
            if (pointerArrow != null) pointerArrow.SetActive(false);
            
            // If the camera is found, jump to it immediately
            if (_camTransform != null)
            {
                Vector3 startPos = ComputeTargetPosition();
                Quaternion startRot = GetFaceCameraRotation();
                _panelTransform.SetPositionAndRotation(startPos, startRot);
                _lastCameraPos = _camTransform.position;
                _lastCameraRot = _camTransform.rotation;
            }

            // Do not hide the panel if we are in the middle of a persistent message (e.g. from Init)
            // Or force it active for now to ensure visibility
            hudPanel.SetActive(true);
            _canvasGroup.alpha = 1f;

            _isFollowing = true;
            _uiManager = Object.FindAnyObjectByType<SessionUiController>();
        }

        private void LateUpdate()
        {
            if (hudPanel == null) return;

            // Late-binding camera discovery for VR rigs
            if (_camTransform == null)
            {
                _camTransform = Camera.main?.transform;
                if (_camTransform == null) return;
            }

            // Determine if the user has moved or looked away enough to trigger following
            float angle = Quaternion.Angle(_lastCameraRot, _camTransform.rotation);
            float distance = Vector3.Distance(_lastCameraPos, _camTransform.position);
            
            if (angle > angleThreshold || distance > distanceThreshold)
            {
                _isFollowing = true;
                _lastCameraPos = _camTransform.position;
                _lastCameraRot = _camTransform.rotation;
            }

            if (_isFollowing)
            {
                ApplyLazyFollow();
            }

            UpdatePointerArrow();
        }

        private void ApplyLazyFollow()
        {
            Vector3 targetPos = ComputeTargetPosition();
            
            // Safety check for NaN
            if (!float.IsFinite(targetPos.x) || !float.IsFinite(targetPos.y) || !float.IsFinite(targetPos.z))
            {
                return;
            }

            _panelTransform.position = Vector3.SmoothDamp(_panelTransform.position, targetPos, ref _velocity, smoothTime);
            
            Quaternion targetRot = GetFaceCameraRotation();
            if (float.IsFinite(targetRot.x))
            {
                _panelTransform.rotation = Quaternion.Slerp(_panelTransform.rotation, targetRot, rotationSpeed * Time.deltaTime);
            }

            if (Vector3.Distance(_panelTransform.position, targetPos) < 0.01f &&
                Quaternion.Angle(_panelTransform.rotation, targetRot) < 0.5f)
            {
                _isFollowing = false;
                _panelTransform.SetPositionAndRotation(targetPos, targetRot);
            }
        }

        private void UpdatePointerArrow()
        {
            if (pointerArrow == null) return;

            if (_currentTarget != null && hudPanel.activeSelf)
            {
                Vector3 toTarget = _currentTarget.position - pointerArrow.transform.position;
                if (toTarget.sqrMagnitude > 0.001f)
                {
                    pointerArrow.transform.rotation = Quaternion.LookRotation(toTarget, Vector3.up);
                }
                if (!pointerArrow.activeSelf) pointerArrow.SetActive(true);
            }
            else if (pointerArrow.activeSelf)
            {
                pointerArrow.SetActive(false);
            }
        }

        private Vector3 ComputeTargetPosition()
        {
            // Use a flattened camera coordinate system to prevent 'twisting' when the head rolls/tilts
            Vector3 flatForward = _camTransform.forward;
            flatForward.y = 0;
            if (flatForward.sqrMagnitude < 0.001f) flatForward = Vector3.ProjectOnPlane(_camTransform.up, Vector3.up).normalized;
            else flatForward.Normalize();

            Vector3 flatRight = Vector3.Cross(Vector3.up, flatForward);

            return _camTransform.position
                   + flatForward * forwardDistance
                   + flatRight * horizontalOffset
                   + Vector3.up * verticalOffset;
        }

        private Quaternion GetFaceCameraRotation()
        {
            // Calculate a Yaw-only look rotation (looking at the user's horizontal position)
            Vector3 directionToCamera = _camTransform.position - _panelTransform.position;
            directionToCamera.y = 0;
            
            if (directionToCamera.sqrMagnitude < 0.001f)
            {
                // Fallback: face in the same direction as the camera's flattened forward
                Vector3 camForward = _camTransform.forward;
                camForward.y = 0;
                if (camForward.sqrMagnitude < 0.001f) return _panelTransform.rotation;
                return Quaternion.LookRotation(camForward, Vector3.up);
            }
            
            // We want the panel to FACE the camera, so we use -directionToCamera
            return Quaternion.LookRotation(-directionToCamera, Vector3.up);
        }

        /// <summary>
        /// Displays a specific instruction message and hint on the HUD.
        /// </summary>
        public void ShowMessage(string mainText, string hint, bool persistent = false)
        {
            _hasActiveText = !string.IsNullOrEmpty(mainText) || !string.IsNullOrEmpty(hint);
            _isPersistent = persistent;

            if (!_hasActiveText)
            {
                StartFadeOut();
                return;
            }

            // Sync with central log
            if (_uiManager != null) _uiManager.AppendChatMessage($"<color=orange>[HUD]</color> {mainText} {hint}");

            if (!hudPanel.activeSelf) hudPanel.SetActive(true);

            if (statusText != null)
            {
                statusText.gameObject.SetActive(true);
                statusText.text = mainText ?? "";
            }
            if (hintText != null)
            {
                hintText.gameObject.SetActive(true);
                hintText.text = hint ?? "";
            }
            if (completionText != null) completionText.gameObject.SetActive(false);

            SetAlpha(1f);
            StopActiveFade();
            
            if (!persistent && fadeDelay > 0) 
            {
                _fadeCoroutine = StartCoroutine(FadeAfterDelay(0f));
            }
        }

        /// <summary>
        /// Displays a specialized completion message.
        /// </summary>
        public void ShowCompletionMessage(string message, bool persistent = false)
        {
            _hasActiveText = true;
            _isPersistent = persistent;

            if (_uiManager != null) _uiManager.AppendChatMessage($"<color=green>[HUD]</color> {message}");

            if (!hudPanel.activeSelf) hudPanel.SetActive(true);

            if (statusText != null) statusText.gameObject.SetActive(false);
            if (hintText != null) hintText.gameObject.SetActive(false);
            if (completionText != null)
            {
                completionText.gameObject.SetActive(true);
                completionText.text = message ?? "";
            }

            SetAlpha(1f);
            StopActiveFade();
            
            if (!persistent && fadeDelay > 0)
            {
                _fadeCoroutine = StartCoroutine(FadeAfterDelay(0f));
            }
        }

        public void HighlightTarget(Transform target) => _currentTarget = target;
        public void ClearHighlight() => _currentTarget = null;

        private void StartFadeOut()
        {
            _isPersistent = false;
            StopActiveFade();
            _fadeCoroutine = StartCoroutine(FadeAlphaTo(0f, fadeDuration));
        }

        private void StopActiveFade()
        {
            if (_fadeCoroutine != null)
            {
                StopCoroutine(_fadeCoroutine);
                _fadeCoroutine = null;
            }
        }

        private IEnumerator FadeAfterDelay(float targetAlpha)
        {
            yield return new WaitForSeconds(fadeDelay);
            yield return FadeAlphaTo(targetAlpha, fadeDuration);
        }

        private IEnumerator FadeAlphaTo(float targetAlpha, float duration)
        {
            if (_canvasGroup == null) yield break;
            
            float startAlpha = _canvasGroup.alpha;
            float elapsed = 0;
            
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                _canvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, elapsed / duration);
                yield return null;
            }
            
            _canvasGroup.alpha = targetAlpha;

            if (targetAlpha <= 0.01f && !_hasActiveText && hudPanel != null)
            {
                hudPanel.SetActive(false);
            }
            _fadeCoroutine = null;
        }

        private void SetAlpha(float alpha)
        {
            if (_canvasGroup != null) _canvasGroup.alpha = alpha;
        }
    }
}