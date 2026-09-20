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
        ("ADOFAI missing or out-of-range floor is not dropped", MalformedFloorIndexIsNotDropped),
        ("ADOFAI non-object action is retained, not discarded", NonObjectActionIsRetained),
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
