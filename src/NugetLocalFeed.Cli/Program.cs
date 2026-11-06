using NugetLocalFeed.Cli.Commands;
using NugetLocalFeed.Cli.Commands.Settings;
using System.Reflection;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Cli.Help;
using Spectre.Console.Rendering;

var version = typeof(Program).Assembly
    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
    .Split('+')[0] ?? "unknown";

var app = new CommandApp();

app.Configure(config =>
{
    config.SetApplicationName("nuget-lf");
    config.SetApplicationVersion(version);
    config.SetHelpProvider(new VersionedHelpProvider(config.Settings, version));

    config.AddCommand<PublishCommand>("publish")
        .WithDescription("Pack package project(s) and add them to your local NuGet feed.");

    config.AddBranch("settings", settings =>
    {
        settings.SetDescription("Import, validate, view, or remove settings files.");
        settings.AddBranch("import", import =>
        {
            import.SetDescription("Import a settings JSON file (validated before it is stored).");
            import.AddCommand<ImportGlobalCommand>("global").WithDescription("Import global settings (build config, version, feed path).");
            import.AddCommand<ImportRepoCommand>("repo").WithDescription("Import repo settings (solution directory and packages).");
        });
        settings.AddBranch("refresh", refresh =>
        {
            refresh.SetDescription("Re-import settings from the file that was last imported.");
            refresh.AddCommand<RefreshGlobalCommand>("global").WithDescription("Refresh global settings from the last imported file.");
            refresh.AddCommand<RefreshRepoCommand>("repo").WithDescription("Refresh one repo (or all) from the last imported file.");
        });
        settings.AddBranch("set", set =>
        {
            set.SetDescription("Update a single setting in place.");
            set.AddCommand<SetEnvironmentCommand>("environment").WithDescription("Set the default repo (Environment) in the global settings.");
            set.AddCommand<SetSolutionDirCommand>("solution-dir").WithDescription("Set PackagesSolutionDirectory for a repo.");
        });
        settings.AddCommand<ValidateCommand>("validate").WithDescription("Validate a settings file without importing it.");
        settings.AddCommand<ViewSettingsCommand>("view").WithDescription("Show the imported settings.");
        settings.AddCommand<RemoveRepoCommand>("remove-repo").WithDescription("Remove an imported repo settings file.");
    });
});

return await app.RunAsync(args);

internal sealed class VersionedHelpProvider(ICommandAppSettings settings, string version) : HelpProvider(settings)
{
    public override IEnumerable<IRenderable> GetHeader(ICommandModel model, ICommandInfo? command)
    {
        yield return new Markup($"[bold]{model.ApplicationName}[/] v{version}");
        yield return Text.NewLine;
    }
}
