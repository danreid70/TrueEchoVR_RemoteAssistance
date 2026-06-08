# Plan: Robustness Improvements (5 features) + Docs

## Features
1. **Connection-state watchdog**: SignalingManager raises OnReconnecting/OnReconnectFailed; UI shows
   reconnect status and, on failure, returns to Login (surfacing the Demo button). Add public Reconnect().
2. **Auth token expiry handling**: 401 (and 403/404 on startup-data) -> ClearCredentials + OnCredentialsExpired;
   UI prompts a re-scan instead of silently dropping to Demo. EveryBootSequence must NOT demo on expiry.
3. **Editor-only simulated QR**: QrCodeManager.SimulateQRDetectionEditor (#if UNITY_EDITOR) + menu items to
   simulate RoomAnchor / item / setup-code without a headset. Compiled out of device builds.
4. **Signaling smoke harness**: editor-only Debug hooks + a menu-invoked contract test that validates
   server->client parsing/dispatch (chat, point-to clear/coords, peer-joined) and that an offer starts the
   answer coroutine without synchronous error.
5. **Auto-wire guard**: editor validator (menu + sceneSaving hook) that warns on unassigned scene refs.

## Docs
- Update README.md and the two protocol .md files to reflect: non-blocking sign-in->session flow, Demo Mode
  (offline normal session), continuous login detection, reconnect watchdog, credential-expiry re-scan,
  editor debug tools, and the request timeout.

## Verify
- Compile clean after each feature.
- Re-run the PlayMode flow test; run the signaling smoke test once.
