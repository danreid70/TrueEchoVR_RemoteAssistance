# TrueEchoVR SignalingManager Synchronization Protocol (v2.3 - RemoteAssistance)

This document outlines the communication protocol between the Unity application (TrueEchoVR) and the Replit backend. This is used by the **SignalingManager** to coordinate WebRTC, chat, and spatial data. Payload shapes below were verified directly against `SignalingManager.cs` / `SessionUiController.cs` / `QrCodeManager.cs`.

> **What changed in v2.3 (READ THIS — REPLIT ACTION REQUIRED):**
> 1. **NEW real-time QR registration:** the headset now emits a **`qr-detected`** Socket.IO event the instant it sees a code (and a throttled update when a code moves). **Replit must add a handler for this event** to make detected codes appear live on the dashboard — see §2 "Outgoing" and the dedicated **§2a**. Previously the dashboard only learned of codes via the manual REST **Push**; that path still exists and is still authoritative for persistence.
> 2. **WebRTC answerer order fixed** (headset→web video now negotiates correctly) — §4.
> 3. **Composite alignment + aspect** corrected (4:3 passthrough) — §4.
> 4. **Clear** now also clears the local "legit"/name lists and the focus glow (still no server-side delete) — §7.
> 5. New **video toggles** (compositing on/off, stream-to-Replit on/off, show-remote) — §4.

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

## 4. WebRTC Requirements
- **Capture:** the headset streams a **composite RenderTexture** (B8G8R8A8_SRGB) = real-world passthrough (PCA `WebCamTexture` background) + the Unity-rendered overlay. The overlay is rendered at the **passthrough camera FOV** (`SignalingManager.passthroughHorizontalFovDeg`, Quest 3 default 82°) so virtual content aligns with the real world as the head turns — tune this field on-device if the overlay drifts.
- **Audio:** Bi-directional audio is supported via `AudioStreamTrack`.
- **Answerer negotiation order (headset side):** the headset is answer-only and MUST negotiate in this order, or the web app receives no video: **(1)** `SetRemoteDescription(offer)` → **(2)** `AddTrack` (reuses the offer's negotiated transceivers) → **(3)** `CreateAnswer`. Remote `ice-candidate` events are applied (queued until the remote description is set). STUN-only by default — add a TURN server if media fails to connect across restrictive networks.

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

## 8. QR Detection: Auto-Start, States & Performance
- **Auto-start:** `QrCodeManager.autoStartDetection` (default **true**) begins detection at launch / on scene-permission grant, so the Sign In code is found without pressing Scan.
- **States:** `QrCodeManager.State` = `Off | SignIn | Session` (event `OnDetectionStateChanged`). A persistent **"● QR Detection: ON"** indicator is shown on both the Login and Session panels with a live count.
- **Markers:** every detected code shows a colour-coded status pip (green=Target/setup, blue=ValidListed, orange=Unlisted, red=Invalid) that settles to `fadeQrDetectionMarkerTransparency` (default 0.2) so continued tracking stays visible.
- **Scales to 50+ codes:** toggle `showPayloadLabels` (TextMeshPro is the heaviest per-code object) and `showDebugCenter` (off by default) on `QrCodeManager` to keep frame-rate stable with many codes.

## 7. Calibration Persistence (Session Panel)
| Button | Method | Endpoint |
| :--- | :--- | :--- |
| **Push QR** | `OnPushQRPressed` | `POST /api/locations/{id}/qr-codes` (uploads local calibration). |
| **Pull QR** | `OnPullQRPressed` | `GET /api/locations/{id}/qr-codes` (downloads + applies calibration). |
| **Start/Stop Detection** | `OnToggleDetectQR` | Toggles MRUK QR detection. |
| **Clear QR** | `OnClearQRPressed` → `QrCodeManager.ClearAllUserData()` | **Full local reset:** removes tracked codes + visuals, known/dormant poses, the server-provided *legit*/name lists (empties the dropdown), all detection pips, and the focus glow. **No server-side delete is sent** — a subsequent **Pull** (or startup-data) repopulates from the server. |
| **Room Code (submit)** | `OnJoinPressed` | Emits `join-room` to connect to the remote expert. |
