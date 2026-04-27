using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using System.Collections.Generic;

namespace VRAssemblyFactory
{
    public class SocketTagFilter : MonoBehaviour, IXRSelectFilter, IXRHoverFilter
    {
        [Tooltip("The socket will only interact with objects that have one of these tags.")]
        public List<string> targetTags = new List<string>();

        public bool canProcess => true;

        // Logic for Selection (Actually snapping into the socket)
        public bool Process(IXRSelectInteractor interactor, IXRSelectInteractable interactable)
        {
            return IsTagValid(interactable.transform);
        }

        // Logic for Hover (The visual highlight/cue)
        public bool Process(IXRHoverInteractor interactor, IXRHoverInteractable interactable)
        {
            return IsTagValid(interactable.transform);
        }

        private bool IsTagValid(Transform obj)
        {
            if (targetTags == null || targetTags.Count == 0) return false;

            // Check if the object's tag is anywhere in our list
            foreach (string t in targetTags)
            {
                if (obj.CompareTag(t)) return true;
            }

            return false;
        }
    }
}