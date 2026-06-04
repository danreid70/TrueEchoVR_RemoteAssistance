using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
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

        [Header("Persistence (offline / restart resilience)")]
        [Tooltip("Save progress locally and resume from the last incomplete step after a restart " +
                 "(e.g. if the cloud connection dropped before the session finished).")]
        [SerializeField] private bool persistProgress = true;

        [Tooltip("File name used to persist task progress under Application.persistentDataPath.")]
        [SerializeField] private string progressFileName = "TaskProgress.json";

        [Serializable]
        private class TaskProgress
        {
            public string sessionSignature; // identifies the step-set so we don't resume a different task
            public int currentStepIndex;
            public bool completed;
            public List<string> completedStepIds = new List<string>();
            public string lastUpdatedUtc;
        }

        private readonly HashSet<string> _completedStepIds = new HashSet<string>();

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
                statusUI = UnityEngine.Object.FindAnyObjectByType<VrHudController>(FindObjectsInactive.Include);
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

            _completedStepIds.Clear();

            // Resume from persisted progress if a compatible, unfinished session exists.
            int startIndex = 0;
            if (persistProgress && TryLoadProgress(out var saved))
            {
                if (saved.completed)
                {
                    // The previous run already finished this exact task set — start fresh.
                    Debug.Log("[TaskManager] Saved progress was already complete. Starting a new run.");
                    ClearProgress();
                }
                else
                {
                    if (saved.completedStepIds != null)
                        foreach (var id in saved.completedStepIds) _completedStepIds.Add(id);

                    startIndex = Mathf.Clamp(saved.currentStepIndex, 0, steps.Count - 1);
                    Debug.Log($"[TaskManager] Resuming task from step {startIndex} ({_completedStepIds.Count} step(s) already completed).");
                }
            }

            _currentStepIndex = startIndex;
            _isTaskRunning = true;
            onTaskStarted?.Invoke();
            ActivateStep(_currentStepIndex);
            SaveProgress();
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

            if (!string.IsNullOrEmpty(stepId)) _completedStepIds.Add(stepId);

            foreach (var tracker in lmsTrackers)
            {
                if (tracker != null) tracker.LogProgress(stepId, true, Time.time);
            }

            SaveProgress();

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

            // Persist the completed state so a restart starts a fresh run instead of resuming the last step.
            SaveProgress(markCompleted: true);
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
            SaveProgress();

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

        #region Persistence

        private string ProgressFilePath =>
            Path.Combine(Application.persistentDataPath, string.IsNullOrEmpty(progressFileName) ? "TaskProgress.json" : progressFileName);

        /// <summary>
        /// A signature of the current step set so we never resume progress that belongs to a
        /// different/edited task list (which would point at the wrong steps).
        /// </summary>
        private string BuildSessionSignature()
        {
            var ids = new List<string>(steps.Count);
            foreach (var s in steps) ids.Add(s != null ? s.stepId : "<null>");
            return $"{steps.Count}:{string.Join("|", ids)}";
        }

        /// <summary>Writes the current progress to disk. Safe to call frequently.</summary>
        public void SaveProgress(bool markCompleted = false)
        {
            if (!persistProgress) return;
            try
            {
                var data = new TaskProgress
                {
                    sessionSignature = BuildSessionSignature(),
                    currentStepIndex = _currentStepIndex,
                    completed = markCompleted,
                    completedStepIds = new List<string>(_completedStepIds),
                    lastUpdatedUtc = DateTime.UtcNow.ToString("O")
                };
                File.WriteAllText(ProgressFilePath, JsonUtility.ToJson(data, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TaskManager] Could not save task progress: {e.Message}");
            }
        }

        /// <summary>Loads saved progress, returning false if none exists or it belongs to a different task set.</summary>
        private bool TryLoadProgress(out TaskProgress progress)
        {
            progress = null;
            if (!persistProgress) return false;
            try
            {
                string path = ProgressFilePath;
                if (!File.Exists(path)) return false;

                var data = JsonUtility.FromJson<TaskProgress>(File.ReadAllText(path));
                if (data == null) return false;

                // Reject progress saved against a different step list.
                if (data.sessionSignature != BuildSessionSignature())
                {
                    Debug.Log("[TaskManager] Saved progress is for a different task set; ignoring it.");
                    return false;
                }

                progress = data;
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TaskManager] Could not load task progress: {e.Message}");
                return false;
            }
        }

        /// <summary>Deletes any persisted progress (e.g. to force a clean restart).</summary>
        public void ClearProgress()
        {
            _completedStepIds.Clear();
            try
            {
                string path = ProgressFilePath;
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TaskManager] Could not clear task progress: {e.Message}");
            }
        }

        /// <summary>True if a given step id has already been completed in this (possibly resumed) session.</summary>
        public bool IsStepCompleted(string stepId) => _completedStepIds.Contains(stepId);

        #endregion
    }
}