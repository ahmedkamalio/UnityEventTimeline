#nullable enable

#if (DEVELOPMENT_BUILD || UNITY_EDITOR) && EVENTTIMELINE_DEBUG
#define __EVENTTIMELINE_DEBUG
#endif

#if __EVENTTIMELINE_DEBUG && EVENTTIMELINE_DEBUG_VERBOSE
#define __EVENTTIMELINE_DEBUG_VERBOSE
#endif

using System;
using System.Threading;
using UnityEngine;

#if __EVENTTIMELINE_DEBUG || __EVENTTIMELINE_DEBUG_VERBOSE
using UnityEventTimeline.Internal.Logger;
#endif

namespace UnityEventTimeline.Internal
{
    /// <summary>
    /// Base class for the event timeline system providing core timing and state management functionality.
    /// </summary>
    /// <remarks>
    /// Provides fundamental timeline controls including pause state and time scaling.
    /// Serves as the foundation for more specialized timeline implementations.
    /// </remarks>
    public class EventTimelineBase : MonoBehaviour
    {
        /// <summary>
        /// Indicates whether event processing is currently paused.
        /// </summary>
        [SerializeField]
        protected bool isPaused;

        /// <summary>
        /// The scale factor applied to time progression.
        /// </summary>
        /// <remarks>
        /// Default value is 1f for normal time progression.
        /// Values less than 1 slow time down, greater than 1 speed it up.
        /// </remarks>
        [SerializeField]
        protected float timeScale = 1f;

        /// <summary>
        /// Reference to the Unity main thread for ensuring proper thread synchronization.
        /// </summary>
        private Thread? _mainThread;

        /// <summary>
        /// Synchronization context for the main Unity thread.
        /// </summary>
        /// <remarks>
        /// Used to marshal operations back to the main thread when required by Unity's threading model.
        /// </remarks>
        private SynchronizationContext? _mainThreadContext;

        /// <summary>
        /// Gets whether the current executing thread is the Unity main thread.
        /// </summary>
        /// <remarks>
        /// This property compares the current thread against the stored reference to the main Unity thread.
        /// Used for ensuring operations that must run on the main thread are properly synchronized.
        /// </remarks>
        protected bool IsMainThread => Thread.CurrentThread == _mainThread;

        /// <summary>
        /// Sets the time scale for event execution.
        /// </summary>
        /// <param name="scale">
        /// The new time scale factor. 1 is normal time, less than 1 is slower, greater than 1 is faster.
        /// </param>
        public void SetTimeScale(float scale)
        {
#if __EVENTTIMELINE_DEBUG_VERBOSE
            AsyncLogger.LogFormat("[EventTimelineBase] Setting time scale to {0}", scale);
#endif

            timeScale = scale;
        }

        /// <summary>
        /// Sets the paused state of the timeline.
        /// </summary>
        /// <param name="paused">True to pause event processing, false to resume.</param>
        public void SetPaused(bool paused)
        {
#if __EVENTTIMELINE_DEBUG_VERBOSE
            AsyncLogger.LogFormat("[EventTimelineBase] Setting paused to {0}", paused);
#endif

            isPaused = paused;
        }

        /// <summary>
        /// Executes an action on the Unity main thread.
        /// </summary>
        /// <param name="action">The action to execute on the main thread.</param>
        /// <remarks>
        /// If the current thread is already the main thread, executes the action immediately.
        /// Otherwise, posts the action to be executed on the next frame using the main thread's synchronization context.
        /// This ensures Unity-specific operations are always executed in a thread-safe manner.
        /// </remarks>
        public void RunOnMainThread(Action action)
        {
            if (IsMainThread)
            {
#if __EVENTTIMELINE_DEBUG_VERBOSE
                AsyncLogger.Log("[EventTimelineBase] Already on main thread, executing action immediately.");
#endif

                action();
            }
            else
            {
#if __EVENTTIMELINE_DEBUG_VERBOSE
                AsyncLogger.Log("[EventTimelineBase] Posting action to main thread.");
#endif

                _mainThreadContext?.Post(_ => action(), null);
            }
        }

        /// <summary>
        /// Called when the script instance is initialized.
        /// </summary>
        protected virtual void Awake()
        {
        }

        /// <summary>
        /// Called every frame if the script is enabled.
        /// </summary>
        protected virtual void Update()
        {
        }

        /// <summary>
        /// Called when the script becomes enabled and active.
        /// </summary>
        protected virtual void OnEnable()
        {
            _mainThread = Thread.CurrentThread;
            _mainThreadContext = SynchronizationContext.Current;
        }

        /// <summary>
        /// Called when the script becomes disabled or inactive.
        /// </summary>
        protected virtual void OnDisable()
        {
        }

        /// <summary>
        /// Called when the MonoBehaviour will be destroyed.
        /// </summary>
        protected virtual void OnDestroy()
        {
        }
    }
}