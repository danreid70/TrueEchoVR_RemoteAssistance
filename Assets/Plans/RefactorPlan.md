# Refactor Plan for Meta XR Building Blocks

> **Addendum (current QR Sign In):** Provisioning now uses a minimal **bare setup-code QR** resolved via `GET /api/setup/{code}`; the backend URL is stored on-device (default + editable, not in the QR). `QrCodeManager` auto-starts detection with `Off/SignIn/Session` states. See `_SCRIPTS/WebAppManager_Communication_Doc.md`.

## Goals
- Transition core managers to work with Meta XR Building Blocks.
- Remove redundant Unity XR/deprecated components.
- Ensure spatial tracking and UI positioning work correctly with the new Rig.

## Subtasks
1. **Refactor SessionFlowManager.cs**
   - Update `xrOrigin` finding to support the `[BuildingBlock] Camera Rig`.
   - Update `CharacterController` removal logic for new rig architecture.
   - Ensure all event subscriptions use persistent instances.

2. **Refactor QrCodeManager.cs**
   - Ensure robust `MRUK` instance acquisition.
   - Verify QR visualization parenting logic works with the new Rig's tracking space.

3. **Scene Cleanup & Wiring**
   - Remove `META_QUEST3_RIG` (if any part remains).
   - Remove `XR Interaction Manager` and `Input Action Manager`.
   - Re-wire `UIManager` and other managers to the new `CenterEyeAnchor`.
   - Ensure the new Rig's hands and interactors are correctly configured.
