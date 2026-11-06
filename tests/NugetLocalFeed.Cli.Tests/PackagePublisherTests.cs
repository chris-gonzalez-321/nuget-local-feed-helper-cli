using NuGet.Versioning;
using NugetLocalFeed.Cli.Core;
using NugetLocalFeed.Cli.Core.Models;

namespace NugetLocalFeed.Cli.Tests;

public sealed class PackagePublisherTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory().FullName;
    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public void DeleteVersionFolder_RemovesOnlyIdVersionFolder_CaseInsensitiveLayout()
    {
        var target = Path.Combine(_root, "my.pkg", "1.0.0-dev");
        Directory.CreateDirectory(target);
        var sibling = Path.Combine(_root, "my.pkg", "2.0.0");
        Directory.CreateDirectory(sibling);

        var removed = PackagePublisher.DeleteVersionFolder(_root, "My.Pkg", NuGetVersion.Parse("1.0.0-DEV"));

        Assert.NotNull(removed);
        Assert.False(Directory.Exists(target));
        Assert.True(Directory.Exists(sibling));
    }

    [Fact]
    public void DeleteVersionFolder_MissingFolder_ReturnsNull() =>
        Assert.Null(PackagePublisher.DeleteVersionFolder(_root, "Nope", NuGetVersion.Parse("1.0.0")));

    [Fact]
    public void DeleteVersionFolder_TraversalId_Refused() =>
        Assert.Throws<InvalidOperationException>(() =>
            PackagePublisher.DeleteVersionFolder(_root, "..", NuGetVersion.Parse("1.0.0")));

    [Fact]
    public async Task EndToEnd_PacksAndPublishesToLocalFeed_ReplacingOldVersion()
    {
        var sln = Path.Combine(_root, "sln");
        var proj = Path.Combine(sln, "Core", "Demo.Core");
        Directory.CreateDirectory(proj);
        File.WriteAllText(Path.Combine(proj, "Demo.Core.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(proj, "Class1.cs"), "namespace Demo.Core; public class Class1 {}");

        var feed = Path.Combine(_root, "feed");
        var globalPkgs = Path.Combine(_root, "globalpkgs");
        var stale = Path.Combine(globalPkgs, "demo.core", "9.9.9-cg");
        Directory.CreateDirectory(stale);
        File.WriteAllText(Path.Combine(stale, "old.txt"), "old");

        var global = new GlobalSettings { BuildConfiguration = "Debug", PackageVersion = "9.9.9-cg", LocalNugetFeedPath = feed };
        var repo = new RepoSettings { PackagesSolutionDirectory = sln };
        var pkg = new PackageEntry { PkgName = "Core", PkgAssemblyName = "Demo.Core", PkgDir = "{PKGS_SLN_DIR}\\Core\\Demo.Core\\bin\\{BUILD_CONFIG}" };

        var result = await PackagePublisher.PublishAsync(global, repo, pkg, noBuild: false, globalPkgs);

        Assert.True(result.Succeeded, result.Message + string.Join("\n", result.Details));
        Assert.True(Directory.Exists(Path.Combine(feed, "demo.core", "9.9.9-cg")));
        Assert.False(File.Exists(Path.Combine(stale, "old.txt")));

        // Publishing again must replace the existing version rather than fail.
        var again = await PackagePublisher.PublishAsync(global, repo, pkg, noBuild: false, globalPkgs);
        Assert.True(again.Succeeded, again.Message + string.Join("\n", again.Details));
    }
}
