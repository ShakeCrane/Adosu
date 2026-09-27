using System.Globalization;

namespace Adosu.Validation;

/// <summary>
/// Semantic-neutral comparator. It compares only the audit records stored in a
/// fixture: source ids, expected/actual target objects and the assertions built
/// on them. It never calls the Adosu Reader, Resolver or a converter, and it
/// never derives expected values from the current implementation's output.
///
/// Outcome precedence is fail-closed: INVALID FIXTURE beats INDETERMINATE beats
/// FAIL beats PASS. An assertion with no admissible evidence or numeric basis
/// is INDETERMINATE, never PASS. An unasserted dimension is NOT ASSERTED.
/// </summary>
public static class FixtureComparator
{
    public static FixtureValidationResult Compare(FixtureLoadResult loadResult)
    {
        if (!loadResult.IsValid || loadResult.Document is null)
        {
            return new FixtureValidationResult(
                "<unloaded>",
                ValidationOutcome.InvalidFixture,
                loadResult.Diagnostics);
        }

        return Compare(loadResult.Document);
    }

    public static FixtureValidationResult Compare(FixtureDocument fixture)
    {
        // Re-run structural integrity so an in-memory document cannot bypass the
        // loader. This keeps identity/mapping/interval rules authoritative.
        var integrity = FixtureLoader.ValidateIntegrity(fixture);
        if (integrity.Count > 0)
        {
            return new FixtureValidationResult(fixture.FixtureId, ValidationOutcome.InvalidFixture, integrity);
        }

        var diagnostics = new List<ValidationDiagnostic>();
        var outcomes = new List<ValidationOutcome>();

        if (fixture.Assertions.Count == 0)
        {
            diagnostics.Add(new ValidationDiagnostic(
                ValidationOutcome.NotAsserted,
                ValidationCodes.NotAsserted,
                "fixture declares no assertions",
                FixtureId: fixture.FixtureId));
            return new FixtureValidationResult(fixture.FixtureId, ValidationOutcome.NotAsserted, diagnostics);
        }

        foreach (var assertion in fixture.Assertions)
        {
            var outcome = CompareAssertion(fixture, assertion, diagnostics);
            outcomes.Add(outcome);
        }

        var mappingOutcome = CompareMappingTopology(fixture, diagnostics);
        if (mappingOutcome is not null)
        {
            outcomes.Add(mappingOutcome.Value);
        }

        var overall = Aggregate(outcomes);
        return new FixtureValidationResult(fixture.FixtureId, overall, diagnostics);
    }

    private static ValidationOutcome? CompareMappingTopology(
        FixtureDocument fixture,
        List<ValidationDiagnostic> diagnostics)
    {
        var asserted = fixture.Assertions
            .Where(assertion => assertion.Evidence.SupportsCorrectnessPass())
            .ToArray();
        var expected = BuildMappingTopology(asserted, assertion => assertion.ExpectedTargetObjectId);
        var actual = BuildMappingTopology(asserted, assertion => assertion.ActualTargetObjectId);

        if (expected.Count == 0 && actual.Count == 0)
        {
            return null;
        }

        if (MappingTopologiesEqual(expected, actual))
        {
            return null;
        }

        diagnostics.Add(new ValidationDiagnostic(
            ValidationOutcome.Fail,
            ValidationCodes.MappingMismatch,
            "expected and actual source-to-target mapping topology differs",
            FixtureId: fixture.FixtureId,
            Field: "mappingTopology",
            Expected: FormatMappingTopology(expected),
            Actual: FormatMappingTopology(actual)));
        return ValidationOutcome.Fail;
    }

    // Target ids belong to separate expected and actual namespaces. Compare
    // each side by the source set incident to each target, preserving distinct
    // targets even when they have identical source sets.
    private static List<HashSet<string>> BuildMappingTopology(
        IEnumerable<FixtureAssertion> assertions,
        Func<FixtureAssertion, string?> targetSelector)
    {
        var sourcesByTarget = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var assertion in assertions)
        {
            var targetId = targetSelector(assertion);
            if (string.IsNullOrWhiteSpace(targetId))
            {
                continue;
            }

            if (!sourcesByTarget.TryGetValue(targetId, out var sources))
            {
                sources = new HashSet<string>(StringComparer.Ordinal);
                sourcesByTarget[targetId] = sources;
            }

            foreach (var sourceId in assertion.SourceIds.Where(source => !string.IsNullOrWhiteSpace(source)))
            {
                sources.Add(sourceId);
            }
        }

        return sourcesByTarget.Values.ToList();
    }

    private static bool MappingTopologiesEqual(
        IReadOnlyList<HashSet<string>> expected,
        IReadOnlyList<HashSet<string>> actual)
    {
        if (expected.Count != actual.Count)
        {
            return false;
        }

        var unmatched = actual.Select(sources => new HashSet<string>(sources, StringComparer.Ordinal)).ToList();
        foreach (var expectedSources in expected)
        {
            var matchIndex = unmatched.FindIndex(actualSources => actualSources.SetEquals(expectedSources));
            if (matchIndex < 0)
            {
                return false;
            }

            unmatched.RemoveAt(matchIndex);
        }

        return unmatched.Count == 0;
    }

    private static string FormatMappingTopology(IReadOnlyList<HashSet<string>> topology)
    {
        var groups = topology
            .Select(sources => "{" + string.Join(", ", sources.OrderBy(source => source, StringComparer.Ordinal)) + "}")
            .OrderBy(group => group, StringComparer.Ordinal);
        return "[" + string.Join(", ", groups) + "]";
    }

    private static ValidationOutcome CompareAssertion(
        FixtureDocument fixture,
        FixtureAssertion assertion,
        List<ValidationDiagnostic> diagnostics)
    {
        if (assertion.Evidence == EvidenceLevel.NotAsserted)
        {
            diagnostics.Add(new ValidationDiagnostic(
                ValidationOutcome.NotAsserted,
                ValidationCodes.NotAsserted,
                $"assertion '{assertion.AssertionId}' is explicitly not asserted",
                FixtureId: fixture.FixtureId,
                AssertionId: assertion.AssertionId));
            return ValidationOutcome.NotAsserted;
        }

        // HYPOTHESIS / UNKNOWN may be loaded and diffed but never count as a
        // correctness PASS. Anything they would otherwise prove is downgraded to
        // INDETERMINATE so the fixture result cannot be read as verified.
        if (!assertion.Evidence.SupportsCorrectnessPass())
        {
            diagnostics.Add(new ValidationDiagnostic(
                ValidationOutcome.Indeterminate,
                ValidationCodes.EvidenceInsufficient,
                $"assertion '{assertion.AssertionId}' evidence level '{assertion.Evidence}' cannot support a correctness PASS",
                FixtureId: fixture.FixtureId,
                AssertionId: assertion.AssertionId));
            return ValidationOutcome.Indeterminate;
        }

        var worst = ValidationOutcome.Pass;
        var assertedAnyDimension = false;

        worst = Worst(worst, CompareScalar(
            fixture, assertion, "noteKind", assertion.ExpectedNoteKind, assertion.ActualNoteKind, diagnostics, ref assertedAnyDimension));

        worst = Worst(worst, CompareInterval(fixture, assertion, "start", assertion.ExpectedStart, assertion.ActualStart, diagnostics, ref assertedAnyDimension));
        worst = Worst(worst, CompareInterval(fixture, assertion, "end", assertion.ExpectedEnd, assertion.ActualEnd, diagnostics, ref assertedAnyDimension));

        worst = Worst(worst, CompareInt(
            fixture, assertion, "sameTimeGroupSize", assertion.ExpectedSameTimeGroupSize, assertion.ActualSameTimeGroupSize, diagnostics, ref assertedAnyDimension));
        worst = Worst(worst, CompareInt(
            fixture, assertion, "sameTimeOrdinal", assertion.ExpectedSameTimeOrdinal, assertion.ActualSameTimeOrdinal, diagnostics, ref assertedAnyDimension));

        worst = Worst(worst, CompareBaseSources(fixture, assertion, diagnostics, ref assertedAnyDimension));
        worst = Worst(worst, CompareApplicationOrder(fixture, assertion, diagnostics, ref assertedAnyDimension));
        worst = Worst(worst, CompareInt(
            fixture, assertion, "timingPointOrdinal", assertion.ExpectedTimingPointOrdinal, assertion.ActualTimingPointOrdinal, diagnostics, ref assertedAnyDimension));

        // An assertion that compares no dimension asserts nothing and must never
        // surface as a PASS. It is NOT ASSERTED rather than INDETERMINATE: no
        // dimension was claimed, so there is nothing to be undecidable about.
        if (!assertedAnyDimension && worst is ValidationOutcome.Pass)
        {
            diagnostics.Add(new ValidationDiagnostic(
                ValidationOutcome.NotAsserted,
                ValidationCodes.NotAsserted,
                $"assertion '{assertion.AssertionId}' declares no comparison dimension",
                FixtureId: fixture.FixtureId,
                AssertionId: assertion.AssertionId));
            return ValidationOutcome.NotAsserted;
        }

        if (worst == ValidationOutcome.Pass)
        {
            diagnostics.Add(new ValidationDiagnostic(
                ValidationOutcome.Pass,
                FieldMismatchPassCode,
                $"assertion '{assertion.AssertionId}' matched",
                FixtureId: fixture.FixtureId,
                AssertionId: assertion.AssertionId));
        }

        return worst;
    }

    private const string FieldMismatchPassCode = "CMP-PASS";

    private static ValidationOutcome CompareScalar(
        FixtureDocument fixture,
        FixtureAssertion assertion,
        string field,
        string? expected,
        string? actual,
        List<ValidationDiagnostic> diagnostics)
    {
        if (string.Equals(expected, actual, StringComparison.Ordinal))
        {
            return ValidationOutcome.Pass;
        }

        diagnostics.Add(new ValidationDiagnostic(
            ValidationOutcome.Fail,
            ValidationCodes.FieldMismatch,
            $"assertion '{assertion.AssertionId}' field '{field}' differs",
            FixtureId: fixture.FixtureId,
            AssertionId: assertion.AssertionId,
            Field: field,
            Expected: expected,
            Actual: actual));
        return ValidationOutcome.Fail;
    }

    private static ValidationOutcome CompareInt(
        FixtureDocument fixture,
        FixtureAssertion assertion,
        string field,
        int? expected,
        int? actual,
        List<ValidationDiagnostic> diagnostics)
    {
        if (expected is null || actual is null)
        {
            return ValidationOutcome.Pass;
        }

        if (expected == actual)
        {
            return ValidationOutcome.Pass;
        }

        diagnostics.Add(new ValidationDiagnostic(
            ValidationOutcome.Fail,
            ValidationCodes.FieldMismatch,
            $"assertion '{assertion.AssertionId}' field '{field}' differs",
            FixtureId: fixture.FixtureId,
            AssertionId: assertion.AssertionId,
            Field: field,
            Expected: expected.Value.ToString(CultureInfo.InvariantCulture),
            Actual: actual.Value.ToString(CultureInfo.InvariantCulture)));
        return ValidationOutcome.Fail;
    }

    private static ValidationOutcome CompareScalar(
        FixtureDocument fixture,
        FixtureAssertion assertion,
        string field,
        string? expected,
        string? actual,
        List<ValidationDiagnostic> diagnostics,
        ref bool assertedAnyDimension)
    {
        if (string.Equals(expected, actual, StringComparison.Ordinal))
        {
            if (expected is not null)
            {
                assertedAnyDimension = true;
            }

            return ValidationOutcome.Pass;
        }

        assertedAnyDimension = true;
        diagnostics.Add(new ValidationDiagnostic(
            ValidationOutcome.Fail,
            ValidationCodes.FieldMismatch,
            $"assertion '{assertion.AssertionId}' field '{field}' differs",
            FixtureId: fixture.FixtureId,
            AssertionId: assertion.AssertionId,
            Field: field,
            Expected: expected,
            Actual: actual));
        return ValidationOutcome.Fail;
    }

    private static ValidationOutcome CompareInt(
        FixtureDocument fixture,
        FixtureAssertion assertion,
        string field,
        int? expected,
        int? actual,
        List<ValidationDiagnostic> diagnostics,
        ref bool assertedAnyDimension)
    {
        if (expected is null || actual is null)
        {
            return ValidationOutcome.Pass;
        }

        assertedAnyDimension = true;
        if (expected == actual)
        {
            return ValidationOutcome.Pass;
        }

        diagnostics.Add(new ValidationDiagnostic(
            ValidationOutcome.Fail,
            ValidationCodes.FieldMismatch,
            $"assertion '{assertion.AssertionId}' field '{field}' differs",
            FixtureId: fixture.FixtureId,
            AssertionId: assertion.AssertionId,
            Field: field,
            Expected: expected.Value.ToString(CultureInfo.InvariantCulture),
            Actual: actual.Value.ToString(CultureInfo.InvariantCulture)));
        return ValidationOutcome.Fail;
    }

    /// <summary>
    /// Timing-point source dimension: the declared base source ids name the
    /// timing-point sources whose application this assertion concerns. Each must
    /// be present in the expected application order; the comparison never infers
    /// game semantics from note times.
    /// </summary>
    private static ValidationOutcome CompareBaseSources(
        FixtureDocument fixture,
        FixtureAssertion assertion,
        List<ValidationDiagnostic> diagnostics,
        ref bool assertedAnyDimension)
    {
        if (assertion.BaseSourceIds is null || assertion.BaseSourceIds.Length == 0)
        {
            return ValidationOutcome.Pass;
        }

        assertedAnyDimension = true;
        var order = assertion.ExpectedApplicationOrder;
        var expected = string.Join(",", assertion.BaseSourceIds);
        var actual = order is null ? string.Empty : string.Join(",", order);

        if (order is not null)
        {
            var orderSet = order.ToHashSet(StringComparer.Ordinal);
            if (assertion.BaseSourceIds.All(sourceId => orderSet.Contains(sourceId)))
            {
                return ValidationOutcome.Pass;
            }
        }

        diagnostics.Add(new ValidationDiagnostic(
            ValidationOutcome.Fail,
            ValidationCodes.BaseSourceMissing,
            $"assertion '{assertion.AssertionId}' base timing-point sources are not all present in the expected application order",
            FixtureId: fixture.FixtureId,
            AssertionId: assertion.AssertionId,
            Field: "baseSourceIds",
            Expected: expected,
            Actual: actual));
        return ValidationOutcome.Fail;
    }

    /// <summary>
    /// Explicit expected-vs-actual timing-point application order. Both arrays
    /// must be present for the dimension to be asserted; a mismatch is a FAIL,
    /// never a fixture error, and no order is ever inferred from note times.
    /// </summary>
    private static ValidationOutcome CompareApplicationOrder(
        FixtureDocument fixture,
        FixtureAssertion assertion,
        List<ValidationDiagnostic> diagnostics,
        ref bool assertedAnyDimension)
    {
        if (assertion.ExpectedApplicationOrder is null
            && assertion.ActualApplicationOrder is null
            && assertion.ExpectedTimingPointOrdinal is null
            && assertion.ActualTimingPointOrdinal is null)
        {
            return ValidationOutcome.Pass;
        }

        if (assertion.ExpectedApplicationOrder is null || assertion.ActualApplicationOrder is null)
        {
            assertedAnyDimension = true;
            diagnostics.Add(new ValidationDiagnostic(
                ValidationOutcome.Indeterminate,
                ValidationCodes.IndeterminateNumeric,
                $"assertion '{assertion.AssertionId}' timing-point order is asserted on only one side",
                FixtureId: fixture.FixtureId,
                AssertionId: assertion.AssertionId,
                Field: "applicationOrder"));
            return ValidationOutcome.Indeterminate;
        }

        assertedAnyDimension = true;
        var expected = assertion.ExpectedApplicationOrder;
        var actual = assertion.ActualApplicationOrder;
        if (expected.Length == actual.Length
            && expected.Zip(actual, (left, right) => string.Equals(left, right, StringComparison.Ordinal)).All(same => same))
        {
            return ValidationOutcome.Pass;
        }

        diagnostics.Add(new ValidationDiagnostic(
            ValidationOutcome.Fail,
            ValidationCodes.ApplicationOrderMismatch,
            $"assertion '{assertion.AssertionId}' timing-point application order differs",
            FixtureId: fixture.FixtureId,
            AssertionId: assertion.AssertionId,
            Field: "applicationOrder",
            Expected: string.Join(",", expected),
            Actual: string.Join(",", actual)));
        return ValidationOutcome.Fail;
    }

    private static ValidationOutcome CompareInterval(
        FixtureDocument fixture,
        FixtureAssertion assertion,
        string field,
        string? expectedText,
        string? actualText,
        List<ValidationDiagnostic> diagnostics,
        ref bool assertedAnyDimension)
    {
        if (expectedText is null || actualText is null)
        {
            return ValidationOutcome.Pass;
        }

        assertedAnyDimension = true;

        // N2: a malformed interval on the *actual* side is an output defect, not
        // an invalid fixture. It is reported as an output FAIL here and never
        // escalates to INVALID FIXTURE (which the expected-side gate owns).
        if (field == "end"
            && FixtureLoader.TryParseExact(assertion.ActualStart ?? string.Empty, out var actualStart)
            && FixtureLoader.TryParseExact(actualText, out var actualEnd)
            && actualEnd < actualStart)
        {
            diagnostics.Add(new ValidationDiagnostic(
                ValidationOutcome.Fail,
                ValidationCodes.MismatchedInterval,
                $"assertion '{assertion.AssertionId}' actual interval has end < start",
                FixtureId: fixture.FixtureId,
                AssertionId: assertion.AssertionId,
                Field: "actual.interval",
                Expected: $"end >= start (got end={actualText}, start={assertion.ActualStart})"));
            return ValidationOutcome.Fail;
        }

        if (assertion.TimeUnit is null || assertion.TimeOrigin is null)
        {
            diagnostics.Add(new ValidationDiagnostic(
                ValidationOutcome.Indeterminate,
                ValidationCodes.OriginMismatch,
                $"assertion '{assertion.AssertionId}' field '{field}' has no declared time unit/origin",
                FixtureId: fixture.FixtureId,
                AssertionId: assertion.AssertionId,
                Field: field));
            return ValidationOutcome.Indeterminate;
        }

        var policy = assertion.NumericPolicy;
        if (policy is null || !policy.IsAdmissible())
        {
            diagnostics.Add(new ValidationDiagnostic(
                ValidationOutcome.Indeterminate,
                ValidationCodes.NumericPolicyMissing,
                $"assertion '{assertion.AssertionId}' field '{field}' has no admissible numeric policy",
                FixtureId: fixture.FixtureId,
                AssertionId: assertion.AssertionId,
                Field: field));
            return ValidationOutcome.Indeterminate;
        }

        if (!FixtureLoader.TryParseExact(expectedText, out var expectedValue)
            || !FixtureLoader.TryParseExact(actualText, out var actualValue))
        {
            diagnostics.Add(new ValidationDiagnostic(
                ValidationOutcome.Indeterminate,
                ValidationCodes.IndeterminateNumeric,
                $"assertion '{assertion.AssertionId}' field '{field}' cannot be losslessly parsed as a finite number",
                FixtureId: fixture.FixtureId,
                AssertionId: assertion.AssertionId,
                Field: field,
                Expected: expectedText,
                Actual: actualText));
            return ValidationOutcome.Indeterminate;
        }

        bool matched;
        if (policy.IsExact)
        {
            matched = NumberMatchesExactly(expectedText, actualText);
        }
        else
        {
            matched = Math.Abs(expectedValue - actualValue) <= policy.Tolerance!.Value;
        }

        if (matched)
        {
            return ValidationOutcome.Pass;
        }

        diagnostics.Add(new ValidationDiagnostic(
            ValidationOutcome.Fail,
            ValidationCodes.FieldMismatch,
            $"assertion '{assertion.AssertionId}' field '{field}' differs beyond the declared policy",
            FixtureId: fixture.FixtureId,
            AssertionId: assertion.AssertionId,
            Field: field,
            Expected: expectedText,
            Actual: actualText,
            Policy: policy.IsExact ? NumericPolicy.ExactKind : $"{policy.Kind}:{policy.Tolerance}"));
        return ValidationOutcome.Fail;
    }

    /// <summary>
    /// Exact comparison uses exact decimal semantics. Two tokens pass only when
    /// they denote the same exact decimal value; a double round-trip collapse
    /// (for example 1.0 and 1.0000000000000001, or 0.1 and its longer form) must
    /// not make two distinct decimals compare equal.
    /// </summary>
    private static bool NumberMatchesExactly(string expectedText, string actualText)
    {
        var left = expectedText.Trim();
        var right = actualText.Trim();
        if (string.Equals(left, right, StringComparison.Ordinal))
        {
            return true;
        }

        if (!TryParseCanonicalDecimal(left, out var leftDigits, out var leftScale)
            || !TryParseCanonicalDecimal(right, out var rightDigits, out var rightScale))
        {
            return false;
        }

        if (leftScale != rightScale)
        {
            return false;
        }

        return string.Equals(leftDigits, rightDigits, StringComparison.Ordinal);
    }

    /// <summary>
    /// Parses a decimal literal into a sign-aware integer coefficient and a
    /// power-of-ten scale, so equality is decided on the exact value rather than
    /// on a lossy binary floating point conversion. Trailing zeros are
    /// normalised away, so "1.0", "1.00" and "1" are the same exact value.
    /// </summary>
    private static bool TryParseCanonicalDecimal(string text, out string digits, out int scale)
    {
        digits = string.Empty;
        scale = 0;
        if (text.Length == 0)
        {
            return false;
        }

        var negative = false;
        var index = 0;
        if (text[0] is '+' or '-')
        {
            negative = text[0] == '-';
            index = 1;
        }

        if (index >= text.Length)
        {
            return false;
        }

        var significand = new System.Text.StringBuilder();
        var fractionalDigits = 0;
        var sawDigit = false;
        for (; index < text.Length; index++)
        {
            var character = text[index];
            if (character is >= '0' and <= '9')
            {
                sawDigit = true;
                significand.Append(character);
                if (fractionalDigits > 0)
                {
                    fractionalDigits++;
                }

                continue;
            }

            if (character == '.')
            {
                if (fractionalDigits > 0)
                {
                    return false;
                }

                fractionalDigits = 1;
                continue;
            }

            return false;
        }

        if (!sawDigit)
        {
            return false;
        }

        var sign = significand.ToString();
        var scaleValue = fractionalDigits > 0 ? fractionalDigits - 1 : 0;
        var end = sign.Length;
        while (end > 0 && scaleValue > 0 && sign[end - 1] == '0')
        {
            end--;
            scaleValue--;
        }

        var trimmed = sign[..end];
        var allZero = trimmed.All(character => character == '0');
        digits = negative && !allZero ? "-" + trimmed : trimmed;
        if (allZero)
        {
            digits = "0";
        }

        scale = scaleValue;
        return true;
    }

    /// <summary>
    /// Aggregation precedence: InvalidFixture &gt; Indeterminate &gt; Fail &gt;
    /// Pass. NotAsserted combines with others as the lowest, since it asserts
    /// nothing; a fixture with only NOT ASSERTED results reports NotAsserted.
    /// </summary>
    internal static ValidationOutcome Aggregate(IReadOnlyList<ValidationOutcome> outcomes)
    {
        var worst = ValidationOutcome.NotAsserted;
        var sawNotAsserted = false;
        foreach (var outcome in outcomes)
        {
            if (outcome == ValidationOutcome.NotAsserted)
            {
                sawNotAsserted = true;
                continue;
            }

            worst = Worst(worst, outcome);
        }

        if (worst == ValidationOutcome.NotAsserted && sawNotAsserted)
        {
            return ValidationOutcome.NotAsserted;
        }

        return worst;
    }

    private static ValidationOutcome Worst(ValidationOutcome left, ValidationOutcome right)
    {
        if (left == ValidationOutcome.NotAsserted)
        {
            return right;
        }

        if (right == ValidationOutcome.NotAsserted)
        {
            return left;
        }

        return Rank(left) >= Rank(right) ? left : right;
    }

    private static int Rank(ValidationOutcome outcome) => outcome switch
    {
        ValidationOutcome.InvalidFixture => 4,
        ValidationOutcome.Indeterminate => 3,
        ValidationOutcome.Fail => 2,
        ValidationOutcome.Pass => 1,
        _ => 0
    };
}
