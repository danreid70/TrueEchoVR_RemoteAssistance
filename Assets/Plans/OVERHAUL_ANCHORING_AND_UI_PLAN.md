# TrueEchoVR — Spatial Anchoring + UI Overhaul: Evaluation & Task Plan

> **Purpose:** Resumable worklog for (1) overhauling how QR-code spatial data is held/synced — moving the
> *local* spatial layer onto Meta XR Building Blocks / Spatial Anchors while keeping the web-dashboard
> coordinate contract — and (2) cleaning up the UI panels (remove dead/invisible elements, fix text/fonts,
> improve flow).
>
> **Resume after a crash:** read `## CURRENT STATE`, find the last `[x]`, continue at the first `[ ]`.
> Each waypoint has a **Back-track** note.

---

## CURRENT STATE
- **Completed this session:** WP-A0 (eval/plan/dead-code), WP-C1/C2 (deleted dead Meta QR sample folder +
  stale UI prefab), UI rebuild-on-a-copy (TEVR_UI_System_Clean.prefab + UI_Sandbox.unity), Phase D spatial
  hybrid (OVRSpatialAnchor in QrCodeManager.cs). All compiles clean.
- **NEXT (needs user review + on-device):** review UI_Sandbox.unity → approve → migrate the clean prefab onto
  Bootstrap's TEVR_UI_System; on-device validation of spatial-anchor relocalization (see Phase E).
- **DECISIONS RESOLVED:** D1=single-headset for now (keep door open for SSA), D2=HYBRID approved,
  D3=UI rebuilt on a COPY (new prefab + sandbox scene; Bootstrap.unity untouched), D4=delete both dead assets (DONE).

### IMPLEMENTATION NOTES
- UI clean copy: `Assets/_TrueEchoVR/_PREFABS/TEVR_UI_System_Clean.prefab` + review scene
  `Assets/_TrueEchoVR/_SCENES/UI_Sandbox.unity`. Layout Groups throughout; LiberationSans SDF font;
  added previously-invisible labels (detection indicator, latency, consolidated session status) + the
  missing Backend-URL input; fixed RoomCode placeholder; 33 SessionUiController fields wired.
- Spatial hybrid: confirmed API against `Packages/com.meta.xr.sdk.core/Scripts/OVRSpatialAnchor.cs` (v201).
  `useSpatialAnchor` toggle; persists RoomAnchor UUID (PlayerPrefs `tevr_roomAnchorUuid`); relocalizes on
  launch (no re-scan); Editor + failure fallbacks to the original QR-scan path; backend coordinate sync
  byte-for-byte unchanged; `ClearRoomSpatialAnchor()` for re-calibration. SSA isolated for future add.
- ⚠️ Editing gotcha observed: a delayed asset reimport (from the parallel background dev session) reverted
  part of the SessionUiController dead-code removal once; re-applied and verified after sessions ended.

### ⚠️ ANCHOR API DECISION (important correction)
- The active runtime rig is **`OVRCameraRig` (Meta SDK / Oculus.VR)** — confirmed in `PersistentXRRig.cs`
  (it locates/disables OVRCameraRig in the Editor for the XR Simulator).
- Therefore use **`OVRSpatialAnchor` (Meta SDK)** for the hybrid — it integrates with OVRCameraRig + MRUK.
- Do **NOT** use AR Foundation `ARAnchorManager` (the API seen in `Assets/MRTemplateAssets/Scripts/
  SpawnedObjectsManager.cs`): that is leftover Unity MR-template sample code and requires an **XR Origin /
  ARFoundation** setup the project does not run. Switching to it would be a large, risky rig change.
- Implementer MUST read the installed `OVRSpatialAnchor` source (package `com.meta.xr.sdk.core`) to use the
  exact v201 async signatures (`SaveAnchorAsync`, `LoadUnboundAnchorsAsync`, `UnboundAnchor.LocalizeAsync`,
  `BindTo`, `EraseAnchorAsync`) — these are device-only-testable.
- **Environment:** Unity 6000.4.10f1, URP, New Input System, Android/Quest 3,
  `com.meta.xr.sdk.all` 201.0.0, `com.unity.xr.meta-openxr` 2.5.0, MRUK QR tracking, OVRCameraRig + MRUK.

---

## LEGEND
`[ ]` pending  `[~]` in progress  `[x]` done  `[!]` blocked (needs decision/device)

---

## DECISIONS NEEDED (gate the big work)
- **D1 — Colocation:** Will two+ Quests ever share the same physical room/anchors, or is it always ONE
  headset + the web dashboard? → determines if **Shared Spatial Anchors** are designed in now or deferred.
- **D2 — Spatial scope:** Approve the **HYBRID** (add Meta Spatial Anchors for local stability +
  auto-relocalization; KEEP backend coordinate sync for the web peer) vs a fuller replacement?
- **D3 — UI execution mode:** Should the agent directly edit the live `Bootstrap.unity` scene UI
  (scripted scene edits, higher risk) now, or make code-only changes + deliver a precise scene-edit
  checklist for you to apply?
- **D4 — Delete dead sample:** OK to delete `Assets/_TrueEchoVR/QRCodeDetection/` (confirmed-unused Meta
  MRUK sample) and the stale `Assets/_TrueEchoVR/_PREFABS/TEVR_UI_System.prefab`?

---

## EVALUATION — SPATIAL ANCHORING

### How it works today (the custom approach)
- `QrCodeManager`: a "RoomAnchor" QR establishes a zero-point **GameObject**. Item QRs are parented under
  that GameObject and stored as **localPosition/localRotation relative to it**. Backend stores
  `qrValue + position + rotation`; push/pull via REST (`locations/{id}/qr-codes`). StartupData also carries
  a `qrCodes` spatial map.
- **Weaknesses:** the RoomAnchor is NOT a real tracked anchor → drifts, no relocalization, must be re-scanned
  every session; child transforms jitter with the trackable; all coordinate math is hand-rolled.

### Why Meta native is better (and what to actually use)
- **`OVRSpatialAnchor` / "Spatial Anchor Core" + "Spatial Anchor Spawner" Building Blocks:** create a
  drift-free, world-locked anchor at a pose; **persist by UUID locally** (survives app restart) and
  **relocalize automatically** next session — eliminating the mandatory re-scan. This is the primary win.
- **MRUK + Building Blocks (Camera Rig, Passthrough, Anchor):** the project already uses OVRCameraRig + MRUK
  QR tracking; standardizing on the Building Block components reduces custom glue and is more stable.
- **Shared Spatial Anchors (SSA):** cloud-shared anchors by group UUID for **headset↔headset** colocation
  ONLY. ⚠️ The web dashboard cannot consume a Meta SSA UUID — so **SSA does NOT replace the backend
  coordinate sync**. Keep it as a future option gated on D1.

### Recommended target architecture (HYBRID — pending D2)
1. On first RoomAnchor QR detection, create an `OVRSpatialAnchor` at that pose, `SaveAnchorAsync`, and store
   its **UUID** (PlayerPrefs + backend, per location).
2. On launch, load + localize the stored anchor (no re-scan). Fall back to QR re-scan if it can't relocalize.
3. Express item visuals/positions **relative to the persisted anchor** (stable). Continue syncing
   `qrValue + relative pose` to the backend so the **web dashboard contract is unchanged**.
4. (Optional) make physically-present items their own `OVRSpatialAnchor`; remote-pushed items stay
   relative-to-RoomAnchor.
5. (Deferred, only if D1=yes) add SSA group-sharing for multi-headset colocation.

---

## EVALUATION — UI (from scene inventory of Bootstrap.unity)

**Owner:** UI is baked in `Bootstrap.unity` on `TEVR_UI_System` (shares the GameObject with `UIManager`,
`VrHudController`, `SessionUiController`). The prefab `_PREFABS/TEVR_UI_System.prefab` is **stale/disconnected**
(diverged fonts) — edit the SCENE, not the prefab.

**Panels:** `LoginPanel` (on at start) and `SessionPanel` (off at start) under `SessionGroup`. **No JoinPanel.**

### Dead / invisible / broken (confirmed)
- **Vestigial "join" trio:** `joinPanel`, `joinButton`, `joinButtonText`, `joinStatusText` — no GameObjects,
  unassigned; `ShowJoinScreen()` just calls `ShowSessionScreen()`. (CODE removed in WP-A0.)
- **Dead QR list-item feature:** `qrListContent`, `qrListItemPrefab`, and empty stubs
  `AddQRListItem/UpdateQRListItem/RemoveQRListItem` — replaced by the dropdown. (CODE removed in WP-A0.)
- **Invisible labels (serialized field unassigned in scene → output lost):**
  - `connectionStatusText` ("Status: LIVE/DISCONNECTED") — written, never shown.
  - `latencyText` ("Ping: …ms") — written every frame, never shown.
  - `loginDetectionStatusText` + `sessionDetectionStatusText` (the "● QR Detection: ON/OFF (N seen)"
    indicator) — the whole indicator is invisible.
  - `loginApiUrlInput` — no GameObject exists, so the editable Backend-URL field is missing from Login.
- **Wired but never written:** `sessionStatusText` (StatusMessageText) — always blank on the Session panel.
  → Net bug: the visible session status label shows nothing while the LIVE/DISCONNECTED + latency text have
    nowhere to display. **Consolidate to one wired session-status line + one detection indicator.**
- **Mislabeled:** `RoomCode-Input` placeholder reads **"Enter Location..."** (it's the room-code field).

### Layout / fonts
- **No Layout Groups / ContentSizeFitter anywhere** — everything is manually positioned (the main
  cleanliness/UX debt).
- Fonts depend on **Meta Interaction SDK sample fonts** (`Roboto-*-SDF` under the package Sample folder) +
  LiberationSans fallback → fragile. Move a font into the project and apply consistently.
- Minor redundancy: 3 logo Images on the Session panel; button copy ("Send It").

---

## TASK / WAYPOINT PLAN

### Phase A — Evaluation + safe code cleanup
- [x] **WP-A0** Investigate packages, dual QR system, UI inventory, Meta capabilities; write this plan;
  remove definitively-dead UI code (join trio, list-item stubs + fields) from `SessionUiController`.
  - Back-track: restore removed fields/methods from git.

### Phase B — UI refactor (gated on D3; mostly scene-level)
- [ ] **WP-B1** Consolidate status: ONE wired session-status label (LIVE/DISCONNECTED + errors) + restore
  the QR-detection ON/OFF indicator on both panels; wire or remove latency display.
  - Back-track: revert scene + `SessionUiController` status routing.
- [ ] **WP-B2** Add the missing editable **Backend URL** input to the Login panel (field exists in code).
  - Back-track: remove the field; code already null-guards it.
- [ ] **WP-B3** Fix mislabeled `RoomCode-Input` placeholder → "Enter Room Code…".
  - Back-track: revert placeholder text.
- [ ] **WP-B4** Add Vertical/Horizontal Layout Groups + ContentSizeFitter to Login & Session panels for
  clean, auto-flowing layout; group related controls (QR controls, video previews, chat).
  - Back-track: remove layout components (positions were manual before).
- [ ] **WP-B5** Font robustness: bring a TMP font into the project and apply consistently to all UI texts;
  remove dependence on the Meta SDK sample font.
  - Back-track: re-point text fonts to the sample font.
- [ ] **WP-B6** Trim redundancy: consolidate logos, tidy button copy, remove the always-blank label.
  - Back-track: re-add elements from git.

### Phase C — Cleanup (gated on D4)
- [ ] **WP-C1** Delete dead Meta sample `Assets/_TrueEchoVR/QRCodeDetection/`.
  - Back-track: restore from git.
- [ ] **WP-C2** Delete or re-sync the stale `TEVR_UI_System.prefab`.
  - Back-track: restore from git.

### Phase D — Spatial-anchor overhaul (gated on D1/D2; device-validated)
- [!] **WP-D1** Add `OVRSpatialAnchor` to the RoomAnchor on first detection; `SaveAnchorAsync`; store UUID
  (PlayerPrefs + backend per location).
- [!] **WP-D2** On launch, load + localize stored anchor (skip re-scan); fall back to QR scan if absent.
- [!] **WP-D3** Re-parent item visuals under the persisted anchor; keep relative-pose backend sync intact
  (web contract unchanged).
- [!] **WP-D4** (Optional) per-item spatial anchors for physically present items.
- [!] **WP-D5** (Deferred, only if D1=yes) Shared Spatial Anchors for multi-headset colocation.

### Phase E — Verification
- [ ] **WP-E1** Compile clean after each change.
- [ ] **WP-E2** Editor smoke test where feasible (anchors/passthrough are device-only).
- [ ] **WP-E3** Device validation: relocalization without re-scan, stable items at 50+ codes, web dashboard
  still receives the QR coordinate map, UI reads cleanly and all status/indicators are visible.

---

## NOTES
- Keep the backend coordinate sync — it is the bridge to the **web** dashboard; Meta anchors improve the
  *local* layer beneath it.
- All scene UI edits target `Bootstrap.unity` (the prefab is stale).
