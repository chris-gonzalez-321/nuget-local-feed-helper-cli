using System.IO.Compression;
using System.Security.Cryptography;
using System.Xml.Linq;
using NuGet.Versioning;
using NugetLocalFeed.Cli.Core.Models;
using NugetLocalFeed.Cli.Core.Validation;

namespace NugetLocalFeed.Cli.Core;

public sealed record PublishResult(bool Succeeded, string Message, IReadOnlyList<string> Details);

/// <summary>Packs one project and publishes it to the local feed, replacing any previous copy of that version.</summary>
public static class PackagePublisher
{
    public static string GetGlobalPackagesDirectory()
    {
        var fromEnv = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (!string.IsNullOrWhiteSpace(fromEnv) && Path.IsPathFullyQualified(fromEnv)) return fromEnv;
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
    }

    public static async Task<PublishResult> PublishAsync(
        GlobalSettings global, RepoSettings repo, PackageEntry pkg, bool noBuild, string globalPackagesDir, Action<string>? log = null)
    {
        // Callers load settings through SettingsStore (validated), but this is the last line of
        // defence before values reach disk/subprocess calls, so re-check the essentials.
        if (InputValidator.CheckPackageId(pkg.PkgAssemblyName) is not null
            || InputValidator.CheckBuildConfiguration(global.BuildConfiguration) is not null
            || InputValidator.CheckVersion(global.PackageVersion, out var version) is not null)
        {
            return new(false, "Settings failed validation.", []);
        }

        var feedDir = Path.GetFullPath(InputValidator.ExpandHome(global.LocalNugetFeedPath!));
        var projectPath = RepoPathResolver.ResolveProjectPath(repo, pkg, global.BuildConfiguration!);

        var isProjectFile = File.Exists(projectPath);
        if (!isProjectFile && !Directory.Exists(projectPath))
            return new(false, $"Project not found: {projectPath}", []);

        var outputDir = Path.Combine(Path.GetTempPath(), "nuget-lf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDir);

        try
        {
            var packArgs = new List<string>
            {
                "pack", projectPath,
                "--configuration", global.BuildConfiguration!,
                "--output", outputDir,
                "--include-symbols",
                "-p:SymbolPackageFormat=symbols.nupkg",
                $"-p:Version={global.PackageVersion}",
            };
            if (noBuild) packArgs.Add("--no-build");

            log?.Invoke($"Packing {projectPath} ...");
            var pack = await DotnetCli.RunAsync(packArgs.ToArray());
            if (!pack.Succeeded) return new(false, $"dotnet pack failed (exit code {pack.ExitCode}).", pack.GetDiagnosticLines());

            // Same choice as the old script: the *.symbols.nupkg (includes pdbs for debugging).
            var nupkg = Directory.EnumerateFiles(outputDir, "*.symbols.nupkg").FirstOrDefault()
                ?? Directory.EnumerateFiles(outputDir, "*.nupkg").FirstOrDefault();
            if (nupkg is null) return new(false, "dotnet pack succeeded but produced no .nupkg (is the project packable?).", []);

            Directory.CreateDirectory(feedDir);

            // Remove the old copies so the new build is what gets restored / debugged.
            foreach (var root in new[] { feedDir, globalPackagesDir })
            {
                var removed = DeleteVersionFolder(root, pkg.PkgAssemblyName!, version!);
                if (removed is not null) log?.Invoke($"Deleted existing folder {removed}");
            }

            log?.Invoke($"Adding {Path.GetFileName(nupkg)} to {feedDir} ...");
            AddToFeed(nupkg, feedDir, pkg.PkgAssemblyName!, version!);
            return new(true, $"Published {pkg.PkgAssemblyName} {version!.ToNormalizedString()}", []);
        }
        finally
        {
            try { Directory.Delete(outputDir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// Adds a package to a folder feed in the hierarchical layout (id/version/id.version.nupkg,
    /// id.nuspec, id.version.nupkg.sha512) that `nuget add` produced. `dotnet nuget push` to a
    /// folder only writes a flat layout, which the old script's cleanup logic didn't expect.
    /// </summary>
    internal static string AddToFeed(string nupkgPath, string feedDir, string expectedId, NuGetVersion expectedVersion)
    {
        using var zip = ZipFile.OpenRead(nupkgPath);
        var nuspecEntry = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase) && !e.FullName.Contains('/'))
            ?? throw new InvalidOperationException("The package has no .nuspec at its root.");

        string nuspecText;
        using (var reader = new StreamReader(nuspecEntry.Open())) nuspecText = reader.ReadToEnd();

        var metadata = XDocument.Parse(nuspecText).Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "metadata")
            ?? throw new InvalidOperationException("The package .nuspec has no metadata.");
        string Field(string name) => metadata.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value.Trim() ?? "";

        var id = Field("id");
        if (!string.Equals(id, expectedId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"The packed package id is '{id}' but PkgAssemblyName is '{expectedId}'.");
        if (!NuGetVersion.TryParse(Field("version"), out var packed) || packed != expectedVersion)
            throw new InvalidOperationException($"The packed package version is '{Field("version")}', expected '{expectedVersion}'.");

        var idLower = id.ToLowerInvariant();
        var verLower = packed.ToNormalizedString().ToLowerInvariant();
        var destDir = Path.Combine(feedDir, idLower, verLower);
        Directory.CreateDirectory(destDir);

        var destNupkg = Path.Combine(destDir, $"{idLower}.{verLower}.nupkg");
        File.Copy(nupkgPath, destNupkg, overwrite: true);
        File.WriteAllText(Path.Combine(destDir, $"{idLower}.nuspec"), nuspecText);

        using var stream = File.OpenRead(destNupkg);
        File.WriteAllText(Path.Combine(destDir, $"{idLower}.{verLower}.nupkg.sha512"), Convert.ToBase64String(SHA512.HashData(stream)));
        return destDir;
    }

    /// <summary>
    /// Deletes {root}/{id}/{version} (lower-cased, as NuGet lays them out). Ids and versions are
    /// validated to be single path segments, and the target is re-checked to be exactly two
    /// levels below root before anything is deleted.
    /// </summary>
    internal static string? DeleteVersionFolder(string root, string packageId, NuGetVersion version)
    {
        var target = Path.GetFullPath(Path.Combine(root, packageId.ToLowerInvariant(), version.ToNormalizedString().ToLowerInvariant()));
        var relative = Path.GetRelativePath(root, target).Split(Path.DirectorySeparatorChar);
        if (relative.Length != 2 || relative.Any(s => s is "" or "." or ".."))
            throw new InvalidOperationException($"Refusing to delete unexpected path '{target}'.");

        if (!Directory.Exists(target)) return null;
        Directory.Delete(target, recursive: true);
        return target;
    }
}
