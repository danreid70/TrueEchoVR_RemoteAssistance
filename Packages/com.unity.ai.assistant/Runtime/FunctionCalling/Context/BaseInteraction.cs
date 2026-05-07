using System;
using System.Threading.Tasks;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.FunctionCalling
{
    abstract class BaseInteraction<TOutput> : VisualElement, IInteractionSource<TOutput>
    {
        public event Action<TOutput> OnCompleted;

        public TaskCompletionSource<TOutput> TaskCompletionSource { get; } = new();

        protected void CompleteInteraction(TOutput output)
        {
            if (!TaskCompletionSource.TrySetResult(output))
                return;
            OnCompleted?.Invoke(output);
        }

        public void CancelInteraction()
        {
            if (!TaskCompletionSource.TrySetCanceled())
                return;
            try
            {
                OnCanceled();
            }
            finally
            {
                OnCompleted?.Invoke(default);
            }
        }

        protected virtual void OnCanceled()
        {
            // Subclasses may override to react to cancellation.
        }
    }
}
