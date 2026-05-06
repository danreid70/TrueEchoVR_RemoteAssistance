using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

namespace TrueEchoVR
{
    [ExecuteAlways]
    public class VRHUDManager : MonoBehaviour
    {
        public static VRHUDManager Instance { get; private set; }

    [Header("World Scale")]
    [SerializeField] private float worldScaleMultiplier = 1.0f;

    [Header("Position & Follow")]
    [SerializeField] private float followDistance = 1.5f;
    [SerializeField] private float verticalOffset = 0.3f;
    [SerializeField] private float positionSmoothTime = 0.15f;
    [SerializeField] private float rotationSmoothSpeed = 3.0f;

    [Header("Panel Size (UI Pixels)")]
    public float panelWidth = 450f;
    public float panelHeight = 300f;
    public float panelPadding = 24f;

    [Header("Background & Border")]
    public Color backgroundColor = new Color(0, 0, 0, 0.75f);
    public float borderRadius = 16f;
    public float borderWidth = 1f;
    public Color borderColor = new Color(1, 1, 1, 0.2f);

    [Header("Status Text")]
    public float statusFontSize = 20f;
    public Color statusColor = Color.white;
    public float statusBottomMargin = 10f;

    [Header("Hint Text")]
    public float hintFontSize = 14f;
    public Color hintColor = new Color(0.8f, 0.8f, 0.8f);

    [Header("Completion Text")]
    public float completionFontSize = 18f;
    public Color completionColor = new Color(0, 1, 0.53f);

    [Header("Auto‑Fade")]
    [SerializeField] private float fadeDelay = 0f;
    [SerializeField] private float fadeDuration = 0.5f;

    [Header("Pointer")]
    [SerializeField] private GameObject pointerPrefab;
    [SerializeField] private Vector3 pointerOffset = new Vector3(0, 0, -0.1f);

    private GameObject hudObject;
    private UIDocument uiDocument;
    private Label statusLabel;
    private Label hintLabel;
    private Label completionLabel;

    private string lastStatusText = "";
    private string lastHintText = "";
    private bool isCompletionShowing = false;
    private string lastCompletionMessage = "";

    private Transform cameraTransform;
    private Vector3 velocity = Vector3.zero;
    private Quaternion targetRotation;

    private Coroutine fadeCoroutine;
    private float targetOpacity = 1f;

    private GameObject currentPointer;
    private Transform currentTarget;

    private void Awake()
    {
        if (Application.isPlaying)
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            CreateHUD();
        }

        cameraTransform = Camera.main?.transform;
        if (cameraTransform == null && Application.isPlaying)
            Debug.LogError("[VRHUDManager] No main camera found.");
    }

    private IEnumerator Start()
    {
        yield return null;
        if (cameraTransform == null)
            cameraTransform = Camera.main?.transform;

        if (cameraTransform != null)
        {
            transform.position = ComputeTargetPosition();
            transform.rotation = CameraFaceRotation();
        }
    }

    private void LateUpdate()
    {
        if (cameraTransform == null) return;

        Vector3 targetPos = ComputeTargetPosition();
        transform.position = Vector3.SmoothDamp(transform.position, targetPos, ref velocity, positionSmoothTime);

        targetRotation = CameraFaceRotation();
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSmoothSpeed * Time.deltaTime);

        // Pointer update – now uses the pointer's own world position
        if (currentPointer != null && currentTarget != null)
        {
            Vector3 toTarget = currentTarget.position - currentPointer.transform.position;
            if (toTarget != Vector3.zero)
            {
                currentPointer.transform.rotation = Quaternion.LookRotation(toTarget, Vector3.up);
            }
        }
    }

    private Vector3 ComputeTargetPosition()
    {
        return cameraTransform.position
               + cameraTransform.forward * followDistance
               + Vector3.up * verticalOffset;
    }

    private Quaternion CameraFaceRotation()
    {
        Vector3 toCamera = cameraTransform.position - transform.position;
        return Quaternion.LookRotation(-toCamera, Vector3.up);
    }

    private void CreateHUD()
    {
        if (hudObject != null)
        {
            if (Application.isPlaying) Destroy(hudObject);
            else DestroyImmediate(hudObject);
        }

        hudObject = new GameObject("VR_HUD_Panel");
        hudObject.transform.SetParent(transform);
        hudObject.transform.localPosition = Vector3.zero;
        hudObject.transform.localRotation = Quaternion.identity;
        hudObject.transform.localScale = Vector3.one * worldScaleMultiplier;

        uiDocument = hudObject.AddComponent<UIDocument>();
        var panelSettings = Resources.Load<PanelSettings>("VRHUDPanelSettings");
        if (panelSettings == null)
        {
            Debug.LogError("[VRHUDManager] Missing 'VRHUDPanelSettings' in Resources. Ensure Render Mode is World Space.");
            return;
        }
        uiDocument.panelSettings = panelSettings;

        var root = uiDocument.rootVisualElement;
        root.Clear();
        ApplyPanelStyles(root);

        statusLabel = new Label(" ") { name = "StatusLabel" };
        hintLabel = new Label("") { name = "HintLabel" };
        completionLabel = new Label("Complete!") { name = "CompletionLabel" };
        completionLabel.style.display = DisplayStyle.None;

        ApplyLabelStyles(statusLabel, statusFontSize, statusColor, FontStyle.Bold, statusBottomMargin);
        ApplyLabelStyles(hintLabel, hintFontSize, hintColor, FontStyle.Normal, 0);
        ApplyLabelStyles(completionLabel, completionFontSize, completionColor, FontStyle.Bold, 0);
        completionLabel.style.display = DisplayStyle.None;

        root.Add(statusLabel);
        root.Add(hintLabel);
        root.Add(completionLabel);
        root.style.opacity = targetOpacity;

        // Recreate pointer if we had a target before refresh
        if (currentTarget != null)
            CreatePointer(currentTarget);
    }

    private void ApplyPanelStyles(VisualElement panel)
    {
        panel.style.width = panelWidth;
        panel.style.height = panelHeight;
        panel.style.paddingTop = panelPadding;
        panel.style.paddingBottom = panelPadding;
        panel.style.paddingLeft = panelPadding;
        panel.style.paddingRight = panelPadding;
        panel.style.backgroundColor = backgroundColor;
        panel.style.borderTopLeftRadius = borderRadius;
        panel.style.borderTopRightRadius = borderRadius;
        panel.style.borderBottomLeftRadius = borderRadius;
        panel.style.borderBottomRightRadius = borderRadius;
        panel.style.borderLeftWidth = borderWidth;
        panel.style.borderRightWidth = borderWidth;
        panel.style.borderTopWidth = borderWidth;
        panel.style.borderBottomWidth = borderWidth;
        panel.style.borderLeftColor = borderColor;
        panel.style.borderRightColor = borderColor;
        panel.style.borderTopColor = borderColor;
        panel.style.borderBottomColor = borderColor;
        panel.style.alignItems = Align.Center;
        panel.style.justifyContent = Justify.Center;
        panel.style.flexDirection = FlexDirection.Column;
        panel.style.overflow = Overflow.Hidden;
    }

    private void ApplyLabelStyles(Label label, float fontSize, Color color, FontStyle weight, float bottomMargin)
    {
        label.style.fontSize = fontSize;
        label.style.color = color;
        label.style.unityFontStyleAndWeight = weight;
        label.style.marginBottom = bottomMargin;
        label.style.unityTextAlign = TextAnchor.MiddleCenter;
        label.style.whiteSpace = WhiteSpace.Normal;
        label.style.overflow = Overflow.Hidden;
        label.style.textOverflow = TextOverflow.Ellipsis;
        label.style.width = panelWidth - panelPadding * 2;
    }

    // --- Public API ---

    public void SetStatus(string mainText, string hintText)
    {
        lastStatusText = mainText ?? "";
        lastHintText = hintText ?? "";
        isCompletionShowing = false;

        if (string.IsNullOrWhiteSpace(lastStatusText) && string.IsNullOrWhiteSpace(lastHintText))
        {
            StartFadeOut(0f);
        }
        else
        {
            if (statusLabel != null)
            {
                statusLabel.text = lastStatusText;
                hintLabel.text = lastHintText;
                statusLabel.style.display = DisplayStyle.Flex;
                hintLabel.style.display = DisplayStyle.Flex;
                completionLabel.style.display = DisplayStyle.None;
                uiDocument.rootVisualElement.style.opacity = 1f;
                targetOpacity = 1f;
            }

            if (fadeDelay > 0f)
                StartFadeCountdown(fadeDelay);
            else
            {
                CancelFade();
                SetOpacity(1f);
            }
        }
    }

    public void ShowCompletionMessage(string message)
    {
        lastCompletionMessage = message ?? "";
        isCompletionShowing = true;

        if (statusLabel != null)
        {
            statusLabel.style.display = DisplayStyle.None;
            hintLabel.style.display = DisplayStyle.None;
            completionLabel.text = lastCompletionMessage;
            completionLabel.style.display = DisplayStyle.Flex;
            uiDocument.rootVisualElement.style.opacity = 1f;
            targetOpacity = 1f;
        }

        if (fadeDelay > 0f)
            StartFadeCountdown(fadeDelay);
        else
        {
            CancelFade();
            SetOpacity(1f);
        }
    }

    public void SetTarget(Transform target)
    {
        currentTarget = target;

        // Destroy existing pointer
        if (currentPointer != null)
        {
            if (Application.isPlaying) Destroy(currentPointer);
            else DestroyImmediate(currentPointer);
            currentPointer = null;
        }

        if (target != null && pointerPrefab != null)
        {
            CreatePointer(target);
        }

        UpdatePointerVisibility();
    }

    private void CreatePointer(Transform target)
    {
        Transform parent = hudObject != null ? hudObject.transform : transform;
        currentPointer = Instantiate(pointerPrefab, parent);
        currentPointer.transform.localPosition = pointerOffset;
        currentPointer.transform.localRotation = Quaternion.identity;
        // Initial rotation from pointer's position
        Vector3 toTarget = target.position - currentPointer.transform.position;
        if (toTarget != Vector3.zero)
            currentPointer.transform.rotation = Quaternion.LookRotation(toTarget, Vector3.up);
    }

    public void ClearHighlight()
    {
        SetTarget(null);
    }

    private void UpdatePointerVisibility()
    {
        if (currentPointer != null)
        {
            bool visible = targetOpacity > 0.01f;
            currentPointer.SetActive(visible);
        }
    }

    // --- Fade Handling ---

    private void StartFadeCountdown(float delay)
    {
        CancelFade();
        fadeCoroutine = StartCoroutine(FadeAfterDelay(delay));
    }

    private void StartFadeOut(float delay)
    {
        CancelFade();
        fadeCoroutine = StartCoroutine(FadeOutAfterDelay(delay));
    }

    private void CancelFade()
    {
        if (fadeCoroutine != null)
        {
            StopCoroutine(fadeCoroutine);
            fadeCoroutine = null;
        }
    }

    private IEnumerator FadeAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        fadeCoroutine = StartCoroutine(FadeTo(0f, fadeDuration));
    }

    private IEnumerator FadeOutAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        fadeCoroutine = StartCoroutine(FadeTo(0f, fadeDuration));
    }

    private IEnumerator FadeTo(float target, float duration)
    {
        if (uiDocument == null) yield break;
        var root = uiDocument.rootVisualElement;
        float start = root.style.opacity.value;
        float time = 0f;
        while (time < duration)
        {
            time += Time.deltaTime;
            root.style.opacity = Mathf.Lerp(start, target, time / duration);
            UpdatePointerVisibility();
            yield return null;
        }
        root.style.opacity = target;
        targetOpacity = target;
        UpdatePointerVisibility();
        fadeCoroutine = null;
    }

    private void SetOpacity(float value)
    {
        if (uiDocument != null)
        {
            uiDocument.rootVisualElement.style.opacity = value;
            targetOpacity = value;
            UpdatePointerVisibility();
        }
    }

    [ContextMenu("Refresh HUD")]
    public void RefreshHUD()
    {
        CreateHUD();
    }
    }
    }