# TrueEchoVR - RemoteAssistance
## Advanced Mixed Reality Training & Remote Assistance

Welcome to the **TrueEchoVR** project. This application is a professional-grade Extended Reality (XR) solution designed for the Meta Quest 3. It bridges physical operations with digital guidance using **Meta's MR Utility Kit (MRUK)** and **QR Code tracking**.

---

## 🚀 Key Features

- **MR Spatial Awareness**: Full passthrough integration with physical room awareness. **Pure AR Locomotion**: No virtual locomotion (teleport/snap-turn) is enabled; movement is 1:1 with the headset's physical movement in the real world.
- **QR Calibration & Persistence**: Align the virtual coordinate system with the physical world using a dedicated "Room Anchor" QR code. Calibration is persistent and multi-tenant aware.
- **Anchor-Relative Hierarchy**: Virtual objects are parented to physical anchors, ensuring they stay locked in place even if the headset re-localizes.
- **Remote Expert Streaming**: Real-time video and audio via **WebRTC**. An expert can view the operator's perspective and "point" to physical objects. Video defaults to the **Meta Passthrough Camera** (real-world view) and automatically falls back to the Unity-rendered view if the passthrough camera is unavailable.
- **Unified Permission Flow**: A startup `PermissionsBootstrapper` requests every runtime permission the app needs (Scene/spatial data for QR tracking, Camera for passthrough streaming) in a single prompt the first time the app launches.
- **On-Headset Text Entry**: Login fields are pre-populated and fully editable — tapping a field raises the Quest system keyboard so Customer/Location IDs can be typed manually when a setup QR code is not available.
- **Remembered Connection**: Once a device is set up (via setup-QR scan or manual sign-in), the Customer ID and Location ID are persisted and automatically reloaded on every subsequent launch, so the setup QR is **not** required again.
- **Resumable Tasks**: `TaskManager` persists progress to local storage, so if the cloud connection drops mid-session the training resumes from the last incomplete step on restart.
- **Enriched Remote Guidance**: Supports pulsing 3D outlines and billboarding labels that appear at exact real-world coordinates provided by the admin.
- **Intelligent UI System**: A world-space "lazy-follow" HUD and session manager that follow the user comfortably without jitter.
- **Enterprise Health Reporting**: Automatic telemetry (Battery, Calibration State, Location ID) reported to the admin every 60 seconds.
- **LMS Ready**: Built-in support for xAPI and SCORM tracking.

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
- **Demo Mode**: If the backend API is unreachable or internet is disconnected, the system automatically enters **Demo Mode**, bypassing network synchronization and allowing offline testing.
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
Select the `TEVR_UI_System` prefab. In the **UIManager** component, you can adjust `Forward Distance` and `Lazy Follow Settings` for the **HUD** and **Session** groups independently.

### Adding Training Steps
Locate the `TaskManager` on the manager object. Add a new `TaskStepData` (ScriptableObject) to the `steps` list. The HUD will automatically update with the description and hint.

### Deployment Configuration
The active backend settings live in the **`BackendConfig` ScriptableObject** at `Assets/_TrueEchoVR/_DATA/BackendConfig.asset` (assigned to `SignalingManager`). Edit `apiHost`, `customerId`, and `locationId` there. The script defaults in `Assets/_TrueEchoVR/_SCRIPTS/Networking/BackendConfig.cs` are only used when a new asset is created. Both are currently provisioned with `customerId: cust-004` and the matching `locationId`.

### Video Source
`SignalingManager.videoSource` selects the WebRTC stream source:
- **PassthroughCamera** (default): streams the real-world view via the Meta Passthrough Camera Access (`WebCamTexture`). Requires the Camera permission + developer mode.
- **RenderedCamera**: streams only the Unity-rendered eye view (virtual content). Used automatically as a fallback when the passthrough camera cannot start within `passthroughStartTimeout` seconds.

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
- **Unused plugins (no project references):** `Flexalon`, `humanoidcontrol4_free`, `MRUKSamples` (the QR scripts the app actually uses live in `_TrueEchoVR/QRCodeDetection` and depend on the MRUK *package*, not this samples folder).
- **Archived scenes:** `Assets/_TrueEchoVR/_SCENES/_ARCHIVE/` (superseded rig prototypes).
- **Default Unity cruft:** `Assets/Scenes/` (auto-generated `SampleScene`) and `Assets/Basic/` (generic mobile-game button texture pack, wrong visual style and unused).
- **Orphaned Building Block:** the stray `[BuildingBlock] ISDK_PokeInteraction` button (wired to nothing) was removed from `Bootstrap`.

**Deliberately preserved** (look unreferenced by scenes but are required via ProjectSettings or runtime loading): `Settings` (URP pipeline), `XR` (OpenXR config), `Plugins` (AndroidManifest), `Oculus` (project config), `Resources`, `StreamingAssets`, `#Meta`/`MetaAssets`/`CompositionLayers`/`MRTemplateAssets` (Meta SDK), `TextMesh Pro`, `SimpleIcons` & `Samples` (referenced by shipping scenes).

**Remaining optional candidates** (deliberately kept — they may be needed): the disabled demo scene `_TrueEchoVR_StartScene_Demo-XRIT-Rig.unity` (your own dev scene) and `Speech Bubble` (a speech-bubble/label system that may support the planned "show message" training step type). Remove them later if they go unused.

> **Tip:** Keep one config source of truth — edit backend settings on `BackendConfig.asset`, not the script defaults, to avoid drift (the asset had gone stale against the script schema and was repaired during this work).

## 📈 Suggested Next Steps
1. **Item Search**: Implement a search filter for the item dictionary dropdown as location databases grow.
2. **TURN Server**: Configure a dedicated TURN server (e.g., via AWS) for reliable connectivity in corporate firewalls.
3. **Spatial Anchors**: Integrate Meta's Spatial Anchor API for markers that don't have physical QR codes.

---
*Developed for the TrueEchoVR MR Ecosystem.*