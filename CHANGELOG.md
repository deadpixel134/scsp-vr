# SONGforPRISM VR runtime changelog

## 0.1.1-preview.1 — 2026-08-25

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
