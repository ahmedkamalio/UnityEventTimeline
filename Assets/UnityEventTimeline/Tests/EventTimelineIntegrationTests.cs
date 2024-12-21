#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace UnityEventTimeline.Tests
{
    [TestFixture]
    public class EventTimelineIntegrationTests
    {
        [TearDown]
        public void Teardown()
        {
            // Clear all events after each test
            EventTimeline.Instance.Clear();
        }


        [UnityTest]
        public IEnumerator PlayerRespawnScenario()
        {
            // Arrange
            const float respawnDelay = 0.5f;
            var playerState = new PlayerState
            {
                Health = 0,
                Position = Vector3.zero,
                IsAlive = false
            };

            // Act - Schedule a respawn event
            EventTimeline.Instance.Schedule<RespawnEvent>(evt =>
            {
                evt.PlayerState = playerState;
                evt.RespawnPosition = new Vector3(0, 1, 0);
            }, respawnDelay);

            // Wait for respawn delay
            yield return new WaitForSeconds(respawnDelay + 0.1f);

            // Assert
            Assert.That(playerState.Health, Is.EqualTo(100), "Player health should be restored");
            Assert.That(playerState.Position, Is.EqualTo(new Vector3(0, 1, 0)), "Player should be at respawn position");
            Assert.That(playerState.IsAlive, Is.True, "Player should be alive");
        }

        [UnityTest]
        public IEnumerator BuffSystemScenario()
        {
            // Arrange
            const float buffDuration = 0.3f;
            var playerStats = new PlayerStats { Strength = 10 };
            var buffApplied = false;
            var buffRemoved = false;

            // Act - Schedule buff application and removal
            EventTimeline.Instance.Schedule<ApplyBuffEvent>(evt =>
            {
                evt.Stats = playerStats;
                evt.BuffAmount = 5;
                evt.OnBuffApplied = () => buffApplied = true;
            });

            EventTimeline.Instance.Schedule<RemoveBuffEvent>(evt =>
            {
                evt.Stats = playerStats;
                evt.BuffAmount = 5;
                evt.OnBuffRemoved = () => buffRemoved = true;
            }, buffDuration);

            // Wait a frame for buff application
            yield return null;
            Assert.That(buffApplied, Is.True, "Buff should be applied");
            Assert.That(playerStats.Strength, Is.EqualTo(15), "Strength should be increased");

            // Wait for buff duration
            yield return new WaitForSeconds(buffDuration + 0.1f);
            Assert.That(buffRemoved, Is.True, "Buff should be removed");
            Assert.That(playerStats.Strength, Is.EqualTo(10), "Strength should be back to normal");
        }

        [UnityTest]
        public IEnumerator DamageOverTimeScenario()
        {
            // Arrange
            const int tickCount = 3;
            const float tickInterval = 0.1f;
            var damageDealt = 0;
            var ticksProcessed = 0;

            // Act - Schedule periodic damage ticks
            for (var i = 0; i < tickCount; i++)
            {
                EventTimeline.Instance.Schedule<DamageTickEvent>(evt =>
                {
                    evt.DamageAmount = 10;
                    evt.OnDamageDealt = damage =>
                    {
                        damageDealt += damage;
                        ticksProcessed++;
                    };
                }, tickInterval * i);
            }

            // Wait for all ticks to complete
            yield return new WaitForSeconds(tickInterval * tickCount + 0.1f);

            // Assert
            Assert.That(ticksProcessed, Is.EqualTo(tickCount), "All damage ticks should be processed");
            Assert.That(damageDealt, Is.EqualTo(30), "Total damage should be correct");
        }

        [UnityTest]
        public IEnumerator QuestChainScenario()
        {
            // Arrange
            var questLog = new QuestLog();
            var questSteps = new List<string>();

            // Act - Schedule a sequence of quest events
            EventTimeline.Instance.Schedule<QuestEvent>(evt =>
            {
                evt.QuestLog = questLog;
                evt.StepDescription = "Find the ancient scroll";
                evt.OnStepCompleted = desc => questSteps.Add(desc);
            });

            EventTimeline.Instance.Schedule<QuestEvent>(evt =>
            {
                evt.QuestLog = questLog;
                evt.StepDescription = "Decipher the scroll";
                evt.OnStepCompleted = desc => questSteps.Add(desc);
            }, 0.2f);

            EventTimeline.Instance.Schedule<QuestEvent>(evt =>
            {
                evt.QuestLog = questLog;
                evt.StepDescription = "Return to the wizard";
                evt.OnStepCompleted = desc => questSteps.Add(desc);
            }, 0.4f);

            // Wait for all quest steps
            yield return new WaitForSeconds(0.5f);

            // Assert
            Assert.That(questSteps, Has.Count.EqualTo(3), "All quest steps should be completed");
            Assert.That(questSteps[0], Is.EqualTo("Find the ancient scroll"));
            Assert.That(questSteps[1], Is.EqualTo("Decipher the scroll"));
            Assert.That(questSteps[2], Is.EqualTo("Return to the wizard"));
        }

        [UnityTest]
        public IEnumerator ResourceGatheringScenario()
        {
            // Arrange
            var inventory = new Inventory();
            var resourcesGathered = new List<string>();

            // Schedule resource gathering events with different timings
            EventTimeline.Instance.Schedule<GatherResourceEvent>(evt =>
            {
                evt.Inventory = inventory;
                evt.ResourceType = "Wood";
                evt.Amount = 5;
                evt.OnResourceGathered = (type, amount) =>
                    resourcesGathered.Add($"{amount} {type}");
            }, 0.1f);

            EventTimeline.Instance.Schedule<GatherResourceEvent>(evt =>
            {
                evt.Inventory = inventory;
                evt.ResourceType = "Stone";
                evt.Amount = 3;
                evt.OnResourceGathered = (type, amount) =>
                    resourcesGathered.Add($"{amount} {type}");
            }, 0.2f);

            // Wait for gathering to complete
            yield return new WaitForSeconds(0.3f);

            // Assert
            Assert.That(inventory.GetResourceCount("Wood"), Is.EqualTo(5));
            Assert.That(inventory.GetResourceCount("Stone"), Is.EqualTo(3));
            Assert.That(resourcesGathered, Has.Count.EqualTo(2));
        }

        #region Test Helper Classes

        private class PlayerState
        {
            public int Health { get; set; }
            public Vector3 Position { get; set; }
            public bool IsAlive { get; set; }
        }

        private class PlayerStats
        {
            public int Strength { get; set; }
        }

        private class QuestLog
        {
            public List<string> CompletedSteps { get; } = new();
        }

        private class Inventory
        {
            private readonly Dictionary<string, int> _resources = new();

            public void AddResource(string type, int amount)
            {
                if (!_resources.ContainsKey(type))
                {
                    _resources[type] = 0;
                }

                _resources[type] += amount;
            }

            public int GetResourceCount(string type)
            {
                return _resources.GetValueOrDefault(type);
            }
        }

        private class RespawnEvent : TimelineEvent<RespawnEvent>
        {
            public PlayerState PlayerState { get; set; } = null!;
            public Vector3 RespawnPosition { get; set; }

            protected override void Execute()
            {
                PlayerState.Health = 100;
                PlayerState.Position = RespawnPosition;
                PlayerState.IsAlive = true;
            }
        }

        private class ApplyBuffEvent : TimelineEvent<ApplyBuffEvent>
        {
            public PlayerStats Stats { get; set; } = null!;
            public int BuffAmount { get; set; }
            public Action OnBuffApplied { get; set; } = null!;

            protected override void Execute()
            {
                Stats.Strength += BuffAmount;
                OnBuffApplied?.Invoke();
            }
        }

        private class RemoveBuffEvent : TimelineEvent<RemoveBuffEvent>
        {
            public PlayerStats Stats { get; set; } = null!;
            public int BuffAmount { get; set; }
            public Action OnBuffRemoved { get; set; } = null!;

            protected override void Execute()
            {
                Stats.Strength -= BuffAmount;
                OnBuffRemoved?.Invoke();
            }
        }

        private class DamageTickEvent : TimelineEvent<DamageTickEvent>
        {
            public int DamageAmount { get; set; }
            public Action<int> OnDamageDealt { get; set; } = null!;

            protected override void Execute()
            {
                OnDamageDealt?.Invoke(DamageAmount);
            }
        }

        private class QuestEvent : TimelineEvent<QuestEvent>
        {
            public QuestLog QuestLog { get; set; } = null!;
            public string StepDescription { get; set; } = null!;
            public Action<string> OnStepCompleted { get; set; } = null!;

            protected override void Execute()
            {
                QuestLog.CompletedSteps.Add(StepDescription);
                OnStepCompleted?.Invoke(StepDescription);
            }
        }

        private class GatherResourceEvent : TimelineEvent<GatherResourceEvent>
        {
            public Inventory Inventory { get; set; } = null!;
            public string ResourceType { get; set; } = null!;
            public int Amount { get; set; }
            public Action<string, int> OnResourceGathered { get; set; } = null!;

            protected override void Execute()
            {
                Inventory.AddResource(ResourceType, Amount);
                OnResourceGathered?.Invoke(ResourceType, Amount);
            }
        }

        #endregion
    }
}