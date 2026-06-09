# TrueEchoVR — Passthrough Video + QR Merge/Anchor Sync: Evaluation & Resumable Plan

> **Purpose:** Resumable worklog for two work-streams requested after on-device Quest 3 testing:
> 1. **Video** — fix the black passthrough background in the preview/stream and make WebRTC stream correctly to the Replit backend.
> 2. **QR** — merge the server-downloaded "legit" list with headset-detected QR codes (3 statuses), stop treating the Sign-In QR and RoomAnchor as normal items, and keep world-space (detection/Meta storage) pose separate from RoomAnchor-relative (server sync) pose while keeping markers synced and the scene free of stray markers.
>
> **Resume after a crash:** read `## CURRENT STATE`, find the last `[x]`, continue at the first `[ ]`.

---

## CURRENT STATE
- **ALL CODE PHASES COMPLETE (V + Q).** Compiles clean; editor smoke test = 12/12 pass.
  Files changed: `SignalingManager.cs` (video/WebRTC), `QrCodeManager.cs` (QR core),
  `SessionUiController.cs` (dropdown/merge/names), `SessionFlowManager.cs` (reset→SignIn phase).
- **REMAINING = device-only validation (cannot be done in Editor):**
  1. Passthrough background appears in the preview/stream (needs HEADSET_CAMERA granted + PCA frames).
  2. WebRTC media actually flows to the Replit expert (now that incoming ICE candidates are applied).
  3. Listed/detected/merge statuses + RoomAnchor-relative push/pull verified live against the dashboard.
  On-device logs to watch: `[Signaling] WebCamTexture device[..]`, `Passthrough camera frames live`,
  `Passthrough background now live in the stream`, or the failure reason strings.

## DECISIONS RESOLVED (agent-chosen; user delegated "make decisions… just make it WORK with replit")
- **DR1 — Listed-but-not-detected markers:** LIST ENTRY ONLY. No persistent in-room marker for server-listed codes that the headset has not physically seen. They show in the dropdown/list as "listed — not visible". Admin `point-to` may still show a temporary COORDINATE highlight using the stored relative pose. → eliminates stray markers.
- **DR2 — Pose reconciliation when listed AND detected:** DETECTION WINS, MANUAL PUSH. The live detected WORLD pose drives the marker; the server is only updated on "Push QR" (preserves existing Push/Pull intent).
- **DR3 — RoomAnchor strategy:** KEEP RoomAnchor as the shared reference; all item poses synced RELATIVE to it (required by the Replit contract; lets other headsets/dashboard stay in sync). The existing OVRSpatialAnchor hybrid (drift-free relocalization) stays.

---

## LEGEND
`[ ]` pending  `[~]` in progress  `[x]` done  `[!]` blocked (needs device/decision)

---

## EVALUATION — VIDEO (root causes)
1. **Passthrough underlay is invisible to Unity.** `OVRPassthroughLayer` (overlayType=Underlay) is composited by the Quest OS AFTER Unity rendering — it cannot be read by any Unity Camera/RenderTexture. So passthrough can NEVER reach the WebRTC/preview texture via the layer. The ONLY real-world source in code is the Passthrough Camera Access (PCA) **`WebCamTexture`** drawn on a background quad inside `_compositeRT` (`SignalingManager.SetupCompositeCamera`/`SetupPassthroughCamera`).
2. **Black background symptom = WebCamTexture delivers no frames.** When PCA frames never arrive (`_webcamTexture.width <= 16`), `SetupPassthroughCamera(createTrack:false)` fails, the BG quad is `SetActive(false)`, and the composite camera clears to **opaque `Color.black`** → black background + VR objects on top. Most likely cause: `horizonos.permission.HEADSET_CAMERA` not granted at runtime, or the wrong/zero camera device enumerated.
3. **WebRTC media bug:** the headset emits its OWN ICE candidates (`_pc.OnIceCandidate` → `ice-candidate`) but `ProcessIncomingMessage` has **NO incoming `ice-candidate` handler** → remote candidates are never added via `_pc.AddIceCandidate`. With STUN-only (no TURN) this can stop media even after offer/answer succeed. Headset is answer-only (correct per contract).
4. **Config:** `videoSource = Composite` (correct target), `captureCamera` unassigned → falls back to `Camera.main` = CenterEyeAnchor (OK). `captureResolution` 1280x720, `_compositeRT` is B8G8R8A8_SRGB (matches contract §4).

## EVALUATION — QR (structural gaps)
1. **No world/local pose separation in stored records.** A single `position/rotation` per record whose meaning flips on `_isAnchorSet` (world if no anchor, RoomAnchor-local if anchored). Unity transform parenting already exposes both (`.position` world / `.localPosition` local) at runtime, but the serialized/model record keeps only one.
2. **RoomAnchor & Sign-In code are not first-class.** Identified by scattered ad-hoc string checks (`Contains("RoomAnchor")`, `IsBareSetupCode`, `recognizedSetupCode`) in QrCodeManager + SessionUiController. RoomAnchor is even put in `_trackedQRCodes` like an item (just filtered at display).
3. **Three conflicting status concepts:** `QrMarkerCategory` (4-cat Target/ValidListed/Unlisted/Invalid), `QRStatus {Official,Unknown}` (instance field), and the UI dropdown's own green/red/orange logic. They can disagree.
4. **Merge is one-directional & implicit.** Server list seeds `_validPayloads` AND spawns `Official` instances via `UpdateQRCodeFromRemote`; there is no single merged model exposing the 3 statuses.
5. **Stray markers:** a server-listed-but-undetected entry instantiated by `UpdateQRCodeFromRemote` has NO live trackable, so `OnTrackableRemoved` never cleans it up.

### Backend contract constraints (must preserve — `WebAppManager_Communication_Doc.md`)
- StartupData `qrCodes[]` = authoritative legit list; poses RELATIVE to RoomAnchor.
- CalibrationUpload (`POST/GET /api/locations/{id}/qr-codes`): poses RELATIVE to RoomAnchor (RoomAnchor entry itself = world). `JsonUtility` shapes for Vector3/Quaternion are fixed.
- `point-to`: match by `qrCode` payload first, then `name`; coordinate fallback uses RoomAnchor-relative pose; empty payload clears.
- WebRTC: headset is answer-only; emits `answer` + `ice-candidate`; receives `offer`, `peer-joined`, `chat-message`, `point-to`. (Adding an incoming `ice-candidate` handler is a headset-side fix, contract already lists the event.)

---

## TASK / WAYPOINT PLAN

### Phase V — Video (SignalingManager.cs only)
- [ ] **V1** Add incoming `ice-candidate` handler: parse `42["ice-candidate", {candidate,sdpMid,sdpMLineIndex,fromSocketId?}]` and call `_pc.AddIceCandidate(...)`. Queue candidates that arrive before `_pc`/remote-desc is ready, flush after `SetRemoteDescription`. Keep emitting our own candidates.
- [ ] **V2** Harden passthrough capture: verify `HEADSET_CAMERA` (+ `CAMERA`) permission at runtime with clear logs; robust device selection (prefer a passthrough/world device, not blind first device); longer/clear timeout; loud diagnostics so on-device logs reveal exactly why frames are missing.
- [ ] **V3** Composite robustness: when WebCamTexture has frames, ensure the BG quad is enabled and `_BaseMap`/mainTexture set; consider non-opaque/clear when no frames so the failure is obvious; ensure `_compositeRT` actually receives the quad (layer/cull/depth correct). Do NOT change the contract format.
- [ ] **V4** Compile clean; editor smoke (no device): confirm no exceptions when PCA/permissions absent (graceful fallback + logs). Mark device-validation items `[!]`.

### Phase Q — QR redesign (QrCodeManager.cs + SessionUiController.cs + SessionFlowManager.cs)
- [ ] **Q1** Central system-code predicates: `IsRoomAnchorPayload`, `IsSignInCode`, `IsSystemCode`. Replace scattered string checks. System codes are NEVER items.
- [ ] **Q2** Sign-In code lifecycle: SignIn/LoginOnly → detect + green pip + drive login (no item). Session/Full → suppress entirely (remove any pip/marker, never instantiate as item). Signed out → detectable again. Driven by detection State + `recognizedSetupCode`.
- [ ] **Q3** RoomAnchor as pure reference: keep its anchor GameObject for parenting + spatial anchor, but exclude from item list, dropdown, pointing, and item-merge. Preserve current server upload of RoomAnchor world pose for contract compatibility.
- [ ] **Q4** Unified merged item model with status `DetectedUnlisted | DetectedListed | ListedNotDetected` (excludes system codes). Single source of truth combining `_validPayloads` (server legit list) + live detections. Expose for UI.
- [ ] **Q5** World/local pose separation: store BOTH world pose (detection truth / Meta storage) and RoomAnchor-relative local pose (server sync) per item. On detection, WORLD pose is truth → move/parent marker so localPosition recomputes; update record. Server sync uses local; persistence stores both.
- [ ] **Q6** No stray markers: per DR1, do NOT instantiate persistent markers for ListedNotDetected. Ensure every persistent marker maps to a live trackable; clean up on removal/clear; reconcile when a listed code becomes detected (replace ghost-less list entry with a real marker at detected world pose).
- [ ] **Q7** UI (SessionUiController dropdown/list) reflects the unified 3-status model from Q4 (one consistent color/status scheme). Remove the divergent ad-hoc classification. `point-to` resolution unchanged except it ignores system codes.
- [ ] **Q8** Compile clean; editor smoke via `QrCodeManager.SimulateQRDetectionEditor` / existing `TrueEchoVR/Debug/*` to exercise merge + statuses without a device.

### Phase Z — Verification
- [ ] **Z1** Full compile clean (Console error-free).
- [ ] **Z2** Editor smoke tests (QR simulation + signaling contract smoke test) pass.
- [ ] **Z3** Device checklist handed to user for the device-only items (passthrough frames, WebRTC media, relocalization).

---

## NOTES
- Keep the Replit coordinate contract byte-for-byte (relative-to-RoomAnchor). Meta anchors/world pose improve the LOCAL layer beneath it.
- Run work-streams sequentially (V then Q) to avoid the documented parallel-reimport revert gotcha.

---

# ROUND 2 (post on-device test) — Status: IN PROGRESS

On-device results: local preview now works (permissions prompted OK). Remaining issues:

## R2 EVALUATION
- **R1 Permission default-check:** Android/Horizon runtime permission dialogs CANNOT be programmatically pre-checked (OS security). Best we can do: request CAMERA + HEADSET_CAMERA prominently/individually and ensure they are declared so they appear in app permission settings. `unityplayer.SkipPermissionsDialog=false` keeps Unity's auto-request. → explain + make request prominent.
- **R2 Composite alignment:** composite camera does `CopyFrom(eye)` → VR renders at the EYE FOV/aspect, but the passthrough WebCamTexture has the PHYSICAL camera's FOV/aspect. Drawing the webcam stretched to the eye frustum makes real world and VR scale differently → angles diverge as the head turns. Fix: drive the composite camera projection from the passthrough camera FOV + webcam aspect (Quest 3 specs, tunable), size the quad to THIS frustum. Do NOT touch the working capture path otherwise.
- **R3 Not streaming to website:** answerer flow order is wrong. Tracks are added BEFORE SetRemoteDescription; correct WebRTC answerer order is SetRemoteDescription(offer) → AddTrack (reuses negotiated transceivers) → CreateAnswer. Reorder so the answer advertises the sending video m-line correctly.
- **R4 QR detection unreliable + no update on move:** MRUKTrackable has IsTracked but NO TrackableUpdated event; TrackableAdded fires once. Our pending-payload entries are DROPPED after 5s (lost forever if decoded late); missed adds never recovered; nothing re-reads a moved code. Fix = self-healing periodic reconciliation sweep over all live QRCode MRUKTrackables: pick up missed/late ones, (re)link instance.trackable by payload, follow IsTracked poses each frame, prune dead. Fix rotation-threshold compare (stores flipped vs raw). Keep push/pull intact.

## R2 TASKS — ALL CODE COMPLETE (compiles clean; editor smoke 4/4 + 12/12 pass)
- [x] **R2-V1** WebRTC answerer reorder: `SetRemoteDescription(offer)` → `AddTrack` → `CreateAnswer` (+ CreateAnswer error check + answer log). `SignalingManager.HandleRemoteOffer`.
- [x] **R2-V2** Composite projection now uses the passthrough camera FOV (tunable `passthroughHorizontalFovDeg`, default 82°) at the RT aspect, via `ApplyCompositeProjection`; quad sized to the COMPOSITE frustum (`UpdateCompositeQuadTransform`). Capture path otherwise untouched. `SignalingManager`.
- [x] **R2-Q1** Self-healing `ReconcileTrackables` sweep (Full mode, every `qrReconcileInterval`=0.5s): re-arms empty payloads (never permanently dropped — also bumped `PendingPayloadTimeoutSeconds` 5→30s), re-links moved/new trackables, processes missed/late codes. Follow loop now compares RAW rotation (`lastTrackableRotation`) and only follows while `IsTracked`. `QrCodeManager`.
- [x] **R2-P1** Camera perms requested FIRST (before scene/mic). Documented that OS dialogs cannot be pre-checked. `PermissionsBootstrapper`.
- [x] **R2-Z** Compile clean (0 errors); editor smoke passed; docs updated (this file + `WebAppManager_Communication_Doc.md`).

### R2 DEVICE CHECKLIST (cannot be verified in Editor)
1. **Camera permission prompt appears first** at launch; granting it = local preview shows passthrough (already working).
2. **Overlay alignment:** VR markers/UI now track the real room as the head turns. If they still drift, tune `SignalingManager.passthroughHorizontalFovDeg` on the component (try 78–90) until aligned.
3. **Web app receives video:** watch logcat for `Sending answer (video track present)`. If the web app still shows nothing, capture whether ICE connects (it should now apply remote candidates) — may indicate a TURN requirement or a web-side m-line/codec issue.
4. **QR detection consistency:** codes should now detect reliably (sweep recovers misses), update when physically moved, and not vanish. Push/Pull round-trips the merged set (relative-to-RoomAnchor).
