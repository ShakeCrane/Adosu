using System.Globalization;
using System.Text.Json;
using Adosu.Core.Model;
using Adosu.Core.Timing;

namespace Adosu.Core.Parsing;

public static class AdofaiReader
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
        var source = text.TrimStart('\uFEFF');

        using var document = JsonDocument.Parse(
            source,
            new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });

        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("ADOFAI root must be a JSON object.");
        }

        var settings = root.TryGetProperty("settings", out var settingsElement)
            ? settingsElement
            : default;
        var bpmRaw = ReadRawValue(settings, "bpm");
        var offsetRaw = ReadRawValue(settings, "offset");
        var bpm = ReadDouble(settings, "bpm", diagnostics, "ADF001");
        var offset = ReadDouble(settings, "offset", diagnostics, "ADF002");
        var pathData = ReadString(root, "pathData");
        var directions = ParseDirections(root, pathData, diagnostics);
        var actions = ParseActions(root, diagnostics);

        var metadata = new ChartMetadata(
            SourceFormat.AdoFai,
            BaseBpm: bpm,
            AudioOffsetMilliseconds: offset,
            RawPathData: pathData,
            DirectionTokens: directions,
            BaseBpmOriginal: bpmRaw,
            AudioOffsetOriginal: offsetRaw);

        var parsed = new AdofaiDocument(
            directions,
            actions,
            bpm,
            offset,
            bpmRaw,
            offsetRaw,
            metadata,
            diagnostics);

        return AdofaiTimingResolver.Resolve(parsed);
    }

    private static IReadOnlyList<DirectionToken> ParseDirections(
        JsonElement root,
        string? pathData,
        ICollection<Diagnostic> diagnostics)
    {
        if (root.TryGetProperty("angleData", out var angleData)
            && angleData.ValueKind == JsonValueKind.Array)
        {
            var result = new List<DirectionToken>();
            var index = 0;
            foreach (var value in angleData.EnumerateArray())
            {
                var raw = value.GetRawText();
                if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var angle))
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticSeverity.Error,
                        "ADF003",
                        $"angleData[{index}] is not numeric: {raw}"));
                    angle = 0;
                }

                var midspin = angle == 999;
                result.Add(new DirectionToken(
                    index,
                    raw,
                    angle,
                    midspin,
                    new SourceProvenance(
                        SourceFormat.AdoFai,
                        index,
                        FloorIndex: index,
                        SourceType: "angleData",
                        RawValue: raw)));
                index++;
            }

            return result;
        }

        if (pathData is null)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Error,
                "ADF004",
                "ADOFAI level has neither angleData nor pathData."));
            return [];
        }

        var standardAngles = new Dictionary<char, double>
        {
            ['R'] = 0, ['p'] = 15, ['J'] = 30, ['E'] = 45, ['T'] = 60,
            ['o'] = 75, ['U'] = 90, ['q'] = 105, ['G'] = 120, ['Q'] = 135,
            ['H'] = 150, ['W'] = 165, ['L'] = 180, ['x'] = 195, ['N'] = 210,
            ['Z'] = 225, ['F'] = 240, ['V'] = 255, ['D'] = 270, ['Y'] = 285,
            ['B'] = 300, ['C'] = 315, ['M'] = 330, ['A'] = 345, ['!'] = 999
        };
        var relativeAngles = new Dictionary<char, double>
        {
            ['5'] = 72, ['6'] = -72, ['7'] = 52, ['8'] = -52, ['9'] = -30,
            ['h'] = 120, ['j'] = -120, ['t'] = 60, ['y'] = 300
        };

        var directionsFromPath = new List<DirectionToken>(pathData.Length);
        var previous = 0d;
        for (var index = 0; index < pathData.Length; index++)
        {
            var token = pathData[index].ToString();
            double angle;
            if (standardAngles.TryGetValue(pathData[index], out var absolute))
            {
                angle = absolute;
            }
            else if (relativeAngles.TryGetValue(pathData[index], out var relative))
            {
                angle = previous + relative;
            }
            else
            {
                angle = previous;
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Warning,
                    "ADF005",
                    $"Unknown pathData direction '{token}' was retained as the previous angle.",
                    new SourceProvenance(
                        SourceFormat.AdoFai,
                        index,
                        FloorIndex: index,
                        SourceType: "pathData",
                        RawValue: token)));
            }

            var midspin = angle == 999;
            directionsFromPath.Add(new DirectionToken(
                index,
                token,
                angle,
                midspin,
                new SourceProvenance(
                    SourceFormat.AdoFai,
                    index,
                    FloorIndex: index,
                    SourceType: "pathData",
                    RawValue: token)));
            previous = angle;
        }

        return directionsFromPath;
    }

    private static IReadOnlyList<AdofaiAction> ParseActions(
        JsonElement root,
        ICollection<Diagnostic> diagnostics)
    {
        if (!root.TryGetProperty("actions", out var actionsElement)
            || actionsElement.ValueKind != JsonValueKind.Array)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Warning,
                "ADF006",
                "ADOFAI level has no actions array; continuing with path and settings only."));
            return [];
        }

        var actions = new List<AdofaiAction>();
        var sourceIndex = 0;
        foreach (var action in actionsElement.EnumerateArray())
        {
            if (action.ValueKind != JsonValueKind.Object)
            {
                // The raw entry is genuinely retained, not dropped: it becomes
                // a source event and, via the resolver, an unknown gameplay
                // state change. It carries no usable floor index, so the
                // resolver reports it with ADF-FLOOR-RANGE.
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Warning,
                    "ADF007",
                    $"actions[{sourceIndex}] is not an object; its raw JSON is retained as a source record with no usable floor index."));
                actions.Add(new AdofaiAction(sourceIndex, null, "Unknown", action.Clone()));
                sourceIndex++;
                continue;
            }

            var floor = ReadInt(action, "floor");
            var eventType = ReadString(action, "eventType") ?? "Unknown";
            actions.Add(new AdofaiAction(sourceIndex, floor, eventType, action.Clone()));
            sourceIndex++;
        }

        return actions;
    }

    private static string? ReadString(JsonElement element, string property)
    {
        return element.ValueKind == JsonValueKind.Object
               && element.TryGetProperty(property, out var value)
               && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static string? ReadRawValue(JsonElement element, string property)
    {
        return element.ValueKind == JsonValueKind.Object
               && element.TryGetProperty(property, out var value)
            ? value.GetRawText()
            : null;
    }

    private static int? ReadInt(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(property, out var value))
        {
            return null;
        }

        return value.TryGetInt32(out var result) ? result : null;
    }

    private static double? ReadDouble(
        JsonElement element,
        string property,
        ICollection<Diagnostic> diagnostics,
        string code)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(property, out var value))
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Warning,
                code,
                $"settings.{property} is missing."));
            return null;
        }

        var raw = value.GetRawText();
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
        {
            return result;
        }

        diagnostics.Add(new Diagnostic(
            DiagnosticSeverity.Error,
            code,
            $"settings.{property} is not numeric: {raw}"));
        return null;
    }
}

internal sealed record AdofaiAction(
    int SourceIndex,
    int? FloorIndex,
    string EventType,
    JsonElement? Raw);

internal sealed record AdofaiDocument(
    IReadOnlyList<DirectionToken> Directions,
    IReadOnlyList<AdofaiAction> Actions,
    double? BaseBpm,
    double? AudioOffsetMilliseconds,
    string? BaseBpmOriginal,
    string? AudioOffsetOriginal,
    ChartMetadata Metadata,
    IReadOnlyList<Diagnostic> InitialDiagnostics);
