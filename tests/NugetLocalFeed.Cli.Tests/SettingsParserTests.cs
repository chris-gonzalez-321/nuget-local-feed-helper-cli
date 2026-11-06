using NugetLocalFeed.Cli.Core.Validation;

namespace NugetLocalFeed.Cli.Tests;

public class SettingsParserTests
{
    private static readonly string Abs = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "x")).Replace("\\", "\\\\");

    private static string Global(string extra = "") => $$"""
        { "BuildConfiguration": "Debug", "PackageVersion": "9.9.9-cg", "LocalNugetFeedPath": "{{Abs}}", "Environment": "sob" {{extra}} }
        """;

    [Fact]
    public void Global_Valid()
    {
        var r = SettingsParser.ParseGlobal(Global());
        Assert.True(r.IsValid, string.Join("; ", r.Errors));
        Assert.Equal("9.9.9-cg", r.Value!.PackageVersion);
    }

    [Fact]
    public void Global_PropertyNamesAreCaseInsensitive_AndCommentsAllowed()
    {
        var json = $$"""
            { // c
              "buildconfiguration": "Release", "PACKAGEVERSION": "1.0.0", "LocalNugetFeedPath": "{{Abs}}", }
            """;
        Assert.True(SettingsParser.ParseGlobal(json).IsValid);
    }

    [Fact]
    public void Global_UnknownProperty_Rejected() =>
        Assert.False(SettingsParser.ParseGlobal(Global(", \"Evil\": \"x\"")).IsValid);

    [Fact]
    public void Global_DuplicateProperty_Rejected() =>
        Assert.False(SettingsParser.ParseGlobal(Global(", \"BuildConfiguration\": \"Release\"")).IsValid);

    [Fact]
    public void Global_WrongType_Rejected() =>
        Assert.False(SettingsParser.ParseGlobal("""{ "BuildConfiguration": 5 }""").IsValid);

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("not json")]
    [InlineData("")]
    public void Global_NotAnObject_Rejected(string json) => Assert.False(SettingsParser.ParseGlobal(json).IsValid);

    [Fact]
    public void Global_ReportsAllErrorsAtOnce()
    {
        var r = SettingsParser.ParseGlobal("""{ "BuildConfiguration": "x y", "PackageVersion": "1;2", "LocalNugetFeedPath": "rel", "Environment": "../a" }""");
        Assert.Equal(4, r.Errors.Count);
    }

    private static string Repo(string pkgs) =>
        "{ \"PackagesSolutionDirectory\": \"" + Abs + "\", \"Packages\": [ " + pkgs + " ] }";

    [Fact]
    public void Repo_LegacyFormat_Valid()
    {
        var r = SettingsParser.ParseRepo(Repo("""
            { "PkgName": "Azure.Core", "PkgDir": "{PKGS_SLN_DIR}\\Azure\\X.Azure.Core\\bin\\{BUILD_CONFIG}", "PkgAssemblyName": "X.Azure.Core" }
            """));
        Assert.True(r.IsValid, string.Join("; ", r.Errors));
    }

    [Fact]
    public void Repo_ProjectPathInsteadOfPkgDir_Valid() =>
        Assert.True(SettingsParser.ParseRepo(Repo("""{ "PkgName": "A", "ProjectPath": "{PKGS_SLN_DIR}/A/A.csproj", "PkgAssemblyName": "A" }""")).IsValid);

    [Theory]
    [InlineData("""{ "PkgName": "A", "PkgDir": "{PKGS_SLN_DIR}/A/bin/{BUILD_CONFIG}", "PkgAssemblyName": "../../A" }""")]
    [InlineData("""{ "PkgName": "A;calc", "PkgDir": "{PKGS_SLN_DIR}/A/bin/{BUILD_CONFIG}", "PkgAssemblyName": "A" }""")]
    [InlineData("""{ "PkgName": "A", "PkgDir": "{PKGS_SLN_DIR}/../../A/bin/{BUILD_CONFIG}", "PkgAssemblyName": "A" }""")]
    [InlineData("""{ "PkgName": "A", "PkgDir": "{PKGS_SLN_DIR}/A/bin/Debug", "PkgAssemblyName": "A" }""")]
    [InlineData("""{ "PkgName": "A", "PkgAssemblyName": "A" }""")]
    [InlineData("""{ "PkgName": "A", "ProjectPath": "{PKGS_SLN_DIR}/A/run.sh", "PkgAssemblyName": "A" }""")]
    [InlineData("""{ "PkgName": "A", "ProjectPath": "{PKGS_SLN_DIR}/A", "PkgAssemblyName": "A", "Command": "rm -rf" }""")]
    public void Repo_BadPackage_Rejected(string pkg) => Assert.False(SettingsParser.ParseRepo(Repo(pkg)).IsValid);

    [Fact]
    public void Repo_DuplicateNames_Rejected() =>
        Assert.False(SettingsParser.ParseRepo(Repo("""
            { "PkgName": "A", "ProjectPath": "{PKGS_SLN_DIR}/A", "PkgAssemblyName": "A" },
            { "PkgName": "a", "ProjectPath": "{PKGS_SLN_DIR}/B", "PkgAssemblyName": "B" }
            """)).IsValid);

    [Fact]
    public void Repo_EmptyPackages_Rejected() => Assert.False(SettingsParser.ParseRepo(Repo("")).IsValid);

    [Fact]
    public void ReadFile_RejectsOversizedAndNonJson()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var big = Path.Combine(dir, "big.json");
            File.WriteAllText(big, new string(' ', (int)SettingsParser.MaxFileBytes + 1));
            Assert.Throws<InvalidOperationException>(() => SettingsParser.ReadFile(big));

            var txt = Path.Combine(dir, "a.txt");
            File.WriteAllText(txt, "{}");
            Assert.Throws<InvalidOperationException>(() => SettingsParser.ReadFile(txt));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Json_DeeplyNested_Rejected()
    {
        var nested = string.Concat(Enumerable.Repeat("[", 50)) + string.Concat(Enumerable.Repeat("]", 50));
        Assert.False(SettingsParser.ParseGlobal(nested).IsValid);
    }
}
