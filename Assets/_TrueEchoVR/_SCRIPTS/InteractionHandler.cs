using UnityEngine;

/// <summary>
/// Base class for all XRI‑based interaction handlers.
/// Reports completion to a TaskManager when the linked step is satisfied.
/// </summary>
public abstract class InteractionHandler : MonoBehaviour
{
    [Tooltip("Reference to the TaskManager that controls the sequence.")]
    [SerializeField] protected TaskManager taskManager;

    [Tooltip("Must match the stepId of the TaskStep this interaction satisfies.")]
    [SerializeField] protected string linkedStepId;

    /// <summary>
    /// Called by derived classes when the interaction condition is met.
    /// </summary>
    protected virtual void HandleCompletion()
    {
        if (taskManager != null)
            taskManager.CompleteStep(linkedStepId);
    }
}