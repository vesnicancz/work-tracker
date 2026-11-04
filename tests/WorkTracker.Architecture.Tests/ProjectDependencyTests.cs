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
	private const string UiShared = "WorkTracker.UI.Shared";

	private static readonly Dictionary<string, string[]> AllowedReferences = new()
	{
		[Domain] = [],
		[Abstractions] = [],
		[Application] = [Domain, Abstractions],
		[Infrastructure] = [Application, Domain],
		[UiShared] = [Application],
		["WorkTracker.CLI"] = [Application, Infrastructure],
		["WorkTracker.Avalonia"] = [UiShared, Infrastructure],
	};

	private static readonly string[] CoreProjects = [Domain, Abstractions, Application, UiShared];

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
