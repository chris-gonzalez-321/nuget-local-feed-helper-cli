using NugetLocalFeed.Cli.Core.Models;
using NugetLocalFeed.Cli.Core.Validation;

namespace NugetLocalFeed.Cli.Core;

/// <summary>Turns the (already validated) path templates of a repo file into real paths.</summary>
public static class RepoPathResolver
{
    private static readonly string[] ProjectExtensions = [".csproj", ".fsproj", ".vbproj"];

    public static bool DerivesProjectFolder(string pkgDirTemplate)
    {
        var normalized = Path.TrimEndingDirectorySeparator(pkgDirTemplate.Replace('\\', '/'));
        return normalized.EndsWith($"/bin/{InputValidator.BuildConfigToken}", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsAllowedProjectPath(string template)
    {
        var name = Path.GetFileName(template.Replace('\\', '/'));
        // A folder (no extension) or an explicit project file.
        return !name.Contains('.') || ProjectExtensions.Any(e => name.EndsWith(e, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The folder or project file handed to `dotnet pack`.</summary>
    public static string ResolveProjectPath(RepoSettings repo, PackageEntry pkg, string buildConfig)
    {
        var solutionDir = Path.GetFullPath(InputValidator.ExpandHome(repo.PackagesSolutionDirectory!));

        if (pkg.ProjectPath is not null)
        {
            return ResolveInside(pkg.ProjectPath, solutionDir, buildConfig);
        }

        var outputDir = Path.TrimEndingDirectorySeparator(ResolveInside(pkg.PkgDir!, solutionDir, buildConfig));
        var configDir = new DirectoryInfo(outputDir);
        var binDir = configDir.Parent;
        var projectDir = binDir?.Parent;

        if (!string.Equals(configDir.Name, buildConfig, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(binDir?.Name, "bin", StringComparison.OrdinalIgnoreCase)
            || projectDir is null)
        {
            throw new InvalidOperationException($"Cannot derive the project folder from PkgDir '{pkg.PkgDir}'. Set ProjectPath instead.");
        }

        return projectDir.FullName;
    }

    /// <summary>Expands tokens, accepts either slash style, and refuses anything outside the solution dir.</summary>
    private static string ResolveInside(string template, string solutionDir, string buildConfig)
    {
        // Normalize slashes in the template only, so a Windows-style file works on Mac/Linux.
        var expanded = InputValidator.ExpandTokens(template.Replace('\\', '/'), solutionDir, buildConfig);
        var full = Path.GetFullPath(expanded.Replace('/', Path.DirectorySeparatorChar), solutionDir);

        var relative = Path.GetRelativePath(solutionDir, full);
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Path '{template}' resolves outside PackagesSolutionDirectory.");
        }

        return full;
    }
}
