# TrueEchoVR ⇄ Replit Backend — Integration Guide for the Replit AI

**You are the AI maintaining the Replit `trueechovr` backend.** This document explains what the Unity / Meta
Quest 3 client (`TrueEchoVR_RemoteAssistance`) is, how it talks to you, and exactly what you must provide so
the two systems stay in sync. It is written to be read top-to-bottom by an AI assistant — it states intent,
the contract, the failure semantics, and a verification checklist.

> **Source of truth.** The precise wire schemas live in [`BACKEND_CONTRACT.md`](./BACKEND_CONTRACT.md) and
> [`Assets/_TrueEchoVR/_SCRIPTS/WebAppManager_Communication_Doc.md`](./Assets/_TrueEchoVR/_SCRIPTS/WebAppManager_Communication_Doc.md).
> This guide is the **narrative + responsibilities + checklist** that ties them together. If a schema here ever
> disagrees with `BACKEND_CONTRACT.md`, the contract file wins — and please flag the drift.

---

## 1. What the client is (1-paragraph mental model)

TrueEchoVR is a Mixed-Reality remote-assistance app on Meta Quest 3. A field operator wearing the headset
signs in, scans QR codes in their physical space, and connects to a remote expert who watches the operator's
passthrough video (via WebRTC) and "points at" real-world objects. **Your backend is the broker**: it
provisions/authenticates headsets (REST), stores per-location QR calibration (REST), and relays real-time
signaling — chat, WebRTC offer/answer/ICE, and "point-to" commands — between the expert's web dashboard and
the headset (Socket.IO).

```
 Web Dashboard (expert) ──┐                          ┌── Quest 3 Headset (operator)
                          │   Replit backend (YOU)   │
   Socket.IO + REST  <────┤  - REST: provisioning,   ├────>  Socket.IO + REST
                          │    calibration storage   │
                          │  - Socket.IO: relay      │
                          └──────────────────────────┘
```

---

## 2. The two transports you must run

1. **REST API** under a base path (default `/api`) — provisioning + calibration persistence (§4).
2. **Socket.IO v4 (Engine.IO v4)** server at the host root `/socket.io/` — real-time relay (§5).

Base URL the device uses: `apiHost` + `apiPath` (default `https://live-troubleshooting-app.replit.app` + `/api`).
The device strips a trailing `/api` so the **Socket.IO path resolves at the host root**, not under `/api`.
**Keep REST under `/api` and Socket.IO at the root.**

---

## 3. The end-to-end flow (what happens, in order)

This is the single most important section — implement your endpoints/events to satisfy *this* sequence.

1. **Setup (once per device).** An admin generates a short **setup code** (~8 alphanumeric chars, e.g.
   `YT5A5XL3`) on the Locations page and prints it as a **small** QR. The headset scans it. The QR contains
   **only the code** — never the URL or token (Quest passthrough cameras struggle with dense QRs).
2. **Resolve setup code.** Headset calls `GET /api/setup/{code}`. **You return** `customerId`, `locationId`,
   an optional `roomCode`, and a **bearer `token`**. ⚠️ This is the *only* place the headset obtains the token
   — it is required on the next two calls.
3. **Register headset.** On **Sign In**, headset calls `POST /api/headsets/register` with
   `Authorization: Bearer {token}`. **You return** an `id` → becomes the device's `headsetId`.
4. **Fetch startup data.** Headset calls `GET /api/headsets/{id}/startup-data?locationId={locationId}`
   (bearer required). **You return** `StartupData` — the location's name, version, and the **authoritative
   "legit" QR list** (with RoomAnchor-relative poses + friendly names).
5. **Session opens immediately.** The headset shows its Session UI as soon as steps 3–4 succeed. It does
   **not** wait for a Room Anchor scan — calibration is non-blocking.
6. **Join the live room.** When the operator joins, the headset opens the **Socket.IO** connection and (after
   the handshake) emits `join-room` with `role: "headset"`, the `roomCode`, and `locationId`.
7. **Expert connects.** The dashboard joins the same room; you relay `peer-joined` to the headset, then the
   WebRTC **offer → answer → ice-candidate** exchange (you relay these verbatim between the two peers).
8. **Assist.** You relay `chat-message` both ways and `point-to` (expert → headset). Headset emits
   `health-update` every 60 s.
9. **Calibration sync (optional, REST).** The operator can **Push** local QR calibration
   (`POST /api/locations/{id}/qr-codes`) or **Pull** the latest (`GET /api/locations/{id}/qr-codes`).

---

## 4. REST API — your responsibilities

### Required request headers (the client always sends these)
| Header | Value | What you must do |
| :--- | :--- | :--- |
| `Content-Type` | `application/json` | — |
| `X-Requested-With` | `XMLHttpRequest` | **Do not reject a request that includes it.** (It exists to satisfy AJAX/CSRF guards.) |
| `Authorization` | `Bearer {token}` | Validate on `register` and `startup-data`. |

### Endpoints
| Method | Endpoint | You receive | You must return |
| :--- | :--- | :--- | :--- |
| `GET` | `/api/setup/{setupCode}` | — | `{ "customerId","locationId","roomCode"?,"token" }` — **`token` is mandatory.** |
| `POST` | `/api/headsets/register` | `{ "serialNumber","customerId","firmwareVersion","label" }` | `{ "id","serialNumber","label","customerId","customerName" }` — `id` becomes `headsetId`. |
| `GET` | `/api/headsets/{id}/startup-data?locationId={locationId}` | — | `StartupData` (§4.1). |
| `POST` | `/api/locations/{id}/qr-codes` | `CalibrationUpload` (§4.2) | any `2xx` (persist per location; overwrite is fine). |
| `GET` | `/api/locations/{id}/qr-codes` | — | `CalibrationUpload` (same shape). |

### 4.1 `StartupData` (response)
```jsonc
{
  "locationId": "STR",
  "locationName": "STR",
  "version": "STR",
  "qrCodes": [
    { "qrValue": "STR", "name": "STR",
      "position": { "x":0, "y":0, "z":0 },     // RELATIVE to the RoomAnchor
      "rotation": { "x":0, "y":0, "z":0, "w":1 },
      "metadata": "STR" }
  ],
  "nameDictionary": [ { "qrValue": "STR", "name": "STR" } ]
}
```
This `qrCodes` array is the **"legit" list**: the headset uses it both to place items and to color-code the
dropdown (listed = recognized, unlisted = detected-but-not-in-list).

### 4.2 `CalibrationUpload` (Push/Pull)
```jsonc
{
  "headsetId": "STR",
  "qrCodes": [
    { "qrValue": "STR",
      "position": { "x":0, "y":0, "z":0 },     // item: RELATIVE to RoomAnchor; RoomAnchor itself: world-space
      "rotation": { "x":0, "y":0, "z":0, "w":1 } }
  ]
}
```

### JSON rules (Unity `JsonUtility` is strict)
- `Vector3` = `{ "x":F,"y":F,"z":F }`, `Quaternion` = `{ "x":F,"y":F,"z":F,"w":F }`. **Exact field names + nesting.**
- **No** arrays-for-vectors (`[x,y,z]`), **no** flattened keys (`posX`), **no** `null` for value types, **no**
  dictionaries/polymorphism. Extra fields are ignored, but missing/renamed fields break parsing.

---

## 5. Socket.IO (Engine.IO v4) — your responsibilities

### Handshake (STRICT order — the client enforces it)
1. On connect, **you send** Engine.IO **OPEN** `0{...}`.
2. Client replies Socket.IO **CONNECT** `40` (default namespace).
3. **You must ack with `40`.** Only after this ack does the client emit `join-room`. If you skip/delay the
   `40` ack, the client never joins.
- **Heartbeat is server-driven:** **you** send Engine.IO `2` (ping) on your interval; client replies `3`.
  The client never initiates pings. (The client is tuned to a hardcoded `40` prefix — keep Socket.IO **v4**.)
- **Framing:** every app event is `42["event-name", { ...singleJsonObject }]`. Emit **exactly one** JSON
  object argument — not multiple args, not a bare array.

### 5.1 Client → Server (you receive / relay)
| Event | Payload |
| :--- | :--- |
| `join-room` | `{ "role":"headset","roomCode":"STR","locationId":"STR" }` |
| `chat-message` | `{ "roomCode":"STR","message":"STR","senderRole":"headset" }` |
| `answer` | `{ "roomCode":"STR","answer":{ "sdp":"STR","type":"answer" },"targetSocketId":"STR" }` |
| `ice-candidate` | `{ "roomCode":"STR","candidate":{ "candidate":"STR","sdpMid":"STR","sdpMLineIndex":INT },"targetSocketId":"STR" }` |
| `health-update` | `{ "roomCode":"STR","batteryLevel":INT,"calibrated":BOOL,"headsetId":"STR","locationId":"STR","timestamp":"ISO-8601" }` (every 60 s) |

### 5.2 Server → Client (you emit / relay; the client ignores anything else)
| Event | Payload | Notes |
| :--- | :--- | :--- |
| `peer-joined` | `{ "role":"admin","socketId":"STR" }` | `socketId` becomes the headset's WebRTC target. |
| `offer` | `{ "offer":{ "sdp":"STR","type":"offer" },"fromSocketId":"STR" }` | Expert starts the call; headset answers. |
| `chat-message` | `{ "message":"STR" }` | Client reads `message` only. |
| `point-to` | `{ "name":"STR","qrCode":"STR","pose":{ "position":{Vector3},"rotation":{Quaternion} } }` | See §5.3. |

### 5.3 `point-to` ("look-at") — get this right
The headset resolves a point-to command in this order:
1. **By `qrCode` (the exact QR payload value) first, then by `name`** → points the arrow + glow at the *real,
   locally-tracked* code. **`pose` is NOT required here** — sending just `{ "qrCode":"PUMP_03" }` is enough and
   is the most reliable form.
2. **Coordinate fallback:** if the code isn't locally tracked but a **non-zero** `pose.position` is given, the
   headset shows a highlight at those RoomAnchor-relative coordinates.
3. **Clear:** a command with **no `name`, no `qrCode`, and a zero/absent `position`** clears the highlight.

> Recommendation: always include `qrCode`. A zero/omitted position means "clear" — never send `(0,0,0)` to
> mean a real location.

---

## 6. Failure semantics — what your HTTP codes MEAN to the client

The client treats these very differently. Returning the wrong code changes the operator's experience.

| You return | Client behavior |
| :--- | :--- |
| `2xx` | Normal success. |
| **`401`** (any call) | **Credentials expired/invalid.** Client wipes stored credentials and prompts a **Login Code re-scan**. Does **NOT** demo. Use only when the token/headset is genuinely invalid. |
| **`403` / `404`** on `startup-data` | Same as `401` (treated as expiry). |
| `403` due to missing `X-Requested-With` | Hard failure — but the client **always** sends the header, so don't reject on it. |
| Other 4xx/5xx, timeout, unreachable, unparseable body | Client retries (3× / 2s) then falls back to **offline Demo Mode** (real on-device QR detection, no backend data). |

Additional client-side resilience you should be aware of:
- **15-second timeout** on every REST call — respond within it or the client gives up that attempt.
- **Reconnection:** on an *abnormal* socket close the client auto-reconnects (re-emitting `join-room` for the
  same `roomCode`) up to its max attempts, then drops to its Sign-In screen. A **clean** close (operator left)
  does not reconnect. ⇒ **Expect a headset to silently rejoin the same room after a blip; re-send `peer-joined`
  and a fresh `offer` so media renegotiates.**

---

## 7. Authentication model (summary)
- The **setup code** is public-ish and short (it's a QR). The **token** is the secret, delivered by
  `GET /api/setup/{code}` and sent as `Authorization: Bearer {token}` on `register` + `startup-data`.
- The QR **never** carries the URL or token. The backend URL is stored on-device (default + editable).
- Once provisioned, the device persists `customerId`, `locationId`, `headsetId`, `roomCode`, and `token`, so
  subsequent launches sign in **without** re-scanning. A `401` is your lever to force re-provisioning.

---

## 8. Backend verification checklist (do these to confirm sync)

REST
- [ ] `GET /api/setup/{code}` returns `customerId`, `locationId`, and a non-empty **`token`** (and `roomCode` if used).
- [ ] `POST /api/headsets/register` validates the bearer and returns a stable `id`.
- [ ] `GET /api/headsets/{id}/startup-data?locationId=...` validates the bearer and returns `StartupData` with
      vectors/quaternions in the **nested object** form.
- [ ] `POST`/`GET /api/locations/{id}/qr-codes` round-trip the `CalibrationUpload` shape unchanged.
- [ ] A request with `X-Requested-With: XMLHttpRequest` is **never** rejected because of that header.
- [ ] Invalid/expired token returns **`401`** (so the client prompts re-scan rather than demoing).
- [ ] All responses come back within **15 s**.

Socket.IO
- [ ] Server is Socket.IO **v4 / Engine.IO v4** at the **host root** `/socket.io/`.
- [ ] Handshake: send `0{...}` → receive `40` → **ack `40`**.
- [ ] After the ack you receive `join-room` from the headset.
- [ ] You send server-side pings (`2`); you receive client pongs (`3`).
- [ ] You relay `chat-message`, `offer`/`answer`/`ice-candidate`, and `point-to` with **one** JSON-object arg.
- [ ] `point-to` uses `qrCode` (exact payload) and/or `name`; a zero/absent position = "clear".
- [ ] After a headset auto-reconnect (same `roomCode`), you re-send `peer-joined` + a fresh `offer` so WebRTC
      media renegotiates (this is the client's known follow-up test — see `TESTING_CHECKLIST.md`).

---

## 9. Common drift bugs (seen before — avoid these)
- ❌ Putting the full URL/token in the setup QR (too dense for Quest cameras; the client expects only the code).
- ❌ Serializing poses as arrays or flat keys (breaks `JsonUtility`).
- ❌ Using `message`'s sibling fields (e.g. `text`) for chat — the client reads **`message`**.
- ❌ Sending `point-to` with position `(0,0,0)` to mean a real spot — that's the **clear** sentinel.
- ❌ Forgetting the `40` namespace ack — the client waits forever and never emits `join-room`.
- ❌ Returning `404/403` on `startup-data` for transient errors — the client treats it as credential expiry.
- ❌ Hosting Socket.IO under `/api` — it must be at the host root.

---

*Keep this guide and `BACKEND_CONTRACT.md` updated together whenever the client schema changes.*
