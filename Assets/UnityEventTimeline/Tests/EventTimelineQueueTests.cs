#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEventTimeline.Internal;
using Object = UnityEngine.Object;

namespace UnityEventTimeline.Tests
{
    [TestFixture]
    public class EventTimelineQueueTests
    {
        private GameObject _gameObject = null!;
        private TestQueueManager _queueManager = null!;

        [SetUp]
        public void Setup()
        {
            if (_gameObject != null) return;
            _gameObject = new GameObject("EventTimelineQueueManager");
            _queueManager = _gameObject.AddComponent<TestQueueManager>();
        }

        [TearDown]
        public void Teardown()
        {
            if (_gameObject != null)
            {
                Object.DestroyImmediate(_gameObject);
            }
        }

        #region Event Scheduling Tests

        [Test]
        public void Schedule_WithZeroDelay_SetsCorrectScheduledTime()
        {
            // Arrange
            var currentTime = Time.time;

            // Act
            var evt = _queueManager.Schedule<TestEvent>();

            // Assert
            Assert.That(evt.ScheduledTime, Is.EqualTo(currentTime));
            Assert.That(_queueManager.GetEventsCount(), Is.EqualTo(1));
        }

        [Test]
        public void Schedule_WithDelay_SetsCorrectScheduledTime()
        {
            // Arrange
            var currentTime = Time.time;
            const float delay = 1.5f;

            // Act
            var evt = _queueManager.Schedule<TestEvent>(delay);

            // Assert
            Assert.That(evt.ScheduledTime, Is.EqualTo(currentTime + delay));
            Assert.That(_queueManager.GetEventsCount(), Is.EqualTo(1));
        }

        [Test]
        public void Schedule_WithConfiguration_ConfiguresEventCorrectly()
        {
            // Arrange
            const string testData = "Test Configuration";

            // Act
            var evt = _queueManager.Schedule<TestEvent>(e => e.TestData = testData);

            // Assert
            Assert.That(evt.TestData, Is.EqualTo(testData));
            Assert.That(_queueManager.GetEventsCount(), Is.EqualTo(1));
        }

        #endregion

        #region Event Processing Tests

        [UnityTest]
        public IEnumerator ProcessEvents_ExecutesEventsInOrder()
        {
            // Arrange
            var executionOrder = new List<int>();
            const int eventCount = 3;

            // Schedule events with different delays
            for (var i = 0; i < eventCount; i++)
            {
                var index = i;
                _queueManager.Schedule<TestEvent>(
                    e => e.WasExecuted += () => executionOrder.Add(index),
                    delay: i * 0.1f);
            }

            // Wait for all events to execute
            yield return new WaitForSeconds(0.5f);

            // Assert
            Assert.That(executionOrder, Is.EqualTo(new[] { 0, 1, 2 }));
            Assert.That(_queueManager.GetEventsCount(), Is.Zero);
        }

        [UnityTest]
        public IEnumerator ProcessEvents_RespectsMaxEventsPerFrame()
        {
            // Arrange
            var executionCount = 0;
            const int totalEvents = 10;
            const int maxEventsPerFrame = 2;

            _queueManager.SetMaxEventsPerFrame(maxEventsPerFrame);

            // Schedule events
            for (var i = 0; i < totalEvents; i++)
            {
                _queueManager.Schedule<TestEvent>(e => e.WasExecuted += () => executionCount++);
            }

            // Wait one frame
            yield return null;

            // Assert
            Assert.That(executionCount, Is.EqualTo(maxEventsPerFrame));
            Assert.That(_queueManager.GetEventsCount(), Is.EqualTo(totalEvents - maxEventsPerFrame));
        }

        [UnityTest]
        public IEnumerator ProcessEvents_RespectsMaxProcessingTime()
        {
            // Arrange
            const float maxProcessingTime = 1f; // 1ms
            const int eventCount = 100;
            _queueManager.SetMaxProcessingTimePerFrame(maxProcessingTime);

            var executedEvents = new List<TestEvent>();

            // Schedule events that take time to process
            for (var i = 0; i < eventCount; i++)
            {
                _queueManager.Schedule<TestEvent>(e =>
                {
                    e.WasExecuted += () =>
                    {
                        System.Threading.Thread.Sleep(1); // Simulate work
                        executedEvents.Add(e);
                    };
                });
            }

            // Wait one frame
            yield return null;

            // Assert
            Assert.That(executedEvents.Count, Is.LessThan(eventCount));
            Assert.That(_queueManager.GetEventsCount(), Is.GreaterThan(0));
        }

        #endregion

        #region Event Cancellation Tests

        [Test]
        public void CancelAndRemove_ExistingEvent_RemovesFromQueue()
        {
            // Arrange
            var evt = _queueManager.Schedule<TestEvent>();

            // Act
            var result = _queueManager.CancelAndRemove(evt);

            // Assert
            Assert.That(result, Is.True);
            Assert.That(evt.IsCancelled, Is.True);
            Assert.That(_queueManager.GetEventsCount(), Is.Zero);
            Assert.That(_queueManager.GetPendingCancellationCount(), Is.EqualTo(1));
        }

        [Test]
        public void CancelAndRemove_NonExistentEvent_ReturnsFalse()
        {
            // Arrange
            var evt = new TestEvent();

            // Act
            var result = _queueManager.CancelAndRemove(evt);

            // Assert
            Assert.That(result, Is.False);
            Assert.That(_queueManager.GetPendingCancellationCount(), Is.Zero);
        }

        [Test]
        public void CancelAndRemoveAll_RemovesAllEventsOfType()
        {
            // Arrange
            const int eventCount = 5;
            for (var i = 0; i < eventCount; i++)
            {
                _queueManager.Schedule<TestEvent>();
                _queueManager.Schedule<OtherTestEvent>();
            }

            // Act
            var removedCount = _queueManager.CancelAndRemoveAll<TestEvent>();

            // Assert
            Assert.That(removedCount, Is.EqualTo(eventCount));
            Assert.That(_queueManager.GetEventsCount(), Is.EqualTo(eventCount)); // Other events remain
            Assert.That(_queueManager.GetPendingCancellationCount(), Is.EqualTo(eventCount));
        }

        #endregion

        #region Event Rescheduling Tests

        [Test]
        public void Reschedule_ExistingEvent_UpdatesScheduledTime()
        {
            // Arrange
            var evt = _queueManager.Schedule<TestEvent>();
            var originalTime = evt.ScheduledTime;
            const float newDelay = 1.0f;

            // Act
            var result = _queueManager.Reschedule(evt, newDelay);

            // Assert
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(evt.ScheduledTime, Is.GreaterThan(originalTime));
            Assert.That(_queueManager.GetEventsCount(), Is.EqualTo(1));
        }

        [Test]
        public void Reschedule_NonExistentEvent_ReturnsFail()
        {
            // Arrange
            var evt = new TestEvent();

            // Act
            var result = _queueManager.Reschedule(evt, 1.0f);

            // Assert
            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.TryGetError(out var error), Is.True);
            Assert.That(error.Code, Is.EqualTo("EVT_NOT_FOUND"));
        }

        #endregion

        #region Cleanup Tests

        [Test]
        public void RemoveCancelledEvents_RemovesAllCancelledEvents()
        {
            // Arrange
            const int eventCount = 5;
            var events = new List<TestEvent>();

            for (var i = 0; i < eventCount; i++)
            {
                events.Add(_queueManager.Schedule<TestEvent>());
            }

            foreach (var evt in events)
            {
                _queueManager.CancelAndRemove(evt);
            }

            // Act
            var cleanedCount = _queueManager.RemoveCancelledEvents();

            // Assert
            Assert.That(cleanedCount, Is.EqualTo(eventCount));
            Assert.That(_queueManager.GetPendingCancellationCount(), Is.Zero);
        }

        [Test]
        public void ForceCleanup_ClearsAllCancelledEvents()
        {
            // Arrange
            const int eventCount = 5;
            var events = new List<TestEvent>();

            for (var i = 0; i < eventCount; i++)
            {
                events.Add(_queueManager.Schedule<TestEvent>());
            }

            foreach (var evt in events)
            {
                _queueManager.CancelAndRemove(evt);
            }

            // Act
            var cleanedCount = _queueManager.ForceCleanup();

            // Assert
            Assert.That(cleanedCount, Is.EqualTo(eventCount));
            Assert.That(_queueManager.GetPendingCancellationCount(), Is.Zero);
        }

        #endregion

        #region Thread Safety Tests

        [Test]
        public async Task ConcurrentOperations_MaintainsQueueIntegrity()
        {
            // Arrange
            const int numThreads = 10;
            const int operationsPerThread = 100;
            var tasks = new List<Task>();
            var exceptions = new List<Exception>();

            // Act
            for (var i = 0; i < numThreads; i++)
            {
                tasks.Add(Task.Run(() =>
                {
                    try
                    {
                        for (var j = 0; j < operationsPerThread; j++)
                        {
                            var evt = _queueManager.Schedule<TestEvent>();
                            if (j % 2 == 0)
                            {
                                _queueManager.CancelAndRemove(evt);
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

            // Assert
            Assert.That(exceptions, Is.Empty, "No exceptions should occur during concurrent operations");
            Assert.That(_queueManager.GetEventsCount() + _queueManager.GetPendingCancellationCount(),
                Is.LessThanOrEqualTo(numThreads * operationsPerThread));
        }

        #endregion

        #region Pause and Time Scale Tests

        [UnityTest]
        public IEnumerator SetPaused_PreventsEventProcessing()
        {
            // Arrange
            var wasExecuted = false;
            _queueManager.Schedule<TestEvent>(e => e.WasExecuted += () => wasExecuted = true);

            // Act
            _queueManager.SetPaused(true);

            // Wait several frames
            yield return new WaitForSeconds(0.1f);

            // Assert
            Assert.That(wasExecuted, Is.False);
            Assert.That(_queueManager.GetEventsCount(), Is.EqualTo(1));

            // Cleanup
            _queueManager.SetPaused(false);
        }

        [UnityTest]
        public IEnumerator SetTimeScale_AffectsEventTiming()
        {
            // Arrange
            const float delay = 0.1f;
            const float timeScale = 0.5f;
            var executionTime = float.MaxValue;

            // Act
            _queueManager.SetTimeScale(timeScale);
            _queueManager.Schedule<TestEvent>(
                e => e.WasExecuted += () => executionTime = Time.time,
                delay);

            // Wait for normal delay - should not execute yet due to time scale
            yield return new WaitForSeconds(delay);
            Assert.That(executionTime, Is.EqualTo(float.MaxValue), "Event executed too early");

            // Wait for the rest of the scaled time
            yield return new WaitForSeconds(delay);
            Assert.That(executionTime, Is.Not.EqualTo(float.MaxValue), "Event did not execute");
            Assert.That(_queueManager.GetEventsCount(), Is.Zero);
        }

        #endregion

        #region OptimizeMemory Tests

        [Test]
        public void OptimizeMemory_ProcessesCancelledEvents()
        {
            // Arrange
            const int eventCount = 5;
            var events = new List<TestEvent>();

            for (var i = 0; i < eventCount; i++)
            {
                events.Add(_queueManager.Schedule<TestEvent>());
            }

            // Cancel all events
            foreach (var evt in events)
            {
                _queueManager.CancelAndRemove(evt);
            }

            // Act
            var cleanedCount = _queueManager.OptimizeMemory();

            // Assert
            Assert.That(cleanedCount, Is.EqualTo(eventCount), "Should clean up all cancelled events");
            Assert.That(_queueManager.GetPendingCancellationCount(), Is.Zero, "Should have no pending cancellations");
            Assert.That(_queueManager.GetEventsCount(), Is.Zero, "Should have no events in queue");
        }

        [UnityTest]
        public IEnumerator OptimizeMemory_ResetsCleanupFrameCounter()
        {
            // Arrange
            const int eventCount = 3;
            var executionCount = 0;

            // Schedule and cancel some events
            for (var i = 0; i < eventCount; i++)
            {
                var evt = _queueManager.Schedule<TestEvent>(e =>
                    e.WasExecuted += () => executionCount++);
                _queueManager.CancelAndRemove(evt);
            }

            // Act
            _queueManager.OptimizeMemory();

            // Schedule new event right after optimization
            _queueManager.Schedule<TestEvent>(e =>
                e.WasExecuted += () => executionCount++);

            // Wait a frame to allow processing
            yield return null;

            // Assert
            Assert.That(executionCount, Is.EqualTo(1),
                "New event should be processed immediately after optimization");
        }

        [Test]
        public void OptimizeMemory_WithNoEvents_ReturnsZero()
        {
            // Act
            var result = _queueManager.OptimizeMemory();

            // Assert
            Assert.That(result, Is.Zero, "Should return 0 when no events to clean up");
            Assert.That(_queueManager.GetEventsCount(), Is.Zero, "Queue should remain empty");
            Assert.That(_queueManager.GetPendingCancellationCount(), Is.Zero,
                "Should have no pending cancellations");
        }

        [Test]
        public void OptimizeMemory_WithMixedEvents_OnlyRemovesCancelled()
        {
            // Arrange
            const int activeEventCount = 3;
            const int cancelledEventCount = 2;

            // Schedule active events
            for (var i = 0; i < activeEventCount; i++)
            {
                _queueManager.Schedule<TestEvent>();
            }

            // Schedule and cancel events
            for (var i = 0; i < cancelledEventCount; i++)
            {
                var evt = _queueManager.Schedule<TestEvent>();
                _queueManager.CancelAndRemove(evt);
            }

            // Act
            var cleanedCount = _queueManager.OptimizeMemory();

            // Assert
            Assert.That(cleanedCount, Is.EqualTo(cancelledEventCount),
                "Should only clean cancelled events");
            Assert.That(_queueManager.GetEventsCount(), Is.EqualTo(activeEventCount),
                "Active events should remain in queue");
            Assert.That(_queueManager.GetPendingCancellationCount(), Is.Zero,
                "Should have no pending cancellations");
        }

        [UnityTest]
        public IEnumerator OptimizeMemory_WithExecutingEvents_PreservesExecution()
        {
            // Arrange
            const float delay = 0.1f;
            var executedEvents = new List<TestEvent>();

            // Schedule some delayed events
            for (var i = 0; i < 3; i++)
            {
                _queueManager.Schedule<TestEvent>(
                    e => e.WasExecuted += () => executedEvents.Add(e), delay);
            }

            // Act
            _queueManager.OptimizeMemory();

            // Wait for events to execute
            yield return new WaitForSeconds(delay);

            // Assert
            Assert.That(executedEvents, Has.Count.EqualTo(3),
                "All events should execute after optimization");
            Assert.That(_queueManager.GetEventsCount(), Is.Zero,
                "Queue should be empty after execution");
        }

        #endregion

        #region Test Helper Classes

        private class TestQueueManager : EventTimelineQueue
        {
            // Expose protected methods for testing if needed
        }

        private class TestEvent : TimelineEvent<TestEvent>
        {
            public string? TestData { get; set; }
            public event Action? WasExecuted;

            public override void Reset()
            {
                TestData = null;
                WasExecuted = delegate { };
            }

            protected override void Execute()
            {
                WasExecuted?.Invoke();
            }
        }

        private class OtherTestEvent : TimelineEvent<OtherTestEvent>
        {
            protected override void Execute()
            {
            }
        }

        #endregion
    }
}