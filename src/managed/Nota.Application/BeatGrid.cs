// SPDX-License-Identifier: AGPL-3.0-only
namespace Nota.Application;

/// <summary>Beat-space grid math shared by note and arrangement editors.
/// Callers retain their own bounds and modifier policies. No UI or engine dependency.</summary>
public static class BeatGrid
{
    public static double Nearest(double beat, double step, bool enabled = true)
        => enabled && step > 0 ? Math.Round(beat / step) * step : beat;

    /// <summary>The cell containing the pointer, used when drawing new items.</summary>
    public static double CellStart(double beat, double step, bool enabled = true)
        => enabled && step > 0 ? Math.Floor(beat / step) * step : beat;
}
