using NugetLocalFeed.Cli.Core.Validation;

namespace NugetLocalFeed.Cli.Tests;

public class InputValidatorTests
{
    [Theory]
    [InlineData("IntegrationBroker.Azure.Core")]
    [InlineData("Logging.AppInsightsNLogTarget")]
    [InlineData("a")]
    [InlineData("My_Pkg-1")]
    public void PackageId_Valid(string id) => Assert.Null(InputValidator.CheckPackageId(id));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("../evil")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("..")]
    [InlineData("a..b")]
    [InlineData(".hidden")]
    [InlineData("trailing.")]
    [InlineData("-rf")]
    [InlineData("a b")]
    [InlineData("a;b")]
    [InlineData("a$(whoami)")]
    [InlineData("good\n")]
    [InlineData("CON")]
    [InlineData("nul.txt")]
    public void PackageId_Invalid(string? id) => Assert.NotNull(InputValidator.CheckPackageId(id));

    [Theory]
    [InlineData("Debug")]
    [InlineData("Release")]
    [InlineData("Debug-x64")]
    public void BuildConfiguration_Valid(string v) => Assert.Null(InputValidator.CheckBuildConfiguration(v));

    [Theory]
    [InlineData("Debug;Foo=1")]
    [InlineData("Debug /p:X=1")]
    [InlineData("-c")]
    [InlineData("")]
    [InlineData("Debug\n")]
    public void BuildConfiguration_Invalid(string v) => Assert.NotNull(InputValidator.CheckBuildConfiguration(v));

    [Theory]
    [InlineData("9.9.9-cg")]
    [InlineData("1.2.3")]
    [InlineData("1.2.3-beta.1+build5")]
    public void Version_Valid(string v) => Assert.Null(InputValidator.CheckVersion(v, out _));

    [Theory]
    [InlineData("1.0.0;Foo=bar")]
    [InlineData("1.0.0 -p:X=1")]
    [InlineData("-1.0.0")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("1.0.0\n")]
    [InlineData("$(Foo)")]
    public void Version_Invalid(string v) => Assert.NotNull(InputValidator.CheckVersion(v, out _));

    [Theory]
    [InlineData("sob")]
    [InlineData("my-repo_2")]
    public void RepoName_Valid(string v) => Assert.Null(InputValidator.CheckRepoName(v));

    [Theory]
    [InlineData("../x")]
    [InlineData("a.b")]
    [InlineData("a/b")]
    [InlineData("")]
    [InlineData("nul")]
    [InlineData("-x")]
    public void RepoName_Invalid(string v) => Assert.NotNull(InputValidator.CheckRepoName(v));

    [Theory]
    [InlineData("{PKGS_SLN_DIR}\\Core\\My.Core\\bin\\{BUILD_CONFIG}")]
    [InlineData("{PKGS_SLN_DIR}/Core/bin/{BUILD_CONFIG}")]
    [InlineData("Core/My.Core")]
    public void Template_Valid(string v) => Assert.Null(InputValidator.CheckPathTemplate(v));

    [Theory]
    [InlineData("{PKGS_SLN_DIR}/../../etc")]
    [InlineData("{PKGS_SLN_DIR}\\..\\x")]
    [InlineData("{OTHER}/x")]
    [InlineData("{PKGS_SLN_DIR/x")]
    [InlineData("{PKGS_SLN_DIR}/x;rm -rf /")]
    [InlineData("{PKGS_SLN_DIR}/$(whoami)")]
    [InlineData("{PKGS_SLN_DIR}/%TEMP%")]
    [InlineData("{PKGS_SLN_DIR}/`id`")]
    [InlineData("-rf")]
    [InlineData("{PKGS_SLN_DIR}/a|b")]
    [InlineData("{PKGS_SLN_DIR}/a\nb")]
    [InlineData("https://evil.example/x")]
    [InlineData("\\\\?\\C:\\x")]
    public void Template_Invalid(string v) => Assert.NotNull(InputValidator.CheckPathTemplate(v));

    [Fact]
    public void AbsolutePath_AcceptsRootedAndHome()
    {
        var abs = Path.Combine(Path.GetTempPath(), "feed");
        Assert.Null(InputValidator.CheckAbsolutePath(abs, allowHome: false));
        Assert.Null(InputValidator.CheckAbsolutePath("~/feed", allowHome: true));
        Assert.NotNull(InputValidator.CheckAbsolutePath("~/feed", allowHome: false));
    }

    [Theory]
    [InlineData("relative/path")]
    [InlineData("")]
    [InlineData("/")]
    [InlineData("/tmp/../etc")]
    [InlineData("/tmp/a;b")]
    [InlineData("/tmp/a\0b")]
    [InlineData("-/tmp")]
    public void AbsolutePath_Invalid(string v) => Assert.NotNull(InputValidator.CheckAbsolutePath(v, allowHome: true));
}
