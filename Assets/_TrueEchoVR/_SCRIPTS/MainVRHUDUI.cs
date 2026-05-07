using UnityEngine;
using TMPro;
using System.Collections;

namespace TrueEchoVR
{
    public class MainVRHUDUI : MonoBehaviour
    {
        [Header("UI References (assign in Inspector)")]
        public GameObject hudPanel;                 // The root Canvas/Panel to move
        public TMP_Text statusText;
        public TMP_Text hintText;
        public TMP_Text completionText;
        public GameObject pointerArrow;

        [Header("Positioning (relative to camera)")]
        [SerializeField] private float forwardDistance = 1.5f;
        [SerializeField] private float horizontalOffset = 0f;
        [SerializeField] private float verticalOffset = 0.3f;
        [SerializeField] private float smoothTime = 0.15f;
        [SerializeField] private float rotationSpeed = 3f;
        [SerializeField] private float angleThreshold = 30f;
        [SerializeField] private float distanceThreshold = 0.2f;

        [Header("Auto‑Fade")]
        [SerializeField] private float fadeDelay = 2f;
        [SerializeField] private float fadeDuration = 0.5f;

        private Transform camTransform;
        private CanvasGroup canvasGroup;
        private Coroutine fadeCoroutine;
        private bool hasActiveText = false;
        private Transform currentTarget;
        private Vector3 lastCameraPos;
        private Quaternion lastCameraRot;
        private bool isFollowing = true;
        private Vector3 velocity = Vector3.zero;
        private Quaternion targetRotation;
        private Transform panelTransform;   // The transform of hudPanel

        private void Start()
        {
            camTransform = Camera.main?.transform;
            if (camTransform == null)
            {
                Debug.LogError("[TaskStatusUI] No main camera found.");
                enabled = false;
                return;
            }

            if (hudPanel == null)
            {
                Debug.LogError("[TaskStatusUI] No hudPanel assigned.");
                enabled = false;
                return;
            }

            panelTransform = hudPanel.transform;

            canvasGroup = hudPanel.GetComponent<CanvasGroup>();
            if (canvasGroup == null)
                canvasGroup = hudPanel.AddComponent<CanvasGroup>();

            if (statusText != null) statusText.gameObject.SetActive(false);
            if (hintText != null) hintText.gameObject.SetActive(false);
            if (completionText != null) completionText.gameObject.SetActive(false);
            if (pointerArrow != null) pointerArrow.SetActive(false);
            hudPanel.SetActive(false);

            lastCameraPos = camTransform.position;
            lastCameraRot = camTransform.rotation;
            panelTransform.position = ComputeTargetPosition();
            panelTransform.rotation = CameraFaceRotation();
            isFollowing = false;
        }

        private void LateUpdate()
        {
            if (camTransform == null || hudPanel == null) return;

            float angle = Quaternion.Angle(lastCameraRot, camTransform.rotation);
            float distance = Vector3.Distance(lastCameraPos, camTransform.position);
            if (angle > angleThreshold || distance > distanceThreshold)
            {
                isFollowing = true;
                lastCameraPos = camTransform.position;
                lastCameraRot = camTransform.rotation;
            }

            if (isFollowing)
            {
                Vector3 targetPos = ComputeTargetPosition();
                panelTransform.position = Vector3.SmoothDamp(panelTransform.position, targetPos, ref velocity, smoothTime);
                targetRotation = CameraFaceRotation();
                panelTransform.rotation = Quaternion.Slerp(panelTransform.rotation, targetRotation, rotationSpeed * Time.deltaTime);

                if (Vector3.Distance(panelTransform.position, targetPos) < 0.01f &&
                    Quaternion.Angle(panelTransform.rotation, targetRotation) < 0.5f)
                {
                    isFollowing = false;
                    panelTransform.position = targetPos;
                    panelTransform.rotation = targetRotation;
                }
            }

            // Update pointer arrow
            if (pointerArrow != null)
            {
                if (currentTarget != null && hudPanel.activeSelf)
                {
                    Vector3 toTarget = currentTarget.position - pointerArrow.transform.position;
                    if (toTarget != Vector3.zero)
                        pointerArrow.transform.rotation = Quaternion.LookRotation(toTarget, Vector3.up);
                    pointerArrow.SetActive(true);
                }
                else
                {
                    pointerArrow.SetActive(false);
                }
            }
        }

        private Vector3 ComputeTargetPosition()
        {
            return camTransform.position
                   + camTransform.forward * forwardDistance
                   + camTransform.right * horizontalOffset
                   + Vector3.up * verticalOffset;
        }

        private Quaternion CameraFaceRotation()
        {
            Vector3 toCamera = camTransform.position - panelTransform.position;
            return Quaternion.LookRotation(-toCamera, Vector3.up);
        }

        public void ShowMessage(string mainText, string hint)
        {
            hasActiveText = !string.IsNullOrEmpty(mainText) || !string.IsNullOrEmpty(hint);
            if (!hasActiveText)
            {
                StartFadeOut();
                return;
            }

            if (!hudPanel.activeSelf)
                hudPanel.SetActive(true);

            if (statusText != null)
            {
                statusText.gameObject.SetActive(true);
                statusText.text = mainText ?? "";
            }
            if (hintText != null)
            {
                hintText.gameObject.SetActive(true);
                hintText.text = hint ?? "";
            }
            if (completionText != null)
                completionText.gameObject.SetActive(false);

            SetOpacity(1f);
            CancelFade();
            if (fadeDelay > 0)
                StartFadeCountdown();
        }

        public void ShowCompletionMessage(string message)
        {
            hasActiveText = true;
            if (!hudPanel.activeSelf)
                hudPanel.SetActive(true);

            if (statusText != null) statusText.gameObject.SetActive(false);
            if (hintText != null) hintText.gameObject.SetActive(false);
            if (completionText != null)
            {
                completionText.gameObject.SetActive(true);
                completionText.text = message ?? "";
            }

            SetOpacity(1f);
            CancelFade();
            if (fadeDelay > 0)
                StartFadeCountdown();
        }

        public void HighlightTarget(Transform target)
        {
            currentTarget = target;
        }

        public void ClearHighlight()
        {
            currentTarget = null;
        }

        private void StartFadeCountdown()
        {
            CancelFade();
            fadeCoroutine = StartCoroutine(FadeAfterDelay());
        }

        private void StartFadeOut()
        {
            CancelFade();
            fadeCoroutine = StartCoroutine(FadeOutNow());
        }

        private void CancelFade()
        {
            if (fadeCoroutine != null)
            {
                StopCoroutine(fadeCoroutine);
                fadeCoroutine = null;
            }
        }

        private IEnumerator FadeAfterDelay()
        {
            yield return new WaitForSeconds(fadeDelay);
            yield return FadeTo(0f, fadeDuration);
        }

        private IEnumerator FadeOutNow()
        {
            yield return FadeTo(0f, fadeDuration);
        }

        private IEnumerator FadeTo(float targetAlpha, float duration)
        {
            if (canvasGroup == null) yield break;
            float start = canvasGroup.alpha;
            float t = 0;
            while (t < duration)
            {
                t += Time.deltaTime;
                canvasGroup.alpha = Mathf.Lerp(start, targetAlpha, t / duration);
                yield return null;
            }
            canvasGroup.alpha = targetAlpha;

            if (targetAlpha <= 0.01f && !hasActiveText && hudPanel != null)
                hudPanel.SetActive(false);
            fadeCoroutine = null;
        }

        private void SetOpacity(float alpha)
        {
            if (canvasGroup != null)
                canvasGroup.alpha = alpha;
        }
    }
}