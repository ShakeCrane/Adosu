using System.Globalization;

namespace Adosu.Core.Model;

public enum SourceFormat
{
    AdoFai,
    OsuMania,
    Synthetic
}

public enum DiagnosticSeverity
{
    Info,
    Warning,
    Error
}

public sealed record SourceProvenance(
    SourceFormat Format,
    int SourceIndex,
    int? FloorIndex = null,
    int? ObjectIndex = null,
    string? SourceTimestamp = null,
    string? SourceType = null,
    string? RawValue = null);

public sealed record Diagnostic(
    DiagnosticSeverity Severity,
    string Code,
    string Message,
    SourceProvenance? Provenance = null);

/// <summary>
/// A parsed number keeps its original token as well as derived numeric forms.
/// The token is retained so serialization and diagnostics do not have to guess
/// the source precision after a decimal has passed through the semantic model.
/// </summary>
public sealed record SourceNumber(string Text, decimal? DecimalValue, double? DoubleValue)
{
    public static SourceNumber Parse(string text)
    {
        var trimmed = text.Trim();
        var hasDecimal = decimal.TryParse(
            trimmed,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var decimalValue);
        var hasDouble = double.TryParse(
            trimmed,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var doubleValue);

        return new SourceNumber(
            trimmed,
            hasDecimal ? decimalValue : null,
            hasDouble ? doubleValue : null);
    }
}

public enum InputEventKind
{
    Tap,
    Hold,
    Unknown
}

public sealed record InputEvent(
    InputEventKind Kind,
    double TimeSeconds,
    int? Lane,
    double? EndTimeSeconds,
    SourceProvenance Provenance,
    string? OriginalTimestamp = null,
    string? OriginalEndTimestamp = null,
    double? DurationBeats = null,
    string? RawData = null);

public sealed class InputTrack
{
    private readonly List<InputEvent> events;

    public InputTrack(IEnumerable<InputEvent> events)
    {
        this.events = events.ToList();
    }

    public IReadOnlyList<InputEvent> Events => events;

    /// <summary>
    /// Groups exact same-time events without replacing any event. A chord is a
    /// relation over the ordered source list, not a dictionary value keyed by
    /// timestamp.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<InputEvent>> Chords()
    {
        var groups = new List<List<InputEvent>>();
        foreach (var inputEvent in events)
        {
            var group = groups.FirstOrDefault(candidate => candidate[0].TimeSeconds == inputEvent.TimeSeconds);
            if (group is null)
            {
                groups.Add([inputEvent]);
            }
            else
            {
                group.Add(inputEvent);
            }
        }

        return groups
            .Where(group => group.Count > 1)
            .Select(group => (IReadOnlyList<InputEvent>)group)
            .ToList();
    }
}

public enum TempoEventKind
{
    RedTimingPoint,
    AdoFaiSetSpeed
}

public enum SetSpeedMode
{
    Bpm,
    Multiplier,
    Unknown
}

/// <summary>
/// Whether a tempo event was applied to the canonical tempo/time state.
/// A rejected event stays in the track with its raw source record and stable
/// diagnostic, but it never alters BPM, segments or subsequent event times.
/// Its derived numeric fields are canonical-empty (null) so an illegal source
/// value cannot masquerade as an applied canonical value.
/// </summary>
public enum TempoEventApplication
{
    Applied,
    Rejected
}

public sealed record TempoEvent(
    TempoEventKind Kind,
    double TimeSeconds,
    SourceProvenance Provenance,
    double? Bpm = null,
    SetSpeedMode? SpeedMode = null,
    double? Multiplier = null,
    double? AngleOffsetDegrees = null,
    string? OriginalTime = null,
    string? OriginalBeatLength = null,
    string? RawData = null,
    TempoEventApplication Application = TempoEventApplication.Applied,
    int ApplicationOrder = -1);

public sealed record TempoSegment(
    double StartTimeSeconds,
    double EndTimeSeconds,
    double Bpm,
    SourceProvenance? Provenance = null,
    double? StartAngleDegrees = null,
    double? EndAngleDegrees = null);

public sealed class TempoTrack
{
    public TempoTrack(
        IEnumerable<TempoEvent> events,
        IEnumerable<TempoSegment>? segments = null)
    {
        Events = events.ToList();
        Segments = (segments ?? []).ToList();
    }

    public IReadOnlyList<TempoEvent> Events { get; }

    public IReadOnlyList<TempoSegment> Segments { get; }
}

public enum ScrollEventKind
{
    ManiaSliderVelocity,
    Unknown
}

public sealed record ScrollEvent(
    ScrollEventKind Kind,
    double TimeSeconds,
    double? Multiplier,
    SourceProvenance Provenance,
    string? OriginalTime = null,
    string? OriginalValue = null,
    string? RawData = null);

public sealed record PresentationEvent(
    string EventType,
    double? TimeSeconds,
    SourceProvenance Provenance,
    string? RawData = null);

public sealed class ScrollPresentationTrack
{
    public ScrollPresentationTrack(
        IEnumerable<ScrollEvent> scrollEvents,
        IEnumerable<PresentationEvent> presentationEvents)
    {
        ScrollEvents = scrollEvents.ToList();
        PresentationEvents = presentationEvents.ToList();
    }

    public IReadOnlyList<ScrollEvent> ScrollEvents { get; }

    public IReadOnlyList<PresentationEvent> PresentationEvents { get; }

    public ScrollPresentationTrack WithoutPresentationEvents() =>
        new(ScrollEvents, []);
}

public enum GameplayStateChangeKind
{
    Twirl,
    Pause,
    Hold,
    FreeRoam,
    MultiPlanet,
    Multitap,
    Unknown
}

/// <summary>
/// Whether a gameplay state change carrying an explicit duration was accepted
/// into the canonical state. A rejected change stays in the track with its raw
/// source record and a stable diagnostic, but its derived <see cref="GameplayStateChange.DurationBeats"/>
/// is canonical-empty (null) so an illegal source value cannot masquerade as an
/// applied canonical value. Only events whose duration is genuinely part of
/// their semantics (Pause, Hold, FreeRoam) expose this; the default
/// <see cref="NotApplicable"/> keeps every other change type unchanged.
/// </summary>
public enum GameplayStateChangeApplication
{
    NotApplicable,
    Applied,
    Rejected
}

public sealed record GameplayStateChange(
    GameplayStateChangeKind Kind,
    string EventType,
    double TimeSeconds,
    SourceProvenance Provenance,
    double? DurationBeats = null,
    bool? TwirlStateAfter = null,
    string? RawData = null,
    int ApplicationOrder = -1,
    GameplayStateChangeApplication Application = GameplayStateChangeApplication.NotApplicable);

public sealed class GameplayState
{
    public GameplayState(IEnumerable<GameplayStateChange> changes)
    {
        Changes = changes.ToList();
        ChangesInApplicationOrder = Changes
            .OrderBy(change => change.ApplicationOrder)
            .ThenBy(change => change.TimeSeconds)
            .ThenBy(change => change.Provenance.SourceIndex)
            .ToList();
    }

    /// <summary>
    /// The derived track in its presentation order: real time first, then the
    /// source index as a stable tie-break. This order is deterministic but is
    /// presentation only; it is not the order in which stateful events were
    /// applied to the canonical gameplay state.
    /// </summary>
    public IReadOnlyList<GameplayStateChange> Changes { get; }

    /// <summary>
    /// Semantic application order used to replay state. It is ascending by
    /// floor traversal, then by same-floor source order; it is never derived
    /// from the presentation list order or from any lossy timestamp grouping.
    /// </summary>
    public IReadOnlyList<GameplayStateChange> ChangesInApplicationOrder { get; }

    public bool TwirlAt(double timeSeconds)
    {
        var twirl = false;
        foreach (var change in ChangesInApplicationOrder
                     .Where(change => change.Kind == GameplayStateChangeKind.Twirl))
        {
            if (change.TimeSeconds > timeSeconds)
            {
                break;
            }

            twirl = change.TwirlStateAfter ?? !twirl;
        }

        return twirl;
    }
}

public sealed record DirectionToken(
    int SourceIndex,
    string Token,
    double AngleDegrees,
    bool IsMidspin,
    SourceProvenance Provenance);

public sealed record ChartMetadata(
    SourceFormat Format,
    double? BaseBpm = null,
    double? AudioOffsetMilliseconds = null,
    int? KeyCount = null,
    string? RawPathData = null,
    IReadOnlyList<DirectionToken>? DirectionTokens = null,
    string? BaseBpmOriginal = null,
    string? AudioOffsetOriginal = null,
    string? KeyCountOriginal = null);

public sealed record SourceEvent(
    string EventType,
    int SourceIndex,
    int? FloorIndex,
    SourceProvenance Provenance,
    string? RawData = null);

public sealed class GameplayChart
{
    public GameplayChart(
        SourceFormat sourceFormat,
        InputTrack inputTrack,
        TempoTrack tempoTrack,
        ScrollPresentationTrack scrollPresentationTrack,
        GameplayState gameplayState,
        IEnumerable<Diagnostic>? diagnostics = null,
        ChartMetadata? metadata = null,
        IEnumerable<SourceEvent>? sourceEvents = null)
    {
        SourceFormat = sourceFormat;
        InputTrack = inputTrack;
        TempoTrack = tempoTrack;
        ScrollPresentationTrack = scrollPresentationTrack;
        GameplayState = gameplayState;
        Diagnostics = (diagnostics ?? []).ToList();
        Metadata = metadata ?? new ChartMetadata(sourceFormat);
        SourceEvents = (sourceEvents ?? []).ToList();
    }

    public SourceFormat SourceFormat { get; }

    public InputTrack InputTrack { get; }

    public TempoTrack TempoTrack { get; }

    public ScrollPresentationTrack ScrollPresentationTrack { get; }

    public GameplayState GameplayState { get; }

    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    public ChartMetadata Metadata { get; }

    public IReadOnlyList<SourceEvent> SourceEvents { get; }

    /// <summary>
    /// Removes presentation-only events while retaining tempo, input and
    /// gameplay state. In particular, Twirl is never treated as VFX.
    /// </summary>
    public GameplayChart WithoutPresentationEvents() =>
        new(
            SourceFormat,
            InputTrack,
            TempoTrack,
            ScrollPresentationTrack.WithoutPresentationEvents(),
            GameplayState,
            Diagnostics,
            Metadata,
            SourceEvents);
}
