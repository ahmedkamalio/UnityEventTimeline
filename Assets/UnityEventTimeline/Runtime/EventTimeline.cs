#nullable enable

#if (DEVELOPMENT_BUILD || UNITY_EDITOR) && EVENTTIMELINE_DEBUG
#define __EVENTTIMELINE_DEBUG
#endif

#if __EVENTTIMELINE_DEBUG && EVENTTIMELINE_DEBUG_VERBOSE
#define __EVENTTIMELINE_DEBUG_VERBOSE
#endif

using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEventTimeline.Internal;

#if __EVENTTIMELINE_DEBUG || __EVENTTIMELINE_DEBUG_VERBOSE
using UnityEventTimeline.Internal.Logger;
#endif

namespace UnityEventTimeline
{
    /// <summary>
    /// A MonoBehaviour-based event scheduling and timeline management system for Unity.
    /// Provides functionality for scheduling, managing, and executing time-based events.
    /// </summary>
    /// <remarks>
    /// This class implements the Singleton pattern and provides thread-safe event management.
    /// Events are processed during the Update loop and can be paused, scaled, and cleared.
    /// The system includes object pooling for efficient event recycling and a model system
    /// for maintaining state.
    /// </remarks>
    public class EventTimeline : EventTimelineQueue
    {
        /// <summary>
        /// The singleton instance of the EventTimeline.
        /// </summary>
        /// <remarks>
        /// This field is marked as nullable to support proper initialization timing.
        /// </remarks>
        private static EventTimeline? _instance;

        /// <summary>
        /// An object used to ensure thread safety for the singleton instance of the EventTimeline.
        /// </summary>
        /// <remarks>
        /// This lock is used to synchronize access to the singleton instance during initialization,
        /// preventing multiple threads from creating separate instances.
        /// </remarks>
        private static readonly object SingletonLock = new();

        /// <summary>
        /// Gets the singleton instance of the EventTimeline.
        /// </summary>
        /// <remarks>
        /// Creates a new GameObject with EventTimeline component if one doesn't exist.
        /// The instance is marked as DontDestroyOnLoad to persist between scenes.
        /// </remarks>
        public static EventTimeline Instance
        {
            get
            {
                if (_instance is not null)
                {
                    return _instance;
                }

                lock (SingletonLock)
                {
#if __EVENTTIMELINE_DEBUG_VERBOSE
                    AsyncLogger.Log("[EventTimeline] Creating new instance");
#endif
                    var go = new GameObject(nameof(EventTimeline));
                    _instance = go.AddComponent<EventTimeline>();
                    DontDestroyOnLoad(go);
                }

                return _instance;
            }
        }

        /// <summary>
        /// A MonoBehaviour-based event scheduling and timeline management system for Unity.
        /// Provides functionality for scheduling, managing, and executing time-based events.
        /// </summary>
        /// <remarks>
        /// This class implements the Singleton pattern and provides thread-safe event management.
        /// Events are processed during the Update loop and can be paused, scaled, and cleared.
        /// The system includes object pooling for efficient event recycling and a model system
        /// for maintaining state.
        /// </remarks>
        private EventTimeline()
        {
        }

        /// <summary>
        /// Unity Awake callback that handles singleton instance setup.
        /// </summary>
        /// <remarks>
        /// Ensures only one instance exists by destroying duplicate components.
        /// Sets up the singleton instance and marks it to persist between scenes.
        /// </remarks>
        protected override void Awake()
        {
            base.Awake();

            lock (SingletonLock)
            {
                if (_instance is not null && _instance != this)
                {
#if __EVENTTIMELINE_DEBUG
                    AsyncLogger.LogWarning("[EventTimeline] Duplicate instance detected, destroying GameObject");
#endif

                    Destroy(gameObject);

                    return;
                }

                _instance = this;

                DontDestroyOnLoad(gameObject);

#if __EVENTTIMELINE_DEBUG_VERBOSE
                AsyncLogger.Log("[EventTimeline] Instance initialized in Awake");
#endif
            }
        }

        /// <summary>
        /// Unity OnEnable callback that subscribes to scene unloading events.
        /// </summary>
        protected override void OnEnable()
        {
            base.OnEnable();

            SceneManager.sceneUnloaded += HandleSceneUnloaded;
        }

        /// <summary>
        /// Unity OnDisable callback that unsubscribes from scene unloading events.
        /// </summary>
        protected override void OnDisable()
        {
            base.OnDisable();

            SceneManager.sceneUnloaded -= HandleSceneUnloaded;
        }

        /// <summary>
        /// Overrides the MonoBehaviour OnDestroy method to release the singleton instance of EventTimeline.
        /// Ensures the singleton instance is properly cleared when the object is destroyed in the Unity lifecycle.
        /// </summary>
        /// <remarks>
        /// This method locks on the SingletonLock object to ensure thread safety when clearing the singleton instance.
        /// It is responsible for setting the static _instance field to null when the EventTimeline object is destroyed,
        /// preventing potential issues with dangling references and ensuring proper cleanup.
        /// </remarks>
        protected override void OnDestroy()
        {
            base.OnDestroy();

            lock (SingletonLock)
            {
                if (_instance == this)
                {
#if __EVENTTIMELINE_DEBUG_VERBOSE
                    AsyncLogger.Log("[EventTimeline] Clearing singleton instance");
#endif
                    _instance = null;
                }
            }
        }

        /// <summary>
        /// Callback triggered when a scene is unloaded.
        /// </summary>
        /// <param name="scene">The scene that was unloaded.</param>
        /// <remarks>
        /// Optimizes memory usage by trimming excess capacity from collections.
        /// </remarks>
        private void HandleSceneUnloaded(Scene scene)
        {
#if __EVENTTIMELINE_DEBUG_VERBOSE
            AsyncLogger.LogFormat("[EventTimeline] Scene unloaded: {0}", scene.name);
#endif

            OptimizeMemory();

#if __EVENTTIMELINE_DEBUG_VERBOSE
            AsyncLogger.Log("[EventTimeline] Memory optimization completed after scene unload");
#endif
        }
    }
}