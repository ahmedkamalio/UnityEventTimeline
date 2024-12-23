#nullable enable

#if (DEVELOPMENT_BUILD || UNITY_EDITOR) && EVENTTIMELINE_DEBUG
#define __EVENTTIMELINE_DEBUG
#endif

#if __EVENTTIMELINE_DEBUG && EVENTTIMELINE_DEBUG_VERBOSE
#define __EVENTTIMELINE_DEBUG_VERBOSE
#endif

using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using ResultObject;
using UnityEngine;

#if __EVENTTIMELINE_DEBUG || __EVENTTIMELINE_DEBUG_VERBOSE
using UnityEventTimeline.Internal.Logger;
#endif

namespace UnityEventTimeline.Internal
{
    /// <summary>
    /// Manages the model layer for the Unity event timeline system, providing thread-safe state management
    /// and data persistence capabilities.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The EventTimelineModelManager provides a robust, thread-safe implementation for managing state
    /// and data models within the Unity event timeline system. It offers:
    /// <list type="bullet">
    /// <item>Thread-safe model storage and retrieval</item>
    /// <item>Automatic model lifecycle management</item>
    /// <item>Memory optimization through model cleanup</item>
    /// <item>Type-safe access to model instances</item>
    /// </list>
    /// </para>
    /// 
    /// <para>
    /// Models are managed through a concurrent dictionary to ensure thread safety, making this class
    /// suitable for use in multi-threaded environments. All public methods are thread-safe and can
    /// be called from any thread.
    /// </para>
    /// 
    /// <para>
    /// This manager integrates with Unity's lifecycle, automatically cleaning up resources when
    /// the component is destroyed and providing proper synchronization with the main thread
    /// for Unity-specific operations.
    /// </para>
    /// </remarks>
    /// <example>
    /// Basic usage example:
    /// <code>
    /// // Store a model
    /// var gameState = new GameStateModel { Level = 1, Score = 0 };
    /// eventTimeline.SetModel(gameState);
    /// 
    /// // Retrieve a model
    /// if (eventTimeline.TryGetModel&lt;GameStateModel&gt;(out var model))
    /// {
    ///     Debug.Log($"Current level: {model.Level}");
    /// }
    /// 
    /// // Get or create a model
    /// var playerState = eventTimeline.GetOrCreateModel&lt;PlayerStateModel&gt;();
    /// playerState.Health = 100;
    /// </code>
    /// </example>
    /// <seealso cref="EventTimelineBase"/>
    /// <seealso cref="EventTimelineModel"/>
    public class EventTimelineModelManager : EventTimelineBase
    {
        /// <summary>
        /// The default initial capacity for the models dictionary.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Defines the initial allocation size for the internal concurrent dictionary storing models.
        /// This value is optimized for typical use cases where applications maintain a small number
        /// of active models.
        /// </para>
        /// <para>
        /// The dictionary will automatically grow beyond this size if needed, but setting an appropriate
        /// initial capacity helps reduce memory reallocations.
        /// </para>
        /// </remarks>
        private const int DefaultModelsDictionaryCapacity = 5;

        /// <summary>
        /// Thread-safe dictionary storing all registered model instances, keyed by their Type.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Uses ConcurrentDictionary to provide thread-safe access to models without explicit locking.
        /// The dictionary is initialized with:
        /// <list type="bullet">
        /// <item>Concurrency level matching the system's processor count for optimal performance</item>
        /// <item>Initial capacity defined by <see cref="DefaultModelsDictionaryCapacity"/></item>
        /// </list>
        /// </para>
        /// <para>
        /// Each key represents a model's Type, and the corresponding value is the model instance.
        /// Models are stored as <see cref="EventTimelineModel"/> to provide a common base type
        /// while maintaining type safety through the public API.
        /// </para>
        /// </remarks>
        private readonly ConcurrentDictionary<Type, EventTimelineModel> _models = new(
            concurrencyLevel: Environment.ProcessorCount,
            capacity: DefaultModelsDictionaryCapacity);

        /// <summary>
        /// Gets the current number of models stored in the manager's internal collection.
        /// </summary>
        /// <remarks>
        /// This property provides the count of all models currently managed by the instance. It reflects
        /// the number of entries in the internal concurrent dictionary that maintains the models.
        /// </remarks>
        public int Count => _models.Count;

        /// <summary>
        /// Sets or updates a model instance in the timeline's model dictionary.
        /// </summary>
        /// <typeparam name="T">The type of model to set. Must be a reference type that inherits from EventTimelineModel and has a parameterless constructor.</typeparam>
        /// <param name="model">The model instance to store. Must not be null and must pass validation.</param>
        /// <returns>A Result containing either the stored model or an error if validation fails.</returns>
        /// <remarks>
        /// <para>
        /// If a model of the same type already exists, it will be replaced with the new instance.
        /// The operation is thread-safe and can be called from any context.
        /// </para>
        /// <para>
        /// Model validation is performed before storage, and the operation will fail if validation does not pass.
        /// </para>
        /// </remarks>
        /// <example>
        /// This example shows how to set and update a model:
        /// <code>
        /// var gameState = new GameStateModel { Score = 100 };
        /// var result = eventTimeline.SetModel(gameState);
        /// 
        /// if (result.IsSuccess)
        /// {
        ///     Debug.Log("Model stored successfully");
        /// }
        /// else
        /// {
        ///     Debug.LogError($"Failed to store model: {result.Error}");
        /// }
        /// </code>
        /// </example>
        public Result<T> SetModel<T>(T model) where T : EventTimelineModel, new()
        {
#if __EVENTTIMELINE_DEBUG_VERBOSE
            AsyncLogger.LogFormat("[EventTimelineModelManager] Beginning SetModel operation for type {0}", typeof(T).Name);
            AsyncLogger.LogFormat("[EventTimelineModelManager] Current model count: {0}", _models.Count);
#endif

            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (model is null)
            {
#if __EVENTTIMELINE_DEBUG
                AsyncLogger.LogError("[EventTimelineModelManager] Attempted to set null model");
#endif
                return Result.Failure<T>("Model must not be null", "NULL_MODEL");
            }

            if (!model.Validate())
            {
#if __EVENTTIMELINE_DEBUG
                AsyncLogger.LogErrorFormat("[EventTimelineModelManager] Model validation failed for type {0}", typeof(T).Name);
#endif
                return Result.Failure<T>("Model validation failed", "INVALID_MODEL");
            }

            var addedModel = _models.AddOrUpdate(typeof(T), model, (_, _) => model);

#if __EVENTTIMELINE_DEBUG
            AsyncLogger.LogFormat("[EventTimelineModelManager] Set model of type {0}. Total models: {1}", typeof(T).Name, _models.Count);
#endif

            if (IsMainThread)
            {
                addedModel.SetLastAccessedTime(Time.time);
            }
            else
            {
#if __EVENTTIMELINE_DEBUG
                AsyncLogger.LogWarningFormat("[EventTimelineModelManager] Background model access detected for {0}. Posting SetLastAccessedTime to main thread.", typeof(T).Name);
#endif
                RunOnMainThread(() => addedModel.SetLastAccessedTime(Time.time));
            }

#if __EVENTTIMELINE_DEBUG_VERBOSE
            AsyncLogger.Log("[EventTimelineModelManager] SetModel operation completed");
#endif

            return Result.Success((T)addedModel);
        }

        /// <summary>
        /// Attempts to retrieve a model of the specified type from the timeline's model dictionary.
        /// </summary>
        /// <typeparam name="T">The type of model to retrieve. Must inherit from EventTimelineModel and have a parameterless constructor.</typeparam>
        /// <param name="model">When this method returns, contains the requested model if found; otherwise, null.</param>
        /// <returns>true if the model was found; otherwise, false.</returns>
        /// <remarks>
        /// This method is thread-safe and provides a way to safely check for and retrieve a model
        /// without throwing exceptions if the model doesn't exist. The model's LastAccessedTime
        /// is updated when retrieved.
        /// </remarks>
        /// <example>
        /// This example demonstrates safe model retrieval:
        /// <code>
        /// if (eventTimeline.TryGetModel&lt;GameStateModel&gt;(out var gameState))
        /// {
        ///     // Model exists, use it
        ///     Debug.Log($"Current score: {gameState.Score}");
        /// }
        /// else
        /// {
        ///     // Model doesn't exist, handle accordingly
        ///     Debug.Log("GameState model not found");
        /// }
        /// </code>
        /// </example>
        public bool TryGetModel<T>([NotNullWhen(true)] out T? model) where T : EventTimelineModel, new()
        {
#if __EVENTTIMELINE_DEBUG_VERBOSE
            AsyncLogger.LogFormat("[EventTimelineModelManager] Attempting to retrieve model of type {0}", typeof(T).Name);
#endif

            if (_models.TryGetValue(typeof(T), out var m))
            {
                if (IsMainThread)
                {
                    m.SetLastAccessedTime(Time.time);
                }
                else
                {
#if __EVENTTIMELINE_DEBUG
                    AsyncLogger.LogWarningFormat("[EventTimelineModelManager] Background model access detected for {0}. Posting SetLastAccessedTime to main thread.", typeof(T).Name);
#endif
                    RunOnMainThread(() => m.SetLastAccessedTime(Time.time));
                }

                model = (T)m;

#if __EVENTTIMELINE_DEBUG_VERBOSE
                AsyncLogger.LogFormat("[EventTimelineModelManager] Successfully retrieved model of type {0}", typeof(T).Name);
#endif

                return true;
            }

            model = null;

#if __EVENTTIMELINE_DEBUG
            AsyncLogger.LogWarningFormat("[EventTimelineModelManager] Failed to retrieve model of type {0} from dictionary", typeof(T).Name);
#endif

#if __EVENTTIMELINE_DEBUG_VERBOSE
            AsyncLogger.Log("[EventTimelineModelManager] Model retrieval failed");
#endif

            return false;
        }

        /// <summary>
        /// Gets an existing model of the specified type or creates a new instance if one doesn't exist.
        /// </summary>
        /// <typeparam name="T">The type of model to retrieve or create. Must inherit from EventTimelineModel and have a parameterless constructor.</typeparam>
        /// <returns>The existing or newly created model instance.</returns>
        /// <remarks>
        /// <para>
        /// This method is thread-safe and provides an atomic way to ensure a model of the specified type
        /// exists in the dictionary. If multiple threads attempt to create a model simultaneously,
        /// only one instance will be created and stored.
        /// </para>
        /// <para>
        /// The model's LastAccessedTime is updated when retrieved or created.
        /// </para>
        /// </remarks>
        /// <example>
        /// This example shows how to get or create a model in one operation:
        /// <code>
        /// var playerStats = eventTimeline.GetOrCreateModel&lt;PlayerStatsModel&gt;();
        /// 
        /// // The model will exist regardless of whether it existed before
        /// playerStats.Experience += experienceGained;
        /// playerStats.Level = CalculateLevel(playerStats.Experience);
        /// </code>
        /// </example>
        public T GetOrCreateModel<T>() where T : EventTimelineModel, new()
        {
            var model = (T)_models.GetOrAdd(typeof(T), _ =>
            {
#if __EVENTTIMELINE_DEBUG_VERBOSE
                AsyncLogger.LogFormat("[EventTimelineModelManager] Creating new model of type {0}", typeof(T).Name);
#endif
                return new T();
            });

            if (IsMainThread)
            {
                model.SetLastAccessedTime(Time.time);
            }
            else
            {
#if __EVENTTIMELINE_DEBUG
                AsyncLogger.LogWarningFormat("[EventTimelineModelManager] Background model access detected for {0}. Posting SetLastAccessedTime to main thread.", typeof(T).Name);
#endif
                RunOnMainThread(() => model.SetLastAccessedTime(Time.time));
            }

#if __EVENTTIMELINE_DEBUG_VERBOSE
            AsyncLogger.Log("[EventTimelineModelManager] GetOrCreateModel completed");
            AsyncLogger.LogFormat("[EventTimelineModelManager] New model count: {0}", _models.Count);
#endif

            return model;
        }

        /// <summary>
        /// Removes a model of the specified type from the timeline's model dictionary.
        /// </summary>
        /// <typeparam name="T">The type of model to remove. Must be a reference type with a parameterless constructor.</typeparam>
        /// <returns>true if the model was successfully removed; otherwise, false.</returns>
        /// <remarks>
        /// <para>
        /// This method is thread-safe and can be called from any context. It will return false if
        /// no model of the specified type exists in the dictionary.
        /// </para>
        /// <para>
        /// In debug builds, this method logs whether the model removal was successful.
        /// </para>
        /// </remarks>
        /// <example>
        /// This example demonstrates removing a specific model:
        /// <code>
        /// if (eventTimeline.RemoveModel&lt;TemporaryStateModel&gt;())
        /// {
        ///     Debug.Log("Temporary state cleared successfully");
        /// }
        /// </code>
        /// </example>
        public bool RemoveModel<T>() where T : class, new()
        {
            var type = typeof(T);
            var removed = _models.TryRemove(type, out _);

#if __EVENTTIMELINE_DEBUG_VERBOSE
            if (removed)
            {
                AsyncLogger.LogFormat("[EventTimelineModelManager]: Removed model of type {0} from the timeline's model dictionary.", type);
            }
#elif __EVENTTIMELINE_DEBUG
            if (!removed)
            {
                AsyncLogger.LogWarningFormat("[EventTimelineModelManager]: Failed to remove model of type {0} from the timeline's model dictionary. Model not found.", type);
            }
#endif

            return removed;
        }

        /// <summary>
        /// Removes all models that haven't been accessed within the specified time threshold.
        /// </summary>
        /// <param name="unusedThreshold">The time in seconds after which an unused model should be removed.</param>
        /// <returns>The number of models that were removed.</returns>
        /// <remarks>
        /// This method helps prevent memory leaks by cleaning up models that are no longer being used.
        /// The cleanup operation is thread-safe and can be called during gameplay to optimize memory usage.
        /// </remarks>
        /// <example>
        /// This example shows how to perform periodic cleanup of unused models:
        /// <code>
        /// // Clean up models that haven't been accessed in the last 5 minutes
        /// var removedCount = eventTimeline.CleanupUnusedModels(300f);
        /// if (removedCount > 0)
        /// {
        ///     Debug.Log($"Cleaned up {removedCount} unused models");
        /// }
        /// </code>
        /// </example>
        public int CleanupUnusedModels(float unusedThreshold)
        {
            if (unusedThreshold <= 0f)
            {
#if __EVENTTIMELINE_DEBUG
                AsyncLogger.LogWarning("[EventTimelineModelManager] Invalid cleanup threshold (<=0), skipping cleanup");
#endif
                return 0;
            }

            var count = 0;
            var now = Time.time;

            foreach (var (key, _) in _models.Where(m => m.Value.LastAccessedTime + unusedThreshold < now))
            {
#if __EVENTTIMELINE_DEBUG_VERBOSE
                if (_models.TryRemove(key, out var model))
                {
                    AsyncLogger.LogFormat("[EventTimelineModelManager] Removed unused model of type {0}. Last accessed: {1}", key.Name, model.LastAccessedTime);
#else
                if (_models.TryRemove(key, out _))
                {
#endif
                    count++;
                }
            }

#if __EVENTTIMELINE_DEBUG_VERBOSE
            AsyncLogger.LogFormat("[EventTimelineModelManager] Cleanup completed. Removed {0} unused models", count);
#endif

            return count;
        }

        /// <summary>
        /// Removes all models from the timeline's model dictionary.
        /// </summary>
        /// <returns>The number of models that were removed.</returns>
        /// <remarks>
        /// <para>
        /// This method is thread-safe and removes all stored models regardless of their type.
        /// It's typically called during cleanup operations or when resetting the timeline's state.
        /// </para>
        /// <para>
        /// Consider using <see cref="CleanupUnusedModels"/> instead if you only want to remove
        /// inactive models.
        /// </para>
        /// </remarks>
        /// <example>
        /// This example shows how to clear all models during a game reset:
        /// <code>
        /// public void ResetGame()
        /// {
        ///     int clearedCount = eventTimeline.ClearModels();
        ///     Debug.Log($"Cleared {clearedCount} models during game reset");
        ///     
        ///     // Start fresh with new models
        ///     var gameState = eventTimeline.GetOrCreateModel&lt;GameStateModel&gt;();
        ///     gameState.Score = 0;
        ///     gameState.Level = 1;
        /// }
        /// </code>
        /// </example>
        public int ClearModels()
        {
            var count = _models.Count;
            _models.Clear();

#if __EVENTTIMELINE_DEBUG_VERBOSE
            AsyncLogger.LogFormat("Cleared {0} models from the timeline's model dictionary.", count);
#endif

            return count;
        }

        /// <summary>
        /// Called when the MonoBehaviour is being destroyed.
        /// </summary>
        /// <remarks>
        /// This method ensures proper cleanup of resources by clearing all stored models
        /// when the EventTimelineModelManager component is destroyed. This prevents memory leaks
        /// and ensures proper resource management in the Unity lifecycle.
        /// </remarks>
        protected override void OnDestroy()
        {
            base.OnDestroy();

            ClearModels();
        }
    }
}