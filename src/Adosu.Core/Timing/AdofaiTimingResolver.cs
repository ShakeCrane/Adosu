using System.Globalization;
using System.Text.Json;
using Adosu.Core.Model;
using Adosu.Core.Parsing;

namespace Adosu.Core.Timing;

public static class AdofaiTimingResolver
{
    private const double FirstEntryAngle = Math.PI * 1.5;
    private const double AngleEpsilon = 1e-6;
    private const string AdofaiUnclassifiedActionCode = "ADF-ACTION-UNCLASSIFIED";
    private const string AdofaiFloorRangeCode = "ADF-FLOOR-RANGE";

    /// <summary>
    /// Stable ordering for any derived event that exposes source order over
    /// real time: real time first, then the source index as the tie-break.
    /// </summary>
    private static int CompareByTimeThenSourceIndex<T>(
        T left,
        T right,
        Func<T, double?> time,
        Func<T, SourceProvenance> provenance)
    {
        var timeComparison = Nullable.Compare(time(left), time(right));
        return timeComparison != 0
            ? timeComparison
            : provenance(left).SourceIndex.CompareTo(provenance(right).SourceIndex);
    }

    private static int CompareByTimeThenSourceIndex(GameplayStateChange left, GameplayStateChange right) =>
        CompareByTimeThenSourceIndex(left, right, change => change.TimeSeconds, change => change.Provenance);

    private static int CompareByTimeThenSourceIndex(PresentationEvent left, PresentationEvent right) =>
        CompareByTimeThenSourceIndex(left, right, change => change.TimeSeconds, change => change.Provenance);

    private static int CompareByTimeThenSourceIndex(TempoEvent left, TempoEvent right) =>
        CompareByTimeThenSourceIndex(left, right, tempoEvent => tempoEvent.TimeSeconds, tempoEvent => tempoEvent.Provenance);

    /// <summary>
    /// Actions explicitly known to be presentation-only VFX in the supported
    /// ADOFAI scope. Only these are eligible for presentation stripping; any
    /// other action is retained as unknown gameplay state, never silently
    /// reclassified as removable VFX.
    /// </summary>
    private static readonly HashSet<string> AdofaiPresentationActions = new(StringComparer.Ordinal)
    {
        "MoveCamera",
        "MoveTrack",
        "MoveDecorations",
        "AddDecoration",
        "SetFilter",
        "RecolorTrack",
        "CustomBackground",
        "SetPlanetRotation",
        "Bloom",
        "SetHitsound",
        "PositionTrack",
        "Flash"
    };

    internal static GameplayChart Resolve(AdofaiDocument document)
    {
        var diagnostics = document.InitialDiagnostics.ToList();
        var baseBpm = document.BaseBpm.GetValueOrDefault(120);
        if (document.BaseBpm is null)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Error,
                "ADF008",
                "No positive base BPM was supplied; the resolver used 120 BPM as a safe fallback."));
        }

        if (baseBpm <= 0 || double.IsNaN(baseBpm) || double.IsInfinity(baseBpm))
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Error,
                "ADF009",
                $"Base BPM must be positive; received {baseBpm}. The resolver used 120 BPM."));
            baseBpm = 120;
        }

        var floors = BuildFloors(document.Directions);
        var tempoEvents = new List<TempoEvent>();
        var tempoSegments = new List<TempoSegment>();
        var inputEvents = new List<InputEvent>(floors.Count);
        var gameplayChanges = new List<GameplayStateChange>();
        var presentationEvents = new List<PresentationEvent>();
        var sourceEvents = document.Actions
            .OrderBy(action => action.SourceIndex)
            .Select(action => new SourceEvent(
                action.EventType,
                action.SourceIndex,
                action.FloorIndex,
                // A missing or non-numeric floor is reported by its own raw
                // source record; it is never disguised as floor -1.
                Provenance(action, action.FloorIndex),
                RawText(action.Raw)))
            .ToList();
        var currentBpm = baseBpm;
        var twirl = false;
        var planetCount = 2;
        var floorStartTimes = new double[floors.Count];

        // A floor index that is null, negative, or beyond the last floor is
        // not silently dropped from the semantic tracks. Each such action is
        // retained once as unknown gameplay state with its raw source record
        // (or, for a non-object entry, its raw JSON text) plus a stable
        // ADF-FLOOR-RANGE diagnostic.
        foreach (var action in document.Actions
                     .Where(action => action.FloorIndex is null
                                      || action.FloorIndex.Value < 0
                                      || action.FloorIndex.Value >= floors.Count)
                     .OrderBy(action => action.SourceIndex))
        {
            var floorIndex = action.FloorIndex;
            var provenance = Provenance(action, floorIndex);
            gameplayChanges.Add(new GameplayStateChange(
                GameplayStateChangeKind.Unknown,
                action.EventType,
                TimeSeconds: 0,
                provenance,
                RawData: RawText(action.Raw)));
            diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Warning,
                AdofaiFloorRangeCode,
                floorIndex is null
                    ? $"ADOFAI action '{action.EventType}' has no usable floor index; it is preserved as unknown gameplay state at time zero."
                    : $"ADOFAI action '{action.EventType}' references floor {floorIndex}, outside the resolved floor range 0..{floors.Count - 1}; it is preserved as unknown gameplay state at time zero.",
                provenance));
        }

        var inRangeActions = document.Actions
            .Where(action => action.FloorIndex is not null
                             && action.FloorIndex.Value >= 0
                             && action.FloorIndex.Value < floors.Count)
            .ToList();

        for (var floorIndex = 0; floorIndex < floors.Count; floorIndex++)
        {
            var floor = floors[floorIndex];
            var floorActions = inRangeActions
                .Where(action => action.FloorIndex == floorIndex)
                .OrderBy(action => action.SourceIndex)
                .ToList();

            // Every gameplay-state action is classified in one source-ordered
            // pass. Geometry-changing state (Twirl, MultiPlanet) is applied
            // here as well, so multiple action types on the same floor can
            // never be reordered relative to each other in GameplayState.
            var stateDurationBeats = 0d;
            foreach (var action in floorActions)
            {
                switch (action.EventType)
                {
                    case "Twirl":
                        twirl = !twirl;
                        gameplayChanges.Add(new GameplayStateChange(
                            GameplayStateChangeKind.Twirl,
                            action.EventType,
                            floorStartTimes[floorIndex],
                            Provenance(action, floorIndex),
                            TwirlStateAfter: twirl,
                            RawData: RawText(action.Raw)));
                        break;

                    case "MultiPlanet":
                        var planets = ReadDouble(action.Raw, "planets");
                        if (planets is not null)
                        {
                            planetCount = Math.Clamp((int)planets.Value, 2, 3);
                        }

                        gameplayChanges.Add(new GameplayStateChange(
                            GameplayStateChangeKind.MultiPlanet,
                            action.EventType,
                            floorStartTimes[floorIndex],
                            Provenance(action, floorIndex),
                            RawData: RawText(action.Raw)));
                        break;

                    case "Pause":
                        stateDurationBeats += ReadDouble(action.Raw, "duration").GetValueOrDefault();
                        gameplayChanges.Add(new GameplayStateChange(
                            GameplayStateChangeKind.Pause,
                            action.EventType,
                            floorStartTimes[floorIndex],
                            Provenance(action, floorIndex),
                            DurationBeats: ReadDouble(action.Raw, "duration"),
                            RawData: RawText(action.Raw)));
                        break;

                    case "FreeRoam":
                        gameplayChanges.Add(new GameplayStateChange(
                            GameplayStateChangeKind.FreeRoam,
                            action.EventType,
                            floorStartTimes[floorIndex],
                            Provenance(action, floorIndex),
                            DurationBeats: ReadDouble(action.Raw, "duration"),
                            RawData: RawText(action.Raw)));
                        diagnostics.Add(new Diagnostic(
                            DiagnosticSeverity.Warning,
                            "ADF-FREEROAM-UNKNOWN",
                            "FreeRoam is preserved as gameplay state but its game-time rule is not promoted to VERIFIED.",
                            Provenance(action, floorIndex)));
                        break;

                    case "Hold":
                        var holdDuration = ReadDouble(action.Raw, "duration");
                        gameplayChanges.Add(new GameplayStateChange(
                            GameplayStateChangeKind.Hold,
                            action.EventType,
                            floorStartTimes[floorIndex],
                            Provenance(action, floorIndex),
                            DurationBeats: holdDuration,
                            RawData: RawText(action.Raw)));
                        diagnostics.Add(new Diagnostic(
                            DiagnosticSeverity.Warning,
                            "ADF-HOLD-INFERRED",
                            "ADOFAI Hold.duration is retained as beats for the semantic model; its exact game timing remains externally inferred.",
                            Provenance(action, floorIndex)));
                        break;

                    case "Multitap":
                        gameplayChanges.Add(new GameplayStateChange(
                            GameplayStateChangeKind.Multitap,
                            action.EventType,
                            floorStartTimes[floorIndex],
                            Provenance(action, floorIndex),
                            RawData: RawText(action.Raw)));
                        diagnostics.Add(new Diagnostic(
                            DiagnosticSeverity.Warning,
                            "ADF-MULTITAP-UNKNOWN",
                            "Multitap is preserved as gameplay state; simultaneity and duration semantics remain UNKNOWN.",
                            Provenance(action, floorIndex)));
                        break;

                    case "SetSpeed":
                        break;

                    default:
                        if (AdofaiPresentationActions.Contains(action.EventType))
                        {
                            presentationEvents.Add(new PresentationEvent(
                                action.EventType,
                                floorStartTimes[floorIndex],
                                Provenance(action, floorIndex),
                                RawText(action.Raw)));
                            break;
                        }

                        // An action that is neither modeled gameplay/timing
                        // state nor on the explicit presentation whitelist is
                        // not known to be removable VFX. It is preserved as an
                        // explicit unknown gameplay state change with its raw
                        // provenance and a stable diagnostic so downstream
                        // stages cannot silently drop a gameplay/timing
                        // mechanism.
                        gameplayChanges.Add(new GameplayStateChange(
                            GameplayStateChangeKind.Unknown,
                            action.EventType,
                            floorStartTimes[floorIndex],
                            Provenance(action, floorIndex),
                            RawData: RawText(action.Raw)));
                        diagnostics.Add(new Diagnostic(
                            DiagnosticSeverity.Warning,
                            AdofaiUnclassifiedActionCode,
                            $"ADOFAI action '{action.EventType}' is not classified as gameplay, timing or removable presentation; it is preserved as unknown gameplay state with its raw source record.",
                            Provenance(action, floorIndex)));
                        break;
                }
            }

            floor.PlanetCount = planetCount;
            var floorAngleDegrees = GetBaseFloorAngleDegrees(floor, twirl, floorIndex > 0 && floors[floorIndex - 1].IsMidspin, planetCount);
            var timing = ResolveFloorTempo(
                floor,
                floorIndex,
                floorActions,
                floorStartTimes[floorIndex],
                currentBpm,
                floorAngleDegrees,
                tempoEvents,
                tempoSegments,
                diagnostics);
            currentBpm = timing.EndBpm;

            if (stateDurationBeats > 0)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Warning,
                    "ADF-PAUSE-INFERRED",
                    "Pause.duration was included as beats using the public reference implementation convention; validate against game behavior before treating it as VERIFIED.",
                    floorActions.FirstOrDefault(action => action.EventType == "Pause") is { } pause
                        ? Provenance(pause, floorIndex)
                        : null));
            }

            var hold = floorActions.FirstOrDefault(action => action.EventType == "Hold");
            var inputKind = hold is null ? InputEventKind.Tap : InputEventKind.Hold;
            var holdBeats = hold is null || hold.Raw is null ? null : ReadDouble(hold.Raw.Value, "duration");
            var direction = floorIndex < document.Directions.Count
                ? document.Directions[floorIndex]
                : null;
            inputEvents.Add(new InputEvent(
                inputKind,
                floorStartTimes[floorIndex],
                Lane: null,
                // ADOFAI Hold.duration is retained as a source-unit value;
                // its exact game end-time rule is not promoted to VERIFIED.
                EndTimeSeconds: null,
                new SourceProvenance(
                    SourceFormat.AdoFai,
                    floorIndex,
                    FloorIndex: floorIndex,
                    SourceType: "floor",
                    RawValue: direction?.Token),
                OriginalTimestamp: null,
                DurationBeats: holdBeats,
                RawData: direction?.Token));

            var floorDuration = timing.DurationSeconds
                                + stateDurationBeats * 60.0 / Math.Max(currentBpm, 1e-12);
            if (floorIndex + 1 < floorStartTimes.Length)
            {
                floorStartTimes[floorIndex + 1] = floorStartTimes[floorIndex] + floorDuration;
            }
        }

        // Every semantic track over real time is ordered by (TimeSeconds,
        // Provenance.SourceIndex) after all floor times are resolved. This is
        // a stable tie-break over the full ordered list - never a dictionary
        // keyed by timestamp - so same-time events keep their source order
        // even when they originate on different floors (for example a midspin
        // floor crossed with `pathData: "R!"`). Tempo events are included;
        // tempo segments already follow the piecewise floor resolution.
        gameplayChanges.Sort(CompareByTimeThenSourceIndex);
        presentationEvents.Sort(CompareByTimeThenSourceIndex);
        tempoEvents.Sort(CompareByTimeThenSourceIndex);

        var input = new InputTrack(inputEvents);
        var tempo = new TempoTrack(tempoEvents, tempoSegments);
        var state = new GameplayState(gameplayChanges);
        var metadata = document.Metadata with
        {
            BaseBpm = baseBpm,
            DirectionTokens = document.Directions,
            BaseBpmOriginal = document.BaseBpmOriginal,
            AudioOffsetOriginal = document.AudioOffsetOriginal
        };

        return new GameplayChart(
            SourceFormat.AdoFai,
            input,
            tempo,
            new ScrollPresentationTrack([], presentationEvents),
            state,
            diagnostics,
            metadata,
            sourceEvents);
    }

    private static List<FloorState> BuildFloors(IReadOnlyList<DirectionToken> directions)
    {
        var floors = Enumerable.Range(0, directions.Count + 1)
            .Select(_ => new FloorState())
            .ToList();
        floors[0].EntryAngle = FirstEntryAngle;

        for (var index = 0; index < directions.Count; index++)
        {
            var direction = directions[index];
            var floor = floors[index];
            floor.IsMidspin = direction.IsMidspin;
            floor.ExitAngle = direction.IsMidspin
                ? floor.EntryAngle
                : (90 - direction.AngleDegrees) * Math.PI / 180.0;
            floors[index + 1].EntryAngle = Mod(floor.ExitAngle + Math.PI, Math.PI * 2);
        }

        floors[^1].ExitAngle = floors[^1].EntryAngle + Math.PI;
        return floors;
    }

    private static FloorTiming ResolveFloorTempo(
        FloorState floor,
        int floorIndex,
        IReadOnlyList<AdofaiAction> actions,
        double floorStartTime,
        double initialBpm,
        double floorAngleDegrees,
        ICollection<TempoEvent> tempoEvents,
        ICollection<TempoSegment> tempoSegments,
        ICollection<Diagnostic> diagnostics)
    {
        var speedActions = actions
            .Where(action => action.EventType == "SetSpeed")
            .OrderBy(action => action.SourceIndex)
            .ToList();
        var speedOffsets = new List<(AdofaiAction Action, double Offset)>();
        var offsets = new List<double> { 0 };
        foreach (var action in speedActions)
        {
            var offset = ReadDouble(action.Raw, "angleOffset").GetValueOrDefault();
            if (offset < 0 || offset > floorAngleDegrees)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Warning,
                    "ADF-SPEED-OFFSET",
                    $"SetSpeed angleOffset {offset} is outside the resolved floor span {floorAngleDegrees}; it is clamped for timing while the raw event is retained.",
                    Provenance(action, floorIndex)));
                offset = Math.Clamp(offset, 0, floorAngleDegrees);
            }

            speedOffsets.Add((action, offset));
            if (!offsets.Any(existing => existing == offset))
            {
                offsets.Add(offset);
            }
        }

        offsets.Sort();
        var currentBpm = initialBpm;
        var elapsed = 0d;
        var previousOffset = 0d;
        foreach (var offset in offsets)
        {
            if (offset > previousOffset)
            {
                var segmentDuration = (offset - previousOffset) / 180.0 * 60.0 / Math.Max(currentBpm, 1e-12);
                var segmentStart = floorStartTime + elapsed;
                elapsed += segmentDuration;
                tempoSegments.Add(new TempoSegment(
                    segmentStart,
                    floorStartTime + elapsed,
                    currentBpm,
                    StartAngleDegrees: previousOffset,
                    EndAngleDegrees: offset));
            }

            foreach (var speedAction in speedOffsets.Where(item => item.Offset == offset))
            {
                var action = speedAction.Action;
                var modeText = ReadString(action.Raw!.Value, "speedType");
                var mode = modeText switch
                {
                    "Bpm" => SetSpeedMode.Bpm,
                    "Multiplier" => SetSpeedMode.Multiplier,
                    _ => SetSpeedMode.Unknown
                };
                var bpm = ReadDouble(action.Raw, "beatsPerMinute");
                var multiplier = ReadDouble(action.Raw, "bpmMultiplier");
                var provenance = Provenance(action, floorIndex);
                tempoEvents.Add(new TempoEvent(
                    TempoEventKind.AdoFaiSetSpeed,
                    floorStartTime + elapsed,
                    provenance,
                    Bpm: mode == SetSpeedMode.Bpm ? bpm : null,
                    SpeedMode: mode,
                    Multiplier: mode == SetSpeedMode.Multiplier ? multiplier : null,
                    AngleOffsetDegrees: offset,
                    RawData: RawText(action.Raw)));

                switch (mode)
                {
                    case SetSpeedMode.Bpm when bpm is > 0:
                        currentBpm = bpm.Value;
                        break;
                    case SetSpeedMode.Multiplier when multiplier is > 0:
                        currentBpm *= multiplier.Value;
                        break;
                    case SetSpeedMode.Unknown:
                        diagnostics.Add(new Diagnostic(
                            DiagnosticSeverity.Warning,
                            "ADF-SPEED-TYPE",
                            $"Unknown SetSpeed.speedType '{modeText}' was preserved but did not alter timing.",
                            provenance));
                        break;
                    default:
                        diagnostics.Add(new Diagnostic(
                            DiagnosticSeverity.Error,
                            "ADF-SPEED-VALUE",
                            "SetSpeed contained a non-positive or missing BPM/multiplier; the previous tempo was retained.",
                            provenance));
                        break;
                }
            }

            previousOffset = offset;
        }

        if (floorAngleDegrees > previousOffset)
        {
            var segmentStart = floorStartTime + elapsed;
            elapsed += (floorAngleDegrees - previousOffset) / 180.0 * 60.0 / Math.Max(currentBpm, 1e-12);
            tempoSegments.Add(new TempoSegment(
                segmentStart,
                floorStartTime + elapsed,
                currentBpm,
                StartAngleDegrees: previousOffset,
                EndAngleDegrees: floorAngleDegrees));
        }

        return new FloorTiming(elapsed, currentBpm);
    }

    private static double GetBaseFloorAngleDegrees(
        FloorState floor,
        bool isCcw,
        bool previousWasMidspin,
        int planetCount)
    {
        var entry = floor.EntryAngle;
        var exit = floor.ExitAngle;
        var offset = InverseAnglePerBeatMultiplanet(planetCount) * (isCcw ? -1 : 1);
        if (floor.IsMidspin)
        {
            offset = 0;
        }

        if (previousWasMidspin && planetCount > 2)
        {
            offset -= (Math.PI * 2 + InverseAnglePerBeatMultiplanet(planetCount)) * (isCcw ? -1 : 1);
        }

        entry += offset;
        if (floor.IsMidspin)
        {
            exit += offset;
        }

        var moved = AngleMoved(entry, exit, isCw: !isCcw);
        if (moved <= AngleEpsilon || moved >= Math.PI * 2 - AngleEpsilon)
        {
            moved = floor.IsMidspin ? 0 : Math.PI * 2;
        }

        return moved * 180.0 / Math.PI;
    }

    private static SourceProvenance Provenance(AdofaiAction action, int? floorIndex) =>
        new(
            SourceFormat.AdoFai,
            action.SourceIndex,
            FloorIndex: floorIndex,
            SourceType: action.EventType,
            RawValue: RawText(action.Raw));

    private static string? ReadString(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value)
               && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    /// <summary>
    /// Raw text for an optional action payload. Only a non-object array entry
    /// has no payload; its raw JSON text is still captured by the reader as a
    /// source record.
    /// </summary>
    private static string? RawText(JsonElement? element) =>
        element is { } value ? value.GetRawText() : null;

    private static double? ReadDouble(JsonElement? element, string property) =>
        element is { } value ? ReadDouble(value, property) : null;

    private static double? ReadDouble(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
        {
            return null;
        }

        return double.TryParse(
            value.GetRawText(),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var result)
            ? result
            : null;
    }

    private static double InverseAnglePerBeatMultiplanet(int planetCount) =>
        Math.PI * (planetCount - 2.0) / planetCount;

    private static double AngleMoved(double entry, double exit, bool isCw)
    {
        var delta = (exit - entry) * (isCw ? 1 : -1);
        return Mod(delta, Math.PI * 2);
    }

    private static double Mod(double value, double modulus)
    {
        var result = value % modulus;
        return result < 0 ? result + modulus : result;
    }

    private sealed class FloorState
    {
        public double EntryAngle { get; set; }
        public double ExitAngle { get; set; }
        public bool IsMidspin { get; set; }
        public int PlanetCount { get; set; } = 2;
    }

    private sealed record FloorTiming(double DurationSeconds, double EndBpm);
}
