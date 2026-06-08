# Plan: Flow and UI Fix v2

## Problem Statement
The TrueEchoVR project still has flow and UI issues:
1.  **Overlapping HUD Text**: Calibration messages appear during Login.
2.  **Invisible Login Background**: The LoginPanel background is missing.
3.  **Flow Hang**: The app stops after "Loading startup data ...".
4.  **Broken QR Re-scanning**: "Cancel Scan" breaks subsequent detection.
5.  **Scan Reliability**: Codes are sometimes ignored or not shown.

## Implementation Steps

### 1. Fix LoginPanel Background and Layout
- Inspect `LoginPanel` hierarchy and `Background` RectTransform.
- Ensure `Background` uses stretch-anchors (0,0 to 1,1) with 0 offset.
- Ensure the color is not completely transparent and it's the first child.
- Check if `VerticalLayoutGroup` on `LoginPanel` is squashing it.

### 2. Fix HUD Overlap and Visibility
- Update `VrHudController` to be more strict about when to show messages.
- Ensure `SessionFlowManager` doesn't call `ShowMessage` until the state is officially `Calibration`.
- Explicitly hide `HUDPanel` in `ShowLoginPanel`.

### 3. Debug and Fix Signaling Flow
- Trace `ProvisioningSequence` in `SignalingManager.cs`.
- Ensure `EveryBootSequence` always resolves `startupDone` and calls `onComplete`.
- Check for race conditions between `OnStartupDataReceived` and the `onComplete` callback.
- Add more logging to identify the hang point.

### 4. Fix QR Detection Lifecycle
- Verify `StartQRCodeDetection` and `StopQRCodeDetection` in `QrCodeManager.cs`.
- Ensure `EnsureQrTrackingEnabled` is robust and doesn't fail on second call.
- Check `MRUK` subscription logic.

### 5. Improve Setup Code Detection
- Make `HandleLoginQRScan` in `SessionUiController` more persistent.
- Ensure the cyan highlight always appears for detected codes.
- Never ignore valid setup codes in `SignIn` mode.

## Verification
- Audit scene state after modifications.
- Check compilation.
- Verify through code path analysis.
