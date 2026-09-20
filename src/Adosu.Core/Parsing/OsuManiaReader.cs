using System.Globalization;
using Adosu.Core.Model;

namespace Adosu.Core.Parsing;

public static class OsuManiaReader
{
    public static GameplayChart ParseFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Parse(File.ReadAllText(path));
    }

    public static GameplayChart Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var diagnostics = new List<Diagnostic>();
        var lines = text.Split(['\r', '\n'], StringSplitOptions.None);
        var sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var timingLines = new List<RawTimingPoint>();
        var hitObjectLines = new List<RawHitObject>();
        var section = string.Empty;
        var timingOrder = 0;
        var hitObjectOrder = 0;
        var previousTimingTime = decimal.MinValue;
        var previousHitTime = decimal.MinValue;

        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var originalLine = lines[lineIndex].TrimStart('\uFEFF');
            var line = originalLine.Trim();
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1];
                continue;
            }

            if (section.Equals("TimingPoints", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryParseTimingPoint(line, lineIndex, timingOrder, diagnostics, out var timingPoint))
                {
                    continue;
                }

                if (timingPoint.TimeMilliseconds < previousTimingTime)
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticSeverity.Warning,
                        "OSU-TIMING-ORDER",
                        "Timing points are not sorted chronologically; source order is retained.",
                        timingPoint.Provenance));
                }

                previousTimingTime = timingPoint.TimeMilliseconds;
                timingLines.Add(timingPoint);
                timingOrder++;
                continue;
            }

            if (section.Equals("HitObjects", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryParseHitObject(line, lineIndex, hitObjectOrder, diagnostics, out var hitObject))
                {
                    continue;
                }

                if (hitObject.TimeMilliseconds < previousHitTime)
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticSeverity.Warning,
                        "OSU-HIT-ORDER",
                        "HitObjects are not sorted chronologically; source order is retained.",
                        hitObject.Provenance));
                }

                previousHitTime = hitObject.TimeMilliseconds;
                hitObjectLines.Add(hitObject);
                hitObjectOrder++;
                continue;
            }

            var separator = line.IndexOf(':');
            if (separator > 0
                && (section.Equals("General", StringComparison.OrdinalIgnoreCase)
                    || section.Equals("Difficulty", StringComparison.OrdinalIgnoreCase)
                    || section.Equals("Metadata", StringComparison.OrdinalIgnoreCase)
                    || section.Equals("Editor", StringComparison.OrdinalIgnoreCase)))
            {
                if (!sections.TryGetValue(section, out var values))
                {
                    values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    sections.Add(section, values);
                }

                values[line[..separator].Trim()] = line[(separator + 1)..].Trim();
            }
        }

        var general = GetSection(sections, "General");
        var difficulty = GetSection(sections, "Difficulty");
        var mode = ParseInt(general, "Mode", diagnostics, "OSU001");
        if (mode != 3)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Error,
                "OSU-MODE",
                $"Expected Mode: 3 for osu!mania, received {mode?.ToString(CultureInfo.InvariantCulture) ?? "missing"}."));
        }

        var circleSize = ParseDecimal(difficulty, "CircleSize", diagnostics, "OSU002");
        difficulty.TryGetValue("CircleSize", out var circleSizeRaw);
        var keyCount = circleSize is not null && circleSize.Value >= 1 && circleSize.Value == decimal.Truncate(circleSize.Value)
            ? (int)circleSize.Value
            : 0;
        if (keyCount == 0)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Error,
                "OSU-KEYS",
                "CircleSize must be a positive integer for the mania lane model."));
        }

        var tempoEvents = new List<TempoEvent>();
        var scrollEvents = new List<ScrollEvent>();
        var sourceEvents = new List<SourceEvent>();
        foreach (var timingPoint in timingLines)
        {
            sourceEvents.Add(new SourceEvent(
                "TimingPoint",
                timingPoint.Provenance.SourceIndex,
                null,
                timingPoint.Provenance,
                timingPoint.RawLine));

            if (timingPoint.Uninherited)
            {
                var bpm = timingPoint.BeatLengthMilliseconds > 0
                    ? 60_000.0 / (double)timingPoint.BeatLengthMilliseconds
                    : (double?)null;
                if (bpm is null)
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticSeverity.Error,
                        "OSU-BEAT-LENGTH",
                        "An uninherited timing point must have a positive beatLength.",
                        timingPoint.Provenance));
                }

                tempoEvents.Add(new TempoEvent(
                    TempoEventKind.RedTimingPoint,
                    (double)timingPoint.TimeMilliseconds / 1000.0,
                    timingPoint.Provenance,
                    Bpm: bpm,
                    OriginalTime: timingPoint.TimeText,
                    OriginalBeatLength: timingPoint.BeatLengthText,
                    RawData: timingPoint.RawLine));
            }
            else
            {
                double? sv = timingPoint.BeatLengthMilliseconds < 0
                    ? -100.0 / (double)timingPoint.BeatLengthMilliseconds
                    : null;
                if (sv is null)
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticSeverity.Warning,
                        "OSU-SV-VALUE",
                        "An inherited timing point normally uses a negative beatLength; its raw point was retained with no derived SV.",
                        timingPoint.Provenance));
                }

                scrollEvents.Add(new ScrollEvent(
                    ScrollEventKind.ManiaSliderVelocity,
                    (double)timingPoint.TimeMilliseconds / 1000.0,
                    sv,
                    timingPoint.Provenance,
                    timingPoint.TimeText,
                    timingPoint.BeatLengthText,
                    timingPoint.RawLine));
            }
        }

        var inputEvents = new List<InputEvent>();
        foreach (var hitObject in hitObjectLines)
        {
            var lane = keyCount > 0
                ? Math.Clamp((int)(hitObject.X * keyCount / 512m), 0, keyCount - 1)
                : (int?)null;
            var kind = hitObject.IsHold
                ? InputEventKind.Hold
                : hitObject.IsCircle
                    ? InputEventKind.Tap
                    : InputEventKind.Unknown;
            var endTimeSeconds = hitObject.EndTimeMilliseconds is null
                ? null
                : (double?)hitObject.EndTimeMilliseconds.Value / 1000.0;

            if (!hitObject.IsCircle && !hitObject.IsHold)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Warning,
                    "OSU-OBJECT-TYPE",
                    $"HitObject type {hitObject.Type} is not a mania tap or hold; it remains in InputTrack as Unknown.",
                    hitObject.Provenance));
            }

            inputEvents.Add(new InputEvent(
                kind,
                (double)hitObject.TimeMilliseconds / 1000.0,
                lane,
                endTimeSeconds,
                hitObject.Provenance,
                hitObject.TimeText,
                hitObject.EndTimeText,
                RawData: hitObject.RawLine));
            sourceEvents.Add(new SourceEvent(
                "HitObject",
                hitObject.Provenance.SourceIndex,
                null,
                hitObject.Provenance,
                hitObject.RawLine));
        }

        var metadata = new ChartMetadata(
            SourceFormat.OsuMania,
            KeyCount: keyCount,
            KeyCountOriginal: circleSizeRaw);
        return new GameplayChart(
            SourceFormat.OsuMania,
            new InputTrack(inputEvents),
            new TempoTrack(tempoEvents),
            new ScrollPresentationTrack(scrollEvents, []),
            new GameplayState([]),
            diagnostics,
            metadata,
            sourceEvents);
    }

    private static bool TryParseTimingPoint(
        string line,
        int lineIndex,
        int pointIndex,
        ICollection<Diagnostic> diagnostics,
        out RawTimingPoint timingPoint)
    {
        var fields = line.Split(',');
        if (fields.Length < 8
            || !TryParseDecimal(fields[0], out var time)
            || !TryParseDecimal(fields[1], out var beatLength))
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Error,
                "OSU-TIMING-FORMAT",
                $"Invalid timing point at source line {lineIndex + 1}; source line was not discarded from the diagnostic stream."));
            timingPoint = default!;
            return false;
        }

        var uninherited = fields[6].Trim() switch
        {
            "1" => true,
            "0" => false,
            _ => ParseUninherited(fields[6], diagnostics, lineIndex)
        };
        var provenance = new SourceProvenance(
            SourceFormat.OsuMania,
            lineIndex,
            SourceTimestamp: fields[0].Trim(),
            SourceType: uninherited ? "red timing point" : "green timing point",
            RawValue: line);
        timingPoint = new RawTimingPoint(
            pointIndex,
            lineIndex,
            line,
            time,
            beatLength,
            uninherited,
            fields[0].Trim(),
            fields[1].Trim(),
            provenance);
        return true;
    }

    private static bool TryParseHitObject(
        string line,
        int lineIndex,
        int objectIndex,
        ICollection<Diagnostic> diagnostics,
        out RawHitObject hitObject)
    {
        var fields = line.Split(',');
        if (fields.Length < 5
            || !TryParseDecimal(fields[0], out var x)
            || !TryParseDecimal(fields[2], out var time)
            || !int.TryParse(fields[3].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var type))
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Error,
                "OSU-HIT-FORMAT",
                $"Invalid hit object at source line {lineIndex + 1}."));
            hitObject = default!;
            return false;
        }

        var isCircle = (type & 1) != 0;
        var isHold = (type & 128) != 0;
        decimal? endTime = null;
        string? endTimeText = null;
        if (isHold)
        {
            var parameters = fields.Length > 5 ? fields[5].Split(':', 2) : [];
            if (parameters.Length == 0
                || !TryParseDecimal(parameters[0], out var parsedEndTime))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Error,
                    "OSU-HOLD-END",
                    $"Hold object at source line {lineIndex + 1} has no valid endTime."));
            }
            else
            {
                endTime = parsedEndTime;
                endTimeText = parameters[0].Trim();
            }
        }

        hitObject = new RawHitObject(
            objectIndex,
            lineIndex,
            line,
            x,
            time,
            type,
            isCircle,
            isHold,
            endTime,
            fields[2].Trim(),
            endTimeText,
            new SourceProvenance(
                SourceFormat.OsuMania,
                lineIndex,
                ObjectIndex: objectIndex,
                SourceTimestamp: fields[2].Trim(),
                SourceType: isHold ? "hold" : "tap",
                RawValue: line));
        return true;
    }

    private static bool ParseUninherited(
        string raw,
        ICollection<Diagnostic> diagnostics,
        int lineIndex)
    {
        diagnostics.Add(new Diagnostic(
            DiagnosticSeverity.Warning,
            "OSU-TIMING-INHERITED",
            $"Timing point uninherited flag '{raw.Trim()}' is invalid at source line {lineIndex + 1}; treated as inherited."));
        return false;
    }

    private static Dictionary<string, string> GetSection(
        IReadOnlyDictionary<string, Dictionary<string, string>> sections,
        string name)
    {
        return sections.TryGetValue(name, out var result)
            ? result
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private static int? ParseInt(
        IReadOnlyDictionary<string, string> section,
        string key,
        ICollection<Diagnostic> diagnostics,
        string code)
    {
        if (!section.TryGetValue(key, out var raw)
            || !int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, code, $"{key} is missing or invalid."));
            return null;
        }

        return value;
    }

    private static decimal? ParseDecimal(
        IReadOnlyDictionary<string, string> section,
        string key,
        ICollection<Diagnostic> diagnostics,
        string code)
    {
        if (!section.TryGetValue(key, out var raw) || !TryParseDecimal(raw, out var value))
        {
            diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, code, $"{key} is missing or invalid."));
            return null;
        }

        return value;
    }

    private static bool TryParseDecimal(string raw, out decimal value) =>
        decimal.TryParse(
            raw.Trim(),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out value);

    private sealed record RawTimingPoint(
        int PointIndex,
        int LineIndex,
        string RawLine,
        decimal TimeMilliseconds,
        decimal BeatLengthMilliseconds,
        bool Uninherited,
        string TimeText,
        string BeatLengthText,
        SourceProvenance Provenance);

    private sealed record RawHitObject(
        int ObjectIndex,
        int LineIndex,
        string RawLine,
        decimal X,
        decimal TimeMilliseconds,
        int Type,
        bool IsCircle,
        bool IsHold,
        decimal? EndTimeMilliseconds,
        string TimeText,
        string? EndTimeText,
        SourceProvenance Provenance);
}
