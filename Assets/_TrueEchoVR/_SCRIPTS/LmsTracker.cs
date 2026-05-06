using UnityEngine;

namespace TrueEchoVR
{

    public abstract class LmsTracker : MonoBehaviour
    {
        public abstract void Initialize();
        public abstract void LogProgress(string stepId, bool completed, float timestamp);
        public abstract void LogScore(string stepId, float score);
        public abstract void CompleteCourse(string courseId);
    }
}