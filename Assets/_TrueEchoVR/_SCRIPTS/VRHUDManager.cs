using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Spawns a world-space UI panel that stays in front of the player,
/// above eye level, and rotates to face the player after a delay
/// when out of view. Provides public methods to set status text.
/// </summary>
public class VRHUDManager : MonoBehaviour
{
    public static VRHUDManager Instance { get; private set; }

    [Header("Follow Settings")]
    [Tooltip("Distance from the camera (meters).")]
    [SerializeField] private float followDistance = 1.5f;
    [Tooltip("Vertical offset above the camera (meters).")]
    [SerializeField] private float verticalOffset = 0.3f;
    [Tooltip("Smooth time for position follow.")]
    [SerializeField] private float positionSmoothTime = 0.15f;
    [Tooltip("Speed at which the HUD rotates to face the player.")]
    [SerializeField] private float rotationSmoothSpeed = 2.5f;

    [Header("Out-of-View Behavior")]
    [Tooltip("Angle (degrees) within which the HUD is considered 'seen'.")]
    [SerializeField] private float visibleAngle = 35f;
    [Tooltip("Time (seconds) the HUD must be out of view before it snaps back.")]
    [SerializeField] private float outOfViewDelay = 2.0f;

    [Header("UI Look (USS)")]
    [SerializeField, TextArea(3, 10)] private string customUSS = @"
        .container {
            background-color: rgba(0,0,0,0.75);
            border-radius: 16px;
            padding: 24px;
            min-width: 400px;
            align-items: center;
            border-width: 1px;
            border-color: rgba(255,255,255,0.2);
        }
        .status {
            font-size: 22px;
            color: #FFFFFF;
            -unity-font-style: bold;
            margin-bottom: 12px;
        }
        .hint {
            font-size: 16px;
            color: #CCCCCC;
        }
        .completion {
            font-size: 20px;
            color: #00FF88;
            -unity-font-style: bold;
        }
    ";

    private GameObject hudObject;
    private UIDocument uiDocument;
    private VisualElement rootContainer;
    private Label statusLabel;
    private Label hintLabel;
    private Label completionLabel;

    private Transform cameraTransform;
    private Vector3 velocity = Vector3.zero;
    private float outOfViewTimer = 0f;
    private bool isSnappedToView = false;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        cameraTransform = Camera.main?.transform;
        if (cameraTransform == null)
            Debug.LogError("VRHUDManager: No main camera found. Make sure your XR Rig is in the scene.");

        CreateHUD();
        StartCoroutine(InitializeHUDDelayed());
    }

    private IEnumerator InitializeHUDDelayed()
    {
        // Wait one frame so camera is ready (especially in XR)
        yield return null;
        if (cameraTransform != null)
        {
            transform.position = ComputeTargetPosition();
            transform.rotation = Quaternion.LookRotation(cameraTransform.forward, Vector3.up);
        }
    }

    private void LateUpdate()
    {
        if (cameraTransform == null) return;

        // Smooth position follow
        Vector3 targetPos = ComputeTargetPosition();
        transform.position = Vector3.SmoothDamp(transform.position, targetPos, ref velocity, positionSmoothTime);

        // Determine if the HUD is "seen" by the player
        bool isVisible = IsHUDInView();

        if (isVisible)
        {
            outOfViewTimer = 0f;
            isSnappedToView = false;
            // Keep current rotation (do not force face-camera)
        }
        else
        {
            outOfViewTimer += Time.deltaTime;
            if (outOfViewTimer >= outOfViewDelay)
                isSnappedToView = true;
        }

        // Smoothly rotate to face the camera when snapped, otherwise keep stable
        if (isSnappedToView)
        {
            Vector3 lookDir = (cameraTransform.position - transform.position).normalized;
            Quaternion targetRot = Quaternion.LookRotation(lookDir, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotationSmoothSpeed * Time.deltaTime);
        }
    }

    private Vector3 ComputeTargetPosition()
    {
        return cameraTransform.position
               + cameraTransform.forward * followDistance
               + Vector3.up * verticalOffset;
    }

    private bool IsHUDInView()
    {
        Vector3 toHUD = (transform.position - cameraTransform.position).normalized;
        float angle = Vector3.Angle(cameraTransform.forward, toHUD);
        return angle < visibleAngle;
    }

    private void CreateHUD()
    {
        // Create a dedicated GameObject for the UI Document in world space
        hudObject = new GameObject("VR_HUD_Panel");
        hudObject.transform.SetParent(transform);
        hudObject.transform.localPosition = Vector3.zero;
        hudObject.transform.localRotation = Quaternion.identity;

        uiDocument = hudObject.AddComponent<UIDocument>();

        // Load PanelSettings from Resources (see setup instructions below)
        var panelSettings = Resources.Load<PanelSettings>("VRHUDPanelSettings");
        if (panelSettings == null)
        {
            Debug.LogError("VRHUDManager: Missing PanelSettings. Create a PanelSettings asset named 'VRHUDPanelSettings' in Resources folder.");
            return;
        }
        uiDocument.panelSettings = panelSettings;

        // Build UI structure entirely in code
        rootContainer = new VisualElement();
        rootContainer.AddToClassList("container");

        statusLabel = new Label("Status");
        statusLabel.AddToClassList("status");
        rootContainer.Add(statusLabel);

        hintLabel = new Label("Hint");
        hintLabel.AddToClassList("hint");
        rootContainer.Add(hintLabel);

        completionLabel = new Label("Complete!");
        completionLabel.AddToClassList("completion");
        completionLabel.style.display = DisplayStyle.None;
        rootContainer.Add(completionLabel);

        uiDocument.rootVisualElement.Clear();
        uiDocument.rootVisualElement.Add(rootContainer);

        // Apply custom USS
        var styleSheet = ScriptableObject.CreateInstance<StyleSheet>();
        // We use a simple trick: apply USS directly via extension method
        rootContainer.styleSheets.Add(CreateStyleSheet(customUSS));
    }

    /// <summary>
    /// Helper to create a StyleSheet from string content at runtime.
    /// </summary>
    private StyleSheet CreateStyleSheet(string uss)
    {
        var styleSheet = ScriptableObject.CreateInstance<StyleSheet>();
        // Unity's StyleSheet can't be populated from string easily in runtime.
        // Instead we'll use inline styles for simplicity, or you can load a .uss from Resources.
        // For a fully runtime solution, we directly set style properties below.
        ApplyFallbackStyles();
        return styleSheet;
    }

    private void ApplyFallbackStyles()
    {
        // Fallback inline styles – this guarantees visibility even without USS parsing at runtime.
        rootContainer.style.backgroundColor = new Color(0, 0, 0, 0.75f);
        rootContainer.style.borderTopLeftRadius = rootContainer.style.borderTopRightRadius = 16;
        rootContainer.style.borderBottomLeftRadius = rootContainer.style.borderBottomRightRadius = 16;
        rootContainer.style.paddingTop = rootContainer.style.paddingBottom = 24;
        rootContainer.style.paddingLeft = rootContainer.style.paddingRight = 24;
        rootContainer.style.minWidth = 400;
        rootContainer.style.alignItems = Align.Center;
        rootContainer.style.borderLeftWidth = rootContainer.style.borderRightWidth = rootContainer.style.borderTopWidth = rootContainer.style.borderBottomWidth = 1;
        rootContainer.style.borderLeftColor = rootContainer.style.borderRightColor = rootContainer.style.borderTopColor = rootContainer.style.borderBottomColor = new Color(1, 1, 1, 0.2f);
        statusLabel.style.fontSize = 22;
        statusLabel.style.color = Color.white;
        statusLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        statusLabel.style.marginBottom = 12;
        hintLabel.style.fontSize = 16;
        hintLabel.style.color = new Color(0.8f, 0.8f, 0.8f);
        completionLabel.style.fontSize = 20;
        completionLabel.style.color = new Color(0, 1, 0.53f);
        completionLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        completionLabel.style.display = DisplayStyle.None;
    }

    // --- Public API for TaskStatusUI ---

    public void SetStatus(string mainText, string hintText)
    {
        statusLabel.text = mainText;
        hintLabel.text = hintText;
        statusLabel.style.display = DisplayStyle.Flex;
        hintLabel.style.display = DisplayStyle.Flex;
        completionLabel.style.display = DisplayStyle.None;
    }

    public void ShowCompletionMessage(string message)
    {
        statusLabel.style.display = DisplayStyle.None;
        hintLabel.style.display = DisplayStyle.None;
        completionLabel.text = message;
        completionLabel.style.display = DisplayStyle.Flex;
    }

    public void ClearHighlight()
    {
        // No highlight in this minimal HUD, but method exists for compatibility.
    }
}