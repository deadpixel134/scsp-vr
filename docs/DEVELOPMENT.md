[English README](../README.md) · [한국어 README](../README.ko.md) · [日本語 README](../README.ja.md)

# Development, validation scope, and known limitations

## Validation evidence and its limits

The [changelog](../CHANGELOG.md) records user VR verification of the initial flat-panel path and promotion of a user-validated stereo baseline to the stable release line. It does not record a complete headset, connection method, and OpenXR runtime combination. A named runtime must not be marked hardware-validated based on the neighboring Gakumas VR project's documentation.

| Evidence | What it establishes | What it does not establish |
|---|---|---|
| Recorded user VR runs | Panel output and the stable stereo baseline were checked by a user in VR | Every scene, headset, runtime, or later change is validated |
| Runtime source and release notes | Implemented scene recognition, controls, defaults, and documented caveats | A complete user VR acceptance pass for those features |
| Successful build / policy tests | Code builds or the tested policy behaves as expected | Headset display, visual correctness, input comfort, or performance |
| Successful package validation / installation | Package integrity and installer behavior | VR operation on that PC |

The source targets Windows x64, the DMM PC game, Direct3D 12 OpenXR, and Oculus Touch-style input bindings. Virtual Desktop / VDXR, SteamVR OpenXR, and Meta Quest Link / Air Link are not documented as individually hardware-validated combinations in this repository. No exact Windows version or game Unity version is asserted as a verified environment by these README files.

## Known limitations

- The stable-baseline changelog records VR-only character/body and hair divergence, with intermittent character disappearance, while flat output remains normal. Subsequent release notes do not explicitly close this issue; retain it as a documented limitation pending user VR confirmation.
- Localify Free Camera conflicts with stereo. The user must set `baseFreeCamera.enable` to `false`; SCSP VR does not automatically change or restore it.
- OurStream can enter a black waiting screen and can place the initial viewpoint at world origin, including below the floor. Skip the waiting screen and reposition. See the [OurStream release notes](../RELEASE_NOTES_v0.1.3.md) (Korean).
- Stereo depends on recognized scene camera/UI layouts. Unknown or changing layouts can use the safe flat-panel path. Game updates can invalidate those assumptions.
- Runtime startup is designed to fail open so the normal game can continue, but not every failure path is user VR validated.
- World drag is enabled in the source and packaged defaults. Existing explicit `false` settings remain disabled; omitted fields use the new enabled default. Its activators can conflict with panel toggling or trigger clicks. See [controls](en/USAGE.md).
- This controller implementation has no stick-click recenter/camera-follow actions, in-headset settings menu, or direct-touch UI. Optional stick scrolling exists and is off by default.

## Source map for documentation maintenance

| Topic | Source |
|---|---|
| Packaged defaults | [default-settings.json](../release-assets/default-settings.json), [VrSettings.cs](../src/SongPrismVR.Core/VrSettings.cs) |
| Controller bindings and panel toggle | [OpenXrControllerActions.cs](../src/SongPrismVR.RuntimeBootstrap/OpenXrControllerActions.cs) |
| Pointer, click/back, optional scroll, foreground requirement | [VrPointerInput.cs](../src/SongPrismVR.RuntimeBootstrap/VrPointerInput.cs) |
| Head-relative movement and hand dragging | [VrLocomotionIntegrator.cs](../src/SongPrismVR.Core/VrLocomotionIntegrator.cs), [OpenXrWorldDragInput.cs](../src/SongPrismVR.RuntimeBootstrap/OpenXrWorldDragInput.cs), [VrWorldDragState.cs](../src/SongPrismVR.Core/VrWorldDragState.cs) |
| Scene recognition and stereo/hand-panel presentation | [MainThreadSampler.cs](../src/SongPrismVR.RuntimeBootstrap/MainThreadSampler.cs), [D3D12OpenXrPanelRuntime.cs](../src/SongPrismVR.RuntimeBootstrap/D3D12OpenXrPanelRuntime.cs) |
| Installer ownership, preservation, rollback | [InstallationEngine.cs](../src/SongPrismVR.Management/InstallationEngine.cs), [InstallerForm.cs](../src/SongPrismVR.Installer/InstallerForm.cs) |
| Startup game-folder discovery from DMM installed-game records | [GameInstallationLocator.cs](../src/SongPrismVR.Management/GameInstallationLocator.cs) |
| Automatic update trigger and release eligibility | [MainForm.cs](../src/SongPrismVR.Configurator/MainForm.cs), [UpdateService.cs](../src/SongPrismVR.Configurator/UpdateService.cs), [AutoUpdateRunner.cs](../src/SongPrismVR.Installer/AutoUpdateRunner.cs) |
| Settings path and save behavior | [SettingsStore.cs](../src/SongPrismVR.Configurator/SettingsStore.cs) |
| Runtime log path | [Entrypoint.cs](../src/SongPrismVR.RuntimeBootstrap/Entrypoint.cs) |

## Build and checks

The source installer reads `dmmgame.cnf` from the current user's DMM application-data directories (the observed DMM Game Player 5 path is `%APPDATA%/dmmgameplayer5/dmmgame.cnf`). It reads `contents[].detail.path`, skips records explicitly marked uninstalled, and verifies all three game-root files. It does not infer an installation from `defaultInstallDir`, guess a product ID, or scan the drives. Existing application-parent detection has priority, followed by a valid working directory. Duplicate paths are merged; multiple distinct valid DMM installations require manual selection. Missing or unusable configuration falls back to the existing working-directory/manual-selection flow. Launcher configuration is read only and its contents are not logged.

Management tests cover this discovery with synthetic launcher files, including Unicode paths, missing game files, invalid records, corruption, file locks, oversized configuration, duplicate paths, ambiguity, and local-path precedence. The current PC's DMM configuration format was inspected, but it has no SCSP installation entry; a successful real installed-game discovery is not claimed from that inspection. This discovery feature is included starting with v0.1.4.

Run from the repository root with the .NET SDK required by the project files:

```powershell
dotnet run --project tests/SongPrismVR.Management.Tests/SongPrismVR.Management.Tests.csproj -c Release
dotnet run --project tests/SongPrismVR.Core.Tests/SongPrismVR.Core.Tests.csproj -c Release
```

For a distribution build against a supported game workspace:

```powershell
.\scripts\Build-DistributionPackage.ps1 -GameRoot 'C:\Path\To\Game'
```

See the [distribution script](../scripts/Build-DistributionPackage.ps1) for required local dependencies and packaging checks. It creates release artifacts; it does not establish headset validation. Game and third-party binaries must not be committed to the source repository.

## Documentation layout

`README.md` is the default English guide; `README.en.md` keeps the previous English address with identical content. Update both together. `README.ko.md` and `README.ja.md` follow the same section order, defaults, and caveats. Detailed installation and usage guides live in `docs/en/`, `docs/ko/`, and `docs/ja/`. Shared development details are in this English document; historical notes remain in [CHANGELOG.md](../CHANGELOG.md) and the [Releases page](https://github.com/deadpixel134/scsp-vr/releases).

Localized controller guides in [docs/images](images/README.md) use Meta's official Quest 3 Touch Plus artwork with SCSP VR annotations. Keep all three images consistent with the control tables and packaged defaults. Image sources, separate usage terms, and generation prompts are recorded alongside the assets. Relative document links resolve from their containing Markdown file. Controller guides are linked from the repository README and detailed usage documents.
