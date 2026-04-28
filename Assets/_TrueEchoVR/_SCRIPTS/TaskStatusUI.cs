using UnityEngine;

/// <summary>
/// Bridges TaskManager commands to the dynamic VRHUDManager.
/// No UI elements need to be assigned manually.
/// </summary>
public class TaskStatusUI : MonoBehaviour
{
    private VRHUDManager hud;

    private void Start()
    {
        hud = VRHUDManager.Instance;
        if (hud == null)
            Debug.LogError("TaskStatusUI: No VRHUDManager found in scene.");
    }

    public void ShowMessage(string mainText, string hint)
    {
        hud?.SetStatus(mainText, hint);
    }

    public void HighlightTarget(GameObject target)
    {
        hud?.ClearHighlight();   // compatibility, no effect in this HUD
    }

    public void ClearHighlight()
    {
        hud?.ClearHighlight();
    }

    public void ShowCompletionMessage(string message)
    {
        hud?.ShowCompletionMessage(message);
    }
}