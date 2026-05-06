using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace TrueEchoVR
{

    [RequireComponent(typeof(XRBaseInteractable))]
    public class GrabHandler : InteractionHandler
    {
        private XRBaseInteractable grabInteractable;

        private void Awake() => grabInteractable = GetComponent<XRBaseInteractable>();

        private void OnEnable() => grabInteractable.selectEntered.AddListener(OnGrabbed);
        private void OnDisable() => grabInteractable.selectEntered.RemoveListener(OnGrabbed);

        private void OnGrabbed(SelectEnterEventArgs args) => HandleCompletion();
    }
}