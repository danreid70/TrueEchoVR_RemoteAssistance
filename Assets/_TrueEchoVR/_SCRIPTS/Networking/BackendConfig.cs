using UnityEngine;

namespace TEVR
{
    [CreateAssetMenu(fileName = "BackendConfig", menuName = "TEVR/BackendConfig")]
    public class BackendConfig : ScriptableObject
    {
        [Header("Backend Configuration")]
        public string apiHost = "https://live-troubleshooting-app.replit.app";
        public string apiPath = "/api";
        public string customerId = "cust-004";
        public string locationId = "4343e4d8-0dd0-4fd0-8ce6-8f42af994ffc";
        public string firmwareVersion = "1.0.0";
    }
}
