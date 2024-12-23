#nullable enable

#if (DEVELOPMENT_BUILD || UNITY_EDITOR) && EVENTTIMELINE_DEBUG
#define __EVENTTIMELINE_DEBUG
#endif

#if __EVENTTIMELINE_DEBUG && EVENTTIMELINE_DEBUG_VERBOSE
#define __EVENTTIMELINE_DEBUG_VERBOSE
#endif

using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using ResultObject;
using UnityEventTimeline.Internal.EventQueue;

#if __EVENTTIMELINE_DEBUG || __EVENTTIMELINE_DEBUG_VERBOSE
using UnityEventTimeline.Internal.Logger;
#endif

namespace UnityEventTimeline.Internal
{
    public class EventTimelineQueue : EventTimelinePoolsManager
    {
        /// <summary>
        /// Default maximum number of events to process per frame.
        /// </summary>
        private const int DefaultMaxEventsPerFrame = 10;

        /// <summary>
        /// Default maximum time budget in milliseconds for event processing per frame.
        /// </summary>
        private const float DefaultMaxProcessingTimeMs = 8.0f; // Targeting 120fps

        /// <summary>
        /// Default number of frames to skip between cancellation cleanup attempts.
        /// </summary>
        private const int DefaultCleanupInterval = 120;

        /// <summary>
        /// Minimum number of cancelled events required to trigger an immediate cleanup.
        /// </summary>
        private const int ImmediateCleanupThreshold = 100;

        /// <summary>
        /// The priority queue that holds and orders all scheduled events.
        /// </summary>
        /// <remarks>
        /// Events are ordered by their scheduled execution time.
        /// </remarks>
        private readonly IEventPriorityQueue<TimelineEvent> _eventQueue = new EventPriorityQueue<TimelineEvent>();

        /// <summary>
        /// List to temporarily store cancelled events during cleanup.
        /// Reused to avoid allocations.
        /// </summary>
        private readonly List<TimelineEvent> _cancelledEvents = new(ImmediateCleanupThreshold);

        /// <summary>
        /// Object used for thread synchronization.
        /// </summary>
        /// <remarks>
        /// Ensures thread-safe access to cancelled events list.
        /// </remarks>
        private readonly object _cancelledEventsLock = new();

        /// <summary>
        /// Counter for frames since last cleanup.
        /// </summary>
        private int _framesSinceCleanup;

        /// <summary>
        /// Stopwatch instance used to measure elapsed time during event processing.
        /// </summary>
        private readonly Stopwatch _stopwatch = new();

        /// <summary>
        /// A collection of timeline events that are scheduled for future execution.
        /// Stores events that are yet to be processed due to their scheduled time being in the future.
        /// </summary>
        private readonly List<TimelineEvent> _futureEvents = new();

        /// <summary>
        /// The maximum number of events that can be processed in a single frame.
        /// </summary>
        /// <remarks>
        /// Serialized to allow configuration in Unity's inspector.
        /// Set to -1 to process all available events (not recommended).
        /// </remarks>
        [SerializeField]
        protected int maxEventsPerFrame = DefaultMaxEventsPerFrame;

        /// <summary>
        /// The maximum time in milliseconds that can be spent processing events in a single frame.
        /// </summary>
        /// <remarks>
        /// Serialized to allow configuration in Unity's inspector.
        /// Acts as a safety limit even when maxEventsPerFrame is set to -1.
        /// </remarks>
        [SerializeField]
        protected float maxProcessingTimeMs = DefaultMaxProcessingTimeMs;

        /// <summary>
        /// The number of frames to skip between cleanup attempts.
        /// </summary>
        [SerializeField]
        private int cleanupInterval = DefaultCleanupInterval;

        /// <summary>
        /// Schedules a new event of type T to be executed after the specified delay.
        /// </summary>
        /// <typeparam name="T">The type of event to schedule. Must derive from TimelineEvent.</typeparam>
        /// <param name="delay">Optional delay in seconds before the event is executed. Defaults to 0.</param>
        /// <returns>The scheduled event instance.</returns>
        /// <remarks>
        /// Events are pooled for efficiency. This method will reuse an existing event instance if available.
        /// Thread-safe operation protected by a lock.
        /// </remarks>
        public T Schedule<T>(float delay = 0) where T : TimelineEvent<T>, new()
        {
#if __EVENTTIMELINE_DEBUG_VERBOSE
            AsyncLogger.LogFormat("[EventTimelineQueue] Scheduling event of type {0} with delay {1}s", typeof(T).Name, delay);
#endif

            var evt = GetFromPool<T>();

            if (IsMainThread)
            {
                evt.ScheduledTime = Time.time + delay / timeScale;
            }
            else
            {
                RunOnMainThread(() => evt.ScheduledTime = Time.time + delay / timeScale);

#if __EVENTTIMELINE_DEBUG
                AsyncLogger.LogWarningFormat("[EventTimelineQueue] Background event schedule detected. Posting {0} to main thread.", typeof(T));
#endif
            }

            evt.IsCancelled = false;

            _eventQueue.Enqueue(evt);

#if __EVENTTIMELINE_DEBUG_VERBOSE
            AsyncLogger.LogFormat("[EventTimelineQueue] Successfully scheduled event of type {0} for time {1:F3}s", typeof(T).Name, evt.ScheduledTime);
#endif

            return evt;
        }

        /// <summary>
        /// Schedules a new event of type T and configures it using the provided action.
        /// </summary>
        /// <typeparam name="T">The type of event to schedule. Must derive from TimelineEvent.</typeparam>
        /// <param name="configure">Action to configure the event before scheduling.</param>
        /// <param name="delay">Optional delay in seconds before the event is executed. Defaults to 0.</param>
        /// <remarks>
        /// Provides a convenient way to configure event properties during scheduling.
        /// Thread-safe operation protected by a lock.
        /// </remarks>
        public T Schedule<T>(Action<T> configure, float delay = 0) where T : TimelineEvent<T>, new()
        {
            var evt = Schedule<T>(delay);
            configure.Invoke(evt);
            return evt;
        }

        /// <summary>
        /// Reschedules an existing event with a new delay.
        /// </summary>
        /// <typeparam name="T">The type of event to reschedule.</typeparam>
        /// <param name="evt">The event to reschedule.</param>
        /// <param name="delay">The new delay in seconds.</param>
        /// <returns>The rescheduled event instance.</returns>
        /// <remarks>
        /// Updates the scheduled time of an existing event and re-enqueues it.
        /// Thread-safe operation protected by a lock.
        /// </remarks>
        public Result<T> Reschedule<T>(T evt, float delay) where T : TimelineEvent
        {
#if __EVENTTIMELINE_DEBUG_VERBOSE
            AsyncLogger.LogFormat("[EventTimelineQueue] Attempting to reschedule event of type {0} with delay {1}s", typeof(T).Name, delay);
#endif

            if (!_eventQueue.TryRemove(evt))
            {
#if __EVENTTIMELINE_DEBUG
                AsyncLogger.LogWarningFormat("[EventTimelineQueue] Failed to reschedule - event not found in queue: {0}", typeof(T).Name);
#endif
                return Result.Failure<T>("Event doesn't exist in the events queue.", "EVT_NOT_FOUND");
            }

            if (IsMainThread)
            {
                evt.ScheduledTime = Time.time + delay / timeScale;
            }
            else
            {
                RunOnMainThread(() => evt.ScheduledTime = Time.time + delay / timeScale);

#if __EVENTTIMELINE_DEBUG
                AsyncLogger.LogWarningFormat("[EventTimelineQueue] Background event reschedule detected. Posting {0} to main thread.", typeof(T));
#endif
            }

            _eventQueue.Enqueue(evt);

#if __EVENTTIMELINE_DEBUG_VERBOSE
            AsyncLogger.LogFormat("[EventTimelineQueue] Successfully rescheduled event of type {0} for time {1:F3}", typeof(T).Name, evt.ScheduledTime);
#endif

            return Result.Success(evt);
        }

        /// <summary>
        /// Clears all scheduled events and event pools.
        /// </summary>
        /// <remarks>
        /// Thread-safe operation protected by a lock.
        /// Trims excess capacity from event pools after clearing.
        /// </remarks>
        public void Clear()
        {
#if __EVENTTIMELINE_DEBUG_VERBOSE
            AsyncLogger.LogFormat("[EventTimelineQueue] Clearing all events. Current queue size: {0}", _eventQueue.Count);
#endif

            _eventQueue.Clear();

            lock (_cancelledEventsLock)
            {
                // Return cancelled events to pool before clearing
                foreach (var evt in _cancelledEvents)
                {
                    ReturnToPool(evt);
                }

                _cancelledEvents.Clear();
            }

            ClearEventPools();

#if __EVENTTIMELINE_DEBUG_VERBOSE
            AsyncLogger.Log("[EventTimelineQueue] Successfully cleared all events and pools");
#endif
        }

        /// <summary>
        /// Forces an immediate cleanup of cancelled events, regardless of the cleanup interval.
        /// </summary>
        /// <returns>The number of events that were cleaned up.</returns>
        public int ForceCleanup()
        {
#if __EVENTTIMELINE_DEBUG_VERBOSE
            AsyncLogger.Log("[EventTimelineQueue] Forcing immediate cleanup of cancelled events");
#endif

            var count = RemoveCancelledEvents();

            _framesSinceCleanup = 0;

#if __EVENTTIMELINE_DEBUG_VERBOSE
            AsyncLogger.LogFormat("[EventTimelineQueue] Force cleanup completed. Removed {0} cancelled events", count);
#endif

            return count;
        }

        /// <summary>
        /// Checks if a specific event is currently scheduled in the queue.
        /// </summary>
        /// <typeparam name="T">The type of event to check.</typeparam>
        /// <param name="evt">The event instance to look for.</param>
        /// <returns>True if the event is scheduled, false otherwise.</returns>
        /// <remarks>
        /// Thread-safe operation protected by a lock.
        /// </remarks>
        public bool IsEventPending<T>(T evt) where T : TimelineEvent
        {
#if __EVENTTIMELINE_DEBUG_VERBOSE
            var isPending = _eventQueue.Contains(evt);
            AsyncLogger.LogFormat("[EventTimelineQueue] Checking if event is pending: {0}, Result: {1}", typeof(T).Name, isPending);
            return isPending;
#else
            return _eventQueue.Contains(evt);
#endif
        }

        /// <summary>
        /// Optimizes memory usage by trimming excess capacity from all internal collections
        /// and removing cancelled events.
        /// </summary>
        /// <returns>The number of cancelled events that were cleaned up during optimization.</returns>
        /// <remarks>
        /// <para>
        /// This method:
        /// - Processes all pending cancelled events
        /// - Trims excess capacity from the event queue
        /// - Trims excess capacity from all event pools
        /// - Trims excess capacity from the cancelled events list
        /// </para>
        /// 
        /// <para>
        /// It's useful to call this method during scene transitions or after processing
        /// large batches of events.
        /// </para>
        ///
        /// <para>
        /// This method is called automatically on each scene unload event.
        /// </para>
        ///
        /// <para>Thread-safe operation protected by a lock.</para>
        /// </remarks>
        public int OptimizeMemory()
        {
#if __EVENTTIMELINE_DEBUG_VERBOSE
            var stopwatch = new Stopwatch();
            stopwatch.Start();

            lock (_cancelledEventsLock)
            {
                AsyncLogger.Log("[EventTimelineQueue] >>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>");
                AsyncLogger.Log("[EventTimelineQueue] Starting memory optimization");
                AsyncLogger.Log("[EventTimelineQueue] Initial state:");
                AsyncLogger.LogFormat("[EventTimelineQueue] - Event queue count: {0}", _eventQueue.Count);
                AsyncLogger.LogFormat("[EventTimelineQueue] - Cancelled events count: {0}", _cancelledEvents.Count);
                AsyncLogger.LogFormat("[EventTimelineQueue] - Cancelled events capacity: {0}", _cancelledEvents.Capacity);
                AsyncLogger.LogFormat("[EventTimelineQueue] - Cleanup frames counter: {0}", _framesSinceCleanup);
            }
#endif

            // Process any pending cancelled events first
#if __EVENTTIMELINE_DEBUG_VERBOSE
            AsyncLogger.Log("[EventTimelineQueue] Step 1: Processing pending cancelled events...");
#endif
            var cleanedUpCount = RemoveCancelledEvents();

            // Reset the cleanup frame counter since we just did a cleanup
            _framesSinceCleanup = 0;

#if __EVENTTIMELINE_DEBUG_VERBOSE
            AsyncLogger.Log("[EventTimelineQueue] Step 2: Reset cleanup frame counter to 0");
#endif

            // Trim the main event queue
#if __EVENTTIMELINE_DEBUG_VERBOSE
            var preQueueTrimCount = _eventQueue.Count;
            AsyncLogger.Log("[EventTimelineQueue] Step 3: Trimming main event queue...");
            AsyncLogger.LogFormat("[EventTimelineQueue] Pre-trim queue count: {0}", preQueueTrimCount);
#endif

            _eventQueue.TrimExcess();

#if __EVENTTIMELINE_DEBUG_VERBOSE
            AsyncLogger.LogFormat("[EventTimelineQueue] Post-trim queue count: {0}", _eventQueue.Count);
#endif

            // Trim all event pools
#if __EVENTTIMELINE_DEBUG_VERBOSE
            AsyncLogger.Log("[EventTimelineQueue] Step 4: Trimming event pools...");
#endif

            TrimEventPools();

            lock (_cancelledEventsLock)
            {
#if __EVENTTIMELINE_DEBUG_VERBOSE
                AsyncLogger.Log("[EventTimelineQueue] Step 5: Checking cancelled events list capacity...");
                var initialCapacity = _cancelledEvents.Capacity;
                AsyncLogger.LogFormat("[EventTimelineQueue] Current capacity: {0}", initialCapacity);
                AsyncLogger.LogFormat("[EventTimelineQueue] Cleanup threshold: {0}", ImmediateCleanupThreshold);
#endif

                // Trim the cancelled events list if it has grown larger than needed
                if (_cancelledEvents.Capacity > ImmediateCleanupThreshold)
                {
#if __EVENTTIMELINE_DEBUG_VERBOSE
                    AsyncLogger.LogFormat("[EventTimelineQueue] Reducing cancelled events capacity from {0} to {1}", _cancelledEvents.Capacity, ImmediateCleanupThreshold);
#endif
                    // Keep some buffer capacity, but not excessive
                    _cancelledEvents.Capacity = ImmediateCleanupThreshold;
                }
#if __EVENTTIMELINE_DEBUG_VERBOSE
                else
                {
                    AsyncLogger.Log("[EventTimelineQueue] Cancelled events capacity is within acceptable range");
                }
#endif

#if __EVENTTIMELINE_DEBUG_VERBOSE
                stopwatch.Stop();
                AsyncLogger.Log("[EventTimelineQueue] Memory optimization complete. Summary:");
                AsyncLogger.LogFormat("[EventTimelineQueue] - Total execution time: {0}ms", stopwatch.ElapsedMilliseconds);
                AsyncLogger.LogFormat("[EventTimelineQueue] - Cleaned up events: {0}", cleanedUpCount);
                AsyncLogger.LogFormat("[EventTimelineQueue] - Final event queue count: {0}", _eventQueue.Count);
                AsyncLogger.LogFormat("[EventTimelineQueue] - Final cancelled events count: {0}", _cancelledEvents.Count);
                AsyncLogger.LogFormat("[EventTimelineQueue] - Final cancelled events capacity: {0}", _cancelledEvents.Capacity);
                AsyncLogger.Log("[EventTimelineQueue] <<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<");
#endif
            }

            return cleanedUpCount;
        }

        /// <summary>
        /// Sets the interval (in frames) between cleanup attempts.
        /// </summary>
        /// <param name="frames">Number of frames to skip. Must be >= 1.</param>
        public void SetCleanupInterval(int frames)
        {
            cleanupInterval = Mathf.Max(1, frames);
        }

        /// <summary>
        /// Sets the maximum number of events to process per frame.
        /// </summary>
        /// <param name="maxEvents">Maximum events per frame. Use -1 for unlimited.</param>
        /// <remarks>
        /// Even when set to unlimited (-1), the maxProcessingTimeMs limit still applies
        /// to prevent frame time spikes.
        /// </remarks>
        public void SetMaxEventsPerFrame(int maxEvents)
        {
            maxEventsPerFrame = maxEvents;
        }

        /// <summary>
        /// Sets the maximum time budget for event processing per frame.
        /// </summary>
        /// <param name="maxTimeMs">Maximum processing time in milliseconds.</param>
        /// <remarks>
        /// This limit ensures frame rate stability by limiting event processing time.
        /// Must be greater than 0.
        /// </remarks>
        public void SetMaxProcessingTimePerFrame(float maxTimeMs)
        {
            maxProcessingTimeMs = Mathf.Max(0.1f, maxTimeMs);
        }

        /// <summary>
        /// Cancels the specified event and immediately removes it from the queue.
        /// </summary>
        /// <typeparam name="T">The type of event to cancel.</typeparam>
        /// <param name="evt">The event to cancel.</param>
        /// <returns>true if the event was found and cancelled; false if it wasn't in the queue.</returns>
        /// <remarks>
        /// This method is more efficient than letting cancelled events stay in the queue
        /// until their scheduled time. Thread-safe operation protected by a lock.
        /// </remarks>
        public bool CancelAndRemove<T>(T evt) where T : TimelineEvent
        {
            if (!_eventQueue.Contains(evt))
            {
#if __EVENTTIMELINE_DEBUG
            AsyncLogger.LogWarningFormat("[EventTimelineQueue] Attemting to cancel event of type {0} that is not in the queue.", evt.GetType().Name);
#endif
                return false;
            }

#if __EVENTTIMELINE_DEBUG_VERBOSE
            AsyncLogger.LogFormat("[EventTimelineQueue] Beginning cancellation and removal of event type {0}", evt.GetType().Name);
#endif

            evt.IsCancelled = true;

            // Remove from queue immediately
            _eventQueue.TryRemove(evt);

            // Reset the event's state now for potential resource optimization
            evt.Reset();

#if __EVENTTIMELINE_DEBUG_VERBOSE
            AsyncLogger.Log("[EventTimelineQueue] Event reset and added to cancelled events list");
#endif

            lock (_cancelledEventsLock)
            {
                _cancelledEvents.Add(evt);

                if (_cancelledEvents.Count < ImmediateCleanupThreshold)
                {
                    return true;
                }

#if __EVENTTIMELINE_DEBUG_VERBOSE
                AsyncLogger.LogFormat("[EventTimelineQueue] Immediate cleanup of cancelled events triggered. Count: {0} Immediate Cleanup Threshold {1}", _cancelledEvents.Count, ImmediateCleanupThreshold);
#endif
            }

            // Trigger immediate cleanup if we have too many pending cancellations
            RemoveCancelledEvents();
            _framesSinceCleanup = 0;

            return true;
        }

        /// <summary>
        /// Cancels and removes all events of the specified type from the queue.
        /// </summary>
        /// <typeparam name="T">The type of events to cancel.</typeparam>
        /// <returns>The number of events that were cancelled and removed.</returns>
        /// <remarks>
        /// This operation is more efficient than cancelling events individually.
        /// Thread-safe operation protected by a lock.
        /// </remarks>
        public int CancelAndRemoveAll<T>() where T : TimelineEvent
        {
            lock (_cancelledEventsLock)
            {
                var count = 0;
                var type = typeof(T);

                // Get all events in their current priority order
                var allEvents = _eventQueue.DequeueRange(_eventQueue.Count);

                // Clear the queue since we've taken everything
                _eventQueue.Clear();

                // Process all events in one pass
                foreach (var evt in allEvents)
                {
                    if (evt.GetType() == type)
                    {
                        evt.IsCancelled = true;

                        // Reset the event's state now for potential resource optimization
                        evt.Reset();

                        _cancelledEvents.Add(evt);
                        count++;
                    }
                    else
                    {
                        // Only re-enqueue events that aren't of the target type
                        _eventQueue.Enqueue(evt);
                    }
                }

                return count;
            }
        }

        /// <summary>
        /// Immediately processes all cancelled events, removing them from the queue.
        /// </summary>
        /// <returns>The number of cancelled events that were removed.</returns>
        /// <remarks>
        /// This method is called automatically during Update but can be called manually
        /// if immediate cleanup is desired. Thread-safe operation protected by a lock.
        /// </remarks>
        public int RemoveCancelledEvents()
        {
            lock (_cancelledEventsLock)
            {
                if (_cancelledEvents.Count == 0)
                {
                    return 0;
                }

                // Remove from queue and return to pool
                foreach (var evt in _cancelledEvents)
                {
                    _eventQueue.TryRemove(evt); // Remove from queue if still there

                    // Return to pool for reuse
                    // Event state will be reset by the pool
                    ReturnToPool(evt);
                }

                var count = _cancelledEvents.Count;
                _cancelledEvents.Clear();

#if __EVENTTIMELINE_DEBUG_VERBOSE
                AsyncLogger.LogFormat("[EventTimelineQueue] Cleaned up {0} cancelled events after {1} frames", count, _framesSinceCleanup);
#endif

                return count;
            }
        }

        /// <summary>
        /// Gets the current number of cancelled events pending cleanup.
        /// </summary>
        /// <returns>The number of events in the cancelled events list awaiting removal.</returns>
        /// <remarks>
        /// This method provides a thread-safe way to check how many cancelled events are
        /// waiting to be cleaned up. A high number might indicate the need for immediate cleanup.
        /// </remarks>
        public int GetPendingCancellationCount()
        {
            lock (_cancelledEventsLock)
            {
                return _cancelledEvents.Count;
            }
        }

        /// <summary>
        /// Gets the total number of events currently scheduled in the queue.
        /// </summary>
        /// <returns>The total count of events in the event queue.</returns>
        /// <remarks>
        /// This method provides a thread-safe way to check the size of the event queue.
        /// The count includes both active and cancelled events that haven't been cleaned up yet.
        /// </remarks>
        public int GetEventsCount()
        {
            return _eventQueue.Count;
        }

        /// <summary>
        /// Unity Update callback that processes scheduled events.
        /// </summary>
        /// <remarks>
        /// Checks for and executes events that are due based on CurrentTime.
        /// Skips processing if the timeline is paused.
        /// Processes up to 10 events per frame to maintain performance.
        /// </remarks>
        protected override void Update()
        {
            base.Update();

            if (isPaused)
            {
                return;
            }

            CleanUpCancelledEvents();
            ProcessEvents();
        }

        /// <summary>
        /// Determines whether a cleanup operation should be performed based on the current state of the
        /// event timeline queue.
        /// </summary>
        /// <returns>
        /// True if cleanup is required, either because the configured interval has been reached or
        /// the number of pending cancellations exceeds the immediate cleanup threshold. Otherwise, false.
        /// </returns>
        /// <remarks>
        /// This method monitors the state of the queue to ensure efficient memory usage and timely
        /// removal of cancelled events.
        /// </remarks>
        private bool ShouldCleanup()
        {
            // Check if we should run cleanup
            var shouldCleanup = _framesSinceCleanup >= cleanupInterval;

            // Also cleanup if we have too many pending cancellations
            if (!shouldCleanup && GetPendingCancellationCount() >= ImmediateCleanupThreshold)
            {
                shouldCleanup = true;
            }

#if __EVENTTIMELINE_DEBUG_VERBOSE
            if (shouldCleanup)
            {
                var reason = _framesSinceCleanup >= cleanupInterval 
                    ? "frame interval reached" 
                    : "cancellation threshold exceeded";
                AsyncLogger.LogFormat("[EventTimelineQueue] Cleanup needed: {0}. Frames since last cleanup: {1}", reason, _framesSinceCleanup);
            }
#endif

            return shouldCleanup;
        }

        /// <summary>
        /// Performs cleanup of cancelled events from the event timeline queue, ensuring efficient memory usage.
        /// </summary>
        /// <remarks>
        /// This method increments an internal frame counter and checks if a cleanup operation is necessary using the ShouldCleanup method.
        /// If cleanup is warranted, it removes cancelled events from the queue and resets the counter.
        /// It is called during the Update loop to maintain smooth event processing.
        /// </remarks>
        private void CleanUpCancelledEvents()
        {
            _framesSinceCleanup++;

            if (!ShouldCleanup())
            {
                return;
            }

            RemoveCancelledEvents();

            _framesSinceCleanup = 0;
        }

        /// <summary>
        /// Processes pending events from the queue, ensuring execution within the allowed processing time window or event limit per frame.
        /// </summary>
        /// <remarks>
        /// This method checks whether event processing should proceed by evaluating conditions
        /// like frame limits and time constraints. It starts a stopwatch to track execution time,
        /// processes events in batches, and resets the stopwatch upon completion.
        /// Typically called during the update cycle to handle the execution of events that are ready or due.
        /// Ensures efficient and controlled event handling within defined constraints.
        /// </remarks>
        private void ProcessEvents()
        {
            if (!ShouldProcessEvents())
            {
                return;
            }

#if __EVENTTIMELINE_DEBUG_VERBOSE
            AsyncLogger.LogFormat("[EventTimelineQueue] Beginning event processing. Queue size: {0}", _eventQueue.Count);
#endif

            _stopwatch.Start();

            ProcessEventBatches();

            _stopwatch.Reset();

#if __EVENTTIMELINE_DEBUG_VERBOSE
            AsyncLogger.Log("[EventTimelineQueue] Event processing completed");
#endif
        }

        /// <summary>
        /// Determines if events should be processed based on the current state of the event queue and time conditions.
        /// </summary>
        /// <returns>
        /// True if there are events in the queue, the next event in the queue is ready to be processed based on its scheduled time, and the current time has surpassed or met the scheduled time; otherwise, false.
        /// </returns>
        private bool ShouldProcessEvents()
        {
            return _eventQueue.Count > 0 &&
                   _eventQueue.TryPeek(out var nextEvent) &&
                   nextEvent.ScheduledTime <= Time.time;
        }

        /// <summary>
        /// Processes events in batches, adhering to time and event count constraints.
        /// </summary>
        /// <remarks>
        /// The method ensures processing either halts or continues based on configured constraints:
        /// - Maximum number of events to process per frame.
        /// - Allocated maximum processing time in milliseconds.
        /// Processing is divided into batches, where the batch size is dynamically calculated to optimize efficiency.
        /// </remarks>
        private void ProcessEventBatches()
        {
            var processedCount = 0;
            var isTimeLimited = maxEventsPerFrame < 0;
            var eventLimit = isTimeLimited ? int.MaxValue : maxEventsPerFrame;

            while (processedCount < eventLimit)
            {
                if (_stopwatch.ElapsedMilliseconds > maxProcessingTimeMs)
                {
                    break;
                }

                var batchSize = CalculateBatchSize(isTimeLimited, eventLimit, processedCount);
                if (batchSize == 0) break;

                processedCount += ProcessSingleBatch(batchSize);
            }
        }

        /// <summary>
        /// Calculates the batch size for processing events based on given constraints.
        /// </summary>
        /// <param name="isTimeLimited">Indicates whether the processing is constrained by time.</param>
        /// <param name="eventLimit">The maximum number of events allowed to be processed in total.</param>
        /// <param name="processedCount">The number of events already processed so far.</param>
        /// <returns>The calculated batch size.</returns>
        /// <remarks>
        /// If processing is time-limited, the batch size is capped at 100 or the total events available in the queue, whichever is smaller.
        /// Otherwise, the batch size is determined by the remaining event limit and the total events available in the queue.
        /// </remarks>
        private int CalculateBatchSize(bool isTimeLimited, int eventLimit, int processedCount)
        {
            return isTimeLimited
                ? Math.Min(100, _eventQueue.Count)
                : Math.Min(eventLimit - processedCount, _eventQueue.Count);
        }

        /// <summary>
        /// Processes a batch of events from the event queue, up to the specified batch size, while respecting time limits.
        /// </summary>
        /// <param name="batchSize">The maximum number of events to process in this batch.</param>
        /// <returns>The number of events successfully processed in the batch.</returns>
        /// <remarks>
        /// Events that are scheduled for a future time are re-queued for future processing.
        /// Processing stops if the time limit for execution is exceeded.
        /// This method ensures that events are handled efficiently and time constraints are respected.
        /// </remarks>
        private int ProcessSingleBatch(int batchSize)
        {
            var processedCount = 0;
            var dequeued = 0;
            var currentTime = Time.time;

#if __EVENTTIMELINE_DEBUG_VERBOSE
            AsyncLogger.LogFormat("[EventTimelineQueue] Beginning batch processing of up to {0} events. Current time: {1:F3}", batchSize, currentTime);
#endif

            _futureEvents.Clear();

            foreach (var evt in _eventQueue.DequeueRange(batchSize))
            {
                dequeued++;

#if __EVENTTIMELINE_DEBUG_VERBOSE
            AsyncLogger.LogFormat("[EventTimelineQueue] Processing event {0} of batch. Event time: {1:F3}", dequeued, evt.ScheduledTime);
#endif

                if (evt.ScheduledTime > currentTime)
                {
                    _futureEvents.Add(evt);
                    continue;
                }

                ProcessSingleEvent(evt);
                processedCount++;

                if (_stopwatch.ElapsedMilliseconds < maxProcessingTimeMs)
                {
                    continue;
                }

#if __EVENTTIMELINE_DEBUG
            AsyncLogger.LogWarningFormat("[EventTimelineQueue] Processing time limit reached ({0}ms). Adding remaining events to future queue.", maxProcessingTimeMs);
#endif

                // Add remaining events to future events
                _futureEvents.AddRange(_eventQueue.DequeueRange(batchSize - dequeued));

                break;
            }

            HandleFutureEvents();

#if __EVENTTIMELINE_DEBUG_VERBOSE
            AsyncLogger.LogFormat("[EventTimelineQueue] Batch processing complete. Processed {0} events, Dequeued {1} events", processedCount, dequeued);
#endif

            return processedCount;
        }

        /// <summary>
        /// Processes a single timeline event, executing it and returning it to the event pool.
        /// </summary>
        /// <param name="evt">The timeline event to process. Must be derived from TimelineEvent.</param>
        /// <remarks>
        /// This method ensures that the event is executed via its internal execution mechanism
        /// and then returned to the pool for reuse. Designed for efficient batch processing
        /// of events within the timeline queue.
        /// </remarks>
        private void ProcessSingleEvent(TimelineEvent evt)
        {
#if __EVENTTIMELINE_DEBUG_VERBOSE
            AsyncLogger.LogFormat("[EventTimelineQueue] Processing single event of type {0}", evt.GetType().Name);
#endif

            evt.ExecuteInternal();

#if __EVENTTIMELINE_DEBUG_VERBOSE
            AsyncLogger.Log("[EventTimelineQueue] Event execution completed, returning to pool");
#endif

            ReturnToPool(evt);
        }

        /// <summary>
        /// Reinserts future events into the event queue if they are still pending.
        /// </summary>
        /// <remarks>
        /// This method ensures that any events in the `_futureEvents` list, typically identified as not yet ready for processing,
        /// are re-queued for subsequent handling. Operates on the `_eventQueue` to maintain event processing consistency.
        /// </remarks>
        private void HandleFutureEvents()
        {
            if (_futureEvents.Count > 0)
            {
                _eventQueue.EnqueueRange(_futureEvents);

#if __EVENTTIMELINE_DEBUG_VERBOSE
            AsyncLogger.LogFormat("[EventTimelineQueue] Re-queued {0} future events", _futureEvents.Count);
#endif
            }
        }
    }
}