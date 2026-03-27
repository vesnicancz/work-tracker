using System;
using System.Globalization;

namespace Luxafor.HidSharp;

/// <summary>
/// Represents an RGB color for the Luxafor device.
/// </summary>
public readonly record struct LuxaforColor(byte R, byte G, byte B)
{
	/// <summary>Red (255, 0, 0).</summary>
	public static readonly LuxaforColor Red = new(255, 0, 0);

	/// <summary>Green (0, 255, 0).</summary>
	public static readonly LuxaforColor Green = new(0, 255, 0);

	/// <summary>Blue (0, 0, 255).</summary>
	public static readonly LuxaforColor Blue = new(0, 0, 255);

	/// <summary>Yellow (255, 255, 0).</summary>
	public static readonly LuxaforColor Yellow = new(255, 255, 0);

	/// <summary>Cyan (0, 255, 255).</summary>
	public static readonly LuxaforColor Cyan = new(0, 255, 255);

	/// <summary>Magenta (255, 0, 255).</summary>
	public static readonly LuxaforColor Magenta = new(255, 0, 255);

	/// <summary>White (255, 255, 255).</summary>
	public static readonly LuxaforColor White = new(255, 255, 255);

	/// <summary>Off / black (0, 0, 0).</summary>
	public static readonly LuxaforColor Off = new(0, 0, 0);

	/// <summary>
	/// Parses a hex color string (e.g. "#FF0000") into a <see cref="LuxaforColor"/>.
	/// </summary>
	/// <param name="hex">Hex color string in #RRGGBB format.</param>
	/// <returns>The parsed color.</returns>
	/// <exception cref="FormatException">Thrown when the string is not a valid hex color.</exception>
	public static LuxaforColor FromHex(string hex)
	{
		if (TryFromHex(hex, out var color))
		{
			return color;
		}

		throw new FormatException($"Invalid hex color: '{hex}'. Expected format: #RRGGBB");
	}

	/// <summary>
	/// Tries to parse a hex color string (e.g. "#FF0000") into a <see cref="LuxaforColor"/>.
	/// </summary>
	/// <param name="hex">Hex color string in #RRGGBB format.</param>
	/// <param name="color">The parsed color, or default if parsing fails.</param>
	/// <returns><c>true</c> if parsing succeeded.</returns>
	public static bool TryFromHex(string? hex, out LuxaforColor color)
	{
		color = default;

		if (string.IsNullOrWhiteSpace(hex) || hex!.Length != 7 || hex[0] != '#')
		{
			return false;
		}

#if NETSTANDARD2_0
		if (byte.TryParse(hex.Substring(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r) &&
		    byte.TryParse(hex.Substring(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g) &&
		    byte.TryParse(hex.Substring(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
#else
		if (byte.TryParse(hex.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r) &&
			byte.TryParse(hex.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g) &&
			byte.TryParse(hex.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
#endif
		{
			color = new LuxaforColor(r, g, b);
			return true;
		}

		return false;
	}

	/// <summary>
	/// Returns the color as a hex string (e.g. "#FF0000").
	/// </summary>
	public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";

	/// <inheritdoc />
	public override string ToString() => ToHex();
}