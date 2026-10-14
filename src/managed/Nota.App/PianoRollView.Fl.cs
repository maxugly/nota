// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Nota.Application;

namespace Nota.App;

public sealed partial class PianoRollView
{
    public enum FlTool { Draw, Paint, Select, Delete, Slice, Zoom, Playback }
    public bool FlWorkflow { get; private set; }
    public FlTool ActiveFlTool { get; private set; }
    private double _flNoteLength = 1;

    // Shares notes, selection, expression, zoom and history with the original editor.
    public void SetFlWorkflow(bool enabled)
    {
        _gridControl.FinishDrag();
        double oldRow = RowH;
        int oldMax = MaxPitch;
        double oldOffset = _bodyScroll.Offset.Y;
        FlWorkflow = enabled;
        _keys.Height = _gridControl.Height = RangeH;
        if ((Math.Abs(oldRow - RowH) > 1e-9 || oldMax != MaxPitch) && _bodyScroll.Viewport.Height > 0)
        { _pendingVOffset = oldOffset / oldRow * RowH + (MaxPitch - oldMax) * RowH; _pendingCenter = false; }
        _flLaneSplitter.IsVisible = enabled;
        _laneRow.Height = enabled ? double.NaN : (_laneMode == LaneMode.Velocity ? VelH : ExprH);
        _laneLayout.RowDefinitions[3].Height = enabled ? new GridLength(_laneMode == LaneMode.Velocity ? VelH : ExprH) : GridLength.Auto;
        _laneLayout.RowDefinitions[3].MinHeight = enabled ? 48 : 0;
        _rulerCorner.Background = enabled ? FlEditorPalette.Panel : KeysBg;
        _velocityCorner.Background = enabled ? FlEditorPalette.Panel : KeysBg;
        _velHost.Background = enabled ? FlEditorPalette.Grid : VelBg;
        if (enabled) FocusFlGrid();
        _laneTabs.InvalidateVisual();
        Invalidate();
    }
    public void SetFlTool(FlTool tool)
    {
        ActiveFlTool = tool;
        Changed?.Invoke();
    }

    private FlOverview? _flOverview;
    internal Control CreateFlOverview() => _flOverview = new FlOverview(this);

    private sealed class FlOverview : Control
    {
        private readonly PianoRollView _roll;
        public FlOverview(PianoRollView roll)
        {
            _roll = roll;
            Height = FlEditorPalette.OverviewHeight;
            ClipToBounds = true;
            roll.Changed += InvalidateVisual;
        }

        public override void Render(DrawingContext context)
        {
            double width = Bounds.Width, height = Bounds.Height;
            if (width <= 0 || height <= 0) return;
            context.FillRectangle(FlEditorPalette.Toolbar, new Rect(Bounds.Size));
            var notes = _roll._notes;
            int min = notes.Count > 0 ? notes.Min(n => n.Pitch) - 1 : 48;
            int max = notes.Count > 0 ? notes.Max(n => n.Pitch) + 1 : 72;
            double scale = width / Math.Max(_roll._lengthBeats, 1e-6);
            foreach (var n in notes)
            {
                double y = (max - n.Pitch) / (double)(max - min + 1) * height;
                context.FillRectangle(FlEditorPalette.Note, new Rect(n.StartBeat * scale, y,
                    Math.Max(1, n.LengthBeats * scale), Math.Max(1, height / (max - min + 1))));
            }
            double x = _roll.ScrollBeats * scale;
            double viewport = _roll._gridControl.Bounds.Width / Math.Max(1e-6, _roll.PixelsPerBeat) * scale;
            context.DrawRectangle(null, new Pen(FlEditorPalette.MutedText, 1),
                new Rect(x, 0.5, Math.Max(0, Math.Min(width - x, viewport) - 1), height - 1));
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            _roll.SetScrollBeats(e.GetPosition(this).X / Math.Max(1, Bounds.Width) * _roll._lengthBeats
                - _roll._gridControl.Bounds.Width / Math.Max(1e-6, _roll.PixelsPerBeat) / 2);
            InvalidateVisual();
            e.Handled = true;
        }
    }

    private sealed partial class NoteGrid
    {
        private Point _flLastStroke;
        private Point _flPanPoint;
        private double _flPanBeat, _flPanY;
        private KeyModifiers _flDragMods;
        private bool _flCloneGesture;
        public bool _flFreeSnap;

        private void FlPointerPressed(PointerPressedEventArgs e)
        {
            _o.EnsurePpb(Bounds.Width);
            if (_drag != Mode.None) { e.Handled = true; return; }
            var props = e.GetCurrentPoint(this).Properties;
            if (props.IsMiddleButtonPressed || (props.IsRightButtonPressed && (e.KeyModifiers & KeyModifiers.Shift) != 0))
            {
                _dragMoved = false;
                _drag = Mode.Pan;
                _flPanPoint = e.GetPosition(_o);
                _flPanBeat = _o.ScrollBeats; _flPanY = _o._bodyScroll.Offset.Y;
                e.Pointer.Capture(this); e.Handled = true; return;
            }
            if (!props.IsLeftButtonPressed && !props.IsRightButtonPressed) return;
            _flFreeSnap = (e.KeyModifiers & KeyModifiers.Alt) != 0;
            var p = e.GetPosition(this);
            bool select = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;
            bool shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
            _flCloneGesture = shift; _flDragMods = e.KeyModifiers;
            int hit = HitTest(p, out int edge);
            _preDrag.Clear(); _preDrag.AddRange(_o._notes);
            _orig.Clear(); _liveStarted = false; _dragMoved = false; _primary = -1;
            _lastGrid = _flLastStroke = p;
            _flGestureA = _flGestureB = p;
            if (props.IsRightButtonPressed && (e.KeyModifiers & KeyModifiers.Alt) != 0)
            {
                _drag = Mode.Audition; e.Pointer.Capture(this); FlAudition(p); e.Handled = true; return;
            }
            if ((!select && _o.ActiveFlTool is FlTool.Slice or FlTool.Zoom or FlTool.Playback) || (select && props.IsRightButtonPressed))
            {
                _drag = select ? Mode.Zoom : _o.ActiveFlTool switch { FlTool.Slice => Mode.Slice, FlTool.Zoom => Mode.Zoom, _ => Mode.Audition };
                e.Pointer.Capture(this);
                if (_drag == Mode.Audition) FlAudition(p);
                e.Handled = true; return;
            }

            if (props.IsRightButtonPressed || (!select && _o.ActiveFlTool == FlTool.Delete))
            {
                _drag = Mode.Erase;
                e.Pointer.Capture(this);
                FlStroke(p);
            }
            else if (select || _o.ActiveFlTool == FlTool.Select)
            {
                if (select && shift && hit >= 0)
                {
                    if (!_o._selection.Add(hit)) _o._selection.Remove(hit);
                    _o.Invalidate(); _o.Changed?.Invoke(); e.Handled = true; return;
                }
                if (!select && hit >= 0)
                {
                    if (shift)
                    {
                        if (!_o._selection.Add(hit)) _o._selection.Remove(hit);
                    }
                    else { FlBeginNoteDrag(e, p, hit, edge); }
                }
                else
                {
                    _selBase.Clear();
                    if (shift) _selBase.UnionWith(_o._selection);
                    _o._selection.Clear();
                    if (hit >= 0) _o._selection.Add(hit);
                    _marqueeAdd = shift;
                    _drag = Mode.Marquee; _marqueeA = _marqueeB = p;
                    e.Pointer.Capture(this);
                }
            }
            else if (_o.ActiveFlTool == FlTool.Paint)
            {
                _o._selection.Clear();
                _drag = Mode.Paint;
                e.Pointer.Capture(this);
                FlStroke(p);
            }
            else if (hit >= 0)
            {
                _o._flNoteLength = _o._notes[hit].LengthBeats;
                if (!_o._flAllowLeftResize && edge < 0) edge = 0;
                if (e.ClickCount == 2 && !shift) { _o.ShowFlNoteProperties(hit); e.Handled = true; return; }
                if (shift)
                {
                    var ids = (_o._selection.Contains(hit) ? _o._selection : new System.Collections.Generic.HashSet<int> { hit }).ToArray();
                    _o._selection.Clear();
                    foreach (int i in ids)
                    {
                        int copy = _o._notes.Count; _o._notes.Add(_o._notes[i]); _o._selection.Add(copy);
                        if (i == hit) _primary = copy;
                    }
                    hit = _primary; edge = 0; _dragMoved = true;
                }
                FlBeginNoteDrag(e, p, hit, edge);
                if (_dragMoved) PushLive();
            }
            else
            {
                int index = FlAddNote(p);
                if (index >= 0)
                {
                    FlBeginNoteDrag(e, p, index, 0);
                    _dragMoved = true;
                    PushLive();
                }
            }
            _o.Invalidate(); _o.Changed?.Invoke();
            e.Handled = true;
        }

        private void FlBeginNoteDrag(PointerPressedEventArgs e, Point p, int hit, int edge)
        {
            if (!_o._selection.Contains(hit)) { _o._selection.Clear(); _o._selection.Add(hit); }
            _primary = hit;
            foreach (int i in _o._selection)
                if (i >= 0 && i < _o._notes.Count)
                {
                    var n = _o._notes[i];
                    _orig[i] = (n.StartBeat, n.Pitch, n.LengthBeats);
                }
            _drag = edge > 0 ? Mode.Resize : edge < 0 ? Mode.ResizeLeft : Mode.Move;
            _resizeAnchor = Snap(XToBeat(p.X));
            _grabBeat = XToBeat(p.X) - _o._notes[hit].StartBeat;
            e.Pointer.Capture(this);
        }

        private int FlAddNote(Point p)
        {
            double raw = XToBeat(p.X);
            double beat = Math.Max(0, BeatGrid.CellStart(raw, _o._grid, !_flFreeSnap && _o.FlSnapEnabled));
            if (beat >= _o._lengthBeats || p.X < 0 || p.X >= Bounds.Width || p.Y < 0 || p.Y >= Bounds.Height) return -1;
            int pitch = YToPitch(p.Y);
            if (_o._notes.Any(n => n.Pitch == pitch && beat >= n.StartBeat - 1e-9 && beat < n.StartBeat + n.LengthBeats - 1e-9)) return -1;
            double length = Math.Min(Math.Max(_o._grid, _o._flNoteLength), _o._lengthBeats - beat);
            _o._notes.Add(new NotaNote(pitch, beat, length, 0.8f));
            _o._selection.Clear(); _o._selection.Add(_o._notes.Count - 1);
            return _o._notes.Count - 1;
        }

        // Interpolate a stroke so fast pointer movement cannot skip grid cells or notes.
        private void FlStroke(Point p)
        {
            double dx = p.X - _flLastStroke.X, dy = p.Y - _flLastStroke.Y;
            double stepX = Math.Max(1, _o._grid * Ppb / 2);
            int steps = Math.Max(1, (int)Math.Ceiling(Math.Max(Math.Abs(dx) / stepX, Math.Abs(dy) / (RowH / 2))));
            bool changed = false;
            for (int s = 1; s <= steps; s++)
            {
                var sample = new Point(_flLastStroke.X + dx * s / steps, _flLastStroke.Y + dy * s / steps);
                if (sample.X < 0 || sample.X >= Bounds.Width || sample.Y < 0 || sample.Y >= Bounds.Height) continue;
                if (_drag == Mode.Paint) changed |= FlAddNote(sample) >= 0;
                else
                {
                    int hit = HitTest(sample, out _);
                    if (hit >= 0) { _o._notes.RemoveAt(hit); _o._selection.Clear(); changed = true; }
                }
            }
            _flLastStroke = p;
            if (changed) { _dragMoved = true; PushLive(); }
            _o.Invalidate(); _o.Changed?.Invoke();
        }
    }
}
