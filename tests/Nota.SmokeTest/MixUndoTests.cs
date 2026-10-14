// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Undo for volume and pan (specs/009-mix-param-undo): a gesture is one exact undo step, a
// no-change gesture is none, mix steps keep their place among structural steps, automation
// playback and loads never add steps, automation recording undoes with its value, and the
// MCP / Remote / MIDI sources each make one step. Also runnable alone:
// `dotnet run --project tests/Nota.SmokeTest -- --mixundo`.

using Nota.Application;
using Nota.Application.Editing;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class MixUndoTests
{
    public static IEnumerable<(bool ok, string label)> Run()
    {
        foreach (var r in Gestures()) yield return r;
        foreach (var r in OneHistory()) yield return r;
        foreach (var r in NotEdits()) yield return r;
        foreach (var r in Mcp()) yield return r;
    }

    // ---- US3: an MCP call is one step -------------------------------------------------

    private static IEnumerable<(bool, string)> Mcp()
    {
        using var eng = new NotaEngine();
        int t = eng.AddAudioTrack();
        eng.ClearUndoHistory();
        float v0 = Vol(eng, t), p0 = Pan(eng, t);
        var tools = new Nota.Mcp.Tools.TrackTools(eng, new SyncDispatch(), new NoRefresh());
        tools.SetTrackVolume(t, 0.3f).GetAwaiter().GetResult();
        tools.SetTrackPan(t, -0.5f).GetAwaiter().GetResult();
        eng.Undo(); bool panBack = Pan(eng, t) == p0 && Vol(eng, t) == 0.3f;
        eng.Undo();
        yield return (panBack && Vol(eng, t) == v0 && !eng.CanUndo, "mix undo: each MCP set_track_volume / set_track_pan call is one step");
    }

    // ---- US4: playback, loads, freeze make no steps; recording merges --------------------


    private static IEnumerable<(bool, string)> NotEdits()
    {
        var buf = new float[512 * 2];

        // Automation playback (read mode) moves volume and pan without a single step.
        using (var eng = new NotaEngine())
        {
            eng.SetBpm(120);
            int t = eng.AddInstrumentTrack();
            int lv = eng.AddAutomationLane(t, AutomationTarget.Volume, -1, -1);
            eng.SetAutomationPoints(t, lv, new[] { new AutomationPoint(0, 0.1f), new AutomationPoint(8, 0.5f) });
            int lp = eng.AddAutomationLane(t, AutomationTarget.Pan, -1, -1);
            eng.SetAutomationPoints(t, lp, new[] { new AutomationPoint(0, -1f), new AutomationPoint(4, 1f) });
            eng.ClearUndoHistory();
            eng.Seek(0); eng.Play();
            float vStart = Vol(eng, t);
            for (int i = 0; i < 200; i++) eng.RenderOffline(buf, 512, 48000);   // ≈ 2 s
            eng.StopTransport();
            yield return (!eng.CanUndo && Vol(eng, t) != vStart,
                $"mix undo: 2 s of automation playback moves the fader ({vStart} → {Vol(eng, t)}) and adds no step");
        }

        // A new project and a project load leave no volume/pan steps behind.
        using (var eng = new NotaEngine())
        {
            ProjectTemplate.ApplyDefault(eng);
            yield return (!eng.CanUndo, "mix undo: a new project has nothing to undo");
            int t = eng.AddAudioTrack();
            eng.SetTrackVolume(t, 0.42f);
            eng.SetTrackPan(t, 0.3f);
            string dir = Path.Combine(Path.GetTempPath(), "nota-mixundo-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new ProjectStore();
                store.Save(eng, new TransportState(120, 1, false, false), dir);
                using var e2 = new NotaEngine();
                store.Load(e2, dir);
                var vols = Enumerable.Range(0, e2.TrackCount).Select(i => e2.TryGetTrackInfo(i, out var ti) ? (ti.Id, ti.Volume, ti.Pan) : default).ToList();
                bool same = true;
                for (int i = 0; i < 300 && e2.CanUndo; i++)
                {
                    e2.Undo();
                    foreach (var (id, v, pn) in vols)
                        if (Info(e2, id).Id == id && (Vol(e2, id) != v || Pan(e2, id) != pn)) same = false;
                }
                yield return (same, "mix undo: undoing after a load never changes a loaded volume or pan");
            }
            finally { try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch (IOException) { } }
        }

        // Freezing a track makes no volume step.
        using (var eng = new NotaEngine())
        {
            eng.SetBpm(120);
            int t = eng.AddInstrumentTrack();
            int c = eng.AddMidiClip(t, 0, 2);
            eng.SetClipNotes(t, c, new[] { new NotaNote(60, 0, 2, 0.9f) });
            eng.SetTrackVolume(t, 0.6f);
            eng.ClearUndoHistory();
            new TrackFreezer().Freeze(eng, t, 2.0, restoreLoop: false, restoreMetronome: false);
            bool same = true;
            for (int i = 0; i < 20 && eng.CanUndo; i++) { eng.Undo(); if (Info(eng, t).Id == t && Vol(eng, t) != 0.6f) same = false; }
            yield return (same, "mix undo: freezing a track adds no volume step");
        }

        // Automation recording: the lane edit and the value are one step (FR-009). Offline
        // rendering doesn't sample live writes (that needs the audio clock), but starting the
        // write already creates the lane — the structural checkpoint the value must join.
        using (var eng = new NotaEngine())
        {
            eng.SetBpm(120);
            int t = eng.AddInstrumentTrack();
            eng.ClearUndoHistory();
            float v0 = Vol(eng, t);
            int p0 = eng.AutomationLaneCount(t);
            eng.SetAutomationRecord(true);
            eng.Seek(0); eng.Play();
            int h = eng.BeginMixGesture(MixTarget.Volume(t));   // before the automation write, as the UI does
            eng.BeginAutomationWrite(t, AutomationTarget.Volume, -1, -1, "");
            for (int i = 0; i < 40; i++) { eng.SetTrackVolume(t, 0.2f + i * 0.01f); eng.RenderOffline(buf, 512, 48000); }
            eng.EndAutomationWrite(t, AutomationTarget.Volume, -1, -1, "");
            eng.EndMixGesture(h);
            eng.StopTransport();
            eng.SetAutomationRecord(false);
            int p1 = eng.AutomationLaneCount(t);
            float v1 = Vol(eng, t);
            eng.Undo();
            bool undone = eng.AutomationLaneCount(t) == p0 && Vol(eng, t) == v0 && !eng.CanUndo;
            eng.Redo();
            yield return (p1 > p0 && undone && eng.AutomationLaneCount(t) == p1 && Vol(eng, t) == v1,
                $"mix undo: the recorded lane and the value undo and redo together ({p0}→{p1} lanes, {v0}→{v1})");
        }
    }

    // ---- US2: one ordered history (contracts/c-abi-mix-undo.md) --------------------------

    private static void Gesture(NotaEngine e, MixTarget t, Action set)
    {
        int h = e.BeginMixGesture(t);
        set();
        e.EndMixGesture(h);
    }

    private static IEnumerable<(bool, string)> OneHistory()
    {
        using var eng = new NotaEngine();
        int a = eng.AddAudioTrack();
        eng.ClearUndoHistory();
        // State 0 → add track (1) → volume a (2) → pan law (3) → pan a (4)
        float vol0 = Vol(eng, a), pan0 = Pan(eng, a);
        int b = eng.AddAudioTrack();
        Gesture(eng, MixTarget.Volume(a), () => eng.SetTrackVolume(a, 0.3f));
        eng.SetPanLaw(PanLaw.Stereo);
        Gesture(eng, MixTarget.Pan(a), () => eng.SetTrackPan(a, -0.7f));
        bool s4 = eng.TrackCount == 2 && Vol(eng, a) == 0.3f && eng.PanLaw == PanLaw.Stereo && Pan(eng, a) == -0.7f;
        eng.Undo(); bool s3 = Pan(eng, a) == pan0 && eng.PanLaw == PanLaw.Stereo && Vol(eng, a) == 0.3f;
        eng.Undo(); bool s2 = eng.PanLaw == PanLaw.Balance && Vol(eng, a) == 0.3f && eng.TrackCount == 2;
        eng.Undo(); bool s1 = Vol(eng, a) == vol0 && eng.TrackCount == 2;
        eng.Undo(); bool s0 = eng.TrackCount == 1 && !eng.CanUndo;
        yield return (s4 && s3 && s2 && s1 && s0, $"mix undo: four mixed edits undo in reverse order ({s4},{s3},{s2},{s1},{s0})");
        eng.Redo(); eng.Redo(); eng.Redo(); eng.Redo();
        yield return (eng.TrackCount == 2 && Vol(eng, a) == 0.3f && eng.PanLaw == PanLaw.Stereo && Pan(eng, a) == -0.7f && !eng.CanRedo,
            "mix undo: …and redo replays them in order");
        eng.Undo(); eng.Undo();
        Gesture(eng, MixTarget.Volume(a), () => eng.SetTrackVolume(a, 0.9f));
        yield return (!eng.CanRedo, "mix undo: a new edit after undos drops the redo branch");
        GC.KeepAlive(b);

        // A track removed while a gesture on it is open: no mix step, the removal is the step.
        using var e2 = new NotaEngine();
        int t = e2.AddAudioTrack(), keep = e2.AddAudioTrack();
        e2.ClearUndoHistory();
        int h = e2.BeginMixGesture(MixTarget.Volume(t));
        e2.SetTrackVolume(t, 0.2f);
        e2.RemoveTrack(t);
        e2.EndMixGesture(h);
        e2.Undo();
        yield return (e2.TrackCount == 2 && !e2.CanUndo, "mix undo: a track removed mid-gesture leaves only the removal step");

        // Volume of t, then remove t: undo brings t back first, then its volume.
        e2.ClearUndoHistory();
        float tv0 = Vol(e2, t);
        Gesture(e2, MixTarget.Volume(t), () => e2.SetTrackVolume(t, 0.4f));
        e2.RemoveTrack(t);
        e2.Undo(); bool back = e2.TrackCount == 2 && Vol(e2, t) == 0.4f;
        e2.Undo();
        yield return (back && Vol(e2, t) == tv0, $"mix undo: undo restores a removed track, then its volume ({Vol(e2, t)})");

        // Two gestures open at once on different tracks: two steps, in end order.
        e2.ClearUndoHistory();
        float k0 = Vol(e2, keep), t0 = Vol(e2, t);
        int h1 = e2.BeginMixGesture(MixTarget.Volume(t));
        int h2 = e2.BeginMixGesture(MixTarget.Volume(keep));
        e2.SetTrackVolume(t, 0.11f); e2.SetTrackVolume(keep, 0.22f);
        e2.EndMixGesture(h2); e2.EndMixGesture(h1);
        e2.Undo(); bool firstUndo = Vol(e2, t) == t0 && Vol(e2, keep) == 0.22f;
        e2.Undo();
        yield return (firstUndo && Vol(e2, keep) == k0, "mix undo: overlapping gestures make two steps, undone in reverse end order");

        // An undo group around two gestures on the same track: one step, first before → last after.
        e2.ClearUndoHistory();
        float g0 = Vol(e2, t);
        e2.BeginUndoGroup();
        Gesture(e2, MixTarget.Volume(t), () => e2.SetTrackVolume(t, 0.6f));
        Gesture(e2, MixTarget.Volume(t), () => e2.SetTrackVolume(t, 0.7f));
        e2.EndUndoGroup();
        e2.Undo();
        bool grouped = Vol(e2, t) == g0 && !e2.CanUndo;
        e2.Redo();
        yield return (grouped && Vol(e2, t) == 0.7f, "mix undo: an undo group of two gestures is one step");

        // A group that starts with a gesture and then makes a structural edit keeps the graph.
        e2.ClearUndoHistory();
        int n0 = e2.TrackCount;
        e2.BeginUndoGroup();
        Gesture(e2, MixTarget.Volume(t), () => e2.SetTrackVolume(t, 0.33f));
        e2.AddAudioTrack();
        e2.EndUndoGroup();
        e2.Undo();
        yield return (e2.TrackCount == n0 && Vol(e2, t) == 0.7f && !e2.CanUndo,
            $"mix undo: a group of a gesture then a track add undoes both ({e2.TrackCount} tracks, vol {Vol(e2, t)})");
    }

    private static NotaTrackInfo Info(NotaEngine e, int id)
    {
        for (int i = 0; i < e.TrackCount; i++)
            if (e.TryGetTrackInfo(i, out var t) && t.Id == id) return t;
        return default;
    }
    private static float Vol(NotaEngine e, int id) => Info(e, id).Volume;
    private static float Pan(NotaEngine e, int id) => Info(e, id).Pan;

    // ---- the engine API (contracts/c-abi-mix-undo.md) ---------------------------------

    private static IEnumerable<(bool, string)> Gestures()
    {
        using var eng = new NotaEngine();
        int t = eng.AddAudioTrack();
        eng.ClearUndoHistory();
        eng.SetTrackVolume(t, 0.8f);   // a plain set: never a step
        yield return (!eng.CanUndo, "mix undo: a plain volume set makes no step");

        float start = Vol(eng, t);
        int h = eng.BeginMixGesture(MixTarget.Volume(t));
        for (int i = 1; i <= 50; i++) eng.SetTrackVolume(t, 0.8f + i * 0.0137f);
        float last = Vol(eng, t);
        eng.EndMixGesture(h);
        bool one = eng.CanUndo;
        eng.Undo();
        bool back = Vol(eng, t) == start && !eng.CanUndo;
        eng.Redo();
        yield return (h > 0 && one && back && Vol(eng, t) == last,
            $"mix undo: a 50-value drag is one step, undo restores exactly {start}, redo exactly {last} (now {Vol(eng, t)})");

        eng.ClearUndoHistory();
        eng.EndMixGesture(eng.BeginMixGesture(MixTarget.Pan(t)));
        yield return (!eng.CanUndo, "mix undo: a gesture without a change makes no step");

        float pan0 = Pan(eng, t);
        h = eng.BeginMixGesture(MixTarget.Pan(t));
        eng.SetTrackPan(t, 0.4f);
        eng.SetTrackPan(t, pan0);
        eng.EndMixGesture(h);
        yield return (!eng.CanUndo, "mix undo: a drag back to the start value makes no step");

        eng.SetMasterVolume(1f);
        h = eng.BeginMixGesture(MixTarget.Master);
        eng.SetMasterVolume(0.5f);
        eng.EndMixGesture(h);
        eng.Undo();
        bool masterBack = eng.MasterVolume == 1f;
        eng.Redo();
        yield return (masterBack && eng.MasterVolume == 0.5f, $"mix undo: master volume undoes and redoes ({eng.MasterVolume})");

        eng.ClearUndoHistory();
        h = eng.BeginMixGesture(MixTarget.Volume(t));
        eng.SetTrackVolume(t, 0.3f);
        eng.Reset();
        eng.EndMixGesture(h);
        yield return (!eng.CanUndo, "mix undo: ending a gesture after a reset makes no step");

        yield return (eng.BeginMixGesture(MixTarget.Volume(9999)) == 0, "mix undo: a gesture on a missing track is refused");
        yield return (eng.MixUndoSelfTest(), "mix undo: undo/redo pass over a step whose track is gone (FR-010)");

        int t2 = eng.AddAudioTrack();
        eng.ClearUndoHistory();
        float v2 = Vol(eng, t2);
        MixEdit.Step(eng, () => eng.SetTrackVolume(t2, 0.25f), MixTarget.Volume(t2));
        bool stepped = eng.CanUndo;
        eng.Undo();
        yield return (stepped && Vol(eng, t2) == v2 && !eng.CanUndo, $"mix undo: MixEdit.Step is one step (back to {Vol(eng, t2)}, want {v2})");

        // A drag allocates nothing per move (plan: performance): 1000 values inside an open gesture.
        var drag = new MixGesture(eng, MixTarget.Volume(t2));
        Action<double> setter = v => eng.SetTrackVolume(t2, (float)v);
        drag.Begin();
        drag.Change(0.5, setter);   // warm up the path (JIT, P/Invoke stub)
        long bytes0 = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) drag.Change(0.4 + i * 1e-4, setter);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - bytes0;
        drag.End();
        eng.Undo();
        yield return (allocated == 0, $"mix undo: 1000 values in an open gesture allocate {allocated} bytes");

        // A controller stream: one step per movement, ended by the pause (US3 scenario 3).
        eng.ClearUndoHistory();
        float c0 = Vol(eng, t2);
        var knob = new MixMovement(eng, MixTarget.Volume(t2), TimeSpan.FromSeconds(1));
        knob.Touch(() => eng.SetTrackVolume(t2, 0.3f), 0);
        knob.Touch(() => eng.SetTrackVolume(t2, 0.35f), 400);
        knob.Poll(1300);                                 // 900 ms since the last touch: still moving
        bool stillOpen = knob.IsOpen && !eng.CanUndo;
        knob.Poll(1400);                                 // 1 s: the movement ends → one step
        knob.Touch(() => eng.SetTrackVolume(t2, 0.6f), 3000);
        knob.Poll(4100);
        eng.Undo(); bool first = Vol(eng, t2) == 0.35f;
        eng.Undo();
        yield return (stillOpen && first && Vol(eng, t2) == c0 && !eng.CanUndo,
            $"mix undo: two controller movements 1.6 s apart are two steps, a continuous one is one ({Vol(eng, t2)}, want {c0})");
    }
}
