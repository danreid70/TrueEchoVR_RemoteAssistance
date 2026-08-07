using Meta.XR.MRUtilityKit;
using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using System.Linq;

namespace TEVR
{
    public class QrCodeManager : MonoBehaviour
    {
        public static QrCodeManager Instance { get; private set; }

        [Serializable]
public class QRPayloadAction
        {
            public string matchString;
            public GameObject customPrefab;
            public UnityEvent onPayloadMatched;
        }

        [Header("QR Code Tracking Settings")]
        public string qrRoomAnchorLabel = "RoomAnchor";
        public float positionThreshold = 0.01f;
        public float rotationThreshold = 0.2f;

        [Tooltip("When ON, the RoomAnchor visual (and its spawned prefab) snaps to the live QR pose WHILE the " +
                 "RoomAnchor code is actually being tracked, so it visibly syncs to the real code. When the " +
                 "code is out of view, the persisted Meta spatial anchor holds it drift-free. Turn OFF to keep " +
                 "the spatial anchor fully authoritative (never overridden by the live QR pose).")]
        public bool roomAnchorVisualFollowsLiveQr = true;

        [Tooltip("Tighter position/rotation thresholds applied to the RoomAnchor only, so it updates more " +
                 "responsively than ordinary item codes (the RoomAnchor is the zero-point for everything else).")]
        public float roomAnchorPositionThreshold = 0.004f;
        public float roomAnchorRotationThreshold = 0.1f;

        [Header("Setup Code (smallest Sign In QR payload)")]
        [Tooltip("A bare (non-JSON) alphanumeric QR payload whose length is within this range is treated " +
                 "as a Sign In setup code (Target/green) during the SignIn phase. This lets the web app " +
                 "encode JUST a short code (e.g. 8 chars) so the QR is the least dense possible. The backend " +
                 "URL is NOT carried in the QR — it is stored on the device (default + editable).")]
        public int setupCodeMinLength = 6;
        public int setupCodeMaxLength = 16;

        [Header("Visualization & Prefabs")]
        public List<QRPayloadAction> payloadActions = new List<QRPayloadAction>();

        [Header("Persistence")]
        public bool autoSaveLoad = true;
        public string saveFileName = "QRDetectedData.json";

        [Header("Detection Markers (testing aid)")]
        [Tooltip("Show a small colored pip above every detected QR code. " +
                 "Green = target (RoomAnchor / login setup code), Red = invalid, " +
                 "Blue = in the valid QR list, Orange = unlisted.")]
        public bool showDetectionMarkers = true;

        [Tooltip("World size (in meters) of the detection pip.")]
        public float markerSize = 0.03f;

        [Tooltip("Seconds a detection marker stays fully visible after being detected/loaded before it starts to fade.")]
        public float markerHoldSeconds = 3f;

        [Tooltip("Seconds over which a detection marker fades from visible to invisible (after the hold time).")]
        public float markerFadeSeconds = 1.5f;

        [Tooltip("Resting alpha a detection marker settles to AFTER the fade, instead of vanishing. " +
                 "0 = fade fully out after detection (markers disappear). " +
                 "0.2 = keep markers at 20% opacity so you can confirm a QR code is STILL being tracked " +
                 "after its initial detection. Markers follow the live trackable while detection is active.")]
        [Range(0f, 1f)]
        public float fadeQrDetectionMarkerTransparency = 0.2f;

        [Header("Startup")]
        [Tooltip("Begin QR detection automatically when the app starts (in the SignIn phase), so the " +
                 "headset is immediately looking for the Sign In setup code without the user pressing Scan. " +
                 "Detection markers (status pips) appear for every code seen.")]
        public bool autoStartDetection = true;

        [Header("Performance (scales to 50+ codes)")]
        [Tooltip("Draw the per-code TextMeshPro payload label on the heavy session visual. TMP is the most " +
                 "expensive part per code; turn OFF to track many codes (50+) without frame drops. Status is " +
                 "still shown by the colored detection marker regardless of this setting.")]
        public bool showPayloadLabels = true;

        [Tooltip("Draw the small debug sphere at each code's center. Off by default (it is only a debug aid).")]
        public bool showDebugCenter = false;

        // Detection starts automatically at app launch when autoStartDetection is true (SignIn phase),
        // otherwise it stays off until a phase explicitly starts it.
        public bool IsDetecting { get; private set; } = false;

        /// <summary>
        /// LoginOnly: pre-sign-in. Detected codes only show a (scaled) visual box + raise OnRawQRDetected
        /// for the login-code parse. No RoomAnchor handling and no persistent item instances are created.
        /// Full: post valid sign-in. RoomAnchor is established first, then item codes are synced/persisted.
        /// </summary>
        public enum ScanMode { LoginOnly, Full }
        public ScanMode Mode { get; private set; } = ScanMode.LoginOnly;

        /// <summary>
        /// High-level, UI-facing detection phase derived from IsDetecting + Mode:
        ///   Off     = not scanning.
        ///   SignIn  = pre-sign-in, looking for the setup/login QR code (ScanMode.LoginOnly).
        ///   Session = post-sign-in, RoomAnchor + item tracking (ScanMode.Full).
        /// </summary>
        public enum DetectionState { Off, SignIn, Session }

        /// <summary>Current detection phase. Drives the on-screen "QR Detection ON/OFF" indicator.</summary>
        public DetectionState State => !IsDetecting
            ? DetectionState.Off
            : (Mode == ScanMode.Full ? DetectionState.Session : DetectionState.SignIn);

        /// <summary>Fires whenever the detection phase changes (Off / SignIn / Session) so UI can update.</summary>
        public Action<DetectionState> OnDetectionStateChanged;

        private DetectionState _lastRaisedState = DetectionState.Off;

        private void RaiseDetectionState()
        {
            var s = State;
            if (s == _lastRaisedState) return;
            _lastRaisedState = s;
            OnDetectionStateChanged?.Invoke(s);
        }

        /// <summary>Number of QR codes currently showing a detection marker (status pip) in the scene.</summary>
        public int DetectionMarkerCount => _detectionMarkers.Count;

        /// <summary>Auto-starts detection in the SignIn phase at launch (idempotent).</summary>
        private void MaybeAutoStartDetection()
        {
            if (!autoStartDetection || IsDetecting) return;
            SetScanMode(ScanMode.LoginOnly); // SignIn phase
            StartQRCodeDetection();
            Debug.Log("[QrCodeManager] Auto-started QR detection (SignIn phase).");
        }

        /// <summary>Switches scan phase. Full mode is entered only after a valid sign-in.</summary>
        public void SetScanMode(ScanMode mode)
        {
            Mode = mode;
            Debug.Log("[QrCodeManager] Scan mode -> " + mode);
            RaiseDetectionState();
        }

        /// <summary>
        /// Real-world box scale for a detected QR, taken from its tracked plane rectangle so the
        /// visualization border lines up with the physical code (instead of an arbitrary fixed size).
        /// </summary>
        private static Vector3 GetTrackableBoxScale(MRUKTrackable t, float thickness = 0.005f, float fallback = 0.1f)
        {
            if (t != null && t.PlaneRect.HasValue)
            {
                Vector2 s = t.PlaneRect.Value.size;
                if (Mathf.Abs(s.x) > 0.001f && Mathf.Abs(s.y) > 0.001f)
                    return new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), thickness);
            }
            return new Vector3(fallback, fallback, thickness);
        }

        private bool _isSubscribedToMRUK = false;

        public enum QRStatus { Official, Unknown }

        public class QRCodeInstance
        {
            public GameObject visualObject;
            public MRUKTrackable trackable;
            public string fullPayload;
            public string identifierKey;
            public Vector3 lastPosition;
            public Quaternion lastRotation;
            // RAW trackable rotation last applied (UNflipped). Used for the movement threshold compare —
            // lastRotation stores the 180°-flipped DISPLAY rotation, so comparing it against the raw live
            // rotation always read ~180° and defeated the threshold.
            public Quaternion lastTrackableRotation;
            public QRStatus status = QRStatus.Unknown;
        }

        public Action<QRCodeInstance> OnRoomAnchorDiscovered;
        public Action<QRCodeInstance> OnQRCodeAdded;
        public Action<QRCodeInstance> OnQRCodeUpdated;
        public Action<string> OnQRCodeRemoved;

        /// <summary>
        /// Fires for EVERY detected QR trackable (payload, worldPosition, worldRotation),
        /// regardless of whether a RoomAnchor exists yet. Use this for login/setup-code scanning,
        /// which happens before calibration (when QR codes would otherwise be dormant and silent).
        /// </summary>
        public Action<string, Vector3, Quaternion> OnRawQRDetected;

        private readonly Dictionary<string, QRCodeInstance> _trackedQRCodes = new Dictionary<string, QRCodeInstance>();
        public IReadOnlyDictionary<string, QRCodeInstance> TrackedQRCodes => _trackedQRCodes;

        public QRCodeInstance RoomAnchorInstance { get; private set; }
        private bool _isAnchorSet => RoomAnchorInstance != null;
        private List<CalibrationQRData> _dormantQRCodes = new List<CalibrationQRData>();

        // ---- Meta Spatial Anchor (RoomAnchor persistence / auto-relocalization) ----
        // HYBRID upgrade: the RoomAnchor zero-point is backed by a Meta OVRSpatialAnchor so it is
        // drift-free and relocalizes automatically next launch (no QR re-scan). Item poses are STILL
        // stored relative to RoomAnchorInstance.visualObject exactly as before, so the backend coordinate
        // sync (REST + StartupData) is completely unchanged. This is single-headset persistence only
        // (no Shared Spatial Anchors); the create/load/erase logic is isolated behind the methods below
        // so a future Shared-Spatial-Anchor option can be added at that single boundary.
        [Header("Spatial Anchor (Meta) — RoomAnchor persistence")]
        [Tooltip("Back the RoomAnchor zero-point with a Meta OVRSpatialAnchor so it is drift-free and " +
                 "relocalizes automatically on the next launch (no need to re-scan the RoomAnchor QR). " +
                 "Item QR positions are still stored relative to the RoomAnchor and synced to the backend " +
                 "exactly as before. Falls back to the plain-GameObject QR path in the Editor / when the " +
                 "device does not support spatial anchors.")]
        public bool useSpatialAnchor = true;

        private const string RoomAnchorUuidPrefKey = "tevr_roomAnchorUuid";
        private const string RoomAnchorPayloadPrefKey = "tevr_roomAnchorPayload";

        // The live spatial anchor backing the RoomAnchor zero-point (null = using plain-GameObject fallback).
        private OVRSpatialAnchor _roomSpatialAnchor;
        // True once the RoomAnchor transform is (or is about to be) driven by the spatial anchor. While set,
        // the live QR-follow update is skipped for the RoomAnchor so the anchor stays authoritative (drift-free).
        private bool _roomAnchorDrivenBySpatialAnchor;
        private bool _spatialAnchorBusy;                 // guards against overlapping create/save
        private bool _spatialAnchorRelocalizeAttempted;  // relocalize-on-start runs at most once
        // When relocalizing on start, the disk RoomAnchor is deferred (kept as a fallback) so the
        // spatial anchor can be the authority; restored only if relocalization fails.
        private bool _deferRoomAnchorToSpatialAnchor;
        private SerializableQRData _deferredDiskAnchor;

        /// <summary>
        /// Whether device-only Meta spatial-anchor APIs should be used. False in the Editor (XR Simulator)
        /// so play mode never calls device-only APIs — the existing plain-GameObject QR path is used instead.
        /// </summary>
        private bool SpatialAnchorsSupported
        {
            get
            {
#if UNITY_EDITOR
                return false;
#else
                return useSpatialAnchor;
#endif
            }
        }

        private bool HasStoredRoomAnchorUuid()
            => Guid.TryParse(PlayerPrefs.GetString(RoomAnchorUuidPrefKey, ""), out _);

        // QR marker payloads are frequently NOT decoded on the same frame MRUK raises TrackableAdded;
        // the string arrives a few frames later. We defer such trackables here and re-read the payload
        // in Update() so we never process a QR with an empty payload. (This was the cause of "it read
        // once, then never again": detection only succeeded when the payload happened to be ready on
        // the very first frame, and a tracked code never re-fires TrackableAdded.)
        private readonly Dictionary<MRUKTrackable, float> _pendingPayloadTrackables = new Dictionary<MRUKTrackable, float>();
        private const float PendingPayloadTimeoutSeconds = 30f;

        // Trackables we have deliberately decided to ignore for the rest of this session (the Sign-In/setup
        // code is not a room item). Without this, ReconcileTrackables re-runs OnTrackableAdded for them every
        // interval (because they never enter _trackedQRCodes), re-raising OnRawQRDetected -> chat spam + wasted
        // detection. Cleared on scan-mode change / clear so the code is findable again when signed out.
        private readonly HashSet<MRUKTrackable> _ignoredSessionTrackables = new HashSet<MRUKTrackable>();

        // PERF: SaveToDisk() pretty-prints JSON and does a synchronous File.WriteAllText on the main thread.
        // Calling it on every QR add/update caused O(n^2) blocking writes as codes appeared (a cause of the
        // hitching). We now flag the data dirty and flush at most once per saveDebounceSeconds from Update().
        [Tooltip("Minimum seconds between debounced QR auto-saves to disk (avoids per-frame blocking writes).")]
        public float saveDebounceSeconds = 2f;
        private bool _saveDirty;
        private float _nextSaveTime;

        /// <summary>Marks the QR data dirty for a debounced (coalesced) disk write. Honors autoSaveLoad.</summary>
        private void RequestSave()
        {
            if (!autoSaveLoad) return;
            _saveDirty = true;
        }

        /// <summary>Flushes a pending debounced save when its interval has elapsed. Called from Update().</summary>
        private void FlushPendingSave()
        {
            if (!_saveDirty || Time.time < _nextSaveTime) return;
            _saveDirty = false;
            _nextSaveTime = Time.time + Mathf.Max(0.1f, saveDebounceSeconds);
            SaveToDisk();
        }

        // PERF: per-code visuals (background + 4 border bars + optional sphere) previously allocated a
        // NEW Material via Shader.Find() PER BAR. At 50+ codes that is hundreds of material instances and
        // Shader.Find calls (no batching, leaked materials). Cache the shader once and share one material
        // per (color, opaque/transparent) so identical visuals batch and nothing leaks.
        private Shader _cachedUnlitShader;
        private Shader UnlitShader
        {
            get
            {
                if (_cachedUnlitShader == null)
                {
                    _cachedUnlitShader = Shader.Find("Universal Render Pipeline/Unlit");
                    if (_cachedUnlitShader == null) _cachedUnlitShader = Shader.Find("Unlit/Color");
                    if (_cachedUnlitShader == null) _cachedUnlitShader = Shader.Find("Sprites/Default");
                }
                return _cachedUnlitShader;
            }
        }
        private readonly Dictionary<string, Material> _sharedVisualMats = new Dictionary<string, Material>();

        private Material GetSharedVisualMaterial(Color c, bool transparent)
        {
            string key = (transparent ? "t_" : "o_") + ColorUtility.ToHtmlStringRGBA(c);
            if (_sharedVisualMats.TryGetValue(key, out var existing) && existing != null) return existing;
            var m = new Material(UnlitShader) { name = "QRVisual_" + key };
            if (transparent) { ConfigureTransparent(m, c); m.renderQueue = 3000; }
            else { m.color = c; if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c); }
            _sharedVisualMats[key] = m;
            return m;
        }

        public void SetAnchorEstablished(bool established)
        {
            if (established && _isAnchorSet) ActivateDormantQRCodes();
        }

        private void ActivateDormantQRCodes()
        {
            if (!_isAnchorSet) return;

            // Legacy dormant entries (codes seen before the anchor) become known-pose DATA — no markers.
            if (_dormantQRCodes.Count > 0)
            {
                var list = new List<CalibrationQRData>(_dormantQRCodes);
                _dormantQRCodes.Clear();
                foreach (var d in list)
                {
                    if (string.IsNullOrEmpty(d.qrValue) || IsSystemCode(d.qrValue)) continue;
                    _knownPoses[GetIdentifierKey(d.qrValue)] = new KnownPose
                    {
                        payload = d.qrValue,
                        name = GetPayloadName(d.qrValue),
                        localPosition = d.position,
                        localRotation = d.rotation
                    };
                }
            }

            // Re-parent any already-detected item markers so their local (RoomAnchor-relative) pose resolves.
            ReparentDetectedItemsUnderAnchor();
        }

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else { Destroy(gameObject); return; }
        }

        // Meta Quest scene permission required for QR-code / trackable detection.
        public const string ScenePermission = "com.oculus.permission.USE_SCENE";

        /// <summary>
        /// LIVE check of the runtime scene permission (always true in the Editor).
        /// This must NOT be cached: OVRManager (requestScenePermissionOnStartup) may grant it via its own
        /// dialog without our callback firing, so we always query the OS for the current state.
        /// </summary>
        public bool HasScenePermission
        {
#if UNITY_EDITOR
            get => true;
#elif UNITY_ANDROID
            get => UnityEngine.Android.Permission.HasUserAuthorizedPermission(ScenePermission);
#else
            get => true;
#endif
        }

        /// <summary>Fires with the result of the scene-permission request (true = granted).</summary>
        public Action<bool> OnScenePermissionResult;

        private void Start()
        {
            // If the RoomAnchor is persisted as a Meta spatial anchor (and supported), defer the disk-based
            // RoomAnchor restore so the relocalized spatial anchor becomes the authoritative, drift-free
            // zero-point. The disk anchor is only used as a fallback if relocalization fails.
            _deferRoomAnchorToSpatialAnchor = SpatialAnchorsSupported && HasStoredRoomAnchorUuid();

            if (autoSaveLoad) LoadFromDiskAndRestore();

            // Re-establish the RoomAnchor from its stored Meta spatial anchor (no QR re-scan required).
            // No-op in the Editor / when unsupported / when no UUID is stored — the QR-scan path remains.
            TryRelocalizeRoomSpatialAnchorOnStart();

            // CRITICAL: Quest QR / trackable detection needs the scene permission granted at RUNTIME.
            // Declaring it in the AndroidManifest is not enough — without this request MRUK receives
            // zero QR trackables (this is why scanning never worked on device).
            RequestScenePermissionIfNeeded();

            // Register if MRUK is already available
            if (MRUK.Instance != null)
            {
                RegisterWithMRUK();
            }
            
            // Managers in the Bootstrap scene should persist
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Bootstrap")
            {
                DontDestroyOnLoad(gameObject);
            }
        }

        /// <summary>Public entry point so the UI can (re)trigger the scene-permission request on demand.</summary>
        public void RequestScenePermissionPublic() => RequestScenePermissionIfNeeded();

        private void RequestScenePermissionIfNeeded()
        {
#if UNITY_EDITOR
            // Editor / Link: no Android runtime permission. (For Link, enable "Spatial Data over Meta Quest Link" in the Link app.)
            EnsureQrTrackingEnabled();
            OnScenePermissionResult?.Invoke(true);
#elif UNITY_ANDROID
            if (HasScenePermission)
            {
                EnsureQrTrackingEnabled();
                OnScenePermissionResult?.Invoke(true);
                return;
            }

            Debug.Log("[QrCodeManager] Requesting scene permission for QR tracking: " + ScenePermission);
            var callbacks = new UnityEngine.Android.PermissionCallbacks();
            callbacks.PermissionGranted += p =>
            {
                Debug.Log("[QrCodeManager] Scene permission GRANTED: " + p);
                EnsureQrTrackingEnabled();
                MaybeAutoStartDetection();
                OnScenePermissionResult?.Invoke(true);
            };
            callbacks.PermissionDenied += p =>
            {
                Debug.LogError("[QrCodeManager] Scene permission DENIED: " + p + " — QR scanning will not work.");
                OnScenePermissionResult?.Invoke(false);
            };
            UnityEngine.Android.Permission.RequestUserPermission(ScenePermission, callbacks);
#else
            EnsureQrTrackingEnabled();
            OnScenePermissionResult?.Invoke(true);
#endif
        }

        /// <summary>
        /// (Re)applies the MRUK tracker configuration so QR-code tracking actually starts.
        /// Re-applying after the permission grant is what kicks tracking into life.
        /// </summary>
        public void EnsureQrTrackingEnabled()
        {
            if (MRUK.Instance == null) return;
            try
            {
                var config = MRUK.Instance.SceneSettings.TrackerConfiguration;
                config.QRCodeTrackingEnabled = true;
                MRUK.Instance.SceneSettings.TrackerConfiguration = config;
                Debug.Log("[QrCodeManager] QR code tracking configuration applied.");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[QrCodeManager] Could not apply QR tracker config yet: " + e.Message);
            }
        }

        private void Update()
        {
            // Robust check for MRUK initialization (useful if Building Blocks defer it)
            if (!_isSubscribedToMRUK && MRUK.Instance != null)
            {
                RegisterWithMRUK();
            }

            // Focus glow must keep tracking its target even while detection is paused.
            UpdateFocusGlow();

            // Flush any pending debounced disk save regardless of detection state.
            FlushPendingSave();

            if (!IsDetecting) return;

            RetryPendingPayloadTrackables();
            EnsureTrackingPeriodically();
            ReconcileTrackables();
            UpdateDetectionMarkers();

            foreach (var inst in _trackedQRCodes.Values)
            {
                bool isAnchor = inst == RoomAnchorInstance;

                // The spatial-anchored RoomAnchor is normally held drift-free by the OVRSpatialAnchor. We
                // still let its visual snap to the live QR pose WHILE the RoomAnchor code is actively being
                // tracked (so the user sees it sync to the real code); when the code is out of view, the
                // spatial anchor keeps it stable. Set roomAnchorVisualFollowsLiveQr = false to keep the
                // spatial anchor fully authoritative.
                bool anchorTrackedNow = isAnchor && inst.trackable != null && inst.trackable.IsTracked;
                if (isAnchor && _roomAnchorDrivenBySpatialAnchor &&
                    !(roomAnchorVisualFollowsLiveQr && anchorTrackedNow))
                    continue;

                if (inst.trackable != null && inst.visualObject != null)
                {
                    // Only follow while the code is actually being tracked; an untracked trackable can hold
                    // a stale/invalid pose, and snapping the marker to it makes codes appear to "jump".
                    if (!inst.trackable.IsTracked) continue;

                    Vector3 tPos = inst.trackable.transform.position;
                    Quaternion tRot = inst.trackable.transform.rotation;
                    // The RoomAnchor uses tighter thresholds so it tracks more responsively than items.
                    float posThresh = isAnchor ? roomAnchorPositionThreshold : positionThreshold;
                    float rotThresh = isAnchor ? roomAnchorRotationThreshold : rotationThreshold;
                    // Compare against the RAW last rotation (not the flipped display rotation).
                    if (Vector3.Distance(inst.lastPosition, tPos) > posThresh ||
                        Quaternion.Angle(inst.lastTrackableRotation, tRot) > rotThresh)
                    {
                        Quaternion cRot = tRot * Quaternion.Euler(0, 180, 0);
                        inst.visualObject.transform.SetPositionAndRotation(tPos, cRot);
                        inst.lastPosition = tPos;
                        inst.lastRotation = cRot;
                        inst.lastTrackableRotation = tRot;
                        OnQRCodeUpdated?.Invoke(inst);
                    }
                }
            }
        }

        /// <summary>
        /// Re-reads marker payloads for trackables that were added before their QR string was decoded.
        /// Once a payload is available we run the normal add path; entries that never resolve a string
        /// payload within the timeout are dropped so we don't poll forever.
        /// </summary>
        private void RetryPendingPayloadTrackables()
        {
            if (_pendingPayloadTrackables.Count == 0) return;

            List<MRUKTrackable> ready = null;
            List<MRUKTrackable> expired = null;

            foreach (var kvp in _pendingPayloadTrackables)
            {
                MRUKTrackable t = kvp.Key;
                if (t == null)
                {
                    (expired ??= new List<MRUKTrackable>()).Add(kvp.Key);
                }
                else if (!string.IsNullOrEmpty(t.MarkerPayloadString))
                {
                    (ready ??= new List<MRUKTrackable>()).Add(t);
                }
                else if (Time.time - kvp.Value > PendingPayloadTimeoutSeconds)
                {
                    (expired ??= new List<MRUKTrackable>()).Add(t);
                    Debug.LogWarning("[QrCodeManager] QR payload never decoded within timeout; dropping pending trackable.");
                }
            }

            if (expired != null)
                foreach (var t in expired) _pendingPayloadTrackables.Remove(t);

            if (ready != null)
                foreach (var t in ready)
                {
                    _pendingPayloadTrackables.Remove(t);
                    OnTrackableAdded(t); // payload is ready now; runs the full add path
                }
        }

        private void RegisterWithMRUK()
        {
            if (_isSubscribedToMRUK || MRUK.Instance == null) return;
            
            MRUK.Instance.SceneSettings.TrackableAdded.AddListener(OnTrackableAdded);
            MRUK.Instance.SceneSettings.TrackableRemoved.AddListener(OnTrackableRemoved);
            _isSubscribedToMRUK = true;
            Debug.Log("[QrCodeManager] Successfully registered with MRUK SceneSettings listeners.");

            // If permission was already granted before MRUK came online, make sure tracking is active now.
            if (HasScenePermission) EnsureQrTrackingEnabled();
        }

        public void ClearQRCodes()
        {
            // Any pulsing focus glow is following a code we are about to destroy — kill it first so it
            // doesn't linger pointing at nothing.
            ClearFocus();

            foreach (var inst in _trackedQRCodes.Values)
            {
                if (inst.visualObject != null) Destroy(inst.visualObject);
            }
            _trackedQRCodes.Clear();
            _knownPoses.Clear();
            RoomAnchorInstance = null;
            // The RoomAnchor visual (with its OVRSpatialAnchor component) was destroyed above; drop the stale
            // runtime reference. The persisted UUID is intentionally left so it can relocalize next launch
            // (use ClearRoomSpatialAnchor() to erase persistence for re-calibration).
            _roomSpatialAnchor = null;
            _roomAnchorDrivenBySpatialAnchor = false;
            RequestSave();
        }

        /// <summary>
        /// FULL user-initiated reset (the "Clear" button). Removes the focus glow, every detection pip,
        /// all tracked code visuals, known/dormant poses, AND the server-provided "legit" payload + name
        /// lists so the dropdown/merged list empties completely. A subsequent Pull (or startup-data) will
        /// repopulate the lists from the server. This does NOT delete anything server-side.
        /// </summary>
        public void ClearAllUserData()
        {
            ClearFocus();
            ClearDetectionMarkers();   // pips + pending-payload retries (+ ClearFocus again, harmless)

            foreach (var inst in _trackedQRCodes.Values)
                if (inst.visualObject != null) Destroy(inst.visualObject);
            _trackedQRCodes.Clear();
            _knownPoses.Clear();
            _dormantQRCodes.Clear();
            _validPayloads.Clear();
            _payloadNames.Clear();

            RoomAnchorInstance = null;
            _roomSpatialAnchor = null;
            _roomAnchorDrivenBySpatialAnchor = false;
            RequestSave();
        }

        /// <summary>
        /// Removes every transient detection frame currently in the scene (the colored boxes drawn
        /// over detected codes) and clears any focus glow. Used by "Cancel Scan" so the login-phase
        /// visual references don't linger as clutter. Does not touch persisted RoomAnchor/item data.
        /// </summary>
        public void ClearDetectionMarkers()
        {
            foreach (var kvp in _detectionMarkers)
                if (kvp.Value != null && kvp.Value.go != null) Destroy(kvp.Value.go);
            _detectionMarkers.Clear();
            _pendingPayloadTrackables.Clear();
            ClearFocus();
        }

        /// <summary>
        /// Thoroughly removes ALL QR-related visuals from the scene, including detection pips,
        /// persistent item visuals, and dormant codes. Used when the user explicitly cancels
        /// a scan or wants to reset the room view.
        /// </summary>
        public void ClearAllVisuals()
        {
            ClearDetectionMarkers();
            foreach (var inst in _trackedQRCodes.Values)
            {
                if (inst.visualObject != null) Destroy(inst.visualObject);
            }
            _trackedQRCodes.Clear();
            _dormantQRCodes.Clear();
            _knownPoses.Clear();
            RoomAnchorInstance = null;
            // Drop the stale spatial-anchor reference (its GameObject was destroyed above). Persistence is
            // intentionally preserved; call ClearRoomSpatialAnchor() to erase it for re-calibration.
            _roomSpatialAnchor = null;
            _roomAnchorDrivenBySpatialAnchor = false;
            RequestSave();
        }

        /// <summary>
        /// Computes the RoomAnchor-RELATIVE pose of a detected code's current world pose. This is the
        /// canonical coordinate frame for the backend contract (so any headset/the web dashboard can place
        /// the code regardless of per-session tracking origin). Returns false when there is no RoomAnchor
        /// (the relative frame is undefined) or the instance has no visual.
        /// </summary>
        public bool TryGetAnchorRelativePose(QRCodeInstance inst, out Vector3 localPos, out Quaternion localRot)
        {
            localPos = Vector3.zero; localRot = Quaternion.identity;
            if (inst == null || inst.visualObject == null) return false;
            if (RoomAnchorInstance == null || RoomAnchorInstance.visualObject == null) return false;
            var a = RoomAnchorInstance.visualObject.transform;
            localPos = a.InverseTransformPoint(inst.visualObject.transform.position);
            localRot = Quaternion.Inverse(a.rotation) * inst.visualObject.transform.rotation;
            return true;
        }

        /// <summary>
        /// Builds the canonical upload list: RoomAnchor in WORLD pose, every item in RoomAnchor-RELATIVE pose,
        /// plus known-but-not-currently-detected items. Items are skipped (not uploaded in the wrong frame)
        /// when no RoomAnchor exists. Shared by the bulk and per-item upload paths so both are identical.
        /// </summary>
        private List<CalibrationQRData> BuildUploadList(out int skippedNoAnchor)
        {
            var list = new List<CalibrationQRData>();
            var seen = new HashSet<string>();
            skippedNoAnchor = 0;

            foreach (var inst in _trackedQRCodes.Values)
            {
                if (string.IsNullOrEmpty(inst.fullPayload) || IsSignInCode(inst.fullPayload)) continue;

                if (inst == RoomAnchorInstance)
                {
                    list.Add(new CalibrationQRData { qrValue = inst.fullPayload, position = inst.lastPosition, rotation = inst.lastRotation });
                    seen.Add(inst.identifierKey);
                    continue;
                }

                if (TryGetAnchorRelativePose(inst, out var rp, out var rr))
                {
                    list.Add(new CalibrationQRData { qrValue = inst.fullPayload, position = rp, rotation = rr });
                    seen.Add(inst.identifierKey);
                }
                else
                {
                    skippedNoAnchor++;
                }
            }

            foreach (var kp in _knownPoses.Values)
            {
                if (string.IsNullOrEmpty(kp.payload) || IsSystemCode(kp.payload)) continue;
                string key = GetIdentifierKey(kp.payload);
                if (seen.Contains(key)) continue;
                list.Add(new CalibrationQRData { qrValue = kp.payload, position = kp.localPosition, rotation = kp.localRotation });
                seen.Add(key);
            }

            return list;
        }

        /// <summary>Reports how many codes would actually be uploaded (and how many detected items were
        /// skipped because no RoomAnchor is set), so the UI can report the TRUTH instead of TrackedQRCodes.Count
        /// (which includes the anchor/sign-in code and ignores the relative-frame requirement).</summary>
        public int GetUploadableCount(out int skippedNoAnchor)
        {
            return BuildUploadList(out skippedNoAnchor).Count;
        }

        /// <summary>Bulk upload JSON: a single CalibrationWrapper with ALL codes in one qrCodes array.</summary>
        public string GetQRCodeDataAsJson(string headsetId)
        {
            var list = BuildUploadList(out int skippedNoAnchor);
            if (skippedNoAnchor > 0)
                Debug.LogWarning($"[QrCodeManager] Push: skipped {skippedNoAnchor} detected item(s) because no RoomAnchor is set — scan the Room Anchor first so item poses can be expressed relative to it.");
            return JsonUtility.ToJson(new CalibrationWrapper { headsetId = headsetId, qrCodes = list });
        }

        /// <summary>
        /// Per-item upload JSON for the SEQUENTIAL fallback: one CalibrationWrapper per code, each carrying a
        /// single-element qrCodes array. Same shape as the bulk upload, so a backend that iterates qrCodes
        /// accepts either. Used when the bulk POST fails (or a backend that only registers one code per call).
        /// </summary>
        public List<string> GetQRCodeDataAsIndividualJson(string headsetId)
        {
            var list = BuildUploadList(out _);
            var result = new List<string>(list.Count);
            foreach (var item in list)
            {
                var single = new CalibrationWrapper { headsetId = headsetId, qrCodes = new List<CalibrationQRData> { item } };
                result.Add(JsonUtility.ToJson(single));
            }
            return result;
        }

        /// <summary>
        /// Resolves a remote "point-to"/"look-at" command to a locally-tracked QR code. Matches by exact QR
        /// payload (qrValue) first — the most reliable identity, since the dictionary is keyed by the payload —
        /// then by identifierKey/payload equality, then by a payload substring (friendly-name fallback).
        /// Returns null when no locally-represented code matches.
        /// </summary>
        public QRCodeInstance FindTrackedQRCode(string qrValue, string name)
        {
            // RoomAnchor (pure reference) and Sign-In codes are never pointable items.
            bool Pointable(QRCodeInstance i) => i != null && i != RoomAnchorInstance && !IsSystemCode(i.fullPayload);

            // 1) Exact payload value (canonical lookup: the dictionary key is the truncated payload).
            if (!string.IsNullOrEmpty(qrValue))
            {
                if (_trackedQRCodes.TryGetValue(GetIdentifierKey(qrValue), out var byKey) && Pointable(byKey)) return byKey;
                foreach (var inst in _trackedQRCodes.Values)
                    if (Pointable(inst) && inst.fullPayload == qrValue) return inst;
            }
            // 2) Friendly name / identifier-key equality, then a payload substring fallback.
            if (!string.IsNullOrEmpty(name))
            {
                foreach (var inst in _trackedQRCodes.Values)
                    if (Pointable(inst) && (inst.identifierKey == name || inst.fullPayload == name)) return inst;
                foreach (var inst in _trackedQRCodes.Values)
                    if (Pointable(inst) && !string.IsNullOrEmpty(inst.fullPayload) && inst.fullPayload.Contains(name)) return inst;
            }
            return null;
        }

        /// <summary>
        /// Applies a RoomAnchor-relative pose known from the server (StartupData / pulled calibration) or
        /// from disk. The RoomAnchor is (re)established as a real visual (needed for parenting + the Meta
        /// spatial anchor). Every other code is recorded as KNOWN-POSE DATA ONLY — no scene marker is
        /// created, because markers exist solely for physically-detected codes (the "no stray markers"
        /// rule). If the code is already physically detected, the live detection stays authoritative for
        /// its marker; we just refresh the stored pose. Sign-In/setup codes are never items and are ignored.
        /// </summary>
        public void UpdateQRCodeFromRemote(string payload, Vector3 pos, Quaternion rot)
        {
            if (string.IsNullOrEmpty(payload)) return;
            if (IsSignInCode(payload)) return; // setup/login code is never an item

            string key = GetIdentifierKey(payload);

            if (IsRoomAnchorPayload(payload))
            {
                if (_trackedQRCodes.TryGetValue(key, out QRCodeInstance existingAnchor))
                {
                    existingAnchor.lastPosition = pos;
                    existingAnchor.lastRotation = rot;
                    if (existingAnchor.visualObject != null)
                        existingAnchor.visualObject.transform.SetPositionAndRotation(pos, rot);
                    RoomAnchorInstance = existingAnchor;
                }
                else
                {
                    RoomAnchorInstance = CreateAndAddInstance(payload, pos, rot, QRStatus.Official, new Vector3(0.15f, 0.15f, 0.005f), true);
                }
                // The anchor now exists: drain any legacy dormant entries to known poses and re-parent
                // already-detected item markers so their RoomAnchor-relative (local) pose is computed.
                ActivateDormantQRCodes();
                RequestSave();
                return;
            }

            // Non-anchor item: store the RoomAnchor-relative pose as known DATA (no marker). If it is
            // currently detected, the live world pose remains the source of truth for the marker.
            _knownPoses[key] = new KnownPose
            {
                payload = payload,
                name = GetPayloadName(payload),
                localPosition = pos,
                localRotation = rot
            };
            RequestSave();
        }

        /// <summary>
        /// Re-parents every currently-detected item marker under the RoomAnchor (keeping its world pose),
        /// so each marker's localPosition/localRotation become the RoomAnchor-relative pose used for the
        /// backend sync. World pose (detection truth / Meta storage) is preserved; only the parent changes.
        /// </summary>
        private void ReparentDetectedItemsUnderAnchor()
        {
            if (RoomAnchorInstance == null || RoomAnchorInstance.visualObject == null) return;
            var anchorT = RoomAnchorInstance.visualObject.transform;
            foreach (var inst in _trackedQRCodes.Values)
            {
                if (inst == RoomAnchorInstance || inst.visualObject == null) continue;
                if (inst.visualObject.transform.parent != anchorT)
                    inst.visualObject.transform.SetParent(anchorT, true); // worldPositionStays = true
            }
        }

        public void OnTrackableAdded(MRUKTrackable trackable)
        {
            if (!IsDetecting || trackable == null || trackable.TrackableType != OVRAnchor.TrackableType.QRCode) return;

            string fullPayload = trackable.MarkerPayloadString ?? "";

            // The QR string is often not decoded yet on the frame the trackable is first added; MRUK
            // fills it in a few frames later. Defer until it is available (polled in Update) instead of
            // processing an empty payload — otherwise the code is added with a null key and the real
            // payload is never read (TrackableAdded does not fire again for an already-tracked code).
            if (string.IsNullOrEmpty(fullPayload))
            {
                if (!_pendingPayloadTrackables.ContainsKey(trackable))
                {
                    _pendingPayloadTrackables[trackable] = Time.time;
                    Debug.Log("[QrCodeManager] QR trackable added but payload not decoded yet — deferring until ready.");
                }
                return;
            }
            _pendingPayloadTrackables.Remove(trackable);

            // In a SESSION, the Sign-In/setup code is NOT a room item and must not appear at all — no pip,
            // no instance, and crucially NO raw-detection event (that would spam the session chat and waste
            // detection every reconcile interval). Remember it so ReconcileTrackables skips it from now on.
            // (During the SignIn phase it still shows its green pip and drives the login flow below.)
            if (Mode == ScanMode.Full && IsSignInCode(fullPayload))
            {
                RemoveDetectionMarker(trackable);
                _ignoredSessionTrackables.Add(trackable);
                return;
            }

            // Always announce the raw detection (even for codes that will go dormant before calibration).
            // This is what drives the login/setup-code scan + on-screen detection feedback.
            OnRawQRDetected?.Invoke(fullPayload, trackable.transform.position, trackable.transform.rotation);

            // Testing aid: drop/refresh the colored 4-category pip over this code.
            // Sized correctly to the physical QR code bounds.
            CreateOrUpdateDetectionMarker(trackable, fullPayload);

            // In LoginOnly mode, we stop here. We only want visual pips and raw detection events.
            // We do NOT want to establish a RoomAnchor or create persistent item instances yet.
            if (Mode == ScanMode.LoginOnly)
            {
                Debug.Log($"[QrCodeManager] [LoginOnly] Raw detection: {fullPayload}");
                return;
            }

            string key = GetIdentifierKey(fullPayload);
            bool isAnchor = fullPayload.Contains(qrRoomAnchorLabel);

            Debug.Log($"[QrCodeManager] QRCode detected. Payload=\"{fullPayload}\"");

            if (_trackedQRCodes.TryGetValue(key, out QRCodeInstance existing))
            {
                existing.trackable = trackable;
                // When the RoomAnchor is backed by a Meta spatial anchor, the anchor is normally
                // authoritative. But if roomAnchorVisualFollowsLiveQr is on, a fresh re-detection should snap
                // the visual to the real code (the user is looking right at it) so it visibly syncs.
                bool anchorPinned = isAnchor && _roomAnchorDrivenBySpatialAnchor && !roomAnchorVisualFollowsLiveQr;
                if (existing.visualObject != null && !anchorPinned)
                {
                    Quaternion cRot = trackable.transform.rotation * Quaternion.Euler(0, 180, 0);
                    existing.visualObject.transform.SetPositionAndRotation(trackable.transform.position, cRot);
                    existing.lastPosition = trackable.transform.position;
                    existing.lastRotation = cRot;
                    existing.lastTrackableRotation = trackable.transform.rotation;
                    UpdateTextOnObject(existing.visualObject, fullPayload);
                }
                if (isAnchor)
                {
                    RoomAnchorInstance = existing;
                    OnRoomAnchorDiscovered?.Invoke(existing);
                    ActivateDormantQRCodes();
                    TryPersistRoomAnchorAsSpatialAnchor(); // persists once; no-op if already anchored
                }
                OnQRCodeUpdated?.Invoke(existing);
                return;
            }

            if (isAnchor)
            {
                // FIX: Use GetTrackableBoxScale instead of transform.localScale (which is usually (1,1,1))
                // so the green anchor box hits the border of the detected QR code.
                RoomAnchorInstance = CreateAndAddInstance(fullPayload, trackable.transform.position, trackable.transform.rotation, QRStatus.Official, GetTrackableBoxScale(trackable), true);
                RoomAnchorInstance.trackable = trackable;
                OnRoomAnchorDiscovered?.Invoke(RoomAnchorInstance);
                ActivateDormantQRCodes();
                // HYBRID: back this fresh RoomAnchor with a persisted Meta spatial anchor (drift-free +
                // auto-relocalize next launch). No-op in Editor / when unsupported. Backend sync unaffected.
                TryPersistRoomAnchorAsSpatialAnchor();
            }
            else
            {
                // Physically-detected item: ALWAYS create a marker at the DETECTED WORLD pose (the source
                // of truth for detection + Meta storage). If a RoomAnchor exists the visual is parented to
                // it so its local (RoomAnchor-relative) pose is computed for the backend sync; if not, it
                // sits in world space until the anchor is found (ReparentDetectedItemsUnderAnchor).
                var inst = CreateAndAddInstance(fullPayload, trackable.transform.position, trackable.transform.rotation,
                    IsValidListed(fullPayload) ? QRStatus.Official : QRStatus.Unknown, GetTrackableBoxScale(trackable), true);
                inst.trackable = trackable;
                // It is now physically detected — drop any "known but not detected" data for it.
                _knownPoses.Remove(key);
            }
            RequestSave();
        }

        public void OnTrackableRemoved(MRUKTrackable trackable)
        {
            if (!IsDetecting || trackable == null || trackable.TrackableType != OVRAnchor.TrackableType.QRCode) return;
            _pendingPayloadTrackables.Remove(trackable);
            RemoveDetectionMarker(trackable);
            if (_focusTrackable == trackable) ClearFocus();
            
            // For removal, we need to match the key if possible, but MRUK doesn't give us the payload on removal.
            // We'll search by trackable reference.
            QRCodeInstance instanceToRemove = null;
            string keyToRemove = null;
            foreach (var kvp in _trackedQRCodes)
            {
                if (kvp.Value.trackable == trackable)
                {
                    keyToRemove = kvp.Key;
                    instanceToRemove = kvp.Value;
                    break;
                }
            }

            if (instanceToRemove != null)
            {
                if (instanceToRemove.visualObject != null) Destroy(instanceToRemove.visualObject);
                _trackedQRCodes.Remove(keyToRemove);
                OnQRCodeRemoved?.Invoke(keyToRemove);
                RequestSave();
            }
        }

        private QRCodeInstance CreateAndAddInstance(string payload, Vector3 pos, Quaternion rot, QRStatus status, Vector3 scale, bool createVisual, bool isPosLocal = false)
        {
            string key = GetIdentifierKey(payload);
            if (_trackedQRCodes.TryGetValue(key, out var existing)) return existing;

            GameObject visualObj = createVisual ? CreateVisualObject(payload, pos, rot, status, scale, isPosLocal) : null;
            var instance = new QRCodeInstance
            {
                visualObject = visualObj,
                fullPayload = payload,
                identifierKey = key,
                lastPosition = pos,
                lastRotation = rot,
                lastTrackableRotation = rot,
                status = status
            };
            _trackedQRCodes.Add(key, instance);
            OnQRCodeAdded?.Invoke(instance);
            return instance;
        }

        private GameObject CreateVisualObject(string payload, Vector3 pos, Quaternion rot, QRStatus status, Vector3 scale, bool isPosLocal = false)
        {
            bool isAnchor = payload.Contains(qrRoomAnchorLabel);
            GameObject root = new GameObject(isAnchor ? "RoomAnchor" : $"QR_{payload.GetHashCode()}");

            Quaternion cRot = rot * Quaternion.Euler(0, 180, 0);

            if (!isAnchor && RoomAnchorInstance != null && RoomAnchorInstance.visualObject != null)
            {
                root.transform.SetParent(RoomAnchorInstance.visualObject.transform);
                if (isPosLocal)
                {
                    root.transform.localPosition = pos;
                    root.transform.localRotation = cRot;
                }
                else
                {
                    root.transform.SetPositionAndRotation(pos, cRot);
                }
            }
            else
            {
                root.transform.SetPositionAndRotation(pos, cRot);
            }

            foreach (var action in payloadActions)
            {
                if (!string.IsNullOrEmpty(action.matchString) && payload.Contains(action.matchString))
                {
                    if (action.customPrefab != null)
                    {
                        var instantiated = Instantiate(action.customPrefab, root.transform);
                        instantiated.transform.localPosition = Vector3.zero;
                        instantiated.transform.localRotation = Quaternion.identity;
                    }
                    action.onPayloadMatched?.Invoke();
                    break;
                }
            }

            CreateDefaultVisualization(root, payload, status, scale);
            return root;
        }

        private void CreateDefaultVisualization(GameObject root, string payload, QRStatus status, Vector3 scale)
        {
            // Unified 4-category colour + label (green=target, red=invalid, blue=valid-listed, orange=unlisted).
            var category = ClassifyPayload(payload);
            Color baseColor = GetCategoryColor(category);
            string labelPrefix = LabelPrefixFor(category);

            GameObject bg = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bg.name = "VisualBackground";
            bg.transform.SetParent(root.transform);
            bg.transform.localScale = new Vector3(scale.x, scale.y, 0.001f);
            bg.transform.localPosition = Vector3.zero;
            bg.transform.localRotation = Quaternion.identity;
            if (bg.TryGetComponent<BoxCollider>(out var col)) Destroy(col);

            var renderer = bg.GetComponent<Renderer>();
            // Shared transparent material (cached per color) — see GetSharedVisualMaterial (WP-5 perf).
            renderer.sharedMaterial = GetSharedVisualMaterial(new Color(baseColor.r, baseColor.g, baseColor.b, 0.3f), transparent: true);

            CreateVisualBorder(root.transform, scale, baseColor);

            // Optional debug sphere (off by default — purely a debug aid, saves a primitive per code).
            if (showDebugCenter)
            {
                GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sphere.name = "DebugCenter";
                sphere.transform.SetParent(root.transform);
                sphere.transform.localScale = new Vector3(0.015f, 0.015f, 0.015f);
                sphere.transform.localPosition = Vector3.zero;
                if (sphere.TryGetComponent<Collider>(out var sCol)) Destroy(sCol);
                sphere.GetComponent<Renderer>().sharedMaterial = renderer.sharedMaterial;
            }

            // Note: no constant pulse here anymore — only the focused/"pointed-at" code pulses
            // (see FocusQRCode / UpdateFocusGlow). Detection markers fade out to avoid scene clutter.

            // Optional payload label (TextMeshPro is the most expensive per-code object). Skipping it lets
            // the scene track 50+ codes without frame drops; status is still conveyed by marker color.
            if (showPayloadLabels)
            {
                GameObject textObj = new GameObject("PayloadLabel");
                textObj.transform.SetParent(root.transform);
                textObj.transform.localPosition = new Vector3(0, 0, -0.015f);
                textObj.transform.localRotation = Quaternion.identity;

                var tmp = textObj.AddComponent<TextMeshPro>();
                tmp.text = $"{labelPrefix}{payload}";
                tmp.fontSize = 0.15f;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.color = Color.white;
                tmp.rectTransform.sizeDelta = new Vector2(scale.x * 3.0f, scale.y * 3.0f);
                // PERF: auto-sizing forces extra TMP layout passes per code; use a fixed size instead.
                tmp.enableAutoSizing = false;
            }
        }

        private void CreateVisualBorder(Transform parent, Vector3 scale, Color color)
        {
            float t = 0.008f;
            CreateBorderBar(parent, new Vector3(0, scale.y/2, 0), new Vector3(scale.x+t, t, t), color);
            CreateBorderBar(parent, new Vector3(0, -scale.y/2, 0), new Vector3(scale.x+t, t, t), color);
            CreateBorderBar(parent, new Vector3(-scale.x/2, 0, 0), new Vector3(t, scale.y+t, t), color);
            CreateBorderBar(parent, new Vector3(scale.x/2, 0, 0), new Vector3(t, scale.y+t, t), color);
        }

        private void CreateBorderBar(Transform parent, Vector3 pos, Vector3 size, Color color)
        {
            GameObject bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bar.transform.SetParent(parent);
            bar.transform.localPosition = pos;
            bar.transform.localScale = size;
            bar.transform.localRotation = Quaternion.identity;
            if (bar.TryGetComponent<BoxCollider>(out var col)) Destroy(col);
            var r = bar.GetComponent<Renderer>();
            // Shared opaque material (cached per color) so all border bars batch (WP-5 perf).
            r.sharedMaterial = GetSharedVisualMaterial(color, transparent: false);
        }

        private void UpdateTextOnObject(GameObject obj, string payload)
        {
            var tmp = obj.GetComponentInChildren<TextMeshPro>();
            if (tmp != null) 
            {
                tmp.text = $"{LabelPrefixFor(ClassifyPayload(payload))}{payload}";
            }
        }

        #region Detection Markers & Classification (testing aid)

        /// <summary>
        /// Category of a detected QR payload, used to colour the on-headset detection pips.
        /// Target = something the app is actively looking for (RoomAnchor or a login setup code).
        /// ValidListed = a payload present in the server-provided valid QR list.
        /// Unlisted = a readable code that is neither a target nor in the valid list.
        /// Invalid = empty/whitespace, or a JSON-looking payload that failed to parse.
        /// </summary>
        public enum QrMarkerCategory { Target, Invalid, ValidListed, Unlisted }

        // Pool of known-valid payloads (populated from the server StartupData / pulled calibration).
        // A HashSet keeps classification O(1) even with hundreds of codes in view.
        private readonly HashSet<string> _validPayloads = new HashSet<string>();

        private class DetectionMarker { public GameObject go; public QrMarkerCategory category; public QrFadeMarker fade; public List<Renderer> frameRenderers; }
        private readonly Dictionary<MRUKTrackable, DetectionMarker> _detectionMarkers = new Dictionary<MRUKTrackable, DetectionMarker>();
        // One shared material per category — avoids allocating a material per marker.
        private readonly Dictionary<QrMarkerCategory, Material> _markerMaterials = new Dictionary<QrMarkerCategory, Material>();
        private float _nextTrackingAssertTime;

        // ---- Focus glow: a holographic GREEN edge-box that wraps the "pointed-at" code ----
        private GameObject _focusGlow;            // parent; positioned/rotated to the code
        private GameObject _focusGlowBox;         // child; scaled to the code (holds edges + fill)
        private readonly List<Renderer> _focusEdgeRenderers = new List<Renderer>(12);
        private Renderer _focusFillRenderer;
        private MaterialPropertyBlock _focusGlowMpb;
        private Transform _focusFollow;           // transform the glow tracks (trackable or visualObject)
        private MRUKTrackable _focusTrackable;    // pip to keep force-visible while focused (may be null)
        private QrMarkerCategory _focusCategory = QrMarkerCategory.Target;
        private float _focusBaseSize = 0.1f;   // largest dimension of the focused code (meters)
        public bool HasFocus => _focusFollow != null;

        // Recognised Setup/Login QR shapes. EITHER form classifies a code as a Target (green):
        //   Legacy: { "customerId": "...", "locationId": "..." }
        //   New:    { "setupCode": "...", "apiBaseUrl": "https://.../api" }
        [Serializable]
        private class SetupQrPayload
        {
            public string customerId;
            public string locationId;
            public string setupCode;
            public string apiBaseUrl;
        }

        // ---- Valid-payload pool API (call from the networking layer when StartupData arrives) ----

        /// <summary>Replaces the valid-payload pool (authoritative server list).</summary>
        public void SetValidPayloads(IEnumerable<string> payloads)
        {
            _validPayloads.Clear();
            if (payloads != null)
                foreach (var p in payloads) if (!string.IsNullOrEmpty(p)) _validPayloads.Add(p);
            RefreshAllMarkerColors();
        }

        /// <summary>Adds payloads to the valid pool without clearing existing entries.</summary>
        public void AddValidPayloads(IEnumerable<string> payloads)
        {
            if (payloads == null) return;
            foreach (var p in payloads) if (!string.IsNullOrEmpty(p)) _validPayloads.Add(p);
            RefreshAllMarkerColors();
        }

        public void AddValidPayload(string payload)
        {
            if (string.IsNullOrEmpty(payload)) return;
            _validPayloads.Add(payload);
            RefreshAllMarkerColors();
        }

        public void ClearValidPayloads()
        {
            _validPayloads.Clear();
            RefreshAllMarkerColors();
        }

        public bool IsValidListed(string payload) => !string.IsNullOrEmpty(payload) && _validPayloads.Contains(payload);

        /// <summary>Read-only view of the server-provided "legit" payload pool, for UI listing
        /// (e.g. showing list entries that have not yet been discovered locally).</summary>
        public System.Collections.Generic.IReadOnlyCollection<string> ValidPayloads => _validPayloads;

        // ---- System-code predicates (RoomAnchor + Sign-In are NEVER items) ----

        /// <summary>True if the payload is the Room Anchor reference code (a pure spatial reference, not an item).</summary>
        public bool IsRoomAnchorPayload(string payload)
            => !string.IsNullOrEmpty(payload) && payload.Contains(qrRoomAnchorLabel);

        /// <summary>
        /// True if the payload is the Sign-In / setup code (never an item). In a SESSION this is the
        /// known recognised setup code or a JSON login code; during the SignIn phase a bare alphanumeric
        /// setup code also qualifies (before any code has been recognised).
        /// </summary>
        public bool IsSignInCode(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload)) return false;
            if (IsRoomAnchorPayload(payload)) return false;
            if (!string.IsNullOrEmpty(recognizedSetupCode) && payload == recognizedSetupCode) return true;
            if (TryParseLoginCode(payload)) return true;
            // Pre-sign-in only: a bare setup code is a sign-in candidate until one is recognised.
            if (Mode != ScanMode.Full && IsBareSetupCode(payload)) return true;
            return false;
        }

        /// <summary>True if the payload is a system code (RoomAnchor or Sign-In) — never treated as a room item.</summary>
        public bool IsSystemCode(string payload) => IsRoomAnchorPayload(payload) || IsSignInCode(payload);

        // ---- Merged item model (server "legit" list  +  live detections) ----

        /// <summary>
        /// Reconciled status of a room ITEM (system codes excluded):
        ///   DetectedUnlisted  = physically detected by the headset but NOT in the server legit list.
        ///   DetectedListed    = physically detected AND in the server legit list.
        ///   ListedNotDetected = in the server legit list but not currently detected (data only, no marker).
        /// </summary>
        public enum QrItemStatus { DetectedUnlisted, DetectedListed, ListedNotDetected }

        public struct QrItem
        {
            public string payload;
            public string name;                 // friendly name (server nameDictionary) or null
            public QrItemStatus status;
            public QRCodeInstance instance;     // null when ListedNotDetected
        }

        // Friendly names supplied by the server (payload -> name).
        private readonly Dictionary<string, string> _payloadNames = new Dictionary<string, string>();

        // RoomAnchor-relative poses known from the server/disk for codes that are NOT currently detected.
        // These are DATA ONLY (no scene marker) — used for the merged list, point-to coordinate fallback,
        // and re-upload. A code gains a real marker only when it is physically detected.
        [Serializable] private class KnownPose { public string payload; public string name; public Vector3 localPosition; public Quaternion localRotation; }
        private readonly Dictionary<string, KnownPose> _knownPoses = new Dictionary<string, KnownPose>();

        /// <summary>Stores a friendly name for a payload (from the server nameDictionary / qrCodes[].name).</summary>
        public void SetPayloadName(string payload, string name)
        {
            if (string.IsNullOrEmpty(payload) || string.IsNullOrEmpty(name)) return;
            _payloadNames[payload] = name;
        }

        /// <summary>Returns the friendly name for a payload, or null if none is known.</summary>
        public string GetPayloadName(string payload)
            => (!string.IsNullOrEmpty(payload) && _payloadNames.TryGetValue(payload, out var n)) ? n : null;

        /// <summary>Gets the RoomAnchor-relative pose known for a not-currently-detected listed code.</summary>
        public bool TryGetKnownLocalPose(string payload, out Vector3 localPos, out Quaternion localRot)
        {
            localPos = Vector3.zero; localRot = Quaternion.identity;
            if (string.IsNullOrEmpty(payload)) return false;
            if (_knownPoses.TryGetValue(GetIdentifierKey(payload), out var kp)) { localPos = kp.localPosition; localRot = kp.localRotation; return true; }
            return false;
        }

        /// <summary>Gets the WORLD pose for a not-currently-detected listed code (requires a RoomAnchor to resolve).</summary>
        public bool TryGetKnownWorldPose(string payload, out Vector3 worldPos, out Quaternion worldRot)
        {
            worldPos = Vector3.zero; worldRot = Quaternion.identity;
            if (!TryGetKnownLocalPose(payload, out var lp, out var lr)) return false;
            if (RoomAnchorInstance == null || RoomAnchorInstance.visualObject == null) return false;
            var t = RoomAnchorInstance.visualObject.transform;
            worldPos = t.TransformPoint(lp);
            worldRot = t.rotation * lr;
            return true;
        }

        /// <summary>
        /// Returns the merged room-item list: every physically-detected item (classified DetectedListed /
        /// DetectedUnlisted) plus every server-listed payload not currently detected (ListedNotDetected).
        /// RoomAnchor and Sign-In codes are always excluded.
        /// </summary>
        public List<QrItem> GetMergedQrItems()
        {
            var items = new List<QrItem>();
            var seen = new HashSet<string>();

            foreach (var inst in _trackedQRCodes.Values)
            {
                if (inst == null || string.IsNullOrEmpty(inst.fullPayload)) continue;
                if (inst == RoomAnchorInstance) continue;
                if (IsSystemCode(inst.fullPayload)) continue;
                seen.Add(inst.identifierKey);
                items.Add(new QrItem
                {
                    payload = inst.fullPayload,
                    name = GetPayloadName(inst.fullPayload),
                    status = IsValidListed(inst.fullPayload) ? QrItemStatus.DetectedListed : QrItemStatus.DetectedUnlisted,
                    instance = inst
                });
            }

            foreach (var payload in _validPayloads)
            {
                if (string.IsNullOrEmpty(payload) || IsSystemCode(payload)) continue;
                string key = GetIdentifierKey(payload);
                if (seen.Contains(key)) continue;
                seen.Add(key);
                items.Add(new QrItem
                {
                    payload = payload,
                    name = GetPayloadName(payload),
                    status = QrItemStatus.ListedNotDetected,
                    instance = null
                });
            }
            return items;
        }

        // ---- Classification ----

        /// <summary>The current active setup code. If set, this code is always classified as Target (green)
        /// regardless of the current ScanMode.</summary>
        public string recognizedSetupCode { get; set; }

        public QrMarkerCategory ClassifyPayload(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload)) return QrMarkerCategory.Invalid;
            if (payload.Contains(qrRoomAnchorLabel)) return QrMarkerCategory.Target;     // RoomAnchor
            if (TryParseLoginCode(payload)) return QrMarkerCategory.Target;              // JSON login setup code

            // Explicitly recognize the active setup code as a target.
            if (!string.IsNullOrEmpty(recognizedSetupCode) && payload == recognizedSetupCode) return QrMarkerCategory.Target;

            if (_validPayloads.Contains(payload)) return QrMarkerCategory.ValidListed;   // known good

            // Bare alphanumeric setup code (smallest QR). 
            if (IsBareSetupCode(payload))
            {
                // REPLIT AI SUGGESTION: Recognize 8-char alphanumeric codes as Target.
                // We mark 8-char codes as Target regardless of mode (most likely a setup code),
                // but only use the heuristic for other lengths during the SignIn phase.
                if (payload.Length == 8 || Mode != ScanMode.Full) return QrMarkerCategory.Target;
            }

            if (payload.TrimStart().StartsWith("{")) return QrMarkerCategory.Invalid;    // looks like JSON but isn't a valid setup code
            return QrMarkerCategory.Unlisted;
        }

        public static Color GetCategoryColor(QrMarkerCategory cat)
        {
            switch (cat)
            {
                case QrMarkerCategory.Target:      return Color.green;
                case QrMarkerCategory.Invalid:     return Color.red;
                case QrMarkerCategory.ValidListed: return new Color(0.2f, 0.5f, 1f);  // blue
                default:                           return new Color(1f, 0.55f, 0f);   // orange
            }
        }

        /// <summary>Canonical text prefix for a category (used on the per-code visual label).</summary>
        public static string LabelPrefixFor(QrMarkerCategory cat)
        {
            switch (cat)
            {
                case QrMarkerCategory.Target:      return "[Target] ";
                case QrMarkerCategory.Invalid:     return "[Invalid] ";
                case QrMarkerCategory.ValidListed: return "[Listed] ";
                default:                           return "[Unlisted] ";
            }
        }

        private bool TryParseLoginCode(string payload)
        {
            if (string.IsNullOrEmpty(payload)) return false;
            if (!payload.TrimStart().StartsWith("{")) return false;
            try
            {
                var d = JsonUtility.FromJson<SetupQrPayload>(payload);
                if (d == null) return false;
                // Legacy format: explicit customerId + locationId.
                bool legacy = !string.IsNullOrEmpty(d.customerId) && !string.IsNullOrEmpty(d.locationId);
                // New format: setupCode + apiBaseUrl (resolved against the backend after the scan).
                bool setupCodeFormat = !string.IsNullOrEmpty(d.setupCode) && !string.IsNullOrEmpty(d.apiBaseUrl);
                return legacy || setupCodeFormat;
            }
            catch { return false; }
        }

        /// <summary>
        /// True if the payload is a BARE setup code: a non-JSON, non-RoomAnchor, alphanumeric string
        /// whose length is within [setupCodeMinLength, setupCodeMaxLength]. This is the smallest possible
        /// Sign In QR payload (e.g. an 8-char handshake code). The backend URL is NOT in the QR — the
        /// device uses its stored/default URL to resolve the code into customer/location.
        /// </summary>
        public bool IsBareSetupCode(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload)) return false;
            string p = payload.Trim();
            if (p.StartsWith("{")) return false;                 // JSON, handled by TryParseLoginCode
            if (p.Contains(qrRoomAnchorLabel)) return false;     // RoomAnchor, handled separately
            if (p.Length < setupCodeMinLength || p.Length > setupCodeMaxLength) return false;
            for (int i = 0; i < p.Length; i++)
                if (!char.IsLetterOrDigit(p[i])) return false;
            return true;
        }

        // ---- Marker lifecycle ----

        public bool DetectionMarkersVisible => showDetectionMarkers;

        public void SetDetectionMarkersVisible(bool visible)
        {
            showDetectionMarkers = visible;
            foreach (var kvp in _detectionMarkers)
                if (kvp.Value.go != null) kvp.Value.go.SetActive(visible);
        }

        public void ToggleDetectionMarkers() => SetDetectionMarkersVisible(!showDetectionMarkers);

        private Material GetCategoryMaterial(QrMarkerCategory cat)
        {
            if (_markerMaterials.TryGetValue(cat, out var existing) && existing != null) return existing;
            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            var m = new Material(sh) { name = "QRMarker_" + cat };
            Color c = GetCategoryColor(cat);
            ConfigureTransparent(m, c);
            // Allow per-renderer color/alpha overrides via MaterialPropertyBlock while sharing one material.
            m.enableInstancing = true;
            _markerMaterials[cat] = m;
            return m;
        }

        /// <summary>Configures a URP/Unlit (or fallback) material for alpha-blended transparency.</summary>
        private static void ConfigureTransparent(Material m, Color c)
        {
            m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);   // 0 = opaque, 1 = transparent
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);       // 0 = alpha
            if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        private void CreateOrUpdateDetectionMarker(MRUKTrackable trackable, string payload)
        {
            if (trackable == null) return;
            var cat = ClassifyPayload(payload);

            if (_detectionMarkers.TryGetValue(trackable, out var entry) && entry.go != null)
            {
                if (entry.category != cat)
                {
                    entry.category = cat;
                    var mat = GetCategoryMaterial(cat);
                    if (entry.frameRenderers != null)
                        foreach (var r in entry.frameRenderers) if (r != null) r.sharedMaterial = mat;
                    entry.fade?.SetColor(GetCategoryColor(cat));
                }
                // Re-detection counts as a "detected" event -> show again, then fade.
                if (_focusTrackable != trackable) entry.fade?.Show();
                return;
            }

            // Build a thin outline frame sized to the QR's real plane rect so the border lines up
            // with the physical code (4 bars forming a rectangle).
            Vector3 box = GetTrackableBoxScale(trackable);
            var go = new GameObject("QRDetectMarker");
            var mat0 = GetCategoryMaterial(cat);
            var renderers = BuildMarkerFrame(go.transform, box.x, box.y, mat0);
            go.SetActive(showDetectionMarkers);

            var fade = go.AddComponent<QrFadeMarker>();
            // Primary renderer + the rest as "extra" so the whole frame fades together.
            Renderer primary = renderers.Count > 0 ? renderers[0] : null;
            var extra = renderers.Count > 1 ? renderers.GetRange(1, renderers.Count - 1) : null;
            fade.Init(primary, GetCategoryColor(cat), markerHoldSeconds, markerFadeSeconds, fadeQrDetectionMarkerTransparency);
            fade.SetExtraRenderers(extra);

            _detectionMarkers[trackable] = new DetectionMarker { go = go, category = cat, fade = fade, frameRenderers = renderers };
        }

        /// <summary>Creates a rectangular outline (4 thin bars) of width x height under <paramref name="parent"/>.</summary>
        private List<Renderer> BuildMarkerFrame(Transform parent, float width, float height, Material mat)
        {
            var renderers = new List<Renderer>(4);
            float t = Mathf.Clamp(Mathf.Min(width, height) * 0.06f, 0.003f, 0.02f); // border thickness
            const float depth = 0.002f;
            renderers.Add(AddFrameBar(parent, new Vector3(0f, height / 2f, 0f), new Vector3(width + t, t, depth), mat));
            renderers.Add(AddFrameBar(parent, new Vector3(0f, -height / 2f, 0f), new Vector3(width + t, t, depth), mat));
            renderers.Add(AddFrameBar(parent, new Vector3(-width / 2f, 0f, 0f), new Vector3(t, height + t, depth), mat));
            renderers.Add(AddFrameBar(parent, new Vector3(width / 2f, 0f, 0f), new Vector3(t, height + t, depth), mat));
            return renderers;
        }

        private Renderer AddFrameBar(Transform parent, Vector3 localPos, Vector3 localScale, Material mat)
        {
            var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bar.name = "FrameBar";
            bar.transform.SetParent(parent, false);
            bar.transform.localPosition = localPos;
            bar.transform.localScale = localScale;
            bar.transform.localRotation = Quaternion.identity;
            if (bar.TryGetComponent<Collider>(out var col)) Destroy(col);
            var r = bar.GetComponent<Renderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sharedMaterial = mat;
            return r;
        }

        /// <summary>Returns the renderers of the heavy per-QR visual for the given payload, or null.</summary>
        private List<Renderer> GetInstanceVisualRenderers(string payload)
        {
            string key = GetIdentifierKey(payload);
            if (_trackedQRCodes.TryGetValue(key, out var inst) && inst.visualObject != null)
            {
                var list = new List<Renderer>();
                inst.visualObject.GetComponentsInChildren(true, list);
                return list;
            }
            return null;
        }

        private void RemoveDetectionMarker(MRUKTrackable trackable)
        {
            if (trackable != null && _detectionMarkers.TryGetValue(trackable, out var e))
            {
                if (e.go != null) Destroy(e.go);
                _detectionMarkers.Remove(trackable);
            }
        }

        /// <summary>Keeps each outline frame aligned to its (moving) QR code, and prunes dead trackables.</summary>
        private void UpdateDetectionMarkers()
        {
            if (_detectionMarkers.Count == 0) return;
            List<MRUKTrackable> dead = null;
            foreach (var kvp in _detectionMarkers)
            {
                var t = kvp.Key;
                var e = kvp.Value;
                if (t == null || e.go == null) { (dead ??= new List<MRUKTrackable>()).Add(kvp.Key); continue; }
                if (e.go.activeSelf != showDetectionMarkers) e.go.SetActive(showDetectionMarkers);
                if (!showDetectionMarkers) continue;
                var tr = t.transform;
                // Sit just in front of the QR plane so the outline reads clearly without z-fighting.
                e.go.transform.SetPositionAndRotation(tr.position - tr.forward * 0.003f, tr.rotation);
            }
            if (dead != null)
                foreach (var k in dead)
                {
                    if (_detectionMarkers[k].go != null) Destroy(_detectionMarkers[k].go);
                    _detectionMarkers.Remove(k);
                }
        }

        private void RefreshAllMarkerColors()
        {
            foreach (var kvp in _detectionMarkers)
            {
                var t = kvp.Key;
                var e = kvp.Value;
                if (t == null || e.go == null) continue;
                var cat = ClassifyPayload(t.MarkerPayloadString ?? "");
                if (cat != e.category)
                {
                    e.category = cat;
                    var mat = GetCategoryMaterial(cat);
                    if (e.frameRenderers != null)
                        foreach (var r in e.frameRenderers) if (r != null) r.sharedMaterial = mat;
                    e.fade?.SetColor(GetCategoryColor(cat));
                }
            }
        }

        /// <summary>Low-frequency safeguard: re-assert QR tracking if the runtime dropped it.</summary>
        private float _nextReconcileTime;
        [Tooltip("Seconds between QR reconciliation sweeps. The sweep re-scans every live MRUK QR trackable to " +
                 "recover detections that were missed or whose payload decoded late, re-link codes that were " +
                 "physically moved (which can produce a new trackable), and keep the tracked set in sync. " +
                 "Lower = more responsive, slightly more CPU.")]
        public float qrReconcileInterval = 0.5f;

        /// <summary>
        /// SELF-HEALING reconciliation: MRUK fires TrackableAdded only ONCE per trackable and has no
        /// "updated" event, so a missed add, a payload that decodes after our retry window, or a code that
        /// is physically moved (which can surface as a NEW trackable) would otherwise be lost. This sweep
        /// periodically reconciles our tracked set against every live QR trackable in the scene:
        ///   • empty-payload trackables are (re)armed for payload retry (never permanently dropped),
        ///   • already-tracked codes get their live trackable re-linked (handles moves / new trackables),
        ///   • not-yet-tracked codes with a ready payload are processed via the normal add path.
        /// Runs in Full (Session) mode only; SignIn detection is event-driven + the pip path already.
        /// </summary>
        private void ReconcileTrackables()
        {
            if (Mode != ScanMode.Full) return;
            if (Time.time < _nextReconcileTime) return;
            _nextReconcileTime = Time.time + Mathf.Max(0.1f, qrReconcileInterval);

            var all = UnityEngine.Object.FindObjectsByType<MRUKTrackable>(FindObjectsInactive.Exclude);
            foreach (var t in all)
            {
                if (t == null || t.TrackableType != OVRAnchor.TrackableType.QRCode) continue;
                // Already decided to ignore this one (e.g. the sign-in code during a session) — do not
                // reprocess it, or we'd re-raise raw detection every interval.
                if (_ignoredSessionTrackables.Contains(t)) continue;

                string payload = t.MarkerPayloadString ?? "";
                if (string.IsNullOrEmpty(payload))
                {
                    // Keep retrying the payload as long as the trackable exists (never drop it for good).
                    if (!_pendingPayloadTrackables.ContainsKey(t)) _pendingPayloadTrackables[t] = Time.time;
                    continue;
                }

                string key = GetIdentifierKey(payload);
                if (_trackedQRCodes.TryGetValue(key, out var inst))
                {
                    // Re-link the live trackable if it changed (a moved code can yield a new trackable),
                    // so the per-frame follow tracks the correct, current pose.
                    if (inst.trackable != t) inst.trackable = t;
                }
                else
                {
                    // Missed or late detection — run the normal add path (handles anchor/item/system-code rules).
                    OnTrackableAdded(t);
                }
            }
        }

        private void EnsureTrackingPeriodically()
        {
            if (Time.time < _nextTrackingAssertTime) return;
            _nextTrackingAssertTime = Time.time + 2f;
            if (MRUK.Instance == null) return;
            try
            {
                var config = MRUK.Instance.SceneSettings.TrackerConfiguration;
                if (!config.QRCodeTrackingEnabled)
                {
                    config.QRCodeTrackingEnabled = true;
                    MRUK.Instance.SceneSettings.TrackerConfiguration = config;
                    Debug.Log("[QrCodeManager] Re-asserted QR tracking (was disabled).");
                }
            }
            catch { }
        }

        // ---- Focus / "point-at" glow ----

        /// <summary>
        /// Highlights a QR code as the current "pointed-at" target: a discernible pulsing glow
        /// surrounds it (and its detection pip is kept visible) until <see cref="ClearFocus"/> is called.
        /// Works whether the code is physically tracked (follows the live trackable) or only known from
        /// the calibration/dropdown (follows its placed visual object).
        /// </summary>
        public void FocusQRCode(QRCodeInstance instance)
        {
            if (instance == null) { ClearFocus(); return; }

            Transform follow = instance.trackable != null ? instance.trackable.transform
                             : (instance.visualObject != null ? instance.visualObject.transform : null);
            if (follow == null) { ClearFocus(); return; }

            BeginFocus(follow, instance.trackable, ClassifyPayload(instance.fullPayload));
        }

        /// <summary>Focus by live trackable (e.g. from a raw physical detection).</summary>
        public void FocusTrackable(MRUKTrackable trackable)
        {
            if (trackable == null) { ClearFocus(); return; }
            BeginFocus(trackable.transform, trackable, ClassifyPayload(trackable.MarkerPayloadString ?? ""));
        }

        private void BeginFocus(Transform follow, MRUKTrackable trackable, QrMarkerCategory category)
        {
            // Release the previously focused pip back to normal fade behaviour.
            if (_focusTrackable != null && _focusTrackable != trackable
                && _detectionMarkers.TryGetValue(_focusTrackable, out var prev))
                prev.fade?.SetForceVisible(false);

            _focusFollow = follow;
            _focusTrackable = trackable;
            _focusCategory = category;

            // Size the glow to the focused code so it visually wraps it.
            if (trackable != null)
            {
                Vector3 b = GetTrackableBoxScale(trackable);
                _focusBaseSize = Mathf.Max(b.x, b.y);
            }
            else
            {
                _focusBaseSize = 0.12f;
            }

            // Keep the focused pip visible while it is the selection.
            if (trackable != null && _detectionMarkers.TryGetValue(trackable, out var cur))
                cur.fade?.SetForceVisible(true);

            EnsureFocusGlow();
            ApplyFocusGlowPulse(1f);   // green hologram; UpdateFocusGlow drives the breathing pulse
            _focusGlow.SetActive(true);
        }

        /// <summary>Clears the current point-at selection and stops the glow.</summary>
        public void ClearFocus()
        {
            if (_focusTrackable != null && _detectionMarkers.TryGetValue(_focusTrackable, out var cur))
                cur.fade?.SetForceVisible(false);
            _focusFollow = null;
            _focusTrackable = null;
            if (_focusGlow != null) _focusGlow.SetActive(false);
        }

        // Holographic point-at color (bright green so it reads as a glowing hologram, especially under the
        // URP bloom volume). Edges are near-opaque; the fill is faint and transparent.
        private static readonly Color FocusEdgeColor = new Color(0.10f, 1f, 0.35f, 1f);
        private static readonly Color FocusFillColor = new Color(0.10f, 1f, 0.35f, 0.12f);

        private void EnsureFocusGlow()
        {
            if (_focusGlow != null) return;
            _focusGlow = new GameObject("QRFocusGlow");
            _focusGlowMpb = new MaterialPropertyBlock();

            _focusGlowBox = new GameObject("Box");
            _focusGlowBox.transform.SetParent(_focusGlow.transform, false);

            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            if (sh == null) sh = Shader.Find("Sprites/Default");

            // Shared transparent materials (one per role). Per-frame alpha pulse is via MaterialPropertyBlock.
            var edgeMat = new Material(sh) { name = "QRFocusEdge_Mat" };
            ConfigureTransparent(edgeMat, FocusEdgeColor);
            edgeMat.enableInstancing = true;
            var fillMat = new Material(sh) { name = "QRFocusFill_Mat" };
            ConfigureTransparent(fillMat, FocusFillColor);
            fillMat.enableInstancing = true;

            // Faint transparent fill (a cube just inside the edges) -> the "semi-transparent hologram" body.
            var fill = GameObject.CreatePrimitive(PrimitiveType.Cube);
            fill.name = "Fill";
            if (fill.TryGetComponent<Collider>(out var fc)) Destroy(fc);
            fill.transform.SetParent(_focusGlowBox.transform, false);
            fill.transform.localScale = Vector3.one * 0.98f;
            _focusFillRenderer = fill.GetComponent<Renderer>();
            _focusFillRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _focusFillRenderer.receiveShadows = false;
            _focusFillRenderer.sharedMaterial = fillMat;

            // 12 thick edge bars forming a unit-cube wireframe centered at origin (corners at ±0.5) -> the
            // "thick glowing green edges". The whole box is scaled to the code each frame in UpdateFocusGlow.
            const float t = 0.07f; // edge thickness in unit-box space
            _focusEdgeRenderers.Clear();
            AddFocusEdge(new Vector3(0, +0.5f, +0.5f), new Vector3(1, t, t), edgeMat);
            AddFocusEdge(new Vector3(0, +0.5f, -0.5f), new Vector3(1, t, t), edgeMat);
            AddFocusEdge(new Vector3(0, -0.5f, +0.5f), new Vector3(1, t, t), edgeMat);
            AddFocusEdge(new Vector3(0, -0.5f, -0.5f), new Vector3(1, t, t), edgeMat);
            AddFocusEdge(new Vector3(+0.5f, 0, +0.5f), new Vector3(t, 1, t), edgeMat);
            AddFocusEdge(new Vector3(+0.5f, 0, -0.5f), new Vector3(t, 1, t), edgeMat);
            AddFocusEdge(new Vector3(-0.5f, 0, +0.5f), new Vector3(t, 1, t), edgeMat);
            AddFocusEdge(new Vector3(-0.5f, 0, -0.5f), new Vector3(t, 1, t), edgeMat);
            AddFocusEdge(new Vector3(+0.5f, +0.5f, 0), new Vector3(t, t, 1), edgeMat);
            AddFocusEdge(new Vector3(+0.5f, -0.5f, 0), new Vector3(t, t, 1), edgeMat);
            AddFocusEdge(new Vector3(-0.5f, +0.5f, 0), new Vector3(t, t, 1), edgeMat);
            AddFocusEdge(new Vector3(-0.5f, -0.5f, 0), new Vector3(t, t, 1), edgeMat);

            _focusGlow.SetActive(false);
        }

        private void AddFocusEdge(Vector3 localPos, Vector3 localScale, Material mat)
        {
            var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bar.name = "Edge";
            if (bar.TryGetComponent<Collider>(out var c)) Destroy(c);
            bar.transform.SetParent(_focusGlowBox.transform, false);
            bar.transform.localPosition = localPos;
            bar.transform.localScale = localScale;
            var r = bar.GetComponent<Renderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sharedMaterial = mat;
            _focusEdgeRenderers.Add(r);
        }

        /// <summary>Applies the breathing alpha pulse (0..1) to the edge bars and the faint fill.</summary>
        private void ApplyFocusGlowPulse(float pulse01)
        {
            if (_focusGlowMpb == null) return;
            Color edge = FocusEdgeColor; edge.a = Mathf.Lerp(0.6f, 1f, pulse01);
            for (int i = 0; i < _focusEdgeRenderers.Count; i++)
            {
                var r = _focusEdgeRenderers[i];
                if (r == null) continue;
                r.GetPropertyBlock(_focusGlowMpb);
                _focusGlowMpb.SetColor("_BaseColor", edge);
                _focusGlowMpb.SetColor("_Color", edge);
                r.SetPropertyBlock(_focusGlowMpb);
            }
            if (_focusFillRenderer != null)
            {
                Color fill = FocusFillColor; fill.a = Mathf.Lerp(0.06f, 0.18f, pulse01);
                _focusFillRenderer.GetPropertyBlock(_focusGlowMpb);
                _focusGlowMpb.SetColor("_BaseColor", fill);
                _focusGlowMpb.SetColor("_Color", fill);
                _focusFillRenderer.SetPropertyBlock(_focusGlowMpb);
            }
        }

        /// <summary>Positions and animates the focus glow; auto-clears if its target disappears.</summary>
        private void UpdateFocusGlow()
        {
            if (_focusFollow == null)
            {
                if (_focusGlow != null && _focusGlow.activeSelf) _focusGlow.SetActive(false);
                return;
            }
            if (!_focusFollow) // Unity-null (destroyed) target
            {
                ClearFocus();
                return;
            }
            if (_focusGlow == null) return;

            // Center the hologram box on the code, matching its orientation so the box wraps it.
            _focusGlow.transform.SetPositionAndRotation(_focusFollow.position, _focusFollow.rotation);
            _focusGlow.transform.localScale = Vector3.one;

            // Pulse: gentle breathing on both scale and alpha so it reads as a live "pointed-at" hologram.
            float p = (Mathf.Sin(Time.time * 5f) + 1f) * 0.5f;          // 0..1
            float scale = _focusBaseSize * (1.2f + p * 0.25f);          // wraps the code with a breathing margin
            if (_focusGlowBox != null) _focusGlowBox.transform.localScale = Vector3.one * scale;
            ApplyFocusGlowPulse(p);
        }

        #endregion

        /// <summary>
        /// Per-marker fade controller. Drives the detection pip's alpha (and any linked heavy-visual
        /// renderers) via a MaterialPropertyBlock so all markers share one material per category.
        /// Full opacity on Show(), holds, then fades out; SetForceVisible(true) holds it on (used while
        /// the code is the focused/pointed-at selection). Disables itself once fully faded to stay cheap
        /// with hundreds of codes in view.
        /// </summary>
        private class QrFadeMarker : MonoBehaviour
        {
            private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
            private static readonly int ColorId = Shader.PropertyToID("_Color");

            private Renderer _renderer;
            private MaterialPropertyBlock _mpb;
            private List<Renderer> _extra;
            private Color _baseColor = Color.white;
            private float _hold = 3f;
            private float _fade = 1.5f;
            private float _restAlpha = 0f;
            private float _shownAt = -999f;
            private bool _forceVisible;

            public void Init(Renderer r, Color color, float hold, float fade, float restAlpha = 0f)
            {
                _renderer = r;
                _mpb = new MaterialPropertyBlock();
                _baseColor = color;
                _hold = hold;
                _fade = Mathf.Max(0.0001f, fade);
                _restAlpha = Mathf.Clamp01(restAlpha);
                Show();
            }

            public void SetRestAlpha(float restAlpha)
            {
                _restAlpha = Mathf.Clamp01(restAlpha);
                enabled = true; // re-evaluate the fade with the new resting alpha
            }

            public void SetColor(Color color) { _baseColor = color; Apply(CurrentAlpha()); }

            public void SetExtraRenderers(List<Renderer> extra) { _extra = extra; }

            public void Show()
            {
                _forceVisible = false;
                _shownAt = Time.time;
                enabled = true;
                SetRenderersEnabled(true);
                Apply(1f);
            }

            public void SetForceVisible(bool on)
            {
                _forceVisible = on;
                if (on)
                {
                    enabled = true;
                    SetRenderersEnabled(true);
                    Apply(1f);
                }
                else
                {
                    // Resume the normal hold+fade from now.
                    _shownAt = Time.time;
                    enabled = true;
                }
            }

            private float CurrentAlpha()
            {
                if (_forceVisible) return 1f;
                float t = Time.time - _shownAt;
                if (t <= _hold) return 1f;
                float a = 1f - (t - _hold) / _fade;
                // Settle at the configured resting alpha instead of always fading to 0.
                return Mathf.Clamp(a, _restAlpha, 1f);
            }

            private void Update()
            {
                if (_forceVisible) { Apply(1f); return; }
                float a = CurrentAlpha();
                Apply(a);

                // Once the fade has fully run, stop updating to save cost.
                bool fadeComplete = (Time.time - _shownAt) > (_hold + _fade);
                if (fadeComplete)
                {
                    if (_restAlpha <= 0f)
                    {
                        SetRenderersEnabled(false); // invisible -> stop drawing
                        enabled = false;            // and stop updating until shown again
                    }
                    else
                    {
                        // Keep renderers on at the resting alpha so the user can still see the
                        // code is being tracked. The MaterialPropertyBlock already holds restAlpha.
                        enabled = false;
                    }
                }
            }

            private void SetRenderersEnabled(bool on)
            {
                if (_renderer != null) _renderer.enabled = on;
                if (_extra != null)
                    for (int i = 0; i < _extra.Count; i++)
                        if (_extra[i] != null) _extra[i].enabled = on;
            }

            private void Apply(float alpha)
            {
                ApplyTo(_renderer, alpha);
                if (_extra != null)
                    for (int i = 0; i < _extra.Count; i++)
                        ApplyTo(_extra[i], alpha);
            }

            private void ApplyTo(Renderer r, float alpha)
            {
                if (r == null) return;
                Color c = _baseColor; c.a = alpha;
                r.GetPropertyBlock(_mpb);
                _mpb.SetColor(BaseColorId, c);
                _mpb.SetColor(ColorId, c);
                r.SetPropertyBlock(_mpb);
            }
        }

        #region Meta Spatial Anchor (RoomAnchor persistence)

        // All device-only Meta OVRSpatialAnchor calls live here, guarded by SpatialAnchorsSupported and
        // try/catch so the project compiles and runs in the Editor (where these calls are skipped and the
        // existing plain-GameObject QR path is used). This is the single boundary where a future
        // Shared-Spatial-Anchor option would be added.

        /// <summary>
        /// Persists the freshly-detected RoomAnchor as a Meta OVRSpatialAnchor: adds the component to the
        /// existing RoomAnchor zero-point GameObject (so its current pose — including the visual orientation
        /// captured at scan time — becomes the anchor pose), waits for localization, saves it, and stores the
        /// UUID + payload in PlayerPrefs. The visual frame is unchanged, so backend item coordinates (stored
        /// relative to RoomAnchorInstance.visualObject) are preserved exactly. No-op in the Editor / when
        /// unsupported / when an anchor already backs the RoomAnchor.
        /// </summary>
        private async void TryPersistRoomAnchorAsSpatialAnchor()
        {
            if (!SpatialAnchorsSupported) return;
            if (_spatialAnchorBusy || _roomSpatialAnchor != null) return;
            if (RoomAnchorInstance == null || RoomAnchorInstance.visualObject == null) return;

            _spatialAnchorBusy = true;
            // Pin the RoomAnchor immediately so the per-frame QR-follow does not move it while the anchor
            // is being created (the anchor adopts the GameObject pose at creation time).
            _roomAnchorDrivenBySpatialAnchor = true;

            OVRSpatialAnchor sa = null;
            try
            {
                GameObject go = RoomAnchorInstance.visualObject;
                sa = go.GetComponent<OVRSpatialAnchor>();
                if (sa == null) sa = go.AddComponent<OVRSpatialAnchor>();

                if (!await sa.WhenLocalizedAsync())
                {
                    Debug.LogWarning("[QrCodeManager] RoomAnchor spatial anchor failed to create/localize; " +
                                     "falling back to plain QR tracking.");
                    if (sa != null) Destroy(sa);
                    _roomAnchorDrivenBySpatialAnchor = false; // resume live QR-follow fallback
                    return;
                }

                var save = await sa.SaveAnchorAsync();
                if (!save.Success)
                {
                    Debug.LogWarning("[QrCodeManager] RoomAnchor SaveAnchorAsync failed: " + save.Status +
                                     " — keeping live anchor for this session but not persisted.");
                    _roomSpatialAnchor = sa; // still drift-free this session
                    return;
                }

                _roomSpatialAnchor = sa;
                PlayerPrefs.SetString(RoomAnchorUuidPrefKey, sa.Uuid.ToString());
                PlayerPrefs.SetString(RoomAnchorPayloadPrefKey, RoomAnchorInstance.fullPayload ?? qrRoomAnchorLabel);
                PlayerPrefs.Save();
                Debug.Log("[QrCodeManager] RoomAnchor persisted as Meta spatial anchor. Uuid=" + sa.Uuid);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[QrCodeManager] RoomAnchor spatial-anchor persist error: " + e.Message +
                                 " — falling back to plain QR tracking.");
                if (_roomSpatialAnchor == null) _roomAnchorDrivenBySpatialAnchor = false;
            }
            finally
            {
                _spatialAnchorBusy = false;
            }
        }

        /// <summary>
        /// On session start, re-establishes the RoomAnchor from its stored Meta spatial anchor so the user
        /// does NOT have to re-scan the RoomAnchor QR. On failure (or when unsupported / no UUID), falls back
        /// to the existing behavior: restore the disk RoomAnchor if one was deferred, otherwise leave the
        /// QR-scan path to establish it. Runs at most once.
        /// </summary>
        private async void TryRelocalizeRoomSpatialAnchorOnStart()
        {
            if (_spatialAnchorRelocalizeAttempted) return;
            _spatialAnchorRelocalizeAttempted = true;

            if (!SpatialAnchorsSupported || !HasStoredRoomAnchorUuid() || _isAnchorSet)
            {
                RestoreDeferredDiskAnchorIfAny();
                return;
            }

            bool ok = false;
            try
            {
                ok = await RelocalizeRoomSpatialAnchorAsync();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[QrCodeManager] RoomAnchor relocalization error: " + e.Message);
                ok = false;
            }

            if (!ok)
            {
                Debug.Log("[QrCodeManager] RoomAnchor relocalization unavailable — falling back " +
                          "(disk restore / QR re-scan).");
                RestoreDeferredDiskAnchorIfAny();
            }
        }

        /// <summary>Loads, localizes and binds the stored RoomAnchor spatial anchor, then re-establishes the
        /// RoomAnchor zero-point at its physical pose and activates any dormant item codes.</summary>
        private async System.Threading.Tasks.Task<bool> RelocalizeRoomSpatialAnchorAsync()
        {
            if (!Guid.TryParse(PlayerPrefs.GetString(RoomAnchorUuidPrefKey, ""), out var uuid))
                return false;

            string payload = PlayerPrefs.GetString(RoomAnchorPayloadPrefKey, qrRoomAnchorLabel);

            var unbound = new List<OVRSpatialAnchor.UnboundAnchor>();
            var result = await OVRSpatialAnchor.LoadUnboundAnchorsAsync(new[] { uuid }, unbound);
            if (!result.Success || unbound.Count == 0)
            {
                Debug.LogWarning("[QrCodeManager] LoadUnboundAnchorsAsync failed/empty: " + result.Status);
                return false;
            }

            var ua = unbound[0];
            if (!ua.Localized && !await ua.LocalizeAsync())
            {
                Debug.LogWarning("[QrCodeManager] RoomAnchor LocalizeAsync failed for Uuid=" + ua.Uuid);
                return false;
            }

            if (!ua.TryGetPose(out var pose))
            {
                Debug.LogWarning("[QrCodeManager] RoomAnchor TryGetPose failed for Uuid=" + ua.Uuid);
                return false;
            }

            // Build the RoomAnchor zero-point visual, then bind the spatial anchor to it. The spatial-anchor
            // frame is authoritative (it already encodes the orientation captured at save time), so we use the
            // anchor pose directly rather than the extra visual flip CreateVisualObject applies for a live scan.
            GameObject root = CreateVisualObject(payload, pose.position, pose.rotation, QRStatus.Official,
                                                 new Vector3(0.15f, 0.15f, 0.005f), false);
            root.transform.SetPositionAndRotation(pose.position, pose.rotation);

            var sa = root.AddComponent<OVRSpatialAnchor>();
            ua.BindTo(sa);
            _roomSpatialAnchor = sa;
            _roomAnchorDrivenBySpatialAnchor = true;

            string key = GetIdentifierKey(payload);
            var instance = new QRCodeInstance
            {
                visualObject = root,
                fullPayload = payload,
                identifierKey = key,
                lastPosition = pose.position,
                lastRotation = pose.rotation,
                status = QRStatus.Official
            };
            _trackedQRCodes[key] = instance;
            RoomAnchorInstance = instance;
            _deferredDiskAnchor = null; // relocalization succeeded; disk fallback no longer needed

            OnQRCodeAdded?.Invoke(instance);
            OnRoomAnchorDiscovered?.Invoke(instance);
            ActivateDormantQRCodes();

            Debug.Log("[QrCodeManager] RoomAnchor relocalized from Meta spatial anchor. Uuid=" + uuid);
            return true;
        }

        /// <summary>Restores the deferred disk RoomAnchor (existing behavior) when spatial-anchor
        /// relocalization did not run or failed, so item codes still appear without a spatial anchor.</summary>
        private void RestoreDeferredDiskAnchorIfAny()
        {
            if (_deferredDiskAnchor == null || _isAnchorSet) { _deferredDiskAnchor = null; return; }
            var a = _deferredDiskAnchor;
            _deferredDiskAnchor = null;
            UpdateQRCodeFromRemote(a.fullPayload, a.position, a.rotation);
        }

        /// <summary>
        /// Clears/erases the persisted RoomAnchor spatial anchor and its stored UUID for re-calibration.
        /// After this, the next RoomAnchor QR scan establishes (and re-persists) a fresh spatial anchor.
        /// </summary>
        public async void ClearRoomSpatialAnchor()
        {
            try
            {
                if (_roomSpatialAnchor != null && SpatialAnchorsSupported)
                {
                    var res = await _roomSpatialAnchor.EraseAnchorAsync();
                    if (!res.Success)
                        Debug.LogWarning("[QrCodeManager] EraseAnchorAsync failed: " + res.Status);
                }
                if (_roomSpatialAnchor != null) Destroy(_roomSpatialAnchor);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[QrCodeManager] ClearRoomSpatialAnchor error: " + e.Message);
            }
            finally
            {
                _roomSpatialAnchor = null;
                _roomAnchorDrivenBySpatialAnchor = false;
                PlayerPrefs.DeleteKey(RoomAnchorUuidPrefKey);
                PlayerPrefs.DeleteKey(RoomAnchorPayloadPrefKey);
                PlayerPrefs.Save();
                Debug.Log("[QrCodeManager] Cleared persisted RoomAnchor spatial anchor.");
            }
        }

        #endregion

        private void SaveToDisk()
        {
            var list = new List<SerializableQRData>();
            var seen = new HashSet<string>();

            foreach (var inst in _trackedQRCodes.Values)
            {
                if (inst.visualObject == null) continue;
                if (string.IsNullOrEmpty(inst.fullPayload) || IsSignInCode(inst.fullPayload)) continue;
                Vector3 p = inst.visualObject.transform.position;
                Quaternion r = inst.visualObject.transform.rotation;
                if (inst != RoomAnchorInstance && _isAnchorSet) { p = inst.visualObject.transform.localPosition; r = inst.visualObject.transform.localRotation; }
                list.Add(new SerializableQRData { identifierKey = inst.identifierKey, fullPayload = inst.fullPayload, position = p, rotation = r });
                seen.Add(inst.identifierKey);
            }

            // Persist known-but-not-currently-detected item poses (RoomAnchor-relative) so they survive a restart.
            foreach (var kp in _knownPoses.Values)
            {
                if (string.IsNullOrEmpty(kp.payload) || IsSystemCode(kp.payload)) continue;
                string key = GetIdentifierKey(kp.payload);
                if (seen.Contains(key)) continue;
                list.Add(new SerializableQRData { identifierKey = key, fullPayload = kp.payload, position = kp.localPosition, rotation = kp.localRotation });
                seen.Add(key);
            }
            File.WriteAllText(Path.Combine(Application.persistentDataPath, saveFileName), JsonUtility.ToJson(new Wrapper { data = list }, true));
        }

        private void LoadFromDiskAndRestore()
        {
            string path = Path.Combine(Application.persistentDataPath, saveFileName);
            if (!File.Exists(path)) return;
            try {
                Wrapper w = JsonUtility.FromJson<Wrapper>(File.ReadAllText(path));
                if (w?.data == null) return;
                var anchor = w.data.Find(d => d.fullPayload.Contains(qrRoomAnchorLabel));
                // When the RoomAnchor will be relocalized from its Meta spatial anchor, defer establishing it
                // from disk (the spatial anchor is authoritative). Keep it as a fallback if relocalization fails.
                if (anchor != null && _deferRoomAnchorToSpatialAnchor)
                {
                    _deferredDiskAnchor = anchor;
                }
                else if (anchor != null)
                {
                    UpdateQRCodeFromRemote(anchor.fullPayload, anchor.position, anchor.rotation);
                }
                // Items become known-pose DATA only (no markers until physically detected). UpdateQRCodeFromRemote
                // stores the RoomAnchor-relative pose regardless of whether the anchor is set yet.
                foreach (var it in w.data) { if (it == anchor) continue; UpdateQRCodeFromRemote(it.fullPayload, it.position, it.rotation); }
            } catch (Exception e) { Debug.LogError(e.Message); }
        }

        private string GetIdentifierKey(string p) => string.IsNullOrEmpty(p) ? "null" : (p.Length <= 20 ? p : p.Substring(0, 20));

[Serializable] private class CalibrationQRData { public string qrValue; public Vector3 position; public Quaternion rotation; }
        [Serializable] private class CalibrationWrapper { public string headsetId; public List<CalibrationQRData> qrCodes; }
        [Serializable] private class SerializableQRData { public string identifierKey; public string fullPayload; public Vector3 position; public Quaternion rotation; }
        [Serializable] private class Wrapper { public List<SerializableQRData> data; }
        public void StartQRCodeDetection() 
        { 
            IsDetecting = true; 
            EnsureQrTrackingEnabled(); 
            RaiseDetectionState(); 

            // When detection is (re)started, explicitly check for any QR codes that were already 
            // discovered while we were not detecting. This ensures "Cancel Scan" followed by "Scan" 
            // works instantly if the QR is still in view.
            var allTrackables = UnityEngine.Object.FindObjectsByType<MRUKTrackable>(FindObjectsInactive.Exclude);
            foreach (var trackable in allTrackables)
            {
                if (trackable.TrackableType == OVRAnchor.TrackableType.QRCode)
                {
                    OnTrackableAdded(trackable);
                }
            }
        }
        public void StopQRCodeDetection() { IsDetecting = false; RaiseDetectionState(); }

#if UNITY_EDITOR
        /// <summary>
        /// EDITOR-ONLY: simulates detecting a QR code without a headset / MRUKTrackable. Drives the same
        /// downstream behaviour as a real detection — raises OnRawQRDetected (login/setup path + UI feedback)
        /// and, in Full mode, registers a RoomAnchor or item instance so it appears in the world and the
        /// "Look At" dropdown. Compiled OUT of device builds. Use from the TrueEchoVR/Debug menu in Play Mode.
        /// </summary>
        public void SimulateQRDetectionEditor(string payload, Vector3? worldPos = null, Quaternion? worldRot = null)
        {
            if (string.IsNullOrEmpty(payload)) { Debug.LogWarning("[QrCodeManager][SIM] Empty payload ignored."); return; }

            ComputeDefaultSimPose(out Vector3 pos, out Quaternion rot);
            if (worldPos.HasValue) pos = worldPos.Value;
            if (worldRot.HasValue) rot = worldRot.Value;

            if (!IsDetecting) StartQRCodeDetection();

            // 1) Raw detection event (matches the real OnTrackableAdded raw-announce step).
            OnRawQRDetected?.Invoke(payload, pos, rot);

            // 2) LoginOnly mode stops after the raw event (no RoomAnchor / item processing yet).
            if (Mode == ScanMode.LoginOnly)
            {
                Debug.Log($"[QrCodeManager][SIM] LoginOnly raw detection: {payload}");
                return;
            }

            // 3) Full mode: register as anchor / item exactly like the real detection path.
            // The sign-in/setup code is never an item — suppress it in a session.
            if (IsSignInCode(payload))
            {
                Debug.Log($"[QrCodeManager][SIM] Sign-in code ignored during session (not an item): {payload}");
                return;
            }

            string key = GetIdentifierKey(payload);
            bool isAnchor = IsRoomAnchorPayload(payload);

            if (_trackedQRCodes.TryGetValue(key, out var existing))
            {
                existing.lastPosition = pos;
                existing.lastRotation = rot;
                if (isAnchor) { RoomAnchorInstance = existing; OnRoomAnchorDiscovered?.Invoke(existing); ActivateDormantQRCodes(); }
                OnQRCodeUpdated?.Invoke(existing);
                return;
            }

            if (isAnchor)
            {
                RoomAnchorInstance = CreateAndAddInstance(payload, pos, rot, QRStatus.Official, new Vector3(0.15f, 0.15f, 0.005f), true);
                OnRoomAnchorDiscovered?.Invoke(RoomAnchorInstance);
                ActivateDormantQRCodes();
            }
            else
            {
                // Detected item: always create a marker at the world pose (truth). Drop any known-pose data.
                CreateAndAddInstance(payload, pos, rot, IsValidListed(payload) ? QRStatus.Official : QRStatus.Unknown, new Vector3(0.15f, 0.15f, 0.005f), true);
                _knownPoses.Remove(key);
            }
            Debug.Log($"[QrCodeManager][SIM] Full-mode detection registered: {payload}");
        }

        private void ComputeDefaultSimPose(out Vector3 pos, out Quaternion rot)
        {
            var camT = Camera.main != null ? Camera.main.transform : null;
            if (camT != null)
            {
                Vector3 fwd = camT.forward;
                pos = camT.position + fwd * 1.0f;
                Vector3 flat = fwd; flat.y = 0f;
                rot = flat.sqrMagnitude > 0.001f ? Quaternion.LookRotation(-flat.normalized, Vector3.up) : Quaternion.identity;
                return;
            }
            pos = new Vector3(0f, 1.2f, 1.0f);
            rot = Quaternion.identity;
        }
#endif

        public void ManualSave() => SaveToDisk();
        public void ManualLoad() => LoadFromDiskAndRestore();
        public void ManualLoadFromJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return;
            Wrapper wrapper = JsonUtility.FromJson<Wrapper>(json);
            if (wrapper?.data == null) return;
            foreach (var item in wrapper.data)
            {
                if (_isAnchorSet || item.fullPayload.Contains(qrRoomAnchorLabel)) UpdateQRCodeFromRemote(item.fullPayload, item.position, item.rotation);
                else _dormantQRCodes.Add(new CalibrationQRData { qrValue = item.fullPayload, position = item.position, rotation = item.rotation });
            }
            RequestSave();
        }
    }
}
