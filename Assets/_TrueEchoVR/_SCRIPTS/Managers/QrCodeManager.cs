using Meta.XR.MRUtilityKit;
using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

namespace TEVR
{
    /// <summary>
    /// Manages the detection, visualization, and persistence of QR code markers in the MR scene.
    /// Integrated with Meta MR Utility Kit (MRUK).
    /// </summary>
    public class QrCodeManager : MonoBehaviour
    {
        [Serializable]
        public class QRPayloadAction
        {
            public string matchString;
            public GameObject customPrefab;
            public UnityEvent onPayloadMatched;
        }

        [Header("QR Code Tracking Settings")]
        [Tooltip("Label used to identify the room's main anchor QR code.")]
        public string qrRoomAnchorLabel = "RoomAnchor";
        
        [Tooltip("Minimum movement required to trigger a position update event.")]
        public float positionThreshold = 0.02f;
        
        [Tooltip("Minimum rotation change (degrees) required to trigger an update event.")]
        public float rotationThreshold = 0.5f;

        [Header("Payload Configuration")]
        [Tooltip("Maximum length of the payload string used for unique identification keys.")]
        public int payloadIdentifierMaxLength = 20;

        [Header("Visualization & Prefabs")]
        [Tooltip("Specific actions or prefabs to trigger when a matching QR payload is detected.")]
        public List<QRPayloadAction> payloadActions = new List<QRPayloadAction>();

        [Header("Persistence")]
        [Tooltip("Automatically save detected QR codes to disk and load them on startup.")]
        public bool autoSaveLoad = true;
        public string saveFileName = "QRDetectedData.json";

        /// <summary>
        /// Current state of QR code detection.
        /// </summary>
        public bool IsDetecting { get; private set; } = true;

        public enum QRStatus { Official, Unknown }

        /// <summary>
        /// Represents a specific instance of a detected QR code in the world.
        /// </summary>
        public class QRCodeInstance
        {
            public GameObject visualObject;
            public string fullPayload;
            public string identifierKey;
            public Vector3 lastPosition;
            public Quaternion lastRotation;
            public QRStatus status = QRStatus.Unknown;
        }

        // Events
        public Action<QRCodeInstance> OnRoomAnchorDiscovered;
        public Action<QRCodeInstance> OnQRCodeAdded;
        public Action<QRCodeInstance> OnQRCodeUpdated;
        public Action<string> OnQRCodeRemoved;

        private readonly Dictionary<string, QRCodeInstance> _trackedQRCodes = new Dictionary<string, QRCodeInstance>();
        
        /// <summary>
        /// All currently tracked QR codes, keyed by their identifier payload.
        /// </summary>
        public IReadOnlyDictionary<string, QRCodeInstance> TrackedQRCodes => _trackedQRCodes;

        private bool _isAnchorSet = false;
        private List<CalibrationQRData> _dormantQRCodes = new List<CalibrationQRData>();

        /// <summary>
        /// Sets whether the room anchor has been established.
        /// When false, non-anchor QR codes are ignored to ensure spatial consistency.
        /// </summary>
        public void SetAnchorEstablished(bool established)
        {
            _isAnchorSet = established;
            if (_isAnchorSet)
            {
                ActivateDormantQRCodes();
            }
        }

        private void ActivateDormantQRCodes()
        {
            foreach (var dormant in _dormantQRCodes)
            {
                UpdateQRCodeFromRemote(dormant.qrValue, dormant.position, dormant.rotation);
            }
            _dormantQRCodes.Clear();
        }

        private void Start()
        {
            if (autoSaveLoad)
            {
                LoadFromDiskAndRestore();
            }
        }

        public void StartQRCodeDetection() => IsDetecting = true;
        public void StopQRCodeDetection() => IsDetecting = false;

        /// <summary>
        /// Removes all tracked QR codes and clears local persistence.
        /// </summary>
        public void ClearQRCodes()
        {
            var keys = new List<string>(_trackedQRCodes.Keys);
            foreach (var key in keys)
            {
                OnQRCodeRemoved?.Invoke(key);
                if (_trackedQRCodes[key].visualObject != null)
                {
                    Destroy(_trackedQRCodes[key].visualObject);
                }
            }
            _trackedQRCodes.Clear();
            if (autoSaveLoad) SaveToDisk();
        }

        /// <summary>
        /// Serializes all currently tracked QR codes into a JSON string matching the Replit calibration structure.
        /// </summary>
        public string GetQRCodeDataAsJson(string headsetId)
        {
            var list = new List<CalibrationQRData>();
            foreach (var kvp in _trackedQRCodes)
            {
                list.Add(new CalibrationQRData
                {
                    qrValue = kvp.Value.fullPayload,
                    position = kvp.Value.lastPosition,
                    rotation = kvp.Value.lastRotation
                });
            }
            return JsonUtility.ToJson(new CalibrationWrapper { headsetId = headsetId, qrCodes = list });
        }

        /// <summary>
        /// Forces an update or creation of a QR code based on remote data (e.g., from the Expert console).
        /// </summary>
        public void UpdateQRCodeFromRemote(string payload, Vector3 pos, Quaternion rot)
        {
            string key = GetIdentifierKey(payload);
            bool isAnchor = payload.Contains(qrRoomAnchorLabel);

            if (_trackedQRCodes.TryGetValue(key, out QRCodeInstance existing))
            {
                existing.status = QRStatus.Official;
                existing.lastPosition = pos;
                existing.lastRotation = rot;

                if (existing.visualObject != null)
                {
                    existing.visualObject.transform.SetPositionAndRotation(pos, rot);
                    UpdateTextOnObject(existing.visualObject, payload);
                }
                else if (_isAnchorSet || isAnchor)
                {
                    // Create visual if it was missing and we are now allowed to show it
                    existing.visualObject = CreateVisualObject(payload, pos, rot, QRStatus.Official, new Vector3(0.15f, 0.15f, 0.005f));
                }
                
                OnQRCodeUpdated?.Invoke(existing);
            }
            else
            {
                // Create instance but only create visual if anchor is set or it IS the anchor
                CreateAndAddInstance(payload, pos, rot, QRStatus.Official, new Vector3(0.15f, 0.15f, 0.005f), _isAnchorSet || isAnchor);
            }
            if (autoSaveLoad) SaveToDisk();
        }

        /// <summary>
        /// Callback for MRUK when a new trackable object is detected.
        /// </summary>
        public void OnTrackableAdded(MRUKTrackable trackable)
        {
            Debug.Log($"[QrCodeManager] Trackable Added: {trackable?.name}, Type: {trackable?.TrackableType}");
            if (!IsDetecting || trackable == null || trackable.TrackableType != OVRAnchor.TrackableType.QRCode) return;

            string fullPayload = trackable.MarkerPayloadString ?? "";
            string identifierKey = GetIdentifierKey(fullPayload);
            bool isAnchorMarker = fullPayload.Contains(qrRoomAnchorLabel);
            
            Debug.Log($"[QrCodeManager] Detected QR: {fullPayload}, IsAnchor: {isAnchorMarker}, Pos: {trackable.transform.position}");

            // Allow updates for existing QR codes even if the anchor isn't set
            // This ensures stale disk data is corrected as soon as the marker is seen.
            if (_trackedQRCodes.TryGetValue(identifierKey, out QRCodeInstance existing))
            {
                Vector3 newPos = trackable.transform.position;
                Quaternion newRot = trackable.transform.rotation;

                if (Vector3.Distance(existing.lastPosition, newPos) > positionThreshold ||
                    Quaternion.Angle(existing.lastRotation, newRot) > rotationThreshold)
                {
                    existing.visualObject.transform.SetPositionAndRotation(newPos, newRot);
                    existing.lastPosition = newPos;
                    existing.lastRotation = newRot;
                    UpdateTextOnObject(existing.visualObject, fullPayload);
                    OnQRCodeUpdated?.Invoke(existing);
                    
                    if (isAnchorMarker) OnRoomAnchorDiscovered?.Invoke(existing);
                }
                return;
            }

            // Ignore NEW peripheral QR codes until the room anchor is established
            if (!_isAnchorSet && !isAnchorMarker) return;

            QRCodeInstance instance = CreateAndAddInstance(fullPayload, trackable.transform.position, trackable.transform.rotation, 
                isAnchorMarker ? QRStatus.Official : QRStatus.Unknown, trackable.transform.localScale, true);
            
            if (isAnchorMarker) OnRoomAnchorDiscovered?.Invoke(instance);
            
            if (autoSaveLoad) SaveToDisk();
        }

        private QRCodeInstance CreateAndAddInstance(string payload, Vector3 pos, Quaternion rot, QRStatus status, Vector3 scale, bool createVisual)
        {
            GameObject visualObj = createVisual ? CreateVisualObject(payload, pos, rot, status, scale) : null;
            var instance = new QRCodeInstance
            {
                visualObject = visualObj,
                fullPayload = payload,
                identifierKey = GetIdentifierKey(payload),
                lastPosition = pos,
                lastRotation = rot,
                status = status
            };
            _trackedQRCodes.Add(instance.identifierKey, instance);
            OnQRCodeAdded?.Invoke(instance);
            return instance;
        }

        /// <summary>
        /// Callback for MRUK when a trackable object is removed.
        /// </summary>
        public void OnTrackableRemoved(MRUKTrackable trackable)
        {
            if (!IsDetecting || trackable == null || trackable.TrackableType != OVRAnchor.TrackableType.QRCode) return;

            string fullPayload = trackable.MarkerPayloadString ?? "";
            string identifierKey = GetIdentifierKey(fullPayload);

            if (_trackedQRCodes.TryGetValue(identifierKey, out QRCodeInstance instance))
            {
                if (instance.visualObject != null) Destroy(instance.visualObject);
                _trackedQRCodes.Remove(identifierKey);
                OnQRCodeRemoved?.Invoke(identifierKey);
                if (autoSaveLoad) SaveToDisk();
            }
        }

        private string GetIdentifierKey(string payload)
        {
            if (string.IsNullOrEmpty(payload)) return "null";
            if (payloadIdentifierMaxLength <= 0 || payload.Length <= payloadIdentifierMaxLength)
                return payload;
            return payload.Substring(0, payloadIdentifierMaxLength);
        }

        private GameObject CreateVisualObject(string payload, Vector3 position, Quaternion rotation, QRStatus status, Vector3 scale)
        {
            GameObject prefabToUse = null;
            foreach (var action in payloadActions)
            {
                if (!string.IsNullOrEmpty(action.matchString) && payload.Contains(action.matchString))
                {
                    prefabToUse = action.customPrefab;
                    action.onPayloadMatched?.Invoke();
                    break;
                }
            }

            GameObject root = new GameObject($"QR_Instance_{payload.GetHashCode()}");
            root.transform.SetPositionAndRotation(position, rotation);

            if (prefabToUse != null)
            {
                Instantiate(prefabToUse, root.transform);
            }
            else
            {
                CreateDefaultVisualization(root, payload, status, scale);
            }
            return root;
        }

        private void CreateDefaultVisualization(GameObject root, string payload, QRStatus status, Vector3 scale)
        {
            Color baseColor = status == QRStatus.Official ? Color.green : Color.red;

            // Background Plane
            GameObject bg = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bg.name = "VisualBackground";
            bg.transform.SetParent(root.transform);
            bg.transform.localScale = new Vector3(scale.x, scale.y, 0.001f);
            bg.transform.localPosition = Vector3.zero;
            bg.transform.localRotation = Quaternion.identity;
            if (bg.TryGetComponent<BoxCollider>(out var col)) Destroy(col);

            var renderer = bg.GetComponent<Renderer>();
            renderer.material = new Material(Shader.Find("Transparent/Diffuse"));
            renderer.material.color = new Color(baseColor.r, baseColor.g, baseColor.b, 0.25f);

            // Borders
            CreateVisualBorder(root.transform, scale, baseColor);

            // Add Pulse Effect for visibility
            if (status == QRStatus.Official)
            {
                var pulse = bg.AddComponent<QRPulseEffect>();
                pulse.targetColor = baseColor;
            }

            // Text Label
GameObject textObj = new GameObject("PayloadLabel");
            textObj.transform.SetParent(root.transform);
            textObj.transform.localPosition = new Vector3(0, 0, -0.005f);
            textObj.transform.localRotation = Quaternion.identity;

            var tmp = textObj.AddComponent<TextMeshPro>();
            tmp.text = payload;
            tmp.fontSize = 0.12f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            tmp.rectTransform.sizeDelta = new Vector2(scale.x * 2.0f, scale.y * 2.0f); // Larger bounding box
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 0.05f;
            tmp.fontSizeMax = 0.5f;
            tmp.margin = new Vector4(0.01f, 0.01f, 0.01f, 0.01f);
        }

        private void CreateVisualBorder(Transform parent, Vector3 scale, Color color)
        {
            float thickness = 0.006f;
            CreateBorderBar(parent, new Vector3(0, scale.y / 2, 0), new Vector3(scale.x + thickness, thickness, thickness), color);
            CreateBorderBar(parent, new Vector3(0, -scale.y / 2, 0), new Vector3(scale.x + thickness, thickness, thickness), color);
            CreateBorderBar(parent, new Vector3(-scale.x / 2, 0, 0), new Vector3(thickness, scale.y + thickness, thickness), color);
            CreateBorderBar(parent, new Vector3(scale.x / 2, 0, 0), new Vector3(thickness, scale.y + thickness, thickness), color);
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
            r.material = new Material(Shader.Find("Unlit/Color"));
            r.material.color = color;
        }

        private void UpdateTextOnObject(GameObject obj, string newPayload)
        {
            var tmp = obj.GetComponentInChildren<TextMeshPro>();
            if (tmp != null) tmp.text = newPayload;
        }

        private class QRPulseEffect : MonoBehaviour
        {
            public Color targetColor;
            private Material _mat;
            private float _time;

            void Start() 
            { 
                var r = GetComponent<Renderer>();
                if (r != null) _mat = r.material; 
            }
            void Update()
            {
                if (_mat == null) return;
                _time += Time.deltaTime * 2f;
                float pulse = (Mathf.Sin(_time) + 1f) * 0.5f;
                _mat.color = new Color(targetColor.r, targetColor.g, targetColor.b, 0.1f + (pulse * 0.2f));
            }
        }

        [Serializable]
private class CalibrationQRData
        {
            public string qrValue;
            public Vector3 position;
            public Quaternion rotation;
        }

        [Serializable]
        private class CalibrationWrapper
        {
            public string headsetId;
            public List<CalibrationQRData> qrCodes;
        }

        [Serializable]
        private class SerializableQRData
        {
            public string identifierKey;
            public string fullPayload;
            public Vector3 position;
            public Quaternion rotation;
        }

        private void SaveToDisk()
        {
            var saveList = new List<SerializableQRData>();
            foreach (var kvp in _trackedQRCodes)
            {
                saveList.Add(new SerializableQRData
                {
                    identifierKey = kvp.Value.identifierKey,
                    fullPayload = kvp.Value.fullPayload,
                    position = kvp.Value.visualObject.transform.position,
                    rotation = kvp.Value.visualObject.transform.rotation
                });
            }
            string json = JsonUtility.ToJson(new Wrapper { data = saveList }, true);
            File.WriteAllText(Path.Combine(Application.persistentDataPath, saveFileName), json);
        }

        private void LoadFromDiskAndRestore()
        {
            string path = Path.Combine(Application.persistentDataPath, saveFileName);
            if (!File.Exists(path)) return;
            try
            {
                string json = File.ReadAllText(path);
                ManualLoadFromJson(json);
            }
            catch (Exception e)
            {
                Debug.LogError($"[QrCodeManager] Failed to load QR data: {e.Message}");
            }
        }

        [Serializable]
        private class Wrapper { public List<SerializableQRData> data; }

        public void ManualSave() => SaveToDisk();
        public void ManualLoad() => LoadFromDiskAndRestore();

        /// <summary>
        /// Manually loads QR code data from a JSON string.
        /// </summary>
        public void ManualLoadFromJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return;
            Wrapper wrapper = JsonUtility.FromJson<Wrapper>(json);
            if (wrapper?.data == null) return;

            foreach (var item in wrapper.data)
            {
                if (_isAnchorSet || item.fullPayload.Contains(qrRoomAnchorLabel))
                {
                    UpdateQRCodeFromRemote(item.fullPayload, item.position, item.rotation);
                }
                else
                {
                    _dormantQRCodes.Add(new CalibrationQRData { qrValue = item.fullPayload, position = item.position, rotation = item.rotation });
                }
            }
            if (autoSaveLoad) SaveToDisk();
        }
}
}