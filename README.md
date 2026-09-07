# swArena Launcher

Windows launcher and differential patch updater for the swArena World of Warcraft 3.3.5a server.

The launcher checks the managed client files against the manifest published at `https://updates.swami.dev/manifest.json`. Only missing or changed files are downloaded. Personal folders and settings such as `WTF`, `Cache`, `Logs`, screenshots, addons, and account data are not managed or deleted.

## Using the compiled launcher

1. Download `swArena Launcher.exe` from the repository Releases page or from the official swArena download page.
2. Place the executable anywhere you want. It does not need to be inside the World of Warcraft directory.
3. Open `swArena Launcher.exe`.
4. Select **change game folder** and choose the folder that contains `Wow.exe`.
5. Wait for the file check and any required downloads to finish.
6. Click **play**.

The selected game folder is saved in `%LOCALAPPDATA%\swArena Launcher\settings.json`. On later launches, the folder picker will not open automatically. You can change the directory at any time with **change game folder**.

World of Warcraft must be closed while the launcher installs an update. The production configuration requires a successful update check before the game can start.

## Building the launcher from source

### Requirements

- Windows 10 or Windows 11, 64-bit.
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).
- Git.

### Build a standalone EXE

Open PowerShell and run:

```powershell
git clone https://github.com/gallardoS/swarena-launcher.git
cd swarena-launcher
dotnet restore .\SwArena.Launcher.sln
dotnet publish .\src\SwArena.Launcher -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o .\artifacts\launcher
```

The standalone executable will be created at:

```text
artifacts\launcher\swArena Launcher.exe
```

The public download only needs this `.exe`. The update URL and game executable name are embedded as defaults. An optional `launcher-settings.json` placed beside the executable can override them for development.

## Client patch releases

The managed files are listed in `managed-files.json` and stored under `client-patches/`. Generate a local differential release with:

```powershell
.\New-ClientRelease.ps1 -Version 0.1.0 -RealmAddress swarena.swami.dev
```

This creates `release/manifest.json` and immutable, versioned payloads under `release/files/<version>/`. The manifest must always be uploaded after every payload so launchers never observe a partially uploaded release.

To test the generated release locally:

```powershell
python -m http.server 8080 --directory .\release
```

Then temporarily point `src/SwArena.Launcher/launcher-settings.json` to `http://localhost:8080/manifest.json`.

## Automated workflows

- `build-launcher.yml` builds the standalone executable when launcher source files change on `main`. The EXE is available as a GitHub Actions artifact.
- `deploy-client-patches.yml` publishes a new version to Cloudflare R2 only when managed patches or release tooling change on `main`.

The R2 deployment requires a GitHub environment named `production` and these repository or environment secrets:

- `CLOUDFLARE_ACCOUNT_ID`
- `CLOUDFLARE_API_TOKEN`

The API token should have only the R2 write access required for the `swarena-updates` bucket. Never commit credentials, local Wrangler configuration, the original World of Warcraft client, or private server configuration.

## Repository layout

```text
client-patches/                Managed client MPQs
src/SwArena.Launcher/         WPF launcher source code
src/SwArena.Publisher/        Manifest and release generator
.github/workflows/            Build and R2 deployment automation
managed-files.json            Safe list of files controlled by the launcher
New-ClientRelease.ps1         Local release generator
Publish-ClientRelease.ps1     Manual R2 publisher
```

## Current limitations

- The launcher does not update its own executable yet. Users must download a newer EXE when the launcher itself changes.
- The update manifest is delivered over HTTPS and validates files with SHA-256, but it does not yet have an independent digital signature.
- Signing the launcher with a code-signing certificate is recommended before broad public distribution to reduce Microsoft SmartScreen warnings.
- A technical user can still start `Wow.exe` directly. Server-side validation is required if a minimum client version must be mandatory.
