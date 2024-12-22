#nullable enable

namespace UnityEventTimeline
{
    /// <summary>
    /// Base class for all models used within the EventTimeline system. Provides core functionality for model validation and access tracking.
    /// </summary>
    /// <remarks>
    /// <para>
    /// EventTimelineModel serves as the foundation for creating custom data models that can be managed by the EventTimelineModelManager.
    /// It provides built-in support for tracking when models are accessed and basic validation capabilities.
    /// </para>
    /// 
    /// <para>
    /// Models extending this class can be stored in and retrieved from the EventTimeline system using methods like
    /// SetModel, TryGetModel, and GetOrCreateModel. The system automatically tracks access times and can perform
    /// cleanup of unused models.
    /// </para>
    /// 
    /// <para>
    /// Example usage:
    /// <code>
    /// public class GameStateModel : EventTimelineModel
    /// {
    ///     public int Score { get; set; }
    ///     public float TimeRemaining { get; set; }
    ///     
    ///     public override bool Validate()
    ///     {
    ///         return TimeRemaining >= 0;
    ///     }
    /// }
    /// </code>
    /// </para>
    /// </remarks>
    public abstract class EventTimelineModel
    {
        /// <summary>
        /// Gets the time when this model was last accessed, in Unity time scale.
        /// </summary>
        /// <remarks>
        /// This value is automatically updated by the EventTimelineModelManager when the model is accessed
        /// through its API. It's used internally for tracking model usage and supporting cleanup of unused models.
        /// </remarks>
        public float LastAccessedTime { get; private set; }

        /// <summary>
        /// Validates the current state of the model.
        /// </summary>
        /// <returns>
        /// true if the model's state is valid; false otherwise.
        /// By default, returns true unless overridden.
        /// </returns>
        /// <remarks>
        /// Override this method to implement custom validation logic for your model.
        /// This method is called by the EventTimelineModelManager before storing new model instances.
        /// </remarks>
        public virtual bool Validate() => true;

        /// <summary>
        /// Updates the last accessed time of this model.
        /// </summary>
        /// <param name="time">The current time value to set.</param>
        /// <remarks>
        /// This method is called internally by the EventTimelineModelManager and should not be called directly.
        /// It's used to track when models are accessed for cleanup and optimization purposes.
        /// </remarks>
        internal void SetLastAccessedTime(float time) => LastAccessedTime = time;
    }
}