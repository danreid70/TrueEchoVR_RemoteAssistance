# TrueEchoVR — Headset (Quest 3 / Android) Pipeline: Evaluation, Diagnostics & Fix Plan

> **Purpose:** Resumable worklog for diagnosing & fixing the Quest 3 sign-in → handshake → UI flow →
> RoomAnchor → item-QR pipeline, the Replit API registration / video / QR-list issues, and the black
> passthrough background in the session preview.
>
> **How to resume after a crash / lost connection:** Read `## CURRENT STATE` below, find the last
> `[x]` waypoint, then continue at the first `[ ]`. Each waypoint has a **Back-track** note describing
> how to undo it if it made things worse.

---

## CURRENT STATE
- **Last completed waypoint:** WP-8 (Phase 1 & 2 code fixes done, all compile clean)
- **Next waypoint:** WP-9 (BLOCKED — needs on-device socket trace + dashboard/Replit event contract)
- **Done this session:** WP-1 token capture, WP-2 HEADSET_CAMERA runtime perm, WP-3 stop per-frame UI
  rebuilds, WP-4 debounced disk saves, WP-5 shared materials + TMP autosize off, WP-6 no double boot,
  WP-7 sign-in routes through UIManager.SetState, WP-8 valid-payload pool from StartupData.
- **Branch/backup advice:** commit now — this is a clean revertable checkpoint before the signaling work.

---

## LEGEND
`[ ]` pending  `[~]` in progress  `[x]` done  `[!]` blocked (needs device/backend)

---

## KEY FINDINGS (do not lose these — they are the basis for the fixes)

### A. API registration is failing — MISSING BEARER TOKEN
- Backend root (`https://live-troubleshooting-app.replit.app/`) confirms `/api/setup/{code}` returns
  `{ customerId, locationId, token }`, and **steps 3 (register) & 4 (startup-data) REQUIRE
  `Authorization: Bearer <token>`**.
- `SignalingManager.SetupResolveResponse` (Networking/SignalingManager.cs ~L338) has **no `token` field**
  and `ResolveSetup` never calls `SetAuthToken`. With the recommended bare-code QR there is no token in
  the QR either, so register/startup-data go out **unauthenticated → 401/403** on a protected backend.
- Verified: host is reachable; `GET /api/setup/TESTCODE` → HTTP 404 (the spec'd "invalid code" response),
  so the endpoint contract is live.

### B. No video / no QR-list exchange — SOCKET.IO + WEBRTC CONTRACT GAPS
- `SignalingManager.HandleRemoteOffer` only **answers** an incoming `offer`. On `peer-joined` the headset
  just stores `_remoteSocketId` and **never creates/sends an offer**. If the dashboard expects the
  headset (the camera owner) to be the WebRTC *offerer*, negotiation deadlocks → no video.
- There is **no socket handler** for a "request QR list" event and the headset never emits its QR list on
  join. QR push/pull is REST-only and button-driven (`locations/{id}/qr-codes`). Matches the report:
  "doesn't get a request for the QR Code item list, nor send it."
- No inbound `answer` / `ice-candidate` handlers exist (only outbound). 
- `OnStartupDataReceived` does **not** call `qrManager.SetValidPayloads(...)`, so the valid pool stays empty.
- ⚠️ These need the **dashboard/Replit socket event contract** (offerer role + QR-list event names) to fix
  correctly. Capture on-device socket logs first (`verboseSocketLogging` is already true).

### C. Scene gets slow/jittery when item QR codes appear — MAIN-THREAD HOTSPOTS
1. **Per-frame UI rebuilds on tracking noise.** `QrCodeManager.Update()` fires `OnQRCodeUpdated` whenever a
   code moves > `positionThreshold (0.01m)` / `rotationThreshold (0.2°)` — i.e. constantly from tracking
   jitter. Subscribers do heavy work **every frame, per code**:
   - `SessionUiController`: `OnQRCodeUpdated += AppendChatMessage(...)` → re-assigns the entire chat string
     **and calls `Canvas.ForceUpdateCanvases()`**.
   - `SessionFlowManager`: `OnQRCodeUpdatedNormal → RefreshQRCodeDropdown()` → clears & rebuilds the whole
     dropdown.
   → With several visible codes this rebuilds the dropdown + chat + forces a canvas layout **every frame**.
   **This is the primary perf killer.**
2. **Synchronous disk writes.** `SaveToDisk()` (pretty-printed `JsonUtility` + `File.WriteAllText`) runs on
   the main thread on **every** QR add/update → O(n²) writes as codes appear.
3. **Primitive/material explosion.** Each tracked code builds a cube bg + 4 border cubes (+ optional TMP),
   each via `GameObject.CreatePrimitive` with a **`new Material(Shader.Find(...))` per bar** (no batching,
   leaks materials). Detection markers add 4 more cubes per code. TMP uses `enableAutoSizing` (expensive).

### D. Passthrough background is black in the preview — RUNTIME HEADSET_CAMERA PERMISSION NOT REQUESTED
- The composite stream (`SetupCompositeCamera`) draws the live passthrough via **`WebCamTexture`** (Meta
  Passthrough Camera Access). If the webcam never delivers frames, the bg quad is `SetActive(false)` →
  **black background, VR content still renders** (exactly the reported symptom).
- Manifest declares **both** `horizonos.permission.HEADSET_CAMERA` and `android.permission.CAMERA`
  (Assets/Plugins/Android/AndroidManifest.xml L27–29), **but** the runtime request flow only requests
  `android.permission.CAMERA`:
  - `PermissionsBootstrapper.RequestAll()` requests CAMERA + USE_SCENE, **not HEADSET_CAMERA**.
  - `SignalingManager.SetupPassthroughCamera` waits only on `android.permission.CAMERA`.
  → `HEADSET_CAMERA` is a runtime ("dangerous") permission; without it PCA yields no frames.

### E. Flow continuity bugs
- **startup-data fetched twice / QR codes added twice:** `RegisterAndBoot → ProvisioningSequence` already
  runs `EveryBootSequence` (fires `OnStartupDataReceived`); then `SessionFlowManager.CompleteInitializationAfterAnchor`
  calls `EveryBootSequence` **again** → second `OnStartupDataReceived` → duplicate `UpdateQRCodeFromRemote`.
- **Session panel shown before calibration:** `OnSignInPressed → ShowJoinScreen() == ShowSessionScreen()`
  jumps straight to the session panel (Full-mode detection + `StartLocalPreview`) immediately after sign-in,
  bypassing `SessionFlowManager`'s "wait for RoomAnchor" gating (which also calls `SetState(Session)`).
  Two competing "show session" paths → muddled ordering.

---

## TASK / WAYPOINT PLAN

### Phase 0 — Evaluation
- [x] **WP-0** Read pipeline code, test backend reachability, confirm passthrough docs. Findings recorded above.
  - Back-track: n/a (read-only).

### Phase 1 — High-confidence, device-independent fixes
- [x] **WP-1 (API token):** Added `token` to `SetupResolveResponse`; `ResolveSetup` now calls
  `SetAuthToken(token)` before register/startup-data. (SignalingManager.cs) ✅ compiles.
  - Back-track: remove the `token` field + `SetAuthToken` call.
- [x] **WP-2 (Passthrough permission):** `PermissionsBootstrapper` now requests
  `horizonos.permission.HEADSET_CAMERA` (default on); `SetupPassthroughCamera` waits on it too. ✅ compiles.
  - Back-track: revert PermissionsBootstrapper + SetupPassthroughCamera changes.
- [x] **WP-3 (Perf — stop per-frame UI rebuilds):** `OnQRCodeUpdatedNormal` no longer rebuilds the
  dropdown; removed the per-update chat append (`Canvas.ForceUpdateCanvases`). Refresh stays on add/remove. ✅
  - Back-track: restore original event subscriptions.
- [x] **WP-4 (Perf — disk I/O):** `SaveToDisk()` now debounced via `RequestSave()`/`FlushPendingSave()`
  (default 2s) in QrCodeManager. ✅ compiles.
  - Back-track: restore `if (autoSaveLoad) SaveToDisk();` call sites.
- [x] **WP-5 (Perf — visuals):** Shared cached materials per (color, opaque/transparent) for code
  visuals + border bars; cached shader lookup; TMP auto-sizing off. ✅ compiles.
  - Back-track: restore per-bar material creation.

### Phase 2 — Flow continuity fixes
- [x] **WP-6 (No double boot):** Added `SignalingManager.StartupDataLoaded`;
  `CompleteInitializationAfterAnchor` skips the redundant second startup-data fetch when provisioning
  already loaded it (returning-user path still fetches). ✅ compiles.
  - Back-track: restore second `EveryBootSequence` call.
- [x] **WP-7 (Consistent show-session path):** Re-scoped — user's intended flow IS "show Session → then
  wait for RoomAnchor", so we keep that ordering but route sign-in success through
  `UIManager.SetState(Session)` so UIManager state matches the visible panel (was stuck on Login). ✅
  - Back-track: restore `ShowJoinScreen()` call in OnSignInPressed.
- [x] **WP-8 (Valid payloads):** `OnStartupDataReceived` now feeds qr values to
  `qrManager.AddValidPayloads(...)` (+ null guards). ✅ compiles.
  - Back-track: remove the AddValidPayloads call.

### Phase 3 — Signaling / WebRTC / QR-list (needs backend contract + device logs)  [!]
- [!] **WP-9 (Capture socket trace):** On device, sign in + join with `verboseSocketLogging`; capture
  `<=`/`=>` packets to learn offerer + QR-list event names.
- [!] **WP-10 (WebRTC offerer):** If headset must offer, create-offer on `peer-joined`; add inbound
  `answer` + `ice-candidate` handlers.
- [!] **WP-11 (QR-list over socket):** Implement the dashboard's QR-list request/response event(s).

### Phase 4 — Verification
- [ ] **WP-12 (Compile):** Zero compile errors after each phase.
- [ ] **WP-13 (Editor smoke test):** Play-mode bootstrap test of the state machine where feasible.
- [ ] **WP-14 (Device validation):** APK on Quest 3 — full path incl. 50+ codes FPS, passthrough behind VR,
  dashboard receives video + QR list.

---

## NOTES / OPEN QUESTIONS FOR THE USER
1. Confirm the **WebRTC offerer** (headset vs dashboard) and the exact **Socket.IO event names** for the
   QR-list request/response — needed for Phase 3.
2. Confirm the production backend host (default in `BackendConfig` is
   `https://live-troubleshooting-app.replit.app`).
