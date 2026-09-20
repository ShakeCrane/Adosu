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
    private const string AdofaiSpeedValueCode = "ADF-SPEED-VALUE";
    private const string AdofaiSpeedOffsetCode = "ADF-SPEED-OFFSET";
    private const string AdofaiDurationValueCode = "ADF-DURATION-VALUE";

    /// <summary>
    /// A tempo is usable only when the real duration it derives stays finite
    /// and positive. The largest travel a single floor can express is a full
    /// 360-degree turn, so the invariant is checked on that worst case rather
    /// than on an arbitrary hand-picked BPM threshold: very small tempos make
    /// `angle / 180 * 60 / bpm` overflow, very large ones make it underflow.
    /// </summary>
    private static bool IsUsableTempo(double bpm)
    {
        if (!double.IsFinite(bpm) || bpm <= 0)
        {
            return false;
        }

        var worstCaseSeconds = 360.0 / 180.0 * 60.0 / bpm;
        return double.IsFinite(worstCaseSeconds) && worstCaseSeconds > 0;
    }

    /// <summary>
    /// A duration in beats is usable only when it is finite and non-negative.
    /// Zero is legal (an explicit no-op pause); a missing or non-numeric
    /// duration, a non-finite one (including the `1e309` -&gt; +Infinity parse
    /// overflow) and a negative one are illegal. There is deliberately no
    /// clamp, absolute value, epsilon or implicit default: an unrepresentable
    /// source value is rejected, never repaired.
    /// </summary>
    private static bool IsUsableDurationBeats(double? durationBeats) =>
        durationBeats is { } value && double.IsFinite(value) && value >= 0;

    private static bool IsUsableDurationSeconds(double value) =>
        double.IsFinite(value) && value >= 0;

    /// <summary>
    /// One Pause candidate that has passed the raw beat check and now waits for
    /// the floor's canonical end tempo before its seconds can be validated.
    /// The whole candidate delay is validated against the same floor-end BPM
    /// and the same `beats * 60 / BPM` expression that finally advances the
    /// floor, so a value that is legal under the entry BPM but not under the
    /// canonical floor-end BPM is rejected instead of overflowing the axis.
    /// </summary>
    private sealed record PauseCandidate(
        AdofaiAction Action,
        SourceProvenance Provenance,
        double DurationBeats);

    /// <summary>
    /// Validates and commits the still-uncommitted Pause candidates of one
    /// floor against its canonical floor-end tempo. Each candidate is checked
    /// as a transaction over the Pauses accepted so far: the accumulated raw
    /// beats must stay finite and non-negative, the seconds derived from them
    /// with the final `beats * 60 / BPM` step must stay finite and
    /// non-negative, and the resulting floor end must stay finite. Only the
    /// offending Pause is rejected; previously accepted Pauses, other tempo and
    /// state changes, and the application order are preserved.
    ///
    /// Returns the canonical seconds contributed by all accepted Pauses on the
    /// floor, so the caller advances the floor with that exact value instead of
    /// recomputing it through a different expression.
    /// </summary>
    private static double RejectNonCanonicalPauses(
        List<PauseCandidate> pending,
        double pauseSeconds,
        double floorEndBpm,
        double floorTravelSeconds,
        double floorStartTime,
        ICollection<GameplayStateChange> gameplayChanges,
        ICollection<Diagnostic> diagnostics)
    {
        var acceptedBeats = 0d;
        var acceptedSeconds = pauseSeconds;
        foreach (var candidate in pending)
        {
            var candidateBeats = acceptedBeats + candidate.DurationBeats;
            var candidateDelaySeconds = candidateBeats * 60.0 / floorEndBpm;
            var candidateDuration = floorTravelSeconds + candidateDelaySeconds;
            var rejection = (string?)null;
            if (!double.IsFinite(candidateBeats) || candidateBeats < 0)
            {
                rejection = $"Pause.duration {candidate.DurationBeats.ToString(CultureInfo.InvariantCulture)} beats overflows the accumulated pause on this floor.";
            }
            else if (!IsUsableDurationSeconds(candidateDelaySeconds))
            {
                rejection = $"Pause.duration {candidate.DurationBeats.ToString(CultureInfo.InvariantCulture)} beats at floor-end BPM {floorEndBpm.ToString(CultureInfo.InvariantCulture)} produces a non-finite or negative delay in seconds.";
            }
            else if (!double.IsFinite(floorStartTime + candidateDuration))
            {
                rejection = $"Pause.duration {candidate.DurationBeats.ToString(CultureInfo.InvariantCulture)} beats would push the floor end time outside the finite range.";
            }

            if (rejection is not null)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Error,
                    AdofaiDurationValueCode,
                    rejection,
                    candidate.Provenance));
            }
            else
            {
                acceptedBeats = candidateBeats;
                acceptedSeconds = acceptedBeats * 60.0 / floorEndBpm;
            }

            gameplayChanges.Add(new GameplayStateChange(
                GameplayStateChangeKind.Pause,
                candidate.Action.EventType,
                floorStartTime,
                candidate.Provenance,
                DurationBeats: rejection is null ? candidate.DurationBeats : null,
                RawData: RawText(candidate.Action.Raw),
                Application: rejection is null
                    ? GameplayStateChangeApplication.Applied
                    : GameplayStateChangeApplication.Rejected));
        }

        return acceptedSeconds;
    }

    /// <summary>
    /// Reports an illegal Hold/FreeRoam duration. The event keeps its raw
    /// record and provenance, exposes a canonical-empty DurationBeats and
    /// carries <see cref="GameplayStateChangeApplication.Rejected"/>.
    /// </summary>
    private static void ReportIllegalDuration(
        ICollection<Diagnostic> diagnostics,
        string eventType,
        AdofaiAction action,
        int floorIndex,
        double? rawDuration)
    {
        diagnostics.Add(new Diagnostic(
            DiagnosticSeverity.Error,
            AdofaiDurationValueCode,
            $"{eventType}.duration must be a finite non-negative number of beats; received '{rawDuration?.ToString(CultureInfo.InvariantCulture) ?? "<missing>"}'. The event is retained with its raw source record but is canonical-empty.",
            Provenance(action, floorIndex)));
    }

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

        if (!IsUsableTempo(baseBpm))
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Error,
                "ADF009",
                $"Base BPM must be finite, positive and small enough that a full floor's duration stays finite; received {baseBpm.ToString(CultureInfo.InvariantCulture)}. The resolver used 120 BPM."));
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

        // The semantic application ordinal for stateful derived events. It
        // advances in floor traversal order and, within a floor, in source
        // order. It is the track that state replay must follow; it is never
        // derived from the (TimeSeconds, SourceIndex) presentation order,
        // which can invert same-timestamp events originating on different
        // floors.
        var applicationOrder = 0;

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
            //
            // Pause needs the floor's canonical end tempo, which is only known
            // after ResolveFloorTempo. Its duration is therefore validated raw
            // here, deferred, and committed in source order below with the same
            // floor-end BPM and the same expression that advances the floor.
            var pendingPauses = new List<PauseCandidate>();
            var pauseSeconds = 0d;
            var hasPendingPause = false;
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
                            RawData: RawText(action.Raw),
                            ApplicationOrder: applicationOrder++));
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
                            RawData: RawText(action.Raw),
                            ApplicationOrder: applicationOrder++));
                        break;

                    case "Pause":
                        var pauseRawDuration = ReadDouble(action.Raw, "duration");
                        var pauseProvenance = Provenance(action, floorIndex);
                        if (!IsUsableDurationBeats(pauseRawDuration))
                        {
                            diagnostics.Add(new Diagnostic(
                                DiagnosticSeverity.Error,
                                AdofaiDurationValueCode,
                                $"Pause.duration must be a finite non-negative number of beats; received '{pauseRawDuration?.ToString(CultureInfo.InvariantCulture) ?? "<missing>"}'.",
                                pauseProvenance));
                            gameplayChanges.Add(new GameplayStateChange(
                                GameplayStateChangeKind.Pause,
                                action.EventType,
                                floorStartTimes[floorIndex],
                                pauseProvenance,
                                RawData: RawText(action.Raw),
                                Application: GameplayStateChangeApplication.Rejected));
                        }
                        else
                        {
                            pendingPauses.Add(new PauseCandidate(
                                action,
                                pauseProvenance,
                                pauseRawDuration!.Value));
                            hasPendingPause = true;
                        }

                        break;

                    case "FreeRoam":
                        var freeRoamDuration = ReadDouble(action.Raw, "duration");
                        var freeRoamProvenance = Provenance(action, floorIndex);
                        var freeRoamRejected = !IsUsableDurationBeats(freeRoamDuration);
                        if (freeRoamRejected)
                        {
                            // FreeRoam does not advance floor time today, but a
                            // non-finite/negative duration must still not enter
                            // canonical gameplay state as if it were a real value.
                            ReportIllegalDuration(diagnostics, action.EventType, action, floorIndex, freeRoamDuration);
                        }

                        gameplayChanges.Add(new GameplayStateChange(
                            GameplayStateChangeKind.FreeRoam,
                            action.EventType,
                            floorStartTimes[floorIndex],
                            freeRoamProvenance,
                            DurationBeats: freeRoamRejected ? null : freeRoamDuration,
                            RawData: RawText(action.Raw),
                            ApplicationOrder: applicationOrder++,
                            Application: freeRoamRejected
                                ? GameplayStateChangeApplication.Rejected
                                : GameplayStateChangeApplication.Applied));
                        diagnostics.Add(new Diagnostic(
                            DiagnosticSeverity.Warning,
                            "ADF-FREEROAM-UNKNOWN",
                            "FreeRoam is preserved as gameplay state but its game-time rule is not promoted to VERIFIED.",
                            freeRoamProvenance));
                        break;

                    case "Hold":
                        var holdDuration = ReadDouble(action.Raw, "duration");
                        var holdProvenance = Provenance(action, floorIndex);
                        var holdRejected = !IsUsableDurationBeats(holdDuration);
                        if (holdRejected)
                        {
                            ReportIllegalDuration(diagnostics, action.EventType, action, floorIndex, holdDuration);
                        }

                        gameplayChanges.Add(new GameplayStateChange(
                            GameplayStateChangeKind.Hold,
                            action.EventType,
                            floorStartTimes[floorIndex],
                            holdProvenance,
                            DurationBeats: holdRejected ? null : holdDuration,
                            RawData: RawText(action.Raw),
                            ApplicationOrder: applicationOrder++,
                            Application: holdRejected
                                ? GameplayStateChangeApplication.Rejected
                                : GameplayStateChangeApplication.Applied));
                        diagnostics.Add(new Diagnostic(
                            DiagnosticSeverity.Warning,
                            "ADF-HOLD-INFERRED",
                            "ADOFAI Hold.duration is retained as beats for the semantic model; its exact game timing remains externally inferred.",
                            holdProvenance));
                        break;

                    case "Multitap":
                        gameplayChanges.Add(new GameplayStateChange(
                            GameplayStateChangeKind.Multitap,
                            action.EventType,
                            floorStartTimes[floorIndex],
                            Provenance(action, floorIndex),
                            RawData: RawText(action.Raw),
                            ApplicationOrder: applicationOrder++));
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
                            RawData: RawText(action.Raw),
                            ApplicationOrder: applicationOrder++));
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
                applicationOrder,
                tempoEvents,
                tempoSegments,
                diagnostics);
            currentBpm = timing.EndBpm;
            applicationOrder = timing.EndApplicationOrder;

            if (hasPendingPause)
            {
                // The Pause delay is now validated against the canonical
                // floor-end tempo and the accumulated seconds. The candidates
                // are appended to GameplayState.Changes here - after every
                // non-Pause action of the floor - so the list keeps the same
                // same-floor source order the single source-ordered pass would
                // have produced; the application ordinal is then stamped in
                // that same source order.
                pauseSeconds = RejectNonCanonicalPauses(
                    pendingPauses,
                    pauseSeconds,
                    timing.EndBpm,
                    timing.DurationSeconds,
                    floorStartTimes[floorIndex],
                    gameplayChanges,
                    diagnostics);
                var firstPauseIndex = gameplayChanges.Count - pendingPauses.Count;
                for (var pauseIndex = 0; pauseIndex < pendingPauses.Count; pauseIndex++)
                {
                    gameplayChanges[firstPauseIndex + pauseIndex] =
                        gameplayChanges[firstPauseIndex + pauseIndex]
                        with { ApplicationOrder = applicationOrder++ };
                }
            }

            if (hasPendingPause && pauseSeconds > 0)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Warning,
                    "ADF-PAUSE-INFERRED",
                    "Pause.duration was included as beats using the public reference implementation convention; validate against game behavior before treating it as VERIFIED.",
                    pendingPauses.FirstOrDefault()?.Provenance));
            }

            var hold = floorActions.FirstOrDefault(action => action.EventType == "Hold");
            var inputKind = hold is null ? InputEventKind.Tap : InputEventKind.Hold;
            // An illegal Hold duration must be canonical-empty on the input
            // event too: the same value that was rejected from the gameplay
            // state may not reappear as an applied DurationBeats here.
            var holdDurationRaw = hold?.Raw is { } holdRaw ? ReadDouble(holdRaw, "duration") : null;
            var holdBeats = IsUsableDurationBeats(holdDurationRaw) ? holdDurationRaw : null;
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

            var floorDuration = timing.DurationSeconds + pauseSeconds;
            if (floorIndex + 1 < floorStartTimes.Length)
            {
                floorStartTimes[floorIndex + 1] = floorStartTimes[floorIndex] + floorDuration;
            }
        }

        // Final canonical time invariant. Every floor start, every input time,
        // every gameplay-state time and every tempo segment boundary must stay
        // finite, and adjacent floors must not move backwards. A violation here
        // can only mean an unguarded numeric path slipped through validation;
        // it is reported explicitly instead of silently handing non-finite
        // times to every downstream stage.
        for (var floorIndex = 0; floorIndex < floorStartTimes.Length; floorIndex++)
        {
            if (!double.IsFinite(floorStartTimes[floorIndex]))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Error,
                    AdofaiDurationValueCode,
                    $"Floor {floorIndex} resolved to a non-finite start time; the canonical time axis is not usable."));
            }
            else if (floorIndex > 0 && floorStartTimes[floorIndex] < floorStartTimes[floorIndex - 1])
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Error,
                    AdofaiDurationValueCode,
                    $"Floor {floorIndex} start time moved backwards relative to floor {floorIndex - 1}; the canonical time axis is not monotonic."));
            }
        }

        if (inputEvents.Any(inputEvent => !double.IsFinite(inputEvent.TimeSeconds))
            || gameplayChanges.Any(change => !double.IsFinite(change.TimeSeconds))
            || tempoSegments.Any(segment =>
                !double.IsFinite(segment.StartTimeSeconds)
                || !double.IsFinite(segment.EndTimeSeconds)
                || segment.EndTimeSeconds < segment.StartTimeSeconds))
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Error,
                AdofaiDurationValueCode,
                "At least one canonical derived event has a non-finite or backwards time; the canonical time axis is not usable."));
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
        int applicationOrder,
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
            var rawOffset = ReadDouble(action.Raw, "angleOffset");
            var offset = rawOffset.GetValueOrDefault();
            if (!double.IsFinite(offset))
            {
                // A non-finite angleOffset is a malformed source value, not an
                // in-span boundary. It is diagnosed as illegal and placed at
                // the floor start so the geometry unit mapping stays defined.
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Error,
                    AdofaiSpeedOffsetCode,
                    $"SetSpeed angleOffset must be finite; received '{rawOffset?.ToString(CultureInfo.InvariantCulture) ?? "<missing>"}'. The event is retained but treated as a floor-start boundary.",
                    Provenance(action, floorIndex)));
                offset = 0;
            }
            else if (offset < 0 || offset > floorAngleDegrees)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Warning,
                    AdofaiSpeedOffsetCode,
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
                var segmentDuration = (offset - previousOffset) / 180.0 * 60.0 / currentBpm;
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

                // The candidate is validated before anything derived from it
                // reaches canonical state. A rejected event keeps its raw
                // record and provenance, exposes canonical-empty numeric
                // fields and never perturbs BPM, segments or later times.
                var applied = false;
                var rejection = (string?)null;
                var candidateBpm = currentBpm;
                switch (mode)
                {
                    case SetSpeedMode.Bpm when bpm is { } bpmValue && IsUsableTempo(bpmValue):
                        candidateBpm = bpmValue;
                        applied = true;
                        break;
                    case SetSpeedMode.Bpm:
                        rejection = "SetSpeed BPM must be finite, positive and small enough that the derived duration stays finite; the previous tempo was retained.";
                        break;
                    case SetSpeedMode.Multiplier when multiplier is { } multiplierValue
                                                        && double.IsFinite(multiplierValue)
                                                        && multiplierValue > 0:
                        var product = currentBpm * multiplierValue;
                        if (IsUsableTempo(product))
                        {
                            candidateBpm = product;
                            applied = true;
                        }
                        else
                        {
                            rejection = $"SetSpeed multiplier {multiplierValue} would produce a non-finite or underflowed BPM from {currentBpm}; the previous tempo was retained.";
                        }

                        break;
                    case SetSpeedMode.Multiplier:
                        rejection = "SetSpeed multiplier must be a finite positive value; the previous tempo was retained.";
                        break;
                    case SetSpeedMode.Unknown:
                        break;
                }

                tempoEvents.Add(new TempoEvent(
                    TempoEventKind.AdoFaiSetSpeed,
                    floorStartTime + elapsed,
                    provenance,
                    // Only an applied event exposes its canonical value; a
                    // rejected one stays canonical-empty so an illegal value
                    // cannot masquerade as the effective tempo.
                    Bpm: applied && mode == SetSpeedMode.Bpm ? candidateBpm : null,
                    SpeedMode: mode,
                    Multiplier: applied && mode == SetSpeedMode.Multiplier ? multiplier : null,
                    AngleOffsetDegrees: offset,
                    RawData: RawText(action.Raw),
                    Application: applied ? TempoEventApplication.Applied : TempoEventApplication.Rejected,
                    ApplicationOrder: applicationOrder++));

                if (applied)
                {
                    currentBpm = candidateBpm;
                    continue;
                }

                if (mode == SetSpeedMode.Unknown)
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticSeverity.Warning,
                        "ADF-SPEED-TYPE",
                        $"Unknown SetSpeed.speedType '{modeText}' was preserved but did not alter timing.",
                        provenance));
                    continue;
                }

                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Error,
                    AdofaiSpeedValueCode,
                    rejection!,
                    provenance));
            }

            previousOffset = offset;
        }

        if (floorAngleDegrees > previousOffset)
        {
            var segmentStart = floorStartTime + elapsed;
            elapsed += (floorAngleDegrees - previousOffset) / 180.0 * 60.0 / currentBpm;
            tempoSegments.Add(new TempoSegment(
                segmentStart,
                floorStartTime + elapsed,
                currentBpm,
                StartAngleDegrees: previousOffset,
                EndAngleDegrees: floorAngleDegrees));
        }

        return new FloorTiming(elapsed, currentBpm, applicationOrder);
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

    private sealed record FloorTiming(double DurationSeconds, double EndBpm, int EndApplicationOrder);
}
