# TrueEchoVR ⇄ Replit Backend — Integration Guide for the Replit AI

**You are the AI maintaining the Replit `trueechovr` backend.** This document explains what the Unity / Meta
Quest 3 client (`TrueEchoVR_RemoteAssistance`) is, how it talks to you, and exactly what you must provide so
the two systems stay in sync. It is written to be read top-to-bottom by an AI assistant — it states intent,
the contract, the failure semantics, and a verification checklist.

> **Source of truth.** The precise wire schemas live in [`BACKEND_CONTRACT.md`](./BACKEND_CONTRACT.md).
> This guide is the **narrative + responsibilities + checklist** that ties it together. If a schema here ever
> disagrees with `BACKEND_CONTRACT.md`, the contract file wins — and please flag the drift.

> ### 🔴 NEW in client v2.4 — ACTION REQUIRED on the backend
> 1. **NEW Socket.IO event `qr-detected` (client → server).** The headset now registers each detected QR code
>    in **real time** over the socket so the dashboard can show codes the instant they're seen — see **§5.1a**.
>    **You must add a handler.** Without it, the dashboard won't update live (it would only learn of codes when
>    the operator taps **Push**).
> 2. **Push now sends a batch FIRST, then falls back to ONE code per request.** Your
>    `POST /api/locations/{id}/qr-codes` must accept a `qrCodes` array of **any length (1..N)** and upsert each
>    entry by `qrValue`. A backend that only reads `qrCodes[0]` is the #1 cause of "the RoomAnchor saved but the
>    other codes didn't." See **§4 (Push)**.
> 3. **"Waiting for headset to connect" is almost always a server/dashboard issue, not the headset.** The
>    headset is **answer-only** — it never sends an offer. You must relay headset presence to the admin and the
>    **admin must send the WebRTC `offer`**. See the new **§5.4 diagnostic**.
> 4. *(Headset-side, FYI only — no backend change.)* The streamed video now aligns to the **left** passthrough
>    camera and **defaults to clean passthrough** (VR/HUD overlay off until the operator enables it). Display the
>    incoming video at its **native aspect** (Quest 3 passthrough is 4:3) — do not force 16:9.
>
> ### 🟢 client v2.5 — FYI only, NO backend action required
> v2.5 is **UI / visual / robustness only**; the wire contract is unchanged. For awareness:
> - `point-to`, the on-headset dropdown, and "stop pointing" now run through **one** unified code path on the
>   headset. The `point-to` schema and the "clear" sentinel (no `name`/`qrCode`, zero/absent `position`) are
>   exactly as in §5.3 — no change.
> - **Push** is hardened client-side: the operator sees the true uploadable count and is prevented from pushing
>   an empty set (e.g. before a RoomAnchor exists). Same POST shape (§4.2). **Pull** now also tolerates a
>   top-level JSON array in addition to the documented `CalibrationUpload` object — returning the object is still
>   preferred.

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
7. **Expert connects.** The dashboard joins the same room. **You must tell the headset an admin is present**
   (relay `peer-joined`). **The admin/dashboard then sends the WebRTC `offer`** — the headset is *answer-only*
   and will never offer. You relay **offer → answer → ice-candidate** verbatim between the two peers. ⚠️ If this
   relay/offer step is missing, the dashboard sits on "Waiting for headset to connect" forever even though the
   headset is connected and waiting (§5.4).
8. **Real-time QR registration (NEW).** While session detection is on, the headset emits **`qr-detected`** for
   each code the instant it sees it (RoomAnchor first, then items; throttled position updates as codes move).
   Upsert these so the dashboard updates live (§5.1a).
9. **Assist.** You relay `chat-message` both ways and `point-to` (expert → headset). Headset emits
   `health-update` every 60 s.
10. **Calibration sync (REST).** The operator can **Push** local QR calibration
   (`POST /api/locations/{id}/qr-codes`, batch-then-per-item) or **Pull** the latest
   (`GET /api/locations/{id}/qr-codes`). Push is the authoritative *persistence*; `qr-detected` is the live feed.

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
| `POST` | `/api/locations/{id}/qr-codes` | `CalibrationUpload` (§4.2) — `qrCodes` array of **ANY length (1..N)** | any `2xx`. **Upsert each entry by `qrValue`** (don't just read `qrCodes[0]`). See note below. |
| `GET` | `/api/locations/{id}/qr-codes` | — | `CalibrationUpload` (same shape). |

> **Push is batch-first, then per-item (NEW in v2.4).** The client first POSTs the **whole list** in one
> `CalibrationUpload`. **If that POST fails**, it automatically retries by POSTing **each code on its own** — one
> request per code, each body a `CalibrationUpload` with a **single-element `qrCodes` array** (identical shape,
> length 1), continuing past individual failures and reporting an `N/M registered` tally. **Therefore your
> endpoint must accept a `qrCodes` array of any length and upsert every element by `qrValue`.** Reading only the
> first element is the most common cause of "the RoomAnchor registered but the other codes didn't."

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
| `qr-detected` **(NEW)** | see §5.1a — one event **per code** (not a list) |

### 5.1a `qr-detected` (NEW — real-time QR registration)
Emitted while session QR detection is ON and the socket is connected. **One event per code** (multiple codes
arrive as a sequence). Add a handler that **upserts by `(locationId, qrValue)`** into the live room view.
```jsonc
{
  "roomCode":    "STR",
  "locationId":  "STR",
  "headsetId":   "STR",
  "qrValue":     "STR",          // the code's identity — use as the upsert key
  "name":        "STR",          // friendly name if known, else ""
  "listed":      true,            // true = this payload is in the location's "legit" list
  "isRoomAnchor": false,          // true  => position/rotation are WORLD (this code IS the reference frame)
                                  // false => position/rotation are RELATIVE to the RoomAnchor (an item)
  "position":    { "x":0, "y":0, "z":0 },
  "rotation":    { "x":0, "y":0, "z":0, "w":1 },
  "timestamp":   "ISO-8601"
}
```
Notes for your handler:
- **RoomAnchor first, then items.** Use `isRoomAnchor` to interpret the frame (same convention as the REST
  `CalibrationUpload`). If items were detected before the anchor existed, the headset **re-emits** the whole set
  right after the anchor appears — so expect a second burst, and just upsert idempotently.
- **`listed=false`** = the headset saw a code not in the location's legit list (surface as "unrecognised" if you
  like; you don't have to persist it).
- **Fire-and-forget**, no ack expected. This is the *live overlay feed*; the authoritative save is still **Push**
  (REST §4). It's fine to also persist these upserts.

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

### 5.4 "Waiting for headset to connect" — diagnostic (READ if video never starts)
The headset is **answer-only**: it builds its WebRTC PeerConnection and streams **only after it receives an
`offer`**. After `join-room` it logs *"Joined room … waiting for the expert to send a video offer."* and waits.
If the dashboard still shows "waiting", the gap is on the server/dashboard side. Check, in order:
1. **Same room:** are the `headset` and `admin` joined to the **same `roomCode`**? (It's upper-cased on the
   headset — compare exact strings.)
2. **Presence relay:** when the headset emits `join-room`, do you notify the admin (so it knows to call)? The
   headset will **never** offer; if the admin never learns a headset joined, no offer is ever created.
3. **Offer direction:** is the **admin/dashboard** creating + sending the `offer` (with a recvonly video
   transceiver)? A frequent bug is both sides waiting for the other to offer.
4. **Answer/ICE routing:** is the headset's `answer` (and its `ice-candidate`s) routed back to the admin's
   `socketId` (the `targetSocketId` the headset echoes from `peer-joined`/`offer`)?
5. **Payload nesting:** SDP must be under the `offer`/`answer` key; ICE under `candidate` (see §5.1/§5.2).
6. **Media but no pixels:** if the answer is sent but no video appears, it's ICE/NAT — add a TURN server.

> The headset surfaces each milestone (`join-room → peer-joined → offer → answer`) in its in-session chat log
> as `[Backend] …` lines, so you can confirm exactly how far the handshake got from the operator's side.

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
- [ ] **(NEW) You handle `qr-detected` (§5.1a): upsert by `(locationId, qrValue)`; idempotent on the re-emit
      burst after the RoomAnchor appears; dashboard updates live.**
- [ ] **(NEW) On `join-room`, you notify the admin so the ADMIN sends the WebRTC `offer`** (headset is
      answer-only). The dashboard must not wait for the headset to offer.
- [ ] After a headset auto-reconnect (same `roomCode`), you re-send `peer-joined` + a fresh `offer` so WebRTC
      media renegotiates (this is the client's known follow-up test — see `TESTING_CHECKLIST.md`).

REST (NEW)
- [ ] `POST /api/locations/{id}/qr-codes` accepts a `qrCodes` array of **any length (1..N)** and **upserts each
      element by `qrValue`** (handles both the batch and the per-item fallback).

---

## 9. Common drift bugs (seen before — avoid these)
- ❌ Putting the full URL/token in the setup QR (too dense for Quest cameras; the client expects only the code).
- ❌ Serializing poses as arrays or flat keys (breaks `JsonUtility`).
- ❌ Using `message`'s sibling fields (e.g. `text`) for chat — the client reads **`message`**.
- ❌ Sending `point-to` with position `(0,0,0)` to mean a real spot — that's the **clear** sentinel.
- ❌ Forgetting the `40` namespace ack — the client waits forever and never emits `join-room`.
- ❌ Returning `404/403` on `startup-data` for transient errors — the client treats it as credential expiry.
- ❌ Hosting Socket.IO under `/api` — it must be at the host root.
- ❌ **(NEW) Ignoring `qr-detected`** — the dashboard then won't show codes live (only after a manual Push).
- ❌ **(NEW) Reading only `qrCodes[0]`** on the calibration POST — the per-item fallback sends single-element
  arrays, and the batch sends many; upsert **every** element by `qrValue`.
- ❌ **(NEW) Waiting for the headset to send the WebRTC offer** — it never will; the admin must offer.

---

*Keep this guide and `BACKEND_CONTRACT.md` (the single canonical wire contract) updated together whenever the
client schema changes.*
