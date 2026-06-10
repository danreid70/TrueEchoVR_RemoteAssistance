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

        // ---- Faint green dashed line: arrow -> target (R5-G) ----
        private LineRenderer _dashLine;
        private Material _dashMaterial;
        private Texture2D _dashTexture;
        private const float DashTilesPerUnit = 18f;   // higher = shorter dashes
        private static readonly Color DashColor = new Color(0.10f, 1f, 0.35f, 0.35f); // faint green

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
                UpdateDashLine();
            }
            else
            {
                if (pointerArrow.activeSelf) pointerArrow.SetActive(false);
                if (_dashLine != null && _dashLine.enabled) _dashLine.enabled = false;
            }
        }

        /// <summary>
        /// Draws a faint green dashed line from the OUTER EDGE of the arrow to the OUTER EDGE of the target,
        /// so it is obvious what is being pointed at without the line burying into either object's center.
        /// Endpoints are trimmed by each object's half-extent measured along the connecting direction.
        /// </summary>
        private void UpdateDashLine()
        {
            EnsureDashLine();
            if (_dashLine == null) return;

            Vector3 a = pointerArrow.transform.position;
            Vector3 b = _currentTarget.position;
            Vector3 delta = b - a;
            float dist = delta.magnitude;
            if (dist < 0.0005f) { _dashLine.enabled = false; return; }
            Vector3 dir = delta / dist;

            // Start at the arrow's outer edge, end at the target's outer edge (half-bounds along dir).
            float aRadius = RadiusAlong(pointerArrow.transform, dir);
            float bRadius = RadiusAlong(_currentTarget, dir);
            Vector3 start = a + dir * aRadius;
            Vector3 end = b - dir * bRadius;

            // If the two objects overlap (trim consumed the whole span), hide the line.
            if (Vector3.Dot(end - start, dir) <= 0f) { _dashLine.enabled = false; return; }

            _dashLine.enabled = true;
            _dashLine.SetPosition(0, start);
            _dashLine.SetPosition(1, end);

            // Keep dash length roughly constant in world space regardless of distance (Tile texture mode
            // repeats per unit length, multiplied by the material tiling).
            float span = (end - start).magnitude;
            if (_dashMaterial != null)
                _dashMaterial.mainTextureScale = new Vector2(span * DashTilesPerUnit, 1f);
        }

        private void EnsureDashLine()
        {
            if (_dashLine != null) return;

            var go = new GameObject("PointAtDashLine");
            go.transform.SetParent(transform, false);
            _dashLine = go.AddComponent<LineRenderer>();
            _dashLine.useWorldSpace = true;
            _dashLine.positionCount = 2;
            _dashLine.numCapVertices = 0;
            _dashLine.alignment = LineAlignment.View;
            _dashLine.textureMode = LineTextureMode.Tile;
            _dashLine.widthMultiplier = 0.004f;   // thin, faint
            _dashLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _dashLine.receiveShadows = false;

            // Dash texture: half opaque, half transparent (repeats along the line via Tile mode).
            _dashTexture = new Texture2D(8, 1, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point };
            var px = new Color[8];
            for (int i = 0; i < 8; i++) px[i] = i < 4 ? Color.white : new Color(1, 1, 1, 0);
            _dashTexture.SetPixels(px);
            _dashTexture.Apply();

            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Transparent");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            _dashMaterial = new Material(sh) { name = "PointAtDash_Mat" };
            if (_dashMaterial.HasProperty("_BaseMap")) _dashMaterial.SetTexture("_BaseMap", _dashTexture);
            _dashMaterial.mainTexture = _dashTexture;
            if (_dashMaterial.HasProperty("_BaseColor")) _dashMaterial.SetColor("_BaseColor", DashColor);
            _dashMaterial.color = DashColor;
            // Transparent surface setup (URP/Lit-style props are harmless if absent).
            if (_dashMaterial.HasProperty("_Surface")) _dashMaterial.SetFloat("_Surface", 1f);
            if (_dashMaterial.HasProperty("_SrcBlend")) _dashMaterial.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (_dashMaterial.HasProperty("_DstBlend")) _dashMaterial.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (_dashMaterial.HasProperty("_ZWrite")) _dashMaterial.SetFloat("_ZWrite", 0f);
            _dashMaterial.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            _dashLine.material = _dashMaterial;
            _dashLine.enabled = false;
        }

        /// <summary>Distance from a transform's combined renderer-bounds center to its surface along a world
        /// direction (i.e. the half-extent along that axis). Falls back to a small default if no renderers.</summary>
        private static float RadiusAlong(Transform t, Vector3 worldDir)
        {
            if (t == null) return 0.02f;
            var renderers = t.GetComponentsInChildren<Renderer>();
            if (renderers == null || renderers.Length == 0) return 0.02f;
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            Vector3 e = bounds.extents;
            // Distance to AABB surface along the (normalized) direction.
            return Mathf.Abs(e.x * worldDir.x) + Mathf.Abs(e.y * worldDir.y) + Mathf.Abs(e.z * worldDir.z);
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