using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace TEVR
{
    /// <summary>
    /// Unity's EventSystem only ever runs ONE input module (the first one whose
    /// ShouldActivateModule() returns true). This project has TWO modules on the EventSystem:
    ///   - PointableCanvasModule (Meta Interaction SDK) -> drives the hand/controller ray on device.
    ///   - InputSystemUIInputModule -> drives the mouse/keyboard for in-editor testing.
    /// If both are enabled, PointableCanvasModule (listed first) wins and the mouse never works.
    ///
    /// This switcher enables exactly ONE module based on whether a real XR headset is present:
    ///   - On a device build, or in the editor with Quest Link connected -> Meta hand module.
    ///   - In the editor with NO headset -> mouse module (so you can click/drag to test in Play Mode).
    ///
    /// It re-checks for a few seconds so connecting Link slightly late is handled gracefully.
    /// </summary>
    [DefaultExecutionOrder(-500)]
    [DisallowMultipleComponent]
    public class UiEventSystemModeSwitcher : MonoBehaviour
    {
        [Tooltip("How long after start to keep re-checking for an XR headset (covers Quest Link connecting late).")]
        [SerializeField] private float xrDetectWindowSeconds = 6f;
        [SerializeField] private float recheckInterval = 0.5f;
        [SerializeField] private bool verboseLogging = true;

        private BaseInputModule _pointableModule;          // Meta PointableCanvasModule (matched by type name)
        private InputSystemUIInputModule _inputSystemModule; // Mouse/keyboard module

        private bool _hasApplied;
        private bool _lastXr;

        private void Awake()
        {
            foreach (var m in GetComponents<BaseInputModule>())
            {
                if (m == null) continue;
                if (m.GetType().Name == "PointableCanvasModule") _pointableModule = m;
                if (m is InputSystemUIInputModule isim) _inputSystemModule = isim;
            }
        }

        private IEnumerator Start()
        {
            ApplyMode(IsXrActive());

            float elapsed = 0f;
            while (elapsed < xrDetectWindowSeconds)
            {
                yield return new WaitForSeconds(recheckInterval);
                elapsed += recheckInterval;
                ApplyMode(IsXrActive());
            }
        }

        /// <summary>
        /// True when a real headset should drive the UI:
        /// always true in a player build; in the editor only when an HMD input device is present (Link).
        /// </summary>
        private bool IsXrActive()
        {
            if (!Application.isEditor) return true; // device build always uses the Meta hand ray

            var devices = new List<UnityEngine.XR.InputDevice>();
            UnityEngine.XR.InputDevices.GetDevicesWithCharacteristics(
                UnityEngine.XR.InputDeviceCharacteristics.HeadMounted, devices);
            foreach (var d in devices) if (d.isValid) return true;
            return false; // editor, no headset -> use mouse
        }

        private void ApplyMode(bool xr)
        {
            if (_hasApplied && _lastXr == xr) return;
            _hasApplied = true;
            _lastXr = xr;

            if (_pointableModule != null) _pointableModule.enabled = xr;
            if (_inputSystemModule != null) _inputSystemModule.enabled = !xr;

            if (verboseLogging)
                Debug.Log("[UiInputSwitcher] XR headset active=" + xr + " -> active module: " +
                          (xr ? "PointableCanvasModule (hand/controller ray)" : "InputSystemUIInputModule (mouse/keyboard)"));
        }
    }
}
