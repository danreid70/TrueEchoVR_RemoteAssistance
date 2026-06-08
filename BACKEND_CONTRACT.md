# TrueEchoVR — Backend Contract (for the Replit `trueechovr` application)

**Audience:** the Replit backend (and its AI assistant).
**Purpose:** a single, self-contained, verified specification of everything the Unity / Meta Quest 3 client
(`TrueEchoVR_RemoteAssistance`) sends to, and expects from, the backend — so the two sides stay compatible.

> Every schema below was verified against the Unity client source
> (`SignalingManager.cs`, `SessionUiController.cs`, `QrCodeManager.cs`). The client serializes JSON with Unity
> `JsonUtility`, which is **strict about field names and nesting** and does **not** support dictionaries,
> polymorphism, or `null` for value types. Match these shapes exactly.

---

## 0. TL;DR — what the backend must provide
1. A **REST API** under a base path (default `/api`) with the 5 endpoints in §3.
2. A **Socket.IO v4 (Engine.IO v4)** server at `/socket.io/` that speaks the events in §4.
3. An **auth model**: `GET /api/setup/{code}` returns a **bearer token**; the client then sends
   `Authorization: Bearer {token}` on `register` and `startup-data`.
4. Enforcement of the **`X-Requested-With: XMLHttpRequest`** header (the client always sends it; if your
   CSRF guard requires it, that's fine — just don't reject requests that include it).

---

## 1. Transport & base URL
- **Base URL** is stored on the device: `apiHost` + `apiPath` (default
  `https://live-troubleshooting-app.replit.app` + `/api`). It is overridable from the client's Login panel and
  persisted on-device; the QR code does **not** carry the URL.
- **REST:** `https://{host}/api/...`
- **WebSocket (Socket.IO):** `wss://{host}/socket.io/?EIO=4&transport=websocket`
- The client splits a trailing `/api` off the configured base so the **root-level** Socket.IO path still
  resolves. Keep REST under `/api` and Socket.IO at the host root.

---

## 2. Data types (Unity JsonUtility)
| Type | JSON shape |
| :--- | :--- |
| `Vector3` | `{ "x": 0.0, "y": 0.0, "z": 0.0 }` |
| `Quaternion` | `{ "x": 0.0, "y": 0.0, "z": 0.0, "w": 1.0 }` |
| timestamps | ISO-8601 / round-trip (`"O"`) strings |

⚠️ Do **not** send poses as arrays (`[x,y,z]`) or flattened (`posX`). Use the nested objects above.
⚠️ Omit-vs-zero: a `point-to` with a zero/absent `position` is interpreted as **"clear the highlight"** (§4).

---

## 3. REST API

### Required request headers (client sends these on every call)
| Header | Value | Backend expectation |
| :--- | :--- | :--- |
| `Content-Type` | `application/json` | — |
| `X-Requested-With` | `XMLHttpRequest` | Must **not** be the reason a request is rejected. (Client sends it specifically to satisfy an AJAX/CSRF guard; the client treats a missing-header 403 as a hard failure.) |
| `Authorization` | `Bearer {token}` | Present after setup-code resolution. Validate on `register` and `startup-data`. |

### Client behaviour the backend should be aware of
- **Per-request timeout:** every call times out after **15 s** (the client never hangs waiting on a stalled
  response). Respond within that window.
- **Retries:** up to **3 attempts**, **2 s** apart, on any non-success.
- **Credential expiry (re-scan, NOT demo):** a **`401`** on any call — or **`403/404`** on `startup-data` —
  makes the client wipe stored credentials, raise `OnCredentialsExpired`, and prompt the operator to
  **re-scan a Login Code**. The client does **not** silently fall to Demo Mode in this case. Return these
  codes **only** when the token/headset is genuinely invalid/expired.
- **Demo Mode fallback:** other failures during boot (network unreachable, timeout, unparseable body) drop the
  client to an **offline Demo Mode** that still runs real on-device QR detection. There is also a manual
  **Demo Mode** button on the Login panel (auto-revealed after a failed sign-in).

### 3.1 `GET /api/setup/{setupCode}`
Resolves a short (~8-char alphanumeric) setup code scanned from a QR.
**Response:**
```jsonc
{
  "customerId": "STR",
  "locationId": "STR",
  "roomCode":   "STR",   // optional; pre-fills the session room-code field
  "token":      "STR"    // bearer token used for register + startup-data
}
```

### 3.2 `POST /api/headsets/register`
**Request:**
```jsonc
{ "serialNumber": "STR", "customerId": "STR", "firmwareVersion": "STR", "label": "STR" }
```
**Response:**
```jsonc
{ "id": "STR", "serialNumber": "STR", "label": "STR", "customerId": "STR", "customerName": "STR" }
```
`id` is the **headsetId** used in subsequent calls and telemetry.

### 3.3 `GET /api/headsets/{id}/startup-data?locationId={locationId}`
**Response (`StartupData`):**
```jsonc
{
  "locationId": "STR",
  "locationName": "STR",
  "version": "STR",                                  // optional context/version tag
  "qrCodes": [                                        // AUTHORITATIVE "legit" QR list
    {
      "qrValue": "STR",                              // QR payload string = matching identity
      "name": "STR",
      "position": { "x":0, "y":0, "z":0 },           // RELATIVE to the RoomAnchor
      "rotation": { "x":0, "y":0, "z":0, "w":1 },
      "metadata": "STR"                              // optional free-form
    }
  ],
  "nameDictionary": [ { "qrValue": "STR", "name": "STR" } ]
}
```
`qrCodes[].qrValue` drives the client's **color-coded QR dropdown** and marker classifier (a value present
here = "legit/listed").

### 3.4 `POST /api/locations/{id}/qr-codes`  (operator taps **Push**)
Uploads the operator's current local calibration. **Request (`CalibrationUpload`):**
```jsonc
{
  "headsetId": "STR",
  "qrCodes": [
    { "qrValue": "STR",
      "position": { "x":0, "y":0, "z":0 },           // item poses RELATIVE to the RoomAnchor
      "rotation": { "x":0, "y":0, "z":0, "w":1 } }
  ]
}
```
Return any 2xx. Persist per `locationId` (overwrite/atomic is fine).

### 3.5 `GET /api/locations/{id}/qr-codes`  (operator taps **Pull**)
Returns the latest calibration for the location in the **same `CalibrationUpload` shape** as 3.4. The client
applies each entry and adds every `qrValue` to its "legit" set.

> **Spatial frame:** all item poses are **relative to the RoomAnchor** zero-point (the RoomAnchor entry itself
> is world-space). The client recently moved RoomAnchor persistence onto a Meta Spatial Anchor on-device, but
> this is transparent to the backend — the relative poses you store/serve are unchanged.

---

## 4. Socket.IO (Engine.IO v4)

### Handshake (strict order)
1. Server sends Engine.IO **OPEN** `0{...}` on connect.
2. Client replies Socket.IO **CONNECT** `40` (default namespace).
3. Server **must ack with `40`**. Only after this ack does the client emit `join-room` and start telemetry.
- **Heartbeat is server-driven:** server sends Engine.IO `2` (ping) on its interval; client replies `3` (pong).
  The client never initiates pings.
- **Framing:** application events are `42["event-name", { ...singleJsonObject }]`. The client's parser reads
  exactly **one** JSON object argument after the event name — do not emit multiple args or a bare array.
- **Reconnection:** on an **abnormal** close the client auto-reconnects (up to `maxReconnectAttempts`,
  `reconnectDelay` apart) and re-emits `join-room` after the `40` ack — so expect a headset to rejoin the same
  `roomCode`. After a **clean** close (operator pressed *Leave Session*) the client does not reconnect. When
  retries are exhausted the client drops back to its Sign In screen (and offers offline Demo Mode).

### 4.1 Client → Server (emitted by the headset)
| Event | Payload |
| :--- | :--- |
| `join-room` | `{ "role": "headset", "roomCode": "STR", "locationId": "STR" }` |
| `chat-message` | `{ "roomCode": "STR", "message": "STR", "senderRole": "headset" }` |
| `answer` | `{ "roomCode": "STR", "answer": { "sdp": "STR", "type": "answer" }, "targetSocketId": "STR" }` |
| `ice-candidate` | `{ "roomCode": "STR", "candidate": { "candidate": "STR", "sdpMid": "STR", "sdpMLineIndex": INT }, "targetSocketId": "STR" }` |
| `health-update` | `{ "roomCode": "STR", "batteryLevel": INT, "calibrated": BOOL, "headsetId": "STR", "locationId": "STR", "timestamp": "ISO-8601" }` (every **60 s**) |

### 4.2 Server → Client (handled by the headset; anything else is ignored)
| Event | Payload | Notes |
| :--- | :--- | :--- |
| `peer-joined` | `{ "role": "admin", "socketId": "STR" }` | `socketId` is the WebRTC target for `answer`/`ice-candidate`. |
| `offer` | `{ "offer": { "sdp": "STR", "type": "offer" }, "fromSocketId": "STR" }` | Expert starts the WebRTC call; headset answers. |
| `chat-message` | `{ "message": "STR" }` | Client reads `message` only. |
| `point-to` | `{ "name": "STR", "qrCode": "STR", "pose": { "position": {Vector3}, "rotation": {Quaternion} } }` | "Look-at" command. See **§4.3** for the exact resolution rules. |

> The headset does **not** handle a `pull-qrcodes` socket event. To refresh calibration, rely on the operator
> tapping **Pull** (REST `GET /api/locations/{id}/qr-codes`).

### 4.3 `point-to` ("look-at") resolution — IMPORTANT
This is how the admin/dashboard tells the headset to point at a QR code. The headset shows a directional
**arrow** plus a **pulsing glow** on the target. Resolution order (`SessionFlowManager.OnRemotePointToReceived`):

1. **Cross-reference a locally-tracked code first (preferred).** The headset matches the command to a code it
   has already seen, **by `qrCode` (the exact QR payload value) first, then by `name`**. On a match it points
   at the *real, physically-tracked code* — identical to the operator picking it from the on-headset dropdown.
   - ✅ `pose` is **NOT required** for this case. Sending just `{ "qrCode": "PUMP_VALVE_03" }` is enough.
   - `qrCode` must equal the `qrValue` you provided in `startup-data` / `qr-codes` for that code.
2. **Coordinate fallback.** If the code is **not currently represented** on the headset but you supply a
   non-zero `pose.position` (RoomAnchor-relative), the headset shows a position highlight (outline + billboard
   label) at those coordinates. Use this when you can't rely on the headset having seen the code yet.
3. **Clear the highlight.** Send a `point-to` with **no `name`, no `qrCode`, and no/zero `position`** to clear.

**Recommendations for the backend/dashboard:**
- Always include **`qrCode`** = the QR payload value. It is the most reliable identifier.
- Include **`name`** as the human-friendly label (shown on the headset HUD and used as a secondary match key).
- Include `pose` when you have RoomAnchor-relative coordinates — it enables the fallback if the headset hasn't
  seen the code, and is harmless when it has.
- `pose.position` of `{0,0,0}` is treated as "no coordinates" (the client uses zero as the sentinel for absent).

---

## 5. WebRTC
- The expert (admin) is the **offerer**; the headset is the **answerer** (see `offer` → `answer` above).
- ICE candidates are exchanged via the `ice-candidate` event, addressed by `targetSocketId`.
- The headset streams the **Meta Passthrough camera** (real-world view) as the video track, plus bidirectional
  audio. A TURN server is recommended for corporate-firewall reliability (not yet configured).

---

## 6. End-to-end session flow (happy path)
1. **Setup (once):** operator scans the small setup-code QR → client `GET /api/setup/{code}` → stores
   `customerId`, `locationId`, optional `roomCode`, and `token`.
2. **Register/boot:** `POST /api/headsets/register` (Bearer token) → `headsetId`; then
   `GET /api/headsets/{headsetId}/startup-data?locationId=...` → `StartupData` (legit QR list + name dictionary).
3. **Connect:** open Socket.IO → `40` handshake → emit `join-room`. Begin 60 s `health-update` telemetry.
4. **Calibrate:** operator scans the RoomAnchor QR (now persisted as a Meta Spatial Anchor on-device) and item
   QRs. **Push** uploads `CalibrationUpload`; **Pull** re-applies it on another device/session.
5. **Assist:** expert `offer` → headset `answer` + `ice-candidate` exchange → live video/audio. Expert sends
   `chat-message` and `point-to`; operator replies with `chat-message`.

---

## 7. Compatibility checklist for the backend
- [ ] REST under `/api`; Socket.IO at host root (`/socket.io/`, `EIO=4`).
- [ ] `GET /api/setup/{code}` returns a **`token`** (and customer/location).
- [ ] `register` + `startup-data` validate `Authorization: Bearer {token}`; never 403 a request *because* it
      carries `X-Requested-With: XMLHttpRequest`.
- [ ] `startup-data` returns `qrCodes[]` + `nameDictionary[]` with **nested Vector3/Quaternion** poses.
- [ ] `locations/{id}/qr-codes` round-trips the `CalibrationUpload` shape (POST stores, GET returns same).
- [ ] Socket.IO acks the namespace with `40`; emits server-driven `2` pings.
- [ ] Server→client events use the **exact** names/shapes in §4.2 (`message` not `text`; `offer` nested; etc.).
- [ ] `point-to` sends **`qrCode`** = the code's `qrValue` (and ideally `name`); `pose` optional (§4.3).
- [ ] `point-to` with no `name`/`qrCode` and a zero/absent `position` is treated as "clear".

---
*Generated from the verified Unity client. If the client schema changes, update this file and
`Assets/_TrueEchoVR/_SCRIPTS/WebAppManager_Communication_Doc.md` together.*
