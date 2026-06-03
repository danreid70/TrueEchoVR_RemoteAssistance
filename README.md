# TrueEchoVR - RemoteAssistance
## Advanced Mixed Reality Training & Remote Assistance

Welcome to the **TrueEchoVR** project. This application is a professional-grade Extended Reality (XR) solution designed for the Meta Quest 3. It bridges physical operations with digital guidance using **Meta's MR Utility Kit (MRUK)** and **QR Code tracking**.

---

## 🚀 Key Features

- **MR Spatial Awareness**: Full passthrough integration with physical room awareness.
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
- **Managers**: Core systems (`SignalingManager`, `QrCodeManager`, `TaskManager`, `UIManager`) use `DontDestroyOnLoad`.
- **Initialization**: Managers connect to the signaling server and load local calibration before transitioning to the environment scenes.

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