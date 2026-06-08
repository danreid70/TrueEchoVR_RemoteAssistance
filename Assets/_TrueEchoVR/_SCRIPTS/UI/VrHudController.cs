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

        [Header("Fading Settings")]
        [SerializeField] private float fadeDelay = 2f;
        [SerializeField] private float fadeDuration = 0.5f;

        private CanvasGroup _canvasGroup;
        private Coroutine _fadeCoroutine;
        private bool _hasActiveText = false;
        private bool _isPersistent = false;
        private Transform _currentTarget;

        private void Start()
        {
            // Auto-discovery for Bootstrap/Prefab pattern
            if (hudPanel == null)
            {
                var foundPanel = GameObject.Find("HUDPanel");
                if (foundPanel != null) hudPanel = foundPanel;
                else if (UIManager.Instance != null && UIManager.Instance.hudGroup.root != null)
                    hudPanel = UIManager.Instance.hudGroup.root;
            }

            if (hudPanel == null)
            {
                Debug.LogWarning("[VrHudController] No hudPanel assigned. Waiting for UIManager.", this);
                // We'll retry in LateUpdate or rely on state changes
                return;
            }

            _canvasGroup = hudPanel.GetComponent<CanvasGroup>();
            if (_canvasGroup == null)
            {
                _canvasGroup = hudPanel.AddComponent<CanvasGroup>();
            }

            if (pointerArrow != null) pointerArrow.SetActive(false);

            // Subscribe now that all Awakes have run (covers the case where UIManager.Instance was null
            // during OnEnable), then set the initial active state.
            SubscribeToUIManager();
            if (UIManager.Instance != null)
                HandleUIStateChanged(UIManager.Instance.GetCurrentState());
        }

        private bool _subscribedToUIManager = false;

        /// <summary>Idempotently subscribes to UIManager state changes (safe from OnEnable and Start).</summary>
        private void SubscribeToUIManager()
        {
            if (_subscribedToUIManager || UIManager.Instance == null) return;
            UIManager.Instance.OnUIStateChanged += HandleUIStateChanged;
            _subscribedToUIManager = true;
        }

        private void OnEnable()
        {
            SubscribeToUIManager();
        }

        private void OnDisable()
        {
            if (_subscribedToUIManager && UIManager.Instance != null)
                UIManager.Instance.OnUIStateChanged -= HandleUIStateChanged;
            _subscribedToUIManager = false;
        }

        private void HandleUIStateChanged(UIManager.UIState newState)
        {
            // HUD is usually active during Session and Calibration
            bool shouldBeVisible = (newState == UIManager.UIState.Session || newState == UIManager.UIState.Calibration);
            if (hudPanel != null) hudPanel.SetActive(shouldBeVisible);
        }

        private void LateUpdate()
        {
            if (hudPanel == null) return;

            UpdatePointerArrow();
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
            UIManager.Instance?.AppendChatMessage($"<color=orange>[HUD]</color> {mainText} {hint}");

            // FIX: Don't show HUD if we are in Login or None states.
            if (UIManager.Instance != null)
            {
                var state = UIManager.Instance.GetCurrentState();
                if (state == UIManager.UIState.Login || state == UIManager.UIState.None)
                {
                    Debug.Log("[HUD] Suppressing message while in Login/None state.");
                    return;
                }
            }

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

            UIManager.Instance?.AppendChatMessage($"<color=green>[HUD]</color> {message}");

            // FIX: Don't show HUD if we are in Login or None states.
            if (UIManager.Instance != null)
            {
                var state = UIManager.Instance.GetCurrentState();
                if (state == UIManager.UIState.Login || state == UIManager.UIState.None)
                {
                    return;
                }
            }

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