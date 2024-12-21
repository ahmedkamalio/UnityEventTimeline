#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEventTimeline.Internal;
using Object = UnityEngine.Object;

namespace UnityEventTimeline.Tests
{
    [TestFixture]
    public class EventTimelinePoolsManagerTests
    {
        private GameObject _gameObject = null!;
        private TestPoolsManager _poolManager = null!;

        [SetUp]
        public void Setup()
        {
            if (_gameObject != null) return;
            _gameObject = new GameObject("EventTimelineModelManager");
            _poolManager = _gameObject.AddComponent<TestPoolsManager>();
        }

        [TearDown]
        public void Teardown()
        {
            if (_gameObject != null)
            {
                Object.DestroyImmediate(_gameObject);
            }
        }

        #region Pool Size Configuration Tests

        [Test]
        public void SetMaxPoolSize_WithValidSize_UpdatesMaxSize()
        {
            // Act
            _poolManager.SetMaxPoolSize<TestEvent>(50);

            // Assert
            Assert.That(_poolManager.GetMaxPoolSize<TestEvent>(), Is.EqualTo(50), "Pool size should be updated");
        }

        [Test]
        public void SetMaxPoolSize_WithZeroSize_ResetsToDefault()
        {
            // Arrange
            _poolManager.SetMaxPoolSize<TestEvent>(50);

            // Act
            _poolManager.SetMaxPoolSize<TestEvent>(0);

            // Assert
            Assert.That(_poolManager.GetMaxPoolSize<TestEvent>(), Is.EqualTo(100), "Pool size should reset to default");
        }

        [Test]
        public void SetMaxPoolSize_WithNegativeSize_ResetsToDefault()
        {
            // Arrange
            _poolManager.SetMaxPoolSize<TestEvent>(50);

            // Act
            _poolManager.SetMaxPoolSize<TestEvent>(-1);

            // Assert
            Assert.That(_poolManager.GetMaxPoolSize<TestEvent>(), Is.EqualTo(100), "Pool size should reset to default");
        }

        #endregion

        #region Pool Statistics Tests

        [Test]
        public void GetPoolStats_ForNewPool_ReturnsCorrectStats()
        {
            // Act
            var (currentSize, maxSize) = _poolManager.GetPoolStats<TestEvent>();

            // Assert
            Assert.That(currentSize, Is.Zero, "Current size should be 0 for new pool");
            Assert.That(maxSize, Is.EqualTo(100), "Max size should be default value");
        }

        [Test]
        public void GetPoolStats_AfterSettingCustomMaxSize_ReturnsUpdatedStats()
        {
            // Arrange
            _poolManager.SetMaxPoolSize<TestEvent>(50);

            // Act
            var (currentSize, maxSize) = _poolManager.GetPoolStats<TestEvent>();

            // Assert
            Assert.That(currentSize, Is.Zero, "Current size should still be 0");
            Assert.That(maxSize, Is.EqualTo(50), "Max size should be updated value");
        }

        #endregion

        #region Pool Operations Tests

        [Test]
        public void GetFromPool_WhenPoolEmpty_CreatesNewInstance()
        {
            // Act
            var evt = _poolManager.GetEventFromPool<TestEvent>();

            // Assert
            Assert.That(evt, Is.Not.Null, "Should create new instance");
            Assert.That(evt.WasDisposed, Is.False, "New instance should not be disposed");
        }

        [Test]
        public void ReturnToPool_WhenUnderMaxSize_StoresInstance()
        {
            // Arrange
            var evt = _poolManager.GetEventFromPool<TestEvent>();

            // Act
            _poolManager.ReturnEventToPool(evt);
            var stats = _poolManager.GetPoolStats<TestEvent>();

            // Assert
            Assert.That(stats.currentSize, Is.EqualTo(1), "Pool should contain returned instance");
            Assert.That(evt.WasDisposed, Is.False, "Instance should not be disposed");
        }

        [Test]
        public void ReturnToPool_WhenAtMaxSize_DisposesInstance()
        {
            // Arrange
            _poolManager.SetMaxPoolSize<TestEvent>(1);
            var evt1 = _poolManager.GetEventFromPool<TestEvent>();
            var evt2 = _poolManager.GetEventFromPool<TestEvent>();
            _poolManager.ReturnEventToPool(evt1); // Fill pool to max

            // Act
            _poolManager.ReturnEventToPool(evt2); // Should dispose

            // Assert
            Assert.That(evt2.WasDisposed, Is.True, "Instance should be disposed");
            var stats = _poolManager.GetPoolStats<TestEvent>();
            Assert.That(stats.currentSize, Is.EqualTo(1), "Pool size should remain at max");
        }

        [Test]
        public void GetFromPool_ReusesReturnedInstance()
        {
            // Arrange
            var originalEvent = _poolManager.GetEventFromPool<TestEvent>();
            _poolManager.ReturnEventToPool(originalEvent);

            // Act
            var retrievedEvent = _poolManager.GetEventFromPool<TestEvent>();

            // Assert
            Assert.That(retrievedEvent, Is.SameAs(originalEvent), "Should reuse returned instance");
            var stats = _poolManager.GetPoolStats<TestEvent>();
            Assert.That(stats.currentSize, Is.Zero, "Pool should be empty after retrieval");
        }

        #endregion

        #region Pool Cleanup Tests

        [Test]
        public void ClearEventPools_DisposesAllInstances()
        {
            // Arrange
            var events = new List<TestEvent>();
            for (var i = 0; i < 5; i++)
            {
                events.Add(_poolManager.GetEventFromPool<TestEvent>());
            }

            events.ForEach(e => _poolManager.ReturnEventToPool(e));

            // Act
            _poolManager.ClearPools();

            // Assert
            Assert.That(events.All(e => e.WasDisposed), Is.True, "All instances should be disposed");
            var stats = _poolManager.GetPoolStats<TestEvent>();
            Assert.That(stats.currentSize, Is.Zero, "Pool should be empty");
        }

        [Test]
        public void TrimEventPools_ReducesToMaxSize()
        {
            _poolManager.SetMaxPoolSize<TestEvent>(2);

            // Arrange
            var events = new List<TestEvent>();
            for (var i = 0; i < 5; i++)
            {
                events.Add(_poolManager.GetEventFromPool<TestEvent>());
            }

            events.ForEach(evt => _poolManager.ReturnEventToPool(evt));

            // Act
            _poolManager.TrimPools();

            // Assert
            var stats = _poolManager.GetPoolStats<TestEvent>();
            Assert.That(stats.currentSize, Is.EqualTo(2), "Pool should be trimmed to max size");
            Assert.That(events.Count(e => e.WasDisposed), Is.EqualTo(3), "Excess instances should be disposed");
        }

        #endregion

        #region Concurrency Tests

        [Test]
        public async Task ConcurrentOperations_MaintainsThreadSafety()
        {
            // Arrange
            const int numThreads = 10;
            const int operationsPerThread = 1000;
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
                            var evt = _poolManager.GetEventFromPool<TestEvent>();
                            _poolManager.ReturnEventToPool(evt);
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
            var stats = _poolManager.GetPoolStats<TestEvent>();
            Assert.That(stats.currentSize, Is.LessThanOrEqualTo(100), "Pool size should not exceed maximum");
        }

        [Test]
        public async Task ConcurrentSizeChanges_HandlesRaceConditions()
        {
            // Arrange
            const int numThreads = 5;
            var tasks = new List<Task>();
            var exceptions = new List<Exception>();

            // Act - Concurrently change pool size and perform operations
            for (var i = 0; i < numThreads; i++)
            {
                var size = i * 20; // Different sizes
                tasks.Add(Task.Run(() =>
                {
                    try
                    {
                        _poolManager.SetMaxPoolSize<TestEvent>(size);
                        for (var j = 0; j < 100; j++)
                        {
                            var evt = _poolManager.GetEventFromPool<TestEvent>();
                            _poolManager.ReturnEventToPool(evt);
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
            Assert.That(exceptions, Is.Empty, "No exceptions should occur during concurrent size changes");
            var stats = _poolManager.GetPoolStats<TestEvent>();
            Assert.That(stats.currentSize, Is.LessThanOrEqualTo(stats.maxSize),
                "Pool size should not exceed current max size");
        }

        #endregion

        #region Test Helpers

        /// <summary>
        /// Test implementation of EventTimelinePoolsManager that exposes protected members for testing.
        /// </summary>
        private class TestPoolsManager : EventTimelinePoolsManager
        {
            public T GetEventFromPool<T>() where T : TimelineEvent, new()
            {
                return GetFromPool<T>();
            }

            public void ReturnEventToPool(TimelineEvent evt)
            {
                ReturnToPool(evt);
            }

            public void TrimPools()
            {
                TrimEventPools();
            }

            public void ClearPools()
            {
                ClearEventPools();
            }
        }

        /// <summary>
        /// Test event implementation that tracks disposal.
        /// </summary>
        private class TestEvent : TimelineEvent
        {
            public bool WasDisposed { get; private set; }

            protected override void Execute()
            {
            }

            protected override void OnDisposing()
            {
                base.OnDisposing();
                WasDisposed = true;
            }
        }

        #endregion
    }
}