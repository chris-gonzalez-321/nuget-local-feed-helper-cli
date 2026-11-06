using System.Text.Json;
using NugetLocalFeed.Cli.Core.Models;

namespace NugetLocalFeed.Cli.Core.Validation;

public sealed record ParseResult<T>(T? Value, IReadOnlyList<string> Errors) where T : class
{
    public bool IsValid => Value is not null && Errors.Count == 0;
}

public sealed class SettingsValidationException(string source, IReadOnlyList<string> errors)
    : Exception($"{source} is invalid:{Environment.NewLine}  - {string.Join($"{Environment.NewLine}  - ", errors)}")
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

/// <summary>
/// Strict parsing + validation of the two settings JSON formats. Unknown properties,
/// duplicate properties, oversized or deeply-nested documents and any value failing
/// <see cref="InputValidator"/> are rejected. All problems are reported together.
/// </summary>
public static class SettingsParser
{
    public const long MaxFileBytes = 1_000_000;

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true, // PowerShell was case-insensitive
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
        MaxDepth = 8,
    };

    public static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Reads a user-supplied settings file with size / type checks.</summary>
    public static string ReadFile(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"File not found: {path}");
        if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Settings files must have a .json extension.");

        var length = new FileInfo(path).Length;
        if (length > MaxFileBytes) throw new InvalidOperationException($"File is too large ({length:N0} bytes; the limit is {MaxFileBytes:N0}).");

        return File.ReadAllText(path);
    }

    public static ParseResult<GlobalSettings> ParseGlobal(string json)
    {
        var (value, errors) = Deserialize<GlobalSettings>(json);
        if (value is null) return new(null, errors);

        Add(errors, nameof(value.BuildConfiguration), InputValidator.CheckBuildConfiguration(value.BuildConfiguration));
        Add(errors, nameof(value.PackageVersion), InputValidator.CheckVersion(value.PackageVersion, out _));
        Add(errors, nameof(value.LocalNugetFeedPath), InputValidator.CheckAbsolutePath(value.LocalNugetFeedPath, allowHome: true));
        if (value.Environment is not null)
            Add(errors, nameof(value.Environment), InputValidator.CheckRepoName(value.Environment));

        return new(value, errors);
    }

    public static ParseResult<RepoSettings> ParseRepo(string json)
    {
        var (value, errors) = Deserialize<RepoSettings>(json);
        if (value is null) return new(null, errors);

        Add(errors, nameof(value.PackagesSolutionDirectory), InputValidator.CheckAbsolutePath(value.PackagesSolutionDirectory, allowHome: true));

        if (value.Packages is null || value.Packages.Count == 0)
        {
            errors.Add("Packages: at least one package is required");
            return new(value, errors);
        }

        if (value.Packages.Count > InputValidator.MaxPackages)
        {
            errors.Add($"Packages: at most {InputValidator.MaxPackages} packages are allowed");
            return new(value, errors);
        }

        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < value.Packages.Count; i++)
        {
            var pkg = value.Packages[i];
            var where = $"Packages[{i}]";

            if (pkg is null)
            {
                errors.Add($"{where}: must be an object");
                continue;
            }

            Add(errors, $"{where}.PkgName", InputValidator.CheckPackageId(pkg.PkgName));
            Add(errors, $"{where}.PkgAssemblyName", InputValidator.CheckPackageId(pkg.PkgAssemblyName));

            if (pkg.PkgName is not null && !seenNames.Add(pkg.PkgName))
                errors.Add($"{where}.PkgName: '{pkg.PkgName}' is listed more than once");

            if (pkg.PkgDir is null && pkg.ProjectPath is null)
            {
                errors.Add($"{where}: either PkgDir or ProjectPath is required");
            }

            if (pkg.PkgDir is not null)
            {
                var error = InputValidator.CheckPathTemplate(pkg.PkgDir);
                if (error is null && !RepoPathResolver.DerivesProjectFolder(pkg.PkgDir) && pkg.ProjectPath is null)
                    error = $"must end with 'bin/{InputValidator.BuildConfigToken}' (or set ProjectPath)";
                Add(errors, $"{where}.PkgDir", error);
            }

            if (pkg.ProjectPath is not null)
            {
                var error = InputValidator.CheckPathTemplate(pkg.ProjectPath);
                if (error is null && !RepoPathResolver.IsAllowedProjectPath(pkg.ProjectPath))
                    error = "must be a folder or a .csproj/.fsproj/.vbproj file";
                Add(errors, $"{where}.ProjectPath", error);
            }
        }

        return new(value, errors);
    }

    private static (T? Value, List<string> Errors) Deserialize<T>(string json) where T : class
    {
        var errors = new List<string>();
        try
        {
            var value = JsonSerializer.Deserialize<T>(json, ReadOptions);
            if (value is null) errors.Add("document must be a JSON object");
            return (value, errors);
        }
        catch (JsonException ex)
        {
            errors.Add($"not valid for this format: {ex.Message}");
            return (null, errors);
        }
    }

    private static void Add(List<string> errors, string field, string? error)
    {
        if (error is not null) errors.Add($"{field}: {error}");
    }
}
