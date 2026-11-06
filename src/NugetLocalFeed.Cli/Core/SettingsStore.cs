using System.Text.Json;
using NugetLocalFeed.Cli.Core.Models;
using NugetLocalFeed.Cli.Core.Validation;

namespace NugetLocalFeed.Cli.Core;

/// <summary>
/// Persists imported settings in the per-user app-data folder (override with NUGET_LF_HOME).
/// Only validated models are written (re-serialized, never copied byte-for-byte), and they are
/// validated again on load in case the files were edited by hand.
/// </summary>
public sealed class SettingsStore(string rootDirectory)
{
    public string RootDirectory { get; } = rootDirectory;
    public string GlobalPath => Path.Combine(RootDirectory, "global.json");
    public string SourcesPath => Path.Combine(RootDirectory, "sources.json");
    public string ReposDirectory => Path.Combine(RootDirectory, "repos");

    public static SettingsStore CreateDefault()
    {
        var overrideDir = Environment.GetEnvironmentVariable("NUGET_LF_HOME");
        var root = !string.IsNullOrWhiteSpace(overrideDir)
            ? overrideDir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "nuget-lf");
        return new SettingsStore(Path.GetFullPath(root));
    }

    public void SaveGlobal(GlobalSettings settings) =>
        WriteAtomic(GlobalPath, JsonSerializer.Serialize(settings, SettingsParser.WriteOptions));

    public GlobalSettings LoadGlobal()
    {
        if (!File.Exists(GlobalPath))
            throw new InvalidOperationException("No global settings imported yet. Run: nuget-lf settings import global <file.json>");

        var result = SettingsParser.ParseGlobal(File.ReadAllText(GlobalPath));
        return result.IsValid ? result.Value! : throw new SettingsValidationException(GlobalPath, result.Errors);
    }

    /// <summary>Like <see cref="LoadGlobal"/> but returns null when missing or invalid.</summary>
    public GlobalSettings? TryLoadGlobal()
    {
        try { return LoadGlobal(); }
        catch (Exception ex) when (ex is InvalidOperationException or SettingsValidationException) { return null; }
    }

    public void SaveRepo(string name, RepoSettings settings) =>
        WriteAtomic(RepoPath(name), JsonSerializer.Serialize(settings, SettingsParser.WriteOptions));

    public RepoSettings LoadRepo(string name)
    {
        var path = RepoPath(name);
        if (!File.Exists(path))
            throw new InvalidOperationException($"No repo settings named '{name}'. Run: nuget-lf settings import repo <file.json> --name {name}");

        var result = SettingsParser.ParseRepo(File.ReadAllText(path));
        return result.IsValid ? result.Value! : throw new SettingsValidationException(path, result.Errors);
    }

    public IReadOnlyList<string> ListRepos() =>
        !Directory.Exists(ReposDirectory)
            ? []
            : Directory.EnumerateFiles(ReposDirectory, "*.json")
                .Select(f => Path.GetFileNameWithoutExtension(f))
                .Where(n => InputValidator.CheckRepoName(n) is null)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();

    public bool RemoveRepo(string name)
    {
        var path = RepoPath(name);
        if (!File.Exists(path)) return false;
        File.Delete(path);
        RecordSource(RepoSourceKey(name), null);
        return true;
    }

    public bool RepoExists(string name) => File.Exists(RepoPath(name));

    // --- "last uploaded file" tracking, used by `settings refresh` ---------------------------

    private const string GlobalSourceKey = "global";
    private static string RepoSourceKey(string name) => "repo:" + name.ToLowerInvariant();

    public void RecordGlobalSource(string file) => RecordSource(GlobalSourceKey, Path.GetFullPath(file));
    public void RecordRepoSource(string name, string file) => RecordSource(RepoSourceKey(name), Path.GetFullPath(file));
    public string? GetGlobalSource() => ReadSources().GetValueOrDefault(GlobalSourceKey);
    public string? GetRepoSource(string name) => ReadSources().GetValueOrDefault(RepoSourceKey(name));

    private Dictionary<string, string> ReadSources()
    {
        try
        {
            return File.Exists(SourcesPath)
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(SourcesPath)) ?? []
                : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private void RecordSource(string key, string? file)
    {
        var sources = ReadSources();
        if (file is null) sources.Remove(key); else sources[key] = file;
        WriteAtomic(SourcesPath, JsonSerializer.Serialize(sources, SettingsParser.WriteOptions));
    }

    private string RepoPath(string name)
    {
        var error = InputValidator.CheckRepoName(name);
        if (error is not null) throw new InvalidOperationException($"Invalid repo name '{name}': {error}");
        return Path.Combine(ReposDirectory, name.ToLowerInvariant() + ".json");
    }

    private static void WriteAtomic(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temp, content);
        File.Move(temp, path, overwrite: true);
    }
}
