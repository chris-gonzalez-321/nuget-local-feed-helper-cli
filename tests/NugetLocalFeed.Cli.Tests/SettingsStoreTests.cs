using NugetLocalFeed.Cli.Core;
using NugetLocalFeed.Cli.Core.Models;
using NugetLocalFeed.Cli.Core.Validation;

namespace NugetLocalFeed.Cli.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory().FullName;
    private readonly string _abs = Path.GetFullPath(Path.GetTempPath());

    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public void Global_RoundTrips()
    {
        var store = new SettingsStore(_root);
        store.SaveGlobal(new GlobalSettings { BuildConfiguration = "Debug", PackageVersion = "1.0.0", LocalNugetFeedPath = _abs, Environment = "sob" });
        Assert.Equal("sob", store.LoadGlobal().Environment);
    }

    [Fact]
    public void Repo_RoundTrips_AndLists_AndRemoves()
    {
        var store = new SettingsStore(_root);
        store.SaveRepo("SOB", new RepoSettings { PackagesSolutionDirectory = _abs, Packages = [new() { PkgName = "A", PkgAssemblyName = "A", ProjectPath = "A" }] });

        Assert.Equal(["sob"], store.ListRepos());
        Assert.Single(store.LoadRepo("sob").Packages!);
        Assert.True(store.RemoveRepo("sob"));
        Assert.Empty(store.ListRepos());
    }

    [Theory]
    [InlineData("../evil")]
    [InlineData("a/b")]
    [InlineData("..")]
    public void RepoName_PathTraversal_Refused(string name)
    {
        var store = new SettingsStore(_root);
        var repo = new RepoSettings { PackagesSolutionDirectory = _abs, Packages = [new() { PkgName = "A", PkgAssemblyName = "A", ProjectPath = "A" }] };
        Assert.Throws<InvalidOperationException>(() => store.SaveRepo(name, repo));
        Assert.Throws<InvalidOperationException>(() => store.LoadRepo(name));
        Assert.Throws<InvalidOperationException>(() => store.RemoveRepo(name));
    }

    [Fact]
    public void TamperedStoredFile_IsRevalidatedOnLoad()
    {
        var store = new SettingsStore(_root);
        Directory.CreateDirectory(store.ReposDirectory);
        File.WriteAllText(Path.Combine(store.ReposDirectory, "bad.json"),
            $$"""{ "PackagesSolutionDirectory": "{{_abs.Replace("\\", "\\\\")}}", "Packages": [ { "PkgName": "A", "PkgAssemblyName": "../../x", "ProjectPath": "A" } ] }""");
        Assert.Throws<SettingsValidationException>(() => store.LoadRepo("bad"));
    }

    [Fact]
    public void Sources_AreRecordedPerKind_AndClearedOnRepoRemoval()
    {
        var store = new SettingsStore(_root);
        Assert.Null(store.GetGlobalSource());

        store.RecordGlobalSource("a/global.json");
        store.RecordRepoSource("SOB", "b/sob.json");
        store.SaveRepo("sob", new RepoSettings { PackagesSolutionDirectory = _abs, Packages = [new() { PkgName = "A", PkgAssemblyName = "A", ProjectPath = "A" }] });

        Assert.Equal(Path.GetFullPath("a/global.json"), store.GetGlobalSource());
        Assert.Equal(Path.GetFullPath("b/sob.json"), store.GetRepoSource("sob"));

        store.RemoveRepo("sob");
        Assert.Null(store.GetRepoSource("sob"));
        Assert.NotNull(store.GetGlobalSource());
    }

    [Fact]
    public void CorruptSourcesFile_IsIgnored()
    {
        var store = new SettingsStore(_root);
        File.WriteAllText(store.SourcesPath, "not json");
        Assert.Null(store.GetGlobalSource());
    }
}
