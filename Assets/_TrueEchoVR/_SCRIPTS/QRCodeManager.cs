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
        [Tooltip("Max position change (meters) before updating the visual object.")]
        public float positionThreshold = 0.05f;
        [Tooltip("Max euler angle change (degrees) before updating the visual object.")]
        public float rotationThreshold = 0.05f;

        [Header("Payload Identification")]
        [Tooltip("How many characters of the payload to use as the unique key. Set to 0 or negative to use the full payload.")]
        public int payloadIdentifierMaxLength = 20;

        [Header("Actions & Prefabs")]
        public List<QRPayloadAction> payloadActions = new List<QRPayloadAction>();

        [Header("Persistence")]
        public bool autoSaveLoad = true;
        public string saveFileName = "QRDetectedData.json";

        public bool IsDetecting { get; private set; } = true;

        // Public class to expose tracked QR code information
        public class QRCodeInstance
        {
            public GameObject visualObject;
            public string fullPayload;
            public string identifierKey;
            public Vector3 lastPosition;
            public Quaternion lastRotation;
        }

        // Events for UI binding
        public System.Action<QRCodeInstance> OnQRCodeAdded;
        public System.Action<QRCodeInstance> OnQRCodeUpdated;
        public System.Action<string> OnQRCodeRemoved; // identifierKey

        // Public read-only access to tracked QR codes
        private Dictionary<string, QRCodeInstance> trackedQRCodes = new Dictionary<string, QRCodeInstance>();
        public IReadOnlyDictionary<string, QRCodeInstance> TrackedQRCodes => trackedQRCodes;

        public void StartQRCodeDetection() => IsDetecting = true;
        public void StopQRCodeDetection() => IsDetecting = false;

        public void ClearQRCodes()
        {
            foreach (var instance in trackedQRCodes.Values)
            {
                if (instance.visualObject != null) Destroy(instance.visualObject);
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
            return JsonUtility.ToJson(new { data = list });
        }

        public void UpdateQRCodeFromRemote(string payload, Vector3 pos, Quaternion rot)
        {
            string key = GetIdentifierKey(payload);
            if (trackedQRCodes.TryGetValue(key, out QRCodeInstance existing))
            {
                existing.visualObject.transform.SetPositionAndRotation(pos, rot);
                existing.lastPosition = pos;
                existing.lastRotation = rot;
                existing.fullPayload = payload; // update payload in case it changed
                OnQRCodeUpdated?.Invoke(existing);
            }
            else
            {
                GameObject visualObj = CreateVisualObject(payload, pos, rot);
                var instance = new QRCodeInstance
                {
                    visualObject = visualObj,
                    fullPayload = payload,
                    identifierKey = key,
                    lastPosition = pos,
                    lastRotation = rot
                };
                trackedQRCodes.Add(key, instance);
                OnQRCodeAdded?.Invoke(instance);
            }
            if (autoSaveLoad) SaveToDisk();
        }

        public void OnTrackableAdded(MRUKTrackable trackable)
        {
            if (!IsDetecting) return;
            if (trackable == null) return;
            if (trackable.TrackableType != OVRAnchor.TrackableType.QRCode) return;

            string fullPayload = trackable.MarkerPayloadString ?? "";
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
                InvokePayloadActions(fullPayload, existing.visualObject);
                if (autoSaveLoad) SaveToDisk();
                return;
            }

            GameObject visualObj = CreateVisualObject(fullPayload, trackable.transform.position, trackable.transform.rotation);
            var instance = new QRCodeInstance
            {
                visualObject = visualObj,
                fullPayload = fullPayload,
                identifierKey = identifierKey,
                lastPosition = visualObj.transform.position,
                lastRotation = visualObj.transform.rotation
            };
            trackedQRCodes.Add(identifierKey, instance);
            InvokePayloadActions(fullPayload, visualObj);
            OnQRCodeAdded?.Invoke(instance);
            if (autoSaveLoad) SaveToDisk();
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

        private GameObject CreateVisualObject(string payload, Vector3 position, Quaternion rotation)
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

            GameObject obj;
            if (prefabToUse != null)
            {
                obj = Instantiate(prefabToUse, position, rotation);
            }
            else
            {
                obj = new GameObject($"QR_Display_{payload.GetHashCode()}");
                obj.transform.SetPositionAndRotation(position, rotation);

                GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.transform.SetParent(obj.transform);
                cube.transform.localScale = Vector3.one * 0.2f;
                cube.transform.localPosition = Vector3.zero;

                GameObject textObj = new GameObject("QR_Text");
                textObj.transform.SetParent(obj.transform);
                TextMeshPro tmp = textObj.AddComponent<TextMeshPro>();
                tmp.text = payload;
                tmp.fontSize = 0.5f;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.color = Color.black;
                tmp.rectTransform.sizeDelta = new Vector2(1.0f, 0.5f);
                tmp.rectTransform.localPosition = new Vector3(0, 0.15f, 0);
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
            string json = JsonUtility.ToJson(new { data = saveList }, true);
            File.WriteAllText(Path.Combine(Application.persistentDataPath, saveFileName), json);
        }

        private void LoadFromDisk()
        {
            string path = Path.Combine(Application.persistentDataPath, saveFileName);
            if (!File.Exists(path)) return;

            string json = File.ReadAllText(path);
            Wrapper wrapper = JsonUtility.FromJson<Wrapper>(json);
            if (wrapper?.data == null) return;

            Debug.Log($"Loaded {wrapper.data.Count} previous QR records. They are kept for reference only – objects will be re‑created upon real detection.");
        }

        [System.Serializable]
        private class Wrapper
        {
            public List<SerializableQRData> data;
        }

        public void ManualSave() => SaveToDisk();
        public void ManualLoad() => LoadFromDisk();

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