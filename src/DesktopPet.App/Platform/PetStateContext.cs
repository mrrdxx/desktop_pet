using DesktopPet.Core.Emotion;
using DesktopPet.Core.Events;

namespace DesktopPet.App.Platform;

/// <summary>
/// PREP-002: Collects the single-pet shared state objects and save-dirty
/// notification into one explicit context that <c>App</c> owns and passes
/// to <c>MainWindow</c>.
/// <para>
/// Instead of <c>MainWindow</c> reaching back through
/// <c>Application.Current is App</c> to grab individual objects, App creates
/// this context in <c>OnStartup</c> and assigns it to
/// <c>MainWindow.StateContext</c>. This makes the dependency direction
/// explicit (App → MainWindow) while keeping the same objects and save
/// semantics.
/// </para>
/// <para>
/// The <c>MarkStateDirty</c> delegate is supplied by App so that the
/// context does not need to know about <c>SaveCoordinator</c> or
/// <c>PetStateArchive</c>. MainWindow calls
/// <c>StateContext.MarkStateDirty?.Invoke()</c> instead of
/// <c>Application.Current is App app → app.MarkStateDirty()</c>.
/// </para>
/// <para>
/// Test injection: unit tests can either set individual <c>Test*</c>
/// fields on <c>MainWindow</c> (highest priority) or construct a
/// <c>PetStateContext</c> with test objects and assign it to
/// <c>MainWindow.StateContext</c>. When both are null,
/// <c>MainWindow</c> falls back to <c>Application.Current is App</c> for
/// backward compatibility with existing tests that use the parameterless
/// constructor.
/// </para>
/// </summary>
public sealed class PetStateContext
{
    /// <summary>
    /// Emotion decay accumulator (P1-003). Null when not yet initialised.
    /// </summary>
    public EmotionDecayAccumulator? EmotionAccumulator { get; init; }

    /// <summary>
    /// Corner stay tracker (P1-003). Null when not yet initialised.
    /// </summary>
    public CornerTracker? CornerTracking { get; init; }

    /// <summary>
    /// Grievance expression state (P1-003). Null when not yet initialised.
    /// </summary>
    public GrievanceExpressionState? GrievanceExpression { get; init; }

    /// <summary>
    /// Effective runtime tracker (P1-004). Null when not yet initialised.
    /// </summary>
    public EffectiveRuntimeTracker? RuntimeTracker { get; init; }

    /// <summary>
    /// Rare-event scheduler (P1-004). Null when not yet initialised.
    /// </summary>
    public RareEventScheduler? RareEventScheduler { get; init; }

    /// <summary>
    /// Save-dirty notification callback. Supplied by App so that MainWindow
    /// can request a debounced save without knowing about SaveCoordinator.
    /// Null in test contexts where save is not exercised.
    /// </summary>
    public Action? MarkStateDirty { get; init; }
}
