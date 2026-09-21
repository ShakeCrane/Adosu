using System.Globalization;
using Adosu.Core.Model;
using Adosu.Core.Parsing;
using Adosu.Core.Timing;

return TestRunner.Run();

internal static class TestRunner
{
    private static readonly (string Name, Action Test)[] Tests =
    [
        ("ADOFAI path mapping, SetSpeed and state preservation", AdoFaiMicroFixture),
        ("ADOFAI angleData keeps explicit 999 midspin", AdoFaiAngleDataMidspin),
        ("ADOFAI repeated direction never creates a zero-time pileup", RepeatedDirection),
        ("ADOFAI non-zero angleOffset stays piecewise", AdoFaiAngleOffset),
        ("Twirl survives presentation stripping", TwirlIsNotVfx),
        ("ADOFAI same-floor SetSpeed Bpm then Multiplier ordering", SameFloorBpmThenMultiplier),
        ("ADOFAI same-floor SetSpeed Multiplier then Bpm ordering", SameFloorMultiplierThenBpm),
        ("ADOFAI two non-zero angleOffsets stay piecewise", TwoAngleOffsetsPiecewise),
        ("ADOFAI midspin adjacent to Twirl", MidspinAdjacentTwirl),
        ("ADOFAI same-floor mixed gameplay state keeps source order", GameplayStateSourceOrder),
        ("ADOFAI unclassified action is not silent presentation", UnclassifiedActionIsNotVfx),
        ("ADOFAI cross-floor same-timestamp state keeps source order", CrossFloorSameTimestampSourceOrder),
        ("ADOFAI cross-floor same-time Twirl applies floor order", CrossFloorSameTimeTwirlApplicationOrder),
        ("ADOFAI cross-floor same-time SetSpeed applies floor order", CrossFloorSameTimeSetSpeedApplicationOrder),
        ("ADOFAI SetSpeed rejects non-finite BPM", SetSpeedRejectsNonFiniteBpm),
        ("ADOFAI SetSpeed rejects non-positive and missing values", SetSpeedRejectsNonPositiveValues),
        ("ADOFAI SetSpeed rejects overflow and underflow products", SetSpeedRejectsOverflowAndUnderflow),
        ("ADOFAI base BPM must keep its reciprocal finite", BaseBpmReciprocalMustBeFinite),
        ("ADOFAI non-finite angleOffset is an explicit illegal value", NonFiniteAngleOffsetIsIllegal),
        ("ADOFAI missing or out-of-range floor is not dropped", MalformedFloorIndexIsNotDropped),
        ("ADOFAI non-object action is retained, not discarded", NonObjectActionIsRetained),
        ("ADOFAI Pause duration legality and illegal-pause isolation", PauseDurationLegality),
        ("ADOFAI Pause overflow is rejected without corrupting the time axis", PauseOverflowRejection),
        ("ADOFAI Pause cumulative overflow keeps only the first contribution", PauseCumulativeOverflow),
        ("ADOFAI Pause validates against the canonical floor-end tempo", PauseValidatesAgainstFloorEndTempo),
        ("ADOFAI rejected Pause preserves Twirl and SetSpeed application order", PauseRejectionPreservesApplicationOrder),
        ("ADOFAI Hold duration legality and canonical-empty rejection", HoldDurationLegality),
        ("ADOFAI FreeRoam duration legality and canonical-empty rejection", FreeRoamDurationLegality),
        ("ADOFAI illegal Pause leaves Twirl and SetSpeed consistent", IllegalPauseKeepsLaterState),
        ("ADOFAI underflowed segment times stay finite, ordered and non-decreasing", UnderflowSegmentPrecisionLimitation),
        ("ADOFAI Pause, state and tempo preserve source application order", PauseSourceApplicationOrder),
        ("ADOFAI rejected Pauses retain reserved source ordinals", RejectedPauseSourceApplicationOrder),
        ("ADOFAI inferred Pause provenance identifies accepted contribution", PauseInferredDiagnosticProvenance),
        ("ADOFAI cumulative floor travel overflow fails closed", CumulativeFloorTravelOverflow),
        ("osu!mania lanes, taps, LN, chords and separate SV", ManiaMicroFixture),
        ("source decimal precision and same-timestamp order", PrecisionAndOrdering),
        ("real ADOFAI fixture regression statistics", RealAdoFaiFixture),
        ("real osu!mania SV fixture regression statistics", RealManiaFixture)
    ];

    public static int Run()
    {
        var failures = 0;
        foreach (var (name, test) in Tests)
        {
            try
            {
                test();
                Console.WriteLine($"PASS {name}");
            }
            catch (Exception exception)
            {
                failures++;
                Console.Error.WriteLine($"FAIL {name}: {exception.Message}");
            }
        }

        Console.WriteLine($"{Tests.Length - failures}/{Tests.Length} tests passed.");
        return failures == 0 ? 0 : 1;
    }

    private static void AdoFaiMicroFixture()
    {
        const string source = """
            {
              "pathData": "RR!",
              "settings": { "bpm": 60, "offset": 10 },
              "actions": [
                { "floor": 1, "eventType": "Twirl" },
                { "floor": 1, "eventType": "SetSpeed", "speedType": "Bpm", "beatsPerMinute": 120, "bpmMultiplier": 1, "angleOffset": 0 },
                { "floor": 2, "eventType": "SetSpeed", "speedType": "Multiplier", "beatsPerMinute": 100, "bpmMultiplier": 0.5, "angleOffset": 90 },
                { "floor": 0, "eventType": "Flash", "duration": 1 },
                { "floor": 2, "eventType": "Multitap" }
              ]
            }
            """;

        var chart = AdofaiReader.Parse(source);
        Assert.Equal(SourceFormat.AdoFai, chart.SourceFormat);
        Assert.Equal(4, chart.InputTrack.Events.Count);
        Assert.Equal(3, chart.Metadata.DirectionTokens!.Count);
        Assert.True(chart.Metadata.DirectionTokens[2].IsMidspin, "! must remain a midspin token");
        Assert.Equal(2, chart.TempoTrack.Events.Count);
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, chart.SourceEvents.Select(sourceEvent => sourceEvent.SourceIndex));
        Assert.Equal(120, chart.TempoTrack.Events[0].Bpm!.Value, 3);
        Assert.Equal(SetSpeedMode.Bpm, chart.TempoTrack.Events[0].SpeedMode!.Value);
        Assert.Equal(SetSpeedMode.Multiplier, chart.TempoTrack.Events[1].SpeedMode!.Value);
        Assert.Equal(1, chart.GameplayState.Changes.Count(change => change.Kind == GameplayStateChangeKind.Twirl));
        Assert.Equal(1, chart.GameplayState.Changes.Count(change => change.Kind == GameplayStateChangeKind.Multitap));
        Assert.Equal(1, chart.ScrollPresentationTrack.PresentationEvents.Count);
        Assert.True(chart.Diagnostics.Any(diagnostic => diagnostic.Code == "ADF-MULTITAP-UNKNOWN"));
        Assert.True(chart.TempoTrack.Segments.Count > 0, "tempo segments should retain piecewise timing");
        Assert.True(chart.InputTrack.Events.All(input => !double.IsNaN(input.TimeSeconds)));
    }

    private static void RepeatedDirection()
    {
        // "RR" advances the same +180 travel on every floor; it never reaches
        // the near-zero/full-circle fallback. Under the current path mapping
        // "RL" produces a 0-degree entry-to-exit delta on floor 1, which must
        // fall back to a full 360-degree travel instead of a zero-duration
        // note. Twirl on the same floor must not create a zero-time pileup.
        var withoutTwirl = AdofaiReader.Parse("""
            { "pathData": "RL", "settings": { "bpm": 60, "offset": 0 }, "actions": [] }
            """);

        var times = withoutTwirl.InputTrack.Events.Select(input => input.TimeSeconds).ToArray();
        Assert.Equal(3, times.Length);
        Assert.True(times[0] == 0, "floor entry starts at time zero");
        Assert.Equal(1.0, times[1], 9);
        // 360 degrees of travel at 60 BPM is 2.0 seconds, not zero.
        Assert.Equal(3.0, times[2], 9);
        Assert.True(times[1] > times[0] && times[2] > times[1], "repeated direction must advance time");
        Assert.True(times.All(double.IsFinite));

        var fullCircleSegment = withoutTwirl.TempoTrack.Segments
            .Single(segment => segment.StartTimeSeconds == 1.0 && segment.EndTimeSeconds == 3.0);
        Assert.Equal(360.0, fullCircleSegment.EndAngleDegrees!.Value, 6);
        Assert.Equal(60.0, fullCircleSegment.Bpm, 9);

        var withTwirl = AdofaiReader.Parse("""
            {
              "pathData": "RL",
              "settings": { "bpm": 60, "offset": 0 },
              "actions": [ { "floor": 1, "eventType": "Twirl" } ]
            }
            """);

        var twirlTimes = withTwirl.InputTrack.Events.Select(input => input.TimeSeconds).ToArray();
        Assert.Equal(1, withTwirl.GameplayState.Changes.Count(change => change.Kind == GameplayStateChangeKind.Twirl));
        Assert.Equal(times.Length, twirlTimes.Length);
        // Twirl still resolves the 0-delta floor to a real 360-degree turn
        // here; the key invariant is that no floor collapses to zero time.
        Assert.Equal(
            times.Zip(twirlTimes, (before, after) => Math.Abs(before - after) < 1e-9).All(same => same),
            true);
        Assert.True(withTwirl.TempoTrack.Segments.All(segment => segment.EndTimeSeconds > segment.StartTimeSeconds),
            "no adjacent floor may collapse to a zero-duration segment");
    }

    private static void AdoFaiAngleDataMidspin()
    {
        var chart = AdofaiReader.Parse("""
            {
              "angleData": [0, 999],
              "settings": { "bpm": 60, "offset": 0 },
              "actions": []
            }
            """);

        Assert.Equal(3, chart.InputTrack.Events.Count);
        Assert.Equal(999, chart.Metadata.DirectionTokens![1].AngleDegrees, 9);
        Assert.True(chart.Metadata.DirectionTokens[1].IsMidspin);
        Assert.Equal("999", chart.Metadata.DirectionTokens[1].Provenance.RawValue);
    }

    private static void AdoFaiAngleOffset()
    {
        var chart = AdofaiReader.Parse("""
            {
              "pathData": "RE",
              "settings": { "bpm": 60, "offset": 0 },
              "actions": [
                { "floor": 1, "eventType": "SetSpeed", "speedType": "Multiplier", "beatsPerMinute": 100, "bpmMultiplier": 2, "angleOffset": 45 }
              ]
            }
            """);

        Assert.Equal(1, chart.TempoTrack.Events.Count);
        Assert.Equal(45, chart.TempoTrack.Events[0].AngleOffsetDegrees!.Value, 9);
        Assert.True(chart.TempoTrack.Segments.Count >= 3, "an intra-floor speed change must create piecewise segments");
        Assert.True(chart.TempoTrack.Events[0].TimeSeconds > chart.InputTrack.Events[1].TimeSeconds);
    }

    private static void TwirlIsNotVfx()
    {
        var withoutTwirl = AdofaiReader.Parse("""
            { "pathData": "RE", "settings": { "bpm": 60, "offset": 0 }, "actions": [] }
            """);
        var chart = AdofaiReader.Parse("""
            {
              "pathData": "RE",
              "settings": { "bpm": 60, "offset": 0 },
              "actions": [
                { "floor": 1, "eventType": "Twirl" },
                { "floor": 1, "eventType": "Flash", "duration": 2 }
              ]
            }
            """);

        var stripped = chart.WithoutPresentationEvents();
        Assert.Equal(0, stripped.ScrollPresentationTrack.PresentationEvents.Count);
        Assert.Equal(1, stripped.GameplayState.Changes.Count(change => change.Kind == GameplayStateChangeKind.Twirl));
        Assert.Equal(
            chart.InputTrack.Events.Select(input => input.TimeSeconds),
            stripped.InputTrack.Events.Select(input => input.TimeSeconds));
        Assert.True(
            Math.Abs(withoutTwirl.InputTrack.Events[2].TimeSeconds - chart.InputTrack.Events[2].TimeSeconds) > 1e-9,
            "Twirl must participate in the gameplay/timing state before VFX stripping");
    }

    private static void SameFloorBpmThenMultiplier()
    {
        // Both actions share angleOffset 90 on the same floor and must be
        // applied in source order: at the boundary the BPM 60 replaces the
        // base tempo, then x2 doubles it to 120. Floor 0 is 180 degrees of
        // travel: the first 90 stays at the base 120 BPM, the second 90 is at
        // the resulting 120 BPM. (Final tempo 120 is the same either way, so
        // the source-order proof here is provenance plus event timing.)
        var chart = AdofaiReader.Parse("""
            {
              "pathData": "R",
              "settings": { "bpm": 120, "offset": 0 },
              "actions": [
                { "floor": 0, "eventType": "SetSpeed", "speedType": "Bpm", "beatsPerMinute": 60, "angleOffset": 90 },
                { "floor": 0, "eventType": "SetSpeed", "speedType": "Multiplier", "bpmMultiplier": 2, "angleOffset": 90 }
              ]
            }
            """);

        Assert.Equal(2, chart.TempoTrack.Events.Count);
        Assert.Equal(
            new[] { SetSpeedMode.Bpm, SetSpeedMode.Multiplier },
            chart.TempoTrack.Events.Select(eventInfo => eventInfo.SpeedMode!.Value));
        Assert.Equal(new[] { 0, 1 }, chart.TempoTrack.Events.Select(eventInfo => eventInfo.Provenance.SourceIndex));
        Assert.True(chart.TempoTrack.Events[0].Provenance.SourceIndex < chart.TempoTrack.Events[1].Provenance.SourceIndex,
            "same-offset SetSpeed must keep source order");
        Assert.Equal(90.0, chart.TempoTrack.Events[0].AngleOffsetDegrees!.Value, 9);
        Assert.Equal(90.0, chart.TempoTrack.Events[1].AngleOffsetDegrees!.Value, 9);
        // Both events sit at the same real time; the first half is at 120 BPM.
        Assert.Equal(0.25, chart.TempoTrack.Events[0].TimeSeconds, 9);
        Assert.Equal(0.25, chart.TempoTrack.Events[1].TimeSeconds, 9);
        // 90 at 120 BPM (0.25) + 90 at 120 BPM (0.25); floor 1 is 180 degrees
        // at the resulting 120 BPM (0.5), so the second floor's entry is 1.0.
        Assert.Equal(0.5, chart.InputTrack.Events[1].TimeSeconds, 9);
        Assert.Equal(120.0, chart.Metadata.BaseBpm!.Value, 9);
    }

    private static void SameFloorMultiplierThenBpm()
    {
        // Reverse source order on the same offset: base 120 x0.5 = 60, then
        // BPM 60. Final tempo must be 60, regardless of the earlier multiplier.
        var chart = AdofaiReader.Parse("""
            {
              "pathData": "R",
              "settings": { "bpm": 120, "offset": 0 },
              "actions": [
                { "floor": 0, "eventType": "SetSpeed", "speedType": "Multiplier", "bpmMultiplier": 0.5, "angleOffset": 90 },
                { "floor": 0, "eventType": "SetSpeed", "speedType": "Bpm", "beatsPerMinute": 60, "angleOffset": 90 }
              ]
            }
            """);

        Assert.Equal(2, chart.TempoTrack.Events.Count);
        Assert.Equal(
            new[] { SetSpeedMode.Multiplier, SetSpeedMode.Bpm },
            chart.TempoTrack.Events.Select(eventInfo => eventInfo.SpeedMode!.Value));
        Assert.True(chart.TempoTrack.Events[0].Provenance.SourceIndex < chart.TempoTrack.Events[1].Provenance.SourceIndex,
            "same-offset SetSpeed must keep source order");
        // 90 at 120 BPM before the boundary, then 90 at 60 BPM after it.
        Assert.Equal(0.25, chart.TempoTrack.Events[0].TimeSeconds, 9);
        Assert.Equal(0.25, chart.TempoTrack.Events[1].TimeSeconds, 9);
        Assert.Equal(0.25 + 0.5, chart.InputTrack.Events[1].TimeSeconds, 9);
    }

    private static void TwoAngleOffsetsPiecewise()
    {
        // 180-degree floor with two non-zero offsets: 0-60 at 60 BPM,
        // 60-120 at 120 BPM, 120-180 back at 60 BPM.
        var chart = AdofaiReader.Parse("""
            {
              "pathData": "R",
              "settings": { "bpm": 60, "offset": 0 },
              "actions": [
                { "floor": 0, "eventType": "SetSpeed", "speedType": "Multiplier", "bpmMultiplier": 2, "angleOffset": 60 },
                { "floor": 0, "eventType": "SetSpeed", "speedType": "Multiplier", "bpmMultiplier": 0.5, "angleOffset": 120 }
              ]
            }
            """);

        Assert.Equal(2, chart.TempoTrack.Events.Count);
        Assert.Equal(60.0, chart.TempoTrack.Events[0].AngleOffsetDegrees!.Value, 9);
        Assert.Equal(120.0, chart.TempoTrack.Events[1].AngleOffsetDegrees!.Value, 9);
        Assert.Equal(1.0 / 3.0, chart.TempoTrack.Events[0].TimeSeconds, 9);
        Assert.Equal(0.5, chart.TempoTrack.Events[1].TimeSeconds, 9);

        var intraFloor = chart.TempoTrack.Segments
            .Where(segment => segment.StartTimeSeconds < chart.InputTrack.Events[1].TimeSeconds
                              && segment.EndTimeSeconds <= chart.InputTrack.Events[1].TimeSeconds + 1e-9)
            .ToList();
        Assert.Equal(3, intraFloor.Count);
        Assert.Equal(60.0, intraFloor[0].Bpm, 9);
        Assert.Equal(120.0, intraFloor[1].Bpm, 9);
        Assert.Equal(60.0, intraFloor[2].Bpm, 9);
        Assert.Equal(0.0, intraFloor[0].StartAngleDegrees!.Value, 9);
        Assert.Equal(60.0, intraFloor[1].StartAngleDegrees!.Value, 9);
        Assert.Equal(120.0, intraFloor[2].StartAngleDegrees!.Value, 9);
        Assert.Equal(180.0, intraFloor[2].EndAngleDegrees!.Value, 9);
        Assert.Equal(1.0 / 3.0 + 1.0 / 6.0 + 1.0 / 3.0, chart.InputTrack.Events[1].TimeSeconds, 9);
    }

    private static void MidspinAdjacentTwirl()
    {
        // Floor 1 and floor 2 are midspins (999). A midspin has zero travel
        // regardless of Twirl, so those floors must not add tempo time, while
        // the surrounding non-midspin floors keep their normal travel. Twirl
        // on the adjacent floor still applies to the surrounding geometry.
        var chart = AdofaiReader.Parse("""
            {
              "pathData": "R!!R",
              "settings": { "bpm": 60, "offset": 0 },
              "actions": [ { "floor": 0, "eventType": "Twirl" } ]
            }
            """);

        var directions = chart.Metadata.DirectionTokens!;
        Assert.Equal(4, directions.Count);
        Assert.True(directions[1].IsMidspin && directions[2].IsMidspin, "both ! tokens must remain midspins");
        Assert.Equal("!", directions[1].Provenance.RawValue);
        Assert.Equal(1, directions[1].Provenance.SourceIndex);

        var events = chart.InputTrack.Events;
        Assert.Equal(5, events.Count);

        // Midspin floors contribute no travel; they must not create a
        // *positive-duration* segment, and the non-midspin floors keep their
        // normal 180-degree travel.
        var nonZeroSegments = chart.TempoTrack.Segments
            .Where(segment => segment.EndAngleDegrees > segment.StartAngleDegrees)
            .ToList();
        Assert.Equal(3, nonZeroSegments.Count);
        Assert.True(nonZeroSegments.All(segment => Math.Abs(segment.EndAngleDegrees!.Value - segment.StartAngleDegrees!.Value - 180.0) < 1e-6),
            "non-midspin floors must keep 180-degree travel");
        Assert.True(chart.TempoTrack.Segments.All(segment => segment.EndTimeSeconds >= segment.StartTimeSeconds),
            "no segment may run backwards in time");

        // The surrounding real floors still advance by one second each at
        // 60 BPM, and the midspin floors collapse onto the same time.
        Assert.Equal(1.0, events[1].TimeSeconds, 9);
        Assert.Equal(2.0, events[4].TimeSeconds, 9);
        Assert.True(events[4].TimeSeconds > events[1].TimeSeconds, "the last floor must advance past the midspin run");

        // Directions keep source order and the Twirl provenance stays on
        // floor 0 even though it is adjacent to midspins.
        Assert.Equal(new[] { 0, 1, 2, 3 }, directions.Select(direction => direction.Provenance.SourceIndex));
        var twirlChange = chart.GameplayState.Changes.Single(change => change.Kind == GameplayStateChangeKind.Twirl);
        Assert.Equal(0, twirlChange.Provenance.SourceIndex);
        Assert.Equal(0, twirlChange.Provenance.FloorIndex);
        Assert.Equal(1, chart.GameplayState.Changes.Count);
    }

    private static void GameplayStateSourceOrder()
    {
        // Mixed action types on one floor must keep source order in the
        // semantic GameplayState track, not be grouped by action type.
        var chart = AdofaiReader.Parse("""
            {
              "pathData": "RE",
              "settings": { "bpm": 60, "offset": 0 },
              "actions": [
                { "floor": 0, "eventType": "Hold", "duration": 1 },
                { "floor": 0, "eventType": "Twirl" },
                { "floor": 0, "eventType": "Pause", "duration": 2 },
                { "floor": 0, "eventType": "MultiPlanet", "planets": 3 },
                { "floor": 0, "eventType": "Flash", "duration": 1 }
              ]
            }
            """);

        Assert.Equal(
            new[]
            {
                GameplayStateChangeKind.Hold,
                GameplayStateChangeKind.Twirl,
                GameplayStateChangeKind.Pause,
                GameplayStateChangeKind.MultiPlanet
            },
            chart.GameplayState.Changes.Select(change => change.Kind));
        Assert.Equal(
            new[] { 0, 1, 2, 3 },
            chart.GameplayState.Changes.Select(change => change.Provenance.SourceIndex));
        Assert.True(
            chart.GameplayState.Changes.Zip(
                    chart.GameplayState.Changes.Skip(1),
                    (first, second) => first.Provenance.SourceIndex < second.Provenance.SourceIndex)
                .All(ordered => ordered),
            "GameplayState must preserve source order across action types");
        Assert.Equal(1, chart.ScrollPresentationTrack.PresentationEvents.Count);
        Assert.Equal("Flash", chart.ScrollPresentationTrack.PresentationEvents[0].EventType);
    }

    private static void UnclassifiedActionIsNotVfx()
    {
        // AutoPlayTiles is not on the explicit presentation whitelist and has
        // no modeled semantics. It must not be silently classified as a
        // removable presentation event; it stays as unknown gameplay state
        // with provenance and a stable diagnostic.
        var chart = AdofaiReader.Parse("""
            {
              "pathData": "R",
              "settings": { "bpm": 60, "offset": 0 },
              "actions": [
                { "floor": 0, "eventType": "AutoPlayTiles", "enabled": "Enabled" },
                { "floor": 0, "eventType": "Flash" }
              ]
            }
            """);

        var unknown = chart.GameplayState.Changes.Single();
        Assert.Equal(GameplayStateChangeKind.Unknown, unknown.Kind);
        Assert.Equal("AutoPlayTiles", unknown.EventType);
        Assert.Equal(0, unknown.Provenance.SourceIndex);
        Assert.True(unknown.RawData!.Contains("AutoPlayTiles"), "raw source record must be preserved");
        Assert.True(chart.Diagnostics.Any(diagnostic => diagnostic.Code == "ADF-ACTION-UNCLASSIFIED"
                                                       && diagnostic.Severity == DiagnosticSeverity.Warning),
            "an unclassified action must produce a stable warning diagnostic");
        Assert.Equal(1, chart.ScrollPresentationTrack.PresentationEvents.Count);
        Assert.Equal("Flash", chart.ScrollPresentationTrack.PresentationEvents[0].EventType);

        // The real presentation whitelist still strips cleanly.
        var stripped = chart.WithoutPresentationEvents();
        Assert.Equal(0, stripped.ScrollPresentationTrack.PresentationEvents.Count);
        Assert.Equal(1, stripped.GameplayState.Changes.Count);
        Assert.Equal(GameplayStateChangeKind.Unknown, stripped.GameplayState.Changes[0].Kind);
    }

    private static void CrossFloorSameTimestampSourceOrder()
    {
        // pathData "R!" makes floor 0 a normal floor and floor 1 a midspin.
        // The midspin has zero travel, so floor 1 starts at the same real time
        // as floor 0's end. Two actions are placed so their floor order is the
        // reverse of the resolved real-time order.
        var chart = AdofaiReader.Parse("""
            {
              "pathData": "R!",
              "settings": { "bpm": 60, "offset": 0 },
              "actions": [
                { "floor": 1, "eventType": "Twirl" },
                { "floor": 0, "eventType": "MultiPlanet", "planets": 3 }
              ]
            }
            """);

        var crossFloorPair = chart.GameplayState.Changes
            .Where(change => change.Kind is GameplayStateChangeKind.Twirl
                or GameplayStateChangeKind.MultiPlanet)
            .ToList();
        Assert.Equal(2, crossFloorPair.Count);

        // Twirl sits on floor 1 (time 1.0); MultiPlanet sits on floor 0
        // (time 0.0). Real time ordering therefore puts MultiPlanet (source 1)
        // before Twirl (source 0), proving the derived track is ordered by
        // time first and uses SourceIndex only as the stable tie-break.
        Assert.True(
            crossFloorPair[0].Provenance.SourceIndex == 1
            && crossFloorPair[1].Provenance.SourceIndex == 0,
            "cross-floor derived state must be ordered by real time, not source index");
        Assert.True(crossFloorPair[0].TimeSeconds < crossFloorPair[1].TimeSeconds);

        // The presentation order above is display only: the semantic
        // application order must stay floor traversal then same-floor source
        // order (MultiPlanet on floor 0 before Twirl on floor 1). Read in
        // presentation order the application ordinals are therefore (0, 1),
        // not the SourceIndex order (1, 0).
        var applicationOrder = chart.GameplayState.Changes
            .Where(change => change.Kind is GameplayStateChangeKind.Twirl
                or GameplayStateChangeKind.MultiPlanet)
            .Select(change => change.ApplicationOrder);
        Assert.Equal(new[] { 0, 1 }, applicationOrder);
        Assert.Equal(
            new[] { 1, 0 },
            chart.GameplayState.ChangesInApplicationOrder
                .Where(change => change.Kind is GameplayStateChangeKind.Twirl
                    or GameplayStateChangeKind.MultiPlanet)
                .Select(change => change.Provenance.SourceIndex));
    }

    private static void CrossFloorSameTimeTwirlApplicationOrder()
    {
        // "R!!R" resolves floor 1 and floor 2 as zero-travel midspins, so both
        // floors start at the same real time 1.0s. The source order is the
        // inverse of the floor traversal order: source 0 is on floor 2 and
        // source 1 is on floor 1. State must be applied in floor order, not in
        // provenance or presentation order.
        var chart = AdofaiReader.Parse("""
            {
              "pathData": "R!!R",
              "settings": { "bpm": 60, "offset": 0 },
              "actions": [
                { "floor": 2, "eventType": "Twirl" },
                { "floor": 1, "eventType": "Twirl" }
              ]
            }
            """);

        var twirls = chart.GameplayState.Changes
            .Where(change => change.Kind == GameplayStateChangeKind.Twirl)
            .ToList();
        Assert.Equal(2, twirls.Count);
        Assert.True(twirls.All(change => change.TimeSeconds == 1.0),
            "both midspin-floor Twirls must share the same resolved real time");

        // Floor 1's Twirl (source 1) is applied first and turns Twirl on; floor
        // 2's Twirl (source 0) is applied second and turns it back off.
        var floorOne = twirls.Single(change => change.Provenance.FloorIndex == 1);
        var floorTwo = twirls.Single(change => change.Provenance.FloorIndex == 2);
        Assert.Equal(1, floorOne.Provenance.SourceIndex);
        Assert.Equal(0, floorTwo.Provenance.SourceIndex);
        Assert.True(floorOne.TwirlStateAfter == true, "the first applied Twirl must turn Twirl on");
        Assert.True(floorTwo.TwirlStateAfter == false, "the second applied Twirl must turn Twirl back off");

        // The presentation order is deterministic but must not decide state.
        Assert.Equal(0, chart.GameplayState.Changes[0].Provenance.SourceIndex);
        Assert.Equal(1, chart.GameplayState.Changes[1].Provenance.SourceIndex);

        var application = chart.GameplayState.ChangesInApplicationOrder
            .Where(change => change.Kind == GameplayStateChangeKind.Twirl)
            .ToList();
        Assert.Equal(new[] { 1, 0 }, application.Select(change => change.Provenance.SourceIndex));
        Assert.True(application[0].ApplicationOrder < application[1].ApplicationOrder,
            "floor traversal must order the application track, not SourceIndex");

        // TwirlAt(t) must equal the state after applying every event at or
        // before t, and the true final state after the same-time pair is off.
        Assert.True(!chart.GameplayState.TwirlAt(1.0), "R!!R with an inverse-order Twirl pair must end non-twirled");
        Assert.True(!chart.GameplayState.TwirlAt(2.0), "the post-run state must stay non-twirled");
        Assert.True(!chart.GameplayState.TwirlAt(0.5), "Twirl before the pair must be off");

        // Later geometry must be consistent with the final non-twirled state:
        // the trailing "R" floor still advances 180 degrees of travel, so the
        // time axis is not silent-corrupted by the same-time pair.
        var inputs = chart.InputTrack.Events;
        Assert.Equal(5, inputs.Count);
        Assert.Equal(1.0, inputs[1].TimeSeconds, 9);
        Assert.Equal(2.0, inputs[4].TimeSeconds, 9);
        Assert.True(inputs.All(input => double.IsFinite(input.TimeSeconds)));
    }

    private static void CrossFloorSameTimeSetSpeedApplicationOrder()
    {
        // Same "R!!R" same-time structure, now for tempo state. Floor 1's
        // source 1 (120 BPM) must be applied before floor 2's source 0 (30
        // BPM), so the canonical effective BPM after time 1.0 is 30, regardless
        // of the presentation (SourceIndex) order.
        var chart = AdofaiReader.Parse("""
            {
              "pathData": "R!!R",
              "settings": { "bpm": 60, "offset": 0 },
              "actions": [
                { "floor": 2, "eventType": "SetSpeed", "speedType": "Bpm", "beatsPerMinute": 30 },
                { "floor": 1, "eventType": "SetSpeed", "speedType": "Bpm", "beatsPerMinute": 120 }
              ]
            }
            """);

        Assert.Equal(2, chart.TempoTrack.Events.Count);
        var events = chart.TempoTrack.Events;
        Assert.Equal(new[] { 0, 1 }, events.Select(eventInfo => eventInfo.Provenance.SourceIndex));
        Assert.True(events[0].TimeSeconds == events[1].TimeSeconds,
            "the two midspin-floor SetSpeed events share the same real time");
        Assert.Equal(new[] { 1, 0 }, events.Select(eventInfo => eventInfo.ApplicationOrder));
        var appliedByApplication = events
            .OrderBy(eventInfo => eventInfo.ApplicationOrder)
            .ToList();
        Assert.Equal(new[] { 1, 0 }, appliedByApplication.Select(eventInfo => eventInfo.Provenance.SourceIndex));

        // Segments must follow the canonical application order: floor 0 at the
        // base 60 BPM, then the same-time pair resolving to 30 BPM.
        var postPair = chart.TempoTrack.Segments
            .Where(segment => segment.StartTimeSeconds >= 1.0)
            .ToList();
        Assert.Equal(2, postPair.Count);
        Assert.True(postPair.All(segment => Math.Abs(segment.Bpm - 30.0) < 1e-9),
            "display order must not let the source 0 event decide canonical BPM");
        Assert.True(postPair.All(segment => double.IsFinite(segment.Bpm)));
        // Floor 0 before the same-time pair runs at the base 60 BPM.
        Assert.Equal(60.0, chart.TempoTrack.Segments[0].Bpm, 9);
        Assert.Equal(1.0, chart.TempoTrack.Segments[0].EndTimeSeconds, 9);
    }

    private static void SetSpeedRejectsNonFiniteBpm()
    {
        // 1e309 is valid JSON but overflows double.Parse to +Infinity. It must
        // be rejected with a stable diagnostic, a canonical-empty event and an
        // untouched tempo/time axis - not silently adopted as +Infinity.
        var chart = AdofaiReader.Parse("""
            {
              "pathData": "R",
              "settings": { "bpm": 60, "offset": 0 },
              "actions": [
                { "floor": 0, "eventType": "SetSpeed", "speedType": "Bpm", "beatsPerMinute": 1e309 }
              ]
            }
            """);

        var tempoEvent = chart.TempoTrack.Events.Single();
        Assert.Equal(TempoEventApplication.Rejected, tempoEvent.Application);
        Assert.True(tempoEvent.Bpm is null, "a rejected BPM must stay canonical-empty");
        Assert.True(tempoEvent.RawData!.Contains("1e309"), "the raw source record must be preserved");
        Assert.Equal(0, tempoEvent.Provenance.SourceIndex);
        Assert.True(chart.Diagnostics.Any(diagnostic =>
                diagnostic.Code == "ADF-SPEED-VALUE" && diagnostic.Severity == DiagnosticSeverity.Error),
            "rejection must emit the stable ADF-SPEED-VALUE error");

        Assert.True(chart.TempoTrack.Segments.All(segment => double.IsFinite(segment.Bpm) && segment.Bpm > 0));
        Assert.True(chart.TempoTrack.Segments.All(segment =>
            double.IsFinite(segment.StartTimeSeconds) && double.IsFinite(segment.EndTimeSeconds)));
        Assert.True(chart.InputTrack.Events.All(input => double.IsFinite(input.TimeSeconds)));
        // The previous tempo (60 BPM) must remain: 180 degrees is 1.0 second.
        Assert.Equal(60.0, chart.TempoTrack.Segments[0].Bpm, 9);
        Assert.Equal(1.0, chart.InputTrack.Events[1].TimeSeconds, 9);
    }

    private static void SetSpeedRejectsNonPositiveValues()
    {
        // Zero, negative and missing BPM/multiplier are all illegal. Each must
        // be rejected, keep its raw provenance, leave the previous tempo in
        // place, and never fabricate a zero-duration segment.
        var chart = AdofaiReader.Parse("""
            {
              "pathData": "RR",
              "settings": { "bpm": 60, "offset": 0 },
              "actions": [
                { "floor": 0, "eventType": "SetSpeed", "speedType": "Bpm", "beatsPerMinute": 0 },
                { "floor": 0, "eventType": "SetSpeed", "speedType": "Multiplier", "bpmMultiplier": -2 },
                { "floor": 0, "eventType": "SetSpeed", "speedType": "Bpm" }
              ]
            }
            """);

        var events = chart.TempoTrack.Events;
        Assert.Equal(3, events.Count);
        Assert.True(events.All(eventInfo => eventInfo.Application == TempoEventApplication.Rejected),
            "zero, negative and missing values must all be rejected");
        Assert.True(events.All(eventInfo => eventInfo.Bpm is null && eventInfo.Multiplier is null),
            "rejected events must not expose a canonical value");
        Assert.True(events.All(eventInfo => eventInfo.RawData is not null),
            "rejected events must keep their raw source record");
        Assert.Equal(new[] { 0, 1, 2 }, events.Select(eventInfo => eventInfo.Provenance.SourceIndex));
        Assert.Equal(3, chart.Diagnostics.Count(diagnostic => diagnostic.Code == "ADF-SPEED-VALUE"));

        Assert.True(chart.TempoTrack.Segments.All(segment => segment.Bpm == 60.0),
            "every segment must keep the previous 60 BPM");
        Assert.True(chart.TempoTrack.Segments.All(segment => segment.EndTimeSeconds > segment.StartTimeSeconds),
            "no rejection may fabricate a zero-duration segment");
        Assert.Equal(1.0, chart.InputTrack.Events[1].TimeSeconds, 9);
        Assert.Equal(2.0, chart.InputTrack.Events[2].TimeSeconds, 9);
    }

    private static void SetSpeedRejectsOverflowAndUnderflow()
    {
        // A finite multiplier can still overflow the canonical product
        // (1e308 * 10 = +Infinity) or underflow it below the usable BPM floor
        // (1e-300 * 0.5). Both must be rejected instead of entering canonical
        // tempo, while the previous finite positive tempo is retained.
        var overflow = AdofaiReader.Parse("""
            {
              "pathData": "R",
              "settings": { "bpm": 1e308, "offset": 0 },
              "actions": [
                { "floor": 0, "eventType": "SetSpeed", "speedType": "Multiplier", "bpmMultiplier": 10 }
              ]
            }
            """);

        var overflowEvent = overflow.TempoTrack.Events.Single();
        Assert.Equal(TempoEventApplication.Rejected, overflowEvent.Application);
        Assert.True(overflowEvent.Multiplier is null, "the rejecting multiplier must not become canonical");
        Assert.True(overflowEvent.RawData!.Contains("bpmMultiplier"));
        Assert.True(overflow.Diagnostics.Any(diagnostic => diagnostic.Code == "ADF-SPEED-VALUE"));
        Assert.True(overflow.TempoTrack.Segments.All(segment => double.IsFinite(segment.Bpm) && segment.Bpm > 0));
        Assert.True(overflow.TempoTrack.Segments.All(segment => Math.Abs(segment.Bpm - 1e308) < 1e292),
            "the pre-existing finite tempo must be retained");
        Assert.True(overflow.InputTrack.Events.All(input => double.IsFinite(input.TimeSeconds)));

        var underflow = AdofaiReader.Parse("""
            {
              "pathData": "R",
              "settings": { "bpm": 1e-306, "offset": 0 },
              "actions": [
                { "floor": 0, "eventType": "SetSpeed", "speedType": "Multiplier", "bpmMultiplier": 0.5 }
              ]
            }
            """);

        var underflowEvent = underflow.TempoTrack.Events.Single();
        Assert.Equal(TempoEventApplication.Rejected, underflowEvent.Application);
        Assert.True(underflowEvent.Multiplier is null);
        Assert.True(underflow.Diagnostics.Any(diagnostic => diagnostic.Code == "ADF-SPEED-VALUE"));
        Assert.True(underflow.TempoTrack.Segments.All(segment => segment.Bpm > 0));
        // 60/BPM for the underflowed candidate would already be infinite, so
        // the retained tempo must keep every derived time finite.
        Assert.True(underflow.TempoTrack.Segments.All(segment =>
            double.IsFinite(segment.StartTimeSeconds) && double.IsFinite(segment.EndTimeSeconds)));
        Assert.True(underflow.InputTrack.Events.All(input => double.IsFinite(input.TimeSeconds)));

        // A multiplier that merely shrinks a tempo while staying usable is not
        // an underflow: 1e-300 * 0.5 keeps a finite derived duration and must
        // still be applied.
        var shrink = AdofaiReader.Parse("""
            {
              "pathData": "R",
              "settings": { "bpm": 1e-300, "offset": 0 },
              "actions": [
                { "floor": 0, "eventType": "SetSpeed", "speedType": "Multiplier", "bpmMultiplier": 0.5 }
              ]
            }
            """);
        Assert.Equal(TempoEventApplication.Applied, shrink.TempoTrack.Events.Single().Application);
        Assert.Equal(0.5, shrink.TempoTrack.Events.Single().Multiplier!.Value, 9);
        Assert.True(shrink.Diagnostics.All(diagnostic => diagnostic.Code != "ADF-SPEED-VALUE"));
    }

    private static void BaseBpmReciprocalMustBeFinite()
    {
        // A base BPM so small that 60/BPM overflows is not a usable canonical
        // tempo. It must trigger the existing ADF009 guard and fall back to a
        // finite tempo instead of producing infinite segment durations.
        var chart = AdofaiReader.Parse("""
            { "pathData": "R", "settings": { "bpm": 5e-324, "offset": 0 }, "actions": [] }
            """);

        Assert.True(chart.Diagnostics.Any(diagnostic =>
                diagnostic.Code == "ADF009" && diagnostic.Severity == DiagnosticSeverity.Error),
            "an unusable base BPM must keep the ADF009 error");
        Assert.Equal(120.0, chart.Metadata.BaseBpm!.Value, 9);
        Assert.True(chart.TempoTrack.Segments.All(segment => double.IsFinite(segment.Bpm) && segment.Bpm > 0));
        Assert.True(chart.InputTrack.Events.All(input => double.IsFinite(input.TimeSeconds)));
        Assert.Equal(0.5, chart.InputTrack.Events[1].TimeSeconds, 9);
    }

    private static void NonFiniteAngleOffsetIsIllegal()
    {
        // A non-finite angleOffset is an illegal source value, not an ordinary
        // out-of-span boundary. It must emit the stable ADF-SPEED-OFFSET code
        // at error severity while the event itself stays applicable, so the
        // geometry unit mapping and the time axis both stay defined.
        var chart = AdofaiReader.Parse("""
            {
              "pathData": "R",
              "settings": { "bpm": 60, "offset": 0 },
              "actions": [
                { "floor": 0, "eventType": "SetSpeed", "speedType": "Bpm", "beatsPerMinute": 120, "angleOffset": 1e309 }
              ]
            }
            """);

        var offsetDiagnostics = chart.Diagnostics
            .Where(diagnostic => diagnostic.Code == "ADF-SPEED-OFFSET")
            .ToList();
        Assert.Equal(1, offsetDiagnostics.Count);
        Assert.Equal(DiagnosticSeverity.Error, offsetDiagnostics[0].Severity);

        var tempoEvent = chart.TempoTrack.Events.Single();
        Assert.Equal(TempoEventApplication.Applied, tempoEvent.Application);
        Assert.Equal(120.0, tempoEvent.Bpm!.Value, 9);
        // The illegal offset is placed at the floor start; the boundary unit is
        // defined and carries a finite real time.
        Assert.Equal(0.0, tempoEvent.AngleOffsetDegrees!.Value, 9);
        Assert.Equal(0.0, tempoEvent.TimeSeconds, 9);
        Assert.True(tempoEvent.RawData!.Contains("angleOffset"));
        Assert.True(chart.TempoTrack.Segments.All(segment =>
            double.IsFinite(segment.StartTimeSeconds) && double.IsFinite(segment.EndTimeSeconds)));
        Assert.True(chart.InputTrack.Events.All(input => double.IsFinite(input.TimeSeconds)));
    }

    private static void MalformedFloorIndexIsNotDropped()
    {
        // A null floor, a negative floor and a floor beyond the last floor
        // must not vanish from the semantic tracks; each becomes an explicit
        // unknown gameplay state with raw data and a stable ADF-FLOOR-RANGE
        // warning. The in-range Twirl must still be modeled normally.
        var chart = AdofaiReader.Parse("""
            {
              "pathData": "R",
              "settings": { "bpm": 60, "offset": 0 },
              "actions": [
                { "eventType": "Twirl" },
                { "floor": -3, "eventType": "Pause", "duration": 1 },
                { "floor": 99, "eventType": "Multitap" },
                { "floor": 0, "eventType": "Twirl" }
              ]
            }
            """);

        var unknown = chart.GameplayState.Changes
            .Where(change => change.Kind == GameplayStateChangeKind.Unknown)
            .ToList();
        Assert.Equal(3, unknown.Count);
        var nullFloorTwirl = unknown.Single(change => change.EventType == "Twirl");
        Assert.True(nullFloorTwirl.Provenance.FloorIndex is null,
            "a missing floor must stay null in provenance, never -1");
        Assert.Equal(-3, unknown.Single(change => change.EventType == "Pause").Provenance.FloorIndex);
        Assert.Equal(99, unknown.Single(change => change.EventType == "Multitap").Provenance.FloorIndex);
        Assert.True(unknown.All(change => change.RawData is not null),
            "malformed-floor actions must keep their raw source record");
        Assert.Equal(3, chart.Diagnostics.Count(diagnostic => diagnostic.Code == "ADF-FLOOR-RANGE"));
        Assert.True(
            chart.Diagnostics
                .Where(diagnostic => diagnostic.Code == "ADF-FLOOR-RANGE")
                .All(diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning),
            "ADF-FLOOR-RANGE must be a stable warning diagnostic");

        Assert.Equal(1, chart.GameplayState.Changes.Count(change => change.Kind == GameplayStateChangeKind.Twirl));
        // pathData "R" yields two floors (a start and an end), so two input
        // events remain unaffected by the malformed-floor actions.
        Assert.Equal(2, chart.InputTrack.Events.Count);
    }

    private static void NonObjectActionIsRetained()
    {
        // A non-object entry in actions[] previously claimed to be retained
        // but was actually discarded. It must now survive as a source record
        // and an explicit unknown gameplay state, with its raw JSON text and a
        // floor-range diagnostic.
        var chart = AdofaiReader.Parse("""
            {
              "pathData": "R",
              "settings": { "bpm": 60, "offset": 0 },
              "actions": [
                "not-an-action",
                { "floor": 0, "eventType": "Twirl" }
              ]
            }
            """);

        Assert.Equal(2, chart.SourceEvents.Count);
        Assert.Equal(new[] { 0, 1 }, chart.SourceEvents.Select(sourceEvent => sourceEvent.SourceIndex));
        Assert.Equal("\"not-an-action\"", chart.SourceEvents[0].RawData);
        Assert.True(chart.SourceEvents[0].FloorIndex is null,
            "a non-object entry has no usable floor index");

        var unknown = chart.GameplayState.Changes.Single(change => change.Kind == GameplayStateChangeKind.Unknown);
        Assert.Equal("Unknown", unknown.EventType);
        Assert.Equal(0, unknown.Provenance.SourceIndex);
        Assert.Equal("\"not-an-action\"", unknown.RawData);
        Assert.True(chart.Diagnostics.Any(diagnostic => diagnostic.Code == "ADF007"),
            "the reader must report the non-object entry");
        Assert.True(chart.Diagnostics.Any(diagnostic =>
                diagnostic.Code == "ADF-FLOOR-RANGE"
                && diagnostic.Provenance!.SourceIndex == 0),
            "the retained non-object entry must be diagnosed as floor-out-of-range");
        Assert.Equal(1, chart.GameplayState.Changes.Count(change => change.Kind == GameplayStateChangeKind.Twirl));
    }

    private static void PauseDurationLegality()
    {
        // Pause.duration must be finite and non-negative. Zero is legal (an
        // explicit no-op), a normal positive value is legal, and 1e309 (which
        // JSON parses to +Infinity), a negative value and a missing value are
        // all illegal: they keep their raw source record, expose a
        // canonical-empty DurationBeats and emit the stable ADF-DURATION-VALUE
        // error without ever shifting the time axis.
        var legal = AdofaiReader.Parse("""
            {
              "pathData": "RR",
              "settings": { "bpm": 60, "offset": 0 },
              "actions": [
                { "floor": 0, "eventType": "Pause", "duration": 0 },
                { "floor": 1, "eventType": "Pause", "duration": 2 }
              ]
            }
            """);

        var legalPauses = legal.GameplayState.Changes
            .Where(change => change.Kind == GameplayStateChangeKind.Pause)
            .ToList();
        Assert.Equal(2, legalPauses.Count);
        Assert.True(legalPauses.All(change => change.Application == GameplayStateChangeApplication.Applied),
            "zero and a normal positive duration must both be applied");
        Assert.Equal(0.0, legalPauses[0].DurationBeats!.Value, 9);
        Assert.Equal(2.0, legalPauses[1].DurationBeats!.Value, 9);
        Assert.True(legal.Diagnostics.All(diagnostic => diagnostic.Code != "ADF-DURATION-VALUE"),
            "a legal Pause must not emit a duration error");
        // Floor 0 is 180 degrees at 60 BPM = 1.0s (its zero Pause adds nothing).
        // Floor 1's 2-beat pause adds 2.0s to that floor's own 1.0s of travel,
        // so the final floor entry is 1.0 + (1.0 + 2.0) = 4.0s.
        Assert.Equal(4.0, legal.InputTrack.Events[^1].TimeSeconds, 9);
        Assert.True(legal.Diagnostics.Any(diagnostic => diagnostic.Code == "ADF-PAUSE-INFERRED"
                                                       && diagnostic.Severity == DiagnosticSeverity.Warning),
            "a legal Pause keeps the existing ADF-PAUSE-INFERRED semantic level");

        var illegal = AdofaiReader.Parse("""
            {
              "pathData": "RR",
              "settings": { "bpm": 60, "offset": 0 },
              "actions": [
                { "floor": 0, "eventType": "Pause", "duration": 1e309 },
                { "floor": 1, "eventType": "Pause", "duration": -5 },
                { "floor": 1, "eventType": "Pause" }
              ]
            }
            """);

        var illegalPauses = illegal.GameplayState.Changes
            .Where(change => change.Kind == GameplayStateChangeKind.Pause)
            .ToList();
        Assert.Equal(3, illegalPauses.Count);
        Assert.True(illegalPauses.All(change => change.Application == GameplayStateChangeApplication.Rejected),
            "1e309, negative and missing durations must all be rejected");
        Assert.True(illegalPauses.All(change => change.DurationBeats is null),
            "a rejected Pause must be canonical-empty");
        Assert.True(illegalPauses.All(change => change.RawData is not null),
            "a rejected Pause must keep its raw source record");
        Assert.Equal(new[] { 0, 1, 2 }, illegalPauses.Select(change => change.Provenance.SourceIndex));
        Assert.Equal(3, illegal.Diagnostics.Count(diagnostic => diagnostic.Code == "ADF-DURATION-VALUE"
                                                              && diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.True(illegal.Diagnostics
                .Where(diagnostic => diagnostic.Code == "ADF-DURATION-VALUE")
                .All(diagnostic => diagnostic.Provenance is not null),
            "every duration rejection must carry action provenance");
        // None of the rejected pauses advanced real time: the two R floors are
        // still 1.0s apart and never move backwards.
        var times = illegal.InputTrack.Events.Select(input => input.TimeSeconds).ToArray();
        Assert.True(times.Length == 3 && times[0] == 0.0 && times[1] == 1.0 && times[2] == 2.0,
            $"rejected pauses must not advance real time; got [{string.Join(", ", times)}]");
        Assert.True(illegal.TempoTrack.Segments.All(segment =>
            double.IsFinite(segment.StartTimeSeconds)
            && double.IsFinite(segment.EndTimeSeconds)
            && segment.EndTimeSeconds >= segment.StartTimeSeconds));
        Assert.True(illegal.Diagnostics.All(diagnostic => diagnostic.Code != "ADF-PAUSE-INFERRED"),
            "a rejected Pause must not claim it was included as beats");
    }

    private static void PauseOverflowRejection()
    {
        // A finite Pause duration can still overflow a derived quantity. Each
        // such candidate must be rejected before it enters the time axis, so no
        // downstream floor, input or segment ever sees Infinity or a backwards
        // timestamp.
        //
        // (a) The beats themselves stay finite individually but overflow when
        // accumulated with a previous Pause on the same floor (1e308 + 1e308).
        var beatOverflow = AdofaiReader.Parse("""
            {
              "pathData": "RR",
              "settings": { "bpm": 60, "offset": 0 },
              "actions": [
                { "floor": 0, "eventType": "Pause", "duration": 1e308 },
                { "floor": 0, "eventType": "Pause", "duration": 1e308 }
              ]
            }
            """);

        Assert.True(beatOverflow.GameplayState.Changes
                .Where(change => change.Kind == GameplayStateChangeKind.Pause)
                .All(change => change.Application == GameplayStateChangeApplication.Rejected),
            "an overflowing beat accumulation must be rejected");
        Assert.True(beatOverflow.Diagnostics.Any(diagnostic => diagnostic.Code == "ADF-DURATION-VALUE"
                                                              && diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.True(beatOverflow.InputTrack.Events.All(input => double.IsFinite(input.TimeSeconds)));
        Assert.True(beatOverflow.TempoTrack.Segments.All(segment =>
            double.IsFinite(segment.StartTimeSeconds) && double.IsFinite(segment.EndTimeSeconds)));

        // (b) A finite beat duration at a usable-but-tiny BPM converts to a
        // non-finite number of seconds (1e300 beats at 1e-300 BPM = Infinity).
        var secondOverflow = AdofaiReader.Parse("""
            {
              "pathData": "R",
              "settings": { "bpm": 1e-300, "offset": 0 },
              "actions": [ { "floor": 0, "eventType": "Pause", "duration": 1e300 } ]
            }
            """);

        var secondPause = secondOverflow.GameplayState.Changes
            .Single(change => change.Kind == GameplayStateChangeKind.Pause);
        Assert.Equal(GameplayStateChangeApplication.Rejected, secondPause.Application);
        Assert.True(secondPause.DurationBeats is null, "a Pause whose seconds overflow must be canonical-empty");
        Assert.True(secondOverflow.Diagnostics.Any(diagnostic => diagnostic.Code == "ADF-DURATION-VALUE"
                                                                && diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.True(secondOverflow.InputTrack.Events.All(input => double.IsFinite(input.TimeSeconds)));
        Assert.True(secondOverflow.TempoTrack.Segments.All(segment =>
            double.IsFinite(segment.StartTimeSeconds)
            && double.IsFinite(segment.EndTimeSeconds)
            && segment.EndTimeSeconds >= segment.StartTimeSeconds));

        // (c) A finite floor start plus a finite pause delay can still overflow
        // the next floor start; the pause must be rejected rather than allowed
        // to push a floor outside the finite range.
        var startOverflow = AdofaiReader.Parse("""
            {
              "pathData": "RR",
              "settings": { "bpm": 60, "offset": 0 },
              "actions": [ { "floor": 0, "eventType": "Pause", "duration": 1e308 } ]
            }
            """);

        var startPause = startOverflow.GameplayState.Changes
            .Single(change => change.Kind == GameplayStateChangeKind.Pause);
        Assert.Equal(GameplayStateChangeApplication.Rejected, startPause.Application);
        Assert.True(startOverflow.Diagnostics.Any(diagnostic => diagnostic.Code == "ADF-DURATION-VALUE"));
        Assert.True(startOverflow.InputTrack.Events.All(input => double.IsFinite(input.TimeSeconds)));
        Assert.True(startOverflow.InputTrack.Events.Zip(
                startOverflow.InputTrack.Events.Skip(1),
                (first, second) => second.TimeSeconds >= first.TimeSeconds).All(ordered => ordered),
            "the time axis must never move backwards after a rejected pause");
    }

    private static void PauseCumulativeOverflow()
    {
        // Two individually-representable Pauses can still overflow only when
        // their raw beats are accumulated before the beat-to-second step. At
        // entry BPM 1e307 a single 2e306-beat Pause is legal (12s); adding a
        // second one makes the accumulated 4e306 beats * 60 overflow, so only
        // the second Pause is rejected and the axis carries exactly the first
        // contribution.
        var chart = AdofaiReader.Parse("""
            {
              "pathData": "RR",
              "settings": { "bpm": 1e307, "offset": 0 },
              "actions": [
                { "floor": 0, "eventType": "Pause", "duration": 2e306 },
                { "floor": 0, "eventType": "Pause", "duration": 2e306 }
              ]
            }
            """);

        var pauses = chart.GameplayState.Changes
            .Where(change => change.Kind == GameplayStateChangeKind.Pause)
            .ToList();
        Assert.Equal(2, pauses.Count);
        Assert.Equal(GameplayStateChangeApplication.Applied, pauses[0].Application);
        Assert.Equal(2e306, pauses[0].DurationBeats!.Value, 300);
        Assert.Equal(GameplayStateChangeApplication.Rejected, pauses[1].Application);
        Assert.True(pauses[1].DurationBeats is null, "the rejected cumulative Pause must be canonical-empty");
        Assert.True(pauses[1].RawData is not null, "the rejected Pause must keep its raw source record");
        Assert.Equal(new[] { 0, 1 }, pauses.Select(change => change.Provenance.SourceIndex));

        var durationDiagnostics = chart.Diagnostics
            .Where(diagnostic => diagnostic.Code == "ADF-DURATION-VALUE")
            .ToList();
        Assert.Equal(1, durationDiagnostics.Count);
        Assert.True(durationDiagnostics[0].Provenance!.SourceIndex == 1,
            "only the second Pause may carry a duration provenance");
        Assert.True(chart.Diagnostics.Any(diagnostic => diagnostic.Code == "ADF-PAUSE-INFERRED"
                                                       && diagnostic.Provenance!.SourceIndex == 0),
            "the accepted Pause keeps its inferred-evidence warning");

        var times = chart.InputTrack.Events.Select(input => input.TimeSeconds).ToArray();
        Assert.True(times.All(double.IsFinite), $"every input time must stay finite; got [{string.Join(", ", times)}]");
        Assert.True(times.Zip(times.Skip(1), (first, second) => second >= first).All(ordered => ordered),
            "the axis must stay non-decreasing after a rejected cumulative Pause");
        Assert.Equal(12.0, times[1], 6);
        Assert.Equal(12.0, times[2], 6);
        Assert.True(chart.TempoTrack.Segments.All(segment =>
            double.IsFinite(segment.StartTimeSeconds)
            && double.IsFinite(segment.EndTimeSeconds)
            && segment.EndTimeSeconds >= segment.StartTimeSeconds));
        Assert.True(chart.GameplayState.Changes
            .Zip(chart.GameplayState.ChangesInApplicationOrder, (first, second) => ReferenceEquals(first, second))
            .All(same => same));
    }

    private static void PauseValidatesAgainstFloorEndTempo()
    {
        // A Pause must be validated against the canonical floor-end BPM, not
        // the entry BPM that is current when the action is read. Here a legal
        // SetSpeed drops the floor to 1e-300 BPM, under which the huge Pause's
        // candidate floor duration is not finite. The Pause must be rejected in
        // both source orders while SetSpeed stays applied and the axis stays
        // finite and non-decreasing.
        foreach (var (label, actionOrder) in new[]
                 {
                     ("SetSpeed then Pause", """
                        { "floor": 0, "eventType": "SetSpeed", "speedType": "Bpm", "beatsPerMinute": 1e-300, "angleOffset": 0 },
                        { "floor": 0, "eventType": "Pause", "duration": 1e300 }
                        """),
                     ("Pause then SetSpeed", """
                        { "floor": 0, "eventType": "Pause", "duration": 1e300 },
                        { "floor": 0, "eventType": "SetSpeed", "speedType": "Bpm", "beatsPerMinute": 1e-300, "angleOffset": 0 }
                        """)
                 })
        {
            var chart = AdofaiReader.Parse($$"""
                {
                  "pathData": "R",
                  "settings": { "bpm": 120, "offset": 0 },
                  "actions": [ {{actionOrder}} ]
                }
                """);

            var pause = chart.GameplayState.Changes.Single(change => change.Kind == GameplayStateChangeKind.Pause);
            Assert.Equal(GameplayStateChangeApplication.Rejected, pause.Application);
            Assert.True(pause.DurationBeats is null, $"{label}: the Pause must be canonical-empty");

            var speed = chart.TempoTrack.Events.Single();
            Assert.Equal(TempoEventApplication.Applied, speed.Application);
            Assert.Equal(1e-300, speed.Bpm!.Value, 320);

            Assert.True(chart.Diagnostics.Any(diagnostic => diagnostic.Code == "ADF-DURATION-VALUE"
                                                          && diagnostic.Provenance!.SourceIndex == pause.Provenance.SourceIndex),
                $"{label}: the rejection must point at the Pause itself");
            Assert.True(chart.InputTrack.Events.All(input => double.IsFinite(input.TimeSeconds)),
                $"{label}: the time axis must stay finite");
            Assert.True(chart.InputTrack.Events.Zip(
                    chart.InputTrack.Events.Skip(1),
                    (first, second) => second.TimeSeconds >= first.TimeSeconds).All(ordered => ordered),
                $"{label}: the time axis must stay non-decreasing");
            Assert.True(chart.TempoTrack.Segments.All(segment =>
                    double.IsFinite(segment.StartTimeSeconds)
                    && double.IsFinite(segment.EndTimeSeconds)
                    && segment.EndTimeSeconds >= segment.StartTimeSeconds),
                $"{label}: segments must stay finite and non-decreasing");
        }
    }

    private static void PauseRejectionPreservesApplicationOrder()
    {
        // Pause, Twirl and SetSpeed on one floor keep their source order as
        // application ordinals even when the Pause is rejected after floor
        // tempo resolution. The rejection must not reorder or drop the legal
        // Twirl and SetSpeed state.
        var chart = AdofaiReader.Parse("""
            {
              "pathData": "RR",
              "settings": { "bpm": 120, "offset": 0 },
              "actions": [
                { "floor": 0, "eventType": "Pause", "duration": 1e300 },
                { "floor": 0, "eventType": "Twirl" },
                { "floor": 0, "eventType": "SetSpeed", "speedType": "Bpm", "beatsPerMinute": 1e-300, "angleOffset": 0 }
              ]
            }
            """);

        var pause = chart.GameplayState.Changes.Single(change => change.Kind == GameplayStateChangeKind.Pause);
        var twirl = chart.GameplayState.Changes.Single(change => change.Kind == GameplayStateChangeKind.Twirl);
        var speed = chart.TempoTrack.Events.Single();

        Assert.Equal(GameplayStateChangeApplication.Rejected, pause.Application);
        Assert.True(twirl.TwirlStateAfter == true, "the legal Twirl must still apply");
        Assert.True(chart.GameplayState.TwirlAt(0.0));
        Assert.Equal(TempoEventApplication.Applied, speed.Application);
        Assert.Equal(1e-300, speed.Bpm!.Value, 320);

        Assert.True(pause.ApplicationOrder >= 0 && twirl.ApplicationOrder >= 0,
            "both the rejected Pause and the applied Twirl keep a real application ordinal");
        Assert.True(pause.ApplicationOrder < twirl.ApplicationOrder
                    && twirl.ApplicationOrder < speed.ApplicationOrder,
            "Pause, Twirl and SetSpeed must retain source order after deferred Pause rejection");
        Assert.True(chart.GameplayState.ChangesInApplicationOrder
            .Zip(chart.GameplayState.ChangesInApplicationOrder.Skip(1),
                (first, second) => first.ApplicationOrder < second.ApplicationOrder)
            .All(ordered => ordered),
            "application order must stay strictly ascending");
        Assert.True(chart.InputTrack.Events.All(input => double.IsFinite(input.TimeSeconds)));
    }

    private static void HoldDurationLegality()
    {
        // Hold.duration follows the same finite/non-negative invariant. An
        // illegal Hold must be canonical-empty in BOTH the gameplay state and
        // the InputEvent.DurationBeats, while a legal one keeps its beats.
        var legal = AdofaiReader.Parse("""
            {
              "pathData": "RR",
              "settings": { "bpm": 60, "offset": 0 },
              "actions": [ { "floor": 0, "eventType": "Hold", "duration": 1.5 } ]
            }
            """);

        var legalHold = legal.GameplayState.Changes.Single(change => change.Kind == GameplayStateChangeKind.Hold);
        Assert.Equal(GameplayStateChangeApplication.Applied, legalHold.Application);
        Assert.Equal(1.5, legalHold.DurationBeats!.Value, 9);
        Assert.Equal(InputEventKind.Hold, legal.InputTrack.Events[0].Kind);
        Assert.Equal(1.5, legal.InputTrack.Events[0].DurationBeats!.Value, 9);
        Assert.True(legal.Diagnostics.Any(diagnostic => diagnostic.Code == "ADF-HOLD-INFERRED"
                                                       && diagnostic.Severity == DiagnosticSeverity.Warning));
        Assert.True(legal.Diagnostics.All(diagnostic => diagnostic.Code != "ADF-DURATION-VALUE"));

        var illegal = AdofaiReader.Parse("""
            {
              "pathData": "RRR",
              "settings": { "bpm": 60, "offset": 0 },
              "actions": [
                { "floor": 0, "eventType": "Hold", "duration": 1e309 },
                { "floor": 1, "eventType": "Hold", "duration": -3 },
                { "floor": 2, "eventType": "Hold" }
              ]
            }
            """);

        var holds = illegal.GameplayState.Changes
            .Where(change => change.Kind == GameplayStateChangeKind.Hold)
            .ToList();
        Assert.Equal(3, holds.Count);
        Assert.True(holds.All(change => change.Application == GameplayStateChangeApplication.Rejected),
            "1e309, negative and missing Hold durations must all be rejected");
        Assert.True(holds.All(change => change.DurationBeats is null),
            "a rejected Hold must be canonical-empty in the gameplay state");
        Assert.True(holds.All(change => change.RawData is not null),
            "a rejected Hold must keep its raw source record");
        Assert.Equal(3, illegal.Diagnostics.Count(diagnostic => diagnostic.Code == "ADF-DURATION-VALUE"
                                                              && diagnostic.Severity == DiagnosticSeverity.Error));
        // The Hold-floored input events must not resurrect the rejected beats.
        var holdInputs = illegal.InputTrack.Events
            .Where(input => input.Kind == InputEventKind.Hold)
            .ToList();
        Assert.Equal(3, holdInputs.Count);
        Assert.True(holdInputs.All(input => input.DurationBeats is null),
            "a rejected Hold must also leave InputEvent.DurationBeats canonical-empty");
        Assert.True(illegal.InputTrack.Events.All(input => double.IsFinite(input.TimeSeconds)));
    }

    private static void FreeRoamDurationLegality()
    {
        // FreeRoam does not advance floor time in this baseline, but an illegal
        // duration may still not enter canonical gameplay state as if applied.
        // It must be canonical-empty with a stable error; zero and a normal
        // positive value stay applied.
        var chart = AdofaiReader.Parse("""
            {
              "pathData": "RRRR",
              "settings": { "bpm": 60, "offset": 0 },
              "actions": [
                { "floor": 0, "eventType": "FreeRoam" },
                { "floor": 1, "eventType": "FreeRoam", "duration": 1e309 },
                { "floor": 2, "eventType": "FreeRoam", "duration": -1 },
                { "floor": 3, "eventType": "FreeRoam", "duration": 0 }
              ]
            }
            """);

        var freeRoams = chart.GameplayState.Changes
            .Where(change => change.Kind == GameplayStateChangeKind.FreeRoam)
            .ToList();
        Assert.Equal(4, freeRoams.Count);
        Assert.True(freeRoams.All(change => change.Application == GameplayStateChangeApplication.Rejected
                                           || change.DurationBeats is not null),
            "an applied FreeRoam must expose a canonical duration");
        Assert.True(freeRoams.Take(3).All(change => change.Application == GameplayStateChangeApplication.Rejected),
            "missing, 1e309 and negative durations must all be rejected");
        Assert.True(freeRoams[3].Application == GameplayStateChangeApplication.Applied
                    && freeRoams[3].DurationBeats == 0.0,
            "a zero duration is legal for FreeRoam");
        Assert.True(freeRoams.Where(change => change.Application == GameplayStateChangeApplication.Rejected)
            .All(change => change.RawData is not null));
        Assert.Equal(3, chart.Diagnostics.Count(diagnostic => diagnostic.Code == "ADF-DURATION-VALUE"
                                                            && diagnostic.Severity == DiagnosticSeverity.Error));
        // FreeRoam is still not promoted past INFERRED: its provenance warning
        // stays on every event, legal or not.
        Assert.True(chart.Diagnostics
                .Where(diagnostic => diagnostic.Code == "ADF-FREEROAM-UNKNOWN")
                .All(diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning));
        Assert.Equal(4, chart.Diagnostics.Count(diagnostic => diagnostic.Code == "ADF-FREEROAM-UNKNOWN"));
        Assert.True(chart.InputTrack.Events.All(input => double.IsFinite(input.TimeSeconds)));
    }

    private static void IllegalPauseKeepsLaterState()
    {
        // A rejected Pause must not desynchronise the geometry/state that
        // follows it. Twirl after an illegal Pause still flips the geometry and
        // SetSpeed still applies using the untouched tempo.
        var chart = AdofaiReader.Parse("""
            {
              "pathData": "RR",
              "settings": { "bpm": 60, "offset": 0 },
              "actions": [
                { "floor": 0, "eventType": "Pause", "duration": 1e309 },
                { "floor": 0, "eventType": "Twirl" },
                { "floor": 1, "eventType": "SetSpeed", "speedType": "Bpm", "beatsPerMinute": 120 }
              ]
            }
            """);

        var pause = chart.GameplayState.Changes.Single(change => change.Kind == GameplayStateChangeKind.Pause);
        Assert.Equal(GameplayStateChangeApplication.Rejected, pause.Application);
        Assert.True(pause.DurationBeats is null);

        var twirl = chart.GameplayState.Changes.Single(change => change.Kind == GameplayStateChangeKind.Twirl);
        Assert.True(twirl.TwirlStateAfter == true, "the Twirl after an illegal Pause must still apply");
        Assert.True(chart.GameplayState.TwirlAt(0.0), "TwirlAt must agree with the applied Twirl state");

        var speed = chart.TempoTrack.Events.Single();
        Assert.Equal(TempoEventApplication.Applied, speed.Application);
        Assert.Equal(120.0, speed.Bpm!.Value, 9);
        // Floor 0 is 180 degrees at the base 60 BPM (1.0s); floor 1's SetSpeed
        // boundary sits at that time, and the floor itself then runs at 120 BPM
        // (0.5s). No rejection may have shifted either time.
        Assert.Equal(1.0, speed.TimeSeconds, 9);
        Assert.Equal(1.5, chart.InputTrack.Events[^1].TimeSeconds, 9);

        Assert.True(chart.InputTrack.Events.All(input => double.IsFinite(input.TimeSeconds)));
        Assert.True(chart.TempoTrack.Segments.All(segment =>
            double.IsFinite(segment.StartTimeSeconds)
            && double.IsFinite(segment.EndTimeSeconds)
            && segment.EndTimeSeconds >= segment.StartTimeSeconds));
        Assert.True(chart.GameplayState.ChangesInApplicationOrder
            .Zip(chart.GameplayState.ChangesInApplicationOrder.Skip(1),
                (first, second) => first.ApplicationOrder < second.ApplicationOrder)
            .All(ordered => ordered),
            "application order must stay strict after a rejected Pause");
        Assert.Equal(1, chart.Diagnostics.Count(diagnostic => diagnostic.Code == "ADF-DURATION-VALUE"));
    }

    private static void UnderflowSegmentPrecisionLimitation()
    {
        // 5e-324 is the smallest positive double: a legal angleOffset multiplied
        // by a very large (but still usable) BPM yields a sub-precision real
        // time that can round to the floor's start instant. The contract is not
        // to fabricate a minimum spacing: the derived times must stay finite,
        // non-decreasing and deterministically ordered, and the source angle
        // must stay visible.
        var chart = AdofaiReader.Parse("""
            {
              "pathData": "R",
              "settings": { "bpm": 60, "offset": 0 },
              "actions": [
                { "floor": 0, "eventType": "SetSpeed", "speedType": "Bpm", "beatsPerMinute": 1e307, "angleOffset": 5e-324 }
              ]
            }
            """);

        var speed = chart.TempoTrack.Events.Single();
        Assert.Equal(TempoEventApplication.Applied, speed.Application);
        Assert.Equal(5e-324, speed.AngleOffsetDegrees!.Value, 320);
        Assert.True(speed.RawData!.Contains("angleOffset"), "the source angle must stay visible in the raw record");

        Assert.True(chart.TempoTrack.Segments.All(segment =>
            double.IsFinite(segment.StartTimeSeconds)
            && double.IsFinite(segment.EndTimeSeconds)
            && segment.EndTimeSeconds >= segment.StartTimeSeconds),
            "segments must stay finite and non-decreasing under underflow");
        Assert.True(chart.InputTrack.Events.All(input => double.IsFinite(input.TimeSeconds)));
        Assert.True(chart.InputTrack.Events.Zip(
                chart.InputTrack.Events.Skip(1),
                (first, second) => second.TimeSeconds >= first.TimeSeconds).All(ordered => ordered),
            "underflowed times may be equal but may never move backwards");
        Assert.True(chart.Diagnostics.All(diagnostic => diagnostic.Code != "ADF-DURATION-VALUE"),
            "a finite non-decreasing underflow must not be reported as an illegal duration");
    }

    private static void PauseSourceApplicationOrder()
    {
        var chart = AdofaiReader.Parse("""
            {
              "pathData": "RR",
              "settings": { "bpm": 60, "offset": 0 },
              "actions": [
                { "floor": 0, "eventType": "Pause", "duration": 1 },
                { "floor": 0, "eventType": "Twirl" },
                { "floor": 0, "eventType": "SetSpeed", "speedType": "Bpm", "beatsPerMinute": 120 },
                { "floor": 0, "eventType": "Hold", "duration": 1 }
              ]
            }
            """);
        var bySource = chart.GameplayState.Changes
            .Concat(chart.TempoTrack.Events.Select(speed => new GameplayStateChange(
                GameplayStateChangeKind.Unknown, "SetSpeed", speed.TimeSeconds,
                speed.Provenance, ApplicationOrder: speed.ApplicationOrder)))
            .OrderBy(change => change.Provenance.SourceIndex).ToList();
        Assert.Equal(new[] { 0, 1, 2, 3 }, bySource.Select(change => change.ApplicationOrder));
        Assert.Equal(new[] { 0, 1, 3 }, chart.GameplayState.ChangesInApplicationOrder
            .Select(change => change.Provenance.SourceIndex));
        Assert.Equal(1.0, chart.InputTrack.Events[1].TimeSeconds, 9);
    }

    private static void RejectedPauseSourceApplicationOrder()
    {
        var chart = AdofaiReader.Parse("""
            {
              "pathData": "R",
              "settings": { "bpm": 120, "offset": 0 },
              "actions": [
                { "floor": 0, "eventType": "Pause", "duration": 1e309 },
                { "floor": 0, "eventType": "Twirl" },
                { "floor": 0, "eventType": "SetSpeed", "speedType": "Bpm", "beatsPerMinute": 1e-300 },
                { "floor": 0, "eventType": "Pause", "duration": 1e300 }
              ]
            }
            """);
        var pauses = chart.GameplayState.Changes
            .Where(change => change.Kind == GameplayStateChangeKind.Pause)
            .OrderBy(change => change.Provenance.SourceIndex).ToList();
        Assert.Equal(2, pauses.Count);
        Assert.True(pauses.All(change => change.Application == GameplayStateChangeApplication.Rejected));
        Assert.Equal(new[] { 0, 3 }, pauses.Select(change => change.ApplicationOrder));
        Assert.Equal(new[] { 0, 1, 3 }, chart.GameplayState.ChangesInApplicationOrder
            .Select(change => change.Provenance.SourceIndex));
        Assert.Equal(2, chart.TempoTrack.Events.Single().ApplicationOrder);
        Assert.True(chart.GameplayState.TwirlAt(0));
        Assert.True(chart.InputTrack.Events.All(input => double.IsFinite(input.TimeSeconds)));
    }

    private static void PauseInferredDiagnosticProvenance()
    {
        // Rejected positive, accepted zero, accepted positive: only the last
        // Pause actually contributes time and can own the inference warning.
        var chart = AdofaiReader.Parse("""
            {
              "pathData": "R",
              "settings": { "bpm": 1e-300, "offset": 0 },
              "actions": [
                { "floor": 0, "eventType": "Pause", "duration": 1e300 },
                { "floor": 0, "eventType": "Pause", "duration": 0 },
                { "floor": 0, "eventType": "Pause", "duration": 1 }
              ]
            }
            """);
        var pauses = chart.GameplayState.Changes
            .Where(change => change.Kind == GameplayStateChangeKind.Pause)
            .OrderBy(change => change.Provenance.SourceIndex).ToList();
        Assert.Equal(GameplayStateChangeApplication.Rejected, pauses[0].Application);
        Assert.Equal(GameplayStateChangeApplication.Applied, pauses[1].Application);
        Assert.Equal(GameplayStateChangeApplication.Applied, pauses[2].Application);
        Assert.Equal(new[] { 0, 1, 2 }, pauses.Select(change => change.ApplicationOrder));
        var inferred = chart.Diagnostics.Single(diagnostic => diagnostic.Code == "ADF-PAUSE-INFERRED");
        Assert.Equal(2, inferred.Provenance!.SourceIndex);
        Assert.True(chart.Diagnostics.Any(diagnostic =>
            diagnostic.Code == "ADF-DURATION-VALUE" && diagnostic.Provenance!.SourceIndex == 0));
    }

    private static void CumulativeFloorTravelOverflow()
    {
        // Individually valid floor spans may have an unrepresentable cumulative
        // endpoint, even when the overflowing floor is the final one.
        var safe = AdofaiReader.Parse("""
            { "pathData": "R", "settings": { "bpm": 1e-306, "offset": 0 }, "actions": [] }
            """);
        Assert.True(safe.InputTrack.Events.All(input => double.IsFinite(input.TimeSeconds)));
        Assert.True(safe.TempoTrack.Segments.All(segment =>
            double.IsFinite(segment.StartTimeSeconds) && double.IsFinite(segment.EndTimeSeconds)));
        foreach (var path in new[] { "RR", "RRR" })
        {
            try
            {
                AdofaiReader.Parse($$"""
                    { "pathData": "{{path}}", "settings": { "bpm": 1e-306, "offset": 0 }, "actions": [] }
                    """);
                throw new InvalidOperationException("a non-finite cumulative time must not produce a chart");
            }
            catch (FormatException exception)
            {
                Assert.True(exception.Message.Contains("ADF-DURATION-VALUE", StringComparison.Ordinal)
                            && exception.Message.Contains("floor", StringComparison.Ordinal),
                    "fail-closed timing must identify the invalid floor and stable code");
            }
        }
    }

    private static void ManiaMicroFixture()
    {
        const string source = """
            osu file format v14

            [General]
            Mode: 3

            [Difficulty]
            CircleSize: 4

            [TimingPoints]
            0,500,4,1,0,100,1,0
            1000,-50,4,1,0,100,0,0
            1000,-25,4,1,0,100,0,0

            [HitObjects]
            0,192,1000,1,0
            127,192,1000,1,0
            128,192,1000,1,0
            255,192,1000,1,0
            256,192,1000,1,0
            383,192,1000,1,0
            384,192,1000,1,0
            511,192,1000,1,0
            256,192,1500,128,0,2000:0:0:0:0:
            """;

        var chart = OsuManiaReader.Parse(source);
        Assert.Equal(4, chart.Metadata.KeyCount!.Value);
        Assert.Equal(9, chart.InputTrack.Events.Count);
        Assert.Equal(8, chart.InputTrack.Chords().Single().Count);
        Assert.Equal(
            new[] { 0, 0, 1, 1, 2, 2, 3, 3, 2 },
            chart.InputTrack.Events.Select(input => input.Lane).Select(lane => lane!.Value));
        Assert.Equal(8, chart.InputTrack.Events.Count(input => input.Kind == InputEventKind.Tap));
        var hold = chart.InputTrack.Events.Single(input => input.Kind == InputEventKind.Hold);
        Assert.Equal(1.5, hold.TimeSeconds, 9);
        Assert.Equal(2, hold.EndTimeSeconds!.Value, 9);
        Assert.Equal(1, chart.TempoTrack.Events.Count);
        Assert.Equal(2, chart.ScrollPresentationTrack.ScrollEvents.Count);
        Assert.Equal(4, ManiaTimingResolver.EffectiveScrollAt(chart.ScrollPresentationTrack, 1.0), 9);
        Assert.Equal(120, chart.TempoTrack.Events[0].Bpm!.Value, 9);
        Assert.Equal(1.0, chart.InputTrack.Events[0].TimeSeconds, 9);
        Assert.True(chart.Diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error));
    }

    private static void PrecisionAndOrdering()
    {
        const string source = """
            osu file format v14

            [General]
            Mode: 3

            [Difficulty]
            CircleSize: 4

            [TimingPoints]
            123.4567,500,4,1,0,100,1,0
            123.4567,-50,4,1,0,100,0,0
            123.4567,-25,4,1,0,100,0,0

            [HitObjects]
            10,192,100.125,1,0
            20,192,100.125,1,0
            """;

        var chart = OsuManiaReader.Parse(source);
        Assert.Equal("123.4567", chart.TempoTrack.Events[0].OriginalTime);
        Assert.Equal("100.125", chart.InputTrack.Events[0].OriginalTimestamp);
        Assert.Equal(5, chart.SourceEvents.Count);
        Assert.True(
            chart.ScrollPresentationTrack.ScrollEvents[0].Provenance.SourceIndex
            < chart.ScrollPresentationTrack.ScrollEvents[1].Provenance.SourceIndex);
        Assert.True(
            chart.InputTrack.Events[0].Provenance.SourceIndex
            < chart.InputTrack.Events[1].Provenance.SourceIndex);
        Assert.Equal(1, chart.InputTrack.Chords().Count);

        // Red and green points share the exact decimal timestamp 123.4567.
        // The red point fixes the tempo; the later green lines stack as SV.
        // Neither may rewrite the HitObject timestamps.
        var redTime = chart.TempoTrack.Events[0].TimeSeconds;
        var greenTimes = chart.ScrollPresentationTrack.ScrollEvents
            .Select(scroll => scroll.TimeSeconds)
            .ToList();
        Assert.True(greenTimes.All(green => Math.Abs(green - redTime) < 1e-9),
            "same-timestamp red and green points must keep the same derived time");
        Assert.Equal(2, chart.ScrollPresentationTrack.ScrollEvents.Count);
        Assert.Equal(
            new[] { 2.0, 4.0 },
            chart.ScrollPresentationTrack.ScrollEvents.Select(scroll => scroll.Multiplier!.Value));
        Assert.Equal(120.0, ManiaTimingResolver.EffectiveBpmAt(chart.TempoTrack, redTime)!.Value, 9);
        Assert.Equal(4.0, ManiaTimingResolver.EffectiveScrollAt(chart.ScrollPresentationTrack, redTime), 9);

        // The green SV lines must not move the HitObjects: both keep the
        // source-decimal timestamp (100.125 ms => 0.100125 s).
        Assert.Equal(0.100125, chart.InputTrack.Events[0].TimeSeconds, 9);
        Assert.Equal(0.100125, chart.InputTrack.Events[1].TimeSeconds, 9);
        Assert.Equal("100.125", chart.InputTrack.Events[0].OriginalTimestamp);
        Assert.Equal("100.125", chart.InputTrack.Events[1].OriginalTimestamp);
        Assert.True(chart.InputTrack.Events.All(input => input.TimeSeconds < redTime),
            "SV lines must not shift HitObject timestamps relative to the red point");
    }

    private static void RealAdoFaiFixture()
    {
        var root = RepositoryRoot();
        var path = Path.Combine(root, ".sample", "ADOFAI-sample", "main.adofai");
        var chart = AdofaiReader.ParseFile(path);
        var directions = chart.Metadata.DirectionTokens!;

        Assert.Equal(217, directions.Count);
        Assert.Equal(218, chart.InputTrack.Events.Count);
        Assert.Equal(265, chart.SourceEvents.Count);
        Assert.Equal(41, chart.TempoTrack.Events.Count);
        Assert.Equal(31, chart.TempoTrack.Events.Count(eventInfo => eventInfo.SpeedMode == SetSpeedMode.Bpm));
        Assert.Equal(10, chart.TempoTrack.Events.Count(eventInfo => eventInfo.SpeedMode == SetSpeedMode.Multiplier));
        Assert.Equal(8, directions.Count(direction => direction.IsMidspin));
        Assert.Equal(8, chart.GameplayState.Changes.Count(change => change.Kind == GameplayStateChangeKind.Twirl));
        Assert.Equal(50, chart.Metadata.BaseBpm!.Value, 9);
        Assert.Equal(30, chart.Metadata.AudioOffsetMilliseconds!.Value, 9);
        Assert.Equal("50", chart.Metadata.BaseBpmOriginal);
        Assert.Equal("30", chart.Metadata.AudioOffsetOriginal);
        Assert.True(chart.InputTrack.Events.All(input => double.IsFinite(input.TimeSeconds)));
        Assert.True(chart.InputTrack.Events.Zip(chart.InputTrack.Events.Skip(1), (first, second) => second.TimeSeconds >= first.TimeSeconds).All(result => result));
    }

    private static void RealManiaFixture()
    {
        var root = RepositoryRoot();
        var directory = Path.Combine(root, ".sample", "osu!mania-sample");
        var path = Directory.GetFiles(directory, "*.osu")
            .Single(file => Path.GetFileName(file).Contains("[SV JUDGMENT]", StringComparison.Ordinal));
        var chart = OsuManiaReader.ParseFile(path);

        Assert.Equal(4, chart.Metadata.KeyCount!.Value);
        Assert.Equal("4", chart.Metadata.KeyCountOriginal);
        Assert.Equal(5982, chart.InputTrack.Events.Count);
        Assert.Equal(5575, chart.InputTrack.Events.Count(input => input.Kind == InputEventKind.Tap));
        Assert.Equal(407, chart.InputTrack.Events.Count(input => input.Kind == InputEventKind.Hold));
        Assert.Equal(57, chart.TempoTrack.Events.Count);
        Assert.Equal(3333, chart.ScrollPresentationTrack.ScrollEvents.Count);
        Assert.Equal(4, chart.InputTrack.Chords().Max(group => group.Count));
        Assert.True(chart.ScrollPresentationTrack.ScrollEvents.Any(eventInfo => eventInfo.Multiplier is > 1));
        Assert.True(chart.Diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Adosu.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root from the test output directory.");
    }
}

internal static class Assert
{
    public static void True(bool condition, string message = "assertion failed")
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"expected {expected}, actual {actual}");
        }
    }

    public static void Equal<T>(IEnumerable<T> expected, IEnumerable<T> actual)
    {
        if (!expected.SequenceEqual(actual))
        {
            throw new InvalidOperationException(
                $"expected [{string.Join(", ", expected)}], actual [{string.Join(", ", actual)}]");
        }
    }

    public static void Equal(double expected, double actual, int precision)
    {
        if (Math.Abs(expected - actual) > Math.Pow(10, -precision))
        {
            throw new InvalidOperationException($"expected {expected}, actual {actual}, precision {precision}");
        }
    }
}
