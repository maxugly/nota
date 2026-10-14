// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace Nota.App;

internal sealed class FlRollToolbar : UserControl
{
    public FlRollToolbar(PianoRollView roll, string name)
    {
        var strip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        Button Icon(string path, string tip, Action<Button> click)
        {
            var button = new Button
            {
                Width = FlEditorPalette.ToolSize, Height = FlEditorPalette.ToolSize,
                Padding = new Thickness(4), Background = FlEditorPalette.Toolbar,
                Foreground = FlEditorPalette.Text, BorderThickness = new Thickness(0),
                CornerRadius = NotaRadius.Clip,
                Content = new Avalonia.Controls.Shapes.Path { Data = StreamGeometry.Parse(path), Stroke = FlEditorPalette.Text, StrokeThickness = 1.5, Stretch = Stretch.Uniform, Width = 16, Height = 16 },
            };
            ToolTip.SetTip(button, tip); button.Click += (_, _) => click(button); return button;
        }
        MenuItem Item(string text, Action action, string? gesture = null)
        {
            var item = new MenuItem { Header = text, InputGesture = gesture is null ? null : Avalonia.Input.KeyGesture.Parse(gesture) };
            item.Click += (_, _) => action(); return item;
        }
        var menu = new ContextMenu
        {
            ItemsSource = new object[]
            {
                Item("Duplicate", roll.FlDuplicate, "Ctrl+B"), Item("Deselect", roll.FlDeselect, "Ctrl+D"),
                Item("Select all", roll.FlSelectAll, "Ctrl+A"), Item("Invert selection", roll.FlInvertSelection, "Shift+I"), new Separator(),
                Item("Quick quantize", roll.FlQuickQuantize, "Ctrl+Q"), Item("Quick legato", roll.FlLegato, "Ctrl+L"),
                Item("Glue notes", roll.FlGlue, "Ctrl+G"), new Separator(),
                Item("Zoom to selection", roll.FlZoomToSelection, "Shift+5"), Item("Zoom to fit", roll.FlZoomFit, "Shift+4"),
                Item("Allow resizing from left", roll.FlToggleLeftResize, "Ctrl+Alt+Home"),
            },
        };
        strip.Children.Add(Icon("M2,3 L14,3 M2,8 L14,8 M2,13 L14,13", "Piano roll menu", b => menu.Open(b)));
// FL-26 Snap menu, in FL's own order. "Div" is the snap/line division in beats
// (1 beat = quarter note; Bar = 4 in 4/4). Div == 0 -> (none); Div < 0 -> a
// mode that reuses the active division for now (its distinct snap behaviour
// lands in the follow-up pass).
var snapItems = new (string Label, double Div)[]
{
    ("Main", -1), ("Line", -1), ("Cell", -1), ("(none)", 0),
    ("1/6 step", 1.0 / 24), ("1/4 step", 1.0 / 16), ("1/3 step", 1.0 / 12), ("1/2 step", 1.0 / 8), ("Step", 0.25),
    ("1/6 beat", 1.0 / 6), ("1/4 beat", 0.25), ("1/3 beat", 1.0 / 3), ("1/2 beat", 0.5), ("Beat", 1.0), ("Bar", 4.0),
    ("Events", -1), ("Markers", -1),
};
var snap = new ComboBox
{
    Width = 96, Height = FlEditorPalette.ToolSize, Padding = new Thickness(6, 0),
    Background = FlEditorPalette.Toolbar, Foreground = FlEditorPalette.Text,
    BorderBrush = FlEditorPalette.Outline, FontFamily = FlEditorPalette.Font.FontFamily, FontSize = 11,
    ItemsSource = Array.ConvertAll(snapItems, i => i.Label), SelectedIndex = 9,
};
bool syncing = false;
snap.SelectionChanged += (_, _) =>
{
    if (syncing || snap.SelectedIndex < 0) return;
    var (_, div) = snapItems[snap.SelectedIndex];
    if (div == 0) { if (roll.FlSnapEnabled) roll.FlToggleSnap(); }
    else
    {
        if (!roll.FlSnapEnabled) roll.FlToggleSnap();
        if (div > 0) roll.Grid = div;
    }
};
snap.DropDownClosed += (_, _) => roll.FocusFlGrid();
ToolTip.SetTip(snap, "Snap to grid · Backspace toggles snap · Alt bypasses snap");
strip.Children.Add(snap);
        var buttons = new Dictionary<PianoRollView.FlTool, Button>();
        var tools = new[]
        {
            (PianoRollView.FlTool.Draw, "Draw (P)", "M2,12 L1,16 L5,15 L15,5 L12,2 Z M10,4 L13,7"),
            (PianoRollView.FlTool.Paint, "Paint (B)", "M9,1 L15,7 L9,13 L3,7 Z M3,10 L1,16 L7,14"),
            (PianoRollView.FlTool.Delete, "Delete (D)", "M2,2 L14,14 M14,2 L2,14"),
            (PianoRollView.FlTool.Slice, "Slice (C)", "M2,1 L10,7 L5,14 L1,10 Z M10,7 L15,15"),
            (PianoRollView.FlTool.Select, "Select (E)", "M1,5 L1,1 L5,1 M10,1 L15,1 L15,5 M15,10 L15,15 L10,15 M5,15 L1,15 L1,10"),
            (PianoRollView.FlTool.Zoom, "Zoom (Z)", "M11,9 A5,5 0 1 0 9,11 M10,10 L16,16"),
            (PianoRollView.FlTool.Playback, "Playback (Y)", "M2,6 L6,6 L11,2 L11,14 L6,10 L2,10 Z M14,4 Q18,8 14,12"),
        };
        foreach (var (tool, tip, path) in tools)
        {
            var button = Icon(path, tip, _ => { roll.SetFlTool(tool); roll.FocusFlGrid(); });
            button.IsEnabled = tool != PianoRollView.FlTool.Playback || roll.PreviewNoteOn is not null;
            if (!button.IsEnabled) ToolTip.SetTip(button, "Playback preview needs an instrument track");
            buttons[tool] = button; strip.Children.Add(button);
        }
        var title = new TextBlock
        {
            Text = "Piano roll - " + name, Foreground = FlEditorPalette.Text, FontFamily = FlEditorPalette.Font.FontFamily,
            FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        strip.Children.Add(title);
        void Sync()
        {
            foreach (var pair in buttons)
            {
                bool selected = roll.ActiveFlTool == pair.Key;
                pair.Value.Background = selected ? FlEditorPalette.Accent : FlEditorPalette.Toolbar;
                if (pair.Value.Content is Avalonia.Controls.Shapes.Path icon) icon.Stroke = selected ? FlEditorPalette.Outline : FlEditorPalette.Text;
            }
            syncing = true;
            if (!roll.FlSnapEnabled) { snap.SelectedIndex = 3; }          // (none)
            else
            {
                int idx = Array.FindIndex(snapItems, i => i.Div > 0 && Math.Abs(i.Div - roll.Grid) < 1e-9);
                snap.SelectedIndex = idx >= 0 ? idx : 9;                 // fall back to 1/4 beat
            }
            syncing = false;
        }
        roll.Changed += Sync; Sync();
        Content = new StackPanel
        {
            Children =
            {
                new Border { Height = FlEditorPalette.ToolbarHeight, Background = FlEditorPalette.Panel, BorderBrush = FlEditorPalette.Outline,
                    BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(3, 2), Child = strip },
                roll.CreateFlOverview(),
            },
        };
    }
}
