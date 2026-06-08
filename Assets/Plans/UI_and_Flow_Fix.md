# Plan: UI and Flow Fix

## Problem Statement
The TrueEchoVR project has several regressions in its UI and user flow:
1.  **Broken Scan QR Code logic**: Sign in doesn't work as expected after QR detection.
2.  **Overlapping Panels**: Multiple UI panels are visible simultaneously (Login and Session).
3.  **Broken Navigation Flow**: The transition from Login to Session panel is missing or broken.
4.  **Broken Arrow Logic**: The pointing arrow doesn't rotate correctly or hide when appropriate.
5.  **Broken Panel Dragging/Lazy Follow**: Panels don't follow the user correctly, and the drag-to-fix behavior is broken.
6.  **Readability Issues**: Oversized text, overlapping elements, and white-on-white input fields.

## Implementation Steps

### 1. Fix UI Panel Management and Flow
- Analyze `UIManager.cs` and `SessionUiController.cs`.
- Ensure `UIManager.SetState` correctly toggles `LoginPanel` and `SessionPanel`.
- Verify `SessionFlowManager` or `SessionUiController` calls `UIManager.SetState` after a successful login.

### 2. Fix QR Code Detection and Sign In Logic
- Check `SignalingManager.cs` for setup code resolution.
- Verify `SessionUiController.HandleLoginQRScan` is correctly processing the scanned payload.
- Ensure the "Sign In" button is correctly wired and triggers the registration sequence.

### 3. Fix Panel Dragging and Lazy Follow
- Examine `UIManager.cs` for the lazy-follow implementation.
- Check `UiPanelDragHandler.cs` (or similar) for dragging logic.
- Ensure rotation-to-face-user works during dragging.
- Restore "stay where released" behavior.

### 4. Fix Readability and Sizing
- Correct oversized dynamic text in `LoginPanel` and `HUDPanel`.
- Fix input field styles (text color vs background/border).
- Ensure all text elements use dynamic sizing and proper wrapping.

### 5. Fix Arrow Logic
- Analyze `VrHudController.cs` for arrow rotation and visibility.
- Ensure `HighlightTarget` and `ClearHighlight` are called correctly from `SessionFlowManager`.

## Verification
- Run tests in the Unity Editor (if possible) or verify via code audit.
- Check console logs for any errors during the flow.
