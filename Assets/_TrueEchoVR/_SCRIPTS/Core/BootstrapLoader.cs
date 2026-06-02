using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

namespace TEVR
{
    /// <summary>
    /// Handles the initial loading sequence from the Bootstrap scene.
    /// Ensures all persistent managers are ready before entering the main environment.
    /// </summary>
    public class BootstrapLoader : MonoBehaviour
    {
        [Header("Configuration")]
        public string nextSceneName = "TroubleshootingWebIntegration";
        public float minimumDisplayTime = 1.0f;

        private IEnumerator Start()
        {
            Debug.Log("[Bootstrap] Initializing TrueEchoVR Ecosystem...");
            
            // Wait a moment for any Awake/Start logic in managers to fire
            yield return new WaitForSeconds(minimumDisplayTime);

            Debug.Log($"[Bootstrap] Loading primary scene: {nextSceneName}");
            SceneManager.LoadScene(nextSceneName);
        }
    }
}
