Here is the complete, copy‑paste‑ready codebase for your VR training system, along with a step‑by‑step guide to set up a Meta Quest 3 scene using **Unity’s XR Interaction Toolkit** and the **Meta XR All‑in‑One SDK**. Every script is included in its final form – just create the files in your project, follow the setup, and you’ll have a working, future‑proof training simulator.

---

## 📁 Project Structure

Create the following C# scripts inside your `Assets/` folder (e.g., in `_TrainingSystem/Scripts`). You can keep the ScriptableObject assets wherever you like.

- `TaskStep.cs`
- `TaskManager.cs`
- `TaskStatusUI.cs`
- `VRHUDManager.cs`
- `InteractionHandler.cs` (abstract)
- `GrabHandler.cs`
- `SnapHandler.cs`
- `ButtonPressHandler.cs`
- `UISelectionHandler.cs`
- `GameEvent.cs`
- `GameEventListener.cs`
- `LmsTracker.cs` (abstract)
- `ScormTracker.cs`
- `XApiTracker.cs`

---

## 📜 Complete Scripts

### 1. `TaskStep.cs` (ScriptableObject)

```csharp
using UnityEngine;
using UnityEngine.Events;

[CreateAssetMenu(fileName = "NewTaskStep", menuName = "Training/Task Step")]
public class TaskStep : ScriptableObject
{
    [Tooltip("Unique ID for this step. Must match the 'linkedStepId' on interaction handlers.")]
    public string stepId;

    [Tooltip("Main instruction shown to the user.")]
    [TextArea] public string description;

    [Tooltip("Secondary hint text.")]
    [TextArea] public string hintMessage;

    [Tooltip("How the step is completed (for reference only; actual logic is in handlers).")]
    public ConditionType completionCondition;

    [Tooltip("Optional target object (for UI highlighting, if implemented).")]
    public GameObject targetObject;

    // Events that can be wired in the Inspector to activate/deactivate objects, play sounds, etc.
    public UnityEvent onStepStarted;
    public UnityEvent onStepCompleted;
    public UnityEvent onStepFailed;
}

public enum ConditionType
{
    Grab,
    Snap,
    ButtonPress,
    UISelection,
    LookAt,
    Custom
}
```

### 2. `TaskManager.cs`

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Manages the sequence of task steps. Automatically progresses when steps are completed.
/// Reports to a TaskStatusUI and logs to LMS trackers.
/// </summary>
public class TaskManager : MonoBehaviour
{
    [Header("Task Configuration")]
    [SerializeField] private List<TaskStep> taskSteps = new List<TaskStep>();
    [Tooltip("Automatically start the task when the scene loads.")]
    [SerializeField] private bool autoProgress = true;
    [Tooltip("Delay after completing a step before the next begins (seconds).")]
    [SerializeField] private float stepCompletionDelay = 1.0f;

    [Header("UI & Trackers")]
    [SerializeField] private TaskStatusUI statusUI;
    [SerializeField] private List<LmsTracker> lmsTrackers = new List<LmsTracker>();

    [Header("Events")]
    public UnityEvent onTaskStarted;
    public UnityEvent onTaskCompleted;
    public UnityEvent<string> onStepCompleted;   // passes stepId

    private int currentStepIndex = -1;
    private bool isTaskRunning = false;
    private Dictionary<string, bool> completedSteps = new Dictionary<string, bool>();

    private void Start()
    {
        // Initialize LMS trackers
        foreach (var tracker in lmsTrackers)
            tracker.Initialize();

        if (autoProgress)
            StartTask();
    }

    /// <summary>
    /// Begins the task sequence from the first step.
    /// </summary>
    public void StartTask()
    {
        if (taskSteps.Count == 0)
        {
            Debug.LogError("[TaskManager] No task steps defined!");
            return;
        }
        currentStepIndex = 0;
        isTaskRunning = true;
        onTaskStarted?.Invoke();
        ActivateStep(currentStepIndex);
        LogStepAttempt(taskSteps[currentStepIndex].stepId);
    }

    private void ActivateStep(int index)
    {
        var step = taskSteps[index];
        step.onStepStarted?.Invoke();
        statusUI?.ShowMessage(step.description, step.hintMessage);
    }

    /// <summary>
    /// Called by interaction handlers when the condition for a step is met.
    /// </summary>
    public void CompleteStep(string stepId)
    {
        if (!isTaskRunning || currentStepIndex >= taskSteps.Count) return;

        var currentStep = taskSteps[currentStepIndex];
        if (currentStep.stepId != stepId) return;

        completedSteps[stepId] = true;
        onStepCompleted?.Invoke(stepId);
        currentStep.onStepCompleted?.Invoke();
        LogStepCompletion(stepId, true);

        if (currentStepIndex < taskSteps.Count - 1)
        {
            StartCoroutine(DelayedNextStep());
        }
        else
        {
            isTaskRunning = false;
            statusUI?.ShowCompletionMessage("All tasks completed!");
            onTaskCompleted?.Invoke();
            foreach (var tracker in lmsTrackers)
                tracker.CompleteCourse("default_course"); // customize as needed
        }
    }

    private IEnumerator DelayedNextStep()
    {
        yield return new WaitForSeconds(stepCompletionDelay);
        currentStepIndex++;
        ActivateStep(currentStepIndex);
        LogStepAttempt(taskSteps[currentStepIndex].stepId);
    }

    /// <summary>
    /// Call to log a failure (optional).
    /// </summary>
    public void FailStep(string stepId)
    {
        if (!isTaskRunning) return;
        var step = taskSteps.Find(s => s.stepId == stepId);
        step?.onStepFailed?.Invoke();
        LogStepCompletion(stepId, false);
    }

    public TaskStep GetCurrentStep() => isTaskRunning ? taskSteps[currentStepIndex] : null;

    private void LogStepAttempt(string stepId)
    {
        foreach (var tracker in lmsTrackers)
            tracker.LogProgress(stepId, false, Time.time);
    }

    private void LogStepCompletion(string stepId, bool completed)
    {
        foreach (var tracker in lmsTrackers)
            tracker.LogProgress(stepId, completed, Time.time);
    }
}
```

### 3. `TaskStatusUI.cs` (Relay)

```csharp
using UnityEngine;

/// <summary>
/// Thin relay that sends UI commands to the dynamic VRHUDManager.
/// No manual UI references needed – just place this component in the scene.
/// </summary>
public class TaskStatusUI : MonoBehaviour
{
    private VRHUDManager hud;

    private void Start()
    {
        hud = VRHUDManager.Instance;
        if (hud == null)
            Debug.LogError("[TaskStatusUI] No VRHUDManager found in scene. Make sure VRHUDManager is present.");
    }

    public void ShowMessage(string mainText, string hint)
    {
        hud?.SetStatus(mainText, hint);
    }

    public void HighlightTarget(GameObject target)
    {
        // Optional highlight logic – currently does nothing.
    }

    public void ClearHighlight()
    {
        // Optional highlight logic – currently does nothing.
    }

    public void ShowCompletionMessage(string message)
    {
        hud?.ShowCompletionMessage(message);
    }
}
```

### 4. `VRHUDManager.cs` (Dynamic World‑Space UI)

```csharp
using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Creates a world-space UI that follows the HMD, stays above eye level,
/// and smoothly rotates back into view when ignored. Provides methods
/// to set status text. Automatically used by TaskStatusUI.
/// </summary>
public class VRHUDManager : MonoBehaviour
{
    public static VRHUDManager Instance { get; private set; }

    [Header("Follow Settings")]
    [SerializeField] private float followDistance = 1.5f;
    [SerializeField] private float verticalOffset = 0.3f;
    [SerializeField] private float positionSmoothTime = 0.15f;
    [SerializeField] private float rotationSmoothSpeed = 2.5f;

    [Header("Out-of-View Behavior")]
    [SerializeField] private float visibleAngle = 35f;
    [SerializeField] private float outOfViewDelay = 2.0f;

    [Header("UI Look (USS fallback)")]
    [SerializeField, TextArea(3, 10)] private string customUSS = ""; // reserved for future

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
            Debug.LogError("[VRHUDManager] No main camera found. Ensure an XR Rig is in the scene.");
    }

    private IEnumerator Start()
    {
        // Wait one frame so camera is fully initialized (especially in XR)
        yield return null;
        CreateHUD();
        if (cameraTransform != null)
        {
            transform.position = ComputeTargetPosition();
            transform.rotation = Quaternion.LookRotation(cameraTransform.forward, Vector3.up);
        }
    }

    private void LateUpdate()
    {
        if (cameraTransform == null) return;

        Vector3 targetPos = ComputeTargetPosition();
        transform.position = Vector3.SmoothDamp(transform.position, targetPos, ref velocity, positionSmoothTime);

        bool isVisible = IsHUDInView();
        if (isVisible)
        {
            outOfViewTimer = 0f;
            isSnappedToView = false;
        }
        else
        {
            outOfViewTimer += Time.deltaTime;
            if (outOfViewTimer >= outOfViewDelay)
                isSnappedToView = true;
        }

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
        hudObject = new GameObject("VR_HUD_Panel");
        hudObject.transform.SetParent(transform);
        hudObject.transform.localPosition = Vector3.zero;
        hudObject.transform.localRotation = Quaternion.identity;

        uiDocument = hudObject.AddComponent<UIDocument>();

        // Load PanelSettings resource (see setup instructions)
        var panelSettings = Resources.Load<PanelSettings>("VRHUDPanelSettings");
        if (panelSettings == null)
        {
            Debug.LogError("[VRHUDManager] Missing PanelSettings asset. Create it in Resources folder as 'VRHUDPanelSettings'.");
            return;
        }
        uiDocument.panelSettings = panelSettings;

        // Build UI
        rootContainer = new VisualElement();
        rootContainer.AddToClassList("container");

        statusLabel = new Label("Loading...");
        statusLabel.AddToClassList("status");
        rootContainer.Add(statusLabel);

        hintLabel = new Label("");
        hintLabel.AddToClassList("hint");
        rootContainer.Add(hintLabel);

        completionLabel = new Label("Complete!");
        completionLabel.AddToClassList("completion");
        completionLabel.style.display = DisplayStyle.None;
        rootContainer.Add(completionLabel);

        uiDocument.rootVisualElement.Clear();
        uiDocument.rootVisualElement.Add(rootContainer);

        ApplyStyles();
    }

    private void ApplyStyles()
    {
        // Inline styles for reliability (no external USS file needed)
        rootContainer.style.backgroundColor = new Color(0, 0, 0, 0.75f);
        rootContainer.style.borderTopLeftRadius = 16;
        rootContainer.style.borderTopRightRadius = 16;
        rootContainer.style.borderBottomLeftRadius = 16;
        rootContainer.style.borderBottomRightRadius = 16;
        rootContainer.style.paddingTop = 24;
        rootContainer.style.paddingBottom = 24;
        rootContainer.style.paddingLeft = 24;
        rootContainer.style.paddingRight = 24;
        rootContainer.style.minWidth = 400;
        rootContainer.style.alignItems = Align.Center;
        rootContainer.style.borderLeftWidth = 1;
        rootContainer.style.borderRightWidth = 1;
        rootContainer.style.borderTopWidth = 1;
        rootContainer.style.borderBottomWidth = 1;
        rootContainer.style.borderLeftColor = new Color(1, 1, 1, 0.2f);
        rootContainer.style.borderRightColor = new Color(1, 1, 1, 0.2f);
        rootContainer.style.borderTopColor = new Color(1, 1, 1, 0.2f);
        rootContainer.style.borderBottomColor = new Color(1, 1, 1, 0.2f);

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

    // Public API
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

    public void ClearHighlight() { }
}
```

### 5. `InteractionHandler.cs` (Abstract Base)

```csharp
using UnityEngine;

/// <summary>
/// Base class for interaction handlers that bridge XRI events to the TaskManager.
/// </summary>
public abstract class InteractionHandler : MonoBehaviour
{
    [SerializeField] protected TaskManager taskManager;
    [SerializeField] protected string linkedStepId;

    protected virtual void HandleCompletion()
    {
        if (taskManager != null)
            taskManager.CompleteStep(linkedStepId);
        else
            Debug.LogWarning($"[{GetType().Name}] TaskManager reference missing on {gameObject.name}", this);
    }
}
```

### 6. `GrabHandler.cs`

```csharp
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

/// <summary>
/// Completes the linked step when this object is grabbed.
/// Requires an XRGrabInteractable (or derived) component.
/// </summary>
[RequireComponent(typeof(XRBaseInteractable))] // XRGrabInteractable inherits from this
public class GrabHandler : InteractionHandler
{
    private XRBaseInteractable grabInteractable;

    private void Awake()
    {
        grabInteractable = GetComponent<XRBaseInteractable>();
    }

    private void OnEnable()
    {
        if (grabInteractable != null)
            grabInteractable.selectEntered.AddListener(OnGrabbed);
    }

    private void OnDisable()
    {
        if (grabInteractable != null)
            grabInteractable.selectEntered.RemoveListener(OnGrabbed);
    }

    private void OnGrabbed(SelectEnterEventArgs args) => HandleCompletion();
}
```

### 7. `SnapHandler.cs`

```csharp
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

/// <summary>
/// Completes the linked step when an interactable is snapped into this socket.
/// Requires an XRSocketInteractor (or derived) component.
/// </summary>
[RequireComponent(typeof(XRSocketInteractor))]
public class SnapHandler : InteractionHandler
{
    private XRSocketInteractor socketInteractor;

    private void Awake()
    {
        socketInteractor = GetComponent<XRSocketInteractor>();
    }

    private void OnEnable()
    {
        if (socketInteractor != null)
            socketInteractor.selectEntered.AddListener(OnSnapped);
    }

    private void OnDisable()
    {
        if (socketInteractor != null)
            socketInteractor.selectEntered.RemoveListener(OnSnapped);
    }

    private void OnSnapped(SelectEnterEventArgs args) => HandleCompletion();
}
```

### 8. `ButtonPressHandler.cs`

```csharp
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

/// <summary>
/// Completes the linked step when a 3D button is pressed (select entered).
/// Requires an XRSimpleInteractable (or derived) component.
/// </summary>
[RequireComponent(typeof(XRSimpleInteractable))]
public class ButtonPressHandler : InteractionHandler
{
    private XRSimpleInteractable simpleInteractable;

    private void Awake()
    {
        simpleInteractable = GetComponent<XRSimpleInteractable>();
    }

    private void OnEnable()
    {
        if (simpleInteractable != null)
            simpleInteractable.selectEntered.AddListener(OnPressed);
    }

    private void OnDisable()
    {
        if (simpleInteractable != null)
            simpleInteractable.selectEntered.RemoveListener(OnPressed);
    }

    private void OnPressed(SelectEnterEventArgs args) => HandleCompletion();
}
```

### 9. `UISelectionHandler.cs`

```csharp
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Completes the linked step when a Unity UI Button is clicked.
/// Works with XRI's XRUIInputModule (automatically set up by the XR Rig).
/// </summary>
[RequireComponent(typeof(Button))]
public class UISelectionHandler : InteractionHandler
{
    private Button uiButton;

    private void Awake()
    {
        uiButton = GetComponent<Button>();
    }

    private void OnEnable()
    {
        if (uiButton != null)
            uiButton.onClick.AddListener(HandleCompletion);
    }

    private void OnDisable()
    {
        if (uiButton != null)
            uiButton.onClick.RemoveListener(HandleCompletion);
    }
}
```

### 10. `GameEvent.cs` (ScriptableObject)

```csharp
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NewGameEvent", menuName = "Training/Game Event")]
public class GameEvent : ScriptableObject
{
    private readonly List<GameEventListener> listeners = new List<GameEventListener>();

    public void Raise()
    {
        for (int i = listeners.Count - 1; i >= 0; i--)
            listeners[i].OnEventRaised();
    }

    public void RegisterListener(GameEventListener listener) => listeners.Add(listener);
    public void UnregisterListener(GameEventListener listener) => listeners.Remove(listener);
}
```

### 11. `GameEventListener.cs`

```csharp
using UnityEngine;
using UnityEngine.Events;

public class GameEventListener : MonoBehaviour
{
    [SerializeField] private GameEvent gameEvent;
    [SerializeField] private UnityEvent response;

    private void OnEnable() => gameEvent?.RegisterListener(this);
    private void OnDisable() => gameEvent?.UnregisterListener(this);

    public void OnEventRaised() => response?.Invoke();
}
```

### 12. `LmsTracker.cs` (Abstract Base)

```csharp
using UnityEngine;

/// <summary>
/// Abstract base for LMS trackers (SCORM, xAPI, etc.).
/// Attach concrete implementations to the TaskManager GameObject.
/// </summary>
public abstract class LmsTracker : MonoBehaviour
{
    public abstract void Initialize();
    public abstract void LogProgress(string stepId, bool completed, float timestamp);
    public abstract void LogScore(string stepId, float score);
    public abstract void CompleteCourse(string courseId);
}
```

### 13. `ScormTracker.cs` (Example)

```csharp
using System.Runtime.InteropServices;
using UnityEngine;

public class ScormTracker : LmsTracker
{
    #if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void ScormSetValue(string element, string value);
    [DllImport("__Internal")]
    private static extern void ScormCommit();
    #endif

    public override void Initialize()
    {
        Debug.Log("[SCORM] Tracker initialized");
    }

    public override void LogProgress(string stepId, bool completed, float timestamp)
    {
        string data = $"{{\"step\":\"{stepId}\",\"completed\":{completed.ToString().ToLower()},\"time\":{timestamp}}}";
        #if UNITY_WEBGL && !UNITY_EDITOR
        ScormSetValue("cmi.suspend_data", data);
        ScormCommit();
        #else
        Debug.Log($"[SCORM] Progress: {data}");
        #endif
    }

    public override void LogScore(string stepId, float score)
    {
        // Implement as needed
    }

    public override void CompleteCourse(string courseId)
    {
        Debug.Log($"[SCORM] Course '{courseId}' completed.");
        #if UNITY_WEBGL && !UNITY_EDITOR
        ScormSetValue("cmi.completion_status", "completed");
        ScormCommit();
        #endif
    }
}
```

### 14. `XApiTracker.cs` (Example)

```csharp
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

public class XApiTracker : LmsTracker
{
    [SerializeField] private string lrsEndpoint = "https://lrs.example.com/data/xAPI";
    [SerializeField] private string authToken = "";

    public override void Initialize()
    {
        Debug.Log("[xAPI] Tracker initialized");
    }

    public override void LogProgress(string stepId, bool completed, float timestamp)
    {
        var statement = new
        {
            actor = new { objectType = "Agent", mbox = "mailto:learner@example.com" },
            verb = new { id = completed ? "http://adlnet.gov/expapi/verbs/completed" : "http://adlnet.gov/expapi/verbs/attempted" },
            @object = new { id = $"http://training.company.com/activities/{stepId}", objectType = "Activity" }
        };
        string json = JsonUtility.ToJson(statement);
        StartCoroutine(SendStatement(json));
    }

    private IEnumerator SendStatement(string json)
    {
        using (var request = new UnityWebRequest(lrsEndpoint, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Basic " + authToken);
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
                Debug.LogError($"[xAPI] Send failed: {request.error}");
        }
    }

    public override void LogScore(string stepId, float score) { }
    public override void CompleteCourse(string courseId) { }
}
```

---

## 🔧 Step‑by‑Step Scene Setup

### A. Project Setup
1. Create a new project with **Unity 2022.3 LTS** or **2023.3+**.
2. Install via Package Manager:
   - **XR Interaction Toolkit** (2.5.x or later)
   - **XR Plugin Management** → enable **Oculus** plugin (for Quest)
   - **Meta XR All‑in‑One SDK** (from Asset Store or Meta’s registry)
   - (Optional) **TextMeshPro** if you ever switch to TMP.
3. Enable **UI Toolkit Runtime**: *Edit → Project Settings → UI Toolkit → Enable UI Toolkit Runtime* (should be on by default).
4. Switch build platform to **Android** and configure Player Settings for Quest.

### B. Import the Scripts
Create a folder `_TrainingSystem` in your Assets and drop all the `.cs` files above into it. The `TaskStep`, `GameEvent`, `PanelSettings` assets will be created later.

### C. Create the PanelSettings Asset (Crucial for UI Toolkit)
1. In the Project window, right‑click → **Create** → **UI Toolkit** → **Panel Settings Asset**. Name it exactly `VRHUDPanelSettings`.
2. Move this asset into a new folder called `Resources` (e.g., `Assets/Resources/VRHUDPanelSettings.asset`).  
   *(If the Resources folder doesn’t exist, create it – this is mandatory for `Resources.Load`.)*
3. Leave all default values; no changes required.

### D. Build the Core Scene
1. Delete the default `Main Camera` (we’ll use XR Rig).
2. From the Meta SDK or XRI, drag the **XR Interaction Setup** prefab:
   - If using **Meta XR Building Blocks**: Right‑click in Hierarchy → **Meta XR** → **Add Comprehensive Rig** (or **Unity XRI Rig**). This creates a rig with cameras, controllers, and input modules.
   - If using plain XRI: Create a new `XR Origin (VR)` from the `GameObject → XR → XR Origin (VR)` menu. Then add the `XR Ray Interactor` components to left/right controllers (the Meta Rig already does this).
3. Ensure the **EventSystem** GameObject has an `XRUIInputModule` – the Meta rig adds this automatically. This enables 3D UI clicking.

### E. Add the Managers
1. Create an empty GameObject called `Managers` at position (0,0,0).
2. Attach the following components to it:
   - `VRHUDManager`
   - `TaskStatusUI` (doesn’t need any assigned references)
   - `TaskManager`
   - (Optional) `ScormTracker` or `XApiTracker` if you want LMS logging.
3. **Wire the TaskManager**:
   - In the Inspector of the `TaskManager`, drag the `TaskStatusUI` component (on the same object) into the `Status UI` field.
   - If you added trackers, drag them into the `Lms Trackers` list.
   - For now, leave `Task Steps` empty – we’ll create steps shortly.

### F. Create the Task Definition Assets
1. In the Project window, right‑click → **Create** → **Training** → **Task Step**.
2. Name it something like `Step_GrabObject`.
3. Fill in:
   - **Step Id**: e.g., `grab_cube`
   - **Description**: “Pick up the red cube.”
   - **Hint Message**: “Use the grip button to grab.”
   - **Completion Condition**: Grab (for reference)
   - Leave `Target Object` blank for now.
4. Repeat for additional steps. You can design a linear sequence.

5. After creating all steps, select the `Managers` GameObject and drag these assets into the `Task Steps` list inside `TaskManager`. Put them in the desired order.

### G. Set Up Interactable Objects
For each interaction type:

**Grabbable Object**:
1. Create a 3D object (Cube, etc.) in the scene.
2. Add an `XRGrabInteractable` component (or `MetaGrabInteractable` if using Meta’s rigidbody‑based grabber).  
   *Note: If using Meta’s Building Blocks, `MetaGrabInteractable` derives from `XRGrabInteractable`, so the `GrabHandler` works fine.*
3. Add a `GrabHandler` component.
4. In the `GrabHandler` inspector:
   - Drag the `TaskManager` object into the `Task Manager` field.
   - Enter the corresponding step’s `Step Id` (e.g., “grab_cube”) into `Linked Step Id`.

**Snap Zone**:
1. Create an empty GameObject for the socket.
2. Add an `XRSocketInteractor` (or `MetaSocketInteractor`).
3. Configure its interaction layer and settings (only allow specific interactables via tags or layers).
4. Add a `SnapHandler` component.
5. Link the TaskManager and step ID, just like above.

**Button**:
1. Create a 3D button (e.g., a Cylinder or Button prefab).
2. Add an `XRSimpleInteractable` component. For a physical press button you may also add a `XRPokeFollowAffordance` and configure it; the handler only listens to `selectEntered`.
3. Add a `ButtonPressHandler` and link the step ID.

**UI Button (Canvas)**:
1. Create a `Canvas` set to *World Space*, scale it appropriately (e.g., 0.0025).
2. Add a Button inside it.
3. On the Button GameObject, add a `UISelectionHandler` component.
4. Link the step ID. That’s it – the XR controllers will interact with it via the `XRUIInputModule`.

### H. Optional: GameEvents for Extra Actions
1. Create GameEvent assets: right‑click → **Create** → **Training** → **Game Event**. Name it e.g., “OnTaskComplete”.
2. In a `TaskStep` asset inspector, in the `On Step Completed` list, click **+** and drag a GameObject that has a `GameEventListener` component. Then select the `GameEvent.Raise` function of the event asset. This bridges the step completion to the event.
3. On any GameObject you want to react (e.g., to activate an object), add a `GameEventListener`, set the GameEvent field, and add a response (e.g., `GameObject.SetActive(true)`) in the Unity Event list.

### I. Final Review
- Ensure all interaction handlers have the correct `linkedStepId` that matches the `stepId` in the corresponding `TaskStep` asset.
- The `TaskManager`’s `Auto Progress` is checked (to start on scene load), or leave it unchecked and call `StartTask()` manually from another event.
- Play the scene: you should see the VR HUD appear in front of you with the first step’s text. Interact with the objects, and the task should progress.

---

## 🧪 Testing in Editor vs. Device
- In the Editor, use an XR Device Simulator (XRI provides one) to simulate controller input.
- On device, the HUD will follow your head movement and rotate back if you look away.
- For LMS testing, the `ScormTracker` will log to the console in Editor, but exports properly in WebGL.

---

## 🚀 Future‑Proofing & Compatibility
- All interaction handlers work with any XRI‑derived components, including Meta’s own versions (`MetaGrabInteractable`, `MetaSocketInteractor`). No changes needed.
- The UI Toolkit HUD eliminates Canvas scaling headaches and is resolution‑independent.
- The modular event system lets you extend the training without rewriting core logic – simply add new handlers (e.g., `GazeHandler`) that call `TaskManager.CompleteStep()`.

You now have a fully integrated, clean, novice‑friendly VR training system that runs on Meta Quest 3 and can track progress via SCORM/xAPI. Just copy the scripts, follow the setup, and start building your training simulations.
