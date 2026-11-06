namespace NugetLocalFeed.Cli.Core.Models;

/// <summary>
/// Global (per-user) settings. Property names match the original PowerShell script's
/// settings.user.json so existing files keep working.
/// </summary>
public sealed class GlobalSettings
{
    public string? BuildConfiguration { get; init; }
    public string? PackageVersion { get; init; }
    public string? LocalNugetFeedPath { get; init; }

    /// <summary>Name of the default repo settings to use (see `settings import repo`).</summary>
    public string? Environment { get; init; }
}

/// <summary>One package that can be packed and published from a repo.</summary>
public sealed class PackageEntry
{
    /// <summary>Short name shown in the menu and accepted on the command line.</summary>
    public string? PkgName { get; init; }

    /// <summary>The NuGet package id (used for the feed / global-packages folder names).</summary>
    public string? PkgAssemblyName { get; init; }

    /// <summary>
    /// Legacy field from the PowerShell script: the project's build output folder, e.g.
    /// {PKGS_SLN_DIR}/Core/My.Core/bin/{BUILD_CONFIG}. The project folder is derived from it.
    /// </summary>
    public string? PkgDir { get; init; }

    /// <summary>Optional explicit project folder or project file; wins over PkgDir.</summary>
    public string? ProjectPath { get; init; }
}

/// <summary>Repo settings (settings.&lt;env&gt;.json in the PowerShell script).</summary>
public sealed class RepoSettings
{
    public string? PackagesSolutionDirectory { get; init; }
    public List<PackageEntry>? Packages { get; init; }
}
