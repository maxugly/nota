// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The Mixer lives in its own window (View → Mixer / ⌘⇧M) rather than as a main-view tab.
// The single MixerView instance is hosted in the window while it's open and detached on close.

using System;
using Microsoft.Extensions.DependencyInjection;
using Nota.Application;

namespace Nota.App;

public partial class MainWindow
{
    private MixerWindow? _mixerWindow;

    private void OnMenuMixer(object? sender, EventArgs e) => ToggleMixerWindow();

    /// <summary>Open the Mixer window, or close it if it's already open (⌘⇧M / View → Mixer).</summary>
    internal void ToggleMixerWindow()
    {
        if (_mixerWindow is not null) { _mixerWindow.Close(); return; }
        if (_mixer is null) return;

        // The view (Strips / Stereo Map, specs/008) is a per-user preference.
        var settings = App.Services.GetRequiredService<ISettingsService>();
        var view = settings.Current.MixerView == "map" ? MixerViewMode.StereoMap : MixerViewMode.Strips;
        _mixer.View = view;
        var win = new MixerWindow(this, view);
        win.ViewPicked += v =>
        {
            if (_mixer is not null) _mixer.View = v;
            settings.Current.MixerView = v == MixerViewMode.StereoMap ? "map" : "strips";
            settings.Save();
        };
        win.EnableMidiLearn(_learn);   // faders/knobs stay MIDI-mappable in the window
        win.WindowClosed += () => { if (_mixer is not null) _mixer.IsVisible = false; _mixerWindow = null; };
        _mixerWindow = win;

        _mixer.IsVisible = true;       // so the tick loop updates its meters
        SetHost(win.Host, _mixer);
        _mixer.Refresh();
        win.Show(this);
    }
}
