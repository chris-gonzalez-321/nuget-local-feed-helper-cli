using NugetLocalFeed.Cli.Core;
using NugetLocalFeed.Cli.Core.Models;
using NugetLocalFeed.Cli.Core.Validation;
using Spectre.Console;
using Spectre.Console.Cli;

namespace NugetLocalFeed.Cli.Commands.Settings;

public sealed class RefreshRepoSettings : CommandSettings
{
    [CommandArgument(0, "[NAME]")]
    [System.ComponentModel.Description("Repo to refresh. Omit to refresh every imported repo.")]
    public string? Name { get; init; }
}

public sealed class SetEnvironmentSettings : CommandSettings
{
    [CommandArgument(0, "<NAME>")]
    [System.ComponentModel.Description("Name of an imported repo to use by default.")]
    public required string Name { get; init; }
}

public sealed class SetSolutionDirSettings : CommandSettings
{
    [CommandArgument(0, "<REPO>")]
    public required string Repo { get; init; }

    [CommandArgument(1, "<PATH>")]
    [System.ComponentModel.Description("New PackagesSolutionDirectory (absolute path; '~/' allowed).")]
    public required string Path { get; init; }
}

/// <summary>Re-imports global settings from the file that was last imported.</summary>
public sealed class RefreshGlobalCommand : Command<EmptyCommandSettings>
{
    protected override int Execute(CommandContext context, EmptyCommandSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var store = SettingsStore.CreateDefault();
            var source = store.GetGlobalSource()
                ?? throw new InvalidOperationException("No source file recorded. Run: nuget-lf settings import global <file.json>");

            var result = SettingsParser.ParseGlobal(SettingsParser.ReadFile(source));
            if (!result.IsValid)
            {
                SettingsOutput.PrintErrors(source, result.Errors);
                AnsiConsole.MarkupLine("[yellow]Stored settings were left unchanged.[/]");
                return 1;
            }

            store.SaveGlobal(result.Value!);
            AnsiConsole.MarkupLineInterpolated($"[green]Refreshed global settings[/] from {source}");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]Error:[/] {ex.Message}");
            return 1;
        }
    }
}

/// <summary>Re-imports one (or every) repo's settings from the file that was last imported for it.</summary>
public sealed class RefreshRepoCommand : Command<RefreshRepoSettings>
{
    protected override int Execute(CommandContext context, RefreshRepoSettings settings, CancellationToken cancellationToken)
    {
        var store = SettingsStore.CreateDefault();
        IReadOnlyList<string> names;
        try
        {
            names = settings.Name is null ? store.ListRepos() : [settings.Name];
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]Error:[/] {ex.Message}");
            return 1;
        }

        if (names.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No repos imported yet.[/]");
            return 1;
        }

        var failed = 0;
        foreach (var name in names)
        {
            try
            {
                var source = store.GetRepoSource(name)
                    ?? throw new InvalidOperationException("no source file recorded; re-import it with `settings import repo`");

                var result = SettingsParser.ParseRepo(SettingsParser.ReadFile(source));
                if (!result.IsValid)
                {
                    failed++;
                    SettingsOutput.PrintErrors(source, result.Errors);
                    AnsiConsole.MarkupLineInterpolated($"[yellow]'{name}' left unchanged.[/]");
                    continue;
                }

                store.SaveRepo(name, result.Value!);
                AnsiConsole.MarkupLineInterpolated($"[green]Refreshed repo[/] '{name}' from {source}");
            }
            catch (Exception ex)
            {
                failed++;
                AnsiConsole.MarkupLineInterpolated($"[red]'{name}':[/] {ex.Message}");
            }
        }

        return failed == 0 ? 0 : 1;
    }
}

public sealed class SetEnvironmentCommand : Command<SetEnvironmentSettings>
{
    protected override int Execute(CommandContext context, SetEnvironmentSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var error = InputValidator.CheckRepoName(settings.Name);
            if (error is not null)
            {
                AnsiConsole.MarkupLineInterpolated($"[red]Invalid environment '{settings.Name}':[/] {error}");
                return 1;
            }

            var store = SettingsStore.CreateDefault();
            var global = store.LoadGlobal();
            store.SaveGlobal(new GlobalSettings
            {
                BuildConfiguration = global.BuildConfiguration,
                PackageVersion = global.PackageVersion,
                LocalNugetFeedPath = global.LocalNugetFeedPath,
                Environment = settings.Name.ToLowerInvariant(),
            });

            AnsiConsole.MarkupLineInterpolated($"[green]Environment set to[/] '{settings.Name.ToLowerInvariant()}'");
            if (!store.RepoExists(settings.Name))
            {
                AnsiConsole.MarkupLine("[yellow]Note: no repo with that name is imported yet.[/]");
            }

            AnsiConsole.MarkupLine("[grey]`settings refresh global` will overwrite this with the value in the source file.[/]");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]Error:[/] {ex.Message}");
            return 1;
        }
    }
}

public sealed class SetSolutionDirCommand : Command<SetSolutionDirSettings>
{
    protected override int Execute(CommandContext context, SetSolutionDirSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var error = InputValidator.CheckAbsolutePath(settings.Path, allowHome: true);
            if (error is not null)
            {
                AnsiConsole.MarkupLineInterpolated($"[red]Invalid PackagesSolutionDirectory:[/] {error}");
                return 1;
            }

            var store = SettingsStore.CreateDefault();
            var repo = store.LoadRepo(settings.Repo);
            store.SaveRepo(settings.Repo, new RepoSettings
            {
                PackagesSolutionDirectory = settings.Path,
                Packages = repo.Packages,
            });

            AnsiConsole.MarkupLineInterpolated($"[green]Updated[/] '{settings.Repo.ToLowerInvariant()}' PackagesSolutionDirectory -> {settings.Path}");
            if (!Directory.Exists(Path.GetFullPath(InputValidator.ExpandHome(settings.Path))))
            {
                AnsiConsole.MarkupLine("[yellow]Note: that directory doesn't exist on this machine.[/]");
            }

            AnsiConsole.MarkupLine("[grey]`settings refresh repo` will overwrite this with the value in the source file.[/]");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]Error:[/] {ex.Message}");
            return 1;
        }
    }
}
