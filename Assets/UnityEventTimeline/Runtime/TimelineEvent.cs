#nullable enable

#if (DEVELOPMENT_BUILD || UNITY_EDITOR) && EVENTTIMELINE_DEBUG
#define __EVENTTIMELINE_DEBUG
#endif

#if __EVENTTIMELINE_DEBUG && EVENTTIMELINE_DEBUG_VERBOSE
#define __EVENTTIMELINE_DEBUG_VERBOSE
#endif

using System;
using System.Collections.Generic;

#if __EVENTTIMELINE_DEBUG || __EVENTTIMELINE_DEBUG_VERBOSE
using UnityEngine;
#endif

namespace UnityEventTimeline
{
    /// <summary>
    /// Abstract base class for events that can be scheduled and executed in an EventTimeline.
    /// Provides core functionality for timing, cancellation, and execution of timeline events.
    /// </summary>
    /// <remarks>
    /// TimelineEvent serves as the foundation for creating custom events in the timeline system.
    /// It implements IComparable&lt;TimelineEvent&gt; to enable automatic ordering in the priority queue
    /// based on scheduled execution time. Events can be cancelled, scheduled, and execute custom
    /// logic through the abstract Execute method.
    /// 
    /// Example usage:
    /// <code>
    /// public class MyCustomEvent : TimelineEvent
    /// {
    ///     protected override void Execute()
    ///     {
    ///         // Custom event logic here
    ///     }
    /// }
    /// </code>
    /// </remarks>
    public abstract class TimelineEvent : IComparable<TimelineEvent>, IDisposable
    {
        /// <summary>
        /// Flag to track whether Dispose has been called.
        /// </summary>
        private bool _disposed;

        /// <summary>
        /// Gets or sets the scheduled execution time for this event.
        /// </summary>
        /// <remarks>
        /// The time is represented as a float value corresponding to Unity's Time.time.
        /// This property is managed internally by the EventTimeline system and should not
        /// be modified directly by user code.
        /// </remarks>
        public float ScheduledTime { get; internal set; }

        /// <summary>
        /// Gets or sets whether this event has been cancelled.
        /// </summary>
        /// <remarks>
        /// Cancelled events will not execute even if their scheduled time is reached.
        /// This property is managed internally but can be modified through the Cancel() method.
        /// </remarks>
        public bool IsCancelled { get; internal set; }

        /// <summary>
        /// Compares this event with another event based on their scheduled execution times.
        /// </summary>
        /// <param name="other">The TimelineEvent to compare against.</param>
        /// <returns>
        /// A negative value if this event should execute before the other event,
        /// zero if they should execute at the same time,
        /// or a positive value if this event should execute after the other event.
        /// </returns>
        /// <remarks>
        /// This implementation ensures events are processed in chronological order
        /// when stored in the EventTimeline's priority queue.
        /// </remarks>
        public int CompareTo(TimelineEvent other)
        {
            return ScheduledTime.CompareTo(other.ScheduledTime);
        }

        /// <summary>
        /// Marks the event as cancelled, preventing it from executing when its scheduled time is reached.
        /// </summary>
        /// <remarks>
        /// Cancelled events remain in the timeline queue but are skipped during processing.
        /// They will be removed from the queue and returned to the object pool during the next
        /// event processing cycle.
        /// </remarks>
        public void Cancel()
        {
            ThrowIfDisposed();

            if (IsCancelled)
            {
#if __EVENTTIMELINE_DEBUG
                Debug.LogWarning($"[TimelineEvent] Attempted to cancel already cancelled event: {GetType().Name}");
#endif
                return;
            }

#if __EVENTTIMELINE_DEBUG_VERBOSE
            Debug.Log($"[TimelineEvent] Cancelling event: {GetType().Name} scheduled for {ScheduledTime:F3}");
#endif

            IsCancelled = true;

            // Notify the timeline for immediate removal
            EventTimeline.Instance.CancelAndRemove(this);

#if __EVENTTIMELINE_DEBUG_VERBOSE
            Debug.Log($"[TimelineEvent] Successfully cancelled and removed event: {GetType().Name}");
#endif
        }

        /// <summary>
        /// Defines the custom execution logic for this event.
        /// </summary>
        /// <remarks>
        /// This abstract method must be implemented by derived classes to define
        /// what happens when the event is executed. It will only be called if
        /// the event has not been cancelled.
        /// </remarks>
        protected abstract void Execute();

        /// <summary>
        /// Resets the state of the TimelineEvent instance to its default state.
        /// </summary>
        /// <remarks>
        /// This method is intended to prepare the event for reuse, ensuring that any
        /// internal state or properties are cleared or re-initialized as needed.
        /// Subclasses can override this method to implement custom resetting logic.
        /// </remarks>
        public virtual void Reset()
        {
            ThrowIfDisposed();

#if __EVENTTIMELINE_DEBUG_VERBOSE
            Debug.Log($"[TimelineEvent] Resetting event: {GetType().Name}");
#endif
        }

        /// <summary>
        /// Determines whether the event can be executed.
        /// </summary>
        /// <returns>
        /// true if the event can be executed; false if it should be skipped.
        /// </returns>
        /// <remarks>
        /// This virtual method can be overridden by derived classes to add
        /// additional execution conditions beyond the basic cancellation check.
        /// </remarks>
        protected virtual bool CanExecute()
        {
            ThrowIfDisposed();

            return true;
        }

        /// <summary>
        /// Internal method that handles the event execution process.
        /// </summary>
        /// <remarks>
        /// This method performs the cancellation check and invokes the Execute method
        /// if appropriate. It is called by the EventTimeline system and should not
        /// be called directly by user code.
        /// </remarks>
        internal virtual void ExecuteInternal()
        {
            ThrowIfDisposed();

#if __EVENTTIMELINE_DEBUG_VERBOSE
            Debug.Log("[TimelineEvent] >>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>");
            Debug.Log($"[TimelineEvent] Beginning internal execution of event: {GetType().Name}");
#endif

            if (IsCancelled)
            {
#if __EVENTTIMELINE_DEBUG
                Debug.LogWarning($"[TimelineEvent] Skipping execution of cancelled event: {GetType().Name}");
#endif
                return;
            }

            if (!CanExecute())
            {
#if __EVENTTIMELINE_DEBUG
                Debug.LogWarning($"[TimelineEvent] Event failed CanExecute check: {GetType().Name}");
#endif
                return;
            }

#if __EVENTTIMELINE_DEBUG_VERBOSE
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
#endif

            Execute();

#if __EVENTTIMELINE_DEBUG_VERBOSE
            stopwatch.Stop();
            Debug.Log($"[TimelineEvent] Successfully executed event: {GetType().Name}");
            Debug.Log($"[TimelineEvent] Execution time: {stopwatch.ElapsedMilliseconds}ms");
            Debug.Log("[TimelineEvent] <<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<");
#endif
        }

        /// <summary>
        /// Called when the event is being disposed. Override in derived classes to perform cleanup.
        /// </summary>
        protected virtual void OnDisposing()
        {
        }

        /// <summary>
        /// Performs cleanup of the event, including clearing any listeners.
        /// </summary>
        /// <param name="disposing">true if called from Dispose(), false if called from finalizer</param>
        protected virtual void Dispose(bool disposing)
        {
#if __EVENTTIMELINE_DEBUG_VERBOSE
            Debug.Log($"[TimelineEvent] Disposing event: {GetType().Name} (Disposing: {disposing})");
            Debug.Log($"[TimelineEvent] Current state - Disposed: {_disposed}, Cancelled: {IsCancelled}");
#endif

            if (_disposed)
            {
#if __EVENTTIMELINE_DEBUG
                Debug.LogWarning($"[TimelineEvent] Attempted to dispose already disposed event: {GetType().Name}");
#endif
                return;
            }

            if (disposing)
            {
                // Clear any managed resources here
                OnDisposing();
            }

            _disposed = true;

#if __EVENTTIMELINE_DEBUG_VERBOSE
            Debug.Log($"[TimelineEvent] Successfully disposed event: {GetType().Name}");
#endif
        }

        /// <summary>
        /// Disposes of the event and cleans up any resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Finalizer to ensure cleanup if Dispose wasn't called.
        /// </summary>
        ~TimelineEvent()
        {
#if __EVENTTIMELINE_DEBUG
            if (!_disposed)
            {
                Debug.LogWarning($"[TimelineEvent] Finalizer called for event: {GetType().Name}. This indicates the event wasn't properly disposed.");
            }
#endif
            Dispose(false);
        }

        /// <summary>
        /// Throws an ObjectDisposedException if this event has been disposed.
        /// </summary>
        protected void ThrowIfDisposed()
        {
            if (_disposed)
            {
#if __EVENTTIMELINE_DEBUG
                Debug.LogError($"[TimelineEvent] Attempted to access disposed event: {GetType().Name}");
#endif
                throw new ObjectDisposedException(GetType().Name);
            }
        }
    }

    /// <summary>
    /// Generic implementation of TimelineEvent that adds support for event listeners.
    /// </summary>
    /// <typeparam name="T">The specific event type that derives from this class.</typeparam>
    /// <remarks>
    /// This class extends the base TimelineEvent functionality by adding a type-safe
    /// event system. It allows registration of listeners that will be notified when
    /// events of this specific type are executed.
    /// 
    /// Example usage:
    /// <code>
    /// public class MyCustomEvent : TimelineEvent&lt;MyCustomEvent&gt;
    /// {
    ///     public string Data { get; set; }
    ///     
    ///     protected override void Execute()
    ///     {
    ///         // Custom event logic here
    ///     }
    /// }
    /// 
    /// // Adding a listener
    /// MyCustomEvent.AddListener(evt => Debug.Log(evt.Data));
    /// </code>
    /// </remarks>
    public abstract class TimelineEvent<T> : TimelineEvent where T : TimelineEvent<T>
    {
        /// <summary>
        /// Event that is raised when an event of type T is executed.
        /// </summary>
        /// <remarks>
        /// This static event allows global monitoring of all events of type T.
        /// Listeners are notified after the event's Execute method completes.
        /// </remarks>
        private static event Action<T> OnExecute = delegate { };

        /// <summary>
        /// List to track all active instances of this event type.
        /// Used to ensure proper cleanup of static resources.
        /// </summary>
        private static readonly List<WeakReference<T>> ActiveInstances = new();

        /// <summary>
        /// Checks if there are any active instances of this event type.
        /// </summary>
        public static bool HasActiveInstances
        {
            get
            {
                lock (ActiveInstances)
                {
#if __EVENTTIMELINE_DEBUG_VERBOSE
                    var beforeCleanup = ActiveInstances.Count;
#endif

                    CleanupDeadReferences();

#if __EVENTTIMELINE_DEBUG_VERBOSE
                    var afterCleanup = ActiveInstances.Count;
                    if (beforeCleanup != afterCleanup)
                    {
                        Debug.Log($"[TimelineEvent<{typeof(T).Name}>] Cleaned up {beforeCleanup - afterCleanup} dead references");
                    }

                    Debug.Log($"[TimelineEvent<{typeof(T).Name}>] Active instances: {afterCleanup}");
#endif

                    return ActiveInstances.Count > 0;
                }
            }
        }

        /// <summary>
        /// A weak reference to the current instance of the event, used to ensure
        /// proper cleanup and management of active instances without creating
        /// strong reference cycles.
        /// </summary>
        private readonly WeakReference<T> _weakThis;

        /// <summary>
        /// Adds a listener to be notified when events of type T are executed.
        /// </summary>
        /// <param name="listener">The action to be invoked when an event executes.</param>
        /// <remarks>
        /// Listeners are called after the event's Execute method completes successfully.
        /// Multiple listeners can be registered and will be called in the order they were added.
        /// </remarks>
        public static void AddListener(Action<T> listener)
        {
#if __EVENTTIMELINE_DEBUG_VERBOSE
            Debug.Log($"[TimelineEvent<{typeof(T).Name}>] Adding event listener");
#endif
            OnExecute += listener;
        }

        /// <summary>
        /// Removes a previously registered listener.
        /// </summary>
        /// <param name="listener">The action to be removed from the event notifications.</param>
        /// <remarks>
        /// If the listener is not found, no exception is thrown.
        /// It's safe to call this method even if the listener wasn't previously registered.
        /// </remarks>
        public static void RemoveListener(Action<T> listener)
        {
#if __EVENTTIMELINE_DEBUG_VERBOSE
            Debug.Log($"[TimelineEvent<{typeof(T).Name}>] Removing event listener");
#endif
            OnExecute -= listener;
        }

        /// <summary>
        /// Removes all registered event listeners.
        /// </summary>
        /// <remarks>
        /// This method safely clears all registered listeners for this event type.
        /// It's particularly useful during scene transitions or when needing to reset the event system.
        /// After calling this method, no listeners will be notified of event executions until new listeners are added.
        /// This method is thread-safe and can be called from any context.
        /// </remarks>
        public static void ClearListeners()
        {
#if __EVENTTIMELINE_DEBUG_VERBOSE
            Debug.Log($"[TimelineEvent<{typeof(T).Name}>] Clearing all event listeners");
#endif
            OnExecute = delegate { };
        }

        /// <summary>
        /// Removes dead references from the ActiveInstances list.
        /// </summary>
        private static void CleanupDeadReferences()
        {
            lock (ActiveInstances)
            {
#if __EVENTTIMELINE_DEBUG_VERBOSE
                var beforeCount = ActiveInstances.Count;
#endif
                ActiveInstances.RemoveAll(wr => !wr.TryGetTarget(out _));

#if __EVENTTIMELINE_DEBUG_VERBOSE
                var removedCount = beforeCount - ActiveInstances.Count;
                if (removedCount > 0)
                {
                    Debug.Log($"[TimelineEvent<{typeof(T).Name}>] Cleaned up {removedCount} dead references");
                }
#endif
            }
        }

        /// <summary>
        /// Initializes a new instance of the event and registers it for lifecycle tracking.
        /// </summary>
        protected TimelineEvent()
        {
            _weakThis = new WeakReference<T>((T)this);

            lock (ActiveInstances)
            {
#if __EVENTTIMELINE_DEBUG_VERBOSE
                Debug.Log($"[TimelineEvent<{typeof(T).Name}>] Creating new instance");
#endif

                ActiveInstances.Add(_weakThis);

                CleanupDeadReferences();

#if __EVENTTIMELINE_DEBUG_VERBOSE
                Debug.Log($"[TimelineEvent<{typeof(T).Name}>] Instance created. Total active instances: {ActiveInstances.Count}");
#endif
            }
        }

        /// <summary>
        /// Internal method that handles the event execution process and notifies listeners.
        /// </summary>
        /// <remarks>
        /// This override extends the base implementation by invoking any registered
        /// listeners after the event executes successfully. Listeners are not notified
        /// if the event is cancelled or fails the CanExecute check.
        /// </remarks>
        internal override void ExecuteInternal()
        {
            ThrowIfDisposed();

#if __EVENTTIMELINE_DEBUG_VERBOSE
            Debug.Log("[TimelineEvent] >>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>");
            Debug.Log($"[TimelineEvent] Beginning internal execution of event: {GetType().Name}");
#endif

            if (IsCancelled)
            {
#if __EVENTTIMELINE_DEBUG
                Debug.LogWarning($"[TimelineEvent] Skipping execution of cancelled event: {GetType().Name}");
#endif
                return;
            }

            if (!CanExecute())
            {
#if __EVENTTIMELINE_DEBUG
                Debug.LogWarning($"[TimelineEvent] Event failed CanExecute check: {GetType().Name}");
#endif
                return;
            }

#if __EVENTTIMELINE_DEBUG_VERBOSE
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
#endif

            Execute();

            OnExecute.Invoke((T)this);

#if __EVENTTIMELINE_DEBUG_VERBOSE
            stopwatch.Stop();
            Debug.Log($"[TimelineEvent] Successfully executed event: {GetType().Name}");
            Debug.Log($"[TimelineEvent] Execution time: {stopwatch.ElapsedMilliseconds}ms");
            Debug.Log("[TimelineEvent] <<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<");
#endif
        }

        /// <summary>
        /// Override of OnDisposing to handle cleanup of event-specific resources.
        /// </summary>
        protected override void OnDisposing()
        {
            base.OnDisposing();

            lock (ActiveInstances)
            {
#if __EVENTTIMELINE_DEBUG_VERBOSE
                Debug.Log($"[TimelineEvent<{typeof(T).Name}>] Removing active instance");
#endif

                ActiveInstances.Remove(_weakThis);

                // If this was the last instance, clear all listeners
                if (!HasActiveInstances)
                {
#if __EVENTTIMELINE_DEBUG_VERBOSE
                    Debug.Log($"[TimelineEvent<{typeof(T).Name}>] No more active instances, clearing listeners");
#endif
                    ClearListeners();
                }

#if __EVENTTIMELINE_DEBUG_VERBOSE
                Debug.Log($"[TimelineEvent<{typeof(T).Name}>] Disposal complete. Remaining active instances: {ActiveInstances.Count}");
#endif
            }
        }
    }
}