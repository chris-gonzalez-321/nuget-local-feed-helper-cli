using NugetLocalFeed.Cli.Core;
using NugetLocalFeed.Cli.Core.Validation;
using Spectre.Console;
using Spectre.Console.Cli;

namespace NugetLocalFeed.Cli.Commands.Settings;

public enum SettingsKind { Global, Repo }

public sealed class ValidateSettings : SettingsFileSettings
{
    [CommandOption("--type <TYPE>")]
    [System.ComponentModel.Description("global or repo")]
    public required SettingsKind Type { get; init; }
}

public sealed class RemoveRepoSettings : CommandSettings
{
    [CommandArgument(0, "<NAME>")]
    public required string Name { get; init; }
}

public sealed class ValidateCommand : Command<ValidateSettings>
{
    protected override int Execute(CommandContext context, ValidateSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var json = SettingsParser.ReadFile(settings.File);
            var errors = settings.Type == SettingsKind.Global
                ? SettingsParser.ParseGlobal(json).Errors
                : SettingsParser.ParseRepo(json).Errors;

            if (errors.Count > 0)
            {
                SettingsOutput.PrintErrors(settings.File, errors);
                return 1;
            }

            AnsiConsole.MarkupLine("[green]Valid.[/]");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]Error:[/] {ex.Message}");
            return 1;
        }
    }
}

public sealed class ViewSettingsCommand : Command<EmptyCommandSettings>
{
    protected override int Execute(CommandContext context, EmptyCommandSettings settings, CancellationToken cancellationToken)
    {
        var store = SettingsStore.CreateDefault();
        AnsiConsole.MarkupLineInterpolated($"Settings folder: {store.RootDirectory}\n");

        try
        {
            var global = store.LoadGlobal();
            var table = new Table().AddColumn("Global setting").AddColumn("Value");
            table.AddRow("BuildConfiguration", Markup.Escape(global.BuildConfiguration!));
            table.AddRow("PackageVersion", Markup.Escape(global.PackageVersion!));
            table.AddRow("LocalNugetFeedPath", Markup.Escape(global.LocalNugetFeedPath!));
            table.AddRow("Environment", Markup.Escape(global.Environment ?? "(none)"));
            AnsiConsole.Write(table);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLineInterpolated($"[yellow]Global settings:[/] {ex.Message}");
        }

        var repos = store.ListRepos();
        AnsiConsole.MarkupLine($"\nRepos: {(repos.Count == 0 ? "(none imported)" : "")}");
        foreach (var repo in repos)
        {
            AnsiConsole.MarkupLineInterpolated($"  - {repo}");
        }

        return 0;
    }
}

public sealed class RemoveRepoCommand : Command<RemoveRepoSettings>
{
    protected override int Execute(CommandContext context, RemoveRepoSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var removed = SettingsStore.CreateDefault().RemoveRepo(settings.Name);
            if (removed) AnsiConsole.MarkupLineInterpolated($"[green]Removed repo[/] '{settings.Name}'");
            else AnsiConsole.MarkupLineInterpolated($"[yellow]No repo named[/] '{settings.Name}'");
            return removed ? 0 : 1;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]Error:[/] {ex.Message}");
            return 1;
        }
    }
}
