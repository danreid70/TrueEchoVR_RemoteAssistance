using UnityEngine;
using UnityEngine.UI;

namespace TEVR
{

    [RequireComponent(typeof(Button))]
    public class UiSelectionHandler : BaseInteractionHandler
    {
        private Button uiButton;

        private void Awake() => uiButton = GetComponent<Button>();

        private void OnEnable() => uiButton.onClick.AddListener(HandleCompletion);
        private void OnDisable() => uiButton.onClick.RemoveListener(HandleCompletion);
    }
}