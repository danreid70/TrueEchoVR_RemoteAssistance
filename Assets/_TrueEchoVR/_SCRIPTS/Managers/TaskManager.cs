using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace TEVR
{
    /// <summary>
    /// Manages the sequence of tasks and steps within the training session.
    /// Acts as the central engine for progress tracking and feedback.
    /// </summary>
    public class TaskManager : MonoBehaviour
    {
        /// <summary>
        /// Singleton access to the active TaskManager.
        /// </summary>
        public static TaskManager Current { get; private set; }

        [Header("Task Configuration")]
        [Tooltip("The list of steps defining the procedural training.")]
        [SerializeField] private List<TaskStepData> steps = new List<TaskStepData>();
        
        [Tooltip("Automatically start the first task step on initialization.")]
        [SerializeField] private bool autoStart = true;
        
        [Tooltip("Delay in seconds before progressing to the next step after completion.")]
        [SerializeField] private float stepCompletionDelay = 1.0f;

        [Header("UI & Tracking Integration")]
        [Tooltip("Reference to the world-space HUD for displaying instructions.")]
        [SerializeField] private VrHudController statusUI;
        
        [Tooltip("List of LMS trackers (SCORM, xAPI, etc.) to notify of progress.")]
        [SerializeField] private List<BaseLmsTracker> lmsTrackers = new List<BaseLmsTracker>();

        [Header("Global Events")]
        public UnityEvent onTaskStarted;
        public UnityEvent onTaskCompleted;
        public UnityEvent<string> onStepCompleted;

        private int _currentStepIndex = -1;
        private bool _isTaskRunning = false;

        private void Awake()
        {
            if (Current == null)
            {
                Current = this;
                if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Bootstrap")
                {
                    DontDestroyOnLoad(gameObject);
                }
            }
            else
            {
                Destroy(gameObject);
                return;
            }
        }

        private void OnEnable()
        {
            if (autoStart && !_isTaskRunning && steps.Count > 0)
            {
                StartCoroutine(DelayedStartTask());
            }
        }

        private void OnDisable()
        {
            StopAllCoroutines();
        }

        private IEnumerator DelayedStartTask()
        {
            // Wait one frame to ensure all other managers are initialized
            yield return null;

            if (statusUI == null)
            {
                statusUI = Object.FindAnyObjectByType<VrHudController>(FindObjectsInactive.Include);
            }

            foreach (var tracker in lmsTrackers)
            {
                if (tracker != null) tracker.Initialize();
            }

            StartTask();
        }

        /// <summary>
        /// Begins the task sequence from the first step.
        /// </summary>
        public void StartTask()
        {
            if (steps == null || steps.Count == 0)
            {
                Debug.LogError("[TaskManager] Cannot start task: No steps defined.", this);
                return;
            }

            _currentStepIndex = 0;
            _isTaskRunning = true;
            onTaskStarted?.Invoke();
            ActivateStep(_currentStepIndex);
        }

        private void ActivateStep(int index)
        {
            if (!_isTaskRunning) return;

            if (index < 0 || index >= steps.Count)
            {
                Debug.LogWarning($"[TaskManager] Step index {index} out of range. Stopping task.", this);
                statusUI?.ShowMessage("Configuration Error", "A task step is missing. Please contact support.");
                StopTask();
                return;
            }

            TaskStepData step = steps[index];
            if (step == null) return;

            step.onStepStarted?.Invoke();
            statusUI?.ShowMessage(step.description, step.hintMessage);
            
            if (step.targetObject != null)
            {
                statusUI?.HighlightTarget(step.targetObject);
            }
        }

        /// <summary>
        /// Attempts to complete the current step by checking if the interacted object matches the target.
        /// </summary>
        public void TryCompleteWithObject(GameObject obj)
        {
            if (!_isTaskRunning || _currentStepIndex < 0 || _currentStepIndex >= steps.Count) return;

            var step = steps[_currentStepIndex];
            if (step != null && step.targetObject != null && step.targetObject.gameObject == obj)
            {
                CompleteStep(step.stepId);
            }
        }

        /// <summary>
        /// Completes a specific step by ID. Validates that it is the current active step.
        /// </summary>
        public void CompleteStep(string stepId)
        {
            if (!_isTaskRunning || _currentStepIndex < 0 || _currentStepIndex >= steps.Count) return;

            TaskStepData current = steps[_currentStepIndex];
            if (current == null || current.stepId != stepId) return;

            onStepCompleted?.Invoke(stepId);
            current.onStepCompleted?.Invoke();
            statusUI?.ClearHighlight();

            foreach (var tracker in lmsTrackers)
            {
                if (tracker != null) tracker.LogProgress(stepId, true, Time.time);
            }

            if (_currentStepIndex < steps.Count - 1)
            {
                StartCoroutine(DelayedNextStep());
            }
            else
            {
                CompleteAllTasks();
            }
        }

        private void CompleteAllTasks()
        {
            _isTaskRunning = false;
            statusUI?.ShowCompletionMessage("Training Module Complete!");
            onTaskCompleted?.Invoke();

            foreach (var tracker in lmsTrackers)
            {
                if (tracker != null) tracker.CompleteCourse("default_training_session");
            }
        }

        private IEnumerator DelayedNextStep()
        {
            yield return new WaitForSeconds(stepCompletionDelay);

            if (!_isTaskRunning) yield break;

            _currentStepIndex++;
            if (_currentStepIndex >= steps.Count)
            {
                CompleteAllTasks();
                yield break;
            }

            ActivateStep(_currentStepIndex);

            // Log attempt for the next step
            foreach (var tracker in lmsTrackers)
            {
                if (tracker != null) tracker.LogProgress(steps[_currentStepIndex].stepId, false, Time.time);
            }
        }

        /// <summary>
        /// Reports a failure for a specific step.
        /// </summary>
        public void FailStep(string stepId)
        {
            if (!_isTaskRunning) return;

            var step = steps.Find(s => s != null && s.stepId == stepId);
            if (step != null)
            {
                step.onStepFailed?.Invoke();
                foreach (var tracker in lmsTrackers)
                {
                    if (tracker != null) tracker.LogProgress(stepId, false, Time.time);
                }
            }
        }

        /// <summary>
        /// Returns the currently active step data.
        /// </summary>
        public TaskStepData GetCurrentStep()
        {
            return (_isTaskRunning && _currentStepIndex >= 0 && _currentStepIndex < steps.Count)
                ? steps[_currentStepIndex]
                : null;
        }

        /// <summary>
        /// Forces the task sequence to stop.
        /// </summary>
        public void StopTask()
        {
            _isTaskRunning = false;
            _currentStepIndex = -1;
            statusUI?.ClearHighlight();
        }
    }
}