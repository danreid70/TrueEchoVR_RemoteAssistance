# Project Overview: TrueEchoVR - RemoteAssistance

## 1. Project Description
**TrueEchoVR** is an immersive Mixed Reality (MR) training and troubleshooting application developed for Meta Quest 3. The project facilitates guided procedural training, remote expert assistance via WebRTC streaming, and learning management system (LMS) integration (SCORM/xAPI). It leverages the **Meta MR Utility Kit (MRUK)** for scene awareness and **WebRTC** for live video/audio communication between a VR headset and a web-based expert console.

## 2. Gameplay Flow / User Loop
1.  **Initialization**: The user boots into the `Bootstrap` scene where core managers are initialized and persisted.
2.  **Calibration**: The user aligns their space by looking at a "Room Anchor" QR code (via `QrCodeManager`).
3.  **Session Setup**: For remote troubleshooting, the user enters a room code to connect to a signaling server (`SignalingManager`).
4.  **Task Execution**: The `TaskManager` activates a sequence of `TaskStepData`.
5.  **Interaction**: Users interact with virtual or physical-aligned objects. Handlers detect these actions and notify the `TaskManager`.
6.  **Feedback & Tracking**: Progress is displayed on a world-space HUD and simultaneously logged to external LMS platforms.
7.  **Remote Assistance**: An expert can view the user's stream and "point" to specific objects using the pulsing `TargetHighlightController`.

## 3. Architecture
The project follows a **Manager-Centric** and **Event-Driven** architecture.
-   **Central Management**: `UIManager` acts as the single source of truth for UI states, while `SignalingManager` handles all network communication.
-   **Communication Layer**: Uses **WebSockets** for signaling and **WebRTC** for high-bandwidth data (video/audio).
-   **Event System**: Utilizes C# `Actions` for decoupled communication (e.g., `OnUIStateChanged`).
-   **Persistence**: Managers use `DontDestroyOnLoad` and initialization is handled in a dedicated Bootstrap scene.

`Location: Assets/_TrueEchoVR/_SCRIPTS`

## 4. Game Systems & Domain Concepts

### Task & Procedure System
-   `TaskManager`: The core engine that manages steps, progress, and completion logic.
-   `TaskStepData`: A data container defining a single step's ID, description, hint, and target object.

### Live Troubleshooting & Streaming
-   `SignalingManager`: Handles Socket.io signaling, WebRTC peer connections, and health reporting.
-   `SessionFlowManager`: Orchestrates the transition from calibration to active session.
-   `SessionUiController`: Bridge between the signaling system and the in-game UI.

### UI & Spatial Guidance
-   `UIManager`: Manages the "Lazy Follow" positioning and visibility of all UI groups.
-   `TargetHighlightController`: Provides 3D visual outlines and billboarding labels for remote pointing.
-   `VrHudController`: Controls the world-space instructional HUD.

### QR & Spatial Awareness System
-   `QrCodeManager`: Tracks Meta Quest QR anchors and maintains an **Anchor-Relative Hierarchy** for virtual objects.

`Location: Assets/_TrueEchoVR/_SCRIPTS`

## 5. Scene Overview
-   **Bootstrap**: The mandatory entry point. Initializes all persistent managers and the XR Rig.
-   **TroubleshootingWebIntegration**: The primary MR environment scene.

## 6. Asset & Data Model
-   **Prefabs**: All systems are prefabbed in `Assets/_TrueEchoVR/_PREFABS` for cross-scene consistency.
-   **Network Payloads**: Versioned JSON structures are used for multi-tenant location isolation.

## 7. Notes, Caveats & Gotchas
-   **Thermal Safety**: WebRTC bitrate is capped at 2Mbps to prevent Quest 3 overheating.
-   **Anchor Locking**: Training objects must be parented to the `RoomAnchor` to ensure spatial stability.
-   **LMS Synchronization**: `XApiTracker` is the preferred method for Android/Quest builds.
