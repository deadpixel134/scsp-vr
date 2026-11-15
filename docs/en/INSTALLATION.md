[English](../en/INSTALLATION.md) | [한국어](../ko/INSTALLATION.md) | [日本語](../ja/INSTALLATION.md) · [README](../../README.md) · [Controls](USAGE.md)

# Installation, updates, and recovery

## Before installing

Use the DMM PC edition on Windows x64. Locate the folder containing `imasscprism.exe`, `GameAssembly.dll`, and `UnityPlayer.dll`; select this folder, not the DMM launcher folder. Close the game before installing, updating, removing the mod, or saving settings.

Download the full `SongPrismVR-v*.zip` and matching `.zip.sha256` from [Releases](https://github.com/deadpixel134/scsp-vr/releases). Read the release notes. Extract the entire ZIP outside the game directory and keep its installer, manifest, and payload together. Do not copy only the EXE into the game folder or run it inside the ZIP.

To check the downloaded archive manually, run PowerShell with the actual ZIP path:

```powershell
Get-FileHash -LiteralPath 'C:\Downloads\SongPrismVR-vX.Y.Z.zip' -Algorithm SHA256
```

Compare the hash with the value in that archive's `.sha256` file. If they differ, download again. The installer separately verifies package files against its manifest; it does not automatically verify a manually downloaded ZIP against its adjacent checksum file.

## Install

The source installer first checks its own parent folders and a valid working directory, then DMM's installed-game records. It fills the game folder only after finding `imasscprism.exe`, `GameAssembly.dll`, and `UnityPlayer.dll`. Missing, corrupt, or locked launcher data does not prevent manual selection. If multiple distinct DMM installations are valid, use **Browse** to select one. Older release packages may not include this discovery feature.

1. Run the extracted `SongPrismVR.Installer.exe`.
2. Choose your language and game folder; review the installed-version and Localify status.
3. Select **Install**, **Update**, or **Reinstall**, as appropriate.
4. Wait for completion, then use **Open settings**. The configurator checks for automatic updates when opened.

Clean game folders are supported. If `scsp-localify` is already present, its existing `version.dll`, configuration, translations, and other protected files are preserved. Existing Dobby and VR settings are also preserved. The installer does not install Localify translation data.

If Localify is installed, disable Free Camera in `game directory\scsp-config.json` before starting the game:

```json
"baseFreeCamera": {
  "enable": false
}
```

Edit the existing setting; do not replace the whole file with this fragment. SCSP VR does not toggle or restore it automatically.

| Installed location, relative to the game folder | Purpose |
|---|---|
| `vrmod/tools/SongPrismVR.Configurator.exe` | Settings and automatic updates |
| `vrmod/config/settings.json` | User VR settings, preserved across updates/removal |
| `vrmod/runtime/` | VR runtime files |
| `vrmod/logs/runtime-bootstrap.jsonl` | Runtime diagnostic log |
| `vrmod/install-state.json` | Installer's record of owned files and backups |
| `vrmod/rollback/` | Replaced files and previous installation state |

## Update

**Automatic:** close the game and open the installed configurator. It checks GitHub at startup. If an eligible newer release exists, it downloads and verifies the ZIP, starts the staged installer, closes, and reopens after installation. **There is no confirmation prompt, and prereleases as well as stable releases are eligible.** It is not a stable-only updater. The status bar reports progress and errors. **Check for updates** repeats the check; a running game defers installation until you close it and check again.

**Manual:** obtain the new ZIP and checksum, extract to another folder, and run its installer against the same game folder. Select **Update** or **Reinstall**. Existing settings remain in place; new settings absent from older files use compatible defaults. Review [controls](USAGE.md), especially world drag: new defaults enable it, while an explicit existing `false` setting is preserved.

Downloads require GitHub access. If automatic updates fail, inspect `%LOCALAPPDATA%\SongPrismVR\update.log` and use the manual method. Do not replace runtime files while the game is running.

<a id="openxr-runtimes"></a>
## OpenXR runtimes

Connect the headset and controllers before launching the game through the desktop DMM launcher. The mod uses the active PC OpenXR runtime; a release ZIP alone does not configure a PC VR connection.

| Connection you intend to use | Setup to check |
|---|---|
| Virtual Desktop / VDXR | Connect through Virtual Desktop and select Virtual Desktop OpenXR / VDXR in the PC Streamer |
| SteamVR OpenXR | Start the SteamVR connection and set SteamVR as the active OpenXR runtime |
| Meta Quest Link / Air Link | Establish the Link connection and select Meta's OpenXR runtime in its PC software |

These are general setup directions. The repository does not specify which headset/runtime combination was used for the recorded SCSP VR user validation. **These three paths are not a verified compatibility list.** D3D12-capable OpenXR and Touch-style controller bindings are the implementation targets. See [validation scope](../DEVELOPMENT.md).

Keep the game window in the foreground for pointer input. Check the flat panel first, then stereo in a supported 3D scene. See [README](../../README.md) for symptom-based troubleshooting.

<a id="uninstall-and-rollback"></a>
## Uninstall and rollback

1. Close the game and run the installer from an extracted release.
2. Select the installed game directory.
3. If **Roll back** is shown, it removes the current installation layer and restores the preceding installation. Repeat if you want to remove all layers.
4. When no previous layer remains, select **Uninstall** to remove the mod's owned files and restore pre-install files.

VR settings, logs, and existing Localify files remain. The installer checks for modified/missing files and missing backups before removal; if it reports these problems, it keeps the installation state and does not proceed with file removal. Keep `vrmod/install-state.json` and `vrmod/rollback/` intact. If the state or backups are lost, report the error rather than deleting loaders by guesswork. Rollback depends on a usable previous installation backup.

After a game update, consult [Releases](https://github.com/deadpixel134/scsp-vr/releases) before using VR again. Runtime initialization is designed to let the normal game continue on failure, but not every failure path has user VR validation.
