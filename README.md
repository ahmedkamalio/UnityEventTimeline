# UnityEventTimeline

A robust, thread-safe event scheduling and timeline management system for Unity games and applications.

## Features

- 🔄 Thread-safe event scheduling and execution
- 🏊‍♂️ Efficient object pooling system
- ⚡ Fast priority-based event queue (O(log n) operations)
- 🎮 Zero configuration required - plug and play
- ⏰ Flexible time scaling and pause functionality
- 🧬 Built-in state management system
- 🧹 Automatic memory optimization
- 🔍 Comprehensive debugging support

## Installation

Add the package to your Unity project via the Package Manager:

1. Open the Unity Package Manager
2. Click the '+' button in the top-left corner
3. Select "Add package from git URL"
4. Enter: `https://github.com/ahmedkamalio/UnityEventTimeline.git?path=/Assets/UnityEventTimeline`

## Quick Start

1. No setup required! The system automatically initializes when you first access it.

2. Create your custom event:

```csharp
public class PlayerSpawnEvent : TimelineEvent<PlayerSpawnEvent>
{
    public Vector3 Position { get; set; }
    
    protected override void Execute()
    {
        // Spawn logic here
    }
}
```

3. Schedule events from anywhere:

```csharp
// Schedule immediately
EventTimeline.Instance.Schedule<PlayerSpawnEvent>(evt => {
    evt.Position = new Vector3(0, 1, 0);
});

// Schedule with delay
EventTimeline.Instance.Schedule<PlayerSpawnEvent>(evt => {
    evt.Position = new Vector3(0, 1, 0);
}, delay: 2.0f); // 2 seconds delay
```

## Examples

### Buff System

```csharp
// Schedule a buff that expires after 10 seconds
EventTimeline.Instance.Schedule<BuffExpireEvent>(evt => {
    evt.BuffId = "SpeedBoost";
    evt.OnExpire = () => RemovePlayerBuff(evt.BuffId);
}, delay: 10f);
```

### Quest System

```csharp
// Schedule quest completion check
EventTimeline.Instance.Schedule<QuestCheckEvent>(evt => {
    evt.QuestId = "GatherResources";
    evt.RequiredAmount = 10;
}, delay: 5f);
```

## Advanced Features

### Event Listeners

```csharp
// Add global listener for all PlayerSpawnEvents
PlayerSpawnEvent.AddListener(evt => {
    Debug.Log($"Player spawned at {evt.Position}");
});
```

### State Management

```csharp
// Store game state
var gameState = new GameStateModel { Score = 100 };
EventTimeline.Instance.SetModel(gameState);

// Retrieve state later
if (EventTimeline.Instance.TryGetModel<GameStateModel>(out var model))
{
    Debug.Log($"Current score: {model.Score}");
}
```

### Time Control

```csharp
// Pause all events
EventTimeline.Instance.SetPaused(true);

// Run events at half speed
EventTimeline.Instance.SetTimeScale(0.5f);
```

## Documentation

For complete documentation, see:

- [API Reference](https://github.com/ahmedkamalio/UnityEventTimeline/blob/main/Assets/UnityEventTimeline/Documentation~/UnityEventTimeline.md)

## Performance

- Efficient binary min-heap implementation
- Configurable event processing limits
- Built-in object pooling
- Automatic memory optimization
- Thread-safe operations

## Requirements

- Unity 2021.3 or higher
- .NET Standard 2.1

## License

This package is licensed under
the MIT License. See
the [LICENSE]([MIT License](https://github.com/ahmedkamalio/UnityEventTimeline/blob/main/Assets/UnityEventTimeline/LICENSE))
file for details.
