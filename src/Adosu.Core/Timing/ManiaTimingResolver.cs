using Adosu.Core.Model;

namespace Adosu.Core.Timing;

public static class ManiaTimingResolver
{
    public static double? EffectiveBpmAt(TempoTrack tempoTrack, double timeSeconds)
    {
        TempoEvent? active = null;
        foreach (var point in tempoTrack.Events
                     .Where(point => point.Kind == TempoEventKind.RedTimingPoint)
                     .OrderBy(point => point.TimeSeconds)
                     .ThenBy(point => point.Provenance.SourceIndex))        {
            if (point.TimeSeconds > timeSeconds)
            {
                break;
            }

            active = point;
        }

        return active?.Bpm;
    }

    /// <summary>
    /// Returns the effective mania scroll multiplier. This track is wholly
    /// independent from the red timing/tempo track and never rewrites input
    /// timestamps.
    /// </summary>
    public static double EffectiveScrollAt(
        ScrollPresentationTrack track,
        double timeSeconds)
    {
        ScrollEvent? active = null;
        foreach (var point in track.ScrollEvents
                     .OrderBy(point => point.TimeSeconds)
                     .ThenBy(point => point.Provenance.SourceIndex))
        {
            if (point.TimeSeconds > timeSeconds)
            {
                break;
            }

            active = point;
        }

        return active?.Multiplier ?? 1.0;
    }
}
