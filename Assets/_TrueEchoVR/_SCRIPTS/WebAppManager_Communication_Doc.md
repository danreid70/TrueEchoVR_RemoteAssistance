# TrueEchoVR SignalingManager Synchronization Protocol (v2.1 - RemoteAssistance)

This document outlines the communication protocol between the Unity application (TrueEchoVR) and the Replit backend. This is used by the **SignalingManager** to coordinate WebRTC, chat, and spatial data.

## 1. Connection Endpoints
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
| `GET` | `/api/headsets/{id}/startup-data` | Fetches name dictionary + spatial QRs. |
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
The **Login Panel** drives device provisioning before a session can start. It is wired in `SessionUiController`:

| Button | Method | Behaviour |
| :--- | :--- | :--- |
| **Scan Login Code** | `OnScanLoginCodePressed` | Enables QR detection and waits for a **Setup QR**. Press again to cancel. |
| **Sign In** | `OnSignInPressed` | Calls `RegisterAndBoot(customerId, locationId)` → `POST /api/headsets/register`, then the boot sequence. |

**Setup QR payload** (generated on the admin Locations page, e.g. `…/admin/cust-004/settings/locations`):
```json
{ "customerId": "cust-004", "locationId": "loc-xyz" }
```
When scanned, `HandleLoginQRScan` parses this JSON and populates `BackendConfig.customerId` / `locationId`, after which **Sign In** completes registration. If the backend is unreachable, the system falls back to **Demo Mode**.

## 7. Calibration Persistence (Session Panel)
| Button | Method | Endpoint |
| :--- | :--- | :--- |
| **Push QR** | `OnPushQRPressed` | `POST /api/locations/{id}/qr-codes` (uploads local calibration). |
| **Pull QR** | `OnPullQRPressed` | `GET /api/locations/{id}/qr-codes` (downloads + applies calibration). |
| **Start/Stop Detection** | `OnToggleDetectQR` | Toggles MRUK QR detection. |
| **Clear QR** | `OnClearQRPressed` | Clears tracked QR codes locally. |
| **Room Code (submit)** | `OnJoinPressed` | Emits `join-room` to connect to the remote expert. |
