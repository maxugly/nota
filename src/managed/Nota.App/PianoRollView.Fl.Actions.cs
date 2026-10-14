// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Input;
using Nota.Application;

namespace Nota.App;

public sealed partial class PianoRollView
{
    public Action<IReadOnlyList<NotaNote>, double>? CommitResized { get; set; }
    public Action<int, float>? PreviewNoteOn { get; set; }
    public Action<int>? PreviewNoteOff { get; set; }
    internal void FocusFlGrid() => _gridControl.Focus();
    public bool HandleFlShortcut(KeyEventArgs e) => FlWorkflow && _gridControl.FlKeyDown(e);
    public bool FlSnapEnabled { get; private set; } = true;
    public bool FlKeyboardLabels { get; private set; }
    private bool _flAllowLeftResize;

    public void FlDeselect() { _selection.Clear(); Invalidate(); Changed?.Invoke(); }
    public void FlSelectAll() { _selection.Clear(); for (int i = 0; i < _notes.Count; i++) _selection.Add(i); Invalidate(); Changed?.Invoke(); }
    public void FlInvertSelection() { for (int i = 0; i < _notes.Count; i++) if (!_selection.Add(i)) _selection.Remove(i); Invalidate(); Changed?.Invoke(); }
    public void FlToggleSnap() { FlSnapEnabled = !FlSnapEnabled; Changed?.Invoke(); }
    public void FlToggleKeyboard() { FlKeyboardLabels = !FlKeyboardLabels; Invalidate(); }
    public void FlToggleLeftResize() { _flAllowLeftResize = !_flAllowLeftResize; }
    private int[] FlTargets() => (_selection.Count > 0 ? _selection : Enumerable.Range(0, _notes.Count)).Where(i => i >= 0 && i < _notes.Count).OrderBy(i => _notes[i].StartBeat).ToArray();
    private void FlEdited() { Invalidate(); CommitNotes(); Changed?.Invoke(); }

    public void FlDuplicate()
    {
        int[] ids = FlTargets();
        if (ids.Length == 0) return;
        double shift = ids.Max(i => _notes[i].StartBeat + _notes[i].LengthBeats) - _notes[ids[0]].StartBeat;
        var copies = ids.Select(i => { var n = _notes[i]; n.StartBeat += Math.Max(_grid, shift); return n; }).ToArray();
        double required = copies.Max(n => n.StartBeat + n.LengthBeats);
        bool extend = required > _lengthBeats + 1e-9;
        if (extend && CommitResized is null) return;
        _selection.Clear(); int first = _notes.Count;
        _notes.AddRange(copies);
        for (int i = first; i < _notes.Count; i++) _selection.Add(i);
        if (extend)
        {
            _lengthBeats = Math.Ceiling(required / 4) * 4;
            _previewBackup = null;
            CommitResized?.Invoke(_notes, _lengthBeats);
            SyncScroll(); Invalidate(); Changed?.Invoke();
        }
        else FlEdited();
    }

    public void FlLegato()
    {
        var ids = FlTargets(); bool changed = false;
        var starts = ids.Select(i => _notes[i].StartBeat).Distinct().OrderBy(b => b).ToArray();
        foreach (int i in ids)
        {
            var n = _notes[i];
            int position = Array.BinarySearch(starts, n.StartBeat);
            double end = position + 1 < starts.Length ? starts[position + 1] : n.StartBeat + n.LengthBeats;
            if (Math.Abs(n.LengthBeats - (end - n.StartBeat)) < 1e-9) continue;
            n.LengthBeats = end - n.StartBeat; _notes[i] = n; changed = true;
        }
        if (changed) FlEdited();
    }

    public void FlGlue()
    {
        var ids = FlTargets(); var remove = new HashSet<int>(); bool changed = false;
        foreach (var pitch in ids.GroupBy(i => _notes[i].Pitch))
        {
            int previous = -1;
            foreach (int i in pitch)
            {
                if (previous >= 0)
                {
                    var a = _notes[previous]; var b = _notes[i];
                    // Expression curves are note-relative; leave expressive pairs intact.
                    if (a.ExprId == 0 && b.ExprId == 0 && b.StartBeat <= a.StartBeat + a.LengthBeats + 1e-9)
                    {
                        a.LengthBeats = Math.Max(a.StartBeat + a.LengthBeats, b.StartBeat + b.LengthBeats) - a.StartBeat;
                        _notes[previous] = a; remove.Add(i); changed = true; continue;
                    }
                }
                previous = i;
            }
        }
        if (!changed) return;
        for (int i = _notes.Count - 1; i >= 0; i--) if (remove.Contains(i)) _notes.RemoveAt(i);
        _selection.Clear(); FlEdited();
    }

    public void FlQuickQuantize()
    {
        bool changed = false;
        foreach (int i in FlTargets())
        {
            var n = _notes[i]; double start = Math.Clamp(BeatGrid.Nearest(n.StartBeat, _grid), 0, Math.Max(0, _lengthBeats - n.LengthBeats));
            if (Math.Abs(start - n.StartBeat) < 1e-9) continue;
            n.StartBeat = start; _notes[i] = n; changed = true;
        }
        if (changed) FlEdited();
    }

    public void FlZoomToSelection()
    {
        var ids = FlTargets();
        double start = ids.Length > 0 ? ids.Min(i => _notes[i].StartBeat) : 0;
        double end = ids.Length > 0 ? ids.Max(i => _notes[i].StartBeat + _notes[i].LengthBeats) : _lengthBeats;
        FlZoomRange(start, end);
    }
    public void FlZoomFit() => FlZoomRange(0, _lengthBeats);
    private void FlZoomKeys(double height, double pitch, double screenY)
    {
        _flRowH = Math.Clamp(height, 6, 32);
        _keys.Height = _gridControl.Height = RangeH;
        _pendingVOffset = Math.Max(0, (MaxPitch - pitch) * RowH - screenY);
        _pendingCenter = false; Invalidate();
    }
    private void FlZoomRange(double start, double end)
    {
        _pixelsPerBeat = Math.Clamp(_gridControl.Bounds.Width / Math.Max(_grid, end - start), MinPpb, MaxPpb);
        _scrollBeats = Math.Clamp(start, 0, MaxScrollBeats); SyncScroll(); Invalidate(); SaveView();
    }

    private NotaNote FlNoteSegment(NotaNote note, double offset, double length)
    {
        var segment = note; segment.StartBeat += offset; segment.LengthBeats = length;
        if (note.ExprId == 0 || CreateExpression is null) return segment;
        var points = new List<NotaExprPoint>();
        foreach (var group in ExprOf(note.ExprId).GroupBy(p => p.Dim))
        {
            var curve = group.OrderBy(p => p.Beat).Select(p => (p.Beat, p.Value)).ToList();
            points.Add(new NotaExprPoint(group.Key, 0, ValueAt(curve, (float)offset, 0)));
            foreach (var pt in group.Where(p => p.Beat > offset && p.Beat < offset + length))
                points.Add(new NotaExprPoint(pt.Dim, pt.Beat - (float)offset, pt.Value));
            points.Add(new NotaExprPoint(group.Key, (float)length, ValueAt(curve, (float)(offset + length), 0)));
        }
        segment.ExprId = points.Count > 0 ? CreateExpression(points.ToArray()) : 0;
        return segment;
    }

    private bool FlSliceLine(Point a, Point b)
    {
        if (Math.Abs(b.Y - a.Y) < 1) return false;
        bool changed = false; int count = _notes.Count;
        for (int i = 0; i < count; i++)
        {
            var n = _notes[i]; double y = (MaxPitch - n.Pitch + 0.5) * RowH;
            double t = (y - a.Y) / (b.Y - a.Y);
            if (t < 0 || t > 1) continue;
            double cut = XToBeat(a.X + (b.X - a.X) * t);
            if (FlSnapEnabled && !_gridControl._flFreeSnap) cut = BeatGrid.Nearest(cut, _grid);
            double offset = cut - n.StartBeat;
            if (offset < 1.0 / 128 || n.LengthBeats - offset < 1.0 / 128) continue;
            if (n.ExprId > 0 && CreateExpression is null) continue;
            _notes[i] = FlNoteSegment(n, 0, offset);
            _notes.Add(FlNoteSegment(n, offset, n.LengthBeats - offset)); changed = true;
        }
        if (changed) { _selection.Clear(); FlEdited(); }
        return changed;
    }

    private sealed partial class NoteGrid
    {
        private int _flPreviewPitch = -1;
        private Point _flGestureA, _flGestureB;
        public bool FlKeyDown(KeyEventArgs e)
        {
            bool ctrl = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;
            bool shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
            bool alt = (e.KeyModifiers & KeyModifiers.Alt) != 0;
            if (_drag != Mode.None) return e.Key != Key.Space && e.Key != Key.Return;
            if (ctrl && !alt && e.Key == Key.B) { _o.FlDuplicate(); return true; }
            if (ctrl && !alt && e.Key == Key.D) { _o.FlDeselect(); return true; }
            if (ctrl && !alt && e.Key == Key.A) { _o.FlSelectAll(); return true; }
            if (ctrl && !alt && e.Key == Key.G) { _o.FlGlue(); return true; }
            if (ctrl && !alt && e.Key == Key.L) { _o.FlLegato(); return true; }
            if (ctrl && e.Key == Key.Q) { _o.FlQuickQuantize(); return true; }
            if (ctrl && (e.Key == Key.Up || e.Key == Key.Down)) { _o.MoveSelection(e.Key == Key.Up ? 12 : -12, 0); return true; }
            if (!ctrl && shift && e.Key == Key.I) { _o.FlInvertSelection(); return true; }
            if (!ctrl && !alt && e.Key == Key.Back) { _o.FlToggleSnap(); return true; }
            if (!ctrl && !shift && !alt && e.Key == Key.M) { _o.FlToggleKeyboard(); return true; }
            if (ctrl && alt && e.Key == Key.Home) { _o.FlToggleLeftResize(); return true; }
            if (!ctrl && (e.Key == Key.PageUp || e.Key == Key.PageDown))
            { _o.ZoomAt(e.Key == Key.PageUp ? 1.5 : 1 / 1.5, XToBeat(_lastGrid.X)); return true; }
            if (shift && e.Key == Key.D4) { _o.FlZoomFit(); return true; }
            if (shift && e.Key == Key.D5) { _o.FlZoomToSelection(); return true; }
            if (!ctrl && !shift && !alt)
            {
                FlTool? tool = e.Key switch { Key.P => FlTool.Draw, Key.B => FlTool.Paint, Key.E => FlTool.Select, Key.D => FlTool.Delete, Key.C => FlTool.Slice, Key.Z => FlTool.Zoom, Key.Y => FlTool.Playback, _ => null };
                if (tool is not null) { _o.SetFlTool(tool.Value); return true; }
            }
            if (!ctrl && (e.Key == Key.Up || e.Key == Key.Down)) { _o.MoveSelection(e.Key == Key.Up ? 1 : -1, 0); return true; }
            if (!ctrl && alt && (e.Key == Key.Left || e.Key == Key.Right)) { _o.MoveSelection(0, (e.Key == Key.Left ? -1 : 1) / Math.Max(1, Ppb)); return true; }
            return false;
        }

        private bool FlWheel(PointerWheelEventArgs e)
        {
            if ((e.KeyModifiers & KeyModifiers.Alt) == 0) return false;
            int hit = HitTest(e.GetPosition(this), out _);
            if (hit < 0) { e.Handled = true; return true; }
            int[] ids = _o._selection.Contains(hit) ? _o._selection.ToArray() : new[] { hit };
            foreach (int i in ids)
            {
                var n = _o._notes[i];
                if ((e.KeyModifiers & KeyModifiers.Shift) != 0)
                    n.StartBeat = Math.Clamp(n.StartBeat + Math.Sign(e.Delta.Y) * _o._grid, 0, Math.Max(0, _o._lengthBeats - n.LengthBeats));
                else n.Velocity = Math.Clamp(n.Velocity + Math.Sign(e.Delta.Y) / ((e.KeyModifiers & KeyModifiers.Control) != 0 ? 127f : 32f), 0, 1);
                _o._notes[i] = n;
            }
            _o.FlEdited(); e.Handled = true; return true;
        }

        private void FlAudition(Point p)
        {
            int hit = HitTest(p, out _);
            int pitch = hit >= 0 ? _o._notes[hit].Pitch : -1;
            if (pitch == _flPreviewPitch) return;
            if (_flPreviewPitch >= 0) _o.PreviewNoteOff?.Invoke(_flPreviewPitch);
            _flPreviewPitch = pitch;
            if (pitch >= 0) _o.PreviewNoteOn?.Invoke(pitch, _o._notes[hit].Velocity);
        }

        private void FinishFlGesture()
        {
            if (_drag == Mode.Slice) _o.FlSliceLine(_flGestureA, _flGestureB);
            if (_drag == Mode.Zoom)
            {
                if (Math.Abs(_flGestureA.X - _flGestureB.X) < 3) _o.FlZoomFit();
                else _o.FlZoomRange(Math.Min(XToBeat(_flGestureA.X), XToBeat(_flGestureB.X)), Math.Max(XToBeat(_flGestureA.X), XToBeat(_flGestureB.X)));
            }
            if (_flPreviewPitch >= 0) { _o.PreviewNoteOff?.Invoke(_flPreviewPitch); _flPreviewPitch = -1; }
        }
    }
}
