using System;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Contains all data for a single task step, including its UI text,
/// an optional target object for the HUD arrow, and UnityEvents
/// for inspector‑driven behaviour.
/// </summary>
[Serializable]
public class TaskStepData
{
    [Tooltip("Unique ID used by interaction handlers to complete this step.")]
    public string stepId;

    [TextArea]
    public string description;

    [TextArea]
    public string hintMessage;

    [Tooltip("For reference only – the interaction handler decides completion.")]
    public ConditionType completionCondition;

    [Tooltip("Scene object the HUD arrow should point at during this step (optional).")]
    public Transform targetObject;

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