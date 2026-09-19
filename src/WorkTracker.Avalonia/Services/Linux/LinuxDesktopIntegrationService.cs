using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Microsoft.Extensions.Logging;
using WorkTracker.UI.Shared.Services;

namespace WorkTracker.Avalonia.Services.Linux;

/// <summary>
/// Installs the freedesktop.org desktop entry and the hicolor icons into the user's home directory,
/// so the window manager can pair a window with the application (via <see cref="DesktopEntry.AppId"/>
/// as its WM_CLASS) and show its real name and icon instead of a generic placeholder. The published
/// Linux build is a bare archive with no installer, so the app registers itself on first run.
/// <para>
/// Everything here is best-effort: failures are logged and swallowed, because cosmetic desktop
/// integration must never keep the application from starting.
/// </para>
/// </summary>
public sealed class LinuxDesktopIntegrationService : IDesktopIntegrationService
{
	/// <summary>Sizes written into the hicolor theme, scaled down from the embedded source icon.</summary>
	private static readonly int[] s_iconSizes = [48, 128, 256];

	private const string IconResource = "app-ico.png";

	private readonly ILogger<LinuxDesktopIntegrationService> _logger;
	private readonly string _applicationsDirectory;
	private readonly string _iconsDirectory;
	private readonly bool _refreshDesktopCaches;
	private readonly Lock _installationLock = new();

	private Task? _installation;

	public LinuxDesktopIntegrationService(ILogger<LinuxDesktopIntegrationService> logger)
		: this(logger, XdgDirectories.ApplicationsDirectory, XdgDirectories.IconsDirectory, refreshDesktopCaches: true)
	{
	}

	/// <summary>Installs into explicit directories, so tests do not touch the real desktop.</summary>
	internal LinuxDesktopIntegrationService(
		ILogger<LinuxDesktopIntegrationService> logger,
		string applicationsDirectory,
		string iconsDirectory,
		bool refreshDesktopCaches)
	{
		_logger = logger;
		_applicationsDirectory = applicationsDirectory;
		_iconsDirectory = iconsDirectory;
		_refreshDesktopCaches = refreshDesktopCaches;
	}

	/// <summary>
	/// Installs once per process and hands every later caller the same task. Startup kicks this off
	/// and the global shortcut waits on it, and both mean the one installation — repeating it would
	/// rewrite the files underneath whoever is reading them.
	/// <para>
	/// The first caller's <paramref name="cancellationToken"/> is the one that governs the work.
	/// </para>
	/// </summary>
	public Task EnsureInstalledAsync(CancellationToken cancellationToken = default)
	{
		lock (_installationLock)
		{
			return _installation ??= StartInstallAsync(cancellationToken);
		}
	}

	private Task StartInstallAsync(CancellationToken cancellationToken)
	{
		if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
		{
			return Task.CompletedTask;
		}

		var processPath = Environment.ProcessPath;
		if (string.IsNullOrEmpty(processPath))
		{
			_logger.LogWarning("Skipping desktop integration: Environment.ProcessPath is null or empty");
			return Task.CompletedTask;
		}

		if (IsSystemLocation(processPath))
		{
			_logger.LogDebug("Skipping desktop integration: running from a system location ({Path})", processPath);
			return Task.CompletedTask;
		}

		if (IsBuildOutput(processPath))
		{
			_logger.LogDebug("Skipping desktop integration: running from a build output ({Path})", processPath);
			return Task.CompletedTask;
		}

		return Task.Run(() => Install(processPath, cancellationToken), cancellationToken);
	}

	/// <summary>
	/// A copy under /usr or /opt was put there by a package manager, which ships its own desktop entry
	/// and icons; writing ours into the home directory would shadow them.
	/// </summary>
	internal static bool IsSystemLocation(string processPath) =>
		processPath.StartsWith("/usr/", StringComparison.Ordinal)
		|| processPath.StartsWith("/opt/", StringComparison.Ordinal);

	/// <summary>
	/// A copy inside bin/Debug or bin/Release is a build being run during development. It would
	/// otherwise point the menu entry at itself, and the next launch from the menu would start that
	/// build instead of the installed one — with an empty plugins/ directory next to it.
	/// </summary>
	internal static bool IsBuildOutput(string processPath)
	{
		for (var directory = Path.GetDirectoryName(processPath); directory is not null; directory = Path.GetDirectoryName(directory))
		{
			var configuration = Path.GetFileName(directory);
			if (configuration is not ("Debug" or "Release"))
			{
				continue;
			}

			var parent = Path.GetDirectoryName(directory);
			if (parent is not null && Path.GetFileName(parent) == "bin")
			{
				return true;
			}
		}

		return false;
	}

	internal void Install(string execPath, CancellationToken cancellationToken = default)
	{
		try
		{
			var iconsChanged = InstallIcons(cancellationToken);
			var entryChanged = InstallApplicationEntry(execPath);

			if (!entryChanged && !iconsChanged)
			{
				return;
			}

			if (_refreshDesktopCaches)
			{
				RefreshDesktopCaches();
			}

			_logger.LogInformation("Desktop integration installed for {Path}", execPath);
		}
		catch (OperationCanceledException) { /* Shutting down */ }
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Desktop integration failed");
		}
	}

	private bool InstallApplicationEntry(string execPath)
	{
		Directory.CreateDirectory(_applicationsDirectory);

		var path = Path.Combine(_applicationsDirectory, DesktopEntry.FileName);
		var contents = DesktopEntry.BuildApplicationEntry(execPath);

		// Rewrite only on change — the Exec path moves when the app is updated, but on an ordinary
		// launch there is nothing to do.
		if (File.Exists(path) && File.ReadAllText(path) == contents)
		{
			return false;
		}

		File.WriteAllText(path, contents);
		return true;
	}

	private bool InstallIcons(CancellationToken cancellationToken)
	{
		using var source = typeof(LinuxDesktopIntegrationService).Assembly.GetManifestResourceStream(IconResource);
		if (source == null)
		{
			_logger.LogWarning("Skipping icon installation: embedded resource {Resource} not found", IconResource);
			return false;
		}

		using var buffer = new MemoryStream();
		source.CopyTo(buffer);

		var changed = false;
		foreach (var size in s_iconSizes)
		{
			cancellationToken.ThrowIfCancellationRequested();
			changed |= InstallIcon(buffer, size);
		}

		return changed;
	}

	private bool InstallIcon(MemoryStream source, int size)
	{
		var directory = Path.Combine(_iconsDirectory, "hicolor", $"{size}x{size}", "apps");
		var path = Path.Combine(directory, $"{DesktopEntry.AppId}.png");

		// The icon never changes for a given app id, so an existing file is already the right one.
		if (File.Exists(path))
		{
			return false;
		}

		var scaled = Scale(source, size);

		// Scaling needs the rendering platform; where that is unavailable an unscaled icon in the
		// wrong size bucket still beats no icon at all — icon themes scale what they find. Encoding
		// into memory first also keeps a failed encode from leaving an empty file behind.
		if (scaled.Length == 0)
		{
			scaled = source.ToArray();
		}

		Directory.CreateDirectory(directory);
		File.WriteAllBytes(path, scaled);
		return true;
	}

	private byte[] Scale(MemoryStream source, int size)
	{
		try
		{
			source.Position = 0;
			using var bitmap = new Bitmap(source);
			using var scaled = bitmap.CreateScaledBitmap(new PixelSize(size, size));
			using var encoded = new MemoryStream();
			// Saves PNG. Avalonia 12.1 deprecates this overload in favour of
			// Save(Stream, PngBitmapEncoderOptions), which does not exist in 12.0 — switch to it
			// when the Avalonia pin in Directory.Packages.props moves back up.
			scaled.Save(encoded);
			return encoded.ToArray();
		}
		catch (Exception ex)
		{
			_logger.LogDebug(ex, "Could not scale the application icon to {Size}px", size);
			return [];
		}
	}

	/// <summary>
	/// Nudges the desktop into picking up the new entry and icons. KDE and GNOME both notice the files
	/// on their own, so a missing or failing tool is not worth reporting.
	/// <para>
	/// Plasma notices in its own time, which is too late for the shortcut we are about to bind: the
	/// portal reads our display name out of the service cache, and whatever it finds on the first
	/// bind is the name the user is stuck with. So that one cache is rebuilt here and now.
	/// </para>
	/// </summary>
	private void RefreshDesktopCaches()
	{
		RunIfAvailable("update-desktop-database", _applicationsDirectory);
		RunIfAvailable("gtk-update-icon-cache", "-t", "-f", Path.Combine(_iconsDirectory, "hicolor"));
		RunIfAvailable("kbuildsycoca6");
	}

	private void RunIfAvailable(string fileName, params string[] arguments)
	{
		try
		{
			var startInfo = new ProcessStartInfo(fileName)
			{
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false
			};

			foreach (var argument in arguments)
			{
				startInfo.ArgumentList.Add(argument);
			}

			using var process = Process.Start(startInfo);
			process?.WaitForExit(milliseconds: 5000);
		}
		catch (Exception ex)
		{
			_logger.LogDebug(ex, "Could not run {Tool}", fileName);
		}
	}
}
