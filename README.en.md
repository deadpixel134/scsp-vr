[한국어](README.md) | [English](README.en.md) | [日本語](README.ja.md)

# SCSP VR

An unofficial OpenXR VR mod for the DMM PC version of **THE iDOLM@STER Shiny Colors: Song for Prism**.

Creator: [@TBluebox12](https://x.com/TBluebox12)\
Arca.live Virtual Reality channel: [Virtual Reality channel](https://arca.live/b/vrshits)\
Support: [buymeacoffee.com/vrshits](https://buymeacoffee.com/vrshits)\
[Report an issue](https://github.com/deadpixel134/scsp-vr/issues)

## Current release

The current public build is the [`v0.1.1-preview.1`](https://github.com/deadpixel134/scsp-vr/releases/tag/v0.1.1-preview.1) prerelease. It targets Windows x64, the DMM PC version, and an OpenXR runtime.

This prerelease is still being validated in the real game and on HMDs. Behavior may change with game or OpenXR runtime updates, so read the release notes before installing.

## Highlights

- Renders the game camera as OpenXR stereo views
- VR presentation handling for portrait, landscape, and live scenes
- OpenXR controller pointer plus VR movement/view settings
- Korean, English, and Japanese configurator and installer
- Coexists with `scsp-localify` while preserving its `version.dll`, settings, and translation data
- Also installs on a clean game folder without the Korean patch
- Install/uninstall with SHA-256 verification, rollback, and modified-file protection
- Configurator auto-updates from both stable GitHub releases and prereleases

## Installation

1. Download the latest `SongPrismVR-v*.zip` and its matching `.sha256` file from [Releases](https://github.com/deadpixel134/scsp-vr/releases).
2. Fully extract the ZIP to any folder.
3. Close the game and run `SongPrismVR.Installer.exe`.
4. Confirm the game folder containing `imasscprism.exe`, then select **Install**.
5. After installation, use `vrmod/tools/SongPrismVR.Configurator.exe` in the game folder to configure OpenXR and controls.

When an update is available, the configurator downloads the release ZIP, verifies its SHA-256, and starts the installer from a separate staging directory. It refuses to update while the game is running.

## Uninstalling and Localify coexistence

Use **Uninstall** in the installer. Pre-install files are retained for rollback, and user-modified files are not deleted by guesswork. Installation works with or without `scsp-localify`; existing Localify files and user VR settings are preserved.

## Important limitations

- DMM PC only; Steam and mobile versions are not supported.
- As a prerelease, it cannot guarantee every HMD, OpenXR runtime, or graphics-settings combination.
- After a game update, uninstall the mod or wait for compatibility confirmation before launching.
- Runtime initialization is designed to fail open, but not every failure path has completed real-device acceptance yet.
- No game binaries, game assets, or `scsp-localify` translation data are included in this repository or its releases.

## Development

Run management and installer-policy tests:

```powershell
dotnet run --project tests/SongPrismVR.Management.Tests/SongPrismVR.Management.Tests.csproj -c Release
```

Run core policy tests:

```powershell
dotnet run --project tests/SongPrismVR.Core.Tests/SongPrismVR.Core.Tests.csproj -c Release
```

Distribution builds use `scripts/Build-DistributionPackage.ps1` inside a supported game workspace. Game and third-party binaries are never committed to this repository.

## License and credits

SCSP VR source is distributed under the [GNU General Public License v3.0](LICENSE). The bundled OpenXR Loader, .NET Runtime, Unity Doorstop, and Dobby remain under their respective licenses. See [CREDITS.md](CREDITS.md) and [THIRD_PARTY_NOTICES.txt](release-assets/THIRD_PARTY_NOTICES.txt) for exact sources and terms.

This is an unofficial fan project and is not affiliated with or endorsed by Bandai Namco Entertainment, Bandai Namco Studios, THE IDOLM@STER, or any related rights holder. Game names, characters, logos, trademarks, and game data belong to their respective owners.
