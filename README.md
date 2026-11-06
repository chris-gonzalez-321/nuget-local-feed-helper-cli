# nuget-lf — NuGet Local Feed Helper CLI

Packs a .NET library project and adds it to a local NuGet feed folder, replacing the old
`add-pkg-to-local-feed.ps1` script. Works on Windows, macOS and Linux (no `nuget.exe` needed).

## Install

```bash
dotnet pack src/NugetLocalFeed.Cli -c Release -o ./nupkg
dotnet tool install --global --add-source ./nupkg NugetLocalFeed.Cli
```

(To update: bump `<Version>` in the csproj, repack, then `dotnet tool update --global --add-source ./nupkg NugetLocalFeed.Cli`.)

## Usage

```bash
nuget-lf settings import global settings.user.json
nuget-lf settings import repo settings.sob.json          # stored as "sob" (or pass --name)
nuget-lf settings view                                   # global settings + list of repos
nuget-lf settings view <repo-name>                       # one repo: solution dir, source file, packages
nuget-lf settings refresh global                         # re-import from the last file imported
nuget-lf settings refresh repo [name]                    # same for one repo (or all if omitted)
nuget-lf settings set environment <repo-name>            # change the default repo in global settings
nuget-lf settings set solution-dir <repo-name> <path>    # change a repo's PackagesSolutionDirectory
nuget-lf settings validate <file.json> --type global|repo  # check a file without importing
nuget-lf settings remove-repo <name>

nuget-lf publish                       # pick package(s) from a menu
nuget-lf publish Core Data 3           # by name or 1-based menu number
nuget-lf publish --all [--repo sob] [--no-build]
```

For each package, `publish` runs `dotnet pack` (with symbols, at the configured version),
deletes any existing copy of that version from the local feed **and** the global packages
cache (`NUGET_PACKAGES` or `~/.nuget/packages`) so stale files aren't used while debugging,
then adds the new package to the feed as `<feed>/<id>/<version>/`.

`refresh` re-reads the file path recorded at import time and re-validates it; if the file is now
invalid, the stored settings are left unchanged. `set ...` edits the stored copy only, so a later
`refresh` overwrites it with whatever is in the source file.

Imported settings live in the OS app-data folder (`NUGET_LF_HOME` overrides it).

## Settings formats

Same shape as the PowerShell script. Property names are case-insensitive; `//` comments and
trailing commas are allowed.

**Global** (`settings.user.json`):

```json
{
  "BuildConfiguration": "Debug",
  "PackageVersion": "9.9.9-cg",
  "LocalNugetFeedPath": "~/nuget-local-feed",
  "Environment": "sob"
}
```

`Environment` is the default repo name; `--repo` overrides it. `LocalNugetFeedPath` must be absolute
for the current OS (a leading `~/` is expanded).

**Repo** (`settings.<name>.json`):

```json
{
  "PackagesSolutionDirectory": "/Users/me/Code/integration_broker_packages",
  "Packages": [
    {
      "PkgName": "Core",
      "PkgAssemblyName": "IntegrationBroker.Core",
      "PkgDir": "{PKGS_SLN_DIR}/Core/IntegrationBroker.Core/bin/{BUILD_CONFIG}"
    },
    {
      "PkgName": "Data",
      "PkgAssemblyName": "IntegrationBroker.Data",
      "ProjectPath": "{PKGS_SLN_DIR}/Data/IntegrationBroker.Data/IntegrationBroker.Data.csproj"
    }
  ]
}
```

- `PkgAssemblyName` is the NuGet package id.
- Because the tool now packs the project itself, it needs the project location: either keep the
  legacy `PkgDir` (must end in `bin/{BUILD_CONFIG}`; the project folder is the parent of `bin`) or
  set `ProjectPath` (a folder or `.csproj/.fsproj/.vbproj`). `ProjectPath` wins if both are present.
- Tokens: `{PKGS_SLN_DIR}`, `{BUILD_CONFIG}`. Either slash style works on every OS.
- Absolute paths in files written for Windows (`C:\...`) must be changed to the Mac/Linux path.

## Validation / security

Files are validated on import **and** again every time they're loaded. All errors are listed at once.

- Strict JSON: max 1 MB, max depth 8, `.json` only, no unknown or duplicate properties, correct types.
- Package ids / names: letters, digits, `.`, `_`, `-` only (they become folder names; no `/`, `\`, `..`, device names).
- Version: valid NuGet version, restricted charset (it's passed to MSBuild as `-p:Version=`).
- Build configuration: `^[A-Za-z][A-Za-z0-9_-]{0,31}$`.
- Paths: no control chars, `; | & < > * ? % $ `` ` ``, `..` segments, URLs, device paths, or leading `-`;
  unknown `{tokens}` rejected; repo paths must resolve inside `PackagesSolutionDirectory`.
- Subprocesses are started with argument lists, never through a shell. Deletions only ever target
  `<root>/<id>/<version>` (checked to be exactly two levels below the root).
- Imported files are re-serialized from the validated model, not copied verbatim.

## Development

```bash
dotnet build
dotnet test
dotnet run --project src/NugetLocalFeed.Cli -- <command> [options]
```
