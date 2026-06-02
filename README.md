# TrueEchoVR - Octane V1
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

### 1. Persistent Bootstrap Flow
The application initializes in the `Bootstrap` scene (`Assets/_TrueEchoVR/_SCENES/Bootstrap.unity`). 
- **Managers**: Core systems (`SignalingManager`, `QrCodeManager`, `TaskManager`, `UIManager`) use `DontDestroyOnLoad`.
- **Initialization**: Managers connect to the signaling server and load local calibration before transitioning to the environment scenes.

### 2. UI System (`TEVR_UI_System`)
All UI is consolidated under a single **UIManager**.
- **Action-Driven**: Panels listen for `OnUIStateChanged` events to toggle visibility (Login -> Calibration -> Session).
- **Lazy Follow**: Handled via `UIPanelGroup` settings for distance and angle thresholds.

### 3. Networking & Signaling
The **`SignalingManager`** handles the hybrid REST/WebSocket communication.
- **WebRTC**: Bitrate capped at 2Mbps for Quest 3 thermal safety.
- **Stats**: Real-time performance monitoring of the peer connection.

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

*Developed by the TrueEchoVR Team.*

---

## 🚀 Key Features

- **MR Spatial Awareness**: Leverages the **Meta MR Utility Kit (MRUK)** for physical room awareness and high-fidelity passthrough integration.
- **QR Calibration & Persistence**: Align the virtual coordinate system with the physical world using a dedicated "Room Anchor" QR code. Calibration data is persisted locally and synced with a remote Replit backend.
- **Remote Expert Streaming**: Real-time video and audio streaming via **WebRTC**. A web-based expert can view the operator's perspective and "point" to physical objects using virtual overlays.
- **Intelligent HUD**: A world-space "lazy-follow" HUD provides instructions, hints, and live connection diagnostics (Latency/Ping).
- **Procedural Task System**: linear task progression with physical interaction detection (Grab, Snap, Button Press).
- **LMS Readiness**: Built-in support for xAPI and SCORM tracking for enterprise training integration.

---

## 📁 Project Architecture

The core logic resides in `Assets/_TrueEchoVR/_SCRIPTS/`, organized into industry-standard subfolders:

- **`Core/`**: Foundational systems (`BaseInteractionHandler`, `TaskStepData`, `GameEvent`).
- **`Interactions/`**: Implementation-specific handlers (`GrabInteractionHandler`, `SnapInteractionHandler`, `ButtonInteractionHandler`).
- **`LMS/`**: Educational tracking implementations (`BaseLmsTracker`, `XApiTracker`, `ScormTracker`).
- **`Managers/`**: Global application state:
  - `TaskManager`: Manages step sequences and progress.
  - `QrCodeManager`: Handles Meta Quest QR tracking and serialization.
  - `VrKeyboardManager`: Manages the system keyboard for input fields.
- **`Networking/`**: Communication layer:
  - `SignalingManager`: The "Web App Manager" that handles Socket.io, WebRTC, and REST APIs.
  - `SessionFlowManager`: Orchestrates the initialization flow and links tracking with the backend.
- **`UI/`**: Spatial interface controllers (`VrHudController`, `SessionUiController`).

---

## 🌐 Web App & Replit Integration

The project is designed to work in tandem with a **Replit-hosted web application**. This backend acts as the signaling server for WebRTC and a persistent database for spatial data.

### 1. SignalingManager (Web App Manager)
The `SignalingManager` (referenced as `webAppManager` in the code) is the central bridge between Unity and Replit.
- **WebSocket (Socket.io)**: Connects to `wss://<replit-app-name>.replit.app/socket.io/`. It uses a custom framing protocol to handle Socket.io events (`join-room`, `chat-message`, `offer`, `answer`, `ice-candidate`).
- **WebRTC Producer**: Captures a designated `captureCamera` (typically a secondary camera following the HMD) into a `RenderTexture` and streams it to the admin console.
- **Remote Commands**: Receives `point-to` events from the expert, which trigger the `PointerArrow` in the operator's HUD to guide them to specific QR codes.

### 2. Linking to Replit
To link this Unity project to your Replit app:
1. Open `SignalingManager.cs` or the `TEVR_Managers_Troubleshooting` object in the scene.
2. Update the `Server Base URL` field to your Replit app's URL (e.g., `https://live-troubleshooting-app.replit.app`).
3. Ensure the Replit app is running and the `socket.io` server is listening on port 3000 (or the default proxy port).

### 3. REST API Persistence
The system uses REST endpoints for permanent data storage:
- `GET /api/headsets/{id}/startup-data`: Fetches initial configuration for the specific Quest unit.
- `POST /api/locations/{id}/qr-codes`: Syncs the spatial calibration (QR code offsets) to the cloud.
- `GET /api/locations/{id}/qr-codes`: Allows multiple headsets in the same location to share the same physical-to-virtual alignment.

---

## 🛠 Setup & Build

- **Scene**: `TroubleshootingWebIntegration.unity` is the primary entry point.
- **Target Platform**: Android (Meta Quest 3).
- **Active Rig**: `XR Origin Hands (XR Rig)` is the primary tracking rig.
- **Input Policy**: 
  - **Editor**: Uses XR Controllers and Hand Simulation.
  - **Quest 3 Build**: Forced to **Hand Tracking Only** to prevent controller interference.
- **Required Plugins**: 
  - Meta XR All-in-One SDK (Core, MRUK, Interaction).
  - Unity WebRTC.
  - NativeWebSocket (Meta version).

---

## 📝 Usage Notes
- **Calibration**: On startup, the system requires the user to look at a "Room Anchor" QR code to align the virtual world.
- **Passthrough**: Ensure the `Main Camera` has clear flags set to `Solid Color` with Alpha 0 to see the real world.
- **LMS Integration**: SCORM tracking is functional in WebGL builds, while xAPI is the default for Android/Quest.

---
*Developed for the TrueEchoVR MR Ecosystem.*
