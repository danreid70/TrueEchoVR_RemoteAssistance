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
        [SerializeField] private float globalSmoothTime = 0.25f;
        [SerializeField] private float globalRotationSpeed = 5f;

        [Header("Controllers")]
        public VrHudController hudController;
        public SessionUiController sessionController;
        public PointerArrowController pointerArrow;
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

            if (mainCamera != null)
            {
                hudGroup.Initialize(mainCamera);
                sessionUiGroup.Initialize(mainCamera);
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

            hudGroup.UpdatePositioning(mainCamera, globalSmoothTime, globalRotationSpeed);
            sessionUiGroup.UpdatePositioning(mainCamera, globalSmoothTime, globalRotationSpeed);
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

        public void SetPointerTarget(Transform target)
        {
            if (pointerArrow != null) pointerArrow.SetTarget(target);
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
