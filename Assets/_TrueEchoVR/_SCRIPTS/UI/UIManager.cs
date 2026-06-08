using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using System;

namespace TEVR
{
    /// <summary>
    /// Centralized UI Manager for the TrueEchoVR MR System.
    /// Handles positioning, visibility, and consolidation of all UI panels.
    /// </summary>
    public class UIManager : MonoBehaviour
    {
        public static UIManager Instance { get; private set; }

        [Header("UI Groups")]
        public UIPanelGroup hudGroup;
        public UIPanelGroup sessionUiGroup;

        [Header("Global Settings")]
        public Transform mainCamera;

        [Header("Canvas Lazy Follow")]
        [Tooltip("The world-space Canvas that holds the collider/PointableCanvas. This single transform is moved, so the interactable surface always stays aligned with the visible UI.")]
        public Transform uiCanvasRoot;
        [Tooltip("Distance in meters the canvas is placed in front of the camera when it recenters.")]
        public float followDistance = 1.3f;
        [Tooltip("Vertical offset (meters) applied when recentering. 0 keeps it at eye height.")]
        public float followVerticalOffset = 0f;
        [Tooltip("If the canvas drifts more than this many degrees from the view center, it recenters back into view.")]
        public float viewAngleThreshold = 35f;
        [SerializeField] private float followSmoothTime = 0.25f;
        [SerializeField] private float faceRotationSpeed = 8f;

        private Vector3 _followVelocity;
        private bool _isRecentering;

        // Drag-to-reposition state.
        // _isDragging: a drag is in progress (the drag handler is setting the position directly).
        // _isLocked:   the user dragged-and-released, so the panel stays put and will NOT auto-recenter
        //              until a quick tap on the background resumes following.
        private bool _isDragging;
        private bool _isLocked;
        public bool IsLocked => _isLocked;

        [Header("Controllers")]
        public VrHudController hudController;
        public SessionUiController sessionController;
        // NOTE: the directional arrow is driven directly by VrHudController.pointerArrow (a GameObject it
        // rotates in LateUpdate). The previous PointerArrowController-based path here was redundant and has
        // been removed to avoid two drivers fighting over the same transform.
        public TargetHighlightController remoteHighlight;

        public enum UIState { None, Login, Calibration, Session }

        [Header("State Management")]
        [SerializeField] private UIState currentState = UIState.Login;
        public Action<UIState> OnUIStateChanged;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Bootstrap")
                {
                    DontDestroyOnLoad(gameObject);
                }
            }
            else
            {
                Destroy(gameObject);
                return;
            }

            if (mainCamera == null) FindMainCamera();
        }

        private void FindMainCamera()
        {
            mainCamera = Camera.main?.transform;
            
            if (mainCamera == null)
            {
                // Fallback to searching in PersistentXRRig
                var persistentRig = UnityEngine.Object.FindAnyObjectByType<TEVR.Core.PersistentXRRig>();
                if (persistentRig != null)
{
                    var cam = persistentRig.GetComponentInChildren<Camera>();
                    if (cam != null) mainCamera = cam.transform;
                }
            }
        }

        private void Start()
        {
            if (mainCamera == null) FindMainCamera();

            // Place the canvas in front of the user immediately so it starts in view.
            if (mainCamera != null && uiCanvasRoot != null)
            {
                uiCanvasRoot.position = ComputeCanvasTarget(mainCamera);
                uiCanvasRoot.rotation = FaceCameraYaw(mainCamera);
            }

            // Ensure the world-space canvas has an event camera. This is required for the
            // GraphicRaycaster (mouse fallback in the editor) and harmless for the Meta ray path.
            if (uiCanvasRoot != null)
            {
                var canvas = uiCanvasRoot.GetComponent<Canvas>();
                if (canvas != null && canvas.renderMode == RenderMode.WorldSpace)
                {
                    // Use the center eye camera as the event camera
                    if (mainCamera != null)
                    {
                        var cam = mainCamera.GetComponent<Camera>();
                        if (cam != null) canvas.worldCamera = cam;
                    }
                    
                    if (canvas.worldCamera == null)
                    {
                         canvas.worldCamera = Camera.main;
                    }
                }

                // Cleanup: Remove XRI raycaster if it's conflicting with Meta interaction
                var tdgr = uiCanvasRoot.GetComponent("TrackedDeviceGraphicRaycaster");
                if (tdgr != null)
                {
                    Debug.Log("[UIManager] Removing TrackedDeviceGraphicRaycaster to prevent conflict.");
                    Destroy(tdgr);
                }
            }

            // Trigger initial state
            SetState(currentState);
        }

        private void LateUpdate()
        {
            if (mainCamera == null || !mainCamera.gameObject.activeInHierarchy)
            {
                FindMainCamera();
                if (mainCamera == null) return;
            }

            UpdateCanvasFollow(mainCamera);
        }

        /// <summary>
        /// Moves the single world-space canvas (which carries the collider/PointableCanvas)
        /// so the interactable surface and the visible UI always stay together.
        /// Behavior: always billboards toward the user on the Y axis, stays put while in view,
        /// and only glides back to a comfortable forward position when it leaves the field of view.
        /// </summary>
        private void UpdateCanvasFollow(Transform cam)
        {
            if (uiCanvasRoot == null) return;

            // 1. Always face the user (yaw only - the panel stays upright and faces the headset).
            // We do this even while dragging, as requested by the user.
            Quaternion targetRot = FaceCameraYaw(cam);
            uiCanvasRoot.rotation = Quaternion.Slerp(uiCanvasRoot.rotation, targetRot, faceRotationSpeed * Time.deltaTime);

            // While the user is actively dragging, the drag handler owns the transform POSITION completely.
            if (_isDragging) return;

            // 2. If the user dragged-and-released, the panel is locked in place: no auto-recenter.
            if (_isLocked) return;

            // 3. Decide whether the canvas needs to come back into view.
            Vector3 toCanvas = uiCanvasRoot.position - cam.position;
            float distance = toCanvas.magnitude;
            float angleFromView = (distance > 0.0001f) ? Vector3.Angle(cam.forward, toCanvas) : 0f;
            bool outOfRange = distance < followDistance * 0.5f || distance > followDistance * 2.0f;

            if (angleFromView > viewAngleThreshold || outOfRange)
            {
                _isRecentering = true;
            }

            // 4. Glide back to a comfortable spot directly ahead, then stop and stay put.
            if (_isRecentering)
            {
                Vector3 targetPos = ComputeCanvasTarget(cam);
                uiCanvasRoot.position = Vector3.SmoothDamp(uiCanvasRoot.position, targetPos, ref _followVelocity, followSmoothTime);

                if (Vector3.Distance(uiCanvasRoot.position, targetPos) < 0.05f)
                {
                    _isRecentering = false;
                }
            }
        }

        // ---- Drag-to-reposition API (called by UiPanelDragHandler) ----

        /// <summary>Begin a manual drag: suspends auto-follow so the drag handler can move the panel.</summary>
        public void BeginManualDrag()
        {
            _isDragging = true;
            _isRecentering = false;
            _followVelocity = Vector3.zero;
        }

        /// <summary>End a drag that actually moved the panel: lock it in place (no auto-recenter).</summary>
        public void EndManualDragAndLock()
        {
            _isDragging = false;
            _isLocked = true;
            _followVelocity = Vector3.zero;
        }

        /// <summary>Resume normal follow/recenter behavior (e.g. after a quick tap on the background).</summary>
        public void ResumeFollow()
        {
            _isDragging = false;
            _isLocked = false;
            _isRecentering = true; // glide back into view immediately
        }

        /// <summary>The camera transform the drag handler should use for ray math.</summary>
        public Transform ActiveCamera => mainCamera;

        private Vector3 ComputeCanvasTarget(Transform cam)
        {
            Vector3 flatForward = cam.forward;
            flatForward.y = 0f;
            if (flatForward.sqrMagnitude < 0.0001f)
                flatForward = Vector3.ProjectOnPlane(cam.up, Vector3.up).normalized;
            else
                flatForward.Normalize();

            // No horizontal offset - keep it centered in the user's view.
            return cam.position + flatForward * followDistance + Vector3.up * followVerticalOffset;
        }

        private Quaternion FaceCameraYaw(Transform cam)
        {
            // Canvas +Z must point away from the camera so its front face is readable by the user.
            if (uiCanvasRoot == null) return Quaternion.identity;
            Vector3 forward = uiCanvasRoot.position - cam.position;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
            {
                Vector3 camFwd = cam.forward;
                camFwd.y = 0f;
                if (camFwd.sqrMagnitude < 0.0001f) return uiCanvasRoot.rotation;
                return Quaternion.LookRotation(camFwd.normalized, Vector3.up);
            }
            return Quaternion.LookRotation(forward.normalized, Vector3.up);
        }

        public void SetState(UIState newState)
        {
            currentState = newState;
            OnUIStateChanged?.Invoke(currentState);
        }

        public UIState GetCurrentState() => currentState;

        public void ShowHUD(string mainText, string hint, bool persistent = false)
        {
            if (hudController != null) hudController.ShowMessage(mainText, hint, persistent);
        }

        public void ShowHUDCompletion(string message, bool persistent = false)
        {
            if (hudController != null) hudController.ShowCompletionMessage(message, persistent);
        }

        public void AppendChatMessage(string message)
        {
            if (sessionController != null) sessionController.AppendChatMessage(message);
        }

        public void RefreshQRCodeDropdown()
        {
            if (sessionController != null) sessionController.RefreshQRCodeDropdown();
        }

        public void FadeHUD(float alpha, float duration) => hudGroup.SetFade(alpha, duration, this);
        public void FadeSessionUI(float alpha, float duration) => sessionUiGroup.SetFade(alpha, duration, this);
    }

    [System.Serializable]
    public class UIPanelGroup
    {
        public GameObject root;
        public CanvasGroup canvasGroup;
        
        [Header("Offsets")]
        public float forwardDistance = 1.2f;
        public float horizontalOffset = 0f;
        public float verticalOffset = 0f;

        [Header("Lazy Follow Settings")]
        public float angleThreshold = 25f;
        public float distanceThreshold = 0.3f;
        public float viewAngleThreshold = 35.0f;

        private Vector3 _velocity = Vector3.zero;
        private Vector3 _lastCamPos;
        private Quaternion _lastCamRot;
        private bool _isFollowing = false;

        public void Initialize(Transform cam)
        {
            if (root == null) return;
            root.transform.position = ComputeTargetPosition(cam);
            root.transform.rotation = GetFaceCameraRotation(cam, root.transform);
            _lastCamPos = cam.position;
            _lastCamRot = cam.rotation;
        }

        public void UpdatePositioning(Transform cam, float smoothTime, float rotationSpeed)
        {
            if (root == null || !root.activeInHierarchy) return;

            // 1. Always update rotation to face camera (Y-axis)
            Quaternion targetRot = GetFaceCameraRotation(cam, root.transform);
            root.transform.rotation = Quaternion.Slerp(root.transform.rotation, targetRot, rotationSpeed * Time.deltaTime);

            // 2. Check if we should trigger movement
            float currentDistToCam = Vector3.Distance(root.transform.position, cam.position);
            bool outOfRange = currentDistToCam < (forwardDistance * 0.5f) || currentDistToCam > (forwardDistance * 2.0f);

            // Check FOV: Is the panel currently in the camera's field of view?
            Vector3 toPanel = (root.transform.position - cam.position).normalized;
            float angleInView = Vector3.Angle(cam.forward, toPanel);

            if (angleInView > viewAngleThreshold || outOfRange)
            {
                _isFollowing = true;
                _lastCamPos = cam.position;
                _lastCamRot = cam.rotation;
            }

            if (_isFollowing)
            {
                Vector3 targetPos = ComputeTargetPosition(cam);
                root.transform.position = Vector3.SmoothDamp(root.transform.position, targetPos, ref _velocity, smoothTime);

                // Stop following once we are close enough to the target position
                if (Vector3.Distance(root.transform.position, targetPos) < 0.05f)
                {
                    _isFollowing = false;
                }
            }
        }

        private Vector3 ComputeTargetPosition(Transform cam)
        {
            Vector3 flatForward = cam.forward;
            flatForward.y = 0;
            if (flatForward.sqrMagnitude < 0.001f) flatForward = Vector3.ProjectOnPlane(cam.up, Vector3.up).normalized;
            else flatForward.Normalize();

            Vector3 flatRight = Vector3.Cross(Vector3.up, flatForward);

            return cam.position
                   + flatForward * forwardDistance
                   + flatRight * horizontalOffset
                   + Vector3.up * verticalOffset;
        }

        private Quaternion GetFaceCameraRotation(Transform cam, Transform rootTransform)
        {
            Vector3 directionToCamera = cam.position - rootTransform.position;
            directionToCamera.y = 0;
            
            if (directionToCamera.sqrMagnitude < 0.001f)
            {
                Vector3 camForward = cam.forward;
                camForward.y = 0;
                if (camForward.sqrMagnitude < 0.001f) return rootTransform.rotation;
                return Quaternion.LookRotation(camForward, Vector3.up);
            }
            
            return Quaternion.LookRotation(-directionToCamera, Vector3.up);
        }

        public void SetFade(float targetAlpha, float duration, MonoBehaviour runner)
        {
            if (canvasGroup == null) return;
            runner.StartCoroutine(FadeCoroutine(targetAlpha, duration));
        }

        private IEnumerator FadeCoroutine(float targetAlpha, float duration)
        {
            float startAlpha = canvasGroup.alpha;
            float elapsed = 0;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                canvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, elapsed / duration);
                yield return null;
            }
            canvasGroup.alpha = targetAlpha;
        }
    }
}
