using UnityEngine;
using TMPro;
using UnityEngine.EventSystems;
using System.Collections.Generic;

namespace TEVR
{
    /// <summary>
    /// Manages the VR System Keyboard for Meta Quest.
    /// Triggers the keyboard when an InputField is focused and hides it when focus is lost.
    /// </summary>
    public class VrKeyboardManager : MonoBehaviour
    {
        private TMP_InputField _activeField;
        private TouchScreenKeyboard _keyboard;
        private HashSet<TMP_InputField> _registeredFields = new HashSet<TMP_InputField>();

        private void Start()
        {
            RefreshFields();
        }

        /// <summary>
        /// Finds and registers all TMP_InputFields in the scene.
        /// </summary>
        public void RefreshFields()
        {
            var fields = Object.FindObjectsByType<TMP_InputField>(FindObjectsInactive.Include);
            foreach (var field in fields)
{
                if (!_registeredFields.Contains(field))
                {
                    SetupField(field);
                    _registeredFields.Add(field);
                }
            }
        }

        private void SetupField(TMP_InputField field)
        {
            EventTrigger trigger = field.gameObject.GetComponent<EventTrigger>();
            if (trigger == null) trigger = field.gameObject.AddComponent<EventTrigger>();

            // Select (Focus)
            EventTrigger.Entry selectEntry = new EventTrigger.Entry { eventID = EventTriggerType.Select };
            selectEntry.callback.AddListener((data) => OnFieldFocused(field));
            trigger.triggers.Add(selectEntry);

            // Deselect (Unfocus)
            EventTrigger.Entry deselectEntry = new EventTrigger.Entry { eventID = EventTriggerType.Deselect };
            deselectEntry.callback.AddListener((data) => OnFieldUnfocused(field));
            trigger.triggers.Add(deselectEntry);
        }

        private void OnFieldFocused(TMP_InputField field)
        {
            // Only attempt to open the system keyboard if supported
            if (!TouchScreenKeyboard.isSupported)
            {
                Debug.Log($"[VrKeyboardManager] System keyboard not supported on this platform. Use physical keyboard in Editor.");
                return;
            }

            _activeField = field;
            // Open the system keyboard (overlay)
            _keyboard = TouchScreenKeyboard.Open(field.text, TouchScreenKeyboardType.Default, false, false, false, false);
            
            if (_keyboard == null)
            {
                Debug.LogWarning("[VrKeyboardManager] Failed to open TouchScreenKeyboard.");
                _activeField = null;
                return;
            }

            Debug.Log($"[VrKeyboardManager] Keyboard opened for: {field.name}");
        }

        private void OnFieldUnfocused(TMP_InputField field)
        {
            if (_activeField == field)
            {
                _activeField = null;
                if (_keyboard != null)
                {
                    // Guard against potential native null refs during close
                    try { _keyboard.active = false; } catch { }
                    _keyboard = null;
                }
                Debug.Log($"[VrKeyboardManager] Keyboard closed for: {field.name}");
            }
        }

        private void Update()
        {
            if (_activeField == null || _keyboard == null) return;

            // On some platforms (like Editor with Android target), accessing properties 
            // on the keyboard handle can throw a native NullReferenceException if 
            // the system keyboard failed to initialize properly.
            try
            {
                bool isActive = _keyboard.active;

                // Sync text
                if (isActive)
                {
                    _activeField.text = _keyboard.text;
                }
                
                // Handle keyboard closing
                if (_keyboard.status == TouchScreenKeyboard.Status.Done || 
                    _keyboard.status == TouchScreenKeyboard.Status.Canceled || 
                    !isActive)
                {
                    OnFieldUnfocused(_activeField);
                }
            }
            catch (System.Exception)
            {
                // If we hit a native null ref, clean up and stop tracking this keyboard
                _keyboard = null;
                _activeField = null;
            }
        }
    }
}