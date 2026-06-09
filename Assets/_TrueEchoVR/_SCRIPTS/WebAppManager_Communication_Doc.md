# TrueEchoVR SignalingManager Synchronization Protocol (v2.4 - RemoteAssistance)

This document outlines the communication protocol between the Unity application (TrueEchoVR) and the Replit backend. This is used by the **SignalingManager** to coordinate WebRTC, chat, and spatial data. Payload shapes below were verified directly against `SignalingManager.cs` / `SessionUiController.cs` / `QrCodeManager.cs`.

> **What changed in v2.4 (READ THIS — REPLIT ACTION ITEMS):**
> 1. **WebRTC: the WEB APP must send the OFFER.** The headset is strictly **answer-only** — it builds its PeerConnection and streams video *only after* it receives an `offer`. If the dashboard stays on "Waiting for headset to connect", it is almost always because **(a)** the server never told the admin a headset joined the room, or **(b)** the admin side never sent an `offer`. The headset is connected and waiting. See the **§4a connection lifecycle** and the **§4b "Waiting for headset" checklist**.
> 2. **`qr-detected` must accept BOTH a single code and a list.** The headset now PUSHes calibration as a **batch first, then falls back to per-item** registration if the batch fails. Replit should accept either a multi-element OR single-element `qrCodes` array (they are the same shape) — see §2a and §3b. This fixes "the RoomAnchor registered but the other code didn't".
> 3. **Composite stream now aligns to the LEFT passthrough camera** (Quest 3's default WebCamTexture eye) and **defaults to passthrough-only** (VR/HUD overlay OFF until the operator enables it) — §4.
> 4. **Session QR detection now defaults OFF** (operator starts it) and **all UI visuals are kept in lock-step with state** — §6/§8.
>
> **Carried over from v2.3 (still REQUIRED on Replit):**
> - Add a Socket.IO handler for **`qr-detected`** (real-time QR registration) — §2a.
> - WebRTC answerer order fix is in place headset-side — §4.

> **Note on the Meta Spatial Anchor upgrade:** the RoomAnchor is now persisted on-device as a Meta `OVRSpatialAnchor` (drift-free, auto-relocalizing). This is a **device-local** change only — QR poses are still expressed relative to the RoomAnchor and synced exactly as documented here. **This protocol/contract is unchanged by that upgrade.** For a backend-implementer-focused spec, see `BACKEND_CONTRACT.md`.

## 1. Connection Endpoints
The base URL is **stored on the device** (`BackendConfig.apiHost` default, overridable via the Login panel's Backend URL field and persisted to `tevr_apiBaseUrl`). `SignalingManager.SetBackendUrl` splits a trailing `/api` into `apiPath` so REST and the root-level WebSocket both resolve correctly. Example (default):
- **WebSocket (Socket.io):** `wss://live-troubleshooting-app.replit.app/socket.io/?EIO=4&transport=websocket`
- **REST API (Persistence):** `https://live-troubleshooting-app.replit.app/api`

## 2. Real-time Signaling (Socket.io / Engine.IO v4)
The Unity application speaks the **Engine.IO v4 / Socket.IO v4** wire protocol over a raw WebSocket. Application events are framed as `42["event-name", {payload}]`.

### Connection Handshake (REQUIRED ORDER)
`SignalingManager.HandleEngineIoPacket()` implements the compliant handshake. Do **not** emit application events before the namespace is connected:
1. Server sends Engine.IO **OPEN** (`0{...}`) on connect.
2. Unity replies with Socket.IO **CONNECT** (`40`) to the default namespace.
3. Server acknowledges with `40`. Only **after** this ack does Unity emit `join-room` and start health telemetry (`IsSocketConnected` becomes `true`).

### Connection Maintenance (Heartbeat)
Engine.IO v4 is **server-driven**:
- **Ping:** The **server** sends `2` on its own interval.
- **Pong:** **Unity** replies with `3` (handled automatically in `HandleEngineIoPacket`). Unity does **not** initiate pings.
- **Latency Tracking:** Round-trip time between consecutive server pings is exposed via `currentLatency`.

> Debugging: set `SignalingManager.verboseSocketLogging = true` to log every raw packet (`=>` sent / `<=` received). The editor harness **Tools ▸ TEVR ▸ Signaling Tester** lets you connect to a room code and watch `WebSocket Open`, `Socket.IO Connected (40 ack)`, and latency live — on desktop, no headset required.
>
> Contract regression: **`TrueEchoVR/Debug/Run Signaling Contract Smoke Test`** (Play Mode) feeds each incoming event shape below through the live `ProcessIncomingMessage` parser and asserts correct dispatch — no backend needed. It uses the editor-only hooks `SignalingManager.Debug_FeedSocketEvent(...)`, `Debug_OnSocketEmit`, and `Debug_RemoteSocketId` (all `#if UNITY_EDITOR`).

### Reconnection Watchdog
On an **abnormal** socket close (`code != Normal`):
- If `autoReconnect` is on and attempts remain (`_reconnectCount < maxReconnectAttempts`), the client waits `reconnectDelay` and re-opens for the current room, firing **`OnReconnecting(attempt, max)`** each try.
- When attempts are exhausted (or auto-reconnect is off), it fires **`OnReconnectFailed`**. The UI then returns to the Sign In window and reveals the **Demo Mode** button.
- **`SignalingManager.Reconnect()`** performs a manual retry (resets the attempt counter and re-opens for `currentRoomCode`).
A clean/`Normal` close (e.g. the user pressing **Leave Session**) does **not** trigger reconnection.

### Outgoing (Unity to Replit)
Every application event is framed `42["event-name", { ...singleJsonObject }]`. Exact payload shapes (these are produced by `JsonUtility`, so field names and nesting must match **exactly**):

| Event Name | Payload Structure | Description |
| :--- | :--- | :--- |
| `join-room` | `{ "role": "headset", "roomCode": "STR", "locationId": "STR" }` | Sent once, immediately after the `40` namespace ack. |
| `chat-message` | `{ "roomCode": "STR", "message": "STR", "senderRole": "headset" }` | Text message sent by the operator. (Field is `message`, **not** `text`.) |
| `answer` | `{ "roomCode": "STR", "answer": { "sdp": "STR", "type": "answer" }, "targetSocketId": "STR" }` | WebRTC SDP answer targeting the expert. (Key is `answer`.) |
| `ice-candidate`| `{ "roomCode": "STR", "candidate": { "candidate": "STR", "sdpMid": "STR", "sdpMLineIndex": INT }, "targetSocketId": "STR" }` | ICE candidate (nested under `candidate`). |
| `health-update` | `{ "roomCode": "STR", "batteryLevel": INT, "calibrated": BOOL, "headsetId": "STR", "locationId": "STR", "timestamp": "ISO-8601" }` | Periodic telemetry, every **60 s** while connected. |
| `qr-detected` | see **§2a** | **NEW.** Real-time registration of a physically-detected QR code. Emitted on first detection / RoomAnchor discovery (immediately) and on movement (throttled ~1/s per code). |

### 2a. `qr-detected` (NEW — real-time QR registration)
Emitted by `SignalingManager.SendQrDetected(...)`, called from `SessionUiController.EmitQrToServer(...)` on the `OnQRCodeAdded`, `OnRoomAnchorDiscovered`, and (throttled) `OnQRCodeUpdated` events. **Only sent while the socket is connected** (i.e. in a live session — Demo Mode is offline and emits nothing).

```jsonc
{
  "roomCode":    "STR",
  "locationId":  "STR",
  "headsetId":   "STR",
  "qrValue":     "STR",          // the QR payload string = the code's identity (use as the upsert key)
  "name":        "STR",          // friendly name if known (from the legit list), else ""
  "listed":      true,            // true = this payload is in the server's legit list for this location
  "isRoomAnchor": false,          // true  => position/rotation are WORLD (this code IS the reference frame)
                                  // false => position/rotation are RELATIVE to the RoomAnchor (an item)
  "position":    { "x":0, "y":0, "z":0 },
  "rotation":    { "x":0, "y":0, "z":0, "w":1 },
  "timestamp":   "ISO-8601"
}
```

**What Replit must do with it (recommended):**
- **Upsert by `(locationId, qrValue)`** into a live, per-room view so the dashboard shows the code immediately. Treat repeated events as position updates (the headset throttles to ~1/sec per code).
- Use **`isRoomAnchor`** to interpret the coordinate frame: the RoomAnchor's pose is the world reference; every item pose is relative to it. (This matches the REST `CalibrationUpload` frame in §3b, so the same placement math applies.)
- `listed=false` means the headset saw a code that is **not** in the server's legit list (e.g. an extra/unknown code, or Demo Mode would be offline so this only happens in live sessions). You may surface these as "unrecognised" rather than auto-persisting them.
- **Persistence vs. live view:** `qr-detected` is the *live overlay feed*. The authoritative save still happens when the operator taps **Push** (`POST /api/locations/{id}/qr-codes`, §3b). It is fine for Replit to also persist `qr-detected` upserts, but the headset does not assume it does.
- **No ack is required.** The headset does not wait for a response; it is fire-and-forget over the socket.
- **One event PER code (not a list).** `qr-detected` always describes a single code. Multiple codes arrive as a **sequence** of separate `qr-detected` events, so handle each independently. Ordering note: the **RoomAnchor is emitted first** (its world pose anchors the frame); items follow. If items were detected *before* the anchor existed, they are **re-emitted** right after the anchor is discovered (the headset re-flushes the whole set), so a late anchor still results in every item registering — just expect a second burst of `qr-detected` events after the anchor appears.
- **Why you might have seen only the RoomAnchor before:** items detected before any RoomAnchor have no relative frame and were skipped; they are now re-sent once the anchor exists (headset-side fix in v2.4). No Replit change needed for that specific symptom, but the per-item REST fallback below DOES need Replit to accept single-item arrays.

### Incoming (Replit to Unity)
Unity's parser (`ProcessIncomingMessage`) reads `42["event-name", { singleObject }]` and only handles the four events below. Any other event name is ignored. Each event must carry **exactly one** JSON object as its argument.

| Event Name | Payload Structure | Description |
| :--- | :--- | :--- |
| `peer-joined` | `{ "role": "admin", "socketId": "STR" }` | Expert connected; `socketId` becomes the WebRTC target. |
| `offer` | `{ "offer": { "sdp": "STR", "type": "offer" }, "fromSocketId": "STR" }` | WebRTC SDP offer from the expert console. |
| `chat-message` | `{ "message": "STR" }` | Text from the expert. Unity reads `message` (extra fields ignored). |
| `point-to` | `{ "name": "STR", "qrCode": "STR", "pose": { "position": {"x":F,"y":F,"z":F}, "rotation": {"x":F,"y":F,"z":F,"w":F} } }` | "Look-at" command — see resolution rules below. |

**`point-to` resolution (headset side, `SessionFlowManager.OnRemotePointToReceived`):**
1. **Cross-reference a local code first.** The client matches the command to a locally-tracked QR code by **`qrCode`** (the exact QR payload value — most reliable) and then by **`name`**. If found, it points at the *real code* with the directional arrow **and** the pulsing focus glow — identical to selecting it from the dropdown. `pose` is **not required** for this; sending just `qrCode` (or `name`) is enough.
2. **Coordinate fallback.** If the code is **not** locally represented but a non-zero `pose.position` is supplied, the client shows the position highlight (outline + billboard label) at those RoomAnchor-relative coordinates.
3. **Clear.** A command with **no `name`, no `qrCode`, and no `position`** clears the current highlight.
> Recommendation: always include `qrCode` (the payload value). `name` is the friendly label used for the on-HUD message and as a secondary match key.

> **Not implemented headset-side:** there is no `pull-qrcodes` socket handler. The headset pulls calibration over REST (`GET /api/locations/{id}/qr-codes`) when the operator taps **Pull**; do not rely on a socket push to refresh QR data.

## 3. Data Persistence (REST API)
Persistence is handled via HTTP requests to `{apiHost}{apiPath}` (default `https://live-troubleshooting-app.replit.app/api`). Implemented in `SignalingManager.SendRequest`.

### Required headers (on EVERY request)
| Header | Value | Notes |
| :--- | :--- | :--- |
| `Content-Type` | `application/json` | |
| `X-Requested-With` | `XMLHttpRequest` | **Required.** The backend's AJAX/CSRF guard returns **HTTP 403** without it. |
| `Authorization` | `Bearer {token}` | Sent when a token is available (from `GET /api/setup/{code}`, persisted as `TEVR_AUTH_TOKEN`). |

### Behaviour
- **Retries:** each request is attempted up to **3 times** with a **2 s** delay between attempts.
- **Credential invalidation:** a `startup-data` response of **404 or 403** causes the headset to clear stored credentials and fall back to Demo Mode.
- **Vector/Quaternion JSON:** Unity `JsonUtility` serialises `Vector3` as `{ "x":F, "y":F, "z":F }` and `Quaternion` as `{ "x":F, "y":F, "z":F, "w":F }`. The backend must use these exact field names and nesting or the headset cannot parse them.

### Endpoints
| Method | Endpoint | Request body | Success response |
| :--- | :--- | :--- | :--- |
| `GET` | `/api/setup/{setupCode}` | — | `{ "customerId": "STR", "locationId": "STR", "roomCode": "STR"?, "token": "STR" }` — the **`token`** is the bearer used by `register` and `startup-data`. |
| `POST` | `/api/headsets/register` | `{ "serialNumber": "STR", "customerId": "STR", "firmwareVersion": "STR", "label": "STR" }` | `{ "id": "STR", "serialNumber": "STR", "label": "STR", "customerId": "STR", "customerName": "STR" }` — `id` becomes `headsetId`. |
| `GET` | `/api/headsets/{id}/startup-data?locationId={locationId}` | — | `StartupData` (see §3a). |
| `POST` | `/api/locations/{id}/qr-codes` | `CalibrationUpload` (see §3b) | any 2xx. Uploads the operator's local calibration. |
| `GET` | `/api/locations/{id}/qr-codes` | — | `CalibrationUpload` (same shape; applied on **Pull**). |

### 3a. `StartupData` (GET startup-data response)
```jsonc
{
  "locationId": "STR",
  "locationName": "STR",
  "version": "STR",                 // optional version tag for context isolation
  "qrCodes": [                       // authoritative "legit" QR list (also feeds the dropdown + classifier)
    {
      "qrValue": "STR",             // the QR payload string (the identity used for matching)
      "name": "STR",               // human-friendly label
      "position": { "x":0, "y":0, "z":0 },     // pose RELATIVE to the RoomAnchor
      "rotation": { "x":0, "y":0, "z":0, "w":1 },
      "metadata": "STR"            // optional, free-form
    }
  ],
  "nameDictionary": [ { "qrValue": "STR", "name": "STR" } ]
}
```

### 3b. `CalibrationUpload` (POST/GET locations/{id}/qr-codes)
```jsonc
{
  "headsetId": "STR",
  "qrCodes": [
    {
      "qrValue": "STR",
      "position": { "x":0, "y":0, "z":0 },     // RELATIVE to the RoomAnchor (item) / world (RoomAnchor itself)
      "rotation": { "x":0, "y":0, "z":0, "w":1 }
    }
  ]
}
```
> Poses are stored **relative to the RoomAnchor** zero-point (except the RoomAnchor entry itself). This relative frame is what makes the data portable to the web dashboard and is preserved unchanged by the on-device Meta Spatial Anchor upgrade.

> **ROBUST PUSH (bulk → sequential fallback) — Replit must accept BOTH:**
> The **Push** button (`OnPushQRPressed`) first POSTs the **whole list** in one `CalibrationUpload` (multi-element `qrCodes`). **If that POST fails**, the headset automatically retries by POSTing **each code individually** — one `CalibrationUpload` per request, each with a **single-element `qrCodes` array** (identical shape, just length 1), continuing past any individual failure and reporting a final `N/M registered` tally.
> - **Therefore the endpoint must accept a `qrCodes` array of ANY length (1..N) and upsert each entry by `qrValue`.** A backend that only ever reads `qrCodes[0]` will appear to "only register one code" on the bulk path and is the most likely cause of "RoomAnchor saved but items didn't".
> - This bulk-first/sequential-fallback pattern is the project's standard safety net: try the efficient path, fall back to the granular path, verify, and report. Mirror the same tolerance server-side (accept either shape) so a change on one side never breaks the other.

## 4. WebRTC Requirements
- **Capture:** the headset streams a **composite RenderTexture** (B8G8R8A8_SRGB) = real-world passthrough (PCA `WebCamTexture` background) + (optionally) the Unity-rendered VR/HUD overlay.
- **Compositing defaults OFF:** the stream starts as **clean passthrough only**. The operator turns the VR/HUD overlay on with the **Compositing** toggle (`SetCompositingEnabled`). So by default the expert sees the real world exactly as the technician does, with no UI clutter.
- **Aspect + alignment:** RT is sized to the **live passthrough aspect** (Quest 3 = 4:3, fallback 1280×960). The overlay renders at the **passthrough FOV** (`passthroughHorizontalFovDeg`, default 82°). The composite camera is **aligned to the LEFT passthrough camera** by default (`compositeEyeAlignment = Left`) because Quest 3's default `WebCamTexture` device maps to the left RGB camera — this removes the lateral parallax between the real-world background and the overlay. Fine-tune on-device with `manualEyeOffsetMeters` (a small +Z helps, since the camera sits slightly forward of the eye). The dashboard should display incoming video at its **native aspect** (do not force 16:9).
- **Audio:** Bi-directional audio via `AudioStreamTrack`.
- **Answerer negotiation order (headset side):** the headset is **answer-only** and negotiates: **(1)** `SetRemoteDescription(offer)` → **(2)** `AddTrack` → **(3)** `CreateAnswer`. Remote `ice-candidate` events are queued until the remote description is set. STUN-only by default — add a TURN server if media fails across restrictive networks.
- **Operator video toggles (headset UI, no protocol impact):** Compositing on/off (overlay), Stream-to-Replit on/off (mutes the outbound track via `VideoStreamTrack.Enabled` without renegotiating), Show-remote (local visibility of the expert feed).

### 4a. Connection lifecycle (who does what, in order)
This is the exact sequence. **The web app/admin is the OFFERER; the headset is the ANSWERER.**
1. Headset opens the WebSocket, completes the Engine.IO/Socket.IO handshake (`0`→`40`→`40` ack), then emits **`join-room`** `{ role:"headset", roomCode, locationId }`. Headset logs: *"Socket connected. Joined room … waiting for the expert to send a video offer…"*.
2. **Server responsibility:** notify the admin/dashboard that a headset is present in that room (e.g. emit a peer/headset-joined to the admin, or let the admin poll room membership). **If this step is missing, the dashboard sits on "Waiting for headset to connect" even though the headset is fully connected.**
3. Admin side creates an RTCPeerConnection with a **recvonly** video transceiver and sends **`offer`** `{ offer:{sdp,type}, fromSocketId }` to the headset (routed by the server).
4. Headset receives `offer` → runs the answerer sequence → emits **`answer`** `{ roomCode, answer:{sdp,type}, targetSocketId }`. Headset logs: *"Offer received … negotiating"* then *"Answer sent — streaming video/audio to the expert."*
5. Both sides exchange **`ice-candidate`** events. Media flows once ICE connects.
6. Optional: headset emits **`peer-joined`** capture — it stores `socketId`/`fromSocketId` as the ICE/answer target. The server must route the answer/candidates back to that admin socket.

### 4b. "Waiting for headset to connect" — diagnostic checklist (Replit side)
The headset prints its progress to the in-session chat log (`[Backend]` lines) AND the Editor console. If you see *"Joined room … waiting for the expert to send a video offer"* on the headset but the dashboard still says "waiting", the problem is on the server/web side. Check, in order:
1. **Room routing:** does the server place the `headset` and the `admin` in the **same room** keyed by `roomCode`? (Compare the exact `roomCode` string — it's upper-cased on the headset.)
2. **Presence relay:** when the headset emits `join-room`, does the server tell the admin? The headset does **not** send an offer; if the admin never learns a headset joined, no offer is ever created.
3. **Offer direction:** is the **admin** creating and sending the `offer`? The headset only ever answers. A common bug is both sides waiting for the other to offer.
4. **Answer routing:** is the headset's `answer` (and its `ice-candidate`s) routed back to the admin's `socketId` (the `targetSocketId` the headset echoes)?
5. **Payload shapes:** `offer`/`answer` SDP must be nested under the `offer`/`answer` key (not at the top level). `ice-candidate` must be nested under `candidate`. See §2.
6. **TURN:** if signaling completes (answer sent) but no media appears, it's an ICE/NAT problem — add a TURN server.

## 5. Identification
- **LocationID:** Critical for spatial calibration routing.
- **HeadsetID:** Unique identifier per device (default: `quest-3-unit-01`).
- **RoomCode:** Transient session identifier for signaling routing.

## 6. Provisioning & Login Flow (Login Panel)
The **Login Panel** drives device provisioning before a session can start (wired in `SessionUiController`). Scan the setup QR **once**; everything needed is then stored on the device and pre-populated on every launch.

| Button / Field | Method | Behaviour |
| :--- | :--- | :--- |
| **Scan Login Code** | `OnScanLoginCodePressed` | Manual scan toggle. Not required — detection auto-starts at launch (see §8). Press again to cancel. |
| **Backend URL** (input) | `loginApiUrlInput` → `SaveBackendUrl` | Editable backend base URL with a **default**, **overridable** on-device, **persisted** locally, and **pre-populated** every launch. The QR no longer carries the URL. |
| **Sign In** | `OnSignInPressed` | Calls `RegisterAndBoot(customerId, locationId)` → `POST /api/headsets/register`, then the boot sequence. On success, `SessionFlowManager.EnterLiveSession()` opens the session **immediately** (Room Anchor scan is optional/non-blocking). On failure, stays on Login, shows the error, and **reveals the Demo Mode button**. |
| **Demo Mode** | `OnDemoModePressed` | Offline fallback (auto-revealed after a failed sign-in). Calls `SessionFlowManager.EnterDemoSession()`: sets demo credentials, opens a normal session, runs **real** QR detection. Detected codes are pointable and classified *detected-but-unlisted* (no downloaded legit list). |
| **Leave Session** | `OnLeaveSession` | `Disconnect()` + `SessionFlowManager.ResetForNewSession()` → returns to Login with stored credentials intact (no re-scan needed). |

### Minimal Setup QR (preferred — smallest/least-dense payload)
To keep the QR easy for the Quest 3 passthrough cameras to detect, the web app should encode **only a short setup code** (≈8 alphanumeric chars), nothing else:
```
YT5A5XL3
```
Flow when scanned (`HandleLoginQRScan` → `AcceptSetupCode`):
1. The bare code is recognised by `QrCodeManager.IsBareSetupCode` (non-JSON, alphanumeric, length within `setupCodeMin/MaxLength`). It is classified **Target** (green) during the SignIn phase.
2. The device persists the code and calls `GET /api/setup/{setupCode}` against its **stored/default Backend URL** (`SignalingManager.ResolveSetup`).
3. The response `{ customerId, locationId, roomCode? }` is stored via `SaveConnectionInfo` (room code pre-fills the join field).
4. **Sign In** then registers + boots normally. If the backend is unreachable, the system falls back to **Demo Mode**.

### Backwards-compatible QR formats (still accepted)
- **JSON setup code:** `{ "setupCode": "YT5A5XL3", "apiBaseUrl": "https://host/api" }` — also overrides + persists the backend URL.
- **Legacy:** `{ "customerId": "cust-004", "locationId": "loc-xyz" }` — populates IDs directly (no `/api/setup` call).

### Persistence keys (PlayerPrefs)
`tevr_setupCode`, `tevr_apiBaseUrl` (new), plus `TEVR_CUSTOMER_ID`, `TEVR_LOCATION_ID`, `TEVR_HEADSET_ID`, `TEVR_ROOM_CODE`, `TEVR_AUTH_TOKEN`.

## 8. QR Detection: States, Defaults & Performance
- **Sign-In phase auto-start:** detection runs automatically on the Login panel so the **setup/Sign-In QR** is found without pressing Scan (`autoStartDetection`, default true).
- **Session phase defaults OFF (changed in v2.4):** when the live session opens, QR detection is **armed but NOT running**. The operator presses **Start Detection** to begin scanning room/item codes. This keeps the visible state honest (no "silent" scanning) and avoids burning frame-rate before it's needed.
- **States:** `QrCodeManager.State` = `Off | SignIn | Session` (event `OnDetectionStateChanged`). A persistent indicator on both panels shows **● ON / ○ OFF** plus the phase and a live count.
- **VISUAL-STATE SYNC RULE (project-wide):** every place that changes or defaults a state **also updates the matching visual immediately** — the Detection toggle label (`UpdateDetectionButtonLabel`) **and** the ON/OFF indicator (`UpdateDetectionIndicator`) are refreshed together on every start/stop/clear/state-change. The video toggles are authoritative for their defaults and are **pushed into** `SignalingManager` on init so checkbox == actual stream state. Follow this rule for any new state-driven UI.
- **Markers:** every detected code shows a colour-coded status pip (green=Target/setup, blue=ValidListed, orange=Unlisted, red=Invalid) that settles to `fadeQrDetectionMarkerTransparency` (default 0.2) so continued tracking stays visible.
- **Scales to 50+ codes:** toggle `showPayloadLabels` (TextMeshPro is the heaviest per-code object) and `showDebugCenter` (off by default) on `QrCodeManager` to keep frame-rate stable with many codes.

### 8a. Improving QR detection reliability (Quest 3 passthrough)
The passthrough cameras are lower-resolution than the human eye, so dense/small codes are hard to read. For reliable detection:
- **Keep payloads short** (the setup code is ~8 chars by design). Shorter payload = lower module density = easier to read at distance/angle.
- **Print bigger / higher-contrast** codes with a quiet zone (white border). Matte (non-glossy) media avoids glare.
- **Get closer and steadier**, and ensure room lighting is adequate — detection improves markedly at ~0.3–0.8 m for a hand-sized code.
- **Scan the RoomAnchor first** so item codes get a relative frame immediately (items seen before the anchor are re-registered automatically once it appears, but you'll see them placed correctly sooner).

## 7. Calibration Persistence (Session Panel)
| Button | Method | Endpoint / Behaviour |
| :--- | :--- | :--- |
| **Push QR** | `OnPushQRPressed` | `POST /api/locations/{id}/qr-codes` — **bulk list first, then per-item sequential fallback** (see §3b). Reports `N/M registered`. |
| **Pull QR** | `OnPullQRPressed` | `GET /api/locations/{id}/qr-codes` (downloads + applies calibration). |
| **Start/Stop Detection** | `OnToggleDetectQR` | Toggles MRUK QR detection. **Defaults OFF** when a session opens; updates both the button label and the ON/OFF indicator. |
| **Clear QR** | `OnClearQRPressed` → `QrCodeManager.ClearAllUserData()` | **Full local reset:** tracked codes + visuals, known/dormant poses, server *legit*/name lists (empties the dropdown), pips, focus glow, and the emit-throttle. **No server-side delete is sent** — a later **Pull** (or startup-data) repopulates. |
| **Room Code (submit)** | `OnJoinPressed` | Emits `join-room` to connect to the remote expert. |

Also: **real-time `qr-detected`** events stream automatically while detection is ON and the socket is connected (the dashboard updates live without pressing Push). Push remains the authoritative *persistence* action.

## 9. Full Session Walkthrough — Automatic vs. Manual
This is the end-to-end operator flow. **[AUTO]** = happens with no user action; **[USER]** = requires operator intervention.

### A. Launch & Sign-In
1. **[AUTO]** App boots to the **Sign In (Login)** panel. QR detection auto-starts in **SignIn** phase. The ON indicator shows on the panel.
2. **[USER]** Point the headset at the **setup QR** (≈8-char code) on the admin Locations page. *(One-time: after the first successful resolve, the code + customer/location + backend URL are persisted and pre-filled on every later launch — so this becomes [AUTO] on subsequent runs.)*
3. **[AUTO]** Headset resolves `GET /api/setup/{code}` → stores `customerId`/`locationId`/`roomCode?`. Status text + log update.
4. **[USER]** Press **Sign In**. *(Fields are pre-filled; you can also edit IDs/Backend URL manually as a fallback.)*
5. **[AUTO]** `POST /api/headsets/register` → boots → `EnterLiveSession()` opens the **Session** panel **immediately** (no waiting on calibration). On failure: stays on Login, shows the error, **reveals Demo Mode**. On `401`: prompts a re-scan (does **not** demo).

### B. Session opens
6. **[AUTO]** Local **passthrough preview** starts (clean passthrough; **Compositing OFF** by default). Stream-to-Replit is **ON** by default.
7. **[AUTO]** Socket connects → `join-room` is emitted → headset logs *"waiting for the expert to send a video offer"*. **[SERVER/EXPERT]** must offer (see §4a) — the expert's video then appears in the remote panel.
8. **[AUTO]** On connect, any codes already detected are **flushed** to the server (`qr-detected`).
9. **[AUTO]** QR detection in the session is **OFF**. **[USER]** press **Start Detection** to scan room/item codes.

### C. Calibration & QR capture
10. **[USER]** Look at the **RoomAnchor** QR first. **[AUTO]** It's established as the world reference; the optional calibration hint clears; dormant codes are placed; `qr-detected` (RoomAnchor, world pose) is emitted.
11. **[USER]** Look at each **item** QR. **[AUTO]** Each is tracked, added to the **Look-At dropdown**, colour-classified, and emitted as `qr-detected` (RoomAnchor-relative). Items seen *before* the anchor are **re-emitted automatically** once the anchor exists.
12. **[AUTO]** Moving a code → throttled (~1/s) position update emitted. Per-frame jitter is **not** logged (performance).

### D. Persistence (server sync)
13. **[USER]** **Push QR** → uploads the full calibration. **[AUTO]** Bulk list first; if it fails, per-item sequential retry; final `N/M` tally reported.
14. **[USER]** **Pull QR** → downloads the location's calibration and applies/merges it. **[AUTO]** Dropdown refreshes.
15. **[USER]** **Clear QR** → wipes the **local** list/visuals/glow (no server delete). **[AUTO]** A later Pull/startup-data repopulates.
   - **Merge model:** the dropdown/classifier is the union of the **server "legit" list** (from startup-data/Pull) and **locally detected** codes. Detected codes matching the legit list are *ValidListed* (blue); unmatched are *Unlisted* (orange); the setup code is *Target* (green); malformed are *Invalid* (red).

### E. Remote assistance
16. **[EXPERT→AUTO]** `chat-message` appears in the chat log; `point-to` highlights/points at the referenced code (matched locally by `qrValue`/`name`, or shown at supplied coordinates). Leaving Session or Clear removes the focus glow.
17. **[USER]** Toggle **Compositing** to overlay VR/HUD onto the stream; **Stream-to-Replit** to mute/unmute outbound video; **Show-remote** to hide/show the expert feed. Each toggle's checkbox always matches the actual state.

### F. End / re-enter
18. **[USER]** **Leave Session** → disconnects, returns to Sign In with credentials intact (no re-scan), resets the emit-throttle. **[AUTO]** Detection returns to SignIn phase.

### Checks & balances built in (for "it just works")
- Real-time `qr-detected` **plus** manual Push (live view + authoritative save).
- Bulk push **with** per-item fallback (one bad code can't block the rest).
- Items re-flushed when the anchor appears (no "anchor-only" gaps).
- 15 s REST timeouts + 3 retries; socket reconnect watchdog with Demo fallback.
- Every state change updates its visual (toggles/labels/indicator/logs) in lock-step.
- Non-blocking session entry; calibration is optional and never hides the session.
