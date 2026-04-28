using UnityEngine;
using UnityEngine.Events;
using System;

[CreateAssetMenu(fileName = "NewTaskStep", menuName = "Training/Task Step")]
public class TaskStep : ScriptableObject
{
    public string stepId;
    public string description;
    public string hintMessage;
    public ConditionType completionCondition;
    public GameObject targetObject;       // Object to interact with for this step
    public string requiredTag;            // Tag or identifier for the target
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