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