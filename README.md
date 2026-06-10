# TrueEchoVR - RemoteAssistance
## Advanced Mixed Reality Training & Remote Assistance

Welcome to the **TrueEchoVR** project. This application is a professional-grade Extended Reality (XR) solution designed for the Meta Quest 3. It bridges physical operations with digital guidance using **Meta's MR Utility Kit (MRUK)** and **QR Code tracking**.

---

## 🚀 Key Features

- **MR Spatial Awareness**: Full passthrough integration with physical room awareness. **Pure AR Locomotion**: No virtual locomotion (teleport/snap-turn) is enabled; movement is 1:1 with the headset's physical movement in the real world.
- **QR Calibration & Persistence**: Align the virtual coordinate system with the physical world using a dedicated "Room Anchor" QR code. Calibration is persistent and multi-tenant aware.
- **QR Detection Markers & Point-At Focus**: Every detected QR code shows a small colour-coded pip (green/red/blue/orange) that fades away after a few seconds to avoid clutter. Whichever code is currently "pointed at" (by the operator's dropdown or a remote expert's `point-to`) is wrapped in a **pulsing translucent green holographic box** and connected to the HUD direction arrow by a **faint green dashed line**, so the target is unmistakable. A single unified path (`SessionUiController.ApplyPointTarget`) drives the hologram, arrow, dashed line, dropdown selection, and HUD message together. See _QR Detection Markers_ below.
- **Anchor-Relative Hierarchy**: Virtual objects are parented to physical anchors, ensuring they stay locked in place even if the headset re-localizes.
- **Drift-Free RoomAnchor (Meta Spatial Anchor, hybrid)**: The "Room Anchor" zero-point is backed by a persisted **Meta `OVRSpatialAnchor`**, so it is drift-free and **relocalizes automatically on the next launch — no need to re-scan the RoomAnchor QR each session**. Item QR positions are still stored *relative to the RoomAnchor* and synced to the backend exactly as before, so the web-dashboard coordinate contract is unchanged. Single-headset persistence only (no Shared Spatial Anchors yet); falls back to the plain QR-scan path in the Editor and on devices without anchor support. See _Spatial Anchor Persistence_ below.
- **Remote Expert Streaming**: Real-time video and audio via **WebRTC**. An expert can view the operator's perspective and "point" to physical objects. Video defaults to the **Meta Passthrough Camera** (real-world view) and automatically falls back to the Unity-rendered view if the passthrough camera is unavailable.
- **Unified Permission Flow**: A startup `PermissionsBootstrapper` requests every runtime permission the app needs (Scene/spatial data for QR tracking, Camera for passthrough streaming) in a single prompt the first time the app launches.
- **On-Headset Text Entry**: Login fields are pre-populated and fully editable — tapping a field raises the Quest system keyboard so Customer/Location IDs can be typed manually when a setup QR code is not available.
- **Remembered Connection**: Once a device is set up (via setup-QR scan or manual sign-in), the Customer ID and Location ID are persisted and automatically reloaded on every subsequent launch, so the setup QR is **not** required again.
- **Resumable Tasks**: `TaskManager` persists progress to local storage, so if the cloud connection drops mid-session the training resumes from the last incomplete step on restart.
- **Enriched Remote Guidance**: Supports pulsing 3D outlines and billboarding labels that appear at exact real-world coordinates provided by the admin.
- **Intelligent UI System**: A world-space "lazy-follow" HUD and session manager that follow the user comfortably without jitter.
- **Enterprise Health Reporting**: Automatic telemetry (Battery, Calibration State, Location ID) reported to the admin every 60 seconds.
- **LMS Ready**: Built-in support for xAPI and SCORM tracking.
- **Resilient Sign-In → Session Flow**: Sign-in is **non-blocking** — once credentials are valid the session opens immediately and the Room Anchor scan is optional/non-blocking (it only refines item placement). Every REST call has a **15s timeout** so the handshake can never hang. Leaving a session returns cleanly to Sign In with stored credentials (no re-scan needed). _(v2.5)_ The app always starts on the **Login** panel; the **Sign In button is disabled until credentials are actually available** (a persisted/just-scanned setup code or a valid resolve), with the status line guiding the operator ("Please scan a Login Code" vs. "Ready to sign in to: {location}"). The raw **Customer ID / Location ID fields are now hidden** on both the Login and Session panels (they are provisioned by the setup code, not typed).
- **Demo Mode (offline)**: A manual **Demo Mode** button on the Login panel (auto-revealed when a sign-in attempt fails) enters a normal offline session — **real** QR detection still runs and detected codes become pointable in the dropdown; without a downloaded "legit" list they classify as *detected-but-unlisted*.
- **Connection Watchdog**: If the WebSocket drops mid-session the client shows live reconnect status and, when reconnection is abandoned, returns to Sign In and surfaces the Demo Mode fallback (`SignalingManager.OnReconnecting` / `OnReconnectFailed`, plus `Reconnect()`).
- **Credential-Expiry Re-Scan**: A backend `401` (or `403/404` on `startup-data`) clears stored credentials and prompts the operator to **re-scan a Login Code** instead of silently dropping to Demo (`SignalingManager.OnCredentialsExpired`).
- **Editor QA Tooling**: Headset-free debugging via the **`TrueEchoVR/Debug`** menu (simulate setup/RoomAnchor/item QR detections), a **signaling contract smoke test**, and an **auto-wire scene validator** (`TrueEchoVR/Validate Scene Wiring`, also run on scene save). All editor-only (compiled out of device builds).

---

## 🛠 System Architecture

The project follows a modular, manager-centric, and event-driven architecture located in `Assets/_TrueEchoVR/_SCRIPTS/`.

- **`Core/`**: Foundational systems (`BaseInteractionHandler`, `TaskStepData`, `GameEvent`, `BootstrapLoader`, `PermissionsBootstrapper`).
- **`Interactions/`**: Implementation-specific handlers (`GrabInteractionHandler`, `SnapInteractionHandler`, `ButtonInteractionHandler`).
- **`LMS/`**: Educational tracking implementations (`BaseLmsTracker`, `XApiTracker`, `ScormTracker`).
- **`Managers/`**: Global application state (`TaskManager`, `QrCodeManager`, `VrKeyboardManager`).
- **`Networking/`**: Communication layer (`SignalingManager`, `SessionFlowManager`).
- **`UI/`**: Spatial interface controllers (`UIManager`, `VrHudController`, `SessionUiController`, `TargetHighlightController`).

### Persistent Bootstrap Flow
The application initializes in the `Bootstrap` scene (`Assets/_TrueEchoVR/_SCENES/Bootstrap.unity`). 
- **XR Rig**: Uses the **Meta XR Building Blocks** Rig (`[BuildingBlock] Camera Rig`).
- **Persistence**: The `PersistentXRRig` script ensures the Rig, Passthrough, and Hand tracking blocks persist across scenes.
- **No Locomotion**: All virtual locomotion systems are automatically purged from the Rig on initialization to ensure spatial synchronization with physical QR markers.
- **Demo Mode**: If the backend is unreachable, the system can fall back to **Demo Mode** — either automatically when there is no internet/connection during boot, or manually via the **Demo Mode** button on the Login panel (auto-revealed after a failed sign-in). Demo Mode runs a *normal* offline session: real QR detection is active and detected codes are pointable, just colour-coded as *detected-but-unlisted* (no downloaded legit list). **Exception:** when the backend explicitly rejects credentials (`401`/expiry), the app does **not** silently demo — it prompts a Login Code re-scan instead.
- **Non-Blocking Sign-In**: Reaching a live session never waits on a Room Anchor. After valid credentials the session opens immediately (`SessionFlowManager.EnterLiveSession`); Room Anchor discovery is handled separately and only refines item placement. `SessionFlowManager.ResetForNewSession()` returns cleanly to Sign In, preserving stored credentials.
- **Editor Preview**: The `EditorCameraFallback` script ensures the `CenterEyeAnchor` camera remains active in the Unity Editor even when no XR device is detected.
- **Managers**: Core systems (`SignalingManager`, `QrCodeManager`, `TaskManager`, `UIManager`) use `DontDestroyOnLoad`.
- **Initialization**: Managers connect to the signaling server and load local calibration.

---

## 🛠 Troubleshooting

### Package Manager / Access Token Errors
If you see errors in the Package Manager related to "getting access token", this is usually a Unity account session issue.
1. Close Unity.
2. Sign out and back into **Unity Hub**.
3. Restart Unity.
4. Ensure your internet connection is stable.


### Mouse / Hand Interaction (Editor vs Device)
UI input is routed by **`UiEventSystemModeSwitcher`** on the `EventSystem`, which enables exactly one input module based on whether an XR headset is present:
- **In the Editor (no headset)** → `InputSystemUIInputModule` is active. Just press **Play** and use the **mouse** to hover, click, and drag the panels. (Quest Link, if connected, switches to the hand-ray automatically.)
- **On device / Quest Link** → Meta's `PointableCanvasModule` is active, driving the hand/controller ray.

Requirements for world-space clicks (handled automatically by `UIManager` at runtime):
- The `MainCanvas` `worldCamera` is set to `CenterEyeAnchor`.
- `RayInteractable._pointableElement` is linked to the `PointableCanvas` so the hand-ray forwards hover/click events to uGUI.
- The redundant `TrackedDeviceGraphicRaycaster` (XRI) is removed to avoid conflicts.

### Movable UI
The world-space panel can be **grab-dragged** with the ray/mouse (`UiPanelDragHandler`): drag and release to **lock** it in place; a quick tap on the panel background resumes the comfortable **lazy-follow** behaviour. Panel backdrops are rendered at ~50% transparency for passthrough visibility.

### No Cameras Rendering in Editor
The project includes an `EditorCameraFallback` script on the `CenterEyeAnchor`. If the Game View still shows "No cameras rendering":
1. Ensure the `[BuildingBlock] Camera Rig` is active in the scene.
2. Check that `EditorCameraFallback` is present on the `CenterEyeAnchor` child object.

---

## 🔧 How to Modify the Project

### Adjusting UI Comfort
The live UI is the **`TEVR_UI_System`** GameObject in the `Bootstrap` scene (it hosts `UIManager`, `VrHudController`, and `SessionUiController`). On the **UIManager** component you can adjust `Forward Distance` and `Lazy Follow Settings` for the **HUD** and **Session** groups independently.

> ℹ️ The live `TEVR_UI_System` is a plain scene object (not a prefab instance); edit it directly in the `Bootstrap` scene. A more thorough **optional** rebuild of the panels (layout-group-driven, consolidated status labels, project font) lives in **`Assets/_TrueEchoVR/_PREFABS/TEVR_UI_System_Clean.prefab`** and can be previewed in **`Assets/_TrueEchoVR/_SCENES/UI_Sandbox.unity`**. The old stale `TEVR_UI_System.prefab` was removed. Adopting the full rebuild is **not required** (the live QR dropdown is already widened + color-coded); if you do adopt it, swap the clean prefab onto Bootstrap's `TEVR_UI_System` object and re-point `UIManager`'s group/canvas references.

### Adding Training Steps
Locate the `TaskManager` on the manager object. Add a new `TaskStepData` (ScriptableObject) to the `steps` list. The HUD will automatically update with the description and hint.

### Deployment Configuration
The active backend settings live in the **`BackendConfig` ScriptableObject** at `Assets/_TrueEchoVR/_DATA/BackendConfig.asset` (assigned to `SignalingManager`). Edit `apiHost`, `customerId`, and `locationId` there. The script defaults in `Assets/_TrueEchoVR/_SCRIPTS/Networking/BackendConfig.cs` are only used when a new asset is created. Both are currently provisioned with `customerId: cust-004` and the matching `locationId`.

### Video Source
`SignalingManager.videoSource` selects the WebRTC stream source:
- **PassthroughCamera** (default): streams the real-world view via the Meta Passthrough Camera Access (`WebCamTexture`). Requires the Camera permission + developer mode.
- **RenderedCamera**: streams only the Unity-rendered eye view (virtual content). Used automatically as a fallback when the passthrough camera cannot start within `passthroughStartTimeout` seconds.

> ⚠️ **Passthrough Camera startup timing (critical):** Do **not** open the Passthrough Camera (`StartLocalPreview()` / `WebCamTexture`) at app launch. Grabbing the physical headset cameras while the `OVRPassthroughLayer` is still initializing — and before the Camera permission is granted — contends with the system passthrough layer and **blacks out the entire view** (and can hide the OS permission dialog). The local camera capture is therefore started only when a remote session goes **LIVE** (`SessionUiController.OnConnected`), by which point passthrough is rendering and permissions are resolved. During login/calibration the headset shows normal passthrough. If you add new code paths that stream video, gate them the same way.

---

## 🔐 Permissions (Quest)

Declaring a permission in the manifest is **necessary but not sufficient** — the "dangerous"/special permissions below must also be granted at **runtime**, or the related feature silently fails (e.g. QR detection receives zero trackables, the passthrough camera produces no frames).

| Permission | Manifest entry | Used for |
| --- | --- | --- |
| Scene / Spatial Data | `com.oculus.permission.USE_SCENE` | MRUK QR-code & trackable detection (calibration, login-code scan) |
| Camera | `android.permission.CAMERA` + `horizonos.permission.HEADSET_CAMERA` | Meta Passthrough Camera Access (real-world video streaming) |
| Internet / Network | `android.permission.INTERNET`, `ACCESS_NETWORK_STATE` | WebRTC, Socket.IO signaling, REST |

- **Runtime request**: `PermissionsBootstrapper` (in the `Bootstrap` scene) batches the Scene + Camera requests into one OS prompt on first launch. It persists across scenes via `DontDestroyOnLoad`. If the user initially declines, the request can be re-triggered (e.g. the login panel's "Scan Login Code" button also re-requests scene permission via `QrCodeManager`).
- **System keyboard**: `requiresSystemKeyboard` is enabled in `OculusProjectConfig`. This is required for the Quest system keyboard to appear when an input field is tapped (`VrInputFieldActivator` + `SessionUiController.SetupInputFieldKeyboard`).
- **Passthrough Camera Access** is enabled in `OculusProjectConfig` (`isPassthroughCameraAccessEnabled`). Camera access on Quest requires the app to run in **developer mode** until it is approved for the camera permission, and the headset must be on **Horizon OS v74+**.

> ⚠️ **Why QR scanning failed previously:** the scene permission was declared but never requested at runtime, so no prompt appeared and MRUK never received QR trackables. The unified `PermissionsBootstrapper` resolves this.

---

## 🟢 QR Detection Markers & Point-At Focus

A lightweight visualization layer in **`QrCodeManager`** makes QR tracking easy to test on-device. It is designed to stay cheap with **hundreds** of codes in view.

### Colour categories (the single, unified indicator)
Every detected code is classified and coloured by one scheme used everywhere (the small detection pip *and* the heavier post-calibration visual):

| Colour | Category | Meaning |
| --- | --- | --- |
| 🟢 Green | **Target** | What the app is actively looking for — the **RoomAnchor**, or a valid **login setup code** (e.g. `YT5A5XL3`). Bare 8-char alphanumeric codes are always Targets. |
| 🔴 Red | **Invalid** | Empty/whitespace payload, or a JSON-looking payload that fails to parse into a setup code. |
| 🔵 Blue | **ValidListed** | Payload is present in the server-provided **valid QR list**. |
| 🟠 Orange | **Unlisted** | A readable code that is neither a target nor in the valid list. |

### Valid-payload pool
Classification uses an O(1) `HashSet` of known-good payloads so it scales to large rooms. It is populated automatically from the backend:
- `SessionUiController.OnStartupDataReceived` → `QrCodeManager.SetValidPayloads(...)` (server `StartupData.qrCodes[].qrValue`).
- The **Pull** calibration action → `QrCodeManager.AddValidPayloads(...)`.
- When the list arrives after codes are already visible, existing pips recolour automatically (e.g. orange → blue).
- API: `SetValidPayloads`, `AddValidPayloads`, `AddValidPayload`, `ClearValidPayloads`, `IsValidListed`.

### Fade behaviour (avoids scene clutter)
A detection pip appears at full opacity when a code is **detected/loaded**, holds for `markerHoldSeconds`, then fades to invisible over `markerFadeSeconds`. This lets the operator *see* the detect/load event without leaving markers scattered everywhere. A pip **reappears** when:
- the code is re-detected, or
- the code becomes the focused/"pointed-at" selection.

Fading is driven per-renderer via a `MaterialPropertyBlock` (one shared transparent material per colour, no per-marker material allocation). A fully-faded marker disables its own renderer and `Update` until shown again.

### Point-At focus (holographic box + dashed line) — _updated v2.5_
When a code is "pointed at", three visuals are shown together until the selection is cleared:
- A **pulsing translucent green holographic box** (a transparent fill with thick glowing green edge bars) scaled to wrap the target — built by `QrCodeManager.EnsureFocusGlow()` and animated by `ApplyFocusGlowPulse()`. It is **off by default** and only appears for a valid pointed-at target, so it never clutters the scene.
- A **faint green dashed line** from the HUD direction arrow to the target (`VrHudController.UpdateDashLine()` / the `PointAtDashLine` `LineRenderer`). Both ends are trimmed by each object's half-extent so the line starts at the arrow's edge and ends at the target's edge — never buried inside either.
- The **HUD direction arrow** (`VrHudController.pointerArrow`) rotating to face the target, and the code's pip held visible.

**One unified driver.** Everything above — plus the dropdown selection and the on-HUD message — is set by **`SessionUiController.ApplyPointTarget(QRCodeInstance)`**. The on-headset dropdown, a remote **`point-to`** command, and "stop pointing" all route through this one method (the dropdown value is synced with `SetValueWithoutNotify` to avoid event recursion).
- **Triggered by**: selecting a code in the dropdown, or a remote `point-to` (resolved to a local code by `QrCodeManager`, then `FocusQRCode`).
- **Cleared by**: choosing the dropdown's first item (**"Click here to point at an object or stop pointing…"**), a remote `point-to` with no name/qrCode, a position-based highlight, **Clear QR**, or the focused code being removed. All converge on `ApplyPointTarget(null)` → `QrCodeManager.ClearFocus()`. (This fixed a prior bug where "stop pointing" silently no-op'd because the dropdown value was never synced.)
- The hologram follows the live trackable when the code is physically tracked, or its placed visual object otherwise, so it works both during scanning and post-calibration.

### Robust / frequent scanning
- `StartQRCodeDetection()` re-asserts the MRUK tracker config so scanning reliably (re)starts.
- A low-frequency safeguard re-enables QR tracking if the runtime ever drops it (only acts when actually disabled).
- **Payload race fix**: MRUK often raises `TrackableAdded` before the QR string is decoded. Codes with an empty payload are deferred and re-read in `Update()` until the string is available, then processed normally. (This fixed the "it read once, then never again" behaviour — previously a code was processed with an empty payload and never re-read, since `TrackableAdded` does not fire again for an already-tracked code.)

### Tuning & toggles (on the `QrCodeManager` component)
- `showDetectionMarkers` (bool) — master on/off for pips. Also `SetDetectionMarkersVisible(bool)` / `ToggleDetectionMarkers()` at runtime.
- `markerSize` (m), `markerHoldSeconds`, `markerFadeSeconds` — pip size and fade timing.

### QR Code Dropdown (Session panel) — color-coded list

The Session panel's **QR-code dropdown** lists the *union* of the server's "legit" list and the codes seen locally, so the operator can see at a glance what is present, missing, or unexpected. It is rebuilt by `SessionUiController.RefreshQRCodeDropdown()` whenever codes are added/removed or a Pull/StartupData arrives. _(v2.5)_ The **first item is always the prompt "Click here to point at an object or stop pointing…"** — selecting it stops pointing (see _Point-At focus_ above), and entry colours are applied as inline rich-text `<color>` tags so they render reliably across TextMeshPro versions. Each entry's **text colour** means:

| Colour | State | Pointable? | Meaning |
| --- | --- | --- | --- |
| 🟢 Green | **Matched** | Yes | In the server "legit" list **and** discovered locally — all good. |
| 🟠 Orange | **Not visible** | No | In the legit list but **not** currently discovered locally — go scan it. Selecting it posts a hint instead of pointing. |
| 🔴 Red | **Unlisted** | Yes | Discovered locally but **not** in the legit list — unexpected/unknown code. |

> ⚠️ **Two different colour schemes (by design):** the dropdown text uses the **green/orange/red** scheme above (operator-facing "is my list complete?" view). The in-world **3D detection markers** use a *different* 4-colour scheme — green=Target, **blue**=ValidListed, orange=Unlisted, red=Invalid (see table above). The same physical code can therefore read **blue in-world but green in the dropdown**. The unlisted colour is a single constant (`QrUnlistedColor` in `SessionUiController`) — change it to blue if you prefer the dropdown to match the marker scheme.
>
> The legit list (`QrCodeManager.ValidPayloads`, exposed read-only for the UI) is populated from `StartupData.qrCodes[].qrValue` and from **Pull** (`AddValidPayloads`). The **live** `qrCode-Dropdown` in the Bootstrap `TEVR_UI_System` was widened to **680×36** (filling the row to the right of the Location ID field) with single-line **ellipsized** labels and a taller (220px) expanded list, so color-coded names are readable without wrapping. The cleaned-up rebuild (`TEVR_UI_System_Clean.prefab` / `UI_Sandbox.unity`) uses a layout-group-driven full-width variant of the same dropdown.

---

## 🧭 Spatial Anchor Persistence (RoomAnchor — Meta `OVRSpatialAnchor`)

The RoomAnchor zero-point is backed by a persisted Meta spatial anchor so calibration survives across sessions **without re-scanning**. All anchor calls live in the `#region Meta Spatial Anchor` block of `QrCodeManager` and are device-only (skipped in the Editor).

- **Toggle:** `QrCodeManager.useSpatialAnchor` (default **true**). When off / unsupported / in the Editor, the original plain-GameObject QR path is used unchanged.
- **First scan (create + save):** when the RoomAnchor QR is detected, `TryPersistRoomAnchorAsSpatialAnchor()` adds an `OVRSpatialAnchor` to the RoomAnchor visual, waits for localization, `SaveAnchorAsync`, and stores the UUID + payload in PlayerPrefs (`tevr_roomAnchorUuid`, `tevr_roomAnchorPayload`).
- **Next launch (relocalize):** `TryRelocalizeRoomSpatialAnchorOnStart()` runs in `Start()` — `LoadUnboundAnchorsAsync` → `LocalizeAsync` → `BindTo` re-establishes the RoomAnchor at its physical pose and activates dormant item codes. The disk RoomAnchor is *deferred* and only restored if relocalization fails. No QR re-scan required.
- **Live re-sync while the RoomAnchor is in view (NEW v2.5):** once persisted, the anchor normally holds the zero-point drift-free even when the code is out of sight. But while the operator is **actively looking at the RoomAnchor QR**, the anchor visual (and the prefab parented to it) now **snaps to the live QR pose** so it visibly tracks the real code; when the code leaves view the spatial anchor takes over again. Controlled by `QrCodeManager.roomAnchorVisualFollowsLiveQr` (default **true**; set false to keep the spatial anchor fully authoritative). The RoomAnchor also uses **tighter update thresholds** than ordinary items (`roomAnchorPositionThreshold` ≈ 4 mm, `roomAnchorRotationThreshold` ≈ 0.1°) so it tracks responsively as the zero-point for everything else.
- **Coordinate-frame parity:** items are still parented to the RoomAnchor visual and stored as `localPosition`/`localRotation`, so **the backend coordinate sync (REST + StartupData) is byte-for-byte unchanged**. The saved anchor pose already encodes the scan-time visual orientation, so relocalized items land where they were calibrated.
- **Re-calibration:** `ClearRoomSpatialAnchor()` erases the anchor (`EraseAnchorAsync`) and clears the stored UUID; the next RoomAnchor scan creates a fresh anchor. _(Not yet wired to a UI button — see Next Steps.)_
- **Scope:** single-headset persistence only. No Shared Spatial Anchors; the create/load/erase logic is the single boundary where SSA would be added later.

> ⚠️ **Device-only:** spatial anchors cannot be exercised in the Editor/XR Simulator. Validate on Quest 3: (1) first scan logs `RoomAnchor persisted as Meta spatial anchor`; (2) relaunch logs `RoomAnchor relocalized from Meta spatial anchor` and items reappear without re-scanning; (3) the web dashboard still receives identical QR coordinate maps.

---

## 💾 Task Progress Persistence

`TaskManager` saves progress to `Application.persistentDataPath/TaskProgress.json` whenever a step starts or completes.

- On launch it **resumes from the last incomplete step** if the saved progress matches the current step set (a signature of the step ids/count guards against resuming an edited/different task list).
- When all steps finish, the saved state is marked complete so the next launch starts a fresh run.
- Toggle via the `Persist Progress` field on the `TaskManager` component; call `ClearProgress()` to force a clean restart.

This makes the session resilient to a broken cloud connection — incomplete training can be continued after a restart without re-doing finished steps.

---

## 🌐 Connectivity (Replit/AWS)
The system connects to a **Socket.io** signaling server for WebRTC handshakes and uses a **REST API** for spatial persistence.
- **Handshake**: `42["join-room", { ... }]`
- **Persistence**: `GET /api/headsets/{id}/startup-data` (Includes location-specific dictionaries and QR offsets).
- **Health**: `42["health-update", { ... }]` sent every minute.

> **Backend integration docs:** the authoritative wire schemas are in [`BACKEND_CONTRACT.md`](./BACKEND_CONTRACT.md); a narrative, responsibilities-and-checklist guide written for the **Replit backend AI** is in [`REPLIT_AI_INTEGRATION_GUIDE.md`](./REPLIT_AI_INTEGRATION_GUIDE.md). The QA/verification checklist (including deferred on-device tests) is in [`TESTING_CHECKLIST.md`](./TESTING_CHECKLIST.md).

### Calibration Push / Pull (Session panel) — _hardened v2.5_
- **Push** (`POST /api/locations/{id}/qr-codes`) now reports the **true number of codes that will upload** (the count excludes the sign-in code and any items that have no RoomAnchor-relative frame yet) instead of a raw tracked count. Pushing an empty set is **blocked with a clear message** — e.g. "scan the Room Anchor first" when items exist but no anchor does. The on-the-wire shape is unchanged (bulk-first, per-item fallback).
- **Pull** (`GET /api/locations/{id}/qr-codes`) parsing is more tolerant — it now also accepts a **top-level JSON array** in addition to the documented `CalibrationUpload` object, with clearer "nothing returned" messaging.
- **Button state follows context** (`SessionUiController.UpdateSessionButtonsState`): **Pull** enables only when a Location is known; **Push** enables only when a Location is known *and* there is at least one uploadable code. State refreshes on every QR add/remove, anchor discovery, clear, and detection toggle, so the UI never offers an action that would silently no-op.

### Resilience & failure handling
- **REST timeout**: Every `UnityWebRequest` uses a **15-second timeout** (`SignalingManager`), so a stalled server can never hang the sign-in handshake. On timeout/failure the boot sequence resolves and either enters Demo Mode (network/parse failure) or reports failure to the caller.
- **Reconnect watchdog**: On an abnormal socket close the client auto-reconnects up to `maxReconnectAttempts` (firing `OnReconnecting(attempt, max)`); if attempts are exhausted (or auto-reconnect is off) it fires `OnReconnectFailed`. The UI then returns to Sign In and surfaces the **Demo Mode** button. `SignalingManager.Reconnect()` performs a manual retry (resets the attempt counter).
- **Credential expiry**: A `401` on any call (or `403/404` on `startup-data`) is treated as an expired/invalid token: credentials are cleared and `OnCredentialsExpired` fires. The UI prompts a **Login Code re-scan** rather than silently entering Demo Mode, so operators know the device must be re-provisioned.

---

## 🧪 Developer / QA Tooling (Editor-only)

These tools live under `Assets/Editor/` and are **compiled out of device builds**. They let you exercise the full flow without a headset or a live backend.

- **Simulated QR detection** — `TrueEchoVR/Debug/` menu (must be in **Play Mode**):
  - *Simulate Login Setup Code (TEVRDEMO)* — drives the SignIn/login-scan path.
  - *Simulate RoomAnchor QR* — establishes calibration so item codes place relative to it.
  - *Simulate Item QR (DEMO-PUMP-01 / DEMO-VALVE-02)* and *Simulate Full Demo Room* — populate the "Look At" dropdown so pointing/arrow/highlight can be tested.
  - Backed by `QrCodeManager.SimulateQRDetectionEditor(payload, pos?, rot?)`, which raises the same `OnRawQRDetected` / add / RoomAnchor events as a real `MRUKTrackable`.
- **Signaling contract smoke test** — `TrueEchoVR/Debug/Run Signaling Contract Smoke Test` (Play Mode). Feeds each server→client Socket.IO shape from `BACKEND_CONTRACT.md` through the live parser (`chat-message`, `point-to` with coords, `point-to` clear sentinel, `peer-joined`, `offer`) and asserts correct dispatch — no backend required. Full WebRTC media negotiation still needs a live admin peer + device. Uses editor-only hooks `Debug_FeedSocketEvent`, `Debug_OnSocketEmit`, `Debug_RemoteSocketId`.
- **Auto-wire scene validator** — `TrueEchoVR/Validate Scene Wiring` (and automatically on **scene save**). Warns when a critical inspector reference is unassigned (e.g. `SessionUiController.demoModeButton`, `VrHudController.pointerArrow`). Fields the managers auto-resolve at runtime are intentionally not flagged.

---

## 🚀 Build & Deployment
1. **Target**: Android (Meta Quest 3 / 3S), Horizon OS **v74+**.
2. **Graphics**: Vulkan preferred.
3. **Input**: Meta XR Hand Tracking (forced in build).
4. **Scenes**: `Bootstrap` must be at Index 0.
5. **Developer Mode**: Keep the headset/app in **developer mode**. Passthrough Camera Access (used for real-world video streaming) requires it until the app is granted the camera permission for general distribution.
6. **Permissions**: On first launch the user is prompted for **Scene/Spatial Data** and **Camera**. Both must be granted for QR calibration and passthrough streaming respectively. If denied, re-grant via _Settings ▸ Apps ▸ TrueEchoVR ▸ Permissions_ or re-trigger from the login panel.

---

## 🧹 Project Cleanup & Maintenance

A dependency-driven audit confirmed the two shipping scenes (`Bootstrap`, `TroubleshootingWebIntegration`) pull in 282 assets. The following **unused** third-party art, demo, plugin, and broken-sample folders were removed (~1.4 GB), verified to leave the shipping dependency set unchanged and the console error-free:

- **Broken samples:** `Assets/Oculus/Avatar2_SampleAssets` (these `.glb` files threw ~30 recurring `GLTFast: JsonParsingFailed` errors on every reimport and are unused).
- **Unused art / environment packages:** `Creepy_Cat`, `ScifiOfficeLite`, `Simple Garage`, `GeniusCrate_Games`, `Hot_Rod_OffRoad`, `3D Laboratory Environment with Appratus`, `polyperfect`.
- **Unused UI kits:** `Dark UI`, `Sci-Fi UI` (not referenced by the shipping UI).
- **Unused plugins (no project references):** `Flexalon`, `humanoidcontrol4_free`, `MRUKSamples`. The QR scripts the app actually uses live in `_TrueEchoVR/_SCRIPTS/Managers/QrCodeManager.cs` and depend on the MRUK *package*, not any samples folder. (The verbatim Meta MRUK QR-Code-Detection sample previously copied into `_TrueEchoVR/QRCodeDetection/` was confirmed unreferenced and **deleted** — do not reintroduce it.)
- **Archived scenes:** `Assets/_TrueEchoVR/_SCENES/_ARCHIVE/` (superseded rig prototypes).
- **Default Unity cruft:** `Assets/Scenes/` (auto-generated `SampleScene`) and `Assets/Basic/` (generic mobile-game button texture pack, wrong visual style and unused).
- **Orphaned Building Block:** the stray `[BuildingBlock] ISDK_PokeInteraction` button (wired to nothing) was removed from `Bootstrap`.

**Deliberately preserved** (look unreferenced by scenes but are required via ProjectSettings or runtime loading): `Settings` (URP pipeline), `XR` (OpenXR config), `Plugins` (AndroidManifest), `Oculus` (project config), `Resources`, `StreamingAssets`, `#Meta`/`MetaAssets`/`CompositionLayers`/`MRTemplateAssets` (Meta SDK), `TextMesh Pro`, `SimpleIcons` & `Samples` (referenced by shipping scenes).

**Remaining optional candidates** (deliberately kept — they may be needed): the disabled demo scene `_TrueEchoVR_StartScene_Demo-XRIT-Rig.unity` (your own dev scene) and `Speech Bubble` (a speech-bubble/label system that may support the planned "show message" training step type). Remove them later if they go unused.

> **Tip:** Keep one config source of truth — edit backend settings on `BackendConfig.asset`, not the script defaults, to avoid drift (the asset had gone stale against the script schema and was repaired during this work).

> **Note:** If an editor Undo (Ctrl+Z) is pressed after these folders are deleted, Unity restores them (some, e.g. `MRUKSamples` and `Simple Garage`, contain corrupt `.png` files that then log `Could not create asset … File could not be read`). They were re-deleted; the build-scene dependency set is unaffected either way (verified at 282 assets). If they reappear, simply delete them again.

### Documentation layout (one source of truth) — _v2.5_
All project docs now live at the **repo root** and each has a single purpose — no duplicates:
- **`README.md`** (this file) — the project overview & how-to.
- **`BACKEND_CONTRACT.md`** — the single canonical client⇄backend **wire contract** (schemas).
- **`REPLIT_AI_INTEGRATION_GUIDE.md`** — the narrative backend integration guide + checklist.
- **`TESTING_CHECKLIST.md`** — the QA/verification checklist.
- **`Assets/Project_Overview.md`** — in-editor architecture index; **`Assets/Plans/`** — historical worklogs.

> The redundant `Assets/_TrueEchoVR/_SCRIPTS/WebAppManager_Communication_Doc.md` (a stale-named duplicate of `BACKEND_CONTRACT.md`) and the stale root copy of `EVALUATION_AND_FIX_PLAN.md` (canonical lives in `Assets/Plans/`) were **removed** in v2.5. Keep `BACKEND_CONTRACT.md` and `REPLIT_AI_INTEGRATION_GUIDE.md` in sync when the client schema changes.

## 📈 Suggested Next Steps
1. **Item Search**: Implement a search filter for the QR-code dropdown as location databases grow (the list is already color-coded — see _QR Code Dropdown_).
2. **TURN Server**: Configure a dedicated TURN server (e.g., via AWS) for reliable connectivity in corporate firewalls.
3. **Per-item Spatial Anchors**: The RoomAnchor is now a persisted Meta `OVRSpatialAnchor` (see _Spatial Anchor Persistence_). A logical next step is giving physically-present items their own anchors for sub-centimeter stability without re-scanning.
4. **Shared Spatial Anchors**: If multi-headset colocation is ever required (two+ Quests in the same room), add Meta Shared Spatial Anchors at the single isolated boundary already provided in `QrCodeManager`. _Note:_ SSA is headset-to-headset only and does **not** replace the backend coordinate sync used by the web dashboard.

---
*Developed for the TrueEchoVR MR Ecosystem.*