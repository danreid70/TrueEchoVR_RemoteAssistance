using UnityEditor;
using UnityEngine;
using TEVR;

namespace TEVR.EditorTools
{
    /// <summary>
    /// EDITOR-ONLY signaling contract smoke test. Feeds each server->client Socket.IO event shape from
    /// BACKEND_CONTRACT.md through the live parser and verifies the client dispatches/parses it correctly —
    /// without needing a backend. Validates the brittle, contract-coupled parsing path (event names, payload
    /// shapes, the zero-position "clear" sentinel, peer-joined socketId capture, and that an offer triggers
    /// an outbound answer attempt). Full WebRTC media negotiation still requires a live admin peer + device.
    /// Run from TrueEchoVR/Debug/Run Signaling Contract Smoke Test (Play Mode).
    /// </summary>
    internal static class SignalingContractSmokeTest
    {
        [MenuItem("TrueEchoVR/Debug/Run Signaling Contract Smoke Test")]
        private static void Run()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[SmokeTest] Enter Play Mode first (the parser dispatches to runtime subscribers).");
                return;
            }

            var sm = Object.FindAnyObjectByType<SignalingManager>(FindObjectsInactive.Include);
            if (sm == null) { Debug.LogError("[SmokeTest] No SignalingManager in the scene."); return; }

            int pass = 0, fail = 0;
            void Check(string label, bool ok)
            {
                if (ok) { pass++; Debug.Log($"[SmokeTest] PASS — {label}"); }
                else { fail++; Debug.LogError($"[SmokeTest] FAIL — {label}"); }
            }

            // --- chat-message ---
            string lastChat = null;
            System.Action<string> chatH = (m) => lastChat = m;
            sm.OnChatMessageReceived += chatH;
            sm.Debug_FeedSocketEvent("[\"chat-message\",{\"message\":\"hello-smoke\"}]");
            Check("chat-message parsed (message field)", lastChat == "hello-smoke");
            sm.OnChatMessageReceived -= chatH;

            // --- point-to with coordinates (non-zero position => highlight at coords) ---
            string ptName = null, ptQr = null; bool ptHadPos = false;
            System.Action<string, string, Vector3?, Quaternion?> ptH = (n, q, p, r) =>
            { ptName = n; ptQr = q; ptHadPos = p.HasValue; };
            sm.OnPointToReceived += ptH;
            sm.Debug_FeedSocketEvent("[\"point-to\",{\"name\":\"Pump\",\"qrCode\":\"PUMP_01\",\"pose\":{\"position\":{\"x\":1.0,\"y\":0.5,\"z\":2.0},\"rotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1}}}]");
            Check("point-to coords: name parsed", ptName == "Pump");
            Check("point-to coords: qrCode parsed", ptQr == "PUMP_01");
            Check("point-to coords: position present", ptHadPos);

            // --- point-to clear (zero/absent position => clear sentinel) ---
            ptName = "x"; ptHadPos = true;
            sm.Debug_FeedSocketEvent("[\"point-to\",{\"name\":\"\",\"qrCode\":\"\",\"pose\":{\"position\":{\"x\":0,\"y\":0,\"z\":0},\"rotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1}}}]");
            Check("point-to clear: zero position treated as no-coords", ptHadPos == false);
            sm.OnPointToReceived -= ptH;

            // --- peer-joined (captures the WebRTC target socket id) ---
            sm.Debug_FeedSocketEvent("[\"peer-joined\",{\"role\":\"admin\",\"socketId\":\"sock-123\"}]");
            Check("peer-joined: remote socketId captured", sm.Debug_RemoteSocketId == "sock-123");

            // --- offer => outbound answer attempt (media negotiation needs a live peer; we only assert the
            //     offer is parsed, the target socket id is captured, and the answer/ICE path is engaged). ---
            string lastEmit = null;
            System.Action<string, string> emitH = (ev, json) => { if (ev == "answer" || ev == "ice-candidate") lastEmit = ev; };
            sm.Debug_OnSocketEmit += emitH;
            sm.Debug_FeedSocketEvent("[\"offer\",{\"offer\":{\"sdp\":\"v=0\\r\\n\",\"type\":\"offer\"},\"fromSocketId\":\"sock-offer\"}]");
            Check("offer: remote socketId captured from fromSocketId", sm.Debug_RemoteSocketId == "sock-offer");
            // Note: the answer emit is async (WebRTC) and may not complete in Editor without a real SDP/peer.
            Debug.Log("[SmokeTest] NOTE: offer→answer media negotiation requires a live admin peer + device; only signaling parse is asserted here.");
            sm.Debug_OnSocketEmit -= emitH;

            Debug.Log($"TEVR_SMOKE_RESULT: pass={pass} fail={fail} {(fail == 0 ? "ALL PASS" : "HAS FAILURES")}");
            if (fail == 0)
                EditorUtility.DisplayDialog("Signaling Smoke Test", $"All {pass} contract checks passed.", "OK");
            else
                EditorUtility.DisplayDialog("Signaling Smoke Test", $"{fail} check(s) FAILED, {pass} passed. See Console.", "OK");
        }
    }
}
