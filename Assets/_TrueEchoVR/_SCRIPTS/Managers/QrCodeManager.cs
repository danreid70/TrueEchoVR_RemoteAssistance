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

        // Detection is OFF until a phase explicitly starts it (login scan, or the post-sign-in
        // RoomAnchor/item scan). This keeps QR markers from appearing before the user asks to scan.
        public bool IsDetecting { get; private set; } = false;

        /// <summary>
        /// LoginOnly: pre-sign-in. Detected codes only show a (scaled) visual box + raise OnRawQRDetected
        /// for the login-code parse. No RoomAnchor handling and no persistent item instances are created.
        /// Full: post valid sign-in. RoomAnchor is established first, then item codes are synced/persisted.
        /// </summary>
        public enum ScanMode { LoginOnly, Full }
        public ScanMode Mode { get; private set; } = ScanMode.LoginOnly;

        /// <summary>Switches scan phase. Full mode is entered only after a valid sign-in.</summary>
        public void SetScanMode(ScanMode mode)
        {
            Mode = mode;
            Debug.Log("[QrCodeManager] Scan mode -> " + mode);
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

        // QR marker payloads are frequently NOT decoded on the same frame MRUK raises TrackableAdded;
        // the string arrives a few frames later. We defer such trackables here and re-read the payload
        // in Update() so we never process a QR with an empty payload. (This was the cause of "it read
        // once, then never again": detection only succeeded when the payload happened to be ready on
        // the very first frame, and a tracked code never re-fires TrackableAdded.)
        private readonly Dictionary<MRUKTrackable, float> _pendingPayloadTrackables = new Dictionary<MRUKTrackable, float>();
        private const float PendingPayloadTimeoutSeconds = 5f;

        public void SetAnchorEstablished(bool established)
        {
            if (established && _isAnchorSet) ActivateDormantQRCodes();
        }

        private void ActivateDormantQRCodes()
        {
            if (!_isAnchorSet) return;
            var list = new List<CalibrationQRData>(_dormantQRCodes);
            _dormantQRCodes.Clear();
            foreach (var dormant in list)
            {
                UpdateQRCodeFromRemote(dormant.qrValue, dormant.position, dormant.rotation);
            }
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
            if (autoSaveLoad) LoadFromDiskAndRestore();

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

            if (!IsDetecting) return;

            RetryPendingPayloadTrackables();
            EnsureTrackingPeriodically();
            UpdateDetectionMarkers();

            foreach (var inst in _trackedQRCodes.Values)
            {
                if (inst.trackable != null && inst.visualObject != null)
                {
                    Vector3 tPos = inst.trackable.transform.position;
                    Quaternion tRot = inst.trackable.transform.rotation;
                    if (Vector3.Distance(inst.lastPosition, tPos) > positionThreshold || Quaternion.Angle(inst.lastRotation, tRot) > rotationThreshold)
                    {
                        Quaternion cRot = tRot * Quaternion.Euler(0, 180, 0);
                        inst.visualObject.transform.SetPositionAndRotation(tPos, cRot);
                        inst.lastPosition = tPos;
                        inst.lastRotation = cRot;
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
            foreach (var inst in _trackedQRCodes.Values)
            {
                if (inst.visualObject != null) Destroy(inst.visualObject);
            }
            _trackedQRCodes.Clear();
            RoomAnchorInstance = null;
            if (autoSaveLoad) SaveToDisk();
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
            RoomAnchorInstance = null;
            if (autoSaveLoad) SaveToDisk();
        }

        public string GetQRCodeDataAsJson(string headsetId)
        {
            var list = new List<CalibrationQRData>();
            foreach (var inst in _trackedQRCodes.Values)
            {
                Vector3 pos = inst.lastPosition;
                Quaternion rot = inst.lastRotation;

                if (inst != RoomAnchorInstance && inst.visualObject != null && _isAnchorSet)
                {
                    pos = inst.visualObject.transform.localPosition;
                    rot = inst.visualObject.transform.localRotation;
                }

                list.Add(new CalibrationQRData { qrValue = inst.fullPayload, position = pos, rotation = rot });
            }
            return JsonUtility.ToJson(new CalibrationWrapper { headsetId = headsetId, qrCodes = list });
        }

        public void UpdateQRCodeFromRemote(string payload, Vector3 pos, Quaternion rot)
        {
            string key = GetIdentifierKey(payload);
            bool isAnchor = payload.Contains(qrRoomAnchorLabel);

            if (isAnchor)
            {
                if (_trackedQRCodes.TryGetValue(key, out QRCodeInstance existingAnchor))
                {
                    existingAnchor.lastPosition = pos;
                    existingAnchor.lastRotation = rot;
                    if (existingAnchor.visualObject != null)
                    {
                        existingAnchor.visualObject.transform.SetPositionAndRotation(pos, rot);
                        // Suggestion: Apply OVRAnchor persistence here if Meta package is available
                    }
                    RoomAnchorInstance = existingAnchor;
                }
                else
                {
                    RoomAnchorInstance = CreateAndAddInstance(payload, pos, rot, QRStatus.Official, new Vector3(0.15f, 0.15f, 0.005f), true);
                }
                
                // Ensure the RoomAnchor is persistent across scene loads if needed
                // DontDestroyOnLoad(RoomAnchorInstance.visualObject); 
                
                ActivateDormantQRCodes();
            }
            else
            {
                if (_trackedQRCodes.TryGetValue(key, out QRCodeInstance existing))
                {
                    existing.status = QRStatus.Official;
                    if (existing.visualObject != null)
                    {
                        // Anchor-Relative positioning: if we have an anchor, we should use local coordinates
                        if (RoomAnchorInstance != null && RoomAnchorInstance.visualObject != null)
                        {
                            existing.visualObject.transform.SetParent(RoomAnchorInstance.visualObject.transform, true);
                            existing.visualObject.transform.localPosition = pos;
                            existing.visualObject.transform.localRotation = rot;
                        }
                        else
                        {
                            existing.visualObject.transform.SetPositionAndRotation(pos, rot);
                        }
                        UpdateTextOnObject(existing.visualObject, payload);
                    }
else if (_isAnchorSet)
                    {
                         existing.visualObject = CreateVisualObject(payload, pos, rot, QRStatus.Official, new Vector3(0.15f, 0.15f, 0.005f), true);
                    }
                    OnQRCodeUpdated?.Invoke(existing);
                }
                else if (_isAnchorSet)
                {
                    CreateAndAddInstance(payload, pos, rot, QRStatus.Official, new Vector3(0.15f, 0.15f, 0.005f), true, true);
                }
                else
                {
                    _dormantQRCodes.Add(new CalibrationQRData { qrValue = payload, position = pos, rotation = rot });
                }
            }
            if (autoSaveLoad) SaveToDisk();
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
                if (existing.visualObject != null)
                {
                    Quaternion cRot = trackable.transform.rotation * Quaternion.Euler(0, 180, 0);
                    existing.visualObject.transform.SetPositionAndRotation(trackable.transform.position, cRot);
                    existing.lastPosition = trackable.transform.position;
                    existing.lastRotation = cRot;
                    UpdateTextOnObject(existing.visualObject, fullPayload);
                }
                if (isAnchor) { RoomAnchorInstance = existing; OnRoomAnchorDiscovered?.Invoke(existing); ActivateDormantQRCodes(); }
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
            }
            else if (_isAnchorSet)
            {
                var inst = CreateAndAddInstance(fullPayload, trackable.transform.position, trackable.transform.rotation, QRStatus.Unknown, GetTrackableBoxScale(trackable), true);
                inst.trackable = trackable;
            }
            else
            {
                _dormantQRCodes.Add(new CalibrationQRData { qrValue = fullPayload, position = trackable.transform.position, rotation = trackable.transform.rotation });
            }
            if (autoSaveLoad) SaveToDisk();
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
                if (autoSaveLoad) SaveToDisk();
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
            // Unified 4-category colour (green=target, red=invalid, blue=valid-listed, orange=unlisted).
            Color baseColor = GetCategoryColor(ClassifyPayload(payload));
            bool isLegit = payload.Contains(qrRoomAnchorLabel) || payload.Contains("TrueEchoVR") || (payload.Length <= 2 && payload != "null");
            string labelPrefix = isLegit ? "[Legit] " : "[Unknown] ";

            GameObject bg = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bg.name = "VisualBackground";
            bg.transform.SetParent(root.transform);
            bg.transform.localScale = new Vector3(scale.x, scale.y, 0.001f);
            bg.transform.localPosition = Vector3.zero;
            bg.transform.localRotation = Quaternion.identity;
            if (bg.TryGetComponent<BoxCollider>(out var col)) Destroy(col);

            var renderer = bg.GetComponent<Renderer>();
            renderer.material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            renderer.material.color = new Color(baseColor.r, baseColor.g, baseColor.b, 0.3f);
            renderer.material.SetInt("_Surface", 1);
            renderer.material.SetInt("_ZWrite", 0);
            renderer.material.renderQueue = 3000;

            CreateVisualBorder(root.transform, scale, baseColor);

            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "DebugCenter";
            sphere.transform.SetParent(root.transform);
            sphere.transform.localScale = new Vector3(0.015f, 0.015f, 0.015f);
            sphere.transform.localPosition = Vector3.zero;
            if (sphere.TryGetComponent<Collider>(out var sCol)) Destroy(sCol);
            sphere.GetComponent<Renderer>().material = renderer.material;

            // Note: no constant pulse here anymore — only the focused/"pointed-at" code pulses
            // (see FocusQRCode / UpdateFocusGlow). Detection markers fade out to avoid scene clutter.

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
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 0.05f;
            tmp.fontSizeMax = 0.5f;
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
            r.material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            r.material.color = color;
        }

        private void UpdateTextOnObject(GameObject obj, string payload)
        {
            var tmp = obj.GetComponentInChildren<TextMeshPro>();
            if (tmp != null) 
            {
                bool isLegit = payload.Contains(qrRoomAnchorLabel) || payload.Contains("TrueEchoVR") || (payload.Length <= 2 && payload != "null");
                tmp.text = $"{(isLegit ? "[Legit] " : "[Unknown] ")}{payload}";
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

        // ---- Focus glow (single, reusable pulsing halo for the "pointed-at" code) ----
        private GameObject _focusGlow;            // the pulsing halo object
        private Renderer _focusGlowRenderer;
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

        // ---- Classification ----

        public QrMarkerCategory ClassifyPayload(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload)) return QrMarkerCategory.Invalid;
            if (payload.Contains(qrRoomAnchorLabel)) return QrMarkerCategory.Target;     // RoomAnchor
            if (TryParseLoginCode(payload)) return QrMarkerCategory.Target;              // login setup code
            if (_validPayloads.Contains(payload)) return QrMarkerCategory.ValidListed;   // known good
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
            ApplyFocusGlowColor(GetCategoryColor(category));
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

        private void EnsureFocusGlow()
        {
            if (_focusGlow != null) return;
            _focusGlow = new GameObject("QRFocusGlow");
            _focusGlowMpb = new MaterialPropertyBlock();

            var halo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            halo.name = "Halo";
            halo.transform.SetParent(_focusGlow.transform, false);
            if (halo.TryGetComponent<Collider>(out var col)) Destroy(col);
            halo.transform.localScale = Vector3.one * (markerSize * 3f);
            _focusGlowRenderer = halo.GetComponent<Renderer>();
            _focusGlowRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _focusGlowRenderer.receiveShadows = false;

            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            var m = new Material(sh) { name = "QRFocusGlow_Mat" };
            ConfigureTransparent(m, GetCategoryColor(QrMarkerCategory.Target));
            m.enableInstancing = true;
            _focusGlowRenderer.sharedMaterial = m;

            _focusGlow.SetActive(false);
        }

        private void ApplyFocusGlowColor(Color c)
        {
            if (_focusGlowRenderer == null) return;
            _focusGlowRenderer.GetPropertyBlock(_focusGlowMpb);
            _focusGlowMpb.SetColor("_BaseColor", c);
            _focusGlowMpb.SetColor("_Color", c);
            _focusGlowRenderer.SetPropertyBlock(_focusGlowMpb);
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

            // Sit slightly in front of the code so the glow reads clearly.
            _focusGlow.transform.SetPositionAndRotation(
                _focusFollow.position - _focusFollow.forward * 0.01f, _focusFollow.rotation);

            // Pulse: scale + alpha driven by a sine wave -> a discernible "breathing" glow that
            // wraps the focused code (sized to the code, with a margin that breathes).
            float p = (Mathf.Sin(Time.time * 6f) + 1f) * 0.5f;          // 0..1
            float scale = _focusBaseSize * (1.4f + p * 0.6f);           // grows/shrinks around the code
            _focusGlow.transform.localScale = Vector3.one;
            if (_focusGlowRenderer != null)
            {
                _focusGlowRenderer.transform.localScale = Vector3.one * scale;
                Color baseC = GetCategoryColor(_focusCategory);
                baseC.a = 0.25f + p * 0.45f;                            // 0.25..0.70
                ApplyFocusGlowColor(baseC);
            }
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

        private void SaveToDisk()
        {
            var list = new List<SerializableQRData>();
            foreach (var inst in _trackedQRCodes.Values)
            {
                if (inst.visualObject == null) continue;
                Vector3 p = inst.visualObject.transform.position;
                Quaternion r = inst.visualObject.transform.rotation;
                if (inst != RoomAnchorInstance && _isAnchorSet) { p = inst.visualObject.transform.localPosition; r = inst.visualObject.transform.localRotation; }
                list.Add(new SerializableQRData { identifierKey = inst.identifierKey, fullPayload = inst.fullPayload, position = p, rotation = r });
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
                if (anchor != null) UpdateQRCodeFromRemote(anchor.fullPayload, anchor.position, anchor.rotation);
                foreach (var it in w.data) { if (it == anchor) continue; if (_isAnchorSet) UpdateQRCodeFromRemote(it.fullPayload, it.position, it.rotation); else _dormantQRCodes.Add(new CalibrationQRData { qrValue = it.fullPayload, position = it.position, rotation = it.rotation }); }
            } catch (Exception e) { Debug.LogError(e.Message); }
        }

        private string GetIdentifierKey(string p) => string.IsNullOrEmpty(p) ? "null" : (p.Length <= 20 ? p : p.Substring(0, 20));

[Serializable] private class CalibrationQRData { public string qrValue; public Vector3 position; public Quaternion rotation; }
        [Serializable] private class CalibrationWrapper { public string headsetId; public List<CalibrationQRData> qrCodes; }
        [Serializable] private class SerializableQRData { public string identifierKey; public string fullPayload; public Vector3 position; public Quaternion rotation; }
        [Serializable] private class Wrapper { public List<SerializableQRData> data; }
        public void StartQRCodeDetection() { IsDetecting = true; EnsureQrTrackingEnabled(); }
        public void StopQRCodeDetection() => IsDetecting = false;
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
            if (autoSaveLoad) SaveToDisk();
        }
    }
}
