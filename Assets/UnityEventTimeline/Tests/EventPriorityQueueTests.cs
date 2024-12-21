#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEventTimeline.Internal.EventQueue;

namespace UnityEventTimeline.Tests
{
    /// <summary>
    /// Test fixture for verifying the functionality of the EventPriorityQueue class.
    /// </summary>
    /// <remarks>
    /// Tests cover core functionality, edge cases, thread safety, and performance considerations
    /// of the EventPriorityQueue implementation.
    /// </remarks>
    [TestFixture]
    public class EventPriorityQueueTests
    {
        private EventPriorityQueue<TestEvent> _queue = null!;

        /// <summary>
        /// Set up a new queue instance before each test.
        /// </summary>
        [SetUp]
        public void Setup()
        {
            _queue = new EventPriorityQueue<TestEvent>();
        }

        #region Constructor Tests

        /// <summary>
        /// Verifies that the default constructor creates an empty queue.
        /// </summary>
        [Test]
        public void Constructor_WithDefaultCapacity_CreatesEmptyQueue()
        {
            Assert.That(_queue.Count, Is.Zero, "New queue should be empty");
        }

        /// <summary>
        /// Verifies that the constructor with custom capacity creates an empty queue.
        /// </summary>
        [Test]
        public void Constructor_WithCustomCapacity_CreatesEmptyQueue()
        {
            var customQueue = new EventPriorityQueue<TestEvent>(50);
            Assert.That(customQueue.Count, Is.Zero, "New queue with custom capacity should be empty");
        }

        #endregion

        #region Basic Operation Tests

        /// <summary>
        /// Verifies enqueue operation updates count correctly.
        /// </summary>
        [Test]
        public void Enqueue_SingleItem_UpdatesCount()
        {
            _queue.Enqueue(new TestEvent(1));
            Assert.That(_queue.Count, Is.EqualTo(1), "Queue should contain exactly one item");
        }

        /// <summary>
        /// Verifies that items are dequeued in priority order.
        /// </summary>
        [Test]
        public void Enqueue_MultipleItems_MaintainsPriorityOrder()
        {
            var events = new[]
            {
                new TestEvent(3),
                new TestEvent(1),
                new TestEvent(2)
            };

            foreach (var evt in events)
            {
                _queue.Enqueue(evt);
            }

            var result = _queue.DequeueRange(_queue.Count).ToList();

            Assert.That(result, Has.Count.EqualTo(3), "Should dequeue all items");
            Assert.That(result[0].Priority, Is.EqualTo(1), "First item should have lowest priority");
            Assert.That(result[1].Priority, Is.EqualTo(2), "Second item should have middle priority");
            Assert.That(result[2].Priority, Is.EqualTo(3), "Third item should have highest priority");
        }

        /// <summary>
        /// Verifies that bulk enqueue operations maintain priority order.
        /// </summary>
        [Test]
        public void EnqueueRange_MultipleItems_MaintainsPriorityOrder()
        {
            var events = new[]
            {
                new TestEvent(3),
                new TestEvent(1),
                new TestEvent(2)
            };

            _queue.EnqueueRange(events);
            var result = _queue.DequeueRange(_queue.Count).ToList();


            Assert.That(result, Has.Count.EqualTo(3), "Should contain all enqueued items");
            Assert.That(result[0].Priority, Is.EqualTo(1), "First item should have lowest priority");
            Assert.That(result[1].Priority, Is.EqualTo(2), "Second item should have middle priority");
            Assert.That(result[2].Priority, Is.EqualTo(3), "Third item should have highest priority");
        }

        #endregion

        #region Thread Safety Tests

        /// <summary>
        /// Verifies thread safety of queue operations under concurrent access.
        /// </summary>
        [Test]
        public async Task ThreadSafety_MultithreadedEnqueueDequeue()
        {
            const int numThreads = 4;
            const int itemsPerThread = 1000;
            var tasks = new List<Task>();
            var exceptions = new List<Exception>();
            var dequeuedCount = 0;
            var lockObj = new object();

            // Enqueue tasks
            for (var i = 0; i < numThreads; i++)
            {
                tasks.Add(Task.Run(() =>
                {
                    try
                    {
                        for (var j = 0; j < itemsPerThread; j++)
                        {
                            _queue.Enqueue(new TestEvent(j));
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (exceptions)
                        {
                            exceptions.Add(ex);
                        }
                    }
                }));
            }

            // Wait for all enqueue operations to complete
            await Task.WhenAll(tasks);

            // Verify expected count after enqueue
            Assert.That(_queue.Count, Is.EqualTo(numThreads * itemsPerThread),
                "Queue should contain all enqueued items");

            tasks.Clear();

            // Dequeue tasks
            for (var i = 0; i < numThreads; i++)
            {
                tasks.Add(Task.Run(() =>
                {
                    try
                    {
                        while (true)
                        {
                            var dequeued = _queue.DequeueRange(1).ToList();
                            if (!dequeued.Any())
                            {
                                break; // No more items to dequeue
                            }

                            lock (lockObj)
                            {
                                dequeuedCount += dequeued.Count;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (exceptions)
                        {
                            exceptions.Add(ex);
                        }
                    }
                }));
            }

            await Task.WhenAll(tasks);

            Assert.That(exceptions, Is.Empty, "No exceptions should occur during concurrent operations");
            Assert.That(dequeuedCount, Is.EqualTo(numThreads * itemsPerThread),
                "Should dequeue exactly as many items as were enqueued");
            Assert.That(_queue.Count, Is.Zero, "Queue should be empty after all operations complete");
        }

        #endregion

        #region Edge Cases

        /// <summary>
        /// Verifies that removing non-existent items is handled correctly.
        /// </summary>
        [Test]
        public void TryRemove_NonExistingItem_ReturnsFalse()
        {
            _queue.Enqueue(new TestEvent(1));
            var nonExistingEvent = new TestEvent(1);

            var result = _queue.TryRemove(nonExistingEvent);

            Assert.That(result, Is.False, "Should return false for non-existing item");
            Assert.That(_queue.Count, Is.EqualTo(1), "Queue count should remain unchanged");
        }

        /// <summary>
        /// Tests the removal of the last item in the queue, ensuring it returns true
        /// and verifies that the queue is empty after the item is removed.
        /// </summary>
        [Test]
        public void TryRemove_LastItem_ReturnsTrue()
        {
            var item = new TestEvent(1);
            _queue.Enqueue(item);

            var result = _queue.TryRemove(item);

            Assert.That(result, Is.True, "Should return true for last item");
            Assert.That(_queue.Count, Is.Zero, "Queue count should be zero after removal");
        }

        /// <summary>
        /// Verifies that dequeuing from an empty queue is handled correctly.
        /// </summary>
        [Test]
        public void DequeueRange_EmptyQueue_ReturnsEmptyEnumerable()
        {
            var result = _queue.DequeueRange(1);
            Assert.That(result, Is.Empty, "Should return empty enumerable for empty queue");
        }

        /// <summary>
        /// Verifies that negative dequeue counts are handled correctly.
        /// </summary>
        [Test]
        public void DequeueRange_NegativeCount_ReturnsEmptyEnumerable()
        {
            _queue.Enqueue(new TestEvent(1));
            var result = _queue.DequeueRange(-1);

            Assert.That(result, Is.Empty, "Should return empty enumerable for negative count");
            Assert.That(_queue.Count, Is.EqualTo(1), "Queue count should remain unchanged");
        }

        /// <summary>
        /// Verifies that attempting to peek into an empty queue using the TryPeek method
        /// returns false, indicating that no items are available in the queue.
        /// </summary>
        [Test]
        public void DequeueRange_TryPeekEmptyQueue_ReturnsFalse()
        {
            Assert.That(_queue.TryPeek(out _), Is.False, "Should return false for empty queue");
        }

        #endregion

        /// <summary>
        /// Test event class used for queue testing.
        /// </summary>
        private class TestEvent : IComparable<TestEvent>
        {
            /// <summary>
            /// Gets the priority value determining the event's position in the queue.
            /// </summary>
            public int Priority { get; }

            /// <summary>
            /// Gets the unique identifier for the event.
            /// </summary>
            // ReSharper disable once UnusedAutoPropertyAccessor.Local
            public string Id { get; }

            /// <summary>
            /// Initializes a new instance of the TestEvent class.
            /// </summary>
            /// <param name="priority">The priority value for ordering.</param>
            /// <param name="id">Optional identifier. Defaults to a new GUID.</param>
            public TestEvent(int priority, string? id = null)
            {
                Priority = priority;
                Id = id ?? Guid.NewGuid().ToString();
            }

            /// <summary>
            /// Compares this event with another for priority queue ordering.
            /// </summary>
            /// <param name="other">The event to compare against.</param>
            /// <returns>A value indicating relative ordering of the events.</returns>
            public int CompareTo(TestEvent? other)
            {
                return other is null ? 1 : Priority.CompareTo(other.Priority);
            }
        }
    }
}