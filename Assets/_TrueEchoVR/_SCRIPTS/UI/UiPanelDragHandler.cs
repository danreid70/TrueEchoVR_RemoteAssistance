using UnityEngine;
using UnityEngine.EventSystems;

namespace TEVR
{
    /// <summary>
    /// Lets the user grab-and-drag the world-space UI panel with the Meta hand/controller ray.
    /// Works through the Unity EventSystem (Meta's PointableCanvasModule delivers the pointer events).
    ///
    /// Behaviour:
    ///  - Pinch + move (a drag) repositions the panel and LOCKS it where released (no more auto-follow).
    ///  - A quick pinch/tap on the panel background (no movement) resumes the normal auto-follow.
    ///  - Taps on buttons still trigger the buttons (their click is consumed before it reaches here).
    ///
    /// Attach to the MainCanvas (an ancestor of all controls) so a drag that starts on a button
    /// bubbles up to this handler, while a button's click is still handled by the button.
    /// </summary>
    [DisallowMultipleComponent]
    public class UiPanelDragHandler : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
    {
        [Tooltip("Total world movement (meters) required before a drag is treated as a real reposition (which locks the panel). Below this it is treated as a tap.")]
        public float dragLockThreshold = 0.02f;

        private Transform _canvasRoot;
        private Vector3 _grabLocal;
        private float _accumulatedMove;
        private bool _validGrab;

        private Transform CanvasRoot
        {
            get
            {
                if (_canvasRoot == null && UIManager.Instance != null)
                    _canvasRoot = UIManager.Instance.uiCanvasRoot;
                return _canvasRoot;
            }
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            _validGrab = false;
            var root = CanvasRoot;
            if (root == null) return;
            if (!eventData.pointerCurrentRaycast.isValid) return;

            Vector3 hit = eventData.pointerCurrentRaycast.worldPosition;
            _grabLocal = root.InverseTransformPoint(hit);
            _accumulatedMove = 0f;
            _validGrab = true;

            if (UIManager.Instance != null) UIManager.Instance.BeginManualDrag();
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!_validGrab) return;
            var root = CanvasRoot;
            if (root == null) return;
            if (!eventData.pointerCurrentRaycast.isValid) return;

            // Keep the originally-grabbed point on the panel underneath the pointer ray.
            // Using the ray/panel intersection point works for any ray origin (hand or controller).
            Vector3 hit = eventData.pointerCurrentRaycast.worldPosition;
            Vector3 grabWorldNow = root.TransformPoint(_grabLocal);
            Vector3 delta = hit - grabWorldNow;
            root.position += delta;
            _accumulatedMove += delta.magnitude;
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!_validGrab) return;
            _validGrab = false;
            if (UIManager.Instance == null) return;

            if (_accumulatedMove >= dragLockThreshold)
                UIManager.Instance.EndManualDragAndLock();
            else
                UIManager.Instance.ResumeFollow();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            // A quick tap on the panel background (not a drag) resumes auto-follow.
            if (eventData.dragging) return;
            if (UIManager.Instance != null) UIManager.Instance.ResumeFollow();
        }
    }
}
