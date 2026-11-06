using NugetLocalFeed.Cli.Core;
using NugetLocalFeed.Cli.Core.Models;

namespace NugetLocalFeed.Cli.Tests;

public class RepoPathResolverTests
{
    private static readonly string Sln = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "sln"));
    private static RepoSettings Repo => new() { PackagesSolutionDirectory = Sln };

    [Fact]
    public void WindowsStylePkgDir_DerivesProjectFolder_OnAnyOs()
    {
        var pkg = new PackageEntry { PkgDir = "{PKGS_SLN_DIR}\\Core\\My.Core\\bin\\{BUILD_CONFIG}" };
        var path = RepoPathResolver.ResolveProjectPath(Repo, pkg, "Debug");
        Assert.Equal(Path.Combine(Sln, "Core", "My.Core"), path);
    }

    [Fact]
    public void ProjectPath_WinsOverPkgDir()
    {
        var pkg = new PackageEntry { PkgDir = "{PKGS_SLN_DIR}/a/bin/{BUILD_CONFIG}", ProjectPath = "{PKGS_SLN_DIR}/b/B.csproj" };
        Assert.Equal(Path.Combine(Sln, "b", "B.csproj"), RepoPathResolver.ResolveProjectPath(Repo, pkg, "Release"));
    }

    [Fact]
    public void RelativeTemplates_AreResolvedAgainstSolutionDir()
    {
        var pkg = new PackageEntry { ProjectPath = "src/Lib" };
        Assert.Equal(Path.Combine(Sln, "src", "Lib"), RepoPathResolver.ResolveProjectPath(Repo, pkg, "Debug"));
    }

    [Fact]
    public void PathOutsideSolutionDir_IsRefused()
    {
        // Parser rejects '..', but the resolver defends independently (e.g. a hand-edited stored file).
        var pkg = new PackageEntry { ProjectPath = "../outside" };
        Assert.Throws<InvalidOperationException>(() => RepoPathResolver.ResolveProjectPath(Repo, pkg, "Debug"));
    }

    [Fact]
    public void LiteralConfigMismatch_Throws()
    {
        var pkg = new PackageEntry { PkgDir = "{PKGS_SLN_DIR}/a/bin/Release" };
        Assert.Throws<InvalidOperationException>(() => RepoPathResolver.ResolveProjectPath(Repo, pkg, "Debug"));
    }
}
