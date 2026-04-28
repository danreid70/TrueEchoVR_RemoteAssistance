using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Completes the task step when a UI Button is clicked.
/// Attach to the same GameObject as the Button.
/// </summary>
[RequireComponent(typeof(Button))]
public class UISelectionHandler : InteractionHandler
{
    private Button uiButton;

    private void Awake()
    {
        uiButton = GetComponent<Button>();
    }

    private void OnEnable()
    {
        if (uiButton != null)
            uiButton.onClick.AddListener(HandleCompletion);
    }

    private void OnDisable()
    {
        if (uiButton != null)
            uiButton.onClick.RemoveListener(HandleCompletion);
    }
}