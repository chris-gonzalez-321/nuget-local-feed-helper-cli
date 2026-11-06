using NugetLocalFeed.Cli.Core;
using NugetLocalFeed.Cli.Core.Validation;
using Spectre.Console;
using Spectre.Console.Cli;

namespace NugetLocalFeed.Cli.Commands.Settings;

public class SettingsFileSettings : CommandSettings
{
    [CommandArgument(0, "<FILE>")]
    public required string File { get; init; }
}

public sealed class ImportRepoSettings : SettingsFileSettings
{
    [CommandOption("--name <NAME>")]
    [System.ComponentModel.Description("Name to store it under. Defaults to <x> for 'settings.<x>.json', else the file name.")]
    public string? Name { get; init; }
}

internal static class SettingsOutput
{
    public static void PrintErrors(string source, IReadOnlyList<string> errors)
    {
        AnsiConsole.MarkupLineInterpolated($"[red]{source} is invalid:[/]");
        foreach (var error in errors)
        {
            AnsiConsole.MarkupLineInterpolated($"    - {error}");
        }
    }
}

public sealed class ImportGlobalCommand : Command<SettingsFileSettings>
{
    protected override int Execute(CommandContext context, SettingsFileSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var result = SettingsParser.ParseGlobal(SettingsParser.ReadFile(settings.File));
            if (!result.IsValid)
            {
                SettingsOutput.PrintErrors(settings.File, result.Errors);
                return 1;
            }

            var store = SettingsStore.CreateDefault();
            store.SaveGlobal(result.Value!);
            AnsiConsole.MarkupLineInterpolated($"[green]Imported global settings[/] -> {store.GlobalPath}");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]Error:[/] {ex.Message}");
            return 1;
        }
    }
}

public sealed class ImportRepoCommand : Command<ImportRepoSettings>
{
    protected override int Execute(CommandContext context, ImportRepoSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var name = settings.Name ?? DeriveName(settings.File);
            var nameError = InputValidator.CheckRepoName(name);
            if (nameError is not null)
            {
                AnsiConsole.MarkupLineInterpolated($"[red]Invalid repo name '{name}':[/] {nameError}. Pass --name <NAME>.");
                return 1;
            }

            var result = SettingsParser.ParseRepo(SettingsParser.ReadFile(settings.File));
            if (!result.IsValid)
            {
                SettingsOutput.PrintErrors(settings.File, result.Errors);
                return 1;
            }

            var store = SettingsStore.CreateDefault();
            store.SaveRepo(name, result.Value!);
            AnsiConsole.MarkupLineInterpolated($"[green]Imported repo settings[/] '{name.ToLowerInvariant()}' ({result.Value!.Packages!.Count} packages)");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]Error:[/] {ex.Message}");
            return 1;
        }
    }

    internal static string DeriveName(string file)
    {
        var stem = Path.GetFileNameWithoutExtension(file);
        return stem.StartsWith("settings.", StringComparison.OrdinalIgnoreCase) ? stem["settings.".Length..] : stem;
    }
}
