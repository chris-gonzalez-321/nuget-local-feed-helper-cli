using NugetLocalFeed.Cli.Core;
using NugetLocalFeed.Cli.Core.Models;
using NugetLocalFeed.Cli.Core.Validation;
using Spectre.Console;
using Spectre.Console.Cli;

namespace NugetLocalFeed.Cli.Commands;

public sealed class PublishSettings : CommandSettings
{
    [CommandArgument(0, "[PACKAGES]")]
    [System.ComponentModel.Description("Package names or menu numbers. Omit to pick from a menu.")]
    public string[] Packages { get; init; } = [];

    [CommandOption("--repo <NAME>")]
    [System.ComponentModel.Description("Imported repo settings to use (defaults to Environment in the global settings).")]
    public string? Repo { get; init; }

    [CommandOption("--all")]
    [System.ComponentModel.Description("Publish every package in the repo.")]
    public bool All { get; init; }

    [CommandOption("--no-build")]
    [System.ComponentModel.Description("Pack without building first (use the existing build output).")]
    public bool NoBuild { get; init; }
}

public sealed class PublishCommand : AsyncCommand<PublishSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, PublishSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var store = SettingsStore.CreateDefault();
            var global = store.LoadGlobal();

            var repoName = settings.Repo ?? global.Environment
                ?? throw new InvalidOperationException("No repo selected: pass --repo <name> or set Environment in the global settings.");
            var repo = store.LoadRepo(repoName);

            var selected = Select(repo.Packages!, settings);
            if (selected.Count == 0) return 1;

            AnsiConsole.MarkupLineInterpolated($"Build configuration [bold]{global.BuildConfiguration}[/], version [bold]{global.PackageVersion}[/], feed [bold]{global.LocalNugetFeedPath}[/]");

            var globalPackagesDir = PackagePublisher.GetGlobalPackagesDirectory();
            var failed = 0;

            foreach (var pkg in selected)
            {
                AnsiConsole.MarkupLineInterpolated($"\n[bold]{pkg.PkgName}[/]");
                var result = await PackagePublisher.PublishAsync(global, repo, pkg, settings.NoBuild, globalPackagesDir,
                    message => AnsiConsole.MarkupLineInterpolated($"  [grey]{message}[/]"));

                if (result.Succeeded)
                {
                    AnsiConsole.MarkupLineInterpolated($"  [green]{result.Message}[/]");
                    continue;
                }

                failed++;
                AnsiConsole.MarkupLineInterpolated($"  [red]{result.Message}[/]");
                foreach (var line in result.Details)
                {
                    AnsiConsole.MarkupLineInterpolated($"      {line}");
                }
            }

            AnsiConsole.MarkupLineInterpolated($"\n{selected.Count - failed} published, {failed} failed.");
            return failed == 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]Error:[/] {ex.Message}");
            return 1;
        }
    }

    private static List<PackageEntry> Select(List<PackageEntry> packages, PublishSettings settings)
    {
        if (settings.All) return packages;

        if (settings.Packages.Length > 0)
        {
            var picked = new List<PackageEntry>();
            foreach (var token in settings.Packages)
            {
                // Name first (a package could be named "2"), then 1-based menu number.
                var match = packages.FirstOrDefault(p => string.Equals(p.PkgName, token, StringComparison.OrdinalIgnoreCase))
                    ?? (int.TryParse(token, out var n) && n >= 1 && n <= packages.Count ? packages[n - 1] : null);

                if (match is null)
                {
                    AnsiConsole.MarkupLineInterpolated($"[red]Unknown package:[/] {token}");
                    return [];
                }

                if (!picked.Contains(match)) picked.Add(match);
            }

            return picked;
        }

        if (!AnsiConsole.Profile.Capabilities.Interactive)
        {
            AnsiConsole.MarkupLine("[red]No packages specified.[/] Pass package names/numbers or --all when not running in a terminal.");
            return [];
        }

        var prompt = new MultiSelectionPrompt<PackageEntry>()
            .Title("Select the package(s) to add to your local feed:")
            .NotRequired()
            .PageSize(20)
            .InstructionsText("[grey](space to toggle, enter to confirm)[/]")
            .UseConverter(p => Markup.Escape(p.PkgName!))
            .AddChoices(packages);

        return AnsiConsole.Prompt(prompt);
    }
}
