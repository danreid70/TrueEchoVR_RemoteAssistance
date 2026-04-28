using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

[RequireComponent(typeof(XRSocketInteractor))]
public class SnapHandler : InteractionHandler
{
    private XRSocketInteractor socketInteractor;

    private void Awake() => socketInteractor = GetComponent<XRSocketInteractor>();

    private void OnEnable() => socketInteractor.selectEntered.AddListener(OnSnapped);
    private void OnDisable() => socketInteractor.selectEntered.RemoveListener(OnSnapped);

    private void OnSnapped(SelectEnterEventArgs args) => HandleCompletion();
}