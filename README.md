# TrueEchoVR - Octane V1

**TrueEchoVR** is an immersive Mixed Reality (MR) training and remote troubleshooting platform developed for the Meta Quest 3. It bridges physical environments with virtual instructional content using real-time spatial calibration and live video streaming.

## 🚀 Key Features
- **MR Spatial Awareness**: Uses the Meta MR Utility Kit (MRUK) for physical room awareness and passthrough integration.
- **QR Calibration & Persistence**: Align the virtual coordinate system with the physical world using a dedicated "Room Anchor" QR code. Calibration data is persisted locally and to a remote backend.
- **Remote Expert Streaming**: Real-time video and audio streaming via WebRTC, allowing a web-based expert to view the operator's perspective and "point" to physical objects.
- **Intelligent HUD**: A world-space "lazy-follow" HUD that provides instructions, hints, and live connection diagnostics (Latency/Ping).
- **Procedural Task System**: Linear task progression with physical interaction detection (Grab, Snap, Button Press).
- **LMS Readiness**: Built-in support for xAPI and SCORM tracking.

## 📁 Project Architecture
The core logic resides in `Assets/_TrueEchoVR/_SCRIPTS/`, organized into industry-standard subfolders:

- **`Core/`**: Foundational systems (`BaseInteractionHandler`, `TaskStepData`, `GameEvent`).
- **`Interactions/`**: Implementation-specific handlers (`GrabInteractionHandler`, `SnapInteractionHandler`, etc.).
- **`LMS/`**: Educational tracking implementations (`BaseLmsTracker`, `XApiTracker`).
- **`Managers/`**: Global application state (`TaskManager`, `QrCodeManager`, `VrKeyboardManager`).
- **`Networking/`**: Communication layer (`SignalingManager`, `SessionFlowManager`).
- **`UI/`**: Spatial interface controllers (`VrHudController`, `SessionUiController`).

## 🌐 Replit Integration
The project connects to a Replit-hosted backend for signaling and data storage.

### Public Functionality
- **Socket.io Signaling**: Real-time event coordination (WebRTC Handshake, Chat, Remote Pointing).
- **REST Persistence**: 
  - `GET /api/headsets/{id}/startup-data`: Fetch configuration.
  - `POST /api/locations/{id}/qr-codes`: Sync spatial calibration.
- **WebRTC Producer**: Streams the headset perspective (CenterEyeAnchor) to the expert console.

## 🛠 Setup & Build
- **Scene**: `TroubleshootingWebIntegration.unity` is the primary entry point.
- **Target Platform**: Android (Meta Quest 3).
- **Required Plugins**: 
  - Meta XR All-in-One SDK (Core, MRUK, Interaction).
  - Unity.WebRTC.
  - NativeWebSocket (Meta version).

---
*Developed for the TrueEchoVR MR Ecosystem.*