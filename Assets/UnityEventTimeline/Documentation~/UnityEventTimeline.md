# UnityEventTimeline

A thread-safe event scheduling and timeline management system for Unity that provides efficient handling of time-based
events with object pooling and state management capabilities.

## Features

- Thread-safe event scheduling and execution
- Efficient object pooling for event instances
- Priority-based event queue with O(log n) operations
- Support for event cancellation and rescheduling
- Automatic cleanup and memory optimization
- Model-based state management
- Configurable time scaling and pause functionality
- Comprehensive event lifecycle management
- Scene transition handling

## Installation

Add the package to your Unity project via the Package Manager:

1. Open the Unity Package Manager
2. Click the '+' button in the top-left corner
3. Select "Add package from git URL"
4. Enter: `https://github.com/ahmedkamalio/UnityEventTimeline.git?path=/Assets/UnityEventTimeline`

## Setup

The UnityEventTimeline system is designed to be plug-and-play with zero configuration required. When you first access
`EventTimeline.Instance`, the system automatically creates a singleton GameObject in your scene that manages all
timeline functionality. This GameObject is marked as `DontDestroyOnLoad`, ensuring the event system persists across
scene transitions.

```csharp
// The system initializes automatically on first access
var timeline = EventTimeline.Instance;
```

You don't need to manually add any prefabs, components, or make any configuration changes to get started. Simply
reference `EventTimeline.Instance` in your code, and the system takes care of all the initialization for you.

## Basic Usage

### Creating an Event

Create a custom event by inheriting from `TimelineEvent<T>`:

```csharp
public class PlayerSpawnEvent : TimelineEvent<PlayerSpawnEvent>
{
    public Vector3 SpawnPosition { get; set; }
    public int PlayerHealth { get; set; }

    protected override void Execute()
    {
        // Spawn logic here
    }
}
```

### Scheduling Events

```csharp
// Schedule an event immediately
EventTimeline.Instance.Schedule<PlayerSpawnEvent>(evt => {
    evt.SpawnPosition = new Vector3(0, 1, 0);
    evt.PlayerHealth = 100;
});

// Schedule an event with delay
float delay = 2.0f; // 2 seconds
EventTimeline.Instance.Schedule<PlayerSpawnEvent>(evt => {
    evt.SpawnPosition = new Vector3(0, 1, 0);
    evt.PlayerHealth = 100;
}, delay);
```

### Event Listeners

```csharp
// Add a listener
PlayerSpawnEvent.AddListener(evt => {
    Debug.Log($"Player spawned at {evt.SpawnPosition}");
});

// Remove a listener
PlayerSpawnEvent.RemoveListener(myListener);

// Clear all listeners
PlayerSpawnEvent.ClearListeners();
```

### Managing Event Timeline

```csharp
// Pause/resume event processing
EventTimeline.Instance.SetPaused(true);
EventTimeline.Instance.SetPaused(false);

// Adjust time scale
EventTimeline.Instance.SetTimeScale(0.5f); // Half speed
EventTimeline.Instance.SetTimeScale(2.0f); // Double speed

// Clear all events
EventTimeline.Instance.Clear();
```

## Advanced Features

### State Management

The EventTimeline includes a model system for managing game state:

```csharp
public class GameStateModel : EventTimelineModel
{
    public int Score { get; set; }
    public float TimeRemaining { get; set; }
}

// Store state
EventTimeline.Instance.SetModel(new GameStateModel
{
    Score = 0,
    TimeRemaining = 60
});

// Retrieve state
if (EventTimeline.Instance.TryGetModel<GameStateModel>(out var model))
{
    Debug.Log($"Current score: {model.Score}");
}

// Get or create state
var playerState = EventTimeline.Instance.GetOrCreateModel<PlayerStateModel>();
```

### Object Pooling

The system automatically pools event instances for better performance:

```csharp
// Configure pool size for specific event type
EventTimeline.Instance.SetMaxPoolSize<PlayerSpawnEvent>(200);

// Get current pool statistics
var (currentSize, maxSize) = EventTimeline.Instance.GetPoolStats<PlayerSpawnEvent>();

// Optimize memory usage
EventTimeline.Instance.OptimizeMemory();
```

### Event Cancellation

```csharp
// Cancel a specific event
var evt = EventTimeline.Instance.Schedule<PlayerSpawnEvent>();
evt.Cancel();

// or
EventTimeline.Instance.CancelAndRemove(evt);

// Cancel all events of a type
EventTimeline.Instance.CancelAndRemoveAll<PlayerSpawnEvent>();
```

### Event Rescheduling

```csharp
var evt = EventTimeline.Instance.Schedule<PlayerSpawnEvent>();
EventTimeline.Instance.Reschedule(evt, 5.0f); // Reschedule to 5 seconds from now
```

## Performance Considerations

- Events are processed using a binary min-heap, ensuring O(log n) complexity for enqueue and dequeue operations
- The system includes configurable limits for events per frame and processing time to maintain performance
- Object pooling reduces garbage collection pressure
- Thread-safe operations support concurrent access from background threads

```csharp
// Configure processing limits
EventTimeline.Instance.SetMaxEventsPerFrame(10);
EventTimeline.Instance.SetMaxProcessingTime(8.0f); // milliseconds
```

## Best Practices

1. **Event Design**
    - Keep events small and focused
    - Implement `Reset()` for proper object pooling
    - Use appropriate validation in model classes

2. **Performance**
    - Configure appropriate pool sizes for frequently used events
    - Use `OptimizeMemory()` during scene transitions
    - Monitor event processing limits in development builds

3. **Thread Safety**
    - Access EventTimeline.Instance from any thread
    - Use model system for thread-safe state management
    - Be aware of Unity's main thread requirements for certain operations

## Debug Support

The event timeline system provides two levels of debug logging that can be enabled via compiler symbols:

### Basic Debug Logging

Basic debug logging includes warnings and errors, enabled by defining the `EVENTTIMELINE_DEBUG` symbol. The logging will
be active in the Unity Editor or development builds when this symbol is defined:

```csharp
#if (DEVELOPMENT_BUILD || UNITY_EDITOR) && EVENTTIMELINE_DEBUG
    // Basic debug logging is enabled in this condition.
#endif
```

### Verbose Debug Logging

Verbose logging includes detailed operational information and requires both `EVENTTIMELINE_DEBUG` and
`EVENTTIMELINE_DEBUG_VERBOSE` symbols to be defined:

```csharp
#if EVENTTIMELINE_DEBUG && EVENTTIMELINE_DEBUG_VERBOSE
    // Verbose debug logging is enabled in this condition.
#endif
```

### Configuring Debug Symbols

There are two ways to define these debug symbols:

1. Via Unity's Player Settings:
    - Open Project Settings -> Player
    - Under "Other Settings" -> "Script Compilation"
    - Add to "Scripting Define Symbols":
      ```
      EVENTTIMELINE_DEBUG
      EVENTTIMELINE_DEBUG_VERBOSE
      ```

2. Via compilation arguments in your build configuration:
   ```
   -define:EVENTTIMELINE_DEBUG
   -define:EVENTTIMELINE_DEBUG_VERBOSE
   ```

### Debug Output Examples

Basic debug logging (`EVENTTIMELINE_DEBUG`) includes:

- Warnings about invalid operations
- Error conditions
- Important state changes

Verbose logging (`EVENTTIMELINE_DEBUG_VERBOSE`) additionally includes:

- Detailed execution timing
- Event processing steps
- Pool operations
- Queue state changes
- Memory optimization details

For example, event execution with verbose logging enabled:

```
[TimelineEvent] >>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>
[TimelineEvent] Beginning internal execution of event: CustomEvent
[TimelineEvent] Successfully executed event: CustomEvent
[TimelineEvent] Execution time: 1ms
[TimelineEvent] <<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<
```

Note: Debug logging is automatically disabled in release builds unless explicitly enabled through compilation symbols.

## Example Scenarios

### Quest System

```csharp
public class QuestEvent : TimelineEvent<QuestEvent>
{
    public string QuestId { get; set; }
    public Action<bool> OnComplete { get; set; }

    protected override void Execute()
    {
        // Quest completion logic
        OnComplete?.Invoke(true);
    }
}

// Schedule quest completion
EventTimeline.Instance.Schedule<QuestEvent>(evt => {
    evt.QuestId = "MainQuest_1";
    evt.OnComplete = success => {
        if (success) Debug.Log("Quest completed!");
    };
}, delay: 60f); // Complete after 60 seconds
```

### Buff System

```csharp
public class BuffEvent : TimelineEvent<BuffEvent>
{
    public float Duration { get; set; }
    public float Magnitude { get; set; }
    public Action OnExpire { get; set; }

    protected override void Execute()
    {
        // Buff expiration logic
        OnExpire?.Invoke();
    }
}

// Apply a temporary buff
var buffEvent = EventTimeline.Instance.Schedule<BuffEvent>(evt => {
    evt.Duration = 10f;
    evt.Magnitude = 1.5f;
    evt.OnExpire = () => Debug.Log("Buff expired");
}, delay: 10f);
```

## License

This package is licensed under
the MIT License. See
the [LICENSE](https://github.com/ahmedkamalio/UnityEventTimeline/blob/main/LICENSE)
file for details.
