using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace TrueEchoVR
{
    public class TaskManager : MonoBehaviour
    {
        public static TaskManager Current { get; private set; }

        [Header("Task Configuration")]
        [SerializeField] private List<TaskStepData> steps = new List<TaskStepData>();
        [SerializeField] private bool autoStart = true;
        [SerializeField] private float stepCompletionDelay = 1.0f;

        [Header("UI & Trackers")]
        [SerializeField] private MainVRHUDUI statusUI;
        [SerializeField] private List<LmsTracker> lmsTrackers = new List<LmsTracker>();

        [Header("Events")]
        public UnityEvent onTaskStarted;
        public UnityEvent onTaskCompleted;
        public UnityEvent<string> onStepCompleted;

        private int currentStepIndex = -1;
        private bool isTaskRunning = false;

        private void OnEnable()
        {
            Current = this;
            if (autoStart && !isTaskRunning && steps.Count > 0)
                StartCoroutine(DelayedStartTask());
        }

        private void OnDisable()
        {
            if (Current == this)
                Current = null;
            StopAllCoroutines();
        }

        private IEnumerator DelayedStartTask()
        {
            yield return null;
            if (statusUI == null)
                statusUI = FindObjectOfType<MainVRHUDUI>(true);
            foreach (var tracker in lmsTrackers)
                tracker.Initialize();
            StartTask();
        }

        public void StartTask()
        {
            if (steps.Count == 0)
            {
                Debug.LogError("[TaskManager] No steps defined!");
                return;
            }
            currentStepIndex = 0;
            isTaskRunning = true;
            onTaskStarted?.Invoke();
            ActivateStep(currentStepIndex);
        }

        private void ActivateStep(int index)
        {
            if (!isTaskRunning) return;
            if (index < 0 || index >= steps.Count)
            {
                Debug.LogWarning($"[TaskManager] Step index {index} is out of range. Task will stop.", this);
                statusUI?.ShowMessage("Task configuration error", "The current step is missing. Notify a developer.");
                StopTask();
                return;
            }

            TaskStepData step = steps[index];
            step.onStepStarted?.Invoke();
            statusUI?.ShowMessage(step.description, step.hintMessage);
            // FIXED: pass Transform directly (step.targetObject is a Transform)
            statusUI?.HighlightTarget(step.targetObject);
        }

        public void TryCompleteWithObject(GameObject obj)
        {
            if (!isTaskRunning || currentStepIndex >= steps.Count) return;
            var step = steps[currentStepIndex];
            if (step.targetObject == null) return;
            if (step.targetObject.gameObject == obj)
            {
                CompleteStep(step.stepId);
            }
        }

        public void CompleteStep(string stepId)
        {
            if (!isTaskRunning || currentStepIndex >= steps.Count) return;
            TaskStepData current = steps[currentStepIndex];
            if (current.stepId != stepId) return;

            onStepCompleted?.Invoke(stepId);
            current.onStepCompleted?.Invoke();
            statusUI?.ClearHighlight();

            foreach (var tracker in lmsTrackers)
                tracker.LogProgress(stepId, true, Time.time);

            if (currentStepIndex < steps.Count - 1)
            {
                StartCoroutine(DelayedNextStep());
            }
            else
            {
                isTaskRunning = false;
                statusUI?.ShowCompletionMessage("All tasks completed!");
                onTaskCompleted?.Invoke();
                foreach (var tracker in lmsTrackers)
                    tracker.CompleteCourse("default_course");
            }
        }

        private IEnumerator DelayedNextStep()
        {
            yield return new WaitForSeconds(stepCompletionDelay);

            if (!isTaskRunning) yield break;

            currentStepIndex++;
            if (currentStepIndex >= steps.Count)
            {
                Debug.LogWarning($"[TaskManager] No more steps after delay (index {currentStepIndex}). Completing task.", this);
                isTaskRunning = false;
                statusUI?.ShowCompletionMessage("All tasks completed!");
                onTaskCompleted?.Invoke();
                yield break;
            }

            ActivateStep(currentStepIndex);

            foreach (var tracker in lmsTrackers)
                tracker.LogProgress(steps[currentStepIndex].stepId, false, Time.time);
        }

        public void FailStep(string stepId)
        {
            if (!isTaskRunning) return;
            var step = steps.Find(s => s.stepId == stepId);
            step?.onStepFailed?.Invoke();
            foreach (var tracker in lmsTrackers)
                tracker.LogProgress(stepId, false, Time.time);
        }

        public TaskStepData GetCurrentStep() => isTaskRunning && currentStepIndex >= 0 && currentStepIndex < steps.Count
            ? steps[currentStepIndex]
            : null;

        public void StopTask()
        {
            isTaskRunning = false;
            currentStepIndex = -1;
            statusUI?.ClearHighlight();
        }
    }
}