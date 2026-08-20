using System.Xml.Linq;
using Xunit;

namespace OpenLogicool.Architecture.Tests;

/// <summary>
/// Slice 1 の project metadata を csproj から直接検査する。
/// P/Invoke および DllImport の検査は Slice 1 の範囲外であり、この class は検査しない。
/// </summary>
public sealed class ProjectReferenceDirectionTests
{
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> AllowedReferences =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            ["OpenLogicool.Contracts"] = new HashSet<string>(StringComparer.Ordinal),
            ["OpenLogicool.Domain"] = Set("OpenLogicool.Contracts"),
            ["OpenLogicool.Devices.G13"] = Set("OpenLogicool.Contracts"),
            ["OpenLogicool.Devices.G600"] = Set("OpenLogicool.Contracts"),
            ["OpenLogicool.Input"] = Set("OpenLogicool.Contracts", "OpenLogicool.Domain"),
            // watchdog は host 死亡後も動く最後の砦なので依存ゼロ（§6.2・DEV-009）
            ["OpenLogicool.Watchdog"] = new HashSet<string>(StringComparer.Ordinal),
            ["OpenLogicool.Profiles"] = Set("OpenLogicool.Contracts", "OpenLogicool.Domain"),
            ["OpenLogicool.Playbooks"] = Set("OpenLogicool.Contracts", "OpenLogicool.Domain"),
            ["OpenLogicool.Persistence"] = Set("OpenLogicool.Contracts", "OpenLogicool.Domain"),
            ["OpenLogicool.Capture"] = Set("OpenLogicool.Contracts"),
            ["OpenLogicool.Perception"] = Set("OpenLogicool.Contracts"),
            ["OpenLogicool.AI"] = Set("OpenLogicool.Contracts"),
            ["OpenLogicool.Desktop"] = Set("OpenLogicool.Contracts", "OpenLogicool.Domain"),
            ["OpenLogicool.Host"] = Set(
                "OpenLogicool.Contracts",
                "OpenLogicool.Domain",
                "OpenLogicool.Devices.G13",
                "OpenLogicool.Devices.G600",
                "OpenLogicool.Input",
                "OpenLogicool.Profiles",
                "OpenLogicool.Playbooks",
                "OpenLogicool.Persistence",
                "OpenLogicool.Capture",
                "OpenLogicool.Perception",
                "OpenLogicool.AI",
                "OpenLogicool.Desktop"),
            ["OpenLogicool.Fakes"] = Set("OpenLogicool.Contracts", "OpenLogicool.Domain"),
        };

    [Fact]
    public void Slice_one_projects_only_reference_the_allowed_projects()
    {
        var referencesByProject = LoadReferences();

        Assert.Equal(AllowedReferences.Keys.OrderBy(name => name), referencesByProject.Keys.OrderBy(name => name));

        foreach (var (projectName, actualReferences) in referencesByProject)
        {
            Assert.True(AllowedReferences.TryGetValue(projectName, out var allowedReferences));
            Assert.True(
                actualReferences.SetEquals(allowedReferences!),
                $"{projectName} の参照が許可行列と一致しません。実際: {string.Join(", ", actualReferences.OrderBy(name => name))}");
        }
    }

    [Fact]
    public void Prohibited_direct_references_are_absent()
    {
        var referencesByProject = LoadReferences();

        Assert.DoesNotContain("OpenLogicool.Playbooks", referencesByProject["OpenLogicool.AI"]);
        Assert.DoesNotContain("OpenLogicool.Input", referencesByProject["OpenLogicool.Perception"]);

        foreach (var deviceProject in new[] { "OpenLogicool.Devices.G13", "OpenLogicool.Devices.G600" })
        {
            Assert.DoesNotContain("OpenLogicool.Desktop", referencesByProject[deviceProject]);
            Assert.DoesNotContain("OpenLogicool.AI", referencesByProject[deviceProject]);
            Assert.DoesNotContain("OpenLogicool.Perception", referencesByProject[deviceProject]);
            Assert.DoesNotContain("OpenLogicool.Persistence", referencesByProject[deviceProject]);
        }

        foreach (var forbiddenReference in new[]
                 {
                     "OpenLogicool.Devices.G13",
                     "OpenLogicool.Devices.G600",
                     "OpenLogicool.Persistence",
                     "OpenLogicool.AI",
                     "OpenLogicool.Capture",
                 })
        {
            Assert.DoesNotContain(forbiddenReference, referencesByProject["OpenLogicool.Desktop"]);
        }
    }

    [Fact]
    public void Ai_project_isolated_from_execution_device_storage_and_capture_modules()
    {
        var referencesByProject = LoadReferences();
        var aiReferences = referencesByProject["OpenLogicool.AI"];

        Assert.Equal(Set("OpenLogicool.Contracts"), aiReferences);

        foreach (var forbiddenReference in new[]
                 {
                     "OpenLogicool.Input",
                     "OpenLogicool.Devices.G13",
                     "OpenLogicool.Devices.G600",
                     "OpenLogicool.Persistence",
                     "OpenLogicool.Capture",
                 })
        {
            Assert.DoesNotContain(forbiddenReference, aiReferences);
        }
    }

    [Fact]
    public void Package_references_match_module_policy()
    {
        var packageReferencesByProject = LoadPackageReferences();

        foreach (var projectName in new[]
                 {
                     "OpenLogicool.Contracts",
                     "OpenLogicool.Devices.G13",
                     "OpenLogicool.Devices.G600",
                     "OpenLogicool.Perception",
                     "OpenLogicool.Domain",
                 })
        {
            Assert.Empty(packageReferencesByProject[projectName]);
        }

        Assert.Equal(
            new[] { "Microsoft.Data.Sqlite", "SQLitePCLRaw.lib.e_sqlite3" },
            packageReferencesByProject["OpenLogicool.Persistence"].OrderBy(packageName => packageName));
    }

    [Fact]
    public void Only_desktop_and_host_use_wpf()
    {
        var useWpfProjects = LoadUseWpf()
            .Where(project => project.Value)
            .Select(project => project.Key)
            .OrderBy(projectName => projectName);

        Assert.Equal(
            new[] { "OpenLogicool.Desktop", "OpenLogicool.Host" },
            useWpfProjects);
    }

    private static Dictionary<string, HashSet<string>> LoadReferences()
    {
        return EnumerateSliceOneProjects()
            .ToDictionary(
                project => project.ProjectName,
                project => ReadReferences(project.ProjectPath),
                StringComparer.Ordinal);
    }

    private static Dictionary<string, HashSet<string>> LoadPackageReferences() =>
        EnumerateSliceOneProjects()
            .ToDictionary(
                project => project.ProjectName,
                project => ReadItemIncludes(project.ProjectPath, "PackageReference"),
                StringComparer.Ordinal);

    private static Dictionary<string, bool> LoadUseWpf() =>
        EnumerateSliceOneProjects()
            .ToDictionary(
                project => project.ProjectName,
                project => ReadUseWpf(project.ProjectPath),
                StringComparer.Ordinal);

    private static HashSet<string> ReadReferences(string projectPath)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath)!;
        return ReadItemIncludes(projectPath, "ProjectReference")
            .Select(include => Path.GetFullPath(include, projectDirectory))
            .Select(projectReferencePath => Path.GetFileNameWithoutExtension(projectReferencePath)!)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static HashSet<string> ReadItemIncludes(string projectPath, string itemName) =>
        XDocument.Load(projectPath)
            .Descendants()
            .Where(element => element.Name.LocalName == itemName)
            .Select(element => element.Attribute("Include")?.Value)
            .OfType<string>()
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .ToHashSet(StringComparer.Ordinal);

    private static bool ReadUseWpf(string projectPath) =>
        XDocument.Load(projectPath)
            .Descendants()
            .Where(element => element.Name.LocalName == "UseWPF")
            .Select(element => bool.TryParse(element.Value, out var value) && value)
            .Any(value => value);

    private static IEnumerable<(string ProjectName, string ProjectPath)> EnumerateSliceOneProjects()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourceDirectory = Path.Combine(repositoryRoot, "src");
        var phaseZeroProjects = new HashSet<string>(StringComparer.Ordinal)
        {
            "OpenLogicool.Probe",
            "OpenLogicool.CaptureProbe",
            "OpenLogicool.GameLab.Prototype",
            "OpenLogicool.Desktop.SmokeApp",
        };

        return Directory.EnumerateFiles(sourceDirectory, "*.csproj", SearchOption.AllDirectories)
            .Append(Path.Combine(repositoryRoot, "tests", "OpenLogicool.Fakes", "OpenLogicool.Fakes.csproj"))
            .Select(projectPath => (ProjectName: Path.GetFileNameWithoutExtension(projectPath)!, ProjectPath: projectPath))
            .Where(project => !phaseZeroProjects.Contains(project.ProjectName));
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OpenLogicool.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("OpenLogicool.sln を含む repository root を特定できません。");
    }

    private static IReadOnlySet<string> Set(params string[] projectNames) =>
        new HashSet<string>(projectNames, StringComparer.Ordinal);
}
