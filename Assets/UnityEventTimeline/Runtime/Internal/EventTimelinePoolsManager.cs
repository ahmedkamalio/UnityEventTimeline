#nullable enable

#if (DEVELOPMENT_BUILD || UNITY_EDITOR) && EVENTTIMELINE_DEBUG
#define __EVENTTIMELINE_DEBUG
#endif

#if __EVENTTIMELINE_DEBUG && EVENTTIMELINE_DEBUG_VERBOSE
#define __EVENTTIMELINE_DEBUG_VERBOSE
#endif

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using ResultObject;

#if __EVENTTIMELINE_DEBUG || __EVENTTIMELINE_DEBUG_VERBOSE
using UnityEventTimeline.Internal.Logger;
#endif

namespace UnityEventTimeline.Internal
{
    /// <summary>
    /// Manages object pools for timeline events to reduce garbage collection pressure.
    /// Provides efficient recycling and reuse of event instances using thread-safe concurrent collections.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This manager extends EventTimelineModelManager to provide object pooling capabilities,
    /// optimizing memory usage and reducing garbage collection overhead for frequently
    /// created/destroyed event instances.
    /// </para>
    /// <para>
    /// Key features:
    /// <list type="bullet">
    /// <item>Lock-free concurrent pool operations</item>
    /// <item>Configurable pool sizes per event type</item>
    /// <item>Automatic pool cleanup and optimization</item>
    /// <item>Memory usage monitoring and control</item>
    /// </list>
    /// </para>
    /// </remarks>
    public class EventTimelinePoolsManager : EventTimelineModelManager
    {
        /// <summary>
        /// The default maximum size for each event type pool.
        /// </summary>
        /// <remarks>
        /// This value provides a reasonable balance between memory usage and performance
        /// for most applications. Can be overridden per event type using SetMaxPoolSize.
        /// </remarks>
        private const int DefaultMaxPoolSize = 100;

        /// <summary>
        /// Stores pools of reusable event instances, keyed by event type.
        /// </summary>
        /// <remarks>
        /// Each pool is implemented as a ConcurrentStack for lock-free LIFO access,
        /// optimizing for cache locality and recent event reuse.
        /// </remarks>
        private readonly ConcurrentDictionary<Type, ConcurrentStack<TimelineEvent>> _eventPools = new(
            concurrencyLevel: Environment.ProcessorCount,
            capacity: DefaultMaxPoolSize);

        /// <summary>
        /// Stores the maximum allowed size for each event type pool.
        /// </summary>
        /// <remarks>
        /// If a type is not present, DefaultMaxPoolSize is used.
        /// </remarks>
        private readonly ConcurrentDictionary<Type, int> _maxPoolSizes = new();

        /// <summary>
        /// Tracks the current size of each pool using atomic operations.
        /// </summary>
        private readonly ConcurrentDictionary<Type, int> _currentPoolSizes = new();

        /// <summary>
        /// Sets the maximum pool size for a specific event type.
        /// </summary>
        /// <typeparam name="T">The type of event to configure.</typeparam>
        /// <param name="maxSize">Maximum number of instances to keep in the pool.</param>
        /// <remarks>
        /// <para>
        /// Setting maxSize to 0 or negative will reset to the default pool size.
        /// If the new size is smaller than the current pool size, excess instances
        /// will be removed gradually as the pool is accessed.
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// poolManager.SetMaxPoolSize&lt;CustomEvent&gt;(200); // Increase pool size for frequent events
        /// poolManager.SetMaxPoolSize&lt;RareEvent&gt;(10);    // Reduce pool size for rare events
        /// </code>
        /// </example>
        public void SetMaxPoolSize<T>(int maxSize) where T : TimelineEvent
        {
#if __EVENTTIMELINE_DEBUG_VERBOSE
            AsyncLogger.LogFormat("[EventTimelinePoolsManager] Setting max pool size for type {0} to {1}", typeof(T).Name, maxSize);
#endif

            var type = typeof(T);
            if (maxSize <= 0)
            {
#if __EVENTTIMELINE_DEBUG
                AsyncLogger.LogWarningFormat("[EventTimelinePoolsManager] Invalid max pool size ({0}) specified for {1}, removing custom size limit", maxSize, type.Name);
#endif

                _maxPoolSizes.TryRemove(type, out _);

                return;
            }

            _maxPoolSizes.AddOrUpdate(type, maxSize, (_, _) => maxSize);

            // Trim pool if it exceeds new maximum size
            if (_eventPools.TryGetValue(type, out var pool))
            {
#if __EVENTTIMELINE_DEBUG_VERBOSE
                AsyncLogger.LogFormat("[EventTimelinePoolsManager] Trimming existing pool for {0} to new max size {1}", type.Name, maxSize);
#endif

                TrimPool(type, pool, maxSize);
            }
        }

        /// <summary>
        /// Gets the maximum pool size for a specific event type.
        /// </summary>
        /// <typeparam name="T">The type of event to query.</typeparam>
        /// <returns>The maximum pool size for the specified event type.</returns>
        /// <remarks>
        /// Returns DefaultMaxPoolSize if no specific size has been set for the type.
        /// </remarks>
        public int GetMaxPoolSize<T>() where T : TimelineEvent
        {
            return _maxPoolSizes.GetValueOrDefault(typeof(T), DefaultMaxPoolSize);
        }

        /// <summary>
        /// Gets current pool statistics for a specific event type.
        /// </summary>
        /// <typeparam name="T">The type of event to query.</typeparam>
        /// <returns>A tuple containing (current pool size, maximum pool size).</returns>
        /// <remarks>
        /// Useful for monitoring pool usage and identifying potential memory optimization
        /// opportunities.
        /// </remarks>
        public (int currentSize, int maxSize) GetPoolStats<T>() where T : TimelineEvent
        {
            var type = typeof(T);
            var currentSize = _currentPoolSizes.GetValueOrDefault(type, 0);
            var maxSize = _maxPoolSizes.GetValueOrDefault(type, DefaultMaxPoolSize);
            return (currentSize, maxSize);
        }

        /// <summary>
        /// Returns an event instance to its type-specific pool.
        /// </summary>
        /// <param name="evt">The event to return to the pool.</param>
        /// <returns>A Result indicating success or failure of the pool return operation.</returns>
        /// <remarks>
        /// <para>
        /// This method handles the recycling of event instances to reduce garbage collection pressure.
        /// If the pool is at capacity, the event will be disposed instead of being pooled.
        /// </para>
        /// <para>
        /// The method includes retry logic with a bounded spin wait to handle high contention scenarios
        /// while maintaining responsiveness.
        /// </para>
        /// </remarks>
        protected Result<Unit> ReturnToPool(TimelineEvent evt)
        {
#if __EVENTTIMELINE_DEBUG_VERBOSE
            AsyncLogger.LogFormat("[EventTimelinePoolsManager] Attempting to return {0} to pool", evt.GetType().Name);
#endif

            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (evt is null)
            {
#if __EVENTTIMELINE_DEBUG
                AsyncLogger.LogError("[EventTimelinePoolsManager] Attempted to return null event to pool");
#endif

                return Result.Failure<Unit>("Event cannot be null");
            }

            var type = evt.GetType();
            var pool = _eventPools.GetOrAdd(type, _ => new ConcurrentStack<TimelineEvent>());
            var maxSize = _maxPoolSizes.GetValueOrDefault(type, DefaultMaxPoolSize);

            // Fast path - try once without spinning
            var currentSize = _currentPoolSizes.GetOrAdd(type, 0);
            if (currentSize >= maxSize)
            {
#if __EVENTTIMELINE_DEBUG
                AsyncLogger.LogWarningFormat("[EventTimelinePoolsManager] Pool for {0} at capacity ({1}), disposing event", type.Name, maxSize);
#endif
                evt.Dispose();
                return Result.Failure<Unit>($"Max pool size ({maxSize}) exceeded");
            }

            if (_currentPoolSizes.TryUpdate(type, currentSize + 1, currentSize))
            {
                // Reset the event's state before reusing
                evt.Reset();
                pool.Push(evt);

#if __EVENTTIMELINE_DEBUG_VERBOSE
                AsyncLogger.LogFormat("[EventTimelinePoolsManager] Successfully returned {0} to pool (Size: {1}/{2})", type.Name, currentSize + 1, maxSize);
#endif

                return Result.Success(Unit.Value);
            }

            // Contention path - use minimal spins
            var spin = new SpinWait();
            const int maxSpins = 10; // Limit retries to maintain responsiveness

#if __EVENTTIMELINE_DEBUG_VERBOSE
            AsyncLogger.LogFormat("[EventTimelinePoolsManager] Contention detected for {0} pool, entering spin wait", type.Name);
#endif

            for (var i = 0; i < maxSpins; i++)
            {
                currentSize = _currentPoolSizes.GetOrAdd(type, 0);
                if (currentSize >= maxSize)
                {
#if __EVENTTIMELINE_DEBUG
                    AsyncLogger.LogWarningFormat("[EventTimelinePoolsManager] Pool for {0} at capacity after spin, disposing event", type.Name);
#endif
                    evt.Dispose();
                    return Result.Failure<Unit>($"Max pool size ({maxSize}) exceeded");
                }

                if (_currentPoolSizes.TryUpdate(type, currentSize + 1, currentSize))
                {
                    // Reset the event's state before reusing
                    evt.Reset();
                    pool.Push(evt);

#if __EVENTTIMELINE_DEBUG_VERBOSE
                    AsyncLogger.LogFormat("[EventTimelinePoolsManager] Successfully returned {0} to pool after {1} spins", type.Name, i + 1);
#endif

                    return Result.Success(Unit.Value);
                }

                spin.SpinOnce();
            }

#if __EVENTTIMELINE_DEBUG
            AsyncLogger.LogErrorFormat("[EventTimelinePoolsManager] Failed to return {0} to pool after {1} attempts, disposing", type.Name, maxSpins);
#endif

            // If still contended after max spins, dispose and fail
            evt.Dispose();

            return Result.Failure<Unit>("High contention - event disposed");
        }

        /// <summary>
        /// Retrieves an event instance from its type-specific pool or creates a new one if none are available.
        /// </summary>
        /// <typeparam name="T">The type of event to retrieve.</typeparam>
        /// <returns>An instance of the requested event type.</returns>
        /// <remarks>
        /// <para>
        /// This method attempts to reuse an existing instance from the pool to reduce allocation overhead.
        /// If the pool is empty, it creates a new instance using the default constructor.
        /// </para>
        /// <para>
        /// Event instances returned by this method are guaranteed to be in a clean, reusable state.
        /// </para>
        /// </remarks>
        protected T GetFromPool<T>() where T : TimelineEvent, new()
        {
#if __EVENTTIMELINE_DEBUG_VERBOSE
            AsyncLogger.LogFormat("[EventTimelinePoolsManager] Attempting to get {0} from pool", typeof(T).Name);
#endif

            var type = typeof(T);
            var pool = _eventPools.GetOrAdd(type, _ => new ConcurrentStack<TimelineEvent>());

            // Try to get an item from the pool
            if (!pool.TryPop(out var evt))
            {
#if __EVENTTIMELINE_DEBUG_VERBOSE
                AsyncLogger.LogFormat("[EventTimelinePoolsManager] Pool empty for {0}, creating new instance", type.Name);
#endif
                return new T();
            }

            // Update the size counter atomically
            _currentPoolSizes.AddOrUpdate(
                type,
                0, // Should never be used since we know the key exists
                (_, oldValue) => oldValue - 1
            );

#if __EVENTTIMELINE_DEBUG_VERBOSE
            AsyncLogger.LogFormat("[EventTimelinePoolsManager] Successfully retrieved {0} from pool", type.Name);
#endif

            return (T)evt;
        }

        /// <summary>
        /// Trims a specific pool to the specified maximum size.
        /// </summary>
        /// <param name="type">The event type whose pool should be trimmed.</param>
        /// <param name="pool">The pool to trim.</param>
        /// <param name="maxSize">The maximum size to enforce.</param>
        private void TrimPool(Type type, ConcurrentStack<TimelineEvent> pool, int maxSize)
        {
#if __EVENTTIMELINE_DEBUG_VERBOSE
            AsyncLogger.LogFormat("[EventTimelinePoolsManager] Beginning pool trim for {0} to size {1}", type.Name, maxSize);
            var initialSize = _currentPoolSizes.GetValueOrDefault(type, 0);
#endif

            // Calculate total items to trim
            var totalItemsToTrim = _currentPoolSizes.GetValueOrDefault(type, 0) - maxSize;
            if (totalItemsToTrim <= 0)
            {
#if __EVENTTIMELINE_DEBUG_VERBOSE
                AsyncLogger.LogFormat("[EventTimelinePoolsManager] No trimming needed for {0} pool", type.Name);
#endif

                return;
            }

            var trimCount = 0;

            for (var i = 0; i < totalItemsToTrim; i++)
            {
                if (!pool.TryPop(out var evt))
                {
                    break;
                }

                evt.Dispose();

                trimCount++;
            }

            // Update the size counter once for the batch
            _currentPoolSizes.AddOrUpdate(
                type,
                0,
                (_, oldValue) => Math.Max(0, oldValue - trimCount)
            );

#if __EVENTTIMELINE_DEBUG_VERBOSE
            var finalSize = _currentPoolSizes.GetValueOrDefault(type, 0);
            AsyncLogger.LogFormat("[EventTimelinePoolsManager] Trimmed {0} items from {1} pool (Size: {2}->{3})", trimCount, type.Name, initialSize, finalSize);
#endif
        }

        /// <summary>
        /// Reduces the capacity of all event pools to match their current sizes.
        /// </summary>
        /// <remarks>
        /// Call this method during memory optimization phases or scene transitions
        /// to minimize memory overhead.
        /// </remarks>
        protected void TrimEventPools()
        {
#if __EVENTTIMELINE_DEBUG_VERBOSE
            AsyncLogger.Log("[EventTimelinePoolsManager] Beginning trim of all event pools");
#endif
            foreach (var kvp in _eventPools)
            {
                var maxSize = _maxPoolSizes.GetValueOrDefault(kvp.Key, DefaultMaxPoolSize);
                TrimPool(kvp.Key, kvp.Value, maxSize);
            }
#if __EVENTTIMELINE_DEBUG_VERBOSE
            AsyncLogger.Log($"[EventTimelinePoolsManager] Completed trimming event pools");
#endif
        }

        /// <summary>
        /// Removes all pooled events and resets pool statistics.
        /// </summary>
        /// <remarks>
        /// Call this method during major state changes or when shutting down
        /// to ensure proper cleanup of resources.
        /// </remarks>
        protected void ClearEventPools()
        {
#if __EVENTTIMELINE_DEBUG_VERBOSE
            AsyncLogger.Log("[EventTimelinePoolsManager] Beginning clear of all event pools");
            var initialPoolCount = _eventPools.Count;
            var totalEvents = 0;
#endif

            foreach (var pool in _eventPools.Values)
            {
                while (pool.TryPop(out var evt))
                {
                    evt.Dispose();

#if __EVENTTIMELINE_DEBUG_VERBOSE
                    totalEvents++;
#endif
                }
            }

            _eventPools.Clear();
            _currentPoolSizes.Clear();
            _maxPoolSizes.Clear();

#if __EVENTTIMELINE_DEBUG_VERBOSE
            AsyncLogger.LogFormat("[EventTimelinePoolsManager] Cleared {0} pools containing {1} total events", initialPoolCount, totalEvents);
#endif
        }
    }
}