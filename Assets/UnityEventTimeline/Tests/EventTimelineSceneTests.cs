#nullable enable

using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.SceneManagement;

namespace UnityEventTimeline.Tests
{
    [TestFixture]
    public class EventTimelineSceneTests
    {
        private GameObject _gameObject = null!;
        private EventTimeline _timeline = null!;

        [OneTimeSetUp]
        public void Setup()
        {
            // Create the EventTimeline instance
            _timeline = EventTimeline.Instance;
            _gameObject = _timeline.gameObject;
        }

        [OneTimeTearDown]
        public void Teardown()
        {
            if (_gameObject != null)
            {
                Object.DestroyImmediate(_gameObject);
            }
        }

        [UnityTest]
        public IEnumerator HandleSceneUnloaded_CallsOptimizeMemory()
        {
            // Arrange
            const float delay = 0.1f;
            const int eventCount = 5;
            var eventsProcessed = 0;

            // Schedule and cancel some events to create a scenario needing optimization
            for (var i = 0; i < eventCount; i++)
            {
                var evt = _timeline.Schedule<TestSceneEvent>(e =>
                    e.WasExecuted += () => eventsProcessed++, delay);
                evt.Cancel(); // This puts the event in the cancelled list
            }

            // Schedule some active events too
            for (var i = 0; i < eventCount; i++)
            {
                _timeline.Schedule<TestSceneEvent>(e =>
                    e.WasExecuted += () => eventsProcessed++, delay);
            }

            // Get initial counts
            var cleanedUpEvents = _timeline.GetEventsCount();

            // Act - Trigger scene unload
            var newScene = SceneManager.CreateScene("TestScene");
            yield return SceneManager.UnloadSceneAsync(newScene);

            // Wait for active events to execute
            yield return new WaitForSeconds(delay);

            // Assert
            Assert.That(_timeline.GetPendingCancellationCount(), Is.Zero,
                "Pending cancellations should be cleared after scene unload");
            Assert.That(cleanedUpEvents, Is.EqualTo(eventCount),
                "Events should be cleaned up after scene unload");
            Assert.That(_timeline.GetEventsCount(), Is.Zero,
                "Active events should be executed and cleaned up after scene unload");
            Assert.That(eventsProcessed, Is.EqualTo(eventCount),
                "Active events should execute after scene unload");
        }

        [UnityTest]
        public IEnumerator HandleSceneUnloaded_PreservesActiveEvents()
        {
            // Arrange
            const float delay = 0.5f;
            var executedEvents = 0;

            // Schedule some future events
            for (var i = 0; i < 3; i++)
            {
                _timeline.Schedule<TestSceneEvent>(e =>
                    e.WasExecuted += () => executedEvents++, delay);
            }

            // Act - Trigger scene unload
            var newScene = SceneManager.CreateScene("TestScene");
            yield return SceneManager.UnloadSceneAsync(newScene);

            // Wait for events to execute
            yield return new WaitForSeconds(delay + 0.1f);

            // Assert
            Assert.That(executedEvents, Is.EqualTo(3),
                "All active events should execute after scene unload");
        }

        [UnityTest]
        public IEnumerator HandleSceneUnloaded_WithMultipleUnloads_OptimizesCorrectly()
        {
            // Arrange
            const int scenesCount = 3;
            const int eventsPerScene = 3;

            // Create and load multiple scenes
            for (var i = 0; i < scenesCount; i++)
            {
                // Load a new scene
                var newScene = SceneManager.CreateScene("TestScene");

                // Schedule and cancel events
                for (var j = 0; j < eventsPerScene; j++)
                {
                    var evt = _timeline.Schedule<TestSceneEvent>();
                    evt.Cancel();
                }

                // Unload the scene
                yield return SceneManager.UnloadSceneAsync(newScene);

                // Verify optimization occurred
                Assert.That(_timeline.GetPendingCancellationCount(), Is.Zero,
                    $"Pending cancellations should be cleared after scene {i} unload");
            }

            // Wait for a frame for the cleanup to finish
            yield return null;

            // Final verification
            Assert.That(_timeline.GetPendingCancellationCount(), Is.Zero,
                "No pending cancellations should remain after all scene unloads");
            Assert.That(_timeline.GetEventsCount(), Is.Zero,
                "No events should remain after all scene unloads");
        }

        private class TestSceneEvent : TimelineEvent<TestSceneEvent>
        {
            public event System.Action? WasExecuted;

            protected override void Execute()
            {
                WasExecuted?.Invoke();
            }

            public override void Reset()
            {
                base.Reset();
                WasExecuted = null;
            }
        }
    }
}