// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
using System;
using Avalonia;
using Avalonia.Media;

namespace Nota.App;

public sealed partial class PianoRollView
{
    private void FlGridLines(DrawingContext ctx, double width, double height)
    {
        EnsurePpb(width);
        double end = Math.Min(_lengthBeats, ScrollBeats + width / Math.Max(1e-6, PixelsPerBeat));
        // FL draws the vertical lines at the time division. Use the active snap
        // division directly; when it is too fine at this zoom, thin by whole
        // factors of two (still a multiple of the division, so bars stay aligned)
        // instead of falling back to an unrelated ladder.
        double div = Math.Max(1.0 / 96, _grid);
        double px = div * Math.Max(1e-6, PixelsPerBeat);
        double mult = 1.0;
        while (px * mult < 4.0 && mult < 256.0) mult *= 2.0;
        double step = div * mult;
        double start = Math.Max(0, Math.Floor(ScrollBeats / step) * step);
        for (double b = start; b <= end + 1e-9; b += step)
        {
            double x = BeatToX(b) + 0.5;
            var pen = Math.Abs(b / 4 - Math.Round(b / 4)) < 1e-6 ? FlEditorPalette.Bar
                : Math.Abs(b - Math.Round(b)) < 1e-6 ? FlEditorPalette.GridBeat : FlEditorPalette.GridSub;
            ctx.DrawLine(pen, new Point(x, 0), new Point(x, height));
        }
    }
    private static FormattedText FlText(string text, IBrush ink, double size = 10)
        => new(text, NotaNum.Culture, FlowDirection.LeftToRight, FlEditorPalette.Font, size, ink);

    private sealed partial class NoteGrid
    {
        private void RenderFl(DrawingContext ctx)
        {
            double w = Bounds.Width, h = Bounds.Height;
            if (w <= 0) return;
            _o.EnsurePpb(w);
            ctx.FillRectangle(FlEditorPalette.Grid, new Rect(Bounds.Size));
            for (int pitch = MinPitch; pitch <= MaxPitch; pitch++)
            {
                double y = PitchToY(pitch);
                if (IsBlack(pitch)) ctx.FillRectangle(FlEditorPalette.BlackRow, new Rect(0, y, w, RowH));
                if (_o._scaleOn && !_o.InScale(pitch)) ctx.FillRectangle(FlEditorPalette.SelectionWash, new Rect(0, y, w, RowH));
                ctx.DrawLine(pitch % 12 == 0 ? FlEditorPalette.Beat : FlEditorPalette.Row, new Point(0, y + RowH - 0.5), new Point(w, y + RowH - 0.5));
            }
            for (double bar = Math.Floor(_o.ScrollBeats / 4) * 4; bar < _o.ScrollBeats + w / Math.Max(1e-6, Ppb); bar += 4)
                if ((int)(bar / 4) % 2 != 0)
                    ctx.FillRectangle(FlEditorPalette.AlternateBar, new Rect(BeatToX(bar), 0, 4 * Ppb, h));
            _o.FlGridLines(ctx, w, h);
            for (int i = 0; i < _o._notes.Count; i++)
            {
                var n = _o._notes[i];
                if (n.Pitch < MinPitch || n.Pitch > MaxPitch) continue;
                double x = BeatToX(n.StartBeat), y = PitchToY(n.Pitch);
                if (x > w || x + n.LengthBeats * Ppb < 0 || y + RowH < _o._bodyScroll.Offset.Y || y > _o._bodyScroll.Offset.Y + _o._bodyScroll.Viewport.Height) continue;
                var rect = new Rect(x + 0.5, y + 0.5, Math.Max(2, n.LengthBeats * Ppb - 1), RowH - 1);
                bool selected = _o._selection.Contains(i);
                var fill = selected ? FlEditorPalette.Selection : FlEditorPalette.Note;
                ctx.DrawRectangle(fill, new Pen(selected ? FlEditorPalette.SelectionEdge : FlEditorPalette.NoteEdge, 1), rect);
                ctx.DrawLine(FlEditorPalette.NoteHighlight, new Point(rect.X + 1, rect.Y + 1), new Point(rect.Right - 1, rect.Y + 1));
                if (rect.Width > 32 && RowH >= 10)
                    ctx.DrawText(FlText(NoteName(n.Pitch + 12), FlEditorPalette.KeyText, 9), new Point(rect.X + 3, rect.Y + Math.Max(0, (RowH - 11) / 2)));
                if (n.ExprId > 0 || _o._expr.Editing(i)) DrawExpression(ctx, i, n, rect, selected, Ppb);
            }
            if (_o._playheadBeat >= 0)
            {
                double x = BeatToX(_o._playheadBeat);
                ctx.DrawLine(new Pen(FlEditorPalette.Accent, 1), new Point(x, 0), new Point(x, h));
            }
            if (_drag is Mode.Marquee or Mode.Zoom)
            {
                var rect = _drag == Mode.Zoom ? RectOf(_flGestureA, _flGestureB) : RectOf(_marqueeA, _marqueeB);
                ctx.DrawRectangle(FlEditorPalette.SelectionWash, new Pen(FlEditorPalette.Selection, 1), rect);
            }
            if (_drag == Mode.Slice) ctx.DrawLine(new Pen(FlEditorPalette.Accent, 1), _flGestureA, _flGestureB);
        }
    }

    private sealed partial class RulerLane
    {
        private void RenderFl(DrawingContext ctx)
        {
            double w = Bounds.Width, h = Bounds.Height;
            ctx.FillRectangle(FlEditorPalette.Panel, new Rect(Bounds.Size));
            _o.EnsurePpb(w);
            _o.FlGridLines(ctx, w, h);
            double end = _o.ScrollBeats + w / Math.Max(1e-6, _o.PixelsPerBeat);
            for (double b = Math.Floor(_o.ScrollBeats / 4) * 4; b <= end; b += 4)
                ctx.DrawText(FlText(((int)(b / 4) + 1).ToString(NotaNum.Culture), FlEditorPalette.Text), new Point(_o.BeatToX(b) + 4, 4));
            if (_o._playheadBeat >= 0)
            {
                double x = _o.BeatToX(_o._playheadBeat);
                var triangle = new StreamGeometry();
                using (var g = triangle.Open()) { g.BeginFigure(new Point(x - 4, 0), true); g.LineTo(new Point(x + 4, 0)); g.LineTo(new Point(x, 6)); g.EndFigure(true); }
                ctx.DrawGeometry(FlEditorPalette.Accent, null, triangle);
            }
        }
    }

    private sealed partial class KeysColumn
    {
        private void RenderFl(DrawingContext ctx)
        {
            double w = Bounds.Width;
            ctx.FillRectangle(FlEditorPalette.WhiteKey, new Rect(Bounds.Size));
            for (int pitch = MinPitch; pitch <= MaxPitch; pitch++)
            {
                double y = (MaxPitch - pitch) * RowH;
                bool black = IsBlack(pitch), active = _o._held.Contains(pitch) || _hover == pitch;
                if (black) ctx.DrawRectangle(active ? FlEditorPalette.Accent : FlEditorPalette.BlackKey, new Pen(FlEditorPalette.Outline, 1), new Rect(0, y, w * 0.65, RowH));
                else
                {
                    if (active) ctx.FillRectangle(FlEditorPalette.Accent, new Rect(0, y, w, RowH));
                    ctx.DrawLine(FlEditorPalette.Beat, new Point(0, y + RowH - 0.5), new Point(w, y + RowH - 0.5));
                }
                if (RowH >= 9 && (pitch % 12 == 0 || _o.FlKeyboardLabels))
                {
                    var label = FlText(NoteName(pitch + 12), black ? FlEditorPalette.Text : FlEditorPalette.KeyText, 9);
                    ctx.DrawText(label, new Point(black ? 3 : w - label.Width - 3, y + Math.Max(0, (RowH - label.Height) / 2)));
                }
            }
        }
    }

    private sealed partial class VelocityLane
    {
        private void RenderFl(DrawingContext ctx)
        {
            double w = Bounds.Width, h = Bounds.Height;
            ctx.FillRectangle(FlEditorPalette.Grid, new Rect(Bounds.Size));
            _o.FlGridLines(ctx, w, h);
            for (int i = 0; i < _o._notes.Count; i++)
            {
                var n = _o._notes[i]; double x = _o.BeatToX(n.StartBeat) + 3;
                double top = h - Math.Clamp(n.Velocity, 0, 1) * (h - 8);
                var brush = _o._selection.Contains(i) ? FlEditorPalette.Selection : FlEditorPalette.Note;
                ctx.DrawLine(new Pen(brush, 1), new Point(x, h), new Point(x, top));
                ctx.DrawEllipse(brush, null, new Point(x, top), 2.5, 2.5);
            }
        }
    }
}
