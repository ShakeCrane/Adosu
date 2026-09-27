using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Adosu.Validation;

/// <summary>
/// A stable, typed source address built from the raw source file, not from a
/// derived array position. For osu! it is the file digest + hit-object line /
/// object ordinal; for ADOFAI it is the floor index, path token index or action
/// ordinal. It never addresses an object by its sorted position or by guessing
/// a field value.
/// </summary>
public sealed record SourceAddress(
    string ArtifactDigest,
    string Format,
    string Kind,
    int Ordinal,
    int? SameTimeOrdinal = null)
{
    public const string HitObjectKind = "hitObject";
    public const string TimingPointKind = "timingPoint";
    public const string FloorKind = "floor";
    public const string PathTokenKind = "pathToken";
    public const string ActionKind = "action";

    /// <summary>
    /// Renders the canonical, collision-resistant id used by fixture sourceIds.
    /// </summary>
    public string ToId()
    {
        var builder = new StringBuilder();
        builder.Append(Format).Append('|').Append(ArtifactDigest).Append('|').Append(Kind).Append('#')
            .Append(Ordinal.ToString(CultureInfo.InvariantCulture));
        if (SameTimeOrdinal is not null)
        {
            builder.Append('@').Append(SameTimeOrdinal.Value.ToString(CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Content digest for an artifact. The caller decides which artifact is
    /// legitimate to hash; the digest itself is only an address component.
    /// </summary>
    public static string Digest(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexStringLower(bytes);
    }
}

/// <summary>
/// Maps a fixture source id onto an Adosu semantic provenance. This adapter is
/// the only bridge between the validation layer and Core; it never produces
/// expected values and it never invokes the Reader or Resolver.
/// </summary>
public static class SourceAddressAdapter
{
    public static SourceAddress FromId(string id)
    {
        var parts = id.Split('|');
        if (parts.Length != 3)
        {
            throw new FormatException($"malformed source id '{id}'");
        }

        var format = parts[0];
        var digest = parts[1];
        var ordinalParts = parts[2].Split('#');
        if (ordinalParts.Length != 2)
        {
            throw new FormatException($"malformed source id ordinal segment in '{id}'");
        }

        var kind = ordinalParts[0];
        var ordinalSegment = ordinalParts[1];
        int? sameTime = null;
        var atIndex = ordinalSegment.IndexOf('@', StringComparison.Ordinal);
        var ordinalText = atIndex >= 0 ? ordinalSegment[..atIndex] : ordinalSegment;
        if (atIndex >= 0)
        {
            sameTime = int.Parse(ordinalSegment[(atIndex + 1)..], CultureInfo.InvariantCulture);
        }

        var ordinal = int.Parse(ordinalText, CultureInfo.InvariantCulture);
        return new SourceAddress(digest, format, kind, ordinal, sameTime);
    }
}
