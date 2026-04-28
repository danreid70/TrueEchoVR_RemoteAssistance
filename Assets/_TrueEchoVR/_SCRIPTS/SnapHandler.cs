using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Completes the task step when an interactable is snapped into this socket.
/// Attach to a GameObject that also has an XRSocketInteractor component.
/// </summary>
[RequireComponent(typeof(XRSocketInteractor))]
public class SnapHandler : InteractionHandler
{
    private XRSocketInteractor socketInteractor;

    private void Awake()
    {
        socketInteractor = GetComponent<XRSocketInteractor>();
        if (socketInteractor == null)
        {
            Debug.LogError($"{nameof(SnapHandler)} requires an {nameof(XRSocketInteractor)} on the same GameObject.", this);
        }
    }

    private void OnEnable()
    {
        if (socketInteractor != null)
            socketInteractor.selectEntered.AddListener(OnSnapped);
    }

    private void OnDisable()
    {
        if (socketInteractor != null)
            socketInteractor.selectEntered.RemoveListener(OnSnapped);
    }

    private void OnSnapped(SelectEnterEventArgs args)
    {
        HandleCompletion();
    }
}