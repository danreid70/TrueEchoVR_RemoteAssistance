# TrueEchoVR Project Overview

## 1. Project Description
**TrueEchoVR** is a Mixed Reality (MR) Remote Assistance and Training platform designed for the Meta Quest ecosystem. It enables real-time collaboration between a headset user and a remote expert via a web-based dashboard. The project leverages MR Utility Kit (MRUK) for spatial awareness and QR code-based calibration to synchronize physical environments with digital instructions. It is built for enterprise maintenance and procedural training, featuring a robust task engine and LMS integration (SCORM/xAPI).

**Core Pillars:**
- **Real-time Spatial Collaboration:** WebRTC-based video streaming and "Point-To" spatial markers.
- **Accurate Calibration:** Environment-to-server alignment using QR code anchors.
- **Guided Procedural Tasks:** A step-by-step training engine with interactive object validation.
- **Enterprise Ready:** LMS tracking and persistent session progress.

## 2. Gameplay Flow / User Loop
1.  **Boot & Provisioning:** The app starts in the `Bootstrap` scene. `SignalingManager` registers the headset serial with the backend and fetches `StartupData`.
2.  **Connection:** User enters a room code to join a live session or starts a local demo.
3.  **Calibration:** User scans a designated "Room Anchor" QR code. This aligns the MR coordinates with the remote expert's view.
4.  **Task Session:** The `TaskManager` activates the first step. The user sees instructions on their VR HUD and spatial arrows pointing toward target objects.
5.  **Interaction Loop:** User interacts with objects (Grab/Snap/Button). `BaseInteractionHandler` notifies `TaskManager` of completion.
6.  **Remote Assistance:** Remote experts can send chat messages or use "Point-To" commands that spawn temporary spatial indicators in the user's MR space.
7.  **Completion & Sync:** Final progress is sent to the LMS, and the session is logged.

## 3. Architecture
The project follows a **Manager-Centric** architecture with a strong emphasis on **Persistence** and **Event-Driven** communication.

-   **Persistent Singletons:** Critical systems (`SignalingManager`, `TaskManager`, `UIManager`, `QrCodeManager`) are initialized in the `Bootstrap` scene and use `DontDestroyOnLoad` to maintain state across scene transitions.
-   **Communication Layer:** 
    -   `SignalingManager`: Uses WebRTC (via `Unity.WebRTC`) and WebSockets (`NativeWebSocket`) for low-latency bidirectional data.
    -   `SessionFlowManager`: Acts as the glue between network events and in-game reactions.
-   **Interaction Layer:** Uses Unity’s **XR Interaction Toolkit (XRI)** wrapped in custom handlers to trigger procedural progress.
-   **Data Flow:** Backend (Node.js/Replit) $\leftrightarrow$ `SignalingManager` $\leftrightarrow$ `TaskManager` $\leftrightarrow$ UI/Interactions.

`Location: Assets/_TrueEchoVR/_SCRIPTS/Core`

## 4. Game Systems & Domain Concepts

### Task & Training System
Manages the procedural flow of the experience. It supports linear steps defined by `TaskStepData`.
-   `TaskManager`: The central engine that tracks the current step, validates completions, and handles persistence.
-   `TaskStepData`: A ScriptableObject-like structure (managed as a list in TaskManager) defining step instructions, target objects, and event hooks.
-   `BaseLmsTracker`: Abstract base for progress reporting (implemented by `ScormTracker` and `XApiTracker`).
-   **Extension:** To add new step types, create a new class inheriting from `BaseInteractionHandler` and call `HandleCompletion()` on interaction.
`Location: Assets/_TrueEchoVR/_SCRIPTS/Managers`

### MR & QR Calibration System
Synchronizes the physical world with the digital session using Meta's MR Utility Kit.
-   `QrCodeManager`: Interfaces with MRUK to detect QR trackables. It manages a "Room Anchor" that serves as the zero-point for all other relative spatial data.
-   `QRAnchorData`: Data structure for storing and syncing QR positions.
-   **Pattern:** Uses a "Dormant Activation" pattern where QR codes detected before the Room Anchor is established are held in a list and repositioned once the anchor is found.
`Location: Assets/_TrueEchoVR/_SCRIPTS/Managers`

### Spatial Interaction System
Standardizes how user actions in VR translate into task progress.
-   `BaseInteractionHandler`: The abstract base that bridges XRI events to the `TaskManager`.
-   `GrabInteractionHandler`: Triggers completion when an `XRGrabInteractable` is selected.
-   `SnapInteractionHandler`: Triggers when an object is placed in a specific socket.
`Location: Assets/_TrueEchoVR/_SCRIPTS/Interactions`

## 5. Scene Overview
-   **Bootstrap:** The entry point. Initializes all persistent managers and handles the initial backend handshake.
-   **TroubleshootingWebIntegration:** The primary functional scene where MR sessions take place. Contains the `XRRig`, `MRUK` setup, and the `UIManager`.
-   **Demo/Sample Scenes:** Located in `Assets/Scenes` and external asset folders (e.g., `ScifiOfficeLite`) for environment testing.

## 6. UI System
The UI is built using **Unity UI (uGUI)** and optimized for Mixed Reality.
-   **UIManager:** Controls the "Lazy Follow" behavior of the world-space UI, ensuring panels stay within the user's FOV without feeling "glued" to their face.
-   **VrHudController:** Manages the primary instruction display and task highlighting.
-   **SessionUiController:** Handles the login flow, QR code list, and chat interface.
-   **Interaction:** Uses the `PointableCanvas` and `RayInteractor` from the Meta Interaction SDK for clean, reliable MR interactions.
-   **Extending:** New screens should be added as children of the `uiCanvasRoot` and registered in the `UIManager` state enum.

`Location: Assets/_TrueEchoVR/_SCRIPTS/UI`

## 7. Asset & Data Model
-   **JSON Persistence:** Task progress (`TaskProgress.json`) and detected QR data (`QRDetectedData.json`) are stored in `Application.persistentDataPath` for session recovery.
-   **StartupData:** A comprehensive JSON payload from the server containing location info, QR mappings, and versioning.
-   **Prefabs:** 
    -   `PersistentXRRig`: The standard player setup.
    -   `RemoteAssistance_V1_Cube`: Placeholder for remote spatial markers.
-   **Naming Convention:** Scripts are organized by functional domain (`Core`, `Managers`, `Networking`, `UI`).

## 8. Notes, Caveats & Gotchas
-   **Scene Permissions:** QR tracking **will fail** on-device if the `com.oculus.permission.USE_SCENE` permission is not granted. `QrCodeManager` includes a runtime request flow to handle this.
-   **Calibration Order:** The system requires the "Room Anchor" QR code to be scanned before other QR-based objects will be correctly positioned relative to the environment.
-   **Socket.IO Version:** The `SignalingManager` is specifically tuned for **Socket.IO v4** (Engine.IO v4). It uses a hardcoded `40` handshake prefix. Changing the backend version will break the connection.
-   **Lazy Follow Lock:** Users can drag panels to a fixed position. To resume the "Lazy Follow" behavior, a "Resume Follow" action (often a tap on the panel background) must be triggered via `UIManager.ResumeFollow()`.