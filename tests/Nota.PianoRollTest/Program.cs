// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Nota.App;
using Nota.Application;

AppBuilder.Configure<Avalonia.Application>()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
Avalonia.Application.Current!.Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());
int checks = 0;
void Check(bool value, string name)
{
    if (!value) throw new Exception(name);
    checks++; Console.WriteLine("PASS " + name);
}
Check(BeatGrid.Nearest(1.37, .25) == 1.25 && BeatGrid.CellStart(1.49, .25) == 1.25,
    "shared grid distinguishes nearest editing from draw cell placement");
Check(BeatGrid.Nearest(1.37, .25, false) == 1.37 && BeatGrid.CellStart(1.37, 0) == 1.37,
    "shared grid supports snap bypass and disabled divisions");
Check(BeatGrid.CellStart(-.1, .25) == -.25 && BeatGrid.Nearest(-.2, .25) == -.25,
    "shared grid preserves negative beat coordinates for caller bounds");
(PianoRollView roll, Window window, Point point) Open(bool fl)
{
    var roll = new PianoRollView();
    roll.SetNotes(Array.Empty<NotaNote>(), 4);
    roll.SetFlWorkflow(fl);
    var window = new Window { Width = 900, Height = 500, Content = roll };
    window.Show();
    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    roll.GetVisualDescendants().OfType<Control>().Single(c => c.GetType().Name == "NoteGrid").Focus();
    return (roll, window, new Point(150, 150));
}
void Click(Window w, Point p, MouseButton button = MouseButton.Left)
{
    w.MouseDown(p, button); w.MouseUp(p, button);
}

{
    var (r, w, p) = Open(false);
    Click(w, p);
    Check(r.SourceNotes.Count == 0, "original editor keeps empty-space single-click selection");
    w.Close();
}
{
    var (r, w, p) = Open(true);
    int commits = 0;
    r.Commit = _ => commits++;
    Click(w, p);
    Check(r.SourceNotes.Count == 1 && commits == 1, "draw adds a note with one committed gesture");
    double start = r.SourceNotes[0].StartBeat;
    w.MouseDown(p + new Vector(10, 0), MouseButton.Left);
    w.MouseMove(p + new Vector(110, 0));
    w.MouseUp(p + new Vector(110, 0), MouseButton.Left);
    Check(r.SourceNotes.Count == 1 && r.SourceNotes[0].StartBeat > start, "draw moves an existing note");
    Click(w, p + new Vector(110, 0), MouseButton.Right);
    Check(r.SourceNotes.Count == 0, "right-click erases without opening a menu");
    w.Close();
}
{
    var (r, w, p) = Open(true);
    int commits = 0;
    r.Commit = _ => commits++;
    r.SetFlTool(PianoRollView.FlTool.Paint);
    w.MouseDown(p, MouseButton.Left);
    w.MouseMove(new Point(800, p.Y));
    w.MouseMove(p);
    w.MouseUp(p, MouseButton.Left);
    Check(r.SourceNotes.Count >= 3, "fast paint stroke fills intermediate note positions");
    Check(commits == 1, "paint stroke is one commit on the session fallback path");
    var notes = r.SourceNotes.OrderBy(n => n.StartBeat).ToArray();
    Check(notes.Zip(notes.Skip(1)).All(pair => pair.First.StartBeat + pair.First.LengthBeats <= pair.Second.StartBeat + 1e-9),
        "painting back over notes does not stack duplicates or overlaps");
    Check(notes.All(n => n.StartBeat >= 0 && n.StartBeat + n.LengthBeats <= 4), "paint stays inside clip bounds");
    w.MouseDown(p, MouseButton.Right);
    w.MouseMove(new Point(850, p.Y));
    w.MouseUp(new Point(850, p.Y), MouseButton.Right);
    Check(r.SourceNotes.Count == 0 && commits == 2, "fast right-drag erases crossed notes in one commit");
    w.Close();
}
{
    var (r, w, p) = Open(true);
    var checkpoints = new List<NotaNote[]>();
    NotaNote[] live = [];
    r.Commit = notes => checkpoints.Add(notes.ToArray());
    r.CommitLive = notes => live = notes.ToArray();
    r.SetFlTool(PianoRollView.FlTool.Paint);
    w.MouseDown(p, MouseButton.Left);
    w.MouseMove(new Point(800, p.Y));
    w.MouseUp(new Point(800, p.Y), MouseButton.Left);
    Check(checkpoints.Count == 1 && checkpoints[0].Length == 0 && live.Length >= 3,
        "live paint seeds exactly one pre-stroke undo snapshot");
    int count = r.SourceNotes.Count;
    w.MouseDown(new Point(60, 130), MouseButton.Left, RawInputModifiers.Control);
    w.MouseMove(new Point(850, 175), RawInputModifiers.Control);
    w.MouseUp(new Point(850, 175), MouseButton.Left, RawInputModifiers.Control);
    Check(r.SourceNotes.Count == count && r.SelectionIndices.Count == count,
        "Ctrl-drag selects notes while leaving the score unchanged");
    r.SetFlWorkflow(false);
    Check(r.SourceNotes.Count == count, "switching back to the original editor preserves notes");
    w.Close();
}
{
    var (r, w, p) = Open(true);
    Click(w, p);
    var grid = r.GetVisualDescendants().OfType<Control>().Single(c => c.GetType().Name == "NoteGrid");
    var origin = grid.TranslatePoint(new Point(), w)!.Value;
    double ppb = grid.Bounds.Width / 4;
    var n = r.SourceNotes[0];
    var edge = new Point(origin.X + (n.StartBeat + n.LengthBeats) * ppb - 1, p.Y);
    w.MouseDown(edge, MouseButton.Left);
    w.MouseMove(edge + new Vector(ppb / 2, 0));
    w.MouseUp(edge + new Vector(ppb / 2, 0), MouseButton.Left);
    double length = r.SourceNotes[0].LengthBeats;
    Check(length > 1, "right edge resizes a note");
    Click(w, new Point(origin.X + 2.5 * ppb, p.Y + 24));
    Check(r.SourceNotes.Count == 2 && Math.Abs(r.SourceNotes[1].LengthBeats - length) < 1e-9,
        "new notes remember the last resized note length");
    w.Close();
}
{
    var (r, w, p) = Open(true);
    Click(w, p);
    w.KeyPress(Key.B, RawInputModifiers.None, default, null);
    Check(r.ActiveFlTool == PianoRollView.FlTool.Paint, "B switches to the paint tool");
    w.KeyPress(Key.P, RawInputModifiers.None, default, null);
    Check(r.ActiveFlTool == PianoRollView.FlTool.Draw, "P switches to the draw tool");
    int count = r.SourceNotes.Count;
    var scroll = r.GetVisualDescendants().OfType<ScrollViewer>().First();
    double before = scroll.Offset.Y;
    w.MouseDown(p, MouseButton.Middle);
    w.MouseMove(p + new Vector(0, -50));
    w.MouseUp(p + new Vector(0, -50), MouseButton.Middle);
    Check(scroll.Offset.Y > before && r.SourceNotes.Count == count, "middle drag pans without editing notes");
    w.MouseDown(new Point(700, 200), MouseButton.Left, RawInputModifiers.Alt);
    w.MouseUp(new Point(700, 200), MouseButton.Left, RawInputModifiers.Alt);
    Check(r.SourceNotes.Count == count + 1 && Math.Abs(r.SourceNotes[^1].StartBeat / r.Grid - Math.Round(r.SourceNotes[^1].StartBeat / r.Grid)) > 0.01,
        "Alt drawing bypasses grid snapping");
    w.Close();
}
{
    var r = new PianoRollView();
    r.SetNotes(new[] { new NotaNote(60, 0, 1, 0.8f) }, 4);
    var editor = new ClipEditorView(r, "Test clip", 0);
    var w = new Window { Width = 1280, Height = 650, Content = editor };
    w.Show(); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    void Tab(string name) => editor.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>()
        .Single(b => b.Content?.ToString() == name)
        .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
    Tab("FL roll");
    Check(r.FlWorkflow && !editor.GetVisualDescendants().OfType<ClipPropsView>().Single().IsVisible,
        "FL tab enables the alternate workflow and expands the grid");
    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    var snap = editor.GetVisualDescendants().OfType<ComboBox>().Single();
    // Look snap entries up by label rather than index — the FL ladder
    // (Main/Line/Cell/(none)/…) reorders these freely.
    int SnapAt(string label) => ((IEnumerable<string>)snap.ItemsSource!).ToList().IndexOf(label);
    snap.SelectedIndex = SnapAt("Beat");
    Check(r.FlSnapEnabled && r.Grid == 1, "choosing Beat snaps at one beat");
    snap.SelectedIndex = SnapAt("(none)");
    Check(!r.FlSnapEnabled && r.Grid == 1, "snap off preserves the previously chosen grid division");
    r.FlToggleSnap();
    Check(r.FlSnapEnabled && snap.SelectedIndex == SnapAt("Beat"), "snap toggle restores the previous division in the toolbar");
    Tab("Notes");
    Check(!r.FlWorkflow && r.SourceNotes.Count == 1 && editor.GetVisualDescendants().OfType<ClipPropsView>().Single().IsVisible,
        "Notes tab restores the original editor and its inspector without changing data");
    w.Close();
}
{
    var (r, w, p) = Open(true);
    r.SetNotes(new[] { new NotaNote(60, 0, 0.5, 0.8f), new NotaNote(64, 1, 0.5, 0.8f) }, 4);
    void Press(Key key, RawInputModifiers mods) => w.KeyPress(key, mods, default, null);
    Press(Key.A, RawInputModifiers.Control);
    Press(Key.B, RawInputModifiers.Control);
    Check(r.SourceNotes.Count == 4 && r.SelectionIndices.Count == 2 && r.SourceNotes[2].StartBeat == 1.5,
        "FL Ctrl+B duplicates to the right and selects the copies");
    Press(Key.D, RawInputModifiers.Control);
    Check(r.SelectionIndices.Count == 0 && r.SourceNotes.Count == 4, "FL Ctrl+D deselects instead of duplicating");
    Press(Key.A, RawInputModifiers.Control);
    Press(Key.Up, RawInputModifiers.Control);
    Check(r.SourceNotes[0].Pitch == 72, "FL Ctrl+Up transposes selection one octave");
    Press(Key.Down, RawInputModifiers.None);
    Check(r.SourceNotes[0].Pitch == 71, "FL Down transposes selection one semitone");
    Press(Key.Back, RawInputModifiers.None);
    Check(!r.FlSnapEnabled && r.SourceNotes.Count == 4, "FL Backspace toggles snap instead of deleting notes");
    r.FlDeselect(); Press(Key.I, RawInputModifiers.Shift);
    Check(r.SelectionIndices.Count == 4, "FL Shift+I inverts selection");
    w.Close();
}
{
    var (r, w, p) = Open(true);
    r.SetNotes(new[] { new NotaNote(60, 0, 0.25, 0.8f), new NotaNote(62, 1, 0.25, 0.8f) }, 4);
    r.FlLegato();
    Check(r.SourceNotes[0].LengthBeats == 1 && r.SourceNotes[1].LengthBeats == 0.25,
        "quick legato joins successive onsets and preserves terminal duration");
    r.SetNotes(new[] { new NotaNote(60, 0, 0.5, 0.8f), new NotaNote(60, 0.5, 0.5, 0.8f) }, 4);
    r.FlGlue();
    Check(r.SourceNotes.Count == 1 && r.SourceNotes[0].LengthBeats == 1, "glue joins touching notes of the same pitch");
    double resized = 0; int resizeCommits = 0;
    r.SetNotes(new[] { new NotaNote(60, 0, 4, 0.8f) }, 4);
    r.CommitResized = (_, length) => { resized = length; resizeCommits++; };
    r.FlDuplicate();
    Check(resized == 8 && r.LengthBeats == 8 && resizeCommits == 1 && r.SourceNotes.Count == 2,
        "duplication extends a full clip through one combined resize callback");
    w.Close();
}
{
    var (r, w, p) = Open(true);
    r.SetNotes(new[] { new NotaNote(60, 0, 1, 0.8f) }, 4);
    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    var grid = r.GetVisualDescendants().OfType<Control>().Single(c => c.GetType().Name == "NoteGrid");
    var origin = grid.TranslatePoint(new Point(), w)!.Value;
    double y = origin.Y + (127 - 60 + 0.5) * 12;
    double x = origin.X + grid.Bounds.Width / 8;
    int commits = 0; r.Commit = _ => commits++;
    r.SetFlTool(PianoRollView.FlTool.Slice);
    w.MouseDown(new Point(x, y - 8), MouseButton.Left);
    w.MouseMove(new Point(x, y + 8));
    w.MouseUp(new Point(x, y + 8), MouseButton.Left);
    Check(r.SourceNotes.Count == 2 && r.SourceNotes.All(n => Math.Abs(n.LengthBeats - 0.5) < 1e-9) && commits == 1,
        "slice splits at the cut line with one undoable commit");
    w.Close();
}
{
    var (r, w, p) = Open(true);
    Click(w, p);
    float velocity = r.SourceNotes[0].Velocity;
    w.MouseWheel(p, new Vector(0, 1), RawInputModifiers.Alt);
    Check(r.SourceNotes[0].Velocity > velocity, "FL Alt+wheel changes hovered note velocity");
    var sounding = new HashSet<int>(); int auditions = 0;
    r.PreviewNoteOn = (pitch, _) => { sounding.Add(pitch); auditions++; };
    r.PreviewNoteOff = pitch => sounding.Remove(pitch);
    w.MouseDown(new Point(12, 150), MouseButton.Left);
    w.MouseMove(new Point(12, 174));
    w.MouseUp(new Point(12, 174), MouseButton.Left);
    Check(auditions == 2 && sounding.Count == 0, "keyboard audition releases old pitches and ends without stuck notes");
    w.Close();
}
{
    var (r, w, p) = Open(true);
    Click(w, p);
    int commits = 0; r.Commit = _ => commits++;
    w.MouseDown(p + new Vector(10, 0), MouseButton.Left, RawInputModifiers.Shift);
    w.MouseMove(p + new Vector(110, 24), RawInputModifiers.Shift);
    w.MouseUp(p + new Vector(110, 24), MouseButton.Left, RawInputModifiers.Shift);
    Check(r.SourceNotes.Count == 2 && commits == 1 && r.SourceNotes[0].StartBeat < r.SourceNotes[1].StartBeat,
        "Shift-drag copies the note in one undoable gesture");
    w.Close();
}
{
    var (r, w, p) = Open(true);
    Click(w, p); Click(w, p);
    var dialog = w.OwnedWindows.Single();
    Check(dialog.Title?.StartsWith("Note properties") == true, "double-click opens note properties");
    var fields = dialog.GetVisualDescendants().OfType<NumericUpDown>().ToArray();
    fields[^1].Value = 64;
    dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Content?.ToString() == "Accept")
        .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
    Check(Math.Abs(r.SourceNotes[0].Velocity - 64f / 127) < 1e-6 && !w.OwnedWindows.Any(),
        "accepting note properties updates the original note and closes the dialog");
    w.Close();
}
{
    var (r, w, p) = Open(true);
    var curves = new Dictionary<int, NotaExprPoint[]>
    {
        [1] = new[] { new NotaExprPoint(NoteExpressionDim.Bend, 0, 0), new NotaExprPoint(NoteExpressionDim.Bend, 1, 6) },
    };
    int next = 10;
    r.ExpressionOf = id => curves[id];
    r.CreateExpression = points => { curves[next] = points; return next++; };
    var note = new NotaNote(60, 0, 1, 0.8f) { ExprId = 1 };
    r.SetNotes(new[] { note }, 4);
    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    var grid = r.GetVisualDescendants().OfType<Control>().Single(c => c.GetType().Name == "NoteGrid");
    var origin = grid.TranslatePoint(new Point(), w)!.Value;
    double y = origin.Y + (127 - 60 + 0.5) * 12;
    double x = origin.X + grid.Bounds.Width / 8;
    r.SetFlTool(PianoRollView.FlTool.Slice);
    w.MouseDown(new Point(x, y - 8), MouseButton.Left);
    w.MouseMove(new Point(x, y + 8)); w.MouseUp(new Point(x, y + 8), MouseButton.Left);
    var right = r.SourceNotes.Single(n => n.StartBeat > 0);
    Check(r.SourceNotes.Count == 2 && curves[right.ExprId][0].Beat == 0 && Math.Abs(curves[right.ExprId][0].Value - 3) < 1e-6,
        "slicing rebases recorded expression and interpolates the cut boundary");
    w.Close();
}
{
    var (r, w, p) = Open(true);
    var grid = r.GetVisualDescendants().OfType<Control>().Single(c => c.GetType().Name == "NoteGrid");
    double height = grid.Height;
    w.MouseDown(new Point(12, 150), MouseButton.Middle);
    w.MouseMove(new Point(12, 110));
    w.MouseUp(new Point(12, 110), MouseButton.Middle);
    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    Check(grid.Height > height && r.SourceNotes.Count == 0, "middle-drag on the keyboard zooms rows without editing notes");
    r.SetFlWorkflow(false);
    Check(grid.Height == 88 * 12, "returning to Notes restores the original row geometry");
    w.Close();
}
Console.WriteLine($"{checks} piano-roll interaction checks passed.");
