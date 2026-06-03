# TrueEchoVR - RemoteAssistance
## Advanced Mixed Reality Training & Remote Assistance

Welcome to the **TrueEchoVR** project. This application is a professional-grade Extended Reality (XR) solution designed for the Meta Quest 3. It bridges physical operations with digital guidance using **Meta's MR Utility Kit (MRUK)** and **QR Code tracking**.

---

## 🚀 Key Features

- **MR Spatial Awareness**: Full passthrough integration with physical room awareness. **Pure AR Locomotion**: No virtual locomotion (teleport/snap-turn) is enabled; movement is 1:1 with the headset's physical movement in the real world.
- **QR Calibration & Persistence**: Align the virtual coordinate system with the physical world using a dedicated "Room Anchor" QR code. Calibration is persistent and multi-tenant aware.
- **Anchor-Relative Hierarchy**: Virtual objects are parented to physical anchors, ensuring they stay locked in place even if the headset re-localizes.
- **Remote Expert Streaming**: Real-time video and audio via **WebRTC**. An expert can view the operator's perspective and "point" to physical objects.
- **Enriched Remote Guidance**: Supports pulsing 3D outlines and billboarding labels that appear at exact real-world coordinates provided by the admin.
- **Intelligent UI System**: A world-space "lazy-follow" HUD and session manager that follow the user comfortably without jitter.
- **Enterprise Health Reporting**: Automatic telemetry (Battery, Calibration State, Location ID) reported to the admin every 60 seconds.
- **LMS Ready**: Built-in support for xAPI and SCORM tracking.

---

## 🛠 System Architecture

The project follows a modular, manager-centric, and event-driven architecture located in `Assets/_TrueEchoVR/_SCRIPTS/`.

- **`Core/`**: Foundational systems (`BaseInteractionHandler`, `TaskStepData`, `GameEvent`, `BootstrapLoader`).
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


### Mouse Interaction & Simulator in Editor
If you cannot rotate the camera or click UI buttons in the Editor:
1. **Focus the Game View**: Click anywhere inside the Game View window.
2. **Toggle Cursor Lock**: Press the **`\` (backslash)** key. This toggles between "Mouse controls Rig" and "Mouse controls Arrow".
3. **Rotate Camera**: When the cursor is locked (hidden), move the mouse. You can also **Hold Right-Click** to manipulate the head.
4. **Click Buttons**: Ensure the cursor is **unlocked** (visible) to use the standard mouse arrow on UI buttons. 
5. **World Space UI**: The `PersistentXRRig` automatically links the `CenterEyeAnchor` camera to all World Space canvases to ensure raycasting works correctly for the mouse arrow.

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
Open `Assets/_TrueEchoVR/_SCRIPTS/Networking/BackendConfig.cs` to set the default `apiHost`, `customerId`, and `locationId`.

---

## 🌐 Connectivity (Replit/AWS)
The system connects to a **Socket.io** signaling server for WebRTC handshakes and uses a **REST API** for spatial persistence.
- **Handshake**: `42["join-room", { ... }]`
- **Persistence**: `GET /api/headsets/{id}/startup-data` (Includes location-specific dictionaries and QR offsets).
- **Health**: `42["health-update", { ... }]` sent every minute.

---

## 🚀 Build & Deployment
1. **Target**: Android (Meta Quest 3).
2. **Graphics**: Vulkan preferred.
3. **Input**: Meta XR Hand Tracking (forced in build).
4. **Scenes**: `Bootstrap` must be at Index 0.

---

## 📈 Suggested Next Steps
1. **Item Search**: Implement a search filter for the item dictionary dropdown as location databases grow.
2. **TURN Server**: Configure a dedicated TURN server (e.g., via AWS) for reliable connectivity in corporate firewalls.
3. **Spatial Anchors**: Integrate Meta's Spatial Anchor API for markers that don't have physical QR codes.

---
*Developed for the TrueEchoVR MR Ecosystem.*