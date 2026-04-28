using UnityEngine;

/// <summary>
/// Base class for auto‑matching interaction handlers.
/// Calls TaskManager.Current.TryCompleteWithObject(gameObject) by default.
/// Also supports an optional linkedStepId for manual fallback.
/// </summary>
public abstract class InteractionHandler : MonoBehaviour
{
    [Tooltip("Optional: fallback step ID. Leave empty to use the object itself as the target match.")]
    [SerializeField] protected string linkedStepId;

    protected virtual void HandleCompletion()
    {
        if (TaskManager.Current == null)
        {
            Debug.LogWarning($"[{GetType().Name}] No active TaskManager found.", this);
            return;
        }

        // Auto‑match by the object this handler is attached to
        TaskManager.Current.TryCompleteWithObject(gameObject);

        // If a manual ID is set, also try that (covers cases where targetObject != the interactable)
        if (!string.IsNullOrEmpty(linkedStepId))
            TaskManager.Current.CompleteStep(linkedStepId);
    }
}