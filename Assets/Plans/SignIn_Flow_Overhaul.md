# Plan: Sign-In Flow, QR Detection & Demo Fallback Overhaul

## Problems
1. Sign-in handshake hangs partway (no UnityWebRequest timeout; dual state drivers fight and block on RoomAnchor forever).
2. QR codes don't stay visible; after Cancel Scan they're never re-detected.
3. Panels overlap / wrong panel shown across the flow; disconnect doesn't cleanly reset.
4. No graceful failure path and no way to demo offline.

## Steps
1. **Network robustness**: add `request.timeout` to all UnityWebRequest calls so REST never hangs; register wait already has retry — ensure it errors out and surfaces.
2. **Reconcile flow (SessionFlowManager)**: after credentials valid go straight to Session (non-blocking). RoomAnchor discovery still places items but never hides the session. Set InitializationComplete on session entry. Remove infinite calibration wait. Add ResetForNewSession().
3. **Graceful failure + Demo override**: on RegisterAndBoot failure stay on Login, show error, re-enable Scan/Sign In, reveal a Demo button. Add demo button to LoginPanel + wire it.
4. **Demo session (SessionFlowManager.EnterDemoSession)**: establish a fake RoomAnchor in front of the user, spawn fake "detected" QR codes, subscribe to add/remove, enter Session.
5. **QR detection lifecycle (QrCodeManager + SessionUiController)**: re-detect existing trackables on StartQRCodeDetection; keep detection markers visible while detecting; ShowLoginPanel always (re)arms detection; never ignore a detected/stored code.
6. **Disconnect/reset**: OnLeaveSession tears down session, keeps stored creds, resets InitializationComplete, returns to Login with detection re-armed and Sign-In ready.
7. **Verify**: compile clean; audit scene panel states; smoke test.
