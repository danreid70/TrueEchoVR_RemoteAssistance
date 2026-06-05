# Task List: Scene Cleanup & UI Reorganization

> **Addendum (current Login UI):** The `TEVR_UI_System` Login panel now has an editable **Backend URL** field (default + override, persisted, pre-populated) and a persistent **QR Detection ON/OFF** indicator (also on the Session panel). The Sign In QR is a minimal bare setup code; obsolete read-only API/Room/Token labels were removed. See `_SCRIPTS/WebAppManager_Communication_Doc.md` §6/§8.

## Status Tracking
- [ ] Analysis: Confirm hierarchy and redundant elements. (pending)
- [ ] Analysis: Identify UI scripts and dependencies. (pending)
- [ ] Scene: Move TroubleshootingSessionUI to MainVRHUDUI. (pending)
- [ ] Scene: Rename MainVRHUDUI to TEVR_UI_System. (pending)
- [ ] Scene: Consolidate PointerPrefab instances. (pending)
- [ ] Scene: Remove redundant Join-Panel. (pending)
- [ ] Code: Implement TEVR.UIManager. (pending)
- [ ] Code: Refactor existing UI logic into UIManager. (pending)
- [ ] Styling: Harmonize UI panel visuals. (pending)
- [ ] Documentation: Update root README.md. (pending)
- [ ] Validation: Final project check and console log review. (pending)

## Reasoning & Notes
- Consolidation of UI into a central system improves maintainability and XR positioning logic.
- UIManager will handle XR-specific layout (following camera).
- PointerPrefab consolidation prevents reference ambiguity.
