using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;

namespace WorkTracker.Architecture.Tests;

/// <summary>
/// Keeps the dependencies between the projects the way the architecture describes them. Reads the
/// project files rather than the built assemblies, because the compiler leaves a reference it does
/// not use out of the metadata, and an unused reference is exactly what should get noticed here.
/// </summary>
public class ProjectDependencyTests
{
	private const string Abstractions = "WorkTracker.Plugin.Abstractions";
	private const string Application = "WorkTracker.Application";
	private const string Domain = "WorkTracker.Domain";
	private const string Infrastructure = "WorkTracker.Infrastructure";

	private static readonly Dictionary<string, string[]> AllowedReferences = new()
	{
		[Domain] = [],
		[Abstractions] = [],
		[Application] = [Domain, Abstractions],
		[Infrastructure] = [Application, Domain],
		["WorkTracker.CLI"] = [Application, Infrastructure],
		["WorkTracker.Avalonia"] = [Application, Infrastructure],
	};

	private static readonly string[] CoreProjects = [Domain, Abstractions, Application];

	private static readonly string[] FrameworkFreeFolders = ["ViewModels", "Orchestrators", "Models", "Helpers"];

	private static readonly string[] PresentationAndPersistencePackages = ["Avalonia", "Microsoft.EntityFrameworkCore", "Spectre.Console"];

	private static readonly string RepositoryRoot = FindRepositoryRoot();

	private static string FindRepositoryRoot()
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory != null && !File.Exists(Path.Combine(directory.FullName, "WorkTracker.slnx")))
		{
			directory = directory.Parent;
		}

		return directory?.FullName ?? throw new InvalidOperationException("WorkTracker.slnx not found above the test output");
	}

	/// <summary>
	/// Goes by the solution file, so leftovers on a developer's disk don't count as projects.
	/// </summary>
	private static List<string> ProductionProjectPaths()
	{
		return XDocument.Load(Path.Combine(RepositoryRoot, "WorkTracker.slnx"))
			.Descendants("Project")
			.Select(project => project.Attribute("Path")!.Value)
			.Where(path => path.StartsWith("src/", StringComparison.Ordinal) || path.StartsWith("plugins/", StringComparison.Ordinal))
			.ToList();
	}

	private static string ProjectName(string path) => Path.GetFileNameWithoutExtension(path.Replace('\\', '/'));

	private static IEnumerable<string> ItemsOf(string projectPath, string itemName)
	{
		return XDocument.Load(Path.Combine(RepositoryRoot, projectPath))
			.Descendants(itemName)
			.Select(item => item.Attribute("Include")!.Value);
	}

	private static string[] AllowedReferencesOf(string projectName)
	{
		// A plugin sees the application only through the contracts
		return projectName.StartsWith("WorkTracker.Plugin.", StringComparison.Ordinal) && projectName != Abstractions
			? [Abstractions]
			: AllowedReferences[projectName];
	}

	[Fact]
	public void ProductionProjects_AreFoundInTheSolution()
	{
		ProductionProjectPaths().Select(ProjectName).Should().Contain(AllowedReferences.Keys);
	}

	[Fact]
	public void ProductionProjects_ReferenceOnlyWhatTheArchitectureAllows()
	{
		var violations = new List<string>();

		foreach (var path in ProductionProjectPaths())
		{
			var name = ProjectName(path);
			var isDeclared = AllowedReferences.ContainsKey(name) || name.StartsWith("WorkTracker.Plugin.", StringComparison.Ordinal);
			if (!isDeclared)
			{
				violations.Add($"{name} is new: decide what it may reference and add it to {nameof(AllowedReferences)}");
				continue;
			}

			var allowed = AllowedReferencesOf(name);
			violations.AddRange(ItemsOf(path, "ProjectReference")
				.Select(ProjectName)
				.Where(reference => !allowed.Contains(reference))
				.Select(reference => $"{name} must not reference {reference}"));
		}

		violations.Should().BeEmpty();
	}

	/// <summary>
	/// View models and what they build on are tested without a UI, and get their colors and threads
	/// from the views and <c>IUiDispatcher</c>. All of it shares a project with the views, so the
	/// compiler doesn't keep them apart. Services are not covered, because the platform ones live there.
	/// </summary>
	[Theory]
	[MemberData(nameof(FrameworkFreeFolderNames))]
	public void FrameworkFreeFolders_DoNotUseAvalonia(string folder)
	{
		var uiFramework = new Regex(@"(?<![\w.])(global::)?Avalonia\.|(?<![\w.])App\.(?!axaml)\w");

		var violations = Directory.EnumerateFiles(Path.Combine(RepositoryRoot, "src", "WorkTracker.Avalonia", folder), "*.cs", SearchOption.AllDirectories)
			.Where(file => uiFramework.IsMatch(File.ReadAllText(file)))
			.Select(Path.GetFileName)
			.ToList();

		violations.Should().BeEmpty();
	}

	public static TheoryData<string> FrameworkFreeFolderNames => new(FrameworkFreeFolders);

	/// <summary>
	/// A view model holds what one view shows. Handing it out from an orchestrator lets a cache or a
	/// plugin host end up sharing it, so orchestrators take and return plain data.
	/// </summary>
	[Fact]
	public void Orchestrators_DoNotUseViewModels()
	{
		var viewModel = new Regex(@"\b\w+ViewModel\b|WorkTracker\.Avalonia\.ViewModels");

		var violations = Directory.EnumerateFiles(Path.Combine(RepositoryRoot, "src", "WorkTracker.Avalonia", "Orchestrators"), "*.cs", SearchOption.AllDirectories)
			.Where(file => viewModel.IsMatch(File.ReadAllText(file)))
			.Select(Path.GetFileName)
			.ToList();

		violations.Should().BeEmpty();
	}

	/// <summary>
	/// The application assigns the main window and <c>MainWindowAccessor</c> is the way to it for everyone else.
	/// </summary>
	[Fact]
	public void ApplicationLifetime_IsUsedOnlyByTheApplicationAndTheAccessor()
	{
		string[] allowed = ["App.axaml.cs", "MainWindowAccessor.cs"];

		var violations = Directory.EnumerateFiles(Path.Combine(RepositoryRoot, "src", "WorkTracker.Avalonia"), "*.cs", SearchOption.AllDirectories)
			.Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
			.Where(file => !allowed.Contains(Path.GetFileName(file)))
			.Where(file => File.ReadAllText(file).Contains("ApplicationLifetime", StringComparison.Ordinal))
			.Select(Path.GetFileName)
			.ToList();

		violations.Should().BeEmpty();
	}

	[Fact]
	public void CoreProjects_DoNotDependOnPresentationOrPersistencePackages()
	{
		var violations = ProductionProjectPaths()
			.Where(path => CoreProjects.Contains(ProjectName(path)))
			.SelectMany(path => ItemsOf(path, "PackageReference")
				.Where(package => PresentationAndPersistencePackages.Any(forbidden => package.StartsWith(forbidden, StringComparison.Ordinal)))
				.Select(package => $"{ProjectName(path)} must not use {package}"))
			.ToList();

		violations.Should().BeEmpty();
	}
}
