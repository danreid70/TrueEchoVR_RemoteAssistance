> MOVED: the canonical, maintained plan lives at `Assets/Plans/EVALUATION_AND_FIX_PLAN.md`.
> This root copy is stale — do not edit.

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
- **Last completed waypoint:** WP-0 (evaluation complete, plan written)
- **Next waypoint:** WP-1
- **Branch/backup advice:** commit current state before WP-1 so every waypoint is revertable via git.

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
- [x] **WP-0** Read pipeline code (SignalingManager, QrCodeManager, SessionFlowManager, SessionUiController,
  PermissionsBootstrapper, BackendConfig, AndroidManifest), test backend reachability, confirm passthrough
  docs. Findings recorded above.
  - Back-track: n/a (read-only).

### Phase 1 — High-confidence, device-independent fixes (safe to do now)
- [ ] **WP-1 (API token):** Add `token` to `SetupResolveResponse`; in `ResolveSetup`, when present call
  `SetAuthToken(token)` **before** register/startup-data. Verify compile.
  - Back-track: remove the `token` field + `SetAuthToken` call.
- [ ] **WP-2 (Passthrough permission):** Request `horizonos.permission.HEADSET_CAMERA` at runtime
  (add to `PermissionsBootstrapper`, default on) and have `SetupPassthroughCamera` wait on it too.
  Verify compile.
  - Back-track: revert PermissionsBootstrapper + SetupPassthroughCamera changes.
- [ ] **WP-3 (Perf — stop per-frame UI rebuilds):** Throttle/​coalesce `OnQRCodeUpdated` handling:
  do NOT `AppendChatMessage` or `RefreshQRCodeDropdown` on every position tick. Options: only refresh on
  add/remove, debounce dropdown refresh (e.g. dirty-flag + max 2–4 Hz), and drop the per-update chat spam.
  Consider raising `positionThreshold`/`rotationThreshold`. Verify compile.
  - Back-track: restore original event subscriptions.
- [ ] **WP-4 (Perf — disk I/O):** Debounce `SaveToDisk()` (dirty flag + coalesced write, e.g. once/2s or
  on phase change) instead of per add/update. Verify compile.
  - Back-track: restore `if (autoSaveLoad) SaveToDisk();` call sites.
- [ ] **WP-5 (Perf — visuals, optional):** Share one material per category for QR item visuals/border bars
  (cache like `_markerMaterials`); avoid `new Material(Shader.Find())` per bar; disable TMP auto-sizing.
  Verify compile.
  - Back-track: restore per-bar material creation.

### Phase 2 — Flow continuity fixes
- [ ] **WP-6 (No double boot):** Make `EveryBootSequence` idempotent OR stop calling it twice — don't
  re-fetch startup-data in `CompleteInitializationAfterAnchor` if provisioning already loaded it.
  - Back-track: restore second `EveryBootSequence` call.
- [ ] **WP-7 (Single show-session path):** Route post-sign-in through one owner. After sign-in go to a
  "calibration/waiting for RoomAnchor" UI; only enter the full Session panel via
  `SessionFlowManager` after the RoomAnchor is found (align `ShowJoinScreen` with the gated flow).
  - Back-track: restore `ShowJoinScreen() => ShowSessionScreen()`.
- [ ] **WP-8 (Valid payloads):** In `OnStartupDataReceived`, call `qrManager.SetValidPayloads(qrValues)`
  so item codes classify as ValidListed (blue).
  - Back-track: remove the SetValidPayloads call.

### Phase 3 — Signaling / WebRTC / QR-list (needs backend contract + device logs)  [!]
- [!] **WP-9 (Capture socket trace):** On device, sign in + join a room with `verboseSocketLogging` on.
  Capture the `<=`/`=>` packet log to determine: does an `offer` arrive? which side offers? what event
  does the dashboard use to request/receive the QR list?
- [!] **WP-10 (WebRTC offerer):** If the headset must offer, implement create-offer on `peer-joined`
  (build PC, add local tracks, `CreateOffer`, send `offer`, handle incoming `answer` + `ice-candidate`).
  Currently there is **no `answer`/`ice-candidate` inbound handler** — add them.
- [!] **WP-11 (QR-list over socket):** Implement the dashboard's QR-list request/response event(s)
  (e.g. respond with `GetQRCodeDataAsJson`, and/or emit the list on join), per the confirmed contract.

### Phase 4 — Verification
- [ ] **WP-12 (Compile):** Confirm zero compile errors after each phase (console check).
- [ ] **WP-13 (Editor smoke test):** Play-mode bootstrap test of the state machine where feasible
  (no device camera in editor — passthrough/WebRTC validated on device).
- [ ] **WP-14 (Device validation):** APK on Quest 3 — verify: bare-code scan → resolve(token) → register
  (200) → startup-data (200) → Session UI → RoomAnchor → item QRs at 50+ codes hold 50+ FPS → preview
  shows passthrough behind VR → dashboard receives video + QR list.

---

## NOTES / OPEN QUESTIONS FOR THE USER
1. Confirm the **WebRTC offerer** (headset vs dashboard) and the exact **Socket.IO event names** for the
   QR-list request/response — needed for Phase 3.
2. Confirm the production backend host (default in `BackendConfig` is
   `https://live-troubleshooting-app.replit.app`).
