// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The Mixer in its own window (opened from View → Mixer). It hosts the single MixerView
// instance the main window owns (reparented, not rebuilt), so meters/state carry over and
// the tick loop keeps updating it while the window is open. Closing detaches the mixer.
// Its own title bar carries the Strips | Stereo Map switch (specs/008).

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace Nota.App;

internal sealed class MixerWindow : NotaWindow
{
    public ContentControl Host { get; }
    public event Action? WindowClosed;
    /// <summary>The user picked a view in the title bar.</summary>
    public event Action<MixerViewMode>? ViewPicked;
    private readonly Action<int> _setViewSegment;

    private const double BarH = 36;
    private static readonly double LeftInset = !NativeFrame && OperatingSystem.IsMacOS() ? 76 : 12;
    private static readonly double RightInset = !NativeFrame && OperatingSystem.IsWindows() ? 8 + 138 : 8;

    private readonly MainWindow _owner;

    public MixerWindow(MainWindow owner, MixerViewMode view) : base(BarH)
    {
        _owner = owner;

        // Route transport keys (Space/Return) in the tunnel phase so a focused control here
        // can't steal them, exactly like the main window does.
        AddHandler(KeyDownEvent, (_, e) => _owner.HandleTransportKeyTunnel(e), RoutingStrategies.Tunnel);

        Title = "Mixer";
        Width = 1040;
        Height = 460;
        MinWidth = 560;
        MinHeight = 280;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = NotaPalette.BgApp;

        Host = new ContentControl { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };

        // Title bar: caption left, the view switch centred; empty bar area drags the window.
        var caption = new TextBlock
        {
            Text = "Mixer", FontSize = NotaType.Name, FontWeight = FontWeight.SemiBold, Foreground = NotaPalette.TextPrimary,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var seg = ClipEditorKit.Segments(new[] { "Strips", "Stereo Map" }, view == MixerViewMode.StereoMap ? 1 : 0,
            i => ViewPicked?.Invoke(i == 1 ? MixerViewMode.StereoMap : MixerViewMode.Strips), out _setViewSegment);
        seg.HorizontalAlignment = HorizontalAlignment.Center;
        seg.VerticalAlignment = VerticalAlignment.Center;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*") };
        grid.Children.Add(caption);
        Grid.SetColumn(seg, 1);
        grid.Children.Add(seg);
        var bar = new Border
        {
            Height = BarH, Background = NotaPalette.Panel, BorderBrush = NotaPalette.GraphBorder,
            BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(LeftInset, 0, RightInset, 0), Child = grid,
        };
        if (!NativeFrame) bar.PointerPressed += OnChromePressed;

        var dock = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        dock.Children.Add(bar);
        dock.Children.Add(Host);
        SetBody(dock);
    }

    /// <summary>Repaints the title-bar switch (the view changed elsewhere).</summary>
    public void ShowView(MixerViewMode view) => _setViewSegment(view == MixerViewMode.StereoMap ? 1 : 0);

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        Host.Content = null;   // detach the mixer so it can be re-hosted next time
        WindowClosed?.Invoke();
    }
}
