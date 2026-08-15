using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
using Material.Icons;
using Material.Icons.Avalonia;

namespace WorkTracker.Avalonia.Controls;

/// <summary>
/// Theme-aware icon. Renders Material Design Icons (legacy) or Material Symbols Outlined (modern)
/// depending on the active theme's <c>IconPackIsLegacy</c> resource (Boolean, false by default).
/// </summary>
/// <remarks>
/// Usage:  <c>&lt;wt:Icon Symbol="play" Size="18" Foreground="..." /&gt;</c>
///
/// <c>Symbol</c> is a logical name from <see cref="Registry"/>. Each entry maps to BOTH the MDI
/// <see cref="MaterialIconKind"/> and the Material Symbols Outlined ligature, so switching the
/// active theme re-renders the icon in the appropriate pack without touching the call site.
/// </remarks>
public class Icon : ContentControl
{
	public static readonly StyledProperty<string> SymbolProperty =
		AvaloniaProperty.Register<Icon, string>(nameof(Symbol), string.Empty);

	public static readonly StyledProperty<double> SizeProperty =
		AvaloniaProperty.Register<Icon, double>(nameof(Size), 16.0);

	public static readonly StyledProperty<bool> FilledProperty =
		AvaloniaProperty.Register<Icon, bool>(nameof(Filled), false);

	public string Symbol
	{
		get => GetValue(SymbolProperty);
		set => SetValue(SymbolProperty, value);
	}

	public double Size
	{
		get => GetValue(SizeProperty);
		set => SetValue(SizeProperty, value);
	}

	/// <summary>
	/// When true, the icon is always rendered with the legacy MDI pack regardless of the
	/// active theme. Use for action glyphs (play, stop, dots) where Material Symbols Outlined
	/// renders an outline and the design calls for a filled shape. Avalonia cannot set the
	/// Material Symbols variable font's <c>FILL</c> axis, so MDI is the fallback for filled.
	/// </summary>
	public bool Filled
	{
		get => GetValue(FilledProperty);
		set => SetValue(FilledProperty, value);
	}

	static Icon()
	{
		SymbolProperty.Changed.AddClassHandler<Icon>((sender, _) => sender.UpdateContent());
		SizeProperty.Changed.AddClassHandler<Icon>((sender, _) => sender.UpdateContent());
		FilledProperty.Changed.AddClassHandler<Icon>((sender, _) => sender.UpdateContent());
	}

	public Icon()
	{
		AttachedToVisualTree += (_, _) =>
		{
			UpdateContent();
			App.ThemeChanged += OnThemeChanged;
		};
		DetachedFromVisualTree += (_, _) =>
		{
			App.ThemeChanged -= OnThemeChanged;
		};
	}

	private void OnThemeChanged(object? sender, EventArgs e)
	{
		Dispatcher.UIThread.Post(UpdateContent);
	}

	private void UpdateContent()
	{
		if (string.IsNullOrEmpty(Symbol) || !Registry.TryGetValue(Symbol, out var entry))
		{
			Content = null;
			return;
		}

		// Filled=true forces MDI (Material Symbols Outlined renders outlines and Avalonia
		// can't set the variable font's FILL axis, so MDI is the only filled fallback).
		var isLegacy = Filled || LookupIsLegacy();

		Width = Size;
		Height = Size;

		if (isLegacy)
		{
			// MDI: Width/Height map 1:1 to glyph pixels.
			Content = new MaterialIcon
			{
				Kind = entry.Mdi,
				Width = Size,
				Height = Size
			};
		}
		else
		{
			// Material Symbols are rendered via TextBlock (ligature glyph). Font metrics
			// (ascent/descent) don't sit symmetrically around the em-square center, so
			// hand-tuning FontSize + LineHeight to land the glyph at the geometric centre
			// of a Size×Size box never lands reliably across glyphs and themes.
			//
			// Viewbox sidesteps it: measures TextBlock's natural bounds and uniform-
			// stretches them into Size×Size, centred. Glyph ends up at geometric centre
			// regardless of font metrics. Internal FontSize is arbitrary (Viewbox scales
			// it); 100 gives sub-pixel precision after the scale-down.
			// Em-square Grid (100×100) keeps Viewbox scaling 1:1 with Size so the visible
			// glyph stays at ~80% of Size (matching MDI). TextBlock inside uses negative
			// top margin to shift the glyph upward — Material Symbols Outlined's font
			// line metric leaves more leading above the visible glyph than below, so
			// without compensation the glyph drifts below the geometric centre.
			Content = new global::Avalonia.Controls.Viewbox
			{
				Stretch = global::Avalonia.Media.Stretch.Uniform,
				Child = new Grid
				{
					Width = 100,
					Height = 100,
					Children =
					{
						new TextBlock
						{
							Classes = { "symbol" },
							Text = entry.Symbol,
							FontSize = 100,
							Margin = new Thickness(0, -10, 0, 0)
						}
					}
				}
			};
		}
	}

	private bool LookupIsLegacy()
	{
		var app = global::Avalonia.Application.Current;
		if (app == null)
		{
			return false;
		}

		// IconPackIsLegacy is a theme-neutral boolean (not wrapped in ThemeDictionaries),
		// so we look it up without a ThemeVariant filter — passing the current variant can
		// miss the resource in some Avalonia versions.
		if (app.Resources.TryGetResource("IconPackIsLegacy", null, out var val) && val is bool b)
		{
			return b;
		}

		return false;
	}

	// Logical icon name → (MDI MaterialIconKind, Material Symbols Outlined ligature)
	private static readonly Dictionary<string, (MaterialIconKind Mdi, string Symbol)> Registry = new(StringComparer.OrdinalIgnoreCase)
	{
		["app"]              = (MaterialIconKind.ClockTimeFourOutline, "schedule"),
		["window-minimize"]  = (MaterialIconKind.Minus, "remove"),
		["window-close"]     = (MaterialIconKind.Close, "close"),
		["active-dot"]       = (MaterialIconKind.CircleMedium, "fiber_manual_record"),
		["play"]             = (MaterialIconKind.Play, "play_arrow"),
		["stop"]             = (MaterialIconKind.Stop, "stop"),
		["skip-next"]        = (MaterialIconKind.SkipNext, "skip_next"),
		["pomodoro"]         = (MaterialIconKind.TimerSandComplete, "hourglass_top"),
		["timer"]            = (MaterialIconKind.TimerOutline, "timer"),
		["cloud-upload"]     = (MaterialIconKind.CloudUploadOutline, "cloud_upload"),
		["settings"]         = (MaterialIconKind.CogOutline, "settings"),
		["refresh"]          = (MaterialIconKind.Refresh, "refresh"),
		["lightbulb"]        = (MaterialIconKind.LightbulbOutline, "lightbulb"),
		["plus"]             = (MaterialIconKind.Plus, "add"),
		["chevron-left"]     = (MaterialIconKind.ChevronLeft, "chevron_left"),
		["chevron-right"]    = (MaterialIconKind.ChevronRight, "chevron_right"),
		["today"]            = (MaterialIconKind.CalendarToday, "today"),
		["edit"]             = (MaterialIconKind.Pencil, "edit"),
		["delete"]           = (MaterialIconKind.Delete, "delete")
	};
}
