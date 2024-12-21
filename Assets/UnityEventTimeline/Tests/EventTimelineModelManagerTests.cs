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
    /// <summary>
    /// Tests for the EventTimelineModelManager class functionality.
    /// </summary>
    [TestFixture]
    public class EventTimelineModelManagerTests
    {
        private GameObject _gameObject = null!;
        private EventTimelineModelManager _timeline = null!;

        [SetUp]
        public void Setup()
        {
            if (_gameObject != null) return;
            _gameObject = new GameObject("EventTimelineModelManager");
            _timeline = _gameObject.AddComponent<EventTimelineModelManager>();
        }

        [TearDown]
        public void Teardown()
        {
            if (_gameObject != null)
            {
                Object.DestroyImmediate(_gameObject);
            }
        }

        #region Model Validation Tests

        [Test]
        public void SetModel_WhenModelIsNull_ReturnsAppropriateError()
        {
            // Act
            var result = _timeline.SetModel<TestGameStateModel>(null!);

            // Assert
            Assert.That(result.IsFailure, "Should fail when model is null");
            Assert.That(result.TryGetError(out var error), Is.True, "Result should contain error");
            Assert.That(error.Code, Is.EqualTo("NULL_MODEL"), "Should return correct error code");
            Assert.That(error.Message, Is.EqualTo("Model must not be null"), "Should return descriptive error message");
        }

        [Test]
        public void SetModel_WhenValidationFails_ReturnsValidationError()
        {
            // Arrange
            var invalidModel = new TestGameStateModel { Score = -1 }; // Invalid score

            // Act
            var result = _timeline.SetModel(invalidModel);

            // Assert
            Assert.That(result.IsFailure, "Should fail validation");
            Assert.That(result.TryGetError(out var error), Is.True, "Result should contain error");
            Assert.That(error.Code, Is.EqualTo("INVALID_MODEL"), "Should return correct error code");
        }

        #endregion

        #region Model Storage and Retrieval Tests

        [Test]
        public void SetModel_WhenModelIsValid_StoresAndRetrievesCorrectly()
        {
            // Arrange
            var model = new TestGameStateModel { Score = 42 };

            // Act
            var setResult = _timeline.SetModel(model);

            // Assert
            Assert.That(setResult.IsSuccess, "Should successfully store valid model");
            Assert.That(_timeline.TryGetModel<TestGameStateModel>(out var retrievedModel), "Should successfully retrieve stored model");
            Assert.That(retrievedModel, Is.Not.Null, "Retrieved model should not be null");
            Assert.That(retrievedModel?.Score, Is.EqualTo(42), "Retrieved model should maintain state");
        }

        [Test]
        public void GetOrCreateModel_WhenModelDoesNotExist_CreatesNewInstance()
        {
            // Act
            var model = _timeline.GetOrCreateModel<TestGameStateModel>();

            // Assert
            Assert.That(model, Is.Not.Null, "Should create new model instance");
            Assert.That(_timeline.TryGetModel<TestGameStateModel>(out var retrievedModel), "Should store created model");
            Assert.That(retrievedModel, Is.SameAs(model), "Retrieved model should be same instance");
        }

        [Test]
        public void GetOrCreateModel_WhenModelExists_ReturnsSameInstance()
        {
            // Arrange
            var initialModel = _timeline.GetOrCreateModel<TestGameStateModel>();
            initialModel.Score = 42;

            // Act
            var retrievedModel = _timeline.GetOrCreateModel<TestGameStateModel>();

            // Assert
            Assert.That(retrievedModel, Is.SameAs(initialModel), "Should return existing instance");
            Assert.That(retrievedModel.Score, Is.EqualTo(42), "Should maintain state");
        }

        #endregion

        #region Model Lifecycle Tests

        [Test]
        public void RemoveModel_WhenModelExists_RemovesSuccessfully()
        {
            // Arrange
            _timeline.SetModel(new TestGameStateModel { Score = 42 });

            // Act
            var removeResult = _timeline.RemoveModel<TestGameStateModel>();
            var modelExists = _timeline.TryGetModel<TestGameStateModel>(out _);

            // Assert
            Assert.That(removeResult, Is.True, "Should successfully remove existing model");
            Assert.That(modelExists, Is.False, "Model should no longer exist after removal");
        }

        [Test]
        public void ClearModels_RemovesAllModelsAndReturnsCount()
        {
            // Arrange
            _timeline.SetModel(new TestGameStateModel());
            _timeline.SetModel(new TestPlayerStateModel { Name = "Test" });

            // Act
            var clearedCount = _timeline.ClearModels();

            // Assert
            Assert.That(clearedCount, Is.EqualTo(2), "Should return correct number of cleared models");
            Assert.That(_timeline.TryGetModel<TestGameStateModel>(out _), Is.False, "GameState model should be removed");
            Assert.That(_timeline.TryGetModel<TestPlayerStateModel>(out _), Is.False, "PlayerState model should be removed");
        }

        #endregion

        #region Thread Safety Tests

        [Test]
        [Timeout(5000)] // Ensure test doesn't hang
        public async Task ConcurrentOperations_MaintainsDataIntegrity()
        {
            // Arrange
            const int numThreads = 10;
            const int operationsPerThread = 100;
            var exceptions = new List<Exception>();
            var tasks = new List<Task>();

            // Act
            for (var i = 0; i < numThreads; i++)
            {
                var threadId = i;
                tasks.Add(Task.Run(() =>
                {
                    try
                    {
                        for (var j = 0; j < operationsPerThread; j++)
                        {
                            var score = threadId * operationsPerThread + j;
                            switch (j % 4)
                            {
                                case 0:
                                    _timeline.SetModel(new TestGameStateModel { Score = score });
                                    break;
                                case 1:
                                    if (_timeline.TryGetModel<TestGameStateModel>(out var model))
                                    {
                                        Assert.That(model.Score >= 0, "Score should never be negative");
                                    }

                                    break;
                                case 2:
                                    var createdModel = _timeline.GetOrCreateModel<TestGameStateModel>();
                                    Assert.That(createdModel, Is.Not.Null, "Created model should not be null");
                                    break;
                                case 3:
                                    _timeline.RemoveModel<TestGameStateModel>();
                                    break;
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

            // Wait for all tasks
            await Task.WhenAll(tasks);

            // Assert
            Assert.That(exceptions, Is.Empty, "No exceptions should occur during concurrent operations");
            Assert.That(_timeline.TryGetModel<TestGameStateModel>(out var finalModel) || finalModel is null, "Final state should be valid");
        }

        #endregion
        
        #region Model Cleanup Tests

        [Test]
        public void CleanupUnusedModels_WhenNoModelsExist_ReturnsZero()
        {
            // Act
            var cleanedCount = _timeline.CleanupUnusedModels(1f);

            // Assert
            Assert.That(cleanedCount, Is.Zero, "Should return 0 when no models exist");
        }

        [Test]
        public void CleanupUnusedModels_WithRecentlyAccessedModels_RetainsModels()
        {
            // Arrange
            _timeline.SetModel(new TestGameStateModel { Score = 42 });
            _timeline.SetModel(new TestPlayerStateModel { Name = "Player1" });

            // Act
            var cleanedCount = _timeline.CleanupUnusedModels(60f); // 60 second threshold

            // Assert
            Assert.That(cleanedCount, Is.Zero, "Should not clean recently accessed models");
            Assert.That(_timeline.TryGetModel<TestGameStateModel>(out _), Is.True, "GameState model should be retained");
            Assert.That(_timeline.TryGetModel<TestPlayerStateModel>(out _), Is.True, "PlayerState model should be retained");
        }

        [UnityTest]
        public IEnumerator CleanupUnusedModels_WithUnusedModels_RemovesOldModels()
        {
            // Arrange
            _timeline.SetModel(new TestGameStateModel { Score = 42 });
            _timeline.SetModel(new TestPlayerStateModel { Name = "Player1" });

            // Wait for models to age
            yield return new WaitForSeconds(0.2f);

            // Create and access a new model to ensure it's not cleaned up
            _timeline.SetModel(new TestPlayerStateModel { Name = "Player2" });

            // Act
            var cleanedCount = _timeline.CleanupUnusedModels(0.1f); // 0.1 second threshold

            // Assert
            Assert.That(cleanedCount, Is.EqualTo(1), "Should clean exactly one unused model");
            Assert.That(_timeline.TryGetModel<TestGameStateModel>(out _), Is.False, "Old GameState model should be removed");
            Assert.That(_timeline.TryGetModel<TestPlayerStateModel>(out var player), Is.True, "Recent PlayerState model should be retained");
            Assert.That(player?.Name, Is.EqualTo("Player2"), "Should retain the most recently accessed model");
        }

        [UnityTest]
        public IEnumerator CleanupUnusedModels_WithAccessedModel_UpdatesLastAccessedTime()
        {
            // Arrange
            _timeline.SetModel(new TestGameStateModel { Score = 42 });

            // Wait some time
            yield return new WaitForSeconds(0.2f);

            // Access the model
            _timeline.TryGetModel<TestGameStateModel>(out _);

            // Act
            var cleanedCount = _timeline.CleanupUnusedModels(0.1f); // 0.1 second threshold

            // Assert
            Assert.That(cleanedCount, Is.Zero, "Should not clean recently accessed model");
            Assert.That(_timeline.TryGetModel<TestGameStateModel>(out _), Is.True, "Model should be retained after access");
        }

        [Test]
        public void CleanupUnusedModels_WithNegativeThreshold_ReturnsZero()
        {
            // Arrange
            _timeline.SetModel(new TestGameStateModel { Score = 42 });

            // Act
            var cleanedCount = _timeline.CleanupUnusedModels(-1f);

            // Assert
            Assert.That(cleanedCount, Is.Zero, "Should not clean any models with negative threshold");
            Assert.That(_timeline.TryGetModel<TestGameStateModel>(out _), Is.True, "Model should be retained");
        }

        #endregion

        #region Component Lifecycle Tests

        [UnityTest]
        public IEnumerator OnDestroy_ClearsAllModelsAndDisposesResources()
        {
            // Arrange
            _timeline.SetModel(new TestGameStateModel { Score = 42 });
            _timeline.SetModel(new TestPlayerStateModel { Name = "Test" });

            // Act
            Object.DestroyImmediate(_gameObject);
            _gameObject = new GameObject();
            var newTimeline = _gameObject.AddComponent<EventTimelineModelManager>();

            // Assert
            Assert.That(newTimeline.TryGetModel<TestGameStateModel>(out _), Is.False, "GameState model should not persist");
            Assert.That(newTimeline.TryGetModel<TestPlayerStateModel>(out _), Is.False, "PlayerState model should not persist");

            yield return null;
        }

        #endregion

        #region Helper Classes

        private class TestGameStateModel : EventTimelineModel
        {
            public int Score { get; set; }

            public override bool Validate()
            {
                return Score >= 0;
            }
        }

        private class TestPlayerStateModel : EventTimelineModel
        {
            public string? Name { get; set; }

            public override bool Validate()
            {
                return !string.IsNullOrEmpty(Name);
            }
        }

        #endregion
    }
}