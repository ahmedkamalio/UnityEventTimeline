#nullable enable

using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace UnityEventTimeline.Tests
{
    [TestFixture]
    public class EventTimelineSingletonTests
    {
        private GameObject _firstGameObject;
        private EventTimeline _firstInstance;

        [OneTimeSetUp]
        public void Setup()
        {
            // Create first instance
            _firstInstance = EventTimeline.Instance;
            _firstGameObject = _firstInstance.gameObject;
        }

        [OneTimeTearDown]
        public void Teardown()
        {
            if (_firstGameObject != null)
            {
                Object.DestroyImmediate(_firstGameObject);
            }
        }

        [UnityTest]
        public IEnumerator SecondInstance_ShouldBeDestroyed()
        {
            // Arrange
            var secondGameObject = new GameObject("EventTimeline_Second");
            secondGameObject.AddComponent<EventTimeline>();

            // The Awake method runs automatically when AddComponent is called
            // At this point, the second instance should have detected the existing instance
            // and destroyed itself

            // Wait for a frame to ensure destruction has completed
            yield return null;

            // Assert
            Assert.That(secondGameObject == null || !secondGameObject.activeInHierarchy,
                "Second GameObject should be destroyed");
            Assert.That(_firstGameObject.activeInHierarchy,
                "First GameObject should remain active");
            Assert.That(EventTimeline.Instance, Is.SameAs(_firstInstance),
                "EventTimeline.Instance should reference the first instance");
        }

        [UnityTest]
        public IEnumerator SecondInstance_ShouldBeDestroyed_WaitForDestroy()
        {
            // Arrange
            var secondGameObject = new GameObject("EventTimeline_Second");
            secondGameObject.AddComponent<EventTimeline>();

            // Wait for a frame to ensure destruction has completed
            yield return null;

            // Assert
            Assert.That(secondGameObject == null || !secondGameObject.activeInHierarchy,
                "Second GameObject should be destroyed");
            Assert.That(_firstGameObject.activeInHierarchy,
                "First GameObject should remain active");
            Assert.That(EventTimeline.Instance, Is.SameAs(_firstInstance),
                "EventTimeline.Instance should reference the first instance");
        }

        [UnityTest]
        public IEnumerator MultipleInstances_OnlyFirstSurvives()
        {
            // Arrange & Act
            var gameObjects = new GameObject[5];

            // Create multiple instances
            for (var i = 0; i < 5; i++)
            {
                gameObjects[i] = new GameObject($"EventTimeline_{i}");
                gameObjects[i].AddComponent<EventTimeline>();
            }

            // Wait for a frame to ensure destruction has completed
            yield return null;

            // Assert
            Assert.That(_firstGameObject.activeInHierarchy,
                "First GameObject should remain active");
            Assert.That(EventTimeline.Instance, Is.SameAs(_firstInstance),
                "EventTimeline.Instance should reference the first instance");

            // Check that all subsequent instances were destroyed
            for (var i = 0; i < 5; i++)
            {
                Assert.That(gameObjects[i] == null || !gameObjects[i].activeInHierarchy,
                    $"GameObject {i} should be destroyed");
            }
        }

        [Test]
        public void FirstInstance_ShouldBeDontDestroyOnLoad()
        {
            // Assert
            Assert.That(_firstGameObject.scene.buildIndex, Is.EqualTo(-1),
                "First instance should be marked DontDestroyOnLoad");
            Assert.That(_firstGameObject.hideFlags, Is.Not.EqualTo(HideFlags.DontSave),
                "First instance should not have DontSave flag");
        }

        [UnityTest]
        public IEnumerator DestroyingInstance_ShouldNullifyStaticReference()
        {
            // Arrange - Get reference to current instance and GameObject
            var currentInstance = EventTimeline.Instance;
            var currentGameObject = currentInstance.gameObject;

            // Act - Destroy the instance
            Object.DestroyImmediate(currentGameObject);

            // Wait a frame to ensure destruction is complete
            yield return null;

            // Get new instance to verify old reference was cleared
            var newInstance = EventTimeline.Instance;
            var newGameObject = newInstance.gameObject;

            try
            {
                // Assert
                Assert.That(currentGameObject == null || !currentGameObject.activeInHierarchy,
                    "Original GameObject should be destroyed");
                Assert.That(newInstance, Is.Not.SameAs(currentInstance),
                    "New instance should be different from destroyed instance");
                Assert.That(newGameObject.activeInHierarchy,
                    "New GameObject should be active");
                Assert.That(newGameObject.scene.buildIndex, Is.EqualTo(-1),
                    "New instance should be marked DontDestroyOnLoad");
            }
            finally
            {
                // Cleanup
                // Store reference to cleanup after test
                _firstGameObject = newGameObject;
                _firstInstance = newInstance;
            }
        }
    }

    [TestFixture]
    public class EventTimelineSingletonConcurrentAccessTests
    {
        private GameObject _firstGameObject;
        private EventTimeline _firstInstance;

        [OneTimeSetUp]
        public void Setup()
        {
            // Create first instance
            _firstInstance = EventTimeline.Instance;
            _firstGameObject = _firstInstance.gameObject;
        }

        [OneTimeTearDown]
        public void Teardown()
        {
            if (_firstGameObject != null)
            {
                Object.DestroyImmediate(_firstGameObject);
            }
        }

        [UnityTest]
        public IEnumerator Instance_ConcurrentAccess_ReturnsSameInstance()
        {
            // Arrange
            const int numTasks = 100;
            var tasks = new Task<EventTimeline>[numTasks];
            var instances = new HashSet<EventTimeline>();
            var instanceLock = new object();

            // Act - Create multiple tasks that try to access Instance simultaneously
            for (var i = 0; i < numTasks; i++)
            {
                tasks[i] = Task.Run(() =>
                {
                    // Get instance
                    var instance = EventTimeline.Instance;

                    // Thread-safe add to our tracking collection
                    lock (instanceLock)
                    {
                        instances.Add(instance);
                    }

                    return instance;
                });
            }

            // Wait for all tasks to complete
            Task.WaitAll(new List<Task>(tasks).ToArray());

            // Get the final instance for our test fixture
            _firstInstance = EventTimeline.Instance;
            _firstGameObject = _firstInstance.gameObject;

            // Assert
            Assert.That(instances, Has.Count.EqualTo(1),
                "All tasks should receive the same instance");
            Assert.That(tasks.Select(t => t.Result),
                Is.All.SameAs(EventTimeline.Instance),
                "All tasks should return the same instance reference");
            Assert.That(_firstGameObject.activeInHierarchy,
                "Instance GameObject should be active");
            Assert.That(_firstGameObject.scene.buildIndex, Is.EqualTo(-1),
                "Instance should be marked DontDestroyOnLoad");

            yield return null;
        }
    }
}