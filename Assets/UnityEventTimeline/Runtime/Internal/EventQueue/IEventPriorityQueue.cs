#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace UnityEventTimeline.Internal.EventQueue
{
    /// <summary>
    /// Defines a thread-safe priority queue that maintains items in ascending order based on their natural ordering.
    /// </summary>
    /// <typeparam name="T">The type of elements in the priority queue. Must be a reference type that implements IComparable&lt;T&gt;.</typeparam>
    /// <remarks>
    /// <para>
    /// This interface defines operations for a priority queue implementation that ensures thread-safe
    /// access and maintains items in order based on their natural comparison. All operations are
    /// expected to be thread-safe and suitable for concurrent access.
    /// </para>
    /// <para>
    /// Implementations should ensure O(log n) complexity for enqueue and dequeue operations
    /// through appropriate data structures (e.g., binary heap).
    /// </para>
    /// </remarks>
    public interface IEventPriorityQueue<T> where T : class, IComparable<T>
    {
        /// <summary>
        /// Gets the number of elements currently in the priority queue.
        /// </summary>
        /// <remarks>
        /// This property must be thread-safe and provide a snapshot of the queue size
        /// at the time of access.
        /// </remarks>
        int Count { get; }

        /// <summary>
        /// Attempts to retrieve the item at the front of the queue without removing it.
        /// </summary>
        /// <param name="item">When this method returns, contains the first item in the queue if available; otherwise, null.</param>
        /// <returns>true if an item was successfully retrieved; otherwise, false.</returns>
        /// <remarks>
        /// This operation must be thread-safe and should not modify the queue state.
        /// </remarks>
        bool TryPeek([NotNullWhen(true)] out T? item);

        /// <summary>
        /// Adds an item to the priority queue.
        /// </summary>
        /// <param name="item">The item to add to the queue.</param>
        /// <exception cref="ArgumentNullException">Thrown when item is null.</exception>
        /// <remarks>
        /// This operation must be thread-safe and maintain the heap property of the queue.
        /// </remarks>
        void Enqueue(T item);

        /// <summary>
        /// Adds multiple items to the priority queue in a single operation.
        /// </summary>
        /// <param name="items">The collection of items to add to the queue.</param>
        /// <exception cref="ArgumentNullException">Thrown when items is null.</exception>
        /// <remarks>
        /// This operation must be thread-safe and optimize the heapification process for bulk insertions.
        /// </remarks>
        void EnqueueRange(IEnumerable<T> items);

        /// <summary>
        /// Removes and returns multiple items from the front of the queue.
        /// </summary>
        /// <param name="count">The maximum number of items to dequeue.</param>
        /// <returns>An enumerable collection containing the dequeued items in priority order.</returns>
        /// <remarks>
        /// This operation must be thread-safe and maintain the heap property throughout the batch removal.
        /// If count is less than or equal to 0, returns an empty collection.
        /// </remarks>
        IEnumerable<T> DequeueRange(int count);

        /// <summary>
        /// Removes all items from the priority queue.
        /// </summary>
        /// <remarks>
        /// This operation must be thread-safe and reset the queue to its empty state.
        /// </remarks>
        void Clear();

        /// <summary>
        /// Reduces the internal capacity of the queue to match its current size.
        /// </summary>
        /// <remarks>
        /// This operation must be thread-safe and can be used to optimize memory usage
        /// when the queue size has decreased significantly.
        /// </remarks>
        void TrimExcess();

        /// <summary>
        /// Determines whether the specified item exists in the priority queue.
        /// </summary>
        /// <param name="item">The item to locate in the queue.</param>
        /// <returns>true if the item is found in the queue; otherwise, false.</returns>
        /// <remarks>
        /// This operation must be thread-safe and should not consider priority ordering when searching.
        /// </remarks>
        bool Contains(T item);

        /// <summary>
        /// Removes a specific item from the queue if it exists.
        /// </summary>
        /// <param name="item">The item to remove from the queue.</param>
        /// <returns>true if the item was found and removed; otherwise, false.</returns>
        /// <remarks>
        /// This operation must be thread-safe and maintain the heap property after removal.
        /// </remarks>
        bool TryRemove(T item);
    }
}