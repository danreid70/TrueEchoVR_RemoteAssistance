using UnityEngine;

namespace TEVR
{
    [CreateAssetMenu(fileName = "BackendConfig", menuName = "TEVR/BackendConfig")]
    public class BackendConfig : ScriptableObject
    {
        [Header("Backend Configuration")]
        public string serverBaseUrl = "https://live-troubleshooting-app.replit.app";
        public string apiPath = "/api";
        
        [Header("Default Session Info")]
        public string headsetId = "quest-3-unit-01";
    }
}
