// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Project pan law (specs/006-stereo-pan-law): the law is graph data (undoable, survives every
// edit), Stereo folds the far channel in while Balance drops it, both agree at centre and
// for mono, groups ignore it, projects round-trip it and older ones open as Balance, and a
// switch mid-playback does not click. Rendered from deterministic WAV clips (synth notes
// drift between renders). Also runnable alone: `dotnet run --project tests/Nota.SmokeTest -- --panlaw`.

using System.Text.Json.Nodes;
using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class PanLawTests
{
    private const int Rate = 48000;
    private const int Utility = 4;   // built-in Utility: unity gain, no tail

    public static IEnumerable<(bool ok, string label)> Run()
    {
        foreach (var r in Abi()) yield return r;
        foreach (var r in SurvivesEdits()) yield return r;
        foreach (var r in StereoImage()) yield return r;
        foreach (var r in ReturnsGroupsTemplate()) yield return r;
        foreach (var r in Persistence()) yield return r;
        foreach (var r in SwitchWithoutClick()) yield return r;
        foreach (var r in LoadTakesLawAtOnce()) yield return r;
    }

    // What the pan stage must output for a source sample pair under `law` (research R1, the
    // same float steps as the engine), for sample-exact comparisons.
    private static (float l, float r) LawOut(float inL, float inR, float pan, float vol, PanLaw law)
    {
        double theta = (pan + 1.0) * (2 * Math.PI / 8.0), c = Math.Cos(theta), s = Math.Sin(theta);
        if (law == PanLaw.Balance) return (inL * ((float)c * vol), inR * ((float)s * vol));
        double b = 0.5 * Math.Abs(pan);
        return pan <= 0
            ? (((float)(c - b) * vol) * inL + ((float)b * vol) * inR, ((float)s * vol) * inR)
            : (((float)c * vol) * inL, ((float)b * vol) * inL + ((float)(s - b) * vol) * inR);
    }

    // Largest per-sample error of `buf` (frames [from, to), output frame i = source frame
    // i + offset) against LawOut of the source — in dBFS.
    private static double NullDb(float[] buf, int from, int to, int offset, float[] srcL, float[] srcR, float pan, float vol, PanLaw law)
    {
        double worst = 0;
        for (int i = from; i < to; i++)
        {
            var (l, r) = LawOut(srcL[i + offset], srcR[i + offset], pan, vol, law);
            worst = Math.Max(worst, Math.Max(Math.Abs(buf[2 * i] - l), Math.Abs(buf[2 * i + 1] - r)));
        }
        return Db(worst);
    }

    private static float TrackVolume(NotaEngine eng, int id)
    {
        for (int i = 0; i < eng.TrackCount; i++)
            if (eng.TryGetTrackInfo(i, out var t) && t.Id == id) return t.Volume;
        return float.NaN;
    }

    // A law set without a checkpoint (project load, the new-project baseline) applies at once —
    // no crossfade from the previous project's law (T038).
    private static IEnumerable<(bool, string)> LoadTakesLawAtOnce()
    {
        var srcL = Sine(440, 1.5, Amp); var srcR = Sine(1000, 1.5, Amp);
        var two = WriteStereoWav(srcL, srcR);
        try
        {
            using var eng = new NotaEngine();
            eng.SetBpm(120);
            int t = eng.AddAudioTrack();
            eng.AddAudioClip(t, two, 0);
            eng.SetTrackPan(t, -1);
            float vol = TrackVolume(eng, t);
            const int lead = Rate / 5, block = 256;
            var pre = new float[lead * 2];
            var buf = new float[block * 2];
            eng.Seek(0);
            eng.Play();
            eng.RenderOffline(pre, lead, Rate);   // Balance
            eng.SetPanLaw(PanLaw.Stereo, checkpoint: false);
            eng.RenderOffline(buf, block, Rate);
            eng.StopTransport();
            double err = NullDb(buf, 0, block, lead, srcL, srcR, -1, vol, PanLaw.Stereo);
            yield return (err < -100, $"pan law: a law set by a load applies at once, no crossfade (residual {err:0} dBFS)");
        }
        finally { File.Delete(two); }
    }

    // ---- US3: switching mid-playback crossfades (FR-009, SC-004) -------------------------

    private static IEnumerable<(bool, string)> SwitchWithoutClick()
    {
        var two = WriteStereoWav(Sine(440, 1.5, Amp), Sine(1000, 1.5, Amp));
        try
        {
            foreach (bool flipBack in new[] { false, true })
            {
                using var eng = new NotaEngine();
                eng.SetBpm(120);
                int t = eng.AddAudioTrack();
                eng.AddAudioClip(t, two, 0);
                eng.SetTrackPan(t, -1);   // where the two laws differ most

                // Render in 256-frame blocks while rolling; switch Balance → Stereo between two
                // blocks at 0.5 s (and back one block later, mid-fade, on the second pass).
                const int block = 256, total = Rate / block * block;
                var buf = new float[total * 2];
                var chunk = new float[block * 2];
                int switchAt = Rate / 2 / block * block, backAt = switchAt + block;   // back one block (5.3 ms) later
                eng.Seek(0);
                eng.Play();
                for (int f = 0; f < total; f += block)
                {
                    if (f == switchAt) eng.SetPanLaw(PanLaw.Stereo);
                    if (flipBack && f == backAt) eng.SetPanLaw(PanLaw.Balance);
                    eng.RenderOffline(chunk, block, Rate);
                    Array.Copy(chunk, 0, buf, f * 2, block * 2);
                }
                eng.StopTransport();

                // Largest sample-to-sample step per channel: steady before, steady after, and
                // across the switch (its 30 ms after). The switch must not outdo the steady
                // signals by more than 1.5×.
                double Step(int ch, int from, int to)
                {
                    double m = 0;
                    for (int i = from + 1; i < to; i++) m = Math.Max(m, Math.Abs(buf[2 * i + ch] - buf[2 * (i - 1) + ch]));
                    return m;
                }
                int win = 30 * Rate / 1000;
                bool ok = true; string detail = "";
                for (int ch = 0; ch < 2; ch++)
                {
                    double before = Step(ch, W0, switchAt), after = Step(ch, total - Rate / 5, total);
                    double across = Step(ch, switchAt - 1, switchAt + win);
                    ok &= across <= 1.5 * Math.Max(before, after) + 1e-6;
                    detail += $"{(ch == 0 ? "L" : " R")} {across:0.0000} vs {Math.Max(before, after):0.0000}";
                }
                yield return (ok, $"pan law: switching {(flipBack ? "and switching back mid-fade " : "")}while playing does not click ({detail})");
            }
        }
        finally { File.Delete(two); }
    }

    // ---- US2: format v21 keeps the law; older projects open as Balance ------------------

    private static IEnumerable<(bool, string)> Persistence()
    {
        var srcL = Sine(440, 1.5, Amp); var srcR = Sine(1000, 1.5, Amp);
        var two = WriteStereoWav(srcL, srcR);
        var store = new ProjectStore();
        string Dir() => Path.Combine(Path.GetTempPath(), "nota-panlaw-" + Guid.NewGuid().ToString("N"));
        var dirs = new List<string>();
        try
        {
            // Round trip of both laws and of the older-project mark.
            foreach (var (law, legacy) in new[] { (PanLaw.Stereo, false), (PanLaw.Balance, true) })
            {
                using var eng = new NotaEngine();
                eng.AddAudioTrack();
                eng.SetPanLaw(law);
                string dir = Dir(); dirs.Add(dir);
                store.Save(eng, new TransportState(120, 1, false, false, PanLawLegacy: legacy), dir);
                using var eng2 = new NotaEngine();
                var res = store.Load(eng2, dir);
                yield return (eng2.PanLaw == law && res.Transport.PanLawLegacy == legacy,
                    $"pan law: save/load keeps {law}{(legacy ? " and the older-project mark" : "")} (FR-008)");
            }

            // A v20 project (no `mixer`) with a stereo track panned −0.5: opens as Balance + older
            // project, and sounds by the Balance law — cos θ on L, sin θ on R, nothing crossed.
            {
                using var eng = new NotaEngine();
                eng.SetBpm(120);
                int t = eng.AddAudioTrack();
                eng.AddAudioClip(t, two, 0);
                eng.SetTrackPan(t, -0.5f);
                string dir = Dir(); dirs.Add(dir);
                store.Save(eng, new TransportState(120, 1, false, false), dir);
                string manifest = Path.Combine(dir, "project.json");
                var root = (JsonObject)JsonNode.Parse(File.ReadAllText(manifest))!;
                root.Remove("mixer");
                root["formatVersion"] = 20;
                File.WriteAllText(manifest, root.ToJsonString());

                using var eng2 = new NotaEngine();
                var res = store.Load(eng2, dir);
                yield return (eng2.PanLaw == PanLaw.Balance && res.Transport.PanLawLegacy,
                    "pan law: a v20 project opens with Balance and the older-project mark (FR-006)");
                var rendered = Render(eng2, 1.3);
                int tid = -1;
                for (int i = 0; i < eng2.TrackCount; i++) if (eng2.TryGetTrackInfo(i, out var ti) && ti.Id > 0) { tid = ti.Id; break; }
                double nullDb = NullDb(rendered, W0, W1, 0, srcL, srcR, -0.5f, TrackVolume(eng2, tid), PanLaw.Balance);
                yield return (nullDb < -120, $"pan law: …and renders sample-for-sample by the Balance law (residual {nullDb:0} dBFS, SC-002)");
                var k = Tones(rendered);
                double theta = (-0.5 + 1) * Math.PI / 4;
                double ratio = Db(k[0]) - Db(k[3]), want = Db(Math.Cos(theta)) - Db(Math.Sin(theta));
                double crossDb = Db(Math.Max(k[1], k[2])) - Db(k[3]);
                yield return (Math.Abs(ratio - want) < 0.01 && crossDb < -90,
                    $"pan law: …and sounds by the Balance law (L/R {ratio:0.000} dB, want {want:0.000}; cross {crossDb:0} dB, SC-002)");
            }

            // The migration step itself: writes the Balance + legacy mixer, keeps an existing one.
            var bare = ProjectMigrations.Migrate(new JsonObject { ["formatVersion"] = 20 });
            var kept = ProjectMigrations.Migrate(new JsonObject { ["formatVersion"] = 20, ["mixer"] = new JsonObject { ["panLaw"] = "stereo" } });
            yield return ((string?)bare["mixer"]?["panLaw"] == "balance" && (bool?)bare["mixer"]?["panLawLegacy"] == true
                          && (string?)kept["mixer"]?["panLaw"] == "stereo",
                "pan law: migration v20 → v21 marks older projects Balance + legacy, keeps an existing mixer");
        }
        finally
        {
            File.Delete(two);
            foreach (var d in dirs) try { if (Directory.Exists(d)) Directory.Delete(d, recursive: true); } catch (IOException) { }
        }
    }

    // Measurement window: 1 s from 0.2 s (whole cycles of 440 Hz and 1 kHz; past any fade).
    private const int W0 = Rate / 5, W1 = W0 + Rate;
    private const float Amp = 0.25f;   // −12 dBFS per tone

    // Level of each tone on each output channel: [440 on L, 440 on R, 1k on L, 1k on R].
    private static double[] Tones(float[] buf) => new[]
    {
        Goertzel(buf, 0, 440, W0, W1), Goertzel(buf, 1, 440, W0, W1),
        Goertzel(buf, 0, 1000, W0, W1), Goertzel(buf, 1, 1000, W0, W1),
    };

    // ---- US1: Stereo moves the whole image (FR-002 … FR-005) ---------------------------

    private static IEnumerable<(bool, string)> StereoImage()
    {
        var two = WriteStereoWav(Sine(440, 1.5, Amp), Sine(1000, 1.5, Amp));   // L = 440, R = 1 kHz
        var mono = WriteStereoWav(Sine(440, 1.5, Amp), Sine(440, 1.5, Amp));
        try
        {
            using var eng = new NotaEngine();
            eng.SetBpm(120);
            int t = eng.AddAudioTrack();
            eng.AddAudioClip(t, two, 0);

            eng.SetPanLaw(PanLaw.Balance);
            eng.SetTrackPan(t, 0);
            var cBal = Render(eng, 1.3);
            eng.SetPanLaw(PanLaw.Stereo);
            var cSt = Render(eng, 1.3);
            yield return (cBal.AsSpan().SequenceEqual(cSt), "pan law: at centre Stereo renders exactly as Balance (FR-003)");
            var c = Tones(cSt);   // centre: 440 on L and 1 kHz on R, each at cos(π/4)·source

            foreach (var side in new[] { -1f, 1f })
            {
                string at = side < 0 ? "L100" : "R100";
                eng.SetTrackPan(t, side);
                var e = Tones(Render(eng, 1.3));
                // The near side carries both tones at half the source amplitude = −3.01 dB under
                // their centre level; the far side is silent.
                double near440 = side < 0 ? e[0] : e[1], near1k = side < 0 ? e[2] : e[3];
                double far440 = side < 0 ? e[1] : e[0], far1k = side < 0 ? e[3] : e[2];
                double d440 = Db(near440) - Db(c[0]), d1k = Db(near1k) - Db(c[3]);
                yield return (Math.Abs(d440 + 3.01) < 0.5 && Math.Abs(d1k + 3.01) < 0.5,
                    $"pan law: Stereo at {at} keeps both channels on the {(side < 0 ? "left" : "right")} (440 Hz {d440:+0.00;-0.00} dB, 1 kHz {d1k:+0.00;-0.00} dB re centre)");
                double farDb = Db(Math.Max(far440, far1k)) - Db(c[0]);
                yield return (farDb < -90, $"pan law: Stereo at {at} leaves the far side silent ({farDb:0} dB)");

                double pC = c[0] * c[0] + c[1] * c[1] + c[2] * c[2] + c[3] * c[3];
                double pE = e[0] * e[0] + e[1] * e[1] + e[2] * e[2] + e[3] * e[3];
                double loss = 10 * Math.Log10(pE / pC);
                yield return (loss > -3.5, $"pan law: unrelated L/R at {at} stays within 3.5 dB of centre ({loss:0.00} dB, FR-005)");
            }

            // Balance still drops the far channel (the legacy law is untouched).
            eng.SetPanLaw(PanLaw.Balance);
            eng.SetTrackPan(t, -1);
            var b = Tones(Render(eng, 1.3));
            yield return (Db(b[2]) - Db(c[3]) < -90, "pan law: Balance at L100 still drops the right channel");

            // Mono material: Stereo == Balance at every position (FR-004).
            eng.RemoveTrack(t);
            int m = eng.AddAudioTrack();
            eng.AddAudioClip(m, mono, 0);
            double worst = 0;
            foreach (var p in new[] { -1f, -0.6f, -0.25f, 0.3f, 0.8f, 1f })
            {
                eng.SetTrackPan(m, p);
                eng.SetPanLaw(PanLaw.Balance);
                var mb = Render(eng, 1.3);
                eng.SetPanLaw(PanLaw.Stereo);
                var ms = Render(eng, 1.3);
                for (int ch = 0; ch < 2; ch++)
                {
                    double lb = Goertzel(mb, ch, 440, W0, W1), ls = Goertzel(ms, ch, 440, W0, W1);
                    if (lb < 1e-6 && ls < 1e-6) continue;   // both silent (full pan, far side)
                    worst = Math.Max(worst, Math.Abs(Db(ls) - Db(lb)));
                }
            }
            yield return (worst < 0.1, $"pan law: mono sounds the same under Stereo and Balance at every pan (worst {worst:0.000} dB, FR-004)");
        }
        finally { File.Delete(two); File.Delete(mono); }
    }

    // ---- US1: returns follow the law, groups don't, new projects use Stereo -------------

    private static IEnumerable<(bool, string)> ReturnsGroupsTemplate()
    {
        var two = WriteStereoWav(Sine(440, 1.5, Amp), Sine(1000, 1.5, Amp));
        try
        {
            // A return panned hard left: Stereo keeps the right channel of what it receives
            // (the 1 kHz tone reaches the left), Balance drops it. The source track is centred,
            // so its own direct signal puts no 1 kHz on the left.
            using (var eng = new NotaEngine())
            {
                eng.SetBpm(120);
                int t = eng.AddAudioTrack();
                eng.AddAudioClip(t, two, 0);
                int ret = eng.AddReturnTrack();
                eng.AddBuiltinDevice(ret, Utility);
                eng.SetTrackSend(t, 0, 1f);
                eng.SetTrackPan(ret, -1);
                eng.SetPanLaw(PanLaw.Stereo);
                var st = Tones(Render(eng, 1.3));
                eng.SetPanLaw(PanLaw.Balance);
                var bal = Tones(Render(eng, 1.3));
                double stDb = Db(st[2]) - Db(st[3]), balDb = Db(bal[2]) - Db(bal[3]);
                yield return (stDb > -12 && balDb < -90,
                    $"pan law: a return panned L100 keeps both channels under Stereo (1 kHz on L {stDb:0.0} dB re R; Balance {balDb:0} dB)");
            }

            // A group panned −0.5 over a centred child: identical under both laws (FR-010).
            using (var eng = new NotaEngine())
            {
                eng.SetBpm(120);
                int t = eng.AddAudioTrack();
                eng.AddAudioClip(t, two, 0);
                int grp = eng.AddGroupTrack();
                eng.SetTrackGroup(t, grp);
                eng.SetTrackPan(grp, -0.5f);
                eng.SetPanLaw(PanLaw.Balance);
                var bal = Render(eng, 1.3);
                eng.SetPanLaw(PanLaw.Stereo);
                var st = Render(eng, 1.3);
                yield return (bal.AsSpan().SequenceEqual(st), "pan law: group pan ignores the project law (FR-010)");
            }

            // New projects start with Stereo, and that is the baseline, not an undo step (FR-007).
            using (var eng = new NotaEngine())
            {
                ProjectTemplate.ApplyDefault(eng);
                yield return (eng.PanLaw == PanLaw.Stereo && !eng.CanUndo, "pan law: a new project starts with Stereo, not undoable (FR-007)");
            }
        }
        finally { File.Delete(two); }
    }

    // ---- the law as engine state (contracts/c-abi-pan-law.md) -------------------------

    private static IEnumerable<(bool, string)> Abi()
    {
        using var eng = new NotaEngine();
        yield return (eng.PanLaw == PanLaw.Balance, "pan law: a fresh engine uses Balance");

        int t = eng.AddAudioTrack();   // an edit to undo past
        eng.SetPanLaw(PanLaw.Stereo);
        yield return (eng.PanLaw == PanLaw.Stereo, "pan law: set Stereo");
        eng.Undo();
        bool undone = eng.PanLaw == PanLaw.Balance && eng.TrackCount == 1;
        eng.Redo();
        yield return (undone && eng.PanLaw == PanLaw.Stereo, "pan law: the switch is one undo step (undo → Balance, redo → Stereo)");

        eng.SetPanLaw(PanLaw.Stereo);   // unchanged: no new step
        eng.Undo();
        yield return (eng.PanLaw == PanLaw.Balance && eng.TrackCount == 1, "pan law: setting the same law adds no undo step");
        eng.Redo();

        // Without a checkpoint the Stereo state is not on the undo stack: undo lands on the
        // Balance state from before the earlier switch, not on Stereo.
        eng.SetPanLaw(PanLaw.Balance, checkpoint: false);
        eng.Undo();
        yield return (eng.PanLaw == PanLaw.Balance && eng.TrackCount == 1, "pan law: checkpoint = false adds no undo step");

        bool threw = false;
        try { eng.SetPanLaw((PanLaw)7); } catch (NotaEngineException) { threw = true; }
        yield return (threw && eng.PanLaw == PanLaw.Balance, "pan law: an unknown law is refused");

        eng.SetPanLaw(PanLaw.Stereo);
        eng.Reset();
        yield return (eng.PanLaw == PanLaw.Balance, "pan law: reset returns to Balance");
        GC.KeepAlive(t);
    }

    // Graph-level fields must be carried through every fresh-graph build (inheritGraphLevel).
    private static IEnumerable<(bool, string)> SurvivesEdits()
    {
        using var eng = new NotaEngine();
        int inst = eng.AddInstrumentTrack();
        eng.SetPanLaw(PanLaw.Stereo);
        bool Kept() => eng.PanLaw == PanLaw.Stereo;

        int audio = eng.AddAudioTrack();
        yield return (Kept(), "pan law survives: add audio track");
        int ret = eng.AddReturnTrack();
        yield return (Kept(), "pan law survives: add return");
        eng.MoveTrack(audio, 0);
        yield return (Kept(), "pan law survives: move track");
        int group = eng.AddGroupTrack();
        eng.SetTrackGroup(audio, group);
        yield return (Kept(), "pan law survives: group a track");
        eng.AddScene();
        yield return (Kept(), "pan law survives: add scene");
        eng.RemoveScene(eng.SceneCount - 1);
        yield return (Kept(), "pan law survives: remove scene");
        int lane = eng.AddAutomationLane(inst, AutomationTarget.Volume, -1, -1);
        eng.SetAutomationPoints(inst, lane, new[] { new AutomationPoint(0, 0.5f), new AutomationPoint(4, 1f) });
        yield return (Kept(), "pan law survives: automation edit");
        int mod = eng.ModulatorAdd(inst, 0);
        if (mod >= 0) eng.CvLinkAdd(inst, mod, -1, 0);
        yield return (Kept(), "pan law survives: modulator + CV link");
        int clip = eng.AddMidiClip(inst, 0, 4);
        eng.SetClipNotes(inst, clip, new[] { new NotaNote(60, 0, 1, 0.8f) });
        yield return (Kept(), "pan law survives: MIDI clip edit");
        eng.DuplicateTrack(inst);
        yield return (Kept(), "pan law survives: duplicate track");
        eng.AddBuiltinDevice(ret, Utility);
        yield return (Kept(), "pan law survives: add device");
        eng.RemoveTrack(audio);
        yield return (Kept(), "pan law survives: remove track");
        eng.Undo(); eng.Undo();
        yield return (Kept(), "pan law survives: undo of later edits");
        eng.Redo(); eng.Redo();
        yield return (Kept(), "pan law survives: redo");
    }

    // ---- helpers -------------------------------------------------------------------------

    private static float[] Sine(double hz, double seconds, float amp)
    {
        var x = new float[(int)(seconds * Rate)];
        for (int i = 0; i < x.Length; i++) x[i] = amp * (float)Math.Sin(2 * Math.PI * hz * i / Rate);
        return x;
    }

    private static string WriteStereoWav(float[] l, float[] r)
    {
        var path = Path.Combine(Path.GetTempPath(), $"nota-panlaw-{Guid.NewGuid():N}.wav");
        var inter = new float[l.Length * 2];
        for (int i = 0; i < l.Length; i++) { inter[2 * i] = l[i]; inter[2 * i + 1] = r[i]; }
        using (var w = new Nota.Infrastructure.WavWriter(path, Rate, 2, WavBitDepth.Float32)) w.WriteFrames(inter, l.Length);
        return path;
    }

    private static float[] Render(NotaEngine eng, double seconds)
    {
        int frames = (int)(seconds * Rate);
        var buf = new float[frames * 2];
        eng.Seek(0);
        eng.Play();
        eng.RenderOffline(buf, frames, Rate);
        eng.StopTransport();
        return buf;
    }

    // Amplitude of a sine at `hz` on one channel of an interleaved buffer, over [from, to) frames.
    private static double Goertzel(float[] inter, int channel, double hz, int from, int to)
    {
        double w = 2 * Math.PI * hz / Rate, c = 2 * Math.Cos(w), s1 = 0, s2 = 0;
        for (int i = from; i < to; i++) { double s0 = inter[2 * i + channel] + c * s1 - s2; s2 = s1; s1 = s0; }
        double power = s1 * s1 + s2 * s2 - c * s1 * s2;
        return 2 * Math.Sqrt(Math.Max(0, power)) / (to - from);
    }

    private static double Db(double amp) => 20 * Math.Log10(Math.Max(amp, 1e-12));
}
