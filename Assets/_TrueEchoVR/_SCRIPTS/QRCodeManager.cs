using Meta.XR.MRUtilityKit;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

namespace TrueEchoVR
{
    public class QRCodeManager : MonoBehaviour
    {
        [System.Serializable]
        public class QRPayloadAction
        {
            public string matchString;
            public GameObject customPrefab;
            public UnityEvent onPayloadMatched;
        }

        [Header("QR Code Tracking")]
        public string qrRoomAnchorLabel = "RoomAnchor";
        public float positionThreshold = 0.02f;
        public float rotationThreshold = 0.5f;

        [Header("Payload Identification")]
        public int payloadIdentifierMaxLength = 20;

        [Header("Actions & Prefabs")]
        public List<QRPayloadAction> payloadActions = new List<QRPayloadAction>();

        [Header("Persistence")]
        public bool autoSaveLoad = true;
        public string saveFileName = "QRDetectedData.json";

        public bool IsDetecting { get; private set; } = true;

        public enum QRStatus { Official, Unknown }

        public class QRCodeInstance
        {
            public GameObject visualObject;
            public string fullPayload;
            public string identifierKey;
            public Vector3 lastPosition;
            public Quaternion lastRotation;
            public QRStatus status = QRStatus.Unknown;
        }

        public System.Action<QRCodeInstance> OnRoomAnchorDiscovered;
        public System.Action<QRCodeInstance> OnQRCodeAdded;
        public System.Action<QRCodeInstance> OnQRCodeUpdated;
        public System.Action<string> OnQRCodeRemoved;

        private Dictionary<string, QRCodeInstance> trackedQRCodes = new Dictionary<string, QRCodeInstance>();
        public IReadOnlyDictionary<string, QRCodeInstance> TrackedQRCodes => trackedQRCodes;

        private bool isAnchorSet = false;
        public void SetAnchorEstablished(bool established) => isAnchorSet = established;

        private void Start()
        {
            if (autoSaveLoad)
            {
                LoadFromDiskAndRestore();
            }
        }

        public void StartQRCodeDetection() => IsDetecting = true;
        public void StopQRCodeDetection() => IsDetecting = false;

        public void ClearQRCodes()
        {
            var keys = new List<string>(trackedQRCodes.Keys);
            foreach (var key in keys)
            {
                OnQRCodeRemoved?.Invoke(key);
                if (trackedQRCodes[key].visualObject != null)
                    Destroy(trackedQRCodes[key].visualObject);
            }
            trackedQRCodes.Clear();
            if (autoSaveLoad) SaveToDisk();
        }

        public string GetQRCodeDataAsJson()
        {
            List<SerializableQRData> list = new List<SerializableQRData>();
            foreach (var kvp in trackedQRCodes)
            {
                list.Add(new SerializableQRData
                {
                    identifierKey = kvp.Value.identifierKey,
                    fullPayload = kvp.Value.fullPayload,
                    position = kvp.Value.lastPosition,
                    rotation = kvp.Value.lastRotation
                });
            }
            return JsonUtility.ToJson(new Wrapper { data = list });
        }

        public void UpdateQRCodeFromRemote(string payload, Vector3 pos, Quaternion rot)
{
            string key = GetIdentifierKey(payload);
            if (trackedQRCodes.TryGetValue(key, out QRCodeInstance existing))
            {
                existing.status = QRStatus.Official;
                existing.visualObject.transform.SetPositionAndRotation(pos, rot);
                existing.lastPosition = pos;
                existing.lastRotation = rot;
                OnQRCodeUpdated?.Invoke(existing);
            }
            else
            {
                CreateAndAddInstance(payload, pos, rot, QRStatus.Official);
            }
            if (autoSaveLoad) SaveToDisk();
        }

        public void OnTrackableAdded(MRUKTrackable trackable)
        {
            if (!IsDetecting || trackable == null || trackable.TrackableType != OVRAnchor.TrackableType.QRCode) return;

            string fullPayload = trackable.MarkerPayloadString ?? "";

            // Special logic for Anchor
            if (!isAnchorSet)
            {
                if (fullPayload.Contains(qrRoomAnchorLabel))
                {
                    string key = GetIdentifierKey(fullPayload);
                    if (!trackedQRCodes.TryGetValue(key, out QRCodeInstance instance))
                    {
                        instance = CreateAndAddInstance(fullPayload, trackable.transform.position, trackable.transform.rotation, QRStatus.Official);
                    }
                    OnRoomAnchorDiscovered?.Invoke(instance);
                }
                return;
            }

            string identifierKey = GetIdentifierKey(fullPayload);

            if (trackedQRCodes.TryGetValue(identifierKey, out QRCodeInstance existing))
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
                }
                if (autoSaveLoad) SaveToDisk();
                return;
            }

            CreateAndAddInstance(fullPayload, trackable.transform.position, trackable.transform.rotation, QRStatus.Unknown);
            if (autoSaveLoad) SaveToDisk();
        }

        private QRCodeInstance CreateAndAddInstance(string payload, Vector3 pos, Quaternion rot, QRStatus status)
        {
            GameObject visualObj = CreateVisualObject(payload, pos, rot, status);
            var instance = new QRCodeInstance
            {
                visualObject = visualObj,
                fullPayload = payload,
                identifierKey = GetIdentifierKey(payload),
                lastPosition = pos,
                lastRotation = rot,
                status = status
            };
            trackedQRCodes.Add(instance.identifierKey, instance);
            OnQRCodeAdded?.Invoke(instance);
            return instance;
        }

        public void OnTrackableRemoved(MRUKTrackable trackable)
        {
            if (!IsDetecting) return;
            if (trackable == null) return;
            if (trackable.TrackableType != OVRAnchor.TrackableType.QRCode) return;

            string fullPayload = trackable.MarkerPayloadString ?? "";
            string identifierKey = GetIdentifierKey(fullPayload);

            if (trackedQRCodes.TryGetValue(identifierKey, out QRCodeInstance instance))
            {
                Destroy(instance.visualObject);
                trackedQRCodes.Remove(identifierKey);
                OnQRCodeRemoved?.Invoke(identifierKey);
                if (autoSaveLoad) SaveToDisk();
            }
        }

        private string GetIdentifierKey(string payload)
        {
            if (payloadIdentifierMaxLength <= 0 || payload.Length <= payloadIdentifierMaxLength)
                return payload;
            return payload.Substring(0, payloadIdentifierMaxLength);
        }

        private GameObject CreateVisualObject(string payload, Vector3 position, Quaternion rotation, QRStatus status)
        {
            GameObject prefabToUse = null;
            foreach (var action in payloadActions)
            {
                if (!string.IsNullOrEmpty(action.matchString) && payload.Contains(action.matchString))
                {
                    prefabToUse = action.customPrefab;
                    break;
                }
            }

            GameObject obj = new GameObject($"QR_{payload.GetHashCode()}");
            obj.transform.SetPositionAndRotation(position, rotation);

            if (prefabToUse != null)
            {
                Instantiate(prefabToUse, obj.transform);
            }
            else
            {
                GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.transform.SetParent(obj.transform);
                cube.transform.localScale = new Vector3(0.15f, 0.15f, 0.01f);
                cube.transform.localPosition = Vector3.zero;
                
                var renderer = cube.GetComponent<Renderer>();
                renderer.material = new Material(Shader.Find("Transparent/Diffuse"));
                renderer.material.color = status == QRStatus.Official ? new Color(0, 1, 0, 0.3f) : new Color(1, 0, 0, 0.3f);

                GameObject textObj = new GameObject("QR_Text");
                textObj.transform.SetParent(obj.transform);
                TextMeshPro tmp = textObj.AddComponent<TextMeshPro>();
                tmp.text = payload;
                tmp.fontSize = 0.2f;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.rectTransform.localPosition = new Vector3(0, 0.1f, 0);
                textObj.transform.localRotation = Quaternion.identity;
            }
            return obj;
        }

        private void UpdateTextOnObject(GameObject obj, string newPayload)
        {
            TextMeshPro tmp = obj.GetComponentInChildren<TextMeshPro>();
            if (tmp != null) tmp.text = newPayload;
        }

        private void InvokePayloadActions(string payload, GameObject visualObject)
        {
            foreach (var action in payloadActions)
            {
                if (!string.IsNullOrEmpty(action.matchString) && payload.Contains(action.matchString))
                {
                    action.onPayloadMatched?.Invoke();
                }
            }
        }

        [System.Serializable]
        private class SerializableQRData
        {
            public string identifierKey;
            public string fullPayload;
            public Vector3 position;
            public Quaternion rotation;
        }

        private void SaveToDisk()
        {
            List<SerializableQRData> saveList = new List<SerializableQRData>();
            foreach (var kvp in trackedQRCodes)
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
            string json = File.ReadAllText(path);
            ManualLoadFromJson(json);
        }

        [System.Serializable]
        private class Wrapper
        {
            public List<SerializableQRData> data;
        }

        public void ManualSave() => SaveToDisk();
        public void ManualLoad() => LoadFromDiskAndRestore();

        public void ManualLoadFromJson(string json)
        {
            Wrapper wrapper = JsonUtility.FromJson<Wrapper>(json);
            if (wrapper?.data == null) return;

            foreach (var item in wrapper.data)
            {
                UpdateQRCodeFromRemote(item.fullPayload, item.position, item.rotation);
            }
            if (autoSaveLoad) SaveToDisk();
        }
    }
}