using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

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
        var statement = new
        {
            actor = new { objectType = "Agent", mbox = "mailto:learner@example.com" },
            verb = new { id = completed ? "http://adlnet.gov/expapi/verbs/completed" : "http://adlnet.gov/expapi/verbs/attempted" },
            @object = new { id = $"http://training.company.com/activities/{stepId}", objectType = "Activity" }
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