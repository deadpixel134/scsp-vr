# SONGforPRISM VR runtime changelog

## 0.1.3 — 2026-09-22

### OurStream support

- Added content-independent 3D stereo rendering for OurStream viewing scenes by recognizing their stable `LiveCamera` and approved UI overlay topology.
- Added safe stereo-generation replacement when the OurStream camera or UI stack changes.

### Usage notes

- The initial OurStream waiting screen appears as a black 3D stereo environment and must be skipped with PC controls or the mini panel.
- Depending on the environment, OurStream may begin at world origin `(0, 0, 0)`, potentially below the floor; position and viewing-angle adjustment is recommended.

## 0.1.2 — 2026-09-20

### [Issue #1](https://github.com/deadpixel134/scsp-vr/issues/1) improvements

- Added 3D stereo rendering support for Photo Studio scenes.
- Kept the established 3D stereo source active while a 3D Live is paused, avoiding a fallback to the flat panel during temporary camera loss.
- Added one-to-one space-drag locomotion with independent left/right Grip and Trigger activators, independent tracked hands, first-press ownership, and simultaneous thumbstick locomotion.
- Split thumbstick view turning into its own setting and added localized configurator controls, conflict guidance, and validation for world dragging.

### Compatibility note

- Documented that `scsp-localify`'s free-camera option conflicts with VR rendering. Users must disable `baseFreeCamera.enable` before starting the game; SCSP VR does not change this Localify setting automatically.

## 0.1.1 — 2026-08-25

### Stable baseline

- Promoted the user-validated `SPVR-CAND-VR-DEFERRED-SOURCE-FRAME-001-D8409815` behavior to the v0.1.1 stable release line.
- Removed the unsafe direct source-camera completion hook and retained the launch-stable one-frame-deferred source gate.
- Documented the remaining VR-only character/body and hair divergence with intermittent character disappearance as a known deferred issue; flat output remains normal.

### Installer, release, and automatic updates

- Added a localized graphical installer and settings application.
- Added clean-install and `scsp-localify` coexistence policies that preserve existing Localify files, Dobby, and user settings.
- Added transactional install, rollback, modified-file protection, package manifest verification, and SHA-256 release checks.
- Added GitHub automatic updates for stable releases and prereleases with semantic-version precedence.
- Added bounded update downloads, contained ZIP extraction, and trusted staging-directory validation.
- Added Korean, English, and Japanese public documentation plus complete project and third-party license notices.

## 0.1.0 — 2026-08-16

### SCSP-M0: D3D12 bootstrap and flat panel

- Added SONGforPRISM D3D12 device/queue/swapchain/Present capture.
- Added OpenXR `XR_KHR_D3D12_enable` session and final-backbuffer flat quad submission.
- Fixed `XrSwapchainSubImage` ABI field order.
- Added D3D12 resource-state barriers for `CopyResource` around OpenXR swapchain images.
- Switched panel layer to opaque flags, view-space `Z=-1.6m`, and aspect-correct size.
- Added sRGB (`R8G8B8A8_UNORM_SRGB`) preferred panel swapchain format to correct gamma.
- Verified via user VR run: session created, panel displayed, 4340 frame submissions without failure.
