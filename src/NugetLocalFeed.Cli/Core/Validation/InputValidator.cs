using System.Text.RegularExpressions;
using NuGet.Versioning;

namespace NugetLocalFeed.Cli.Core.Validation;

/// <summary>
/// Allow-list validation for every value that ends up in a file path or a subprocess argument.
/// Each Check* returns an error message, or null when the value is acceptable.
///
/// Subprocesses are always started with an argument list (never a shell string), so shell
/// injection is not possible; these checks additionally stop path traversal, option/argument
/// injection (values starting with '-'), MSBuild property tricks, and surprising expansions.
/// </summary>
public static partial class InputValidator
{
    public const string SolutionDirToken = "{PKGS_SLN_DIR}";
    public const string BuildConfigToken = "{BUILD_CONFIG}";

    public const int MaxPathLength = 1024;
    public const int MaxPackages = 500;

    // \z (not $) so a trailing newline can't sneak through.
    [GeneratedRegex(@"^[A-Za-z0-9](?:[A-Za-z0-9._-]{0,98}[A-Za-z0-9_-])?\z", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierRegex();

    [GeneratedRegex(@"^[A-Za-z0-9](?:[A-Za-z0-9_-]{0,62}[A-Za-z0-9])?\z", RegexOptions.CultureInvariant)]
    private static partial Regex RepoNameRegex();

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_-]{0,31}\z", RegexOptions.CultureInvariant)]
    private static partial Regex BuildConfigRegex();

    [GeneratedRegex(@"^[0-9A-Za-z.+-]{1,64}\z", RegexOptions.CultureInvariant)]
    private static partial Regex VersionCharsRegex();

    [GeneratedRegex(@"\{[^{}]*\}", RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();

    private static readonly HashSet<string> WindowsReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    private const string ForbiddenPathChars = "\"<>|*?%$`;";

    /// <summary>A NuGet package id / short package name; becomes a single folder name.</summary>
    public static string? CheckPackageId(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "is required";
        if (!IdentifierRegex().IsMatch(value))
            return "must be 1-100 characters: letters, digits, '.', '_' or '-' (starting with a letter/digit, not ending with '.')";
        if (value.Contains("..", StringComparison.Ordinal)) return "must not contain '..'";
        if (WindowsReservedNames.Contains(value.Split('.')[0])) return "is a reserved device name";
        return null;
    }

    /// <summary>Name of a stored repo settings file; becomes a file name.</summary>
    public static string? CheckRepoName(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "is required";
        if (!RepoNameRegex().IsMatch(value))
            return "must be 1-64 characters: letters, digits, '_' or '-' (starting and ending with a letter/digit)";
        if (WindowsReservedNames.Contains(value)) return "is a reserved device name";
        return null;
    }

    public static string? CheckBuildConfiguration(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "is required";
        return BuildConfigRegex().IsMatch(value)
            ? null
            : "must be 1-32 characters: a letter followed by letters, digits, '_' or '-' (e.g. Debug, Release)";
    }

    public static string? CheckVersion(string? value, out NuGetVersion? parsed)
    {
        parsed = null;
        if (string.IsNullOrEmpty(value)) return "is required";
        if (!VersionCharsRegex().IsMatch(value))
            return "must be 1-64 characters: letters, digits, '.', '+' or '-'";
        if (!NuGetVersion.TryParse(value, out parsed)) return "is not a valid NuGet version (e.g. 9.9.9-dev)";
        return null;
    }

    /// <summary>An absolute path (optionally starting with '~/') on the current OS.</summary>
    public static string? CheckAbsolutePath(string? value, bool allowHome)
    {
        var textError = CheckPathText(value, allowHome);
        if (textError is not null) return textError;

        var expanded = ExpandHome(value!);
        if (!Path.IsPathFullyQualified(expanded))
            return "must be an absolute path valid on this operating system";

        var full = Path.GetFullPath(expanded);
        if (string.Equals(Path.TrimEndingDirectorySeparator(full), Path.TrimEndingDirectorySeparator(Path.GetPathRoot(full) ?? ""), StringComparison.Ordinal))
            return "must not be a filesystem root";

        return null;
    }

    /// <summary>
    /// A path that may contain {PKGS_SLN_DIR} / {BUILD_CONFIG}. Anything else in braces is rejected
    /// so a typo can't silently produce a different path.
    /// </summary>
    public static string? CheckPathTemplate(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "is required";

        var withoutTokens = TokenRegex().Replace(value, m =>
            m.Value is SolutionDirToken or BuildConfigToken ? "x" : "\u0001");

        if (withoutTokens.Contains('\u0001')) return $"contains an unknown token (allowed: {SolutionDirToken}, {BuildConfigToken})";
        if (withoutTokens.Contains('{') || withoutTokens.Contains('}')) return "contains unbalanced '{' or '}'";

        return CheckPathText(withoutTokens, allowHome: false);
    }

    /// <summary>Replaces the allowed tokens; callers must have run CheckPathTemplate first.</summary>
    public static string ExpandTokens(string template, string solutionDir, string buildConfig) =>
        template.Replace(SolutionDirToken, solutionDir, StringComparison.Ordinal)
                .Replace(BuildConfigToken, buildConfig, StringComparison.Ordinal);

    public static string ExpandHome(string path)
    {
        if (path == "~" || path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, path.Length > 2 ? path[2..] : "");
        }

        return path;
    }

    /// <summary>Character-level rules shared by every path-like value.</summary>
    private static string? CheckPathText(string? value, bool allowHome)
    {
        if (string.IsNullOrWhiteSpace(value)) return "is required";
        if (value != value.Trim()) return "must not have leading or trailing whitespace";
        if (value.Length > MaxPathLength) return $"must be at most {MaxPathLength} characters";
        if (value[0] == '-') return "must not start with '-'";
        if (value.StartsWith(@"\\?\", StringComparison.Ordinal) || value.StartsWith(@"\\.\", StringComparison.Ordinal))
            return "must not be a Windows device path";
        if (value.Contains("://", StringComparison.Ordinal)) return "must be a local path, not a URL";

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsControl(c)) return "must not contain control characters";
            if (ForbiddenPathChars.Contains(c)) return $"must not contain '{c}'";
            if (c == '~' && !(allowHome && i == 0)) return "must not contain '~' (only a leading '~/' is supported in the feed path)";
            if (c == ':' && !(i == 1 && char.IsAsciiLetter(value[0]))) return "must not contain ':' except in a drive letter";
        }

        foreach (var segment in value.Split('/', '\\'))
        {
            if (segment == "..") return "must not contain '..' segments";
        }

        return null;
    }
}
