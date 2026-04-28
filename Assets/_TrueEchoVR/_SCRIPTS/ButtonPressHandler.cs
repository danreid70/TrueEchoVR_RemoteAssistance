using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Completes the task step when a 3D button is pressed (select entered).
/// Attach to a GameObject that also has an XRSimpleInteractable component.
/// </summary>
[RequireComponent(typeof(XRSimpleInteractable))]
public class ButtonPressHandler : InteractionHandler
{
    private XRSimpleInteractable simpleInteractable;

    private void Awake()
    {
        simpleInteractable = GetComponent<XRSimpleInteractable>();
        if (simpleInteractable == null)
        {
            Debug.LogError($"{nameof(ButtonPressHandler)} requires an {nameof(XRSimpleInteractable)} on the same GameObject.", this);
        }
    }

    private void OnEnable()
    {
        if (simpleInteractable != null)
            simpleInteractable.selectEntered.AddListener(OnPressed);
    }

    private void OnDisable()
    {
        if (simpleInteractable != null)
            simpleInteractable.selectEntered.RemoveListener(OnPressed);
    }

    private void OnPressed(SelectEnterEventArgs args)
    {
        HandleCompletion();
    }
}