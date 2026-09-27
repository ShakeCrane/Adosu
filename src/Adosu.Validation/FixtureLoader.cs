using System.Globalization;
using System.Text.Json;

namespace Adosu.Validation;

/// <summary>
/// Versioned, fail-closed fixture loader. The loader never calls the Adosu
/// Reader, Resolver or any converter to construct expected data; it only reads
/// the externally authored fixture text. Any schema violation yields a
/// <see cref="ValidationOutcome.InvalidFixture"/> diagnostic and no document.
/// </summary>
public static class FixtureLoader
{
    public const int SupportedSchemaVersion = 1;

    public static FixtureLoadResult Load(string json)
    {
        var diagnostics = new List<ValidationDiagnostic>();

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Disallow,
                AllowTrailingCommas = false
            });
        }
        catch (JsonException exception)
        {
            diagnostics.Add(new ValidationDiagnostic(
                ValidationOutcome.InvalidFixture,
                ValidationCodes.InvalidJson,
                $"fixture is not valid JSON: {exception.Message}"));
            return new FixtureLoadResult(null, diagnostics);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                diagnostics.Add(Invalid(ValidationCodes.MissingField, "fixture root must be a JSON object"));
                return new FixtureLoadResult(null, diagnostics);
            }

            if (!root.TryGetProperty("schemaVersion", out var schemaVersionElement)
                || schemaVersionElement.ValueKind != JsonValueKind.Number
                || !schemaVersionElement.TryGetInt32(out var schemaVersion))
            {
                diagnostics.Add(Invalid(ValidationCodes.SchemaVersionMissing, "fixture must declare an integer schemaVersion"));
                return new FixtureLoadResult(null, diagnostics);
            }

            if (schemaVersion != SupportedSchemaVersion)
            {
                diagnostics.Add(Invalid(
                    ValidationCodes.SchemaVersionUnsupported,
                    $"unsupported schemaVersion {schemaVersion}; this loader supports {SupportedSchemaVersion}"));
                return new FixtureLoadResult(null, diagnostics);
            }

            var fixtureId = RequiredString(root, "fixtureId", diagnostics);

            if (!root.TryGetProperty("provenance", out var provenanceElement)
                || provenanceElement.ValueKind != JsonValueKind.Object)
            {
                diagnostics.Add(Invalid(ValidationCodes.MissingField, "fixture must declare a provenance object"));
                return new FixtureLoadResult(null, diagnostics);
            }

            var provenance = ReadProvenance(provenanceElement, diagnostics);

            var sources = ReadSources(root, diagnostics);
            var expectedTargets = ReadTargets(root, "expectedTargets", diagnostics);
            var actualTargets = ReadTargets(root, "actualTargets", diagnostics);
            var strategies = ReadStrategies(root, diagnostics);
            var assertions = ReadAssertions(root, diagnostics);

            var hasWriter = root.TryGetProperty("hasWriter", out var hasWriterElement)
                            && hasWriterElement.ValueKind == JsonValueKind.True;

            var timeOrigin = OptionalString(root, "timeOrigin");
            var timeUnit = OptionalString(root, "timeUnit");
            var excludedDomains = OptionalStringArray(root, "excludedDomains");

            if (diagnostics.Count > 0)
            {
                return new FixtureLoadResult(null, diagnostics);
            }

            var documentModel = new FixtureDocument(
                schemaVersion,
                fixtureId!,
                provenance!,
                sources,
                expectedTargets,
                actualTargets,
                strategies,
                assertions,
                timeOrigin,
                timeUnit,
                hasWriter,
                excludedDomains);

            // Integrity rules run before any comparison. A failure here means the
            // fixture itself is unusable, not that an output mismatched.
            var integrity = ValidateIntegrity(documentModel);
            if (integrity.Count > 0)
            {
                return new FixtureLoadResult(null, integrity);
            }

            return new FixtureLoadResult(documentModel, []);
        }
    }

    public static FixtureLoadResult LoadFile(string path) => Load(File.ReadAllText(path));

    /// <summary>
    /// N1 and N2 structural rules: an explicit source inventory with unique,
    /// well-formed addresses, unique target ids, complete/unambiguous mapping
    /// endpoints, a strategy bound to the exact relation it licenses, and the
    /// expected-only invalid interval rule.
    /// </summary>
    internal static List<ValidationDiagnostic> ValidateIntegrity(FixtureDocument fixture)
    {
        var diagnostics = new List<ValidationDiagnostic>();
        var reportedSourceIds = new HashSet<string>(StringComparer.Ordinal);

        var declaredSourceIds = ValidateSources(fixture, diagnostics);
        if (declaredSourceIds.Count == 0)
        {
            diagnostics.Add(Invalid(
                ValidationCodes.SourceInventoryMissing,
                "fixture must declare a non-empty sources[] inventory; source existence cannot be checked without it",
                fixtureId: fixture.FixtureId));
        }

        ValidateTargets(fixture, fixture.ExpectedTargets, "expectedTargets", diagnostics);
        ValidateTargets(fixture, fixture.ActualTargets, "actualTargets", diagnostics);

        var expectedIds = fixture.ExpectedTargets.Select(target => target.TargetObjectId).ToHashSet(StringComparer.Ordinal);
        var actualIds = fixture.ActualTargets.Select(target => target.TargetObjectId).ToHashSet(StringComparer.Ordinal);
        var strategyById = ValidateStrategies(fixture, diagnostics);

        var assertionIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var assertion in fixture.Assertions)
        {
            if (string.IsNullOrWhiteSpace(assertion.AssertionId))
            {
                diagnostics.Add(Invalid(
                    ValidationCodes.MissingField,
                    "every assertion must declare an assertionId"));
                continue;
            }

            if (!assertionIds.Add(assertion.AssertionId))
            {
                diagnostics.Add(Invalid(
                    ValidationCodes.DuplicateId,
                    $"duplicate assertionId '{assertion.AssertionId}'"));
            }

            ValidateSourceIds(fixture, assertion, declaredSourceIds, reportedSourceIds, diagnostics);

            if (!assertion.IsSourceOnly && assertion.ExpectedTargetObjectId is null && assertion.ActualTargetObjectId is null)
            {
                diagnostics.Add(Invalid(
                    ValidationCodes.TargetObjectIdMissing,
                    $"assertion '{assertion.AssertionId}' must be either a source-only assertion or reference a target object id",
                    assertionId: assertion.AssertionId));
            }

            ValidateTargetReference(
                fixture, expectedIds, assertion.ExpectedTargetObjectId, "expected", assertion.AssertionId, diagnostics);
            ValidateTargetReference(
                fixture, actualIds, assertion.ActualTargetObjectId, "actual", assertion.AssertionId, diagnostics);

            ValidateInterval(fixture, assertion, diagnostics);
            ValidatePolicy(assertion, diagnostics);
            ValidateRelationStrategy(fixture, assertion, strategyById, diagnostics);
            ValidateBaseSources(fixture, assertion, declaredSourceIds, reportedSourceIds, diagnostics);
            ValidateApplicationOrder(fixture, assertion, declaredSourceIds, reportedSourceIds, diagnostics);
        }

        ValidateRelationCoverage(fixture, strategyById, diagnostics);
        ValidateStrategyUsage(fixture, diagnostics);

        // No declared target endpoint may go unaddressed: an unreferenced
        // expected or actual target is an extra mapping endpoint.
        var referencedExpected = fixture.Assertions
            .Where(assertion => !assertion.IsSourceOnly)
            .Select(assertion => assertion.ExpectedTargetObjectId)
            .Where(id => id is not null)
            .ToHashSet(StringComparer.Ordinal);
        var referencedActual = fixture.Assertions
            .Where(assertion => !assertion.IsSourceOnly)
            .Select(assertion => assertion.ActualTargetObjectId)
            .Where(id => id is not null)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var target in fixture.ExpectedTargets)
        {
            if (!referencedExpected.Contains(target.TargetObjectId))
            {
                diagnostics.Add(Invalid(
                    ValidationCodes.MappingExtra,
                    $"expected targetObjectId '{target.TargetObjectId}' is declared but never addressed by any assertion",
                    fixtureId: fixture.FixtureId,
                    targetObjectId: target.TargetObjectId));
            }
        }

        foreach (var target in fixture.ActualTargets)
        {
            if (!referencedActual.Contains(target.TargetObjectId))
            {
                diagnostics.Add(Invalid(
                    ValidationCodes.MappingExtra,
                    $"actual targetObjectId '{target.TargetObjectId}' is declared but never addressed by any assertion",
                    fixtureId: fixture.FixtureId,
                    targetObjectId: target.TargetObjectId));
            }
        }

        return diagnostics;
    }

    /// <summary>
    /// N1: the source inventory. Every declared source id must be a well-formed
    /// canonical <see cref="SourceAddress"/> and unique. This is what makes an
    /// assertion's source reference auditable as "exists" rather than merely
    /// syntactically plausible.
    /// </summary>
    private static HashSet<string> ValidateSources(FixtureDocument fixture, List<ValidationDiagnostic> diagnostics)
    {
        var declared = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in fixture.Sources)
        {
            if (string.IsNullOrWhiteSpace(source.SourceId))
            {
                diagnostics.Add(Invalid(
                    ValidationCodes.SourceIdMissing,
                    "sources[] contains a declaration without a sourceId",
                    fixtureId: fixture.FixtureId));
                continue;
            }

            if (!IsWellFormedSourceId(source.SourceId))
            {
                diagnostics.Add(Invalid(
                    ValidationCodes.SourceDeclarationMalformed,
                    $"sources[] sourceId '{source.SourceId}' is not a well-formed canonical source address",
                    fixtureId: fixture.FixtureId,
                    sourceId: source.SourceId));
                continue;
            }

            if (!declared.Add(source.SourceId))
            {
                diagnostics.Add(Invalid(
                    ValidationCodes.SourceIdUnknown,
                    $"sources[] contains duplicate sourceId '{source.SourceId}'",
                    fixtureId: fixture.FixtureId,
                    sourceId: source.SourceId));
            }
        }

        return declared;
    }

    /// <summary>
    /// N1: every source id an assertion references must resolve to a declared
    /// source. Empty references are reported once per assertion; unknown
    /// references are reported once per (assertion, source) pair.
    /// </summary>
    private static void ValidateSourceIds(
        FixtureDocument fixture,
        FixtureAssertion assertion,
        HashSet<string> declaredSourceIds,
        HashSet<string> reportedSourceIds,
        List<ValidationDiagnostic> diagnostics)
    {
        if (assertion.SourceIds is null || assertion.SourceIds.Length == 0)
        {
            diagnostics.Add(Invalid(
                ValidationCodes.SourceIdMissing,
                $"assertion '{assertion.AssertionId}' must reference at least one sourceId",
                assertionId: assertion.AssertionId));
            return;
        }

        var assertionSourceIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var sourceId in assertion.SourceIds)
        {
            if (string.IsNullOrWhiteSpace(sourceId))
            {
                diagnostics.Add(Invalid(
                    ValidationCodes.SourceIdMissing,
                    $"assertion '{assertion.AssertionId}' references an empty sourceId",
                    assertionId: assertion.AssertionId));
                continue;
            }

            if (!assertionSourceIds.Add(sourceId))
            {
                diagnostics.Add(Invalid(
                    ValidationCodes.SourceIdDuplicate,
                    $"assertion '{assertion.AssertionId}' references duplicate sourceId '{sourceId}'",
                    fixtureId: fixture.FixtureId,
                    assertionId: assertion.AssertionId,
                    sourceId: sourceId));
                continue;
            }

            if (reportedSourceIds.Add(sourceId))
            {
                if (!IsWellFormedSourceId(sourceId))
                {
                    diagnostics.Add(Invalid(
                        ValidationCodes.SourceDeclarationMalformed,
                        $"assertion '{assertion.AssertionId}' references malformed sourceId '{sourceId}'",
                        fixtureId: fixture.FixtureId,
                        assertionId: assertion.AssertionId,
                        sourceId: sourceId));
                }
                else if (!declaredSourceIds.Contains(sourceId))
                {
                    diagnostics.Add(Invalid(
                        ValidationCodes.SourceIdUnknown,
                        $"assertion '{assertion.AssertionId}' references sourceId '{sourceId}' that is not declared in sources[]",
                        fixtureId: fixture.FixtureId,
                        assertionId: assertion.AssertionId,
                        sourceId: sourceId));
                }
            }
        }
    }

    private static bool IsWellFormedSourceId(string sourceId)
    {
        try
        {
            _ = SourceAddressAdapter.FromId(sourceId);
            return true;
        }
        catch (Exception exception) when (exception is FormatException or OverflowException)
        {
            return false;
        }
    }

    private static void ValidateBaseSources(
        FixtureDocument fixture,
        FixtureAssertion assertion,
        HashSet<string> declaredSourceIds,
        HashSet<string> reportedSourceIds,
        List<ValidationDiagnostic> diagnostics)
    {
        foreach (var baseSourceId in assertion.BaseSourceIds ?? [])
        {
            if (string.IsNullOrWhiteSpace(baseSourceId))
            {
                diagnostics.Add(Invalid(
                    ValidationCodes.BaseSourceMissing,
                    $"assertion '{assertion.AssertionId}' references an empty baseSourceId",
                    assertionId: assertion.AssertionId));
                continue;
            }

            if (!reportedSourceIds.Add(baseSourceId))
            {
                continue;
            }

            if (!IsWellFormedSourceId(baseSourceId))
            {
                diagnostics.Add(Invalid(
                    ValidationCodes.SourceDeclarationMalformed,
                    $"assertion '{assertion.AssertionId}' references malformed baseSourceId '{baseSourceId}'",
                    fixtureId: fixture.FixtureId,
                    assertionId: assertion.AssertionId,
                    sourceId: baseSourceId));
            }
            else if (!declaredSourceIds.Contains(baseSourceId))
            {
                diagnostics.Add(Invalid(
                    ValidationCodes.SourceIdUnknown,
                    $"assertion '{assertion.AssertionId}' references baseSourceId '{baseSourceId}' that is not declared in sources[]",
                    fixtureId: fixture.FixtureId,
                    assertionId: assertion.AssertionId,
                    sourceId: baseSourceId));
            }
        }
    }

    private static void ValidateApplicationOrder(
        FixtureDocument fixture,
        FixtureAssertion assertion,
        HashSet<string> declaredSourceIds,
        HashSet<string> reportedSourceIds,
        List<ValidationDiagnostic> diagnostics)
    {
        Check(assertion.ExpectedApplicationOrder, "expectedApplicationOrder");
        Check(assertion.ActualApplicationOrder, "actualApplicationOrder");

        void Check(string[]? order, string field)
        {
            if (order is null)
            {
                return;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var sourceId in order)
            {
                if (string.IsNullOrWhiteSpace(sourceId))
                {
                    diagnostics.Add(Invalid(
                        ValidationCodes.ApplicationOrderMismatch,
                        $"assertion '{assertion.AssertionId}' {field} contains an empty source id",
                        assertionId: assertion.AssertionId,
                        field: field));
                    continue;
                }

                if (!seen.Add(sourceId))
                {
                    diagnostics.Add(Invalid(
                        ValidationCodes.ApplicationOrderMismatch,
                        $"assertion '{assertion.AssertionId}' {field} lists sourceId '{sourceId}' more than once",
                        assertionId: assertion.AssertionId,
                        field: field,
                        sourceId: sourceId));
                    continue;
                }

                if (!reportedSourceIds.Add(sourceId))
                {
                    continue;
                }

                if (!IsWellFormedSourceId(sourceId))
                {
                    diagnostics.Add(Invalid(
                        ValidationCodes.SourceDeclarationMalformed,
                        $"assertion '{assertion.AssertionId}' {field} references malformed sourceId '{sourceId}'",
                        fixtureId: fixture.FixtureId,
                        assertionId: assertion.AssertionId,
                        field: field,
                        sourceId: sourceId));
                }
                else if (!declaredSourceIds.Contains(sourceId))
                {
                    diagnostics.Add(Invalid(
                        ValidationCodes.SourceIdUnknown,
                        $"assertion '{assertion.AssertionId}' {field} references sourceId '{sourceId}' that is not declared in sources[]",
                        fixtureId: fixture.FixtureId,
                        assertionId: assertion.AssertionId,
                        field: field,
                        sourceId: sourceId));
                }
            }
        }
    }

    private static Dictionary<string, MappingStrategy> ValidateStrategies(
        FixtureDocument fixture,
        List<ValidationDiagnostic> diagnostics)
    {
        var byId = new Dictionary<string, MappingStrategy>(StringComparer.Ordinal);
        foreach (var strategy in fixture.MappingStrategies)
        {
            if (string.IsNullOrWhiteSpace(strategy.StrategyId))
            {
                diagnostics.Add(Invalid(
                    ValidationCodes.MappingStrategyMissing,
                    "mappingStrategies[] contains a strategy without a strategyId",
                    fixtureId: fixture.FixtureId));
                continue;
            }

            if (!byId.TryAdd(strategy.StrategyId, strategy))
            {
                diagnostics.Add(Invalid(
                    ValidationCodes.DuplicateId,
                    $"duplicate mapping strategyId '{strategy.StrategyId}'",
                    fixtureId: fixture.FixtureId));
            }

            ValidateStrategyShape(fixture, strategy, diagnostics);
        }

        return byId;
    }

    /// <summary>
    /// Fail-closed shape rules for a declared strategy. An N:1 relation has two
    /// endpoints that are both mandatory: the complete, distinct set of
    /// contributing sources and the single target they merge into. A missing,
    /// empty, singular or duplicate source set cannot name an N:1 relation and
    /// must never be silently ignored. Conversely a 1:N strategy must not carry
    /// the plural N:1 source set, nor an N:1 strategy a singular <c>sourceId</c>,
    /// so an inapplicable field is rejected rather than ignored.
    /// </summary>
    private static void ValidateStrategyShape(
        FixtureDocument fixture,
        MappingStrategy strategy,
        List<ValidationDiagnostic> diagnostics)
    {
        var manyToOne = string.Equals(strategy.Kind, MappingStrategy.ManyToOneKind, StringComparison.Ordinal);
        var oneToMany = string.Equals(strategy.Kind, MappingStrategy.OneToManyKind, StringComparison.Ordinal);

        if (!manyToOne && !oneToMany)
        {
            return;
        }

        if (strategy.SourceId is not null && strategy.SourceIds is not null)
        {
            diagnostics.Add(Invalid(
                ValidationCodes.MappingStrategyScopeInvalid,
                $"mapping strategy '{strategy.StrategyId}' declares both 'sourceId' and 'sourceIds'; only one source scope form applies",
                fixtureId: fixture.FixtureId));
        }

        if (manyToOne)
        {
            if (strategy.SourceId is not null)
            {
                diagnostics.Add(Invalid(
                    ValidationCodes.MappingStrategyScopeInvalid,
                    $"mapping strategy '{strategy.StrategyId}' is N:1 and must declare its source set as 'sourceIds', not the singular 'sourceId'",
                    fixtureId: fixture.FixtureId,
                    sourceId: strategy.SourceId));
            }

            if (strategy.SourceIds is null || strategy.SourceIds.Length < 2)
            {
                diagnostics.Add(Invalid(
                    ValidationCodes.MappingStrategyScopeIncomplete,
                    $"mapping strategy '{strategy.StrategyId}' is N:1 and must declare the complete 'sourceIds' set of at least two distinct contributors",
                    fixtureId: fixture.FixtureId));
            }
            else
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var sourceId in strategy.SourceIds)
                {
                    if (string.IsNullOrWhiteSpace(sourceId) || !seen.Add(sourceId))
                    {
                        diagnostics.Add(Invalid(
                            ValidationCodes.MappingStrategyScopeIncomplete,
                            $"mapping strategy '{strategy.StrategyId}' N:1 sourceIds must be distinct, non-empty source ids",
                            fixtureId: fixture.FixtureId));
                        break;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(strategy.TargetObjectId))
            {
                diagnostics.Add(Invalid(
                    ValidationCodes.MappingStrategyScopeIncomplete,
                    $"mapping strategy '{strategy.StrategyId}' is N:1 and must declare a 'targetObjectId'",
                    fixtureId: fixture.FixtureId));
            }
        }

        if (oneToMany)
        {
            if (string.IsNullOrWhiteSpace(strategy.SourceId))
            {
                diagnostics.Add(Invalid(
                    ValidationCodes.MappingStrategyScopeIncomplete,
                    $"mapping strategy '{strategy.StrategyId}' is 1:N and must declare a non-empty 'sourceId'",
                    fixtureId: fixture.FixtureId));
            }

            if (strategy.SourceIds is not null)
            {
                diagnostics.Add(Invalid(
                    ValidationCodes.MappingStrategyScopeInvalid,
                    $"mapping strategy '{strategy.StrategyId}' is 1:N and must declare its source scope as the singular 'sourceId', not 'sourceIds'",
                    fixtureId: fixture.FixtureId));
            }
        }
    }

    private static void ValidateTargets(
        FixtureDocument fixture,
        IReadOnlyList<TargetObject> targets,
        string side,
        List<ValidationDiagnostic> diagnostics)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var target in targets)
        {
            if (string.IsNullOrWhiteSpace(target.TargetObjectId))
            {
                diagnostics.Add(Invalid(
                    ValidationCodes.TargetObjectIdMissing,
                    $"{side} contains a target without a targetObjectId",
                    fixtureId: fixture.FixtureId));
                continue;
            }

            if (!seen.Add(target.TargetObjectId))
            {
                diagnostics.Add(Invalid(
                    ValidationCodes.TargetObjectIdDuplicate,
                    $"{side} contains duplicate targetObjectId '{target.TargetObjectId}'",
                    fixtureId: fixture.FixtureId,
                    targetObjectId: target.TargetObjectId));
            }
        }
    }

    private static void ValidateTargetReference(
        FixtureDocument fixture,
        HashSet<string> knownIds,
        string? targetId,
        string side,
        string assertionId,
        List<ValidationDiagnostic> diagnostics)
    {
        if (targetId is null)
        {
            return;
        }

        if (!knownIds.Contains(targetId))
        {
            diagnostics.Add(Invalid(
                ValidationCodes.MappingIncomplete,
                $"assertion '{assertionId}' references {side} targetObjectId '{targetId}' that does not exist",
                fixtureId: fixture.FixtureId,
                assertionId: assertionId,
                targetObjectId: targetId));
        }
    }

    /// <summary>
    /// N2: an <em>expected</em> record whose end precedes its start is an invalid
    /// fixture, never a conversion FAIL. <c>end &gt;= start</c> is accepted. The
    /// actual side is deliberately not checked here: malformed actual output is
    /// an output result, never a reclassification of the input fixture.
    /// </summary>
    private static void ValidateInterval(
        FixtureDocument fixture,
        FixtureAssertion assertion,
        List<ValidationDiagnostic> diagnostics)
    {
        ValidateIntervalSide(fixture, assertion, assertion.ExpectedStart, assertion.ExpectedEnd, "expected", diagnostics);
    }

    private static void ValidateIntervalSide(
        FixtureDocument fixture,
        FixtureAssertion assertion,
        string? startText,
        string? endText,
        string side,
        List<ValidationDiagnostic> diagnostics)
    {
        if (startText is null || endText is null)
        {
            return;
        }

        if (!TryParseExact(startText, out var start) || !TryParseExact(endText, out var end))
        {
            // Precision/format problems are reported by the numeric policy gate,
            // not here; the interval rule only applies to comparable values.
            return;
        }

        if (end < start)
        {
            diagnostics.Add(new ValidationDiagnostic(
                ValidationOutcome.InvalidFixture,
                ValidationCodes.InvalidExpectedInterval,
                $"assertion '{assertion.AssertionId}' {side} interval has end < start",
                FixtureId: fixture.FixtureId,
                AssertionId: assertion.AssertionId,
                Field: $"{side}.interval",
                Expected: $"end >= start (got end={endText}, start={startText})"));
        }
    }

    private static void ValidatePolicy(FixtureAssertion assertion, List<ValidationDiagnostic> diagnostics)
    {
        if (assertion.NumericPolicy is null)
        {
            return;
        }

        if (!assertion.NumericPolicy.IsAdmissible())
        {
            diagnostics.Add(Invalid(
                ValidationCodes.InvalidValue,
                $"assertion '{assertion.AssertionId}' declares an inadmissible numericPolicy '{assertion.NumericPolicy.Kind}'",
                assertionId: assertion.AssertionId));
        }
    }

    /// <summary>
    /// N1: an in-assertion many-to-one merge must be licensed by a strategy that
    /// is bound to exactly this relation. 1:1 relations need no strategy; a
    /// fixture-level 1:N fan-out is licensed where it is actually observed, in
    /// <see cref="ValidateRelationCoverage"/>.
    /// </summary>
    private static void ValidateRelationStrategy(
        FixtureDocument fixture,
        FixtureAssertion assertion,
        Dictionary<string, MappingStrategy> strategyById,
        List<ValidationDiagnostic> diagnostics)
    {
        var mergedSources = assertion.SourceIds?.Where(source => !string.IsNullOrWhiteSpace(source))
            .Distinct(StringComparer.Ordinal).ToArray() ?? [];

        // A repeated identical source id collapses to a single relation; only a
        // genuine many-to-one merge needs a strategy. The reference is required
        // on the expected side because that is where the merge is declared.
        if (mergedSources.Length > 1 && assertion.ExpectedTargetObjectId is not null)
        {
            RequireBoundStrategy(
                fixture,
                assertion,
                MappingStrategy.ManyToOneKind,
                assertion.ExpectedStrategyId,
                mergedSources,
                assertion.ExpectedTargetObjectId,
                "expected merge",
                strategyById,
                diagnostics);
        }

        // An actual-side merge is a relation observed in the output; it must be
        // licensed by an actual strategy reference too, never by the expected
        // strategy of a different relation.
        if (mergedSources.Length > 1 && assertion.ActualTargetObjectId is not null)
        {
            RequireBoundStrategy(
                fixture,
                assertion,
                MappingStrategy.ManyToOneKind,
                assertion.ActualStrategyId,
                mergedSources,
                assertion.ActualTargetObjectId,
                "actual merge",
                strategyById,
                diagnostics);
        }
    }

    /// <summary>
    /// Resolves an assertion's strategy reference and verifies that the strategy
    /// kind matches the relation it is being used for and that its declared
    /// scope covers the exact relation: the complete set of merge sources and
    /// the single target. An unknown, mismatched, narrower or wider reference
    /// fails closed; no strategy authorizes a different source set or target.
    /// </summary>
    private static void RequireBoundStrategy(
        FixtureDocument fixture,
        FixtureAssertion assertion,
        string? requiredKind,
        string? strategyId,
        string[]? mergedSources,
        string? targetObjectId,
        string relation,
        Dictionary<string, MappingStrategy> strategyById,
        List<ValidationDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(strategyId) || !strategyById.TryGetValue(strategyId, out var strategy))
        {
            diagnostics.Add(Invalid(
                ValidationCodes.MappingStrategyMissing,
                $"assertion '{assertion.AssertionId}' declares a {relation} relation without a resolvable strategyId",
                fixtureId: fixture.FixtureId,
                assertionId: assertion.AssertionId));
            return;
        }

        if (requiredKind is not null
            && !string.Equals(strategy.Kind, requiredKind, StringComparison.Ordinal))
        {
            diagnostics.Add(Invalid(
                ValidationCodes.MappingStrategyMismatch,
                $"assertion '{assertion.AssertionId}' uses strategy '{strategyId}' of kind '{strategy.Kind}' for a {requiredKind} {relation} relation",
                fixtureId: fixture.FixtureId,
                assertionId: assertion.AssertionId));
            return;
        }

        if (string.Equals(requiredKind, MappingStrategy.ManyToOneKind, StringComparison.Ordinal)
            && mergedSources is not null)
        {
            ValidateManyToOneScope(
                fixture,
                assertion,
                strategy,
                mergedSources,
                targetObjectId,
                relation,
                diagnostics);
        }
    }

    /// <summary>
    /// The N:1 scope must name exactly the participating source set and target
    /// relation. A scope that omits a contributor, adds a non-participant or
    /// names a different target is rejected. This is what stops the previous
    /// singular/absent <c>sourceId</c> scope from being silently ignored.
    /// </summary>
    private static void ValidateManyToOneScope(
        FixtureDocument fixture,
        FixtureAssertion assertion,
        MappingStrategy strategy,
        string[] mergedSources,
        string? targetObjectId,
        string relation,
        List<ValidationDiagnostic> diagnostics)
    {
        var declared = (strategy.SourceIds ?? []).Where(source => !string.IsNullOrWhiteSpace(source))
            .ToHashSet(StringComparer.Ordinal);
        var actual = mergedSources.ToHashSet(StringComparer.Ordinal);

        var missing = actual.Where(source => !declared.Contains(source)).ToArray();
        var extra = declared.Where(source => !actual.Contains(source)).ToArray();
        if (missing.Length > 0 || extra.Length > 0)
        {
            diagnostics.Add(Invalid(
                ValidationCodes.MappingStrategyScopeIncomplete,
                $"strategy '{strategy.StrategyId}' N:1 source scope does not name exactly the {relation} source set",
                fixtureId: fixture.FixtureId,
                assertionId: assertion.AssertionId,
                expected: string.Join(",", strategy.SourceIds ?? []),
                actual: string.Join(",", mergedSources)));
            return;
        }

        if (targetObjectId is not null
            && !string.Equals(strategy.TargetObjectId, targetObjectId, StringComparison.Ordinal))
        {
            diagnostics.Add(Invalid(
                ValidationCodes.MappingStrategyMismatch,
                $"strategy '{strategy.StrategyId}' licenses target '{strategy.TargetObjectId}' but the {relation} relation names '{targetObjectId}'",
                fixtureId: fixture.FixtureId,
                assertionId: assertion.AssertionId,
                targetObjectId: targetObjectId));
        }
    }

    /// <summary>
    /// Fixture-level relation detection across assertions. A 1:N fan-out and an
    /// N:1 merge are each licensed by a strategy scoped to exactly that
    /// relation; a strategy declared for a different relation never authorizes
    /// another, and same-time coincidence never pairs objects.
    /// </summary>
    private static void ValidateRelationCoverage(
        FixtureDocument fixture,
        Dictionary<string, MappingStrategy> strategyById,
        List<ValidationDiagnostic> diagnostics)
    {
        ValidateManyToOneCoverage(fixture, strategyById, diagnostics);
        ValidateOneToManyCoverage(fixture, strategyById, diagnostics);
    }

    /// <summary>
    /// Fixture-level N:1 detection across assertions: distinct source ids that
    /// each converge on the same target object on one side form a many-to-one
    /// relation even when no single assertion names more than one source. That
    /// exact relation must be licensed by an N:1 strategy whose declared source
    /// set is precisely the set of contributing sources and whose target is
    /// precisely the shared target. A strategy for any other relation never
    /// authorizes it.
    /// </summary>
    private static void ValidateManyToOneCoverage(
        FixtureDocument fixture,
        Dictionary<string, MappingStrategy> strategyById,
        List<ValidationDiagnostic> diagnostics)
    {
        foreach (var side in new[] { "expected", "actual" })
        {
            // Bucket every addressed target by its distinct contributing source
            // ids on this side. A merge exists when a target collects more than
            // one distinct source, whether those sources share an assertion or
            // are split across several.
            var targetSources = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var targetStrategyIds = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var assertion in fixture.Assertions)
            {
                var targetId = side == "expected"
                    ? assertion.ExpectedTargetObjectId
                    : assertion.ActualTargetObjectId;
                if (targetId is null)
                {
                    continue;
                }

                var sources = assertion.SourceIds?.Where(source => !string.IsNullOrWhiteSpace(source))
                    .Distinct(StringComparer.Ordinal) ?? [];
                foreach (var sourceId in sources)
                {
                    if (!targetSources.TryGetValue(targetId, out var contributing))
                    {
                        contributing = new HashSet<string>(StringComparer.Ordinal);
                        targetSources[targetId] = contributing;
                    }

                    contributing.Add(sourceId);
                }

                var strategyId = side == "expected"
                    ? assertion.ExpectedStrategyId
                    : assertion.ActualStrategyId;
                if (!string.IsNullOrWhiteSpace(strategyId))
                {
                    if (!targetStrategyIds.TryGetValue(targetId, out var declared))
                    {
                        declared = new HashSet<string>(StringComparer.Ordinal);
                        targetStrategyIds[targetId] = declared;
                    }

                    declared.Add(strategyId);
                }
            }

            foreach (var (targetId, sources) in targetSources)
            {
                if (sources.Count <= 1)
                {
                    continue;
                }

                var declaredStrategyIds = targetStrategyIds.TryGetValue(targetId, out var ids)
                    ? ids
                    : new HashSet<string>(StringComparer.Ordinal);
                if (IsMergeFullyLicensed(declaredStrategyIds, sources, targetId, strategyById))
                {
                    continue;
                }

                diagnostics.Add(Invalid(
                    ValidationCodes.MappingAmbiguous,
                    $"targetObjectId '{targetId}' collects {sources.Count} distinct {side} sources without an N:1 mapping strategy declaring exactly that source set and target",
                    fixtureId: fixture.FixtureId,
                    targetObjectId: targetId));
            }
        }
    }

    /// <summary>
    /// A cross-assertion merge is licensed only when the strategies referenced
    /// by the converging assertions include an N:1 strategy covering the exact
    /// source set and target. A strategy that omits a contributor, adds a
    /// non-participant or names another target does not license the relation.
    /// </summary>
    private static bool IsMergeFullyLicensed(
        HashSet<string> declaredStrategyIds,
        HashSet<string> sources,
        string targetId,
        Dictionary<string, MappingStrategy> strategyById)
    {
        foreach (var strategyId in declaredStrategyIds)
        {
            if (!strategyById.TryGetValue(strategyId, out var strategy)
                || !string.Equals(strategy.Kind, MappingStrategy.ManyToOneKind, StringComparison.Ordinal)
                || !string.Equals(strategy.TargetObjectId, targetId, StringComparison.Ordinal))
            {
                continue;
            }

            var scope = (strategy.SourceIds ?? []).Where(source => !string.IsNullOrWhiteSpace(source))
                .ToHashSet(StringComparer.Ordinal);
            if (scope.SetEquals(sources))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Fixture-level 1:N detection: a source id that fans out to more than one
    /// distinct endpoint on one side must be licensed by a 1:N strategy whose
    /// declared source scope is exactly that source id.
    /// </summary>
    private static void ValidateOneToManyCoverage(
        FixtureDocument fixture,
        Dictionary<string, MappingStrategy> strategyById,
        List<ValidationDiagnostic> diagnostics)
    {
        // Expected and actual are compared independently, so a 1:N relation is
        // only within one side. Bucketing both sides together would make every
        // ordinary 1:1 fixture look one-to-many.
        var expectedFanOut = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var actualFanOut = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var assertion in fixture.Assertions)
        {
            foreach (var sourceId in assertion.SourceIds.Distinct(StringComparer.Ordinal))
            {
                AddEndpoint(expectedFanOut, sourceId, assertion.ExpectedTargetObjectId);
                AddEndpoint(actualFanOut, sourceId, assertion.ActualTargetObjectId);
            }
        }

        foreach (var (side, fanOut) in new[] { ("expected", expectedFanOut), ("actual", actualFanOut) })
        {
            foreach (var (sourceId, targets) in fanOut)
            {
                if (targets.Count <= 1)
                {
                    continue;
                }

                if (IsFanOutFullyLicensed(fixture, sourceId, side, strategyById))
                {
                    continue;
                }

                diagnostics.Add(Invalid(
                    ValidationCodes.MappingAmbiguous,
                    $"sourceId '{sourceId}' maps to {targets.Count} distinct {side} targets without a 1:N mapping strategy scoped to that source on every contributing assertion",
                    fixtureId: fixture.FixtureId,
                    sourceId: sourceId));
            }
        }
    }

    /// <summary>
    /// A 1:N fan-out is licensed only when every assertion that contributes a
    /// distinct endpoint for this source on this side carries a strategy
    /// reference resolving to a declared 1:N strategy whose source scope is
    /// exactly this source (and, when present, whose target scope is the
    /// endpoint the assertion names).
    /// </summary>
    private static bool IsFanOutFullyLicensed(
        FixtureDocument fixture,
        string sourceId,
        string side,
        Dictionary<string, MappingStrategy> strategyById)
    {
        var contributed = false;
        foreach (var assertion in fixture.Assertions)
        {
            if (!assertion.SourceIds.Distinct(StringComparer.Ordinal).Contains(sourceId, StringComparer.Ordinal))
            {
                continue;
            }

            var targetId = side == "expected"
                ? assertion.ExpectedTargetObjectId
                : assertion.ActualTargetObjectId;
            if (targetId is null)
            {
                continue;
            }

            contributed = true;

            var strategyId = side == "expected"
                ? assertion.ExpectedStrategyId
                : assertion.ActualStrategyId;
            if (strategyId is null
                || !strategyById.TryGetValue(strategyId, out var strategy)
                || !string.Equals(strategy.Kind, MappingStrategy.OneToManyKind, StringComparison.Ordinal)
                || !string.Equals(strategy.SourceId, sourceId, StringComparison.Ordinal)
                || (strategy.TargetObjectId is not null
                    && !string.Equals(strategy.TargetObjectId, targetId, StringComparison.Ordinal)))
            {
                return false;
            }
        }

        return contributed;
    }

    /// <summary>
    /// A declared 1:N / N:1 strategy must actually be referenced by at least one
    /// assertion, otherwise its scope is unused and the fixture over-licenses.
    /// </summary>
    private static void ValidateStrategyUsage(FixtureDocument fixture, List<ValidationDiagnostic> diagnostics)
    {
        foreach (var strategy in fixture.MappingStrategies)
        {
            if (string.IsNullOrWhiteSpace(strategy.StrategyId)
                || !IsNonTrivialKind(strategy.Kind))
            {
                continue;
            }

            var referenced = fixture.Assertions.Any(assertion =>
                string.Equals(assertion.ExpectedStrategyId, strategy.StrategyId, StringComparison.Ordinal)
                || string.Equals(assertion.ActualStrategyId, strategy.StrategyId, StringComparison.Ordinal));
            if (!referenced)
            {
                diagnostics.Add(Invalid(
                    ValidationCodes.MappingStrategyRedundant,
                    $"mapping strategy '{strategy.StrategyId}' is declared but never referenced by any assertion",
                    fixtureId: fixture.FixtureId));
            }
        }
    }

    private static bool IsNonTrivialKind(string kind) =>
        string.Equals(kind, MappingStrategy.OneToManyKind, StringComparison.Ordinal)
        || string.Equals(kind, MappingStrategy.ManyToOneKind, StringComparison.Ordinal);

    private static void AddEndpoint(Dictionary<string, HashSet<string>> map, string sourceId, string? targetId)
    {
        if (targetId is null)
        {
            return;
        }

        if (!map.TryGetValue(sourceId, out var targets))
        {
            targets = new HashSet<string>(StringComparer.Ordinal);
            map[sourceId] = targets;
        }

        targets.Add(targetId);
    }

    internal static bool TryParseExact(string text, out double value) =>
        double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
        && !double.IsNaN(value)
        && !double.IsInfinity(value);

    private static FixtureProvenance? ReadProvenance(JsonElement element, List<ValidationDiagnostic> diagnostics)
    {
        var artifactId = RequiredString(element, "artifactId", diagnostics);
        var format = RequiredString(element, "format", diagnostics);
        if (artifactId is null || format is null)
        {
            return null;
        }

        return new FixtureProvenance(
            artifactId,
            format,
            OptionalString(element, "formatVersion"),
            OptionalString(element, "artifactDigest"),
            OptionalString(element, "reference"),
            OptionalString(element, "producer"),
            OptionalString(element, "retrievedAt"),
            OptionalStringArray(element, "generationSteps"));
    }

    private static List<TargetObject> ReadTargets(
        JsonElement root,
        string property,
        List<ValidationDiagnostic> diagnostics)
    {
        var targets = new List<TargetObject>();
        if (!root.TryGetProperty(property, out var element))
        {
            return targets;
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            diagnostics.Add(Invalid(ValidationCodes.InvalidValue, $"{property} must be an array"));
            return targets;
        }

        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                diagnostics.Add(Invalid(ValidationCodes.InvalidValue, $"{property} entries must be objects"));
                continue;
            }

            var id = OptionalString(item, "targetObjectId");
            if (id is null)
            {
                diagnostics.Add(Invalid(
                    ValidationCodes.TargetObjectIdMissing,
                    $"{property} entry is missing the mandatory targetObjectId"));
                continue;
            }

            int? ordinal = null;
            if (item.TryGetProperty("ordinal", out var ordinalElement)
                && ordinalElement.ValueKind == JsonValueKind.Number
                && ordinalElement.TryGetInt32(out var parsedOrdinal))
            {
                ordinal = parsedOrdinal;
            }

            targets.Add(new TargetObject(id, OptionalString(item, "artifactDigest"), ordinal));
        }

        return targets;
    }

    private static List<SourceDeclaration> ReadSources(JsonElement root, List<ValidationDiagnostic> diagnostics)
    {
        var sources = new List<SourceDeclaration>();
        if (!root.TryGetProperty("sources", out var element))
        {
            return sources;
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            diagnostics.Add(Invalid(ValidationCodes.InvalidValue, "sources must be an array"));
            return sources;
        }

        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                sources.Add(new SourceDeclaration(item.GetString()!));
                continue;
            }

            if (item.ValueKind != JsonValueKind.Object)
            {
                diagnostics.Add(Invalid(ValidationCodes.InvalidValue, "sources entries must be strings or objects"));
                continue;
            }

            var sourceId = OptionalString(item, "sourceId");
            if (sourceId is null)
            {
                diagnostics.Add(Invalid(
                    ValidationCodes.SourceIdMissing,
                    "sources entry is missing the mandatory sourceId"));
                continue;
            }

            sources.Add(new SourceDeclaration(sourceId, OptionalString(item, "kind")));
        }

        return sources;
    }

    private static List<MappingStrategy> ReadStrategies(JsonElement root, List<ValidationDiagnostic> diagnostics)
    {
        var strategies = new List<MappingStrategy>();
        if (!root.TryGetProperty("mappingStrategies", out var element))
        {
            return strategies;
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            diagnostics.Add(Invalid(ValidationCodes.InvalidValue, "mappingStrategies must be an array"));
            return strategies;
        }

        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                diagnostics.Add(Invalid(ValidationCodes.InvalidValue, "mappingStrategies entries must be objects"));
                continue;
            }

            var strategyId = RequiredString(item, "strategyId", diagnostics);
            var kind = RequiredString(item, "kind", diagnostics);
            var rationale = RequiredString(item, "rationale", diagnostics);
            if (strategyId is null || kind is null || rationale is null)
            {
                continue;
            }

            strategies.Add(new MappingStrategy(
                strategyId,
                kind,
                rationale,
                OptionalString(item, "sourceId"),
                OptionalString(item, "targetObjectId"),
                OptionalStringArray(item, "sourceIds")));
        }

        return strategies;
    }

    private static List<FixtureAssertion> ReadAssertions(JsonElement root, List<ValidationDiagnostic> diagnostics)
    {
        var assertions = new List<FixtureAssertion>();
        if (!root.TryGetProperty("assertions", out var element) || element.ValueKind != JsonValueKind.Array)
        {
            return assertions;
        }

        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                diagnostics.Add(Invalid(ValidationCodes.InvalidValue, "assertions entries must be objects"));
                continue;
            }

            var assertionId = RequiredString(item, "assertionId", diagnostics);
            if (assertionId is null)
            {
                continue;
            }

            assertions.Add(new FixtureAssertion(
                assertionId,
                ParseEvidence(OptionalString(item, "evidence")),
                OptionalStringArray(item, "sourceIds") ?? [],
                OptionalString(item, "expectedTargetObjectId"),
                OptionalString(item, "actualTargetObjectId"),
                OptionalString(item, "expectedNoteKind"),
                OptionalString(item, "actualNoteKind"),
                OptionalString(item, "expectedStart"),
                OptionalString(item, "expectedEnd"),
                OptionalString(item, "actualStart"),
                OptionalString(item, "actualEnd"),
                OptionalInt(item, "expectedSameTimeGroupSize"),
                OptionalInt(item, "actualSameTimeGroupSize"),
                OptionalInt(item, "expectedSameTimeOrdinal"),
                OptionalInt(item, "actualSameTimeOrdinal"),
                OptionalStringArray(item, "baseSourceIds"),
                ReadPolicy(item),
                OptionalString(item, "timeUnit"),
                OptionalString(item, "timeOrigin"),
                OptionalString(item, "rationale"),
                OptionalBool(item, "sourceOnly") ?? false,
                OptionalString(item, "expectedStrategyId"),
                OptionalString(item, "actualStrategyId"),
                OptionalStringArray(item, "expectedApplicationOrder"),
                OptionalStringArray(item, "actualApplicationOrder"),
                OptionalInt(item, "expectedTimingPointOrdinal"),
                OptionalInt(item, "actualTimingPointOrdinal")));
        }

        return assertions;
    }

    private static NumericPolicy? ReadPolicy(JsonElement item)
    {
        if (!item.TryGetProperty("numericPolicy", out var element) || element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        double? tolerance = null;
        if (element.TryGetProperty("tolerance", out var toleranceElement)
            && toleranceElement.ValueKind == JsonValueKind.Number
            && toleranceElement.TryGetDouble(out var parsedTolerance))
        {
            tolerance = parsedTolerance;
        }

        return new NumericPolicy(
            OptionalString(element, "kind") ?? string.Empty,
            tolerance,
            OptionalString(element, "unit"),
            OptionalString(element, "rationale"));
    }

    private static EvidenceLevel ParseEvidence(string? value) => value switch
    {
        "FORMAT_VERIFIED" => EvidenceLevel.FormatVerified,
        "REFERENCE_VERSION_OBSERVED" => EvidenceLevel.ReferenceVersionObserved,
        "GAME_VERSION_OBSERVED" => EvidenceLevel.GameVersionObserved,
        "HYPOTHESIS" => EvidenceLevel.Hypothesis,
        "UNKNOWN" => EvidenceLevel.Unknown,
        "NOT_ASSERTED" => EvidenceLevel.NotAsserted,
        _ => EvidenceLevel.Unknown
    };

    private static string? RequiredString(JsonElement element, string property, List<ValidationDiagnostic> diagnostics)
    {
        var value = OptionalString(element, property);
        if (value is null)
        {
            diagnostics.Add(Invalid(ValidationCodes.MissingField, $"missing required string field '{property}'"));
        }

        return value;
    }

    private static string? OptionalString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? OptionalInt(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var parsed)
            ? parsed
            : null;

    private static bool? OptionalBool(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static string[]? OptionalStringArray(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var items = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                items.Add(item.GetString()!);
            }
        }

        return [.. items];
    }

    private static ValidationDiagnostic Invalid(
        string code,
        string message,
        string? fixtureId = null,
        string? assertionId = null,
        string? sourceId = null,
        string? targetObjectId = null,
        string? field = null,
        string? expected = null,
        string? actual = null) =>
        new(
            ValidationOutcome.InvalidFixture,
            code,
            message,
            FixtureId: fixtureId,
            AssertionId: assertionId,
            SourceId: sourceId,
            TargetObjectId: targetObjectId,
            Field: field,
            Expected: expected,
            Actual: actual);
}
