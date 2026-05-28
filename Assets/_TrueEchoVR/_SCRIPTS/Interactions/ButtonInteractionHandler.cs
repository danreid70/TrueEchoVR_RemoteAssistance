using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace TEVR
{

    [RequireComponent(typeof(XRSimpleInteractable))]
    public class ButtonInteractionHandler : BaseInteractionHandler
    {
        private XRSimpleInteractable simpleInteractable;

        private void Awake() => simpleInteractable = GetComponent<XRSimpleInteractable>();

        private void OnEnable() => simpleInteractable.selectEntered.AddListener(OnPressed);
        private void OnDisable() => simpleInteractable.selectEntered.RemoveListener(OnPressed);

        private void OnPressed(SelectEnterEventArgs args) => HandleCompletion();
    }
}