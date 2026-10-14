// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using Avalonia.Media;

namespace Nota.App;

// Shared by FL-style editors (piano roll and future playlist), independently of Nota's theme.
// The optional editors keep their familiar
// stock gray/green appearance in both Graphite and Paper. No FL assets are embedded.
internal static class FlEditorPalette
{
    public static readonly SolidColorBrush Panel = new(Color.Parse("#535B60"));
    public static readonly SolidColorBrush Toolbar = new(Color.Parse("#444C51"));
    public static readonly SolidColorBrush Grid = new(Color.Parse("#3B4348"));
    public static readonly SolidColorBrush BlackRow = new(Color.Parse("#353D42"));
    public static readonly SolidColorBrush AlternateBar = new(Color.Parse("#0AFFFFFF"));
    public static readonly SolidColorBrush Text = new(Color.Parse("#D1D8DA"));
    public static readonly SolidColorBrush MutedText = new(Color.Parse("#9FAAAF"));
    public static readonly SolidColorBrush Note = new(Color.Parse("#A1CE9C"));
    public static readonly SolidColorBrush NoteEdge = new(Color.Parse("#668D62"));
    public static readonly SolidColorBrush Selection = new(Color.Parse("#E2A08C"));
    public static readonly SolidColorBrush SelectionEdge = new(Color.Parse("#9E6456"));
    public static readonly SolidColorBrush Accent = new(Color.Parse("#E9A44D"));
    public static readonly SolidColorBrush WhiteKey = new(Color.Parse("#C6CDCF"));
    public static readonly SolidColorBrush BlackKey = new(Color.Parse("#343B3F"));
    public static readonly SolidColorBrush KeyText = new(Color.Parse("#384247"));
    public static readonly SolidColorBrush Outline = new(Color.Parse("#262E33"));
    public static readonly SolidColorBrush SelectionWash = new(Color.Parse("#35E2A08C"));
    public static readonly Pen NoteHighlight = new(new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), 1);
    public static readonly Pen Row = new(new SolidColorBrush(Color.Parse("#414A50")), 1);
    public static readonly Pen Beat = new(new SolidColorBrush(Color.Parse("#2D353A")), 1);
    // Vertical lattice, lightest-to-strongest. These sit ON TOP of the Grid/BlackRow
    // fills, so they must be clearly lighter than Grid (#3B4348) to be visible at all;
    // only the bar line is darker, as a strong separator.
    public static readonly Pen GridSub = new(new SolidColorBrush(Color.Parse("#414A50")), 1);
    public static readonly Pen GridBeat = new(new SolidColorBrush(Color.Parse("#4C565C")), 1);
    public static readonly Pen Bar = new(Outline, 1);
    public static readonly Typeface Font = new("Arial");
    public const double ToolSize = 26, ToolbarHeight = 32, OverviewHeight = 18;
}
