using Adosu.Validation;

/// <summary>
/// Focused synthetic acceptance for the semantic-neutral fixture
/// loader/comparator. Every fixture here is hand-authored and uses no game
/// behaviour: these tests prove the validation infrastructure, not ADOFAI or
/// osu! mechanics. No test reads <c>.sample/</c>.
/// </summary>
internal static class FixtureValidationTests
{
    private const string S0 = "synthetic|d0|hitObject#0";
    private const string S1 = "synthetic|d0|hitObject#1";
    private const string F2 = "synthetic|d0|floor#2";

    public static readonly (string Name, Action Test)[] All =
    [
        ("validation: positive load and compare passes", PositiveLoadAndCompare),
        ("validation: source-only assertion passes without a target", SourceOnlyAssertion),
        ("validation: explicit 1:N strategy licenses one-to-many", OneToManyWithStrategy),
        ("validation: referenced 1:N strategy without sourceId is invalid", OneToManyStrategyWithoutSourceId),
        ("validation: 1:N without a declared strategy is invalid", OneToManyWithoutStrategy),
        ("validation: declared strategy does not license an unrelated fan-out", StrategyScopeDoesNotLeak),
        ("validation: committed 1:N strategy must be referenced", UnusedStrategyIsRejected),
        ("validation: explicit N:1 strategy licenses many-to-one", ManyToOneWithStrategy),
        ("validation: N:1 strategy omitting a contributor is rejected", ManyToOneStrategyOmitsContributor),
        ("validation: two single-source assertions converging need an N:1 strategy", CrossAssertionMergeWithoutStrategy),
        ("validation: explicit N:1 strategy licenses a cross-assertion merge", CrossAssertionMergeWithStrategy),
        ("validation: expected merge versus actual split fails mapping comparison", ExpectedMergeVsActualSplitFails),
        ("validation: N:1 strategy does not leak to a different relation", ManyToOneStrategyDoesNotLeak),
        ("validation: missing target cannot be found (incomplete mapping)", MissingTargetIsIncomplete),
        ("validation: extra actual target is detected", ExtraActualTarget),
        ("validation: duplicate target object id is rejected", DuplicateTargetId),
        ("validation: missing source id is rejected", MissingSourceId),
        ("validation: duplicate source id within assertion is rejected", DuplicateSourceIdWithinAssertion),
        ("validation: undeclared source id is rejected even when well-formed", UndeclaredSourceId),
        ("validation: malformed source id is rejected", MalformedSourceId),
        ("validation: missing source inventory is rejected", MissingSourceInventory),
        ("validation: duplicate assertion id is rejected", DuplicateAssertionId),
        ("validation: swapped same-time order fails", SameTimeOrderSwapFails),
        ("validation: same-time cardinality mismatch fails", SameTimeCardinalityFails),
        ("validation: wrong LN end time fails", WrongEndTimeFails),
        ("validation: wrong note kind fails", WrongNoteKindFails),
        ("validation: expected end < start is INVALID FIXTURE not FAIL", ExpectedEndBeforeStartIsInvalid),
        ("validation: actual end < start is FAIL, never INVALID FIXTURE", ActualEndBeforeStartIsFail),
        ("validation: actual end >= start stays valid", ActualEndEqualsStartIsLegal),
        ("validation: end == start is accepted as a legal interval", EndEqualsStartIsLegal),
        ("validation: unit mismatch is INVALID FIXTURE via policy", MissingTimeUnitIsIndeterminate),
        ("validation: policy missing gives INDETERMINATE", MissingPolicyIsIndeterminate),
        ("validation: global epsilon is not honoured (only per-assertion)", NumericToleranceIsPerAssertion),
        ("validation: inadmissible tolerance fails closed", InadmissibleToleranceIsInvalid),
        ("validation: exact policy rejects decimals that collapse to one double", ExactPolicyRejectsDoubleCollapse),
        ("validation: timing-point application order mismatch fails", TimingPointOrderMismatchFails),
        ("validation: timing-point application order match passes", TimingPointOrderMatchPasses),
        ("validation: assertion with no comparison dimension is not a PASS", EmptyAssertionIsNotPass),
        ("validation: hypothesis evidence never counts as PASS", HypothesisEvidenceIsIndeterminate),
        ("validation: unknown evidence never counts as PASS", UnknownEvidenceIsIndeterminate),
        ("validation: not-asserted dimension is NOT ASSERTED", NotAssertedDimension),
        ("validation: unsupported schema version is INVALID FIXTURE", UnsupportedSchemaVersion),
        ("validation: source address round-trips through the adapter", SourceAddressRoundTrip)
    ];

    private static void PositiveLoadAndCompare()
    {
        var result = LoadAndCompare("""
            {
              "schemaVersion": 1,
              "fixtureId": "positive",
              "provenance": { "artifactId": "synth-a", "format": "synthetic", "artifactDigest": "d0" },
              "timeOrigin": "fixture-start",
              "timeUnit": "seconds",
              "sources": [ "synthetic|d0|hitObject#0" ],
              "expectedTargets": [ { "targetObjectId": "t1" } ],
              "actualTargets": [ { "targetObjectId": "t1" } ],
              "assertions": [
                {
                  "assertionId": "a1",
                  "evidence": "FORMAT_VERIFIED",
                  "sourceIds": ["synthetic|d0|hitObject#0"],
                  "expectedTargetObjectId": "t1",
                  "actualTargetObjectId": "t1",
                  "expectedNoteKind": "tap",
                  "actualNoteKind": "tap",
                  "expectedStart": "0.125",
                  "actualStart": "0.125",
                  "expectedEnd": "1.0",
                  "actualEnd": "1.0",
                  "timeUnit": "seconds",
                  "timeOrigin": "fixture-start",
                  "numericPolicy": { "kind": "exact" }
                }
              ]
            }
            """);

        Assert.Equal(ValidationOutcome.Pass, result.Outcome);
        Assert.True(result.Diagnostics.Any(d => d.Code == "CMP-PASS"));
    }

    private static void SourceOnlyAssertion()
    {
        var result = LoadAndCompare("""
            {
              "schemaVersion": 1,
              "fixtureId": "source-only",
              "provenance": { "artifactId": "synth-b", "format": "synthetic", "artifactDigest": "d0" },
              "timeOrigin": "fixture-start",
              "timeUnit": "seconds",
              "sources": [ "synthetic|d0|floor#2" ],
              "assertions": [
                {
                  "assertionId": "s1",
                  "evidence": "FORMAT_VERIFIED",
                  "sourceIds": ["synthetic|d0|floor#2"],
                  "sourceOnly": true,
                  "expectedNoteKind": "tap",
                  "actualNoteKind": "tap",
                  "expectedStart": "2.5",
                  "actualStart": "2.5",
                  "timeUnit": "seconds",
                  "timeOrigin": "fixture-start",
                  "numericPolicy": { "kind": "exact" }
                }
              ]
            }
            """);

        Assert.Equal(ValidationOutcome.Pass, result.Outcome);
    }

    private static void OneToManyWithStrategy()
    {
        // The same 1:N fan-out as the undeclared case, now licensed by a
        // strategy scoped to exactly this source on the expected side.
        var result = LoadAndCompare("""
            {
              "schemaVersion": 1,
              "fixtureId": "one-to-many",
              "provenance": { "artifactId": "synth-c", "format": "synthetic", "artifactDigest": "d0" },
              "timeOrigin": "fixture-start",
              "timeUnit": "seconds",
              "sources": [ "synthetic|d0|hitObject#0" ],
              "expectedTargets": [ { "targetObjectId": "e1" }, { "targetObjectId": "e2" } ],
              "actualTargets": [ { "targetObjectId": "a1" }, { "targetObjectId": "a2" } ],
              "mappingStrategies": [
                {
                  "strategyId": "m1",
                  "kind": "1:N",
                  "rationale": "declared expected split strategy",
                  "sourceId": "synthetic|d0|hitObject#0"
                },
                {
                  "strategyId": "m1a",
                  "kind": "1:N",
                  "rationale": "declared actual split strategy",
                  "sourceId": "synthetic|d0|hitObject#0"
                }
              ],
              "assertions": [
                {
                  "assertionId": "s1",
                  "evidence": "FORMAT_VERIFIED",
                  "sourceIds": ["synthetic|d0|hitObject#0"],
                  "expectedTargetObjectId": "e1",
                  "actualTargetObjectId": "a1",
                  "expectedStrategyId": "m1",
                  "actualStrategyId": "m1a",
                  "expectedSameTimeGroupSize": 2,
                  "actualSameTimeGroupSize": 2,
                  "expectedSameTimeOrdinal": 0,
                  "actualSameTimeOrdinal": 0
                },
                {
                  "assertionId": "s2",
                  "evidence": "FORMAT_VERIFIED",
                  "sourceIds": ["synthetic|d0|hitObject#0"],
                  "expectedTargetObjectId": "e2",
                  "actualTargetObjectId": "a2",
                  "expectedStrategyId": "m1",
                  "actualStrategyId": "m1a",
                  "expectedSameTimeGroupSize": 2,
                  "actualSameTimeGroupSize": 2,
                  "expectedSameTimeOrdinal": 1,
                  "actualSameTimeOrdinal": 1
                }
              ]
            }
            """);

        Assert.Equal(ValidationOutcome.Pass, result.Outcome);
    }

    private static void OneToManyStrategyWithoutSourceId()
    {
        var result = LoadAndCompare("""
            {
              "schemaVersion": 1,
              "fixtureId": "one-to-many-missing-source-scope",
              "provenance": { "artifactId": "synth-c", "format": "synthetic", "artifactDigest": "d0" },
              "sources": [ "synthetic|d0|hitObject#0" ],
              "expectedTargets": [ { "targetObjectId": "e1" }, { "targetObjectId": "e2" } ],
              "actualTargets": [ { "targetObjectId": "a1" }, { "targetObjectId": "a2" } ],
              "mappingStrategies": [
                { "strategyId": "m1", "kind": "1:N", "rationale": "expected split without source scope" },
                { "strategyId": "m1a", "kind": "1:N", "rationale": "actual split without source scope" }
              ],
              "assertions": [
                {
                  "assertionId": "s1", "evidence": "FORMAT_VERIFIED",
                  "sourceIds": ["synthetic|d0|hitObject#0"],
                  "expectedTargetObjectId": "e1", "actualTargetObjectId": "a1",
                  "expectedStrategyId": "m1", "actualStrategyId": "m1a"
                },
                {
                  "assertionId": "s2", "evidence": "FORMAT_VERIFIED",
                  "sourceIds": ["synthetic|d0|hitObject#0"],
                  "expectedTargetObjectId": "e2", "actualTargetObjectId": "a2",
                  "expectedStrategyId": "m1", "actualStrategyId": "m1a"
                }
              ]
            }
            """);

        Assert.Equal(ValidationOutcome.InvalidFixture, result.Outcome);
        Assert.True(result.Diagnostics.Any(d => d.Code == "MAP-STRATEGY-SCOPE-INCOMPLETE"));
        Assert.True(result.Diagnostics.Any(d => d.Code == "MAP-AMBIGUOUS"));
    }

    private static void OneToManyWithoutStrategy()
    {
        // One source id fans out to two distinct expected targets and two
        // distinct actual targets. Without a declared 1:N strategy this is an
        // ambiguous relation and must be rejected as an invalid fixture.
        var result = LoadAndCompare("""
            {
              "schemaVersion": 1,
              "fixtureId": "one-to-many-undeclared",
              "provenance": { "artifactId": "synth-c", "format": "synthetic", "artifactDigest": "d0" },
              "timeOrigin": "fixture-start",
              "timeUnit": "seconds",
              "sources": [ "synthetic|d0|hitObject#0" ],
              "expectedTargets": [ { "targetObjectId": "e1" }, { "targetObjectId": "e2" } ],
              "actualTargets": [ { "targetObjectId": "a1" }, { "targetObjectId": "a2" } ],
              "assertions": [
                {
                  "assertionId": "s1",
                  "evidence": "FORMAT_VERIFIED",
                  "sourceIds": ["synthetic|d0|hitObject#0"],
                  "expectedTargetObjectId": "e1",
                  "actualTargetObjectId": "a1"
                },
                {
                  "assertionId": "s2",
                  "evidence": "FORMAT_VERIFIED",
                  "sourceIds": ["synthetic|d0|hitObject#0"],
                  "expectedTargetObjectId": "e2",
                  "actualTargetObjectId": "a2"
                }
              ]
            }
            """);

        Assert.Equal(ValidationOutcome.InvalidFixture, result.Outcome);
        Assert.True(result.Diagnostics.Any(d => d.Code == "MAP-AMBIGUOUS"));
    }

    private static void StrategyScopeDoesNotLeak()
    {
        // m1 licenses only the hitObject#0 -> e1/e2 fan-out. The second source
        // (hitObject#1) fans out to e3/e4 with no strategy of its own, so the
        // declared relation must not authorize it.
        var result = LoadAndCompare("""
            {
              "schemaVersion": 1,
              "fixtureId": "strategy-scope",
              "provenance": { "artifactId": "synth-c", "format": "synthetic", "artifactDigest": "d0" },
              "timeOrigin": "fixture-start",
              "timeUnit": "seconds",
              "sources": [ "synthetic|d0|hitObject#0", "synthetic|d0|hitObject#1" ],
              "expectedTargets": [
                { "targetObjectId": "e1" }, { "targetObjectId": "e2" },
                { "targetObjectId": "e3" }, { "targetObjectId": "e4" }
              ],
              "actualTargets": [
                { "targetObjectId": "a1" }, { "targetObjectId": "a2" },
                { "targetObjectId": "a3" }, { "targetObjectId": "a4" }
              ],
              "mappingStrategies": [
                {
                  "strategyId": "m1",
                  "kind": "1:N",
                  "rationale": "declared expected split for hitObject#0 only",
                  "sourceId": "synthetic|d0|hitObject#0"
                },
                {
                  "strategyId": "m1a",
                  "kind": "1:N",
                  "rationale": "declared actual split for hitObject#0 only",
                  "sourceId": "synthetic|d0|hitObject#0"
                }
              ],
              "assertions": [
                {
                  "assertionId": "s1",
                  "evidence": "FORMAT_VERIFIED",
                  "sourceIds": ["synthetic|d0|hitObject#0"],
                  "expectedTargetObjectId": "e1",
                  "actualTargetObjectId": "a1",
                  "expectedStrategyId": "m1",
                  "actualStrategyId": "m1a",
                  "expectedSameTimeGroupSize": 2,
                  "actualSameTimeGroupSize": 2,
                  "expectedSameTimeOrdinal": 0,
                  "actualSameTimeOrdinal": 0
                },
                {
                  "assertionId": "s2",
                  "evidence": "FORMAT_VERIFIED",
                  "sourceIds": ["synthetic|d0|hitObject#0"],
                  "expectedTargetObjectId": "e2",
                  "actualTargetObjectId": "a2",
                  "expectedStrategyId": "m1",
                  "actualStrategyId": "m1a",
                  "expectedSameTimeGroupSize": 2,
                  "actualSameTimeGroupSize": 2,
                  "expectedSameTimeOrdinal": 1,
                  "actualSameTimeOrdinal": 1
                },
                {
                  "assertionId": "s3",
                  "evidence": "FORMAT_VERIFIED",
                  "sourceIds": ["synthetic|d0|hitObject#1"],
                  "expectedTargetObjectId": "e3",
                  "actualTargetObjectId": "a3"
                },
                {
                  "assertionId": "s4",
                  "evidence": "FORMAT_VERIFIED",
                  "sourceIds": ["synthetic|d0|hitObject#1"],
                  "expectedTargetObjectId": "e4",
                  "actualTargetObjectId": "a4"
                }
              ]
            }
            """);

        Assert.Equal(ValidationOutcome.InvalidFixture, result.Outcome);
        Assert.True(result.Diagnostics.Any(d =>
            d.Code == "MAP-AMBIGUOUS"
            && d.SourceId == "synthetic|d0|hitObject#1"));
    }

    private static void UnusedStrategyIsRejected()
    {
        // A committed 1:N strategy that no assertion references over-licenses the
        // fixture and must be rejected instead of silently authorizing later
        // relations.
        var result = LoadAndCompare("""
            {
              "schemaVersion": 1,
              "fixtureId": "unused-strategy",
              "provenance": { "artifactId": "synth-c", "format": "synthetic", "artifactDigest": "d0" },
              "timeOrigin": "fixture-start",
              "timeUnit": "seconds",
              "sources": [ "synthetic|d0|hitObject#0" ],
              "expectedTargets": [ { "targetObjectId": "e1" } ],
              "actualTargets": [ { "targetObjectId": "a1" } ],
              "mappingStrategies": [
                {
                  "strategyId": "m1",
                  "kind": "1:N",
                  "rationale": "declared but never used",
                  "sourceId": "synthetic|d0|hitObject#0"
                }
              ],
              "assertions": [
                {
                  "assertionId": "s1",
                  "evidence": "FORMAT_VERIFIED",
                  "sourceIds": ["synthetic|d0|hitObject#0"],
                  "expectedTargetObjectId": "e1",
                  "actualTargetObjectId": "a1",
                  "expectedSameTimeGroupSize": 1,
                  "actualSameTimeGroupSize": 1
                }
              ]
            }
            """);

        Assert.Equal(ValidationOutcome.InvalidFixture, result.Outcome);
        Assert.True(result.Diagnostics.Any(d => d.Code == "MAP-STRATEGY-REDUNDANT"));
    }

    private static void ManyToOneWithStrategy()
    {
        var result = LoadAndCompare("""
            {
              "schemaVersion": 1,
              "fixtureId": "many-to-one",
              "provenance": { "artifactId": "synth-d", "format": "synthetic", "artifactDigest": "d0" },
              "timeOrigin": "fixture-start",
              "timeUnit": "seconds",
              "sources": [ "synthetic|d0|hitObject#0", "synthetic|d0|hitObject#1" ],
              "expectedTargets": [ { "targetObjectId": "t1" } ],
              "actualTargets": [ { "targetObjectId": "t1" } ],
              "mappingStrategies": [
                {
                  "strategyId": "m1",
                  "kind": "N:1",
                  "rationale": "declared expected merge strategy",
                  "sourceIds": ["synthetic|d0|hitObject#0", "synthetic|d0|hitObject#1"],
                  "targetObjectId": "t1"
                },
                {
                  "strategyId": "m1a",
                  "kind": "N:1",
                  "rationale": "declared actual merge strategy",
                  "sourceIds": ["synthetic|d0|hitObject#0", "synthetic|d0|hitObject#1"],
                  "targetObjectId": "t1"
                }
              ],
              "assertions": [
                {
                  "assertionId": "a1",
                  "evidence": "FORMAT_VERIFIED",
                  "sourceIds": ["synthetic|d0|hitObject#0", "synthetic|d0|hitObject#1"],
                  "expectedTargetObjectId": "t1",
                  "actualTargetObjectId": "t1",
                  "expectedStrategyId": "m1",
                  "actualStrategyId": "m1a",
                  "expectedSameTimeGroupSize": 2,
                  "actualSameTimeGroupSize": 2
                }
              ]
            }
            """);

        Assert.Equal(ValidationOutcome.Pass, result.Outcome);
    }

    private static void ManyToOneStrategyOmitsContributor()
    {
        // The assertion merges hitObject#0 and hitObject#1 onto t1, but the
        // declared N:1 strategy names only hitObject#0. A source scope that
        // omits a participant must be rejected, never silently ignored.
        var result = LoadAndCompare("""
            {
              "schemaVersion": 1,
              "fixtureId": "merge-missing-contributor",
              "provenance": { "artifactId": "synth-d", "format": "synthetic", "artifactDigest": "d0" },
              "timeOrigin": "fixture-start",
              "timeUnit": "seconds",
              "sources": [ "synthetic|d0|hitObject#0", "synthetic|d0|hitObject#1" ],
              "expectedTargets": [ { "targetObjectId": "t1" } ],
              "actualTargets": [ { "targetObjectId": "t1" } ],
              "mappingStrategies": [
                {
                  "strategyId": "m1",
                  "kind": "N:1",
                  "rationale": "declared expected merge strategy",
                  "sourceIds": ["synthetic|d0|hitObject#0"],
                  "targetObjectId": "t1"
                },
                {
                  "strategyId": "m1a",
                  "kind": "N:1",
                  "rationale": "declared actual merge strategy",
                  "sourceIds": ["synthetic|d0|hitObject#0", "synthetic|d0|hitObject#1"],
                  "targetObjectId": "t1"
                }
              ],
              "assertions": [
                {
                  "assertionId": "a1",
                  "evidence": "FORMAT_VERIFIED",
                  "sourceIds": ["synthetic|d0|hitObject#0", "synthetic|d0|hitObject#1"],
                  "expectedTargetObjectId": "t1",
                  "actualTargetObjectId": "t1",
                  "expectedStrategyId": "m1",
                  "actualStrategyId": "m1a",
                  "expectedSameTimeGroupSize": 2,
                  "actualSameTimeGroupSize": 2
                }
              ]
            }
            """);

        Assert.Equal(ValidationOutcome.InvalidFixture, result.Outcome);
        Assert.True(result.Diagnostics.Any(d => d.Code == "MAP-STRATEGY-SCOPE-INCOMPLETE"));
    }

    private static void CrossAssertionMergeWithoutStrategy()
    {
        // Two assertions each name a single source, but both sources converge on
        // the same target object. That is an N:1 relation across assertions and
        // needs an explicit strategy for the exact relation.
        var result = LoadAndCompare("""
            {
              "schemaVersion": 1,
              "fixtureId": "cross-assertion-merge-undeclared",
              "provenance": { "artifactId": "synth-d", "format": "synthetic", "artifactDigest": "d0" },
              "timeOrigin": "fixture-start",
              "timeUnit": "seconds",
              "sources": [ "synthetic|d0|hitObject#0", "synthetic|d0|hitObject#1" ],
              "expectedTargets": [ { "targetObjectId": "t1" } ],
              "actualTargets": [ { "targetObjectId": "t1" } ],
              "assertions": [
                {
                  "assertionId": "a1",
                  "evidence": "FORMAT_VERIFIED",
                  "sourceIds": ["synthetic|d0|hitObject#0"],
                  "expectedTargetObjectId": "t1",
                  "actualTargetObjectId": "t1"
                },
                {
                  "assertionId": "a2",
                  "evidence": "FORMAT_VERIFIED",
                  "sourceIds": ["synthetic|d0|hitObject#1"],
                  "expectedTargetObjectId": "t1",
                  "actualTargetObjectId": "t1"
                }
              ]
            }
            """);

        Assert.Equal(ValidationOutcome.InvalidFixture, result.Outcome);
        Assert.True(result.Diagnostics.Any(d =>
            d.Code == "MAP-AMBIGUOUS"
            && d.TargetObjectId == "t1"));
    }

    private static void CrossAssertionMergeWithStrategy()
    {
        // The same convergence, now licensed by an N:1 strategy whose declared
        // source set and target are exactly the relation.
        var result = LoadAndCompare("""
            {
              "schemaVersion": 1,
              "fixtureId": "cross-assertion-merge-declared",
              "provenance": { "artifactId": "synth-d", "format": "synthetic", "artifactDigest": "d0" },
              "timeOrigin": "fixture-start",
              "timeUnit": "seconds",
              "sources": [ "synthetic|d0|hitObject#0", "synthetic|d0|hitObject#1" ],
              "expectedTargets": [ { "targetObjectId": "t1" } ],
              "actualTargets": [ { "targetObjectId": "t1" } ],
              "mappingStrategies": [
                {
                  "strategyId": "m1",
                  "kind": "N:1",
                  "rationale": "two single-source assertions merge onto t1",
                  "sourceIds": ["synthetic|d0|hitObject#0", "synthetic|d0|hitObject#1"],
                  "targetObjectId": "t1"
                }
              ],
              "assertions": [
                {
                  "assertionId": "a1",
                  "evidence": "FORMAT_VERIFIED",
                  "sourceIds": ["synthetic|d0|hitObject#0"],
                  "expectedTargetObjectId": "t1",
                  "actualTargetObjectId": "t1",
                  "expectedStrategyId": "m1",
                  "actualStrategyId": "m1",
                  "expectedSameTimeGroupSize": 2,
                  "actualSameTimeGroupSize": 2,
                  "expectedSameTimeOrdinal": 0,
                  "actualSameTimeOrdinal": 0
                },
                {
                  "assertionId": "a2",
                  "evidence": "FORMAT_VERIFIED",
                  "sourceIds": ["synthetic|d0|hitObject#1"],
                  "expectedTargetObjectId": "t1",
                  "actualTargetObjectId": "t1",
                  "expectedStrategyId": "m1",
                  "actualStrategyId": "m1",
                  "expectedSameTimeGroupSize": 2,
                  "actualSameTimeGroupSize": 2,
                  "expectedSameTimeOrdinal": 1,
                  "actualSameTimeOrdinal": 1
                }
              ]
            }
            """);

        Assert.Equal(ValidationOutcome.Pass, result.Outcome);
    }

    private static void ExpectedMergeVsActualSplitFails()
    {
        var result = LoadAndCompare("""
            {
              "schemaVersion": 1,
              "fixtureId": "mapping-topology-mismatch",
              "provenance": { "artifactId": "synth-d", "format": "synthetic", "artifactDigest": "d0" },
              "sources": [ "synthetic|d0|hitObject#0", "synthetic|d0|hitObject#1" ],
              "expectedTargets": [ { "targetObjectId": "e-merged" } ],
              "actualTargets": [ { "targetObjectId": "a0" }, { "targetObjectId": "a1" } ],
              "mappingStrategies": [
                {
                  "strategyId": "expected-merge",
                  "kind": "N:1",
                  "rationale": "two sources explicitly converge in expected mapping",
                  "sourceIds": ["synthetic|d0|hitObject#0", "synthetic|d0|hitObject#1"],
                  "targetObjectId": "e-merged"
                }
              ],
              "assertions": [
                {
                  "assertionId": "a0",
                  "evidence": "FORMAT_VERIFIED",
                  "sourceIds": ["synthetic|d0|hitObject#0"],
                  "expectedTargetObjectId": "e-merged",
                  "actualTargetObjectId": "a0",
                  "expectedStrategyId": "expected-merge"
                },
                {
                  "assertionId": "a1",
                  "evidence": "FORMAT_VERIFIED",
                  "sourceIds": ["synthetic|d0|hitObject#1"],
                  "expectedTargetObjectId": "e-merged",
                  "actualTargetObjectId": "a1",
                  "expectedStrategyId": "expected-merge"
                }
              ]
            }
            """);

        Assert.Equal(ValidationOutcome.Fail, result.Outcome);
        Assert.True(result.Diagnostics.Any(d =>
            d.Code == "CMP-MAPPING-MISMATCH"
            && d.Field == "mappingTopology"));
    }

    private static void ManyToOneStrategyDoesNotLeak()
    {
        // m1 licenses only the hitObject#0 + hitObject#1 -> t1 merge. A third
        // source also converging on t1 is outside that declared source set, so
        // the strategy must not authorize the wider relation.
        var result = LoadAndCompare("""
            {
              "schemaVersion": 1,
              "fixtureId": "merge-strategy-leak",
              "provenance": { "artifactId": "synth-d", "format": "synthetic", "artifactDigest": "d0" },
              "timeOrigin": "fixture-start",
              "timeUnit": "seconds",
              "sources": [
                "synthetic|d0|hitObject#0", "synthetic|d0|hitObject#1", "synthetic|d0|hitObject#2"
              ],
              "expectedTargets": [ { "targetObjectId": "t1" } ],
              "actualTargets": [ { "targetObjectId": "t1" } ],
              "mappingStrategies": [
                {
                  "strategyId": "m1",
                  "kind": "N:1",
                  "rationale": "declared merge for hitObject#0 and hitObject#1 only",
                  "sourceIds": ["synthetic|d0|hitObject#0", "synthetic|d0|hitObject#1"],
                  "targetObjectId": "t1"
                }
              ],
              "assertions": [
                {
                  "assertionId": "a1",
                  "evidence": "FORMAT_VERIFIED",
                  "sourceIds": ["synthetic|d0|hitObject#0"],
                  "expectedTargetObjectId": "t1",
                  "actualTargetObjectId": "t1",
                  "expectedStrategyId": "m1"
                },
                {
                  "assertionId": "a2",
                  "evidence": "FORMAT_VERIFIED",
                  "sourceIds": ["synthetic|d0|hitObject#1"],
                  "expectedTargetObjectId": "t1",
                  "actualTargetObjectId": "t1",
                  "expectedStrategyId": "m1"
                },
                {
                  "assertionId": "a3",
                  "evidence": "FORMAT_VERIFIED",
                  "sourceIds": ["synthetic|d0|hitObject#2"],
                  "expectedTargetObjectId": "t1",
                  "actualTargetObjectId": "t1",
                  "expectedStrategyId": "m1"
                }
              ]
            }
            """);

        Assert.Equal(ValidationOutcome.InvalidFixture, result.Outcome);
        Assert.True(result.Diagnostics.Any(d =>
            d.Code == "MAP-AMBIGUOUS"
            && d.TargetObjectId == "t1"));
    }

    private static void MissingTargetIsIncomplete()
    {
        var result = LoadAndCompare("""
            {
              "schemaVersion": 1,
              "fixtureId": "missing-target",
              "provenance": { "artifactId": "synth-e", "format": "synthetic", "artifactDigest": "d0" },
              "timeOrigin": "fixture-start",
              "timeUnit": "seconds",
              "sources": [ "synthetic|d0|hitObject#0" ],
              "expectedTargets": [ { "targetObjectId": "t1" } ],
              "actualTargets": [ { "targetObjectId": "t1" } ],
              "assertions": [
                {
                  "assertionId": "a1",
                  "evidence": "FORMAT_VERIFIED",
                  "sourceIds": ["synthetic|d0|hitObject#0"],
                  "expectedTargetObjectId": "t-absent",
                  "actualTargetObjectId": "t1"
                }
              ]
            }
            """);

        Assert.Equal(ValidationOutcome.InvalidFixture, result.Outcome);
        Assert.True(result.Diagnostics.Any(d => d.Code == "MAP-INCOMPLETE"));
    }

    private static void ExtraActualTarget()
    {
        // A target object declared on the actual side that no assertion ever
        // addresses is an extra mapping endpoint and must be detected.
        var result = LoadAndCompare("""
            {
              "schemaVersion": 1,
              "fixtureId": "extra-target",
              "provenance": { "artifactId": "synth-f", "format": "synthetic", "artifactDigest": "d0" },
              "timeOrigin": "fixture-start",
              "timeUnit": "seconds",
              "sources": [ "synthetic|d0|hitObject#0" ],
              "expectedTargets": [ { "targetObjectId": "t1" } ],
              "actualTargets": [ { "targetObjectId": "t1" }, { "targetObjectId": "t-extra" } ],
              "assertions": [
                {
                  "assertionId": "a1",
                  "evidence": "FORMAT_VERIFIED",
                  "sourceIds": ["synthetic|d0|hitObject#0"],
                  "expectedTargetObjectId": "t1",
                  "actualTargetObjectId": "t1"
                }
              ]
            }
            """);

        Assert.Equal(ValidationOutcome.InvalidFixture, result.Outcome);
        Assert.True(result.Diagnostics.Any(d => d.Code == "MAP-EXTRA"));
    }

    private static void DuplicateTargetId()
    {
        var result = LoadAndCompare("""
            {
              "schemaVersion": 1,
              "fixtureId": "duplicate-target",
              "provenance": { "artifactId": "synth-g", "format": "synthetic", "artifactDigest": "d0" },
              "timeOrigin": "fixture-start",
              "timeUnit": "seconds",
              "sources": [ "synthetic|d0|hitObject#0" ],
              "expectedTargets": [ { "targetObjectId": "t1" }, { "targetObjectId": "t1" } ],
              "actualTargets": [ { "targetObjectId": "t1" } ],
              "assertions": []
            }
            """);

        Assert.Equal(ValidationOutcome.InvalidFixture, result.Outcome);
        Assert.True(result.Diagnostics.Any(d => d.Code == "FIX-TARGET-ID-DUPLICATE"));
    }

    private static void MissingSourceId()
    {
        var result = LoadAndCompare("""
            {
              "schemaVersion": 1,
              "fixtureId": "missing-source",
              "provenance": { "artifactId": "synth-h", "format": "synthetic", "artifactDigest": "d0" },
              "timeOrigin": "fixture-start",
              "timeUnit": "seconds",
              "sources": [ "synthetic|d0|hitObject#0" ],
              "expectedTargets": [ { "targetObjectId": "t1" } ],
              "actualTargets": [ { "targetObjectId": "t1" } ],
              "assertions": [
                {
                  "assertionId": "a1",
                  "evidence": "FORMAT_VERIFIED",
                  "sourceIds": [],
                  "expectedTargetObjectId": "t1",
                  "actualTargetObjectId": "t1"
                }
              ]
            }
            """);

        Assert.Equal(ValidationOutcome.InvalidFixture, result.Outcome);
        Assert.True(result.Diagnostics.Any(d => d.Code == "FIX-SOURCE-ID-MISSING"));
    }

    private static void DuplicateSourceIdWithinAssertion()
    {
        var result = LoadAndCompare("""
            {
              "schemaVersion": 1,
              "fixtureId": "duplicate-assertion-source",
              "provenance": { "artifactId": "synth-h", "format": "synthetic", "artifactDigest": "d0" },
              "sources": [ "synthetic|d0|hitObject#0" ],
              "expectedTargets": [ { "targetObjectId": "e1" }, { "targetObjectId": "e2" } ],
              "actualTargets": [ { "targetObjectId": "a1" }, { "targetObjectId": "a2" } ],
              "assertions": [
                {
                  "assertionId": "a1",
                  "evidence": "FORMAT_VERIFIED",
                  "sourceIds": ["synthetic|d0|hitObject#0", "synthetic|d0|hitObject#0"],
                  "expectedTargetObjectId": "e1",
                  "actualTargetObjectId": "a1"
                },
                {
                  "assertionId": "a2",
                  "evidence": "FORMAT_VERIFIED",
                  "sourceIds": ["synthetic|d0|hitObject#0"],
                  "expectedTargetObjectId": "e2",
                  "actualTargetObjectId": "a2"
                }
              ]
            }
            """);

        Assert.Equal(ValidationOutcome.InvalidFixture, result.Outcome);
        Assert.True(result.Diagnostics.Any(d =>
            d.Code == "FIX-SOURCE-ID-DUPLICATE"
            && d.SourceId == "synthetic|d0|hitObject#0"));
        Assert.True(result.Diagnostics.Any(d =>
            d.Code == "MAP-AMBIGUOUS"
            && d.SourceId == "synthetic|d0|hitObject#0"));
    }

    private static void UndeclaredSourceId()
    {
        // "synthetic|d0|hitObject#9" is a well-formed address but is not part of
        // the declared inventory, so its existence cannot be confirmed and the
        // fixture is invalid.
        var result = LoadAndCompare("""
            {
              "schemaVersion": 1,
              "fixtureId": "undeclared-source",
              "provenance": { "artifactId": "synth-h", "format": "synthetic", "artifactDigest": "d0" },
              "timeOrigin": "fixture-start",
              "timeUnit": "seconds",
              "sources": [ "synthetic|d0|hitObject#0" ],
              "expectedTargets": [ { "targetObjectId": "t1" } ],
              "actualTargets": [ { "targetObjectId": "t1" } ],
              "assertions": [
                {
                  "assertionId": "a1",
                  "evidence": "FORMAT_VERIFIED",
                  "sourceIds": ["synthetic|d0|hitObject#9"],
                  "expectedTargetObjectId": "t1",
                  "actualTargetObjectId": "t1"
                }
              ]
            }
            """);

        Assert.Equal(ValidationOutcome.InvalidFixture, result.Outcome);
        Assert.True(result.Diagnostics.Any(d =>
            d.Code == "FIX-SOURCE-ID-UNKNOWN"
            && d.SourceId == "synthetic|d0|hitObject#9"));
    }

    private static void MalformedSourceId()
    {
        // A sourceId that is not a canonical typed address is rejected as
        // malformed rather than silently loaded.
        var result = LoadAndCompare("""
            {
              "schemaVersion": 1,
              "fixtureId": "malformed-source",
              "provenance": { "artifactId": "synth-h", "format": "synthetic", "artifactDigest": "d0" },
              "timeOrigin": "fixture-start",
              "timeUnit": "seconds",
              "sources": [ "not-an-address" ],
              "expectedTargets": [ { "targetObjectId": "t1" } ],
              "actualTargets": [ { "targetObjectId": "t1" } ],
              "assertions": [
                {
                  "assertionId": "a1",
                  "evidence": "FORMAT_VERIFIED",
                  "sourceIds": ["not-an-address"],
                  "expectedTargetObjectId": "t1",
                  "actualTargetObjectId": "t1"
                }
              ]
            }
            """);

        Assert.Equal(ValidationOutcome.InvalidFixture, result.Outcome);
        Assert.True(result.Diagnostics.Any(d => d.Code == "FIX-SOURCE-DECLARATION"));
    }

    private static void MissingSourceInventory()
    {
        // No sources[] at all means source existence cannot be checked; the
        // loader fails closed instead of pretending existence was validated.
        var result = LoadAndCompare("""
            {
              "schemaVersion": 1,
              "fixtureId": "no-inventory",
              "provenance": { "artifactId": "synth-h", "format": "synthetic", "artifactDigest": "d0" },
              "timeOrigin": "fixture-start",
              "timeUnit": "seconds",
              "expectedTargets": [ { "targetObjectId": "t1" } ],
              "actualTargets": [ { "targetObjectId": "t1" } ],
              "assertions": [
                {
                  "assertionId": "a1",
                  "evidence": "FORMAT_VERIFIED",
                  "sourceIds": ["synthetic|d0|hitObject#0"],
                  "expectedTargetObjectId": "t1",
                  "actualTargetObjectId": "t1"
                }
              ]
            }
            """);

        Assert.Equal(ValidationOutcome.InvalidFixture, result.Outcome);
        Assert.True(result.Diagnostics.Any(d => d.Code == "FIX-SOURCE-INVENTORY-MISSING"));
    }

    private static void DuplicateAssertionId()
    {
        var result = LoadAndCompare("""
            {
              "schemaVersion": 1,
              "fixtureId": "duplicate-assertion",
              "provenance": { "artifactId": "synth-i", "format": "synthetic", "artifactDigest": "d0" },
              "timeOrigin": "fixture-start",
              "timeUnit": "seconds",
              "sources": [ "synthetic|d0|hitObject#0", "synthetic|d0|hitObject#1" ],
              "expectedTargets": [ { "targetObjectId": "t1" } ],
              "actualTargets": [ { "targetObjectId": "t1" } ],
              "assertions": [
                {
                  "assertionId": "a1",
                  "evidence": "FORMAT_VERIFIED",
                  "sourceIds": ["synthetic|d0|hitObject#0"],
                  "expectedTargetObjectId": "t1",
                  "actualTargetObjectId": "t1"
                },
                {
                  "assertionId": "a1",
                  "evidence": "FORMAT_VERIFIED",
                  "sourceIds": ["synthetic|d0|hitObject#1"],
                  "expectedTargetObjectId": "t1",
                  "actualTargetObjectId": "t1"
                }
              ]
            }
            """);

        Assert.Equal(ValidationOutcome.InvalidFixture, result.Outcome);
        Assert.True(result.Diagnostics.Any(d => d.Code == "FIX-DUPLICATE-ID"));
    }

    private static void SameTimeOrderSwapFails()
    {
        var result = LoadAndCompare("""
            {
              "schemaVersion": 1,
              "fixtureId": "same-time-swap",
              "provenance": { "artifactId": "synth-j", "format": "synthetic", "artifactDigest": "d0" },
              "timeOrigin": "fixture-start",
              "timeUnit": "seconds",
              "sources": [ "synthetic|d0|hitObject#0" ],
              "expectedTargets": [ { "targetObjectId": "t1" } ],
              "actualTargets": [ { "targetObjectId": "t1" } ],
              "assertions": [
                {
                  "assertionId": "a1",
                  "evidence": "FORMAT_VERIFIED",
                  "sourceIds": ["synthetic|d0|hitObject#0"],
                  "expectedTargetObjectId": "t1",
                  "actualTargetObjectId": "t1",
                  "expectedSameTimeOrdinal": 0,
                  "actualSameTimeOrdinal": 1,
                  "expectedSameTimeGroupSize": 2,
                  "actualSameTimeGroupSize": 2
                }
              ]
            }
            """);

        Assert.Equal(ValidationOutcome.Fail, result.Outcome);
    }

    private static void SameTimeCardinalityFails()
    {
        var result = LoadAndCompare("""
            {
              "schemaVersion": 1,
              "fixtureId": "same-time-cardinality",
              "provenance": { "artifactId": "synth-k", "format": "synthetic", "artifactDigest": "d0" },
              "timeOrigin": "fixture-start",
              "timeUnit": "seconds",
              "sources": [ "synthetic|d0|hitObject#0" ],
              "expectedTargets": [ { "targetObjectId": "t1" } ],
              "actualTargets": [ { "targetObjectId": "t1" } ],
              "assertions": [
                {
                  "assertionId": "a1",
                  "evidence": "FORMAT_VERIFIED",
                  "sourceIds": ["synthetic|d0|hitObject#0"],
                  "expectedTargetObjectId": "t1",
                  "actualTargetObjectId": "t1",
                  "expectedSameTimeGroupSize": 3,
                  "actualSameTimeGroupSize": 2
                }
              ]
            }
            """);

        Assert.Equal(ValidationOutcome.Fail, result.Outcome);
    }

    private static void WrongEndTimeFails()
    {
        var result = LoadAndCompare("""
            {
              "schemaVersion": 1,
              "fixtureId": "wrong-end",
              "provenance": { "artifactId": "synth-l", "format": "synthetic", "artifactDigest": "d0" },
              "timeOrigin": "fixture-start",
              "timeUnit": "seconds",
              "sources": [ "synthetic|d0|hitObject#0" ],
              "expectedTargets": [ { "targetObjectId": "t1" } ],
              "actualTargets": [ { "targetObjectId": "t1" } ],
              "assertions": [
                {
                  "assertionId": "a1",
                  "evidence": "FORMAT_VERIFIED",
                  "sourceIds": ["synthetic|d0|hitObject#0"],
                  "expectedTargetObjectId": "t1",
                  "actualTargetObjectId": "t1",
                  "expectedStart": "1.0",
                  "actualStart": "1.0",
                  "expectedEnd": "2.0",
                  "actualEnd": "2.5",
                  "timeUnit": "seconds",
                  "timeOrigin": "fixture-start",
                  "numericPolicy": { "kind": "exact" }
                }
              ]
            }
            """);

        Assert.Equal(ValidationOutcome.Fail, result.Outcome);
        Assert.True(result.Diagnostics.Any(d => d.Field == "end"));
    }

    private static void WrongNoteKindFails()
    {
        var result = LoadAndCompare("""
            {
              "schemaVersion": 1,
              "fixtureId": "wrong-kind",
              "provenance": { "artifactId": "synth-m", "format": "synthetic", "artifactDigest": "d0" },
              "timeOrigin": "fixture-start",
              "timeUnit": "seconds",
              "sources": [ "synthetic|d0|hitObject#0" ],
              "expectedTargets": [ { "targetObjectId": "t1" } ],
              "actualTargets": [ { "targetObjectId": "t1" } ],
              "assertions": [
                {
                  "assertionId": "a1",
                  "evidence": "FORMAT_VERIFIED",
                  "sourceIds": ["synthetic|d0|hitObject#0"],
                  "expectedTargetObjectId": "t1",
                  "actualTargetObjectId": "t1",
                  "expectedNoteKind": "hold",
                  "actualNoteKind": "tap"
                }
              ]
            }
            """);

        Assert.Equal(ValidationOutcome.Fail, result.Outcome);
        Assert.True(result.Diagnostics.Any(d => d.Field == "noteKind" && d.Code == "CMP-FIELD-MISMATCH"));
    }

    private static void ExpectedEndBeforeStartIsInvalid()
    {
        var result = LoadAndCompare("""
            {
              "schemaVersion": 1,
              "fixtureId": "bad-interval",
              "provenance": { "artifactId": "synth-n", "format": "synthetic", "artifactDigest": "d0" },
              "timeOrigin": "fixture-start",
              "timeUnit": "seconds",
              "sources": [ "synthetic|d0|hitObject#0" ],
              "expectedTargets": [ { "targetObjectId": "t1" } ],
              "actualTargets": [ { "targetObjectId": "t1" } ],
              "assertions": [
                {
                  "assertionId": "a1",
                  "evidence": "FORMAT_VERIFIED",
                  "sourceIds": ["synthetic|d0|hitObject#0"],
                  "expectedTargetObjectId": "t1",
                  "actualTargetObjectId": "t1",
                  "expectedStart": "2.0",
                  "actualStart": "2.0",
                  "expectedEnd": "1.0",
                  "actualEnd": "2.0"
                }
              ]
            }
            """);

        Assert.Equal(ValidationOutcome.InvalidFixture, result.Outcome);
        Assert.True(result.Diagnostics.Any(d => d.Code == "FIX-INVALID-INTERVAL"));
    }

    private static void ActualEndBeforeStartIsFail()
    {
        // The expected record is a legal interval; only the actual output is
        // malformed. This must be an output FAIL, never INVALID FIXTURE.
        var result = LoadAndCompare("""
            {
              "schemaVersion": 1,
              "fixtureId": "bad-actual-interval",
              "provenance": { "artifactId": "synth-n", "format": "synthetic", "artifactDigest": "d0" },
              "timeOrigin": "fixture-start",
              "timeUnit": "seconds",
              "sources": [ "synthetic|d0|hitObject#0" ],
              "expectedTargets": [ { "targetObjectId": "t1" } ],
              "actualTargets": [ { "targetObjectId": "t1" } ],
              "assertions": [
                {
                  "assertionId": "a1",
                  "evidence": "FORMAT_VERIFIED",
                  "sourceIds": ["synthetic|d0|hitObject#0"],
                  "expectedTargetObjectId": "t1",
                  "actualTargetObjectId": "t1",
                  "expectedStart": "1.0",
                  "actualStart": "2.0",
                  "expectedEnd": "2.0",
                  "actualEnd": "1.0",
                  "timeUnit": "seconds",
                  "timeOrigin": "fixture-start",
                  "numericPolicy": { "kind": "exact" }
                }
              ]
            }
            """);

        Assert.Equal(ValidationOutcome.Fail, result.Outcome);
        Assert.True(result.Diagnostics.All(d => d.Outcome != ValidationOutcome.InvalidFixture));
        Assert.True(result.Diagnostics.Any(d => d.Code == "CMP-INTERVAL-INVALID"));
    }

    private static void ActualEndEqualsStartIsLegal()
    {
        var result = LoadAndCompare("""
            {
              "schemaVersion": 1,
              "fixtureId": "actual-zero-length",
              "provenance": { "artifactId": "synth-n", "format": "synthetic", "artifactDigest": "d0" },
              "timeOrigin": "fixture-start",
              "timeUnit": "seconds",
              "sources": [ "synthetic|d0|hitObject#0" ],
              "expectedTargets": [ { "targetObjectId": "t1" } ],
              "actualTargets": [ { "targetObjectId": "t1" } ],
              "assertions": [
                {
                  "assertionId": "a1",
                  "evidence": "FORMAT_VERIFIED",
                  "sourceIds": ["synthetic|d0|hitObject#0"],
                  "expectedTargetObjectId": "t1",
                  "actualTargetObjectId": "t1",
                  "expectedStart": "1.0",
                  "actualStart": "1.0",
                  "expectedEnd": "1.0",
                  "actualEnd": "1.0",
                  "timeUnit": "seconds",
                  "timeOrigin": "fixture-start",
                  "numericPolicy": { "kind": "exact" }
                }
              ]
            }
            """);

        Assert.Equal(ValidationOutcome.Pass, result.Outcome);
    }

    private static void EndEqualsStartIsLegal()
    {
        var result = LoadAndCompare("""
            {
              "schemaVersion": 1,
              "fixtureId": "zero-length",
              "provenance": { "artifactId": "synth-o", "format": "synthetic", "artifactDigest": "d0" },
              "timeOrigin": "fixture-start",
              "timeUnit": "seconds",
              "sources": [ "synthetic|d0|hitObject#0" ],
              "expectedTargets": [ { "targetObjectId": "t1" } ],
              "actualTargets": [ { "targetObjectId": "t1" } ],
              "assertions": [
                {
                  "assertionId": "a1",
                  "evidence": "FORMAT_VERIFIED",
                  "sourceIds": ["synthetic|d0|hitObject#0"],
                  "expectedTargetObjectId": "t1",
                  "actualTargetObjectId": "t1",
                  "expectedStart": "1.0",
                  "actualStart": "1.0",
                  "expectedEnd": "1.0",
                  "actualEnd": "1.0",
                  "timeUnit": "seconds",
                  "timeOrigin": "fixture-start",
                  "numericPolicy": { "kind": "exact" }
                }
              ]
            }
            """);

        Assert.Equal(ValidationOutcome.Pass, result.Outcome);
    }

    private static void MissingTimeUnitIsIndeterminate()
    {
        var result = LoadAndCompare("""
            {
              "schemaVersion": 1,
              "fixtureId": "no-unit",
              "provenance": { "artifactId": "synth-p", "format": "synthetic", "artifactDigest": "d0" },
              "sources": [ "synthetic|d0|hitObject#0" ],
              "expectedTargets": [ { "targetObjectId": "t1" } ],
              "actualTargets": [ { "targetObjectId": "t1" } ],
              "assertions": [
                {
                  "assertionId": "a1",
                  "evidence": "FORMAT_VERIFIED",
                  "sourceIds": ["synthetic|d0|hitObject#0"],
                  "expectedTargetObjectId": "t1",
                  "actualTargetObjectId": "t1",
                  "expectedStart": "1.0",
                  "actualStart": "1.0",
                  "timeOrigin": "fixture-start",
                  "numericPolicy": { "kind": "exact" }
                }
              ]
            }
            """);

        Assert.Equal(ValidationOutcome.Indeterminate, result.Outcome);
        Assert.True(result.Diagnostics.Any(d => d.Code == "CMP-ORIGIN-MISMATCH"));
    }

    private static void MissingPolicyIsIndeterminate()
    {
        var result = LoadAndCompare("""
            {
              "schemaVersion": 1,
              "fixtureId": "no-policy",
              "provenance": { "artifactId": "synth-q", "format": "synthetic", "artifactDigest": "d0" },
              "timeOrigin": "fixture-start",
              "timeUnit": "seconds",
              "sources": [ "synthetic|d0|hitObject#0" ],
              "expectedTargets": [ { "targetObjectId": "t1" } ],
              "actualTargets": [ { "targetObjectId": "t1" } ],
              "assertions": [
                {
                  "assertionId": "a1",
                  "evidence": "FORMAT_VERIFIED",
                  "sourceIds": ["synthetic|d0|hitObject#0"],
                  "expectedTargetObjectId": "t1",
                  "actualTargetObjectId": "t1",
                  "expectedStart": "1.0",
                  "actualStart": "1.0",
                  "timeUnit": "seconds",
                  "timeOrigin": "fixture-start"
                }
              ]
            }
            """);

        Assert.Equal(ValidationOutcome.Indeterminate, result.Outcome);
        Assert.True(result.Diagnostics.Any(d => d.Code == "CMP-NUMERIC-POLICY-MISSING"));
    }

    private static void NumericToleranceIsPerAssertion()
    {
        // The two values differ by 0.0001, far larger than a fixture-level
        // epsilon would hide but still within the assertion's declared 0.001
        // tolerance. The assertion must pass only because of its own policy.
        var result = LoadAndCompare("""
            {
              "schemaVersion": 1,
              "fixtureId": "tolerance",
              "provenance": { "artifactId": "synth-r", "format": "synthetic", "artifactDigest": "d0" },
              "timeOrigin": "fixture-start",
              "timeUnit": "seconds",
              "sources": [ "synthetic|d0|hitObject#0" ],
              "expectedTargets": [ { "targetObjectId": "t1" } ],
              "actualTargets": [ { "targetObjectId": "t1" } ],
              "assertions": [
                {
                  "assertionId": "a1",
                  "evidence": "FORMAT_VERIFIED",
                  "sourceIds": ["synthetic|d0|hitObject#0"],
                  "expectedTargetObjectId": "t1",
                  "actualTargetObjectId": "t1",
                  "expectedStart": "1.0000",
                  "actualStart": "1.0001",
                  "timeUnit": "seconds",
                  "timeOrigin": "fixture-start",
                  "numericPolicy": { "kind": "absolute", "tolerance": 0.001, "unit": "seconds", "rationale": "declared quantization bound" }
                }
              ]
            }
            """);
        Assert.Equal(ValidationOutcome.Pass, result.Outcome);

        // The same values under the default exact policy must fail; a fixture
        // must never inherit any implicit global epsilon.
        var exact = LoadAndCompare("""
            {
              "schemaVersion": 1,
              "fixtureId": "tolerance-exact",
              "provenance": { "artifactId": "synth-r", "format": "synthetic", "artifactDigest": "d0" },
              "timeOrigin": "fixture-start",
              "timeUnit": "seconds",
              "sources": [ "synthetic|d0|hitObject#0" ],
              "expectedTargets": [ { "targetObjectId": "t1" } ],
              "actualTargets": [ { "targetObjectId": "t1" } ],
              "assertions": [
                {
                  "assertionId": "a1",
                  "evidence": "FORMAT_VERIFIED",
                  "sourceIds": ["synthetic|d0|hitObject#0"],
                  "expectedTargetObjectId": "t1",
                  "actualTargetObjectId": "t1",
                  "expectedStart": "1.0000",
                  "actualStart": "1.0001",
                  "timeUnit": "seconds",
                  "timeOrigin": "fixture-start",
                  "numericPolicy": { "kind": "exact" }
                }
              ]
            }
            """);
        Assert.Equal(ValidationOutcome.Fail, exact.Outcome);
    }

    private static void InadmissibleToleranceIsInvalid()
    {
        var result = LoadAndCompare("""
            {
              "schemaVersion": 1,
              "fixtureId": "bad-tolerance",
              "provenance": { "artifactId": "synth-s", "format": "synthetic", "artifactDigest": "d0" },
              "timeOrigin": "fixture-start",
              "timeUnit": "seconds",
              "sources": [ "synthetic|d0|hitObject#0" ],
              "expectedTargets": [ { "targetObjectId": "t1" } ],
              "actualTargets": [ { "targetObjectId": "t1" } ],
              "assertions": [
                {
                  "assertionId": "a1",
                  "evidence": "FORMAT_VERIFIED",
                  "sourceIds": ["synthetic|d0|hitObject#0"],
                  "expectedTargetObjectId": "t1",
                  "actualTargetObjectId": "t1",
                  "expectedStart": "1.0",
                  "actualStart": "1.0",
                  "timeUnit": "seconds",
                  "timeOrigin": "fixture-start",
                  "numericPolicy": { "kind": "absolute", "tolerance": 0.5 }
                }
              ]
            }
            """);

        Assert.Equal(ValidationOutcome.InvalidFixture, result.Outcome);
    }

    private static void ExactPolicyRejectsDoubleCollapse()
    {
        // 1.0 and 1.0000000000000001 are distinct decimals that round to the
        // same double. Exact comparison must not treat them as equal.
        var collapsed = LoadAndCompare("""
            {
              "schemaVersion": 1,
              "fixtureId": "decimal-collapse",
              "provenance": { "artifactId": "synth-r", "format": "synthetic", "artifactDigest": "d0" },
              "timeOrigin": "fixture-start",
              "timeUnit": "seconds",
              "sources": [ "synthetic|d0|hitObject#0" ],
              "expectedTargets": [ { "targetObjectId": "t1" } ],
              "actualTargets": [ { "targetObjectId": "t1" } ],
              "assertions": [
                {
                  "assertionId": "a1",
                  "evidence": "FORMAT_VERIFIED",
                  "sourceIds": ["synthetic|d0|hitObject#0"],
                  "expectedTargetObjectId": "t1",
                  "actualTargetObjectId": "t1",
                  "expectedStart": "1.0",
                  "actualStart": "1.0000000000000001",
                  "timeUnit": "seconds",
                  "timeOrigin": "fixture-start",
                  "numericPolicy": { "kind": "exact" }
                }
              ]
            }
            """);
        Assert.Equal(ValidationOutcome.Fail, collapsed.Outcome);

        // A second high-precision pair: 0.1 versus a longer decimal that parses
        // to the same double.
        var collapsedTenth = LoadAndCompare("""
            {
              "schemaVersion": 1,
              "fixtureId": "tenth-collapse",
              "provenance": { "artifactId": "synth-r", "format": "synthetic", "artifactDigest": "d0" },
              "timeOrigin": "fixture-start",
              "timeUnit": "seconds",
              "sources": [ "synthetic|d0|hitObject#0" ],
              "expectedTargets": [ { "targetObjectId": "t1" } ],
              "actualTargets": [ { "targetObjectId": "t1" } ],
              "assertions": [
                {
                  "assertionId": "a1",
                  "evidence": "FORMAT_VERIFIED",
                  "sourceIds": ["synthetic|d0|hitObject#0"],
                  "expectedTargetObjectId": "t1",
                  "actualTargetObjectId": "t1",
                  "expectedStart": "0.1",
                  "actualStart": "0.1000000000000000055511151231257827021181583404541015625",
                  "timeUnit": "seconds",
                  "timeOrigin": "fixture-start",
                  "numericPolicy": { "kind": "exact" }
                }
              ]
            }
            """);
        Assert.Equal(ValidationOutcome.Fail, collapsedTenth.Outcome);

        // Trailing zeros are the same exact value, so they must still pass.
        var sameValue = LoadAndCompare("""
            {
              "schemaVersion": 1,
              "fixtureId": "decimal-trailing-zero",
              "provenance": { "artifactId": "synth-r", "format": "synthetic", "artifactDigest": "d0" },
              "timeOrigin": "fixture-start",
              "timeUnit": "seconds",
              "sources": [ "synthetic|d0|hitObject#0" ],
              "expectedTargets": [ { "targetObjectId": "t1" } ],
              "actualTargets": [ { "targetObjectId": "t1" } ],
              "assertions": [
                {
                  "assertionId": "a1",
                  "evidence": "FORMAT_VERIFIED",
                  "sourceIds": ["synthetic|d0|hitObject#0"],
                  "expectedTargetObjectId": "t1",
                  "actualTargetObjectId": "t1",
                  "expectedStart": "1.0",
                  "actualStart": "1.00",
                  "timeUnit": "seconds",
                  "timeOrigin": "fixture-start",
                  "numericPolicy": { "kind": "exact" }
                }
              ]
            }
            """);
        Assert.Equal(ValidationOutcome.Pass, sameValue.Outcome);
    }

    private static void TimingPointOrderMismatchFails()
    {
        // Expected and actual application orders are supplied explicitly; the
        // comparator must not infer order from note times. A swap fails.
        var result = LoadAndCompare("""
            {
              "schemaVersion": 1,
              "fixtureId": "timing-order",
              "provenance": { "artifactId": "synth-w", "format": "synthetic", "artifactDigest": "d0" },
              "timeOrigin": "fixture-start",
              "timeUnit": "seconds",
              "sources": [ "synthetic|d0|timingPoint#0", "synthetic|d0|timingPoint#1" ],
              "expectedTargets": [ { "targetObjectId": "t1" } ],
              "actualTargets": [ { "targetObjectId": "t1" } ],
              "assertions": [
                {
                  "assertionId": "a1",
                  "evidence": "FORMAT_VERIFIED",
                  "sourceIds": ["synthetic|d0|timingPoint#0"],
                  "expectedTargetObjectId": "t1",
                  "actualTargetObjectId": "t1",
                  "baseSourceIds": ["synthetic|d0|timingPoint#0", "synthetic|d0|timingPoint#1"],
                  "expectedApplicationOrder": ["synthetic|d0|timingPoint#0", "synthetic|d0|timingPoint#1"],
                  "actualApplicationOrder": ["synthetic|d0|timingPoint#1", "synthetic|d0|timingPoint#0"]
                }
              ]
            }
            """);

        Assert.Equal(ValidationOutcome.Fail, result.Outcome);
        Assert.True(result.Diagnostics.Any(d => d.Code == "CMP-ORDER-MISMATCH"));
    }

    private static void TimingPointOrderMatchPasses()
    {
        var result = LoadAndCompare("""
            {
              "schemaVersion": 1,
              "fixtureId": "timing-order-match",
              "provenance": { "artifactId": "synth-w", "format": "synthetic", "artifactDigest": "d0" },
              "timeOrigin": "fixture-start",
              "timeUnit": "seconds",
              "sources": [ "synthetic|d0|timingPoint#0", "synthetic|d0|timingPoint#1" ],
              "expectedTargets": [ { "targetObjectId": "t1" } ],
              "actualTargets": [ { "targetObjectId": "t1" } ],
              "assertions": [
                {
                  "assertionId": "a1",
                  "evidence": "FORMAT_VERIFIED",
                  "sourceIds": ["synthetic|d0|timingPoint#0"],
                  "expectedTargetObjectId": "t1",
                  "actualTargetObjectId": "t1",
                  "baseSourceIds": ["synthetic|d0|timingPoint#0", "synthetic|d0|timingPoint#1"],
                  "expectedApplicationOrder": ["synthetic|d0|timingPoint#0", "synthetic|d0|timingPoint#1"],
                  "actualApplicationOrder": ["synthetic|d0|timingPoint#0", "synthetic|d0|timingPoint#1"]
                }
              ]
            }
            """);

        Assert.Equal(ValidationOutcome.Pass, result.Outcome);
    }

    private static void EmptyAssertionIsNotPass()
    {
        // No comparison dimension is declared, so the assertion asserts nothing
        // and must not surface as PASS (or CMP-PASS).
        var result = LoadAndCompare("""
            {
              "schemaVersion": 1,
              "fixtureId": "empty-assertion",
              "provenance": { "artifactId": "synth-x", "format": "synthetic", "artifactDigest": "d0" },
              "timeOrigin": "fixture-start",
              "timeUnit": "seconds",
              "sources": [ "synthetic|d0|hitObject#0" ],
              "expectedTargets": [ { "targetObjectId": "t1" } ],
              "actualTargets": [ { "targetObjectId": "t1" } ],
              "assertions": [
                {
                  "assertionId": "a1",
                  "evidence": "FORMAT_VERIFIED",
                  "sourceIds": ["synthetic|d0|hitObject#0"],
                  "expectedTargetObjectId": "t1",
                  "actualTargetObjectId": "t1"
                }
              ]
            }
            """);

        Assert.True(result.Outcome is ValidationOutcome.NotAsserted or ValidationOutcome.Indeterminate,
            $"an empty assertion must not be PASS, got {result.Outcome}");
        Assert.True(result.Diagnostics.All(d => d.Code != "CMP-PASS"));
    }

    private static void HypothesisEvidenceIsIndeterminate()
    {
        var result = LoadAndCompare("""
            {
              "schemaVersion": 1,
              "fixtureId": "hypothesis",
              "provenance": { "artifactId": "synth-t", "format": "synthetic", "artifactDigest": "d0" },
              "timeOrigin": "fixture-start",
              "timeUnit": "seconds",
              "sources": [ "synthetic|d0|hitObject#0" ],
              "expectedTargets": [ { "targetObjectId": "t1" } ],
              "actualTargets": [ { "targetObjectId": "t1" } ],
              "assertions": [
                {
                  "assertionId": "a1",
                  "evidence": "HYPOTHESIS",
                  "sourceIds": ["synthetic|d0|hitObject#0"],
                  "expectedTargetObjectId": "t1",
                  "actualTargetObjectId": "t1",
                  "expectedStart": "1.0",
                  "actualStart": "1.0",
                  "timeUnit": "seconds",
                  "timeOrigin": "fixture-start",
                  "numericPolicy": { "kind": "exact" }
                }
              ]
            }
            """);

        Assert.Equal(ValidationOutcome.Indeterminate, result.Outcome);
        Assert.True(result.Diagnostics.Any(d => d.Code == "CMP-EVIDENCE-INSUFFICIENT"));
    }

    private static void UnknownEvidenceIsIndeterminate()
    {
        var result = LoadAndCompare("""
            {
              "schemaVersion": 1,
              "fixtureId": "unknown-evidence",
              "provenance": { "artifactId": "synth-u", "format": "synthetic", "artifactDigest": "d0" },
              "timeOrigin": "fixture-start",
              "timeUnit": "seconds",
              "sources": [ "synthetic|d0|hitObject#0" ],
              "expectedTargets": [ { "targetObjectId": "t1" } ],
              "actualTargets": [ { "targetObjectId": "t1" } ],
              "assertions": [
                {
                  "assertionId": "a1",
                  "evidence": "UNKNOWN",
                  "sourceIds": ["synthetic|d0|hitObject#0"],
                  "expectedTargetObjectId": "t1",
                  "actualTargetObjectId": "t1"
                }
              ]
            }
            """);

        Assert.Equal(ValidationOutcome.Indeterminate, result.Outcome);
    }

    private static void NotAssertedDimension()
    {
        var result = LoadAndCompare("""
            {
              "schemaVersion": 1,
              "fixtureId": "not-asserted",
              "provenance": { "artifactId": "synth-v", "format": "synthetic", "artifactDigest": "d0" },
              "timeOrigin": "fixture-start",
              "timeUnit": "seconds",
              "sources": [ "synthetic|d0|hitObject#0" ],
              "expectedTargets": [ { "targetObjectId": "t1" } ],
              "actualTargets": [ { "targetObjectId": "t1" } ],
              "assertions": [
                {
                  "assertionId": "a1",
                  "evidence": "NOT_ASSERTED",
                  "sourceIds": ["synthetic|d0|hitObject#0"],
                  "expectedTargetObjectId": "t1",
                  "actualTargetObjectId": "t1",
                  "expectedStart": "1.0",
                  "actualStart": "9.9"
                }
              ]
            }
            """);

        Assert.Equal(ValidationOutcome.NotAsserted, result.Outcome);
    }

    private static void UnsupportedSchemaVersion()
    {
        var result = LoadAndCompare("""
            { "schemaVersion": 99, "fixtureId": "future" }
            """);

        Assert.Equal(ValidationOutcome.InvalidFixture, result.Outcome);
        Assert.True(result.Diagnostics.Any(d => d.Code == "FIX-SCHEMA-VERSION-UNSUPPORTED"));
    }

    private static void SourceAddressRoundTrip()
    {
        var address = new SourceAddress("d0", "osu-mania", SourceAddress.HitObjectKind, 42, 1);
        var id = address.ToId();
        Assert.Equal("osu-mania|d0|hitObject#42@1", id);
        var parsed = SourceAddressAdapter.FromId(id);
        Assert.Equal(address, parsed);

        var noSameTime = new SourceAddress("d0", "adofai", SourceAddress.FloorKind, 7).ToId();
        Assert.Equal("adofai|d0|floor#7", noSameTime);
        Assert.Equal(new SourceAddress("d0", "adofai", SourceAddress.FloorKind, 7), SourceAddressAdapter.FromId(noSameTime));
    }

    private static FixtureValidationResult LoadAndCompare(string json)
    {
        var loaded = FixtureLoader.Load(json);
        return FixtureComparator.Compare(loaded);
    }
}
