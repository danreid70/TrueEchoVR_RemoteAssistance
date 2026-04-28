using UnityEngine;

public class TaskStatusUI : MonoBehaviour
{
    private VRHUDManager hud;

    private void Start()
    {
        hud = VRHUDManager.Instance;
        if (hud == null)
            Debug.LogError("[TaskStatusUI] No VRHUDManager found in scene.");
    }

    public void ShowMessage(string mainText, string hint)
    {
        hud?.SetStatus(mainText, hint);
    }

    public void HighlightTarget(GameObject target)
    {
        if (hud != null)
        {
            if (target != null)
                hud.SetTarget(target.transform);
            else
                hud.ClearHighlight();
        }
    }

    public void ClearHighlight()
    {
        hud?.ClearHighlight();   // removes the pointer
    }

    public void ShowCompletionMessage(string message)
    {
        hud?.ShowCompletionMessage(message);
    }
}