using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TEVR
{
    /// <summary>
    /// Adds hover/press feedback to a uGUI element: smooth scaling, a cyan glow
    /// <see cref="Outline"/>, and UI sound effects. Driven by standard Unity
    /// EventSystem pointer events, which Meta's PointableCanvasModule dispatches.
    /// </summary>
    [DisallowMultipleComponent]
    public class UiButtonFx : MonoBehaviour,
        IPointerEnterHandler,
        IPointerExitHandler,
        IPointerDownHandler,
        IPointerUpHandler
    {
        [Header("Scale")]
        [SerializeField] private float hoverScale = 1.06f;
        [SerializeField] private float pressedScale = 0.96f;
        [SerializeField] private float scaleSpeed = 12f;

        [Header("Glow")]
        [SerializeField] private float normalGlowDistance = 1f;
        [SerializeField] private float hoverGlowDistance = 3f;

        private RectTransform _rect;
        private Outline _outline;
        private Vector3 _originalScale = Vector3.one;
        private float _targetScaleFactor = 1f;
        private bool _isHovered;
        private bool _isPressed;

        private void Awake()
        {
            _rect = transform as RectTransform;
            _originalScale = transform.localScale;

            _outline = GetComponent<Outline>();
            if (_outline == null)
            {
                _outline = gameObject.AddComponent<Outline>();
            }

            _outline.effectColor = TevrUiTheme.Accent;
            SetGlow(normalGlowDistance);
        }

        private void Update()
        {
            Vector3 desired = _originalScale * _targetScaleFactor;
            transform.localScale = Vector3.Lerp(transform.localScale, desired, Time.unscaledDeltaTime * scaleSpeed);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _isHovered = true;
            RefreshState();

            if (UiSfx.Instance != null)
            {
                UiSfx.Instance.PlayHover();
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _isHovered = false;
            _isPressed = false;
            RefreshState();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            _isPressed = true;
            RefreshState();

            if (UiSfx.Instance != null)
            {
                UiSfx.Instance.PlayClick();
            }
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            _isPressed = false;
            RefreshState();
        }

        private void RefreshState()
        {
            if (_isPressed)
            {
                _targetScaleFactor = pressedScale;
                SetGlow(hoverGlowDistance);
            }
            else if (_isHovered)
            {
                _targetScaleFactor = hoverScale;
                SetGlow(hoverGlowDistance);
            }
            else
            {
                _targetScaleFactor = 1f;
                SetGlow(normalGlowDistance);
            }
        }

        private void SetGlow(float distance)
        {
            if (_outline == null)
            {
                return;
            }

            _outline.effectColor = TevrUiTheme.Accent;
            _outline.effectDistance = new Vector2(distance, -distance);
        }
    }
}
