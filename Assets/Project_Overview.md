# Project Overview: TrueEchoVR - Octane V1

## 1. Project Description
**TrueEchoVR** is an immersive Mixed Reality (MR) training and troubleshooting application developed for Meta Quest 3. The project facilitates guided procedural training, remote expert assistance via WebRTC streaming, and learning management system (LMS) integration (SCORM/xAPI). It leverages the **Meta MR Utility Kit (MRUK)** for scene awareness and **WebRTC** for live video/audio communication between a VR headset and a web-based expert console.

## 2. Gameplay Flow / User Loop
1.  **Initialization**: The user boots into an MR environment where the room is scanned and aligned using QR codes (via `QRCodeManager`).
2.  **Session Setup**: For remote troubleshooting, the user enters a room code to connect to a signaling server (`TroubleshootingStreamingManager`).
3.  **Task Execution**: The `TaskManager` activates a sequence of `TaskStepData` (ScriptableObject-like structures).
4.  **Interaction**: Users interact with virtual or physical-aligned objects. Handlers like `GrabHandler` and `SnapHandler` detect these actions and notify the `TaskManager`.
5.  **Feedback & Tracking**: Progress is displayed on a world-space HUD (`MainVRHUDUI`) and simultaneously logged to external LMS platforms (SCORM or xAPI).
6.  **Remote Assistance**: An expert can view the user's stream, send chat messages, and "point" to specific objects in the user's view by referencing QR code coordinates.

## 3. Architecture
The project follows a **Manager-Centric** and **Event-Driven** architecture.
-   **Central Management**: `TaskManager` acts as the single source of truth for training progress, using a Singleton pattern (`TaskManager.Current`).
-   **Communication Layer**: Uses **WebSockets** for signaling and **WebRTC** for high-bandwidth data (video/audio).
-   **Event System**: Utilizes `UnityEvents` and C# `Actions` for decoupled communication between systems (e.g., `onStepCompleted` triggers UI updates and LMS logging).
-   **Abstraction**: Tracking logic is abstracted via the `LmsTracker` base class, allowing the project to switch between SCORM, xAPI, or local logging seamlessly.

`Location: Assets/_TrueEchoVR/_SCRIPTS`

## 4. Game Systems & Domain Concepts

### Task & Procedure System
Governs the sequence of operations a user must perform.
-   `TaskManager`: The core engine that manages the list of steps, current progress, and completion logic.
-   `TaskStepData`: A data container defining a single step's ID, description, hint, and target object.
-   `InteractionHandler`: Abstract base class for components that trigger step completion when an interaction occurs.
-   `GrabHandler`: Completes a task step when the user grabs an `XRBaseInteractable`.
-   `SnapHandler`: Completes a task step when an object is placed into an `XRSocketInteractor`.

`Location: Assets/_TrueEchoVR/_SCRIPTS`

### Live Troubleshooting & Streaming
Enables real-time collaboration between the VR user and a remote expert.
-   `TroubleshootingStreamingManager`: Handles WebRTC peer connections, signaling via WebSockets, and media stream (camera/RenderTexture) capture.
-   `TroubleshootingSessionInitialization`: Manages the initial handshake and room joining logic.
-   `TroubleshootingSessionUIManager`: Bridge between the streaming system and the in-game UI for chat and connectivity status.

`Location: Assets/_TrueEchoVR/_SCRIPTS/LiveTroubleShooting`

### QR & Spatial Awareness System
Links the physical room to the virtual training content.
-   `QRCodeManager`: Tracks Meta Quest QR anchors, maintains a registry of `QRCodeInstance` objects, and persists spatial data to JSON.
-   `LmsTracker`: Abstract base for reporting progress to Learning Management Systems.
-   `ScormTracker`: WebGL-specific bridge for SCORM communication.
-   `XApiTracker`: REST-based implementation for xAPI (LRS) reporting.

`Location: Assets/_TrueEchoVR/_SCRIPTS`

## 5. Scene Overview
-   **TroubleshootingWebIntegration**: The primary entry point. It sets up the MR environment, initializes the `TaskManager`, and establishes the connection to the remote expert server.
-   **SampleScene**: A playground for testing MRUK interactions and basic physics.
-   **3D Laboratory / Kitchen Set / Scifi Office**: Environment-specific scenes likely used as additive templates or reference environments for training modules.

`Location: Assets/_TrueEchoVR/_SCENES`

## 6. UI System
The project uses a mix of **World-Space UGUI** and **Overlay UI**.
-   **MainVRHUDUI**: A world-space "lazy-follow" HUD that displays current task instructions and hints. It uses a `CanvasGroup` for smooth fading and a `pointerArrow` to guide users toward target objects.
-   **Session UI**: Standard UGUI panels for room code entry and chat logs, often managed by `TroubleshootingSessionUIManager`.
-   **UI Selection**: `UISelectionHandler` manages gaze or controller-based interaction with world-space canvases.

`Location: Assets/_TrueEchoVR/_SCRIPTS`

## 7. Asset & Data Model
-   **Task Configuration**: Defined as lists of `TaskStepData` within the `TaskManager` component.
-   **QR Data Persistence**: Spatial data for detected QR codes is saved as `QRDetectedData.json` in the `Application.persistentDataPath`.
-   **Network Payloads**: JSON-serializable classes (e.g., `ChatPayload`, `JoinRoomPayload`) are used for WebSocket communication.
-   **Prefabs**: Specialized interactables are located in `Assets/_TrueEchoVR/_PREFABS`, categorized by their role in the training (e.g., tools, UI panels).

## 8. Notes, Caveats & Gotchas
-   **Meta Quest Camera Access**: As noted in `TroubleshootingStreamingManager`, the Quest 3 hardware prevents direct raw camera pixel access for privacy. The stream currently sends virtual overlays or a designated `captureCamera` RenderTexture rather than the actual passthrough feed.
-   **QR Tracking**: QR codes are used as "Room Anchors." If the anchor QR is not discovered, the system may prevent other QR-based objects from initializing to ensure spatial consistency.
-   **LMS Synchronization**: `ScormTracker` only functions in WebGL builds. For Android/Quest builds, `XApiTracker` is the preferred method for remote logging.
-   **WebSocket Implementation**: The project uses a native WebSocket implementation (`Meta.Net.NativeWebSocket`) to ensure compatibility with the Quest's Android-based OS.