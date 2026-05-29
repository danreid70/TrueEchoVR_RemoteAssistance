using UnityEngine;

namespace TEVR
{
    [CreateAssetMenu(fileName = "BackendConfig", menuName = "TEVR/BackendConfig")]
    public class BackendConfig : ScriptableObject
    {
        [Header("Backend Configuration")]
        public string apiHost = "https://live-troubleshooting-app.replit.app";
        public string apiPath = "/api";
        public string customerId = "cust-001";
        public string locationId = "loc-abc123";
        public string firmwareVersion = "1.0.0";
    }
}
