using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Completes the linked task step when this object is grabbed (select entered).
/// Attach to a GameObject that also has an XRGrabInteractable component.
/// </summary>
[RequireComponent(typeof(XRGrabInteractable))]
public class GrabHandler : InteractionHandler
{
    private XRGrabInteractable grabInteractable;

    private void Awake()
    {
        grabInteractable = GetComponent<XRGrabInteractable>();
        if (grabInteractable == null)
        {
            Debug.LogError($"{nameof(GrabHandler)} requires an {nameof(XRGrabInteractable)} on the same GameObject.", this);
        }
    }

    private void OnEnable()
    {
        if (grabInteractable != null)
            grabInteractable.selectEntered.AddListener(OnGrabbed);
    }

    private void OnDisable()
    {
        if (grabInteractable != null)
            grabInteractable.selectEntered.RemoveListener(OnGrabbed);
    }

    private void OnGrabbed(SelectEnterEventArgs args)
    {
        HandleCompletion();
    }
}