#nullable enable

#if (DEVELOPMENT_BUILD || UNITY_EDITOR) && EVENTTIMELINE_DEBUG
#define __EVENTTIMELINE_DEBUG
#endif

#if __EVENTTIMELINE_DEBUG && EVENTTIMELINE_DEBUG_VERBOSE
#define __EVENTTIMELINE_DEBUG_VERBOSE
#endif

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

#if __EVENTTIMELINE_DEBUG || __EVENTTIMELINE_DEBUG_VERBOSE
using UnityEventTimeline.Internal.Logger;
#endif

namespace UnityEventTimeline.Internal.EventQueue
{
    /// <summary>
    /// A thread-safe priority queue implementation that maintains items in ascending order based on their natural ordering.
    /// This class is designed specifically for managing scheduled events in a timeline system.
    /// </summary>
    /// <typeparam name="T">
    /// The type of elements in the priority queue. Must be a reference type that implements IComparable&lt;T&gt;.
    /// </typeparam>
    /// <remarks>
    /// <para>
    /// The priority queue is implemented using a binary min-heap data structure, ensuring O(log n) 
    /// complexity for enqueue and dequeue operations. All operations are thread-safe, protected 
    /// by a synchronization lock.
    /// </para>
    /// <para>
    /// Key features:
    /// <list type="bullet">
    /// <item>Thread-safe operations for concurrent access</item>
    /// <item>Efficient O(log n) insertions and removals</item>
    /// <item>Object pooling friendly with Clear() and TrimExcess() support</item>
    /// <item>Bulk operations support through EnqueueRange and DequeueRange</item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <example>
    /// Here's a basic example of using EventPriorityQueue:
    /// <code>
    /// var queue = new EventPriorityQueue&lt;TimelineEvent&gt;();
    /// 
    /// // Add items to the queue
    /// queue.Enqueue(new TimelineEvent { ScheduledTime = 1.0f });
    /// queue.Enqueue(new TimelineEvent { ScheduledTime = 0.5f });
    /// 
    /// // Process items in order
    /// while (queue.TryPeek(out var nextEvent))
    /// {
    ///     // Process event with lowest ScheduledTime first
    /// }
    /// </code>
    /// </example>
    public class EventPriorityQueue<T> : IEventPriorityQueue<T> where T : class, IComparable<T>
    {
        /// <summary>
        /// The default initial capacity of the event queue.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The heap capacity can vary significantly between applications. This value represents
        /// a reasonable default that balances memory usage with typical usage patterns.
        /// </para>
        /// <para>
        /// If your application consistently handles more or fewer events, you can specify a
        /// different initial capacity through the constructor.
        /// </para>
        /// </remarks>
        private const int DefaultCapacity = 100;

        /// <summary>
        /// The internal list that stores the binary heap data structure.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Implements a binary min-heap where elements are ordered based on their natural 
        /// ordering defined by IComparable&lt;T&gt;. The first element (_heap[0]) is always 
        /// the item with the lowest value according to the comparison.
        /// </para>
        /// <para>
        /// The heap property is maintained such that for any given index i:
        /// <list type="bullet">
        /// <item>The left child is at index 2i + 1</item>
        /// <item>The right child is at index 2i + 2</item>
        /// <item>The parent is at index (i - 1) / 2</item>
        /// <item>Each node is less than or equal to its children</item>
        /// </list>
        /// </para>
        /// <para>
        /// The heap property is maintained through HeapifyUp and HeapifyDown operations 
        /// during modifications.
        /// </para>
        /// </remarks>
        private readonly List<T> _heap;

        /// <summary>
        /// The synchronization object used to ensure thread-safe operations.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This object is used with the lock statement to protect all heap operations including
        /// additions, removals, and queries. Each public method acquires this lock before 
        /// accessing or modifying the heap to prevent race conditions in multithreaded scenarios.
        /// </para>
        /// <para>
        /// The lock is implemented using a dedicated object rather than locking on 'this' to
        /// prevent potential deadlocks and follow best practices for synchronization.
        /// </para>
        /// </remarks>
        private readonly object _heapLock = new();

        /// <summary>
        /// Gets the number of elements currently in the priority queue.
        /// </summary>
        /// <returns>The total number of elements in the queue.</returns>
        /// <remarks>
        /// <para>
        /// This property is thread-safe and uses a lock to ensure accurate counting in 
        /// multi-threaded scenarios. The count represents the total number of elements 
        /// currently in the queue, regardless of their priority or scheduled execution time.
        /// </para>
        /// <para>
        /// Note that accessing this property requires acquiring a lock, so frequent access
        /// in performance-critical sections should be avoided.
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// var queue = new EventPriorityQueue&lt;TimelineEvent&gt;();
        /// queue.Enqueue(new TimelineEvent());
        /// Console.WriteLine($"Queue contains {queue.Count} events");
        /// </code>
        /// </example>
        public int Count
        {
            get
            {
                lock (_heapLock)
                {
                    return _heap.Count;
                }
            }
        }

        /// <summary>
        /// Initializes a new instance of the EventPriorityQueue class with the specified capacity.
        /// </summary>
        /// <param name="capacity">
        /// The initial capacity of the queue. If not specified, defaults to DefaultCapacity.
        /// </param>
        /// <remarks>
        /// <para>
        /// The capacity parameter determines the initial size of the internal storage array.
        /// While the queue will automatically grow as needed, setting an appropriate initial
        /// capacity can improve performance by reducing the number of resizing operations.
        /// </para>
        /// <para>
        /// Choose a capacity that is close to but slightly larger than the expected number
        /// of concurrent events in your application.
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// // Create a queue with custom initial capacity
        /// var queue = new EventPriorityQueue&lt;TimelineEvent&gt;(1000);
        /// 
        /// // Create a queue with default capacity
        /// var defaultQueue = new EventPriorityQueue&lt;TimelineEvent&gt;();
        /// </code>
        /// </example>
        public EventPriorityQueue(int capacity = DefaultCapacity)
        {
            _heap = new List<T>(capacity > 0 ? capacity : DefaultCapacity);

#if __EVENTTIMELINE_DEBUG_VERBOSE
        AsyncLogger.LogFormat("[EventPriorityQueue] Initialized with capacity: {0}, DefaultCapacity: {1}", capacity, DefaultCapacity);
#endif
        }

        /// <summary>
        /// Attempts to retrieve the item at the front of the queue without removing it.
        /// </summary>
        /// <param name="item">When this method returns, contains the first item in the queue if available; otherwise, null.</param>
        /// <returns>true if an item was successfully retrieved; otherwise, false.</returns>
        /// <remarks>
        /// This operation is thread-safe and will return the item with the lowest natural ordering based on IComparable&lt;T&gt;.
        /// </remarks>
        public bool TryPeek([NotNullWhen(true)] out T? item)
        {
            lock (_heapLock)
            {
                if (_heap.Count > 0)
                {
                    item = _heap[0];
#if __EVENTTIMELINE_DEBUG_VERBOSE
                AsyncLogger.LogFormat("[EventPriorityQueue] Peeked item of type {0}", item.GetType().Name);
#endif
                    return true;
                }

#if __EVENTTIMELINE_DEBUG
            AsyncLogger.LogWarning("[EventPriorityQueue] TryPeek failed - queue is empty");
#endif
                item = null;
                return false;
            }
        }

        /// <summary>
        /// Adds an item to the priority queue.
        /// </summary>
        /// <param name="item">The item to add to the queue.</param>
        /// <remarks>
        /// This operation is thread-safe and maintains the heap property by performing a heapify-up operation.
        /// The item will be positioned in the queue based on its natural ordering defined by IComparable&lt;T&gt;.
        /// </remarks>
        public void Enqueue(T item)
        {
            lock (_heapLock)
            {
#if __EVENTTIMELINE_DEBUG_VERBOSE
            var initialCount = _heap.Count;
            AsyncLogger.LogFormat("[EventPriorityQueue] Enqueuing item of type {0}. Current queue size: {1}", item.GetType().Name, initialCount);
#endif
                _heap.Add(item);
                HeapifyUp(_heap.Count - 1);

#if __EVENTTIMELINE_DEBUG_VERBOSE
            AsyncLogger.LogFormat("[EventPriorityQueue] Item enqueued successfully. New queue size: {0}", _heap.Count);
#endif
            }
        }

        /// <summary>
        /// Adds multiple items to the priority queue in a single operation.
        /// </summary>
        /// <param name="items">The collection of items to add to the queue.</param>
        /// <remarks>
        /// This operation is thread-safe and optimizes the heapification process for bulk insertions.
        /// Rather than heapifying after each insertion, it performs a single heapify operation after all items are added.
        /// </remarks>
        public void EnqueueRange(IEnumerable<T> items)
        {
            lock (_heapLock)
            {
#if __EVENTTIMELINE_DEBUG_VERBOSE
            var initialCount = _heap.Count;
            AsyncLogger.LogFormat("[EventPriorityQueue] Beginning bulk enqueue operation. Current queue size: {0}", initialCount);
#endif
                _heap.AddRange(items);

                // Heapify from the last parent node
                for (var i = _heap.Count / 2 - 1; i >= 0; i--)
                {
                    HeapifyDown(i);
                }

#if __EVENTTIMELINE_DEBUG_VERBOSE
            var itemsAdded = _heap.Count - initialCount;
            AsyncLogger.LogFormat("[EventPriorityQueue] Bulk enqueue completed. Added {0} items. New queue size: {1}", itemsAdded, _heap.Count);
#endif
            }
        }

        /// <summary>
        /// Removes and returns multiple items from the front of the queue.
        /// </summary>
        /// <param name="count">The maximum number of items to dequeue.</param>
        /// <returns>An enumerable collection containing the dequeued items in ascending order.</returns>
        /// <remarks>
        /// This operation is thread-safe and optimized for both small and large range retrievals.
        /// For large ranges (> 50% of queue size), it uses a more efficient bulk operation.
        /// If count is less than or equal to 0, returns an empty array.
        /// </remarks>
        public IEnumerable<T> DequeueRange(int count)
        {
            if (count <= 0)
            {
#if __EVENTTIMELINE_DEBUG
            AsyncLogger.LogWarning("[EventPriorityQueue] DequeueRange called with count <= 0, returning empty array");
#endif
                return Array.Empty<T>();
            }

            List<T> result;
            lock (_heapLock)
            {
#if __EVENTTIMELINE_DEBUG_VERBOSE
            var initialCount = _heap.Count;
            AsyncLogger.LogFormat("[EventPriorityQueue] Beginning DequeueRange operation. Requested: {0}, Available: {1}", count, initialCount);
#endif

                var resultSize = Math.Min(count, _heap.Count);
                result = new List<T>(resultSize);

                // For large ranges, rebuild heap once instead of multiple times
                if (resultSize > _heap.Count / 2)
                {
#if __EVENTTIMELINE_DEBUG_VERBOSE
                AsyncLogger.LogFormat("[EventPriorityQueue] Using bulk dequeue optimization for {0} items", resultSize);
#endif
                    result.AddRange(_heap.OrderBy(x => x));
                    _heap.Clear();
                    return result.Take(count);
                }

                // For smaller ranges
                while (count-- > 0 && _heap.Count > 0)
                {
                    result.Add(DequeueInternal());
                }

#if __EVENTTIMELINE_DEBUG_VERBOSE
            AsyncLogger.LogFormat("[EventPriorityQueue] DequeueRange completed. Dequeued: {0}, Remaining: {1}", result.Count, _heap.Count);
#endif
            }

            return result;
        }

        /// <summary>
        /// Removes all items from the priority queue.
        /// </summary>
        /// <remarks>
        /// This operation is thread-safe and completely empties the queue.
        /// After this operation, Count will be 0.
        /// </remarks>
        public void Clear()
        {
            lock (_heapLock)
            {
#if __EVENTTIMELINE_DEBUG_VERBOSE
            var clearedCount = _heap.Count;
            AsyncLogger.LogFormat("[EventPriorityQueue] Clearing queue. Items to be cleared: {0}", clearedCount);
#endif

                _heap.Clear();

#if __EVENTTIMELINE_DEBUG_VERBOSE
            AsyncLogger.Log("[EventPriorityQueue] Queue cleared successfully");
#endif
            }
        }

        /// <summary>
        /// Reduces the internal capacity of the queue to match its current size.
        /// </summary>
        /// <remarks>
        /// This operation is thread-safe and can be used to optimize memory usage when the queue size has decreased significantly.
        /// It's recommended to call this method after removing a large number of items from the queue.
        /// </remarks>
        public void TrimExcess()
        {
            lock (_heapLock)
            {
#if __EVENTTIMELINE_DEBUG_VERBOSE
            var initialCapacity = _heap.Capacity;
            AsyncLogger.LogFormat("[EventPriorityQueue] Beginning TrimExcess. Current capacity: {0}, Count: {1}", initialCapacity, _heap.Count);
#endif

                _heap.TrimExcess();

#if __EVENTTIMELINE_DEBUG_VERBOSE
            AsyncLogger.LogFormat("[EventPriorityQueue] TrimExcess completed. New capacity: {0}", _heap.Capacity);
#endif
            }
        }

        /// <summary>
        /// Determines whether the specified item exists in the priority queue.
        /// </summary>
        /// <param name="item">The item to locate in the queue.</param>
        /// <returns>true if the item is found in the queue; otherwise, false.</returns>
        /// <remarks>
        /// This operation is thread-safe and performs a linear search through the queue.
        /// Note that this operation does not consider the priority ordering when searching.
        /// </remarks>
        public bool Contains(T item)
        {
            lock (_heapLock)
            {
                var contains = _heap.Contains(item);
#if __EVENTTIMELINE_DEBUG_VERBOSE
            AsyncLogger.LogFormat("[EventPriorityQueue] Contains check for item of type {0}: {1}", item.GetType().Name, contains);
#endif
                return contains;
            }
        }

        /// <summary>
        /// Removes a specific item from the queue if it exists.
        /// </summary>
        /// <param name="item">The item to remove from the queue.</param>
        /// <returns>true if the item was found and removed; otherwise, false.</returns>
        /// <remarks>
        /// This operation is thread-safe and performs a search through the queue to find the specified item.
        /// After removing the item, the heap property is maintained through re-heapification.
        /// Time complexity is O(n) for the search plus O(log n) for the heap maintenance.
        /// </remarks>
        public bool TryRemove(T item)
        {
            lock (_heapLock)
            {
#if __EVENTTIMELINE_DEBUG_VERBOSE
            AsyncLogger.LogFormat("[EventPriorityQueue] Attempting to remove item of type {0}", item.GetType().Name);
#endif

                var index = _heap.IndexOf(item);
                if (index == -1)
                {
#if __EVENTTIMELINE_DEBUG
                AsyncLogger.LogWarning("[EventPriorityQueue] Item not found in queue");
#endif
                    return false;
                }

                // If we're removing the last item, just remove it
                if (index == _heap.Count - 1)
                {
                    _heap.RemoveAt(index);
#if __EVENTTIMELINE_DEBUG_VERBOSE
                AsyncLogger.Log("[EventPriorityQueue] Removed last item in queue");
#endif
                    return true;
                }

                // Replace the item with the last item in the heap
                var lastIndex = _heap.Count - 1;
                _heap[index] = _heap[lastIndex];
                _heap.RemoveAt(lastIndex);

                // If we still have items, we need to maintain the heap property
                // We need to check both up and down as the replacement item
                // could be either smaller or larger than its parent
                HeapifyUp(index);
                HeapifyDown(index);

#if __EVENTTIMELINE_DEBUG_VERBOSE
            AsyncLogger.LogFormat("[EventPriorityQueue] Item removed successfully. New queue size: {0}", _heap.Count);
#endif

                return true;
            }
        }

        /// <summary>
        /// Removes and returns the item at the front of the queue.
        /// </summary>
        /// <returns>The item with the lowest priority value from the queue.</returns>
        /// <remarks>
        /// This method maintains the heap property by moving the last element to the root
        /// and performing a heapify-down operation. This method assumes the caller holds the lock
        /// and that the queue is not empty.
        /// Time complexity: O(log n) where n is the number of elements in the queue.
        /// </remarks>
        private T DequeueInternal()
        {
#if __EVENTTIMELINE_DEBUG_VERBOSE
        AsyncLogger.Log("[EventPriorityQueue] Beginning internal dequeue operation");
#endif

            var result = _heap[0];
            var lastIndex = _heap.Count - 1;
            _heap[0] = _heap[lastIndex];
            _heap.RemoveAt(lastIndex);

            if (_heap.Count > 0)
            {
                HeapifyDown(0);
            }

#if __EVENTTIMELINE_DEBUG_VERBOSE
        AsyncLogger.LogFormat("[EventPriorityQueue] Internal dequeue completed. Dequeued item of type {0}", result.GetType().Name);
#endif

            return result;
        }

        /// <summary>
        /// Restores the heap property by moving an item up the heap until it's in the correct position.
        /// </summary>
        /// <param name="index">The index of the item to move up the heap.</param>
        /// <remarks>
        /// This method is called after adding a new item to the end of the heap.
        /// It compares the item with its parent and swaps them if they are in the wrong order,
        /// continuing until the heap property is restored.
        /// Time complexity: O(log n) in worst case, where n is the number of elements in the queue.
        /// </remarks>
        private void HeapifyUp(int index)
        {
#if __EVENTTIMELINE_DEBUG_VERBOSE
        AsyncLogger.LogFormat("[EventPriorityQueue] Beginning HeapifyUp from index {0}", index);
        var initialIndex = index;
#endif

            var item = _heap[index];

            while (index > 0)
            {
                var parentIndex = (index - 1) >> 1;
                var parent = _heap[parentIndex];

                if (parent.CompareTo(item) <= 0)
                {
                    break;
                }

                _heap[index] = parent;
                index = parentIndex;
            }

            _heap[index] = item;

#if __EVENTTIMELINE_DEBUG_VERBOSE
        if (index != initialIndex)
        {
            AsyncLogger.LogFormat("[EventPriorityQueue] HeapifyUp moved item from index {0} to {1}", initialIndex, index);
        }
#endif
        }

        /// <summary>
        /// Restores the heap property by moving an item down the heap until it's in the correct position.
        /// </summary>
        /// <param name="index">The index of the item to move down the heap.</param>
        /// <remarks>
        /// This method is called after removing the root element and moving the last element to the root.
        /// It compares the item with its children and swaps with the smallest child if necessary,
        /// continuing until the heap property is restored.
        /// The method uses bit shifting for parent/child index calculations for better performance.
        /// Time complexity: O(log n) in worst case, where n is the number of elements in the queue.
        /// </remarks>
        private void HeapifyDown(int index)
        {
#if __EVENTTIMELINE_DEBUG_VERBOSE
        AsyncLogger.LogFormat("[EventPriorityQueue] Beginning HeapifyDown from index {0}", index);
        var initialIndex = index;
        var swaps = 0;
#endif

            var item = _heap[index];
            var lastIndex = _heap.Count - 1;

            while (true)
            {
                var leftChild = (index << 1) + 1;
                if (leftChild > lastIndex)
                {
                    break;
                }

                var smallestChild = leftChild;
                var rightChild = leftChild + 1;

                if (rightChild <= lastIndex &&
                    _heap[rightChild].CompareTo(_heap[leftChild]) < 0)
                {
                    smallestChild = rightChild;
                }

                if (item.CompareTo(_heap[smallestChild]) <= 0)
                {
                    break;
                }

                _heap[index] = _heap[smallestChild];
                index = smallestChild;

#if __EVENTTIMELINE_DEBUG_VERBOSE
                swaps++;
#endif
            }

            _heap[index] = item;

#if __EVENTTIMELINE_DEBUG_VERBOSE
        if (swaps > 0)
        {
            AsyncLogger.LogFormat("[EventPriorityQueue] HeapifyDown moved item from index {0} to {1} with {2} swaps", initialIndex, index, swaps);
        }
#endif
        }
    }
}