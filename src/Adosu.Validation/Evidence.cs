namespace Adosu.Validation;

/// <summary>
/// How a fixture value or a whole assertion is evidenced. This is the contract's
/// evidence taxonomy and deliberately mirrors the AS-51 clause 1 vocabulary.
///
/// Only <see cref="FormatVerified"/> may support a correctness PASS about the
/// file encoding itself. <see cref="ReferenceVersionObserved"/> and
/// <see cref="GameVersionObserved"/> only support a proposition about that one
/// observed implementation or version and never generalise to "the game".
/// <see cref="Hypothesis"/>, <see cref="Unknown"/> and the not-asserted sentinel
/// never support a correctness PASS.
/// </summary>
public enum EvidenceLevel
{
    /// <summary>The value is a verbatim, independently re-checkable field of the source artifact format.</summary>
    FormatVerified,

    /// <summary>An observation of one fixed reference implementation at a pinned revision.</summary>
    ReferenceVersionObserved,

    /// <summary>An independent observation of one fixed game/editor version.</summary>
    GameVersionObserved,

    /// <summary>An unverified candidate rule or hand-derived formula.</summary>
    Hypothesis,

    /// <summary>Not known; retained so it is never silently upgraded.</summary>
    Unknown,

    /// <summary>The fixture explicitly does not assert this dimension.</summary>
    NotAsserted
}

public static class EvidenceLevelExtensions
{
    /// <summary>
    /// Whether an evidence level is allowed to back a correctness PASS at all.
    /// </summary>
    public static bool SupportsCorrectnessPass(this EvidenceLevel level) =>
        level is EvidenceLevel.FormatVerified
            or EvidenceLevel.ReferenceVersionObserved
            or EvidenceLevel.GameVersionObserved;
}
