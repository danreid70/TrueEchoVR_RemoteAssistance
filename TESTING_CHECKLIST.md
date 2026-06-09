# TrueEchoVR — Testing Checklist

A living checklist for verifying the sign-in flow, QR detection, session/UI behavior, and networking.
Legend: ✅ automated/verified · 🖐️ manual (Editor) · 📱 manual (on-device, needs Quest + live backend/admin)

---

## 1. Sign-In → Session Flow
- ✅ App boots to the **Login** panel (Login active, Session/HUD hidden). *(PlayMode flow test)*
- ✅ Valid credentials → `EnterLiveSession()` opens the Session window immediately (Login hidden). *(PlayMode flow test)*
- ✅ **Leave Session** → `ResetForNewSession()` returns to Login with stored credentials. *(PlayMode flow test)*
- 🖐️ Sign In with **no** internet → falls back to Demo Mode (offline) automatically.
- 📱 Sign In against the live backend → registers headset, fetches `StartupData`, opens session.
- 📱 Re-launch after a successful setup → fields pre-populated, Sign In works **without** re-scanning.

## 2. QR Detection Lifecycle
- 🖐️ `TrueEchoVR/Debug/Simulate Login Setup Code (TEVRDEMO)` → setup code accepted, status updates.
- 🖐️ `TrueEchoVR/Debug/Simulate Full Demo Room` → RoomAnchor + items appear, dropdown populates.
- 📱 Detected codes **stay visible**; pressing **Cancel Scan** then scanning again re-detects in-view codes.
- 📱 During the Login phase, the headset auto-detects the setup code without pressing Scan.
- 📱 Detected-but-unlisted codes show the correct color classification (no downloaded legit list).

## 3. UI / Panels
- ✅ Scene-wiring validator reports **0 issues** (`TrueEchoVR/Validate Scene Wiring`).
- 📱 Login window has a visible background/border; no overlapping/oversized text.
- 📱 Drag-to-move a panel: it stays where released and keeps facing the user while dragged.
- 📱 The HUD directional arrow points at the selected/“point-to” target and hides when cleared.
- ✅ **(v2.4) Chat log scrolls vertically only** (no horizontal drag); newest line stays in view. *(editor smoke)*
- ✅ **(v2.4) State↔visual sync at boot:** compositing toggle OFF, stream-to-Replit ON, show-remote ON, and the
  manager's actual state matches each toggle. *(PlayMode test)*
- 📱 **(v2.4) Session QR detection defaults OFF** — the Detection button reads "Start Detection" and the ON/OFF
  indicator says OFF until the operator starts it.
- 📱 **(v2.4) Video toggles:** Compositing overlays/removes the VR/HUD on the stream; Stream-to-Replit
  mutes/unmutes outbound video (local preview stays); Show-remote hides/shows the expert feed. Each checkbox
  always matches the actual behaviour.

## 4. Demo Mode
- ✅ Demo Mode enters a Session (real detection path). *(PlayMode flow test)*
- 📱 **Demo Mode** button auto-reveals after a failed sign-in.
- 📱 Demo session: real QR detection runs, codes are pointable, classified *detected-but-unlisted*.

## 5. Networking / Signaling
- ✅ Signaling contract smoke test passes for `chat-message`, `point-to` (coords + clear), `peer-joined`, `offer` parse. *(`TrueEchoVR/Debug/Run Signaling Contract Smoke Test`)*
- ✅ Every REST call has a 15s timeout (no infinite hang on a stalled server).
- 📱 Socket.IO handshake completes (`0` → `40` → `40` ack) and `join-room` is emitted.
- 📱 Server-driven heartbeat: server `2` → client `3`; `currentLatency` updates.
- 📱 **(v2.4) Streaming handshake:** in-session chat shows `[Backend]` milestones `join-room → peer-joined →
  offer → answer`. If it stalls at "waiting for the expert to send a video offer", the **admin/server** isn't
  offering (headset is answer-only) — see `REPLIT_AI_INTEGRATION_GUIDE.md` §5.4.

## 5a. QR Sync ⇄ Backend (v2.4)
- ✅ Bulk-then-per-item push builders produce valid `CalibrationUpload` shapes. *(editor smoke)*
- 📱 **Real-time registration:** with detection ON in a live session, each detected code appears on the Replit
  dashboard live via `qr-detected` (RoomAnchor first, then items). Requires the Replit `qr-detected` handler.
- 📱 **Anchor-after-item:** detect an item *before* the RoomAnchor → confirm it still registers once the anchor
  is scanned (re-emit burst).
- 📱 **Push fallback:** if the batch POST fails, the client retries per-item and reports `N/M registered`;
  confirm the backend upserts every element (not just `qrCodes[0]`).
- 📱 **Pull/merge:** Pull repopulates the dropdown; detected vs. listed colour classification is correct.

## 6. Resilience (Watchdog & Credential Expiry)
- 📱 **Mid-session reconnect:** kill the WebSocket (drop server / network blip) → UI shows
  `RECONNECTING (n/max)`; on recovery the session resumes; on exhaustion it returns to Login + Demo.
- 📱 **Manual reconnect:** `SignalingManager.Reconnect()` re-opens for the current room.
- 📱 **Credential expiry:** backend returns `401` (or `403/404` on `startup-data`) → credentials cleared,
  UI prompts a **Login Code re-scan** (does NOT silently drop to Demo).

---

## ⏳ Deferred / Not-Yet-Verified (follow-up)

These require a live admin peer + device and are NOT covered by the editor/PlayMode tests:

- 📱 **WebRTC media renegotiation after reconnect (HIGH PRIORITY).** The reconnect watchdog and the
  signaling *parse* path are verified, but the full media path after a drop is not. Steps:
  1. Establish a live session with the admin dashboard; confirm two-way video/audio.
  2. Force an abnormal socket close (drop network/server briefly).
  3. Confirm the client auto-reconnects, re-emits `join-room`, and the admin re-sends an **`offer`**.
  4. Confirm `HandleRemoteOffer` produces a new **`answer`** + ICE and **video/audio resume** (not a black/frozen feed).
  - Watch for: stale `RTCPeerConnection` not torn down, `_remoteSocketId` mismatch after rejoin,
    duplicate tracks, or `_videoTrack`/`_localStream` left non-null blocking re-preview.
- 📱 **WebRTC offer→answer end-to-end** (smoke test only asserts the offer is parsed + socket id captured;
  real SDP/ICE negotiation needs a live peer).
- 📱 **Composite passthrough capture** streamed to the admin (real-world + virtual), with rendered-camera fallback.
- 📱 **TURN traversal** behind a corporate firewall (currently STUN-only unless a TURN server is configured).
- 📱 **LMS reporting** (xAPI/SCORM) end-to-end against a real LMS endpoint.
- 📱 **Scene-permission denial** path on-device (`USE_SCENE` denied → re-grant flow).
