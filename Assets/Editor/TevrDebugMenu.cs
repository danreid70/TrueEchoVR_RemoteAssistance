using UnityEditor;
using UnityEngine;
using TEVR;

namespace TEVR.EditorTools
{
    /// <summary>
    /// EDITOR-ONLY debug tools for QA without a headset. Lets you simulate QR detections (setup code,
    /// RoomAnchor, item codes) so the sign-in flow, dropdown, arrow and highlight can be exercised in the
    /// Editor. Everything here is under Assets/Editor and compiled OUT of device builds.
    /// </summary>
    internal static class TevrDebugMenu
    {
        private const string Root = "TrueEchoVR/Debug/";

        private static QrCodeManager FindQr()
        {
            var qr = Object.FindAnyObjectByType<QrCodeManager>(FindObjectsInactive.Include);
            if (qr == null) Debug.LogError("[TevrDebug] No QrCodeManager found in the open scene(s).");
            return qr;
        }

        private static bool RequirePlayMode()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[TevrDebug] Enter Play Mode first — runtime managers subscribe to detection events in Start().");
                return false;
            }
            return true;
        }

        [MenuItem(Root + "Simulate Login Setup Code (TEVRDEMO)")]
        private static void SimulateSetupCode()
        {
            if (!RequirePlayMode()) return;
            var qr = FindQr(); if (qr == null) return;
            qr.SetScanMode(QrCodeManager.ScanMode.LoginOnly);
            qr.SimulateQRDetectionEditor("TEVRDEMO");
            Debug.Log("[TevrDebug] Simulated bare setup code 'TEVRDEMO' (LoginOnly).");
        }

        [MenuItem(Root + "Simulate RoomAnchor QR")]
        private static void SimulateRoomAnchor()
        {
            if (!RequirePlayMode()) return;
            var qr = FindQr(); if (qr == null) return;
            qr.SetScanMode(QrCodeManager.ScanMode.Full);
            qr.SimulateQRDetectionEditor("RoomAnchor");
            Debug.Log("[TevrDebug] Simulated RoomAnchor QR (Full).");
        }

        [MenuItem(Root + "Simulate Item QR (DEMO-PUMP-01)")]
        private static void SimulateItemA()
        {
            if (!RequirePlayMode()) return;
            var qr = FindQr(); if (qr == null) return;
            qr.SetScanMode(QrCodeManager.ScanMode.Full);
            qr.SimulateQRDetectionEditor("DEMO-PUMP-01");
            Debug.Log("[TevrDebug] Simulated item QR 'DEMO-PUMP-01' (Full).");
        }

        [MenuItem(Root + "Simulate Item QR (DEMO-VALVE-02)")]
        private static void SimulateItemB()
        {
            if (!RequirePlayMode()) return;
            var qr = FindQr(); if (qr == null) return;
            qr.SetScanMode(QrCodeManager.ScanMode.Full);
            qr.SimulateQRDetectionEditor("DEMO-VALVE-02");
            Debug.Log("[TevrDebug] Simulated item QR 'DEMO-VALVE-02' (Full).");
        }

        [MenuItem(Root + "Simulate Full Demo Room (Anchor + 2 Items)")]
        private static void SimulateFullRoom()
        {
            if (!RequirePlayMode()) return;
            var qr = FindQr(); if (qr == null) return;
            qr.SetScanMode(QrCodeManager.ScanMode.Full);
            qr.SimulateQRDetectionEditor("RoomAnchor");
            qr.SimulateQRDetectionEditor("DEMO-PUMP-01");
            qr.SimulateQRDetectionEditor("DEMO-VALVE-02");
            Debug.Log("[TevrDebug] Simulated full demo room (anchor + 2 items).");
        }
    }
}
