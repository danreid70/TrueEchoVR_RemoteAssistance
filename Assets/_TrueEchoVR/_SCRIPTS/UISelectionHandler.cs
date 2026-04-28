using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
public class UISelectionHandler : InteractionHandler
{
    private Button uiButton;

    private void Awake() => uiButton = GetComponent<Button>();

    private void OnEnable() => uiButton.onClick.AddListener(HandleCompletion);
    private void OnDisable() => uiButton.onClick.RemoveListener(HandleCompletion);
}