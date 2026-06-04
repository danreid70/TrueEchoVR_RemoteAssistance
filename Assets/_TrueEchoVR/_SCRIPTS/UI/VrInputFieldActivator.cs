using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;

namespace TEVR
{
    /// <summary>
    /// Meta's PointableCanvasModule (derived from PointerInputModule) delivers pointer
    /// down/up/CLICK events — which is why Buttons work with the hand ray — but it does NOT
    /// run the selection logic that StandaloneInputModule/InputSystemUIInputModule do. As a
    /// result a TMP_InputField is never made the EventSystem's "selected" object, so its
    /// onSelect event never fires and the on-screen keyboard never opens on device.
    ///
    /// This component restores the missing step: on a pointer click it explicitly selects and
    /// activates the field. Selecting the field raises onSelect, which the project's
    /// SessionUiController.SetupInputFieldKeyboard() listens to in order to open the VR keyboard.
    /// Pointer clicks ARE delivered to this object (same path Buttons use), so this is reliable.
    /// </summary>
    [RequireComponent(typeof(TMP_InputField))]
    [DisallowMultipleComponent]
    public class VrInputFieldActivator : MonoBehaviour, IPointerClickHandler
    {
        private TMP_InputField _input;

        private void Awake() => _input = GetComponent<TMP_InputField>();

        public void OnPointerClick(PointerEventData eventData)
        {
            if (_input == null || !_input.interactable || !_input.enabled) return;

            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(gameObject, eventData);

            // Show the caret and raise onSelect (opens the VR keyboard via SessionUiController).
            _input.ActivateInputField();
        }
    }
}
