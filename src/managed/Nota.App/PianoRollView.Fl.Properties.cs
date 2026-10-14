// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Nota.Application;

namespace Nota.App;

public sealed partial class PianoRollView
{
    private void ShowFlNoteProperties(int index)
    {
        if (index < 0 || index >= _notes.Count || TopLevel.GetTopLevel(this) is not Window owner) return;
        var original = _notes[index];
        var content = new StackPanel { Spacing = 10, Margin = new Thickness(16) };
        NumericUpDown Field(string name, decimal value, decimal minimum, decimal maximum, decimal increment)
        {
            content.Children.Add(new TextBlock { Text = name, Foreground = FlEditorPalette.Text });
            var field = new NumericUpDown { Minimum = minimum, Maximum = maximum, Increment = increment, Value = value, FormatString = "0.###" };
            content.Children.Add(field); return field;
        }
        var pitch = Field("Pitch (MIDI)", original.Pitch, MinPitch, MaxPitch, 1);
        var start = Field("Start (beats)", (decimal)original.StartBeat, 0, (decimal)Math.Max(0, _lengthBeats - 1.0 / 128), (decimal)_grid);
        var length = Field("Length (beats)", (decimal)original.LengthBeats, 1m / 128, (decimal)_lengthBeats, (decimal)_grid);
        var velocity = Field("Velocity", (decimal)(original.Velocity * 127), 0, 127, 1);
        var dialog = new Window
        {
            Title = "Note properties - " + NoteName(original.Pitch + 12), Width = 300,
            SizeToContent = SizeToContent.Height, CanResize = false, Background = FlEditorPalette.Panel,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = content,
        };
        var apply = new Button { Content = "Accept", HorizontalAlignment = HorizontalAlignment.Right };
        apply.Click += (_, _) =>
        {
            // A modeless dialog may outlive a recording refresh, undo, or clip replacement.
            if (index < _notes.Count && _notes[index].Equals(original))
            {
                var n = original;
                n.Pitch = (int)(pitch.Value ?? original.Pitch);
                n.StartBeat = (double)(start.Value ?? (decimal)original.StartBeat);
                n.LengthBeats = Math.Min((double)(length.Value ?? (decimal)original.LengthBeats), _lengthBeats - n.StartBeat);
                n.Velocity = (float)(velocity.Value ?? (decimal)(original.Velocity * 127)) / 127;
                if (!n.Equals(original)) { _notes[index] = n; _flNoteLength = n.LengthBeats; FlEdited(); }
            }
            dialog.Close();
        };
        content.Children.Add(apply);
        dialog.Show(owner);
    }

    private sealed partial class KeysColumn
    {
        private bool _flKeyPressed;
        private bool _flZooming;
        private Point _flZoomStart;
        private double _flZoomRow, _flZoomPitch, _flZoomY;
        private int _flSounding = -1;
        private void FlSoundKey(Point p)
        {
            int pitch = (int)Math.Clamp(MaxPitch - Math.Floor(p.Y / RowH), MinPitch, MaxPitch);
            if (pitch == _flSounding) return;
            if (_flSounding >= 0) _o.PreviewNoteOff?.Invoke(_flSounding);
            _flSounding = pitch;
            _o.PreviewNoteOn?.Invoke(pitch, 0.8f);
        }
        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            if (!_o.FlWorkflow) return;
            var buttons = e.GetCurrentPoint(this).Properties;
            if (buttons.IsMiddleButtonPressed)
            {
                _flZooming = true; _flZoomStart = e.GetPosition(_o); _flZoomRow = RowH;
                _flZoomPitch = MaxPitch - e.GetPosition(this).Y / RowH; _flZoomY = _flZoomStart.Y - RulerH;
                e.Pointer.Capture(this); e.Handled = true; return;
            }
            if (!buttons.IsLeftButtonPressed) return;
            _o.FocusFlGrid();
            if ((e.KeyModifiers & KeyModifiers.Control) != 0)
            {
                int pitch = (int)Math.Clamp(MaxPitch - Math.Floor(e.GetPosition(this).Y / RowH), MinPitch, MaxPitch);
                if ((e.KeyModifiers & KeyModifiers.Shift) == 0) _o._selection.Clear();
                for (int i = 0; i < _o._notes.Count; i++) if (_o._notes[i].Pitch == pitch) _o._selection.Add(i);
                _o.Invalidate(); _o.Changed?.Invoke();
            }
            else { _flKeyPressed = true; FlSoundKey(e.GetPosition(this)); e.Pointer.Capture(this); }
            e.Handled = true;
        }
        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            FlKeyOff(); e.Pointer.Capture(null);
        }
        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) { FlKeyOff(); base.OnPointerCaptureLost(e); }
        private void FlKeyOff()
        {
            if (_flSounding >= 0) _o.PreviewNoteOff?.Invoke(_flSounding);
            _flSounding = -1; _flKeyPressed = false; _flZooming = false;
        }
    }
}
