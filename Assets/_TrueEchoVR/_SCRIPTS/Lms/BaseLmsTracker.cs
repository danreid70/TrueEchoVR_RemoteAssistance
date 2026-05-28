using UnityEngine;

namespace TEVR
{

    public abstract class BaseLmsTracker : MonoBehaviour
    {
        public abstract void Initialize();
        public abstract void LogProgress(string stepId, bool completed, float timestamp);
        public abstract void LogScore(string stepId, float score);
        public abstract void CompleteCourse(string courseId);
    }
}