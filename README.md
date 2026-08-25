[한국어](README.md) | [English](README.en.md) | [日本語](README.ja.md)

# SCSP VR

제작자: [@TBluebox12](https://x.com/TBluebox12)\
아카라이브 가상현실 채널: [가상현실 채널](https://arca.live/b/vrshits)\
후원: [buymeacoffee.com/vrshits](https://buymeacoffee.com/vrshits)\

**아이돌마스터 샤이니 컬러즈 Song for Prism** DMM PC판을 OpenXR VR로 플레이하기 위한 비공식 모드입니다.

[문제 보고](https://github.com/deadpixel134/scsp-vr/issues)

## 현재 릴리스

현재 공개 빌드는 [`v0.1.1-preview.1`](https://github.com/deadpixel134/scsp-vr/releases/tag/v0.1.1-preview.1) 프리릴리스입니다. Windows x64, DMM PC판, OpenXR 런타임을 대상으로 합니다.

프리릴리스는 실제 게임과 HMD에서 계속 검증 중입니다. 게임 업데이트나 OpenXR 런타임에 따라 동작이 달라질 수 있으므로 설치 전에 릴리스 노트를 확인해 주세요.

## 주요 기능

- 게임 카메라를 OpenXR 스테레오 뷰로 렌더링
- 세로/가로 화면과 라이브 장면에 맞춘 VR 표시 처리
- OpenXR 컨트롤러 포인터 및 VR 이동/시점 설정
- 한국어·영어·일본어 설정 앱과 설치 프로그램
- 기존 `scsp-localify` 한글 패치의 `version.dll`, 설정, 번역 데이터를 보존하는 공존 설치
- 한글 패치가 없는 깨끗한 게임 폴더에도 필요한 로더 구성 설치
- SHA-256 검증, 롤백, 수정 파일 보호를 포함한 설치/제거
- GitHub 안정판과 프리릴리스를 지원하는 설정 앱 자동 업데이트

## 설치

1. [Releases](https://github.com/deadpixel134/scsp-vr/releases)에서 최신 `SongPrismVR-v*.zip`과 같은 이름의 `.sha256` 파일을 받습니다.
2. ZIP을 임의의 폴더에 완전히 압축 해제합니다.
3. 게임을 종료한 상태에서 `SongPrismVR.Installer.exe`를 실행합니다.
4. `imasscprism.exe`가 있는 게임 폴더를 확인하고 **설치**를 누릅니다.
5. 설치 후 게임 폴더의 `vrmod/tools/SongPrismVR.Configurator.exe`에서 OpenXR와 조작 설정을 변경할 수 있습니다.

업데이트가 발견되면 설정 앱이 릴리스 ZIP을 내려받아 SHA-256을 확인한 뒤 별도 스테이징 폴더에서 설치 프로그램을 실행합니다. 게임 실행 중에는 업데이트하지 않습니다.

## 제거와 공존

설치 프로그램의 **제거**를 사용하세요. 설치 전 파일은 롤백용으로 보관되며, 사용자가 수정한 파일은 추측으로 삭제하지 않습니다. `scsp-localify`가 있든 없든 설치할 수 있고, 기존 Localify 파일과 사용자 VR 설정은 덮어쓰지 않습니다.

## 알려진 제한사항

- DMM PC판 전용이며 Steam/모바일판을 지원하지 않습니다.
- 프리릴리스 단계이므로 모든 HMD, OpenXR 런타임, 그래픽 설정 조합을 보장하지 않습니다.
- 게임 업데이트 후에는 호환성 확인 전까지 모드를 제거하거나 새 릴리스를 기다리는 편이 안전합니다.
- VR 런타임 초기화에 실패하면 게임을 계속 실행하도록 설계했지만, 프리릴리스의 모든 실패 경로가 실기기에서 승인된 것은 아닙니다.
- 게임 파일, 게임 에셋, `scsp-localify` 번역 데이터는 저장소나 릴리스에 포함하지 않습니다.

## 개발

관리/설치 계층 테스트:

```powershell
dotnet run --project tests/SongPrismVR.Management.Tests/SongPrismVR.Management.Tests.csproj -c Release
```

핵심 정책 테스트:

```powershell
dotnet run --project tests/SongPrismVR.Core.Tests/SongPrismVR.Core.Tests.csproj -c Release
```

배포 빌드는 지원되는 게임 작업공간에서 `scripts/Build-DistributionPackage.ps1`을 사용합니다. 게임 및 제3자 바이너리는 저장소에 커밋하지 않습니다.

## 라이선스와 크레딧

SCSP VR 소스는 [GNU General Public License v3.0](LICENSE)으로 배포됩니다. 배포 파일에 포함되는 OpenXR Loader, .NET Runtime, Unity Doorstop, Dobby에는 각각의 라이선스가 적용됩니다. 자세한 출처와 조건은 [CREDITS.md](CREDITS.md) 및 [THIRD_PARTY_NOTICES.txt](release-assets/THIRD_PARTY_NOTICES.txt)를 확인하세요.

본 프로젝트는 비공식 팬 프로젝트이며 Bandai Namco Entertainment, Bandai Namco Studios, THE IDOLM@STER 또는 관련 권리자와 제휴하거나 승인을 받지 않았습니다. 게임명, 캐릭터, 로고, 상표 및 게임 데이터의 권리는 각 권리자에게 있습니다.
