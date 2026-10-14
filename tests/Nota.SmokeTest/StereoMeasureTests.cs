// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Stereo measurements for the Stereo Map (specs/008-stereo-map): a mono source panned to p
// reads balance p and width 0, unrelated left/right material reads width 1 and correlation 0,
// an anti-phase pair correlation −1, silence is below −60 dBFS, the master row matches its
// only track, and a muted track fades out. Rendered from deterministic WAV clips. Also
// runnable alone: `dotnet run --project tests/Nota.SmokeTest -- --stereomap`.

using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class StereoMeasureTests
{
    private const int Rate = 48000;

    public static IEnumerable<(bool ok, string label)> Run()
    {
        foreach (var r in Measures()) yield return r;
    }

    private static float[] Sine(double hz, double seconds, float amp)
    {
        var x = new float[(int)(seconds * Rate)];
        for (int i = 0; i < x.Length; i++) x[i] = amp * (float)Math.Sin(2 * Math.PI * hz * i / Rate);
        return x;
    }

    private static string Wav(float[] l, float[] r)
    {
        var path = Path.Combine(Path.GetTempPath(), $"nota-stereo-{Guid.NewGuid():N}.wav");
        var inter = new float[l.Length * 2];
        for (int i = 0; i < l.Length; i++) { inter[2 * i] = l[i]; inter[2 * i + 1] = r[i]; }
        using (var w = new Nota.Infrastructure.WavWriter(path, Rate, 2, WavBitDepth.Float32)) w.WriteFrames(inter, l.Length);
        return path;
    }

    // Play `seconds` from the start in 512-frame blocks (the averages need real blocks).
    private static void Play(NotaEngine eng, double seconds)
    {
        var buf = new float[512 * 2];
        eng.Seek(0); eng.Play();
        for (int f = 0; f < seconds * Rate; f += 512) eng.RenderOffline(buf, 512, Rate);
        eng.StopTransport();
    }

    private static StereoMeasure Row(NotaEngine eng, int id)
    {
        var rows = new StereoMeasure[64];
        int n = eng.GetStereoMeasures(rows);
        for (int i = 0; i < Math.Min(n, rows.Length); i++) if (rows[i].TrackId == id) return rows[i];
        return new StereoMeasure { TrackId = -1, LevelDb = float.NaN };
    }

    private static bool Near(float a, float b, float tol) => Math.Abs(a - b) <= tol;

    private static IEnumerable<(bool, string)> Measures()
    {
        var mono = Wav(Sine(440, 3, 0.25f), Sine(440, 3, 0.25f));
        var pair = Wav(Sine(440, 3, 0.25f), Sine(1000, 3, 0.25f));
        var anti = Wav(Sine(440, 3, 0.25f), Sine(440, 3, -0.25f));
        try
        {
            // Mono panned L50 under the Stereo law: balance on the pan scale, no width.
            using (var eng = new NotaEngine())
            {
                eng.SetBpm(120);
                eng.SetPanLaw(PanLaw.Stereo);
                int t = eng.AddAudioTrack();
                eng.AddAudioClip(t, mono, 0);
                eng.SetTrackPan(t, -0.5f);
                Play(eng, 2.0);
                var m = Row(eng, t);
                yield return (Near(m.Balance, -0.5f, 0.05f) && m.Width < 0.05f && m.Correlation > 0.95f,
                    $"stereo map: mono at L50 reads balance {m.Balance:0.000}, width {m.Width:0.000}, correlation {m.Correlation:0.000}");
            }

            // Unrelated left/right material at C, alone in the project: wide, uncorrelated; the
            // master row reads the same.
            using (var eng = new NotaEngine())
            {
                eng.SetBpm(120);
                eng.SetPanLaw(PanLaw.Stereo);
                int t = eng.AddAudioTrack();
                eng.AddAudioClip(t, pair, 0);
                Play(eng, 2.0);
                var m = Row(eng, t);
                yield return (Near(m.Balance, 0f, 0.05f) && m.Width > 0.95f && Math.Abs(m.Correlation) < 0.05f,
                    $"stereo map: unrelated L/R at C reads balance {m.Balance:0.000}, width {m.Width:0.000}, correlation {m.Correlation:0.000}");
                var ms = Row(eng, eng.MasterTrackId);
                yield return (Near(ms.Balance, m.Balance, 0.05f) && Near(ms.Width, m.Width, 0.05f) && Near(ms.Correlation, m.Correlation, 0.05f),
                    $"stereo map: the master row matches its only track (width {ms.Width:0.000}, correlation {ms.Correlation:0.000})");

                // Reaction (SC-005, τ = 0.3 s): muting drops the level by ≥ 20 dB within 1.5 s (the map
                // hides a muted track's cloud at once from the mute flag); unmuting reaches within
                // 3 dB of the steady level after 0.5 s.
                float steady = m.LevelDb;
                eng.SetTrackMute(t, true);
                Play(eng, 1.5);
                float muted = Row(eng, t).LevelDb;
                eng.SetTrackMute(t, false);
                Play(eng, 0.5);
                float back = Row(eng, t).LevelDb;
                yield return (steady - muted >= 20f && Math.Abs(back - steady) <= 3f,
                    $"stereo map: the level reacts in time (steady {steady:0.0}, muted 1.5 s {muted:0.0}, unmuted 0.5 s {back:0.0} dBFS)");
            }

            // Anti-phase pair: correlation −1, width 1.
            using (var eng = new NotaEngine())
            {
                eng.SetBpm(120);
                int t = eng.AddAudioTrack();
                eng.AddAudioClip(t, anti, 0);
                Play(eng, 2.0);
                var m = Row(eng, t);
                yield return (m.Correlation < -0.95f && m.Width == 1f,
                    $"stereo map: an anti-phase pair reads correlation {m.Correlation:0.000}, width {m.Width:0.000}");
            }

            // Silence: an empty track and an idle master read below −60 dBFS; groups have no row.
            using (var eng = new NotaEngine())
            {
                int t = eng.AddAudioTrack();
                int g = eng.AddGroupTrack();
                Play(eng, 0.5);
                var m = Row(eng, t);
                var gr = Row(eng, g);
                yield return (m.Silent && Row(eng, eng.MasterTrackId).Silent && gr.TrackId == -1,
                    $"stereo map: silence reads {m.LevelDb:0} dBFS; groups have no row");
            }
        }
        finally { File.Delete(mono); File.Delete(pair); File.Delete(anti); }
    }
}
