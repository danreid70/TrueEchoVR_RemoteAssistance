using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace TrueEchoVR
{
    public class XApiTracker : LmsTracker
    {
        [SerializeField] private string lrsEndpoint = "https://lrs.example.com/data/xAPI";
        [SerializeField] private string authToken = "";

        public override void Initialize()
        {
            Debug.Log("xAPI tracker initialized");
        }

        public override void LogProgress(string stepId, bool completed, float timestamp)
        {
            var statement = new XApiStatement
            {
                actor = new XApiActor { objectType = "Agent", mbox = "mailto:learner@example.com" },
                verb = new XApiVerb { id = completed ? "http://adlnet.gov/expapi/verbs/completed" : "http://adlnet.gov/expapi/verbs/attempted" },
                @object = new XApiObject { id = $"http://training.company.com/activities/{stepId}", objectType = "Activity" }
            };
            string json = JsonUtility.ToJson(statement);
            StartCoroutine(SendStatement(json));
        }

        private IEnumerator SendStatement(string json)
        {
            using (var request = new UnityWebRequest(lrsEndpoint, "POST"))
            {
                byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Authorization", "Basic " + authToken);
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                    Debug.LogError($"xAPI send failed: {request.error}");
            }
        }

        public override void LogScore(string stepId, float score) { /* ... */ }
        public override void CompleteCourse(string courseId) { /* ... */ }
        }

        [System.Serializable] public class XApiStatement { public XApiActor actor; public XApiVerb verb; public XApiObject @object; }
        [System.Serializable] public class XApiActor { public string objectType; public string mbox; }
        [System.Serializable] public class XApiVerb { public string id; }
        [System.Serializable] public class XApiObject { public string id; public string objectType; }
        }
