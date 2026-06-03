# TrueEchoVR SignalingManager Synchronization Protocol (v2.1 - RemoteAssistance)

This document outlines the communication protocol between the Unity application (TrueEchoVR) and the Replit backend. This is used by the **SignalingManager** to coordinate WebRTC, chat, and spatial data.

## 1. Connection Endpoints
- **WebSocket (Socket.io):** `wss://live-troubleshooting-app.replit.app/socket.io/?EIO=4&transport=websocket`
- **REST API (Persistence):** `https://live-troubleshooting-app.replit.app/api`

## 2. Real-time Signaling (Socket.io)
The Unity application uses **Socket.io-style framing** for WebSocket messages. Every message is prefixed with `42` followed by a JSON array: `42["event-name", {payload}]`.

### Connection Maintenance
- **Heartbeat (Ping):** Unity sends `2` every 5 seconds.
- **Heartbeat (Pong):** Replit responds with `3`.
- **Latency Tracking:** The `SignalingManager` calculates round-trip time and exposes it via `currentLatency`.

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
