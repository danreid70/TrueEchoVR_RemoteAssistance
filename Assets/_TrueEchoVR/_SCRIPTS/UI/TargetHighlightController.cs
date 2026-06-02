using UnityEngine;
using TMPro;

namespace TEVR
{
    /// <summary>
    /// Visualizes a target location or object in 3D space.
    /// Used for remote expert 'pointing' and 'outlining' instructions.
    /// </summary>
    public class TargetHighlightController : MonoBehaviour
    {
        [Header("Visuals")]
        public GameObject outlineBox;
        public GameObject pointerArrow;
        public TextMeshPro labelText;
        
        [Header("Animation")]
        public float pulseSpeed = 2f;
        public float minScale = 0.95f;
        public float maxScale = 1.05f;

        private Transform _cameraTransform;
        private Vector3 _baseScale;
        private bool _isPersistent = false;

        private void Awake()
        {
            _cameraTransform = Camera.main?.transform;
            _baseScale = outlineBox != null ? outlineBox.transform.localScale : Vector3.one;
            Hide();
        }

        public void HighlightPosition(string label, Vector3 position, Quaternion rotation, bool persistent = false)
        {
            transform.SetPositionAndRotation(position, rotation);
            if (labelText != null) labelText.text = label;
            
            _isPersistent = persistent;
            gameObject.SetActive(true);
            
            if (pointerArrow != null) pointerArrow.SetActive(true);
            if (outlineBox != null) outlineBox.SetActive(true);
        }

        public void Hide()
        {
            if (_isPersistent) return;
            gameObject.SetActive(false);
        }

        public void ClearForce()
        {
            _isPersistent = false;
            Hide();
        }

        private void Update()
        {
            if (!gameObject.activeSelf) return;

            // Billboarding for the label
            if (labelText != null && _cameraTransform != null)
            {
                labelText.transform.LookAt(labelText.transform.position + _cameraTransform.forward);
            }

            // Pulse effect
            if (outlineBox != null)
            {
                float pulse = Mathf.PingPong(Time.time * pulseSpeed, 1.0f);
                float scaleMultiplier = Mathf.Lerp(minScale, maxScale, pulse);
                outlineBox.transform.localScale = _baseScale * scaleMultiplier;
            }
        }
    }
}
