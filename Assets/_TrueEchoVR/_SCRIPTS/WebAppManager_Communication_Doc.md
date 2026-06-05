# TrueEchoVR SignalingManager Synchronization Protocol (v2.1 - RemoteAssistance)

This document outlines the communication protocol between the Unity application (TrueEchoVR) and the Replit backend. This is used by the **SignalingManager** to coordinate WebRTC, chat, and spatial data.

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

### Outgoing (Unity to Replit)
| Event Name | Payload Structure | Description |
| :--- | :--- | :--- |
| `join-room` | `{ "role": "headset", "roomCode": "STR", "locationId": "STR" }` | Headset registration. |
| `chat-message` | `{ "text": "STR" }` | Text message sent by user. |
| `answer` | `{ "offer": { "sdp": "STR", "type": "answer" }, "targetSocketId": "STR" }` | WebRTC Answer targeting the Admin. |
| `ice-candidate`| `{ "candidate": "STR", "sdpMid": "STR", "sdpMLineIndex": INT, "targetSocketId": "STR" }` | WebRTC ICE candidate data. |
| `health-update` | `{ "batteryLevel": INT, "calibrated": BOOL, "headsetId": "STR", "locationId": "STR", "timestamp": "ISO-8601" }` | periodic system health telemetry. |

### Incoming (Replit to Unity)
| Event Name | Payload Structure | Description |
| :--- | :--- | :--- |
| `peer-joined` | `{ "role": "admin", "socketId": "STR" }` | Admin connection notification. |
| `offer` | `{ "offer": { "sdp": "STR", "type": "offer" }, "fromSocketId": "STR" }` | WebRTC SDP offer from expert console. |
| `chat-message` | `{ "text": "STR" }` | Text message from expert console. |
| `point-to` | `{ "name": "STR", "qrCode": "STR", "pose": { "position": VEC3, "rotation": QUAT } }` | Enriched calibration-aware highlight command. |
| `pull-qrcodes` | `{...payload}` | Request to refresh local QR data. |

## 3. Data Persistence (REST API)
Persistence is handled via standard HTTP requests to the Replit API.

| Method | Endpoint | Description |
| :--- | :--- | :--- |
| `GET` | `/api/setup/{setupCode}` | **Resolves a Sign In setup code** → `{ customerId, locationId, roomCode? }`. Drives the minimal-QR handshake (see §6). |
| `GET` | `/api/headsets/{id}/startup-data` | Fetches name dictionary + spatial QRs. |
| `POST` | `/api/headsets/register` | Registers the device → `{ id }` (headset id). |
| `POST` | `/api/locations/{id}/qr-codes` | Uploads current location calibration (Atomic). |
| `GET` | `/api/locations/{id}/qr-codes` | Fetches latest calibration for a location. |

## 4. WebRTC Requirements
- **Capture:** Quest 3 hardware prevents direct raw camera access; Unity sends a **Capture RenderTexture** (B8G8R8A8_SRGB format).
- **Audio:** Bi-directional audio is supported via `AudioStreamTrack`.

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
| **Sign In** | `OnSignInPressed` | Calls `RegisterAndBoot(customerId, locationId)` → `POST /api/headsets/register`, then the boot sequence. |

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
| **Clear QR** | `OnClearQRPressed` | Clears tracked QR codes locally. |
| **Room Code (submit)** | `OnJoinPressed` | Emits `join-room` to connect to the remote expert. |
