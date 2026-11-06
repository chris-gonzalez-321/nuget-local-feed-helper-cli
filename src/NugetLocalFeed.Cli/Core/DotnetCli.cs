using System.Diagnostics;

namespace NugetLocalFeed.Cli.Core;

public sealed record DotnetCliResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Succeeded => ExitCode == 0;

    /// <summary>
    /// Failure diagnostics for display. `dotnet` writes its actual NuGet/MSBuild error text
    /// (NU1102, NU1101, etc.) to standard output rather than standard error, so this pulls the
    /// "error"-prefixed lines out of the combined output; falls back to the full output when
    /// nothing matches that pattern.
    /// </summary>
    public IReadOnlyList<string> GetDiagnosticLines()
    {
        var combinedLines = $"{StandardOutput}\n{StandardError}"
            .Split('\n')
            .Select(line => line.TrimEnd('\r').Trim())
            .Where(line => line.Length > 0)
            .ToList();

        var errorLines = combinedLines
            .Where(line => line.Contains("error", StringComparison.OrdinalIgnoreCase))
            .ToList();

        return errorLines.Count > 0 ? errorLines : combinedLines;
    }
}

/// <summary>Runs the `dotnet` CLI as a subprocess and captures its output.</summary>
public static class DotnetCli
{
    public static async Task<DotnetCliResult> RunAsync(params string[] arguments)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = psi };
        process.Start();

        var stdOutTask = process.StandardOutput.ReadToEndAsync();
        var stdErrTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();
        var stdOut = await stdOutTask;
        var stdErr = await stdErrTask;

        return new DotnetCliResult(process.ExitCode, stdOut, stdErr);
    }
}
