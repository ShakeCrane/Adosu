namespace Adosu.Validation;

/// <summary>
/// Stable machine-readable diagnostics emitted by the fixture loader and the
/// comparator. Codes are part of the validation contract and must not be
/// renumbered once published.
/// </summary>
public static class ValidationCodes
{
    // Loader / schema
    public const string SchemaVersionMissing = "FIX-SCHEMA-VERSION";
    public const string SchemaVersionUnsupported = "FIX-SCHEMA-VERSION-UNSUPPORTED";
    public const string MissingField = "FIX-MISSING-FIELD";
    public const string DuplicateId = "FIX-DUPLICATE-ID";
    public const string InvalidValue = "FIX-INVALID-VALUE";
    public const string InvalidJson = "FIX-INVALID-JSON";

    // N1: target addressing and mapping integrity
    public const string TargetObjectIdMissing = "FIX-TARGET-ID-MISSING";
    public const string TargetObjectIdDuplicate = "FIX-TARGET-ID-DUPLICATE";
    public const string SourceIdMissing = "FIX-SOURCE-ID-MISSING";
    public const string SourceIdDuplicate = "FIX-SOURCE-ID-DUPLICATE";
    public const string SourceInventoryMissing = "FIX-SOURCE-INVENTORY-MISSING";
    public const string SourceIdUnknown = "FIX-SOURCE-ID-UNKNOWN";
    public const string SourceDeclarationMalformed = "FIX-SOURCE-DECLARATION";
    public const string MappingIncomplete = "MAP-INCOMPLETE";
    public const string MappingExtra = "MAP-EXTRA";
    public const string MappingDuplicate = "MAP-DUPLICATE";
    public const string MappingAmbiguous = "MAP-AMBIGUOUS";
    public const string MappingStrategyMissing = "MAP-STRATEGY-MISSING";
    public const string MappingStrategyUnknown = "MAP-STRATEGY-UNKNOWN";
    public const string MappingStrategyMismatch = "MAP-STRATEGY-MISMATCH";
    public const string MappingStrategyRedundant = "MAP-STRATEGY-REDUNDANT";
    public const string MappingStrategyScopeIncomplete = "MAP-STRATEGY-SCOPE-INCOMPLETE";
    public const string MappingStrategyScopeInvalid = "MAP-STRATEGY-SCOPE-INVALID";

    // N2: invalid fixture vs output mismatch
    public const string InvalidExpectedInterval = "FIX-INVALID-INTERVAL";
    public const string MismatchedInterval = "CMP-INTERVAL-INVALID";

    // Timing-point base sources and application order
    public const string BaseSourceMissing = "CMP-BASE-SOURCE-MISSING";
    public const string ApplicationOrderMismatch = "CMP-ORDER-MISMATCH";

    // Comparison
    public const string FieldMismatch = "CMP-FIELD-MISMATCH";
    public const string MappingMismatch = "CMP-MAPPING-MISMATCH";
    public const string NumericPolicyMissing = "CMP-NUMERIC-POLICY-MISSING";
    public const string UnitMismatch = "CMP-UNIT-MISMATCH";
    public const string OriginMismatch = "CMP-ORIGIN-MISMATCH";
    public const string EvidenceInsufficient = "CMP-EVIDENCE-INSUFFICIENT";
    public const string NotAsserted = "CMP-NOT-ASSERTED";
    public const string IndeterminateNumeric = "CMP-INDETERMINATE-NUMERIC";
}

/// <summary>
/// The overall result of validating one fixture. Ordered by increasing
/// specificity is irrelevant; the load gate runs first and short-circuits.
/// </summary>
public enum ValidationOutcome
{
    /// <summary>Every asserted dimension matched and was backed by admissible evidence.</summary>
    Pass,

    /// <summary>An asserted field differed between expected and actual.</summary>
    Fail,

    /// <summary>A dimension was asserted but the fixture lacked the basis (precision, origin, unit, evidence).</summary>
    Indeterminate,

    /// <summary>The fixture itself is malformed; no comparison is meaningful.</summary>
    InvalidFixture,

    /// <summary>Nothing was asserted for this fixture.</summary>
    NotAsserted
}

public sealed record ValidationDiagnostic(
    ValidationOutcome Outcome,
    string Code,
    string Message,
    string? FixtureId = null,
    string? AssertionId = null,
    string? SourceId = null,
    string? TargetObjectId = null,
    string? Field = null,
    string? Expected = null,
    string? Actual = null,
    string? Policy = null);

/// <summary>
/// Per-field numeric policy. There is deliberately no fixture-level epsilon:
/// a tolerance is only honoured when an assertion carries an explicit value,
/// unit and rationale.
/// </summary>
public sealed record NumericPolicy(
    string Kind,
    double? Tolerance = null,
    string? Unit = null,
    string? Rationale = null)
{
    public const string ExactKind = "exact";
    public const string AbsoluteToleranceKind = "absolute";

    public static NumericPolicy Exact() => new(ExactKind);

    public bool IsExact => string.Equals(Kind, ExactKind, StringComparison.Ordinal);

    /// <summary>
    /// A tolerance policy is only admissible when it names the tolerance value,
    /// its unit and the rationale. Anything else fails closed.
    /// </summary>
    public bool IsAdmissible()
    {
        if (IsExact)
        {
            return true;
        }

        return string.Equals(Kind, AbsoluteToleranceKind, StringComparison.Ordinal)
               && Tolerance is not null
               && Tolerance >= 0
               && !string.IsNullOrWhiteSpace(Unit)
               && !string.IsNullOrWhiteSpace(Rationale);
    }
}

/// <summary>
/// Minimal, format-addressed target object. The <see cref="TargetObjectId"/> is
/// mandatory, explicit and unique within the fixture. The optional artifact
/// digest + ordinal form is the only allowed future addressing scheme for
/// formats without persistent ids.
/// </summary>
public sealed record TargetObject(
    string TargetObjectId,
    string? ArtifactDigest = null,
    int? Ordinal = null);

/// <summary>
/// A single comparison assertion. Every dimension is optional; an omitted
/// dimension is NOT ASSERTED, never an implicit pass.
/// </summary>
public sealed record FixtureAssertion(
    string AssertionId,
    EvidenceLevel Evidence,
    string[] SourceIds,
    string? ExpectedTargetObjectId,
    string? ActualTargetObjectId,
    string? ExpectedNoteKind = null,
    string? ActualNoteKind = null,
    string? ExpectedStart = null,
    string? ExpectedEnd = null,
    string? ActualStart = null,
    string? ActualEnd = null,
    int? ExpectedSameTimeGroupSize = null,
    int? ActualSameTimeGroupSize = null,
    int? ExpectedSameTimeOrdinal = null,
    int? ActualSameTimeOrdinal = null,
    string[]? BaseSourceIds = null,
    NumericPolicy? NumericPolicy = null,
    string? TimeUnit = null,
    string? TimeOrigin = null,
    string? Rationale = null,
    bool IsSourceOnly = false,
    string? ExpectedStrategyId = null,
    string? ActualStrategyId = null,
    string[]? ExpectedApplicationOrder = null,
    string[]? ActualApplicationOrder = null,
    int? ExpectedTimingPointOrdinal = null,
    int? ActualTimingPointOrdinal = null);

/// <summary>
/// An explicit, fixture-declared source object. Only declared source ids may be
/// referenced by assertions, so source existence is auditable instead of merely
/// syntactic. The id is in the canonical <see cref="SourceAddress"/> form.
/// </summary>
public sealed record SourceDeclaration(
    string SourceId,
    string? Kind = null);

/// <summary>
/// A declared mapping strategy that licenses exactly one 1:N or N:1 relation.
/// <see cref="StrategyId"/> is referenced by the assertion(s) that form the
/// relation, and expected and actual relations are licensed separately so a
/// strategy for one relation can never authorize another.
///
/// A 1:N strategy names <see cref="SourceId"/>, the single source that fans out.
/// An N:1 strategy names <see cref="SourceIds"/>, the complete set of sources
/// that merge, together with <see cref="TargetObjectId"/>, the single target
/// they merge into. Both endpoints of an N:1 relation are explicit: a source
/// scope that is absent, empty, singular or otherwise incomplete is not a valid
/// N:1 declaration and fails closed rather than silently licensing a relation it
/// does not name.
/// </summary>
public sealed record MappingStrategy(
    string StrategyId,
    string Kind,
    string Rationale,
    string? SourceId = null,
    string? TargetObjectId = null,
    string[]? SourceIds = null)
{
    public const string OneToManyKind = "1:N";
    public const string ManyToOneKind = "N:1";
    public const string OneToOneKind = "1:1";
}

public sealed record FixtureProvenance(
    string ArtifactId,
    string Format,
    string? FormatVersion,
    string? ArtifactDigest,
    string? Reference,
    string? Producer,
    string? RetrievedAt,
    string[]? GenerationSteps);

/// <summary>
/// A loaded, schema-validated fixture. Only a fixture that passes
/// <see cref="FixtureLoader"/> reaches the comparator.
/// </summary>
public sealed record FixtureDocument(
    int SchemaVersion,
    string FixtureId,
    FixtureProvenance Provenance,
    IReadOnlyList<SourceDeclaration> Sources,
    IReadOnlyList<TargetObject> ExpectedTargets,
    IReadOnlyList<TargetObject> ActualTargets,
    IReadOnlyList<MappingStrategy> MappingStrategies,
    IReadOnlyList<FixtureAssertion> Assertions,
    string? TimeOrigin,
    string? TimeUnit,
    bool HasWriter,
    string[]? ExcludedDomains);

public sealed record FixtureLoadResult(
    FixtureDocument? Document,
    IReadOnlyList<ValidationDiagnostic> Diagnostics)
{
    public bool IsValid => Document is not null && Diagnostics.Count == 0;
}

public sealed record FixtureValidationResult(
    string FixtureId,
    ValidationOutcome Outcome,
    IReadOnlyList<ValidationDiagnostic> Diagnostics)
{
    public bool IsPass => Outcome == ValidationOutcome.Pass;
}
