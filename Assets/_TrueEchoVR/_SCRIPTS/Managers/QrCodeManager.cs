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

        public bool IsDetecting { get; private set; } = true;

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

        private readonly Dictionary<string, QRCodeInstance> _trackedQRCodes = new Dictionary<string, QRCodeInstance>();
        public IReadOnlyDictionary<string, QRCodeInstance> TrackedQRCodes => _trackedQRCodes;

        public QRCodeInstance RoomAnchorInstance { get; private set; }
        private bool _isAnchorSet => RoomAnchorInstance != null;
        private List<CalibrationQRData> _dormantQRCodes = new List<CalibrationQRData>();

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

        private void Start()
{
            if (autoSaveLoad) LoadFromDiskAndRestore();
            if (MRUK.Instance != null)
            {
                MRUK.Instance.SceneSettings.TrackableAdded.AddListener(OnTrackableAdded);
                MRUK.Instance.SceneSettings.TrackableRemoved.AddListener(OnTrackableRemoved);
            }
            
            // Managers in the Bootstrap scene should persist
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Bootstrap")
            {
                DontDestroyOnLoad(gameObject);
            }
        }

        private void Update()
        {
            if (!IsDetecting) return;
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
                        if (_isAnchorSet) 
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
            string key = GetIdentifierKey(fullPayload);
            bool isAnchor = fullPayload.Contains(qrRoomAnchorLabel);
            
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
                RoomAnchorInstance = CreateAndAddInstance(fullPayload, trackable.transform.position, trackable.transform.rotation, QRStatus.Official, trackable.transform.localScale, true);
                RoomAnchorInstance.trackable = trackable;
                OnRoomAnchorDiscovered?.Invoke(RoomAnchorInstance);
                ActivateDormantQRCodes();
            }
            else if (_isAnchorSet)
            {
                var inst = CreateAndAddInstance(fullPayload, trackable.transform.position, trackable.transform.rotation, QRStatus.Unknown, trackable.transform.localScale, true);
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
            string key = GetIdentifierKey(trackable.MarkerPayloadString ?? "");
            if (_trackedQRCodes.TryGetValue(key, out QRCodeInstance instance))
            {
                if (instance.visualObject != null) Destroy(instance.visualObject);
                _trackedQRCodes.Remove(key);
                OnQRCodeRemoved?.Invoke(key);
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

            if (!isAnchor && _isAnchorSet)
            {
                root.transform.SetParent(RoomAnchorInstance.visualObject.transform);
                if (isPosLocal) { root.transform.localPosition = pos; root.transform.localRotation = cRot; }
                else root.transform.SetPositionAndRotation(pos, cRot);
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
            Color baseColor = status == QRStatus.Official ? Color.green : Color.yellow;
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

            var pulse = bg.AddComponent<QRPulseEffect>();
            pulse.targetColor = baseColor;

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

        private class QRPulseEffect : MonoBehaviour { public Color targetColor; private Material _mat; private float _time; void Start() { var r = GetComponent<Renderer>(); if (r != null) _mat = r.material; } void Update() { if (_mat == null) return; _time += Time.deltaTime * 2f; float p = (Mathf.Sin(_time) + 1f) * 0.5f; _mat.color = new Color(targetColor.r, targetColor.g, targetColor.b, 0.1f + (p * 0.2f)); } }
        [Serializable] private class CalibrationQRData { public string qrValue; public Vector3 position; public Quaternion rotation; }
        [Serializable] private class CalibrationWrapper { public string headsetId; public List<CalibrationQRData> qrCodes; }
        [Serializable] private class SerializableQRData { public string identifierKey; public string fullPayload; public Vector3 position; public Quaternion rotation; }
        [Serializable] private class Wrapper { public List<SerializableQRData> data; }
        public void StartQRCodeDetection() => IsDetecting = true;
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
