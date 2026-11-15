[English](../en/INSTALLATION.md) | [한국어](../ko/INSTALLATION.md) | [日本語](../ja/INSTALLATION.md) · [README](../../README.ko.md) · [조작법](USAGE.md)

# 설치·업데이트·복구 안내

## 설치 전 준비

Windows x64의 DMM PC판을 사용합니다. `imasscprism.exe`, `GameAssembly.dll`, `UnityPlayer.dll`이 있는 폴더를 찾으십시오. DMM 런처 폴더가 아니라 이 게임 폴더를 선택해야 합니다. 설치·업데이트·제거·설정 저장 전에는 게임을 종료합니다.

[Releases](https://github.com/deadpixel134/scsp-vr/releases)에서 `SongPrismVR-v*.zip` 전체와 대응하는 `.zip.sha256`을 받고 릴리스 노트를 읽습니다. ZIP 전체를 게임 폴더 밖에 풀어 설치기·manifest·payload를 함께 둡니다. EXE만 게임 폴더에 복사하거나 ZIP 안에서 실행하지 마십시오.

내려받은 ZIP을 직접 검증하려면 PowerShell에서 실제 파일 경로로 실행합니다.

```powershell
Get-FileHash -LiteralPath 'C:\Downloads\SongPrismVR-vX.Y.Z.zip' -Algorithm SHA256
```

출력된 해시와 대응하는 `.sha256` 파일의 값을 비교합니다. 다르면 다시 다운로드하십시오. 설치기는 별도로 manifest를 기준으로 패키지 파일을 검증하며, 수동 다운로드한 ZIP을 옆의 체크섬 파일과 자동 비교하지는 않습니다.

## 설치

이 저장소의 설치기는 먼저 실행 파일의 상위 폴더와 유효한 작업 폴더를 확인하고, 이어서 DMM 설치 목록을 읽습니다. `imasscprism.exe`, `GameAssembly.dll`, `UnityPlayer.dll`이 모두 있는 경로만 자동 입력합니다. DMM 설정이 없거나 손상·잠금 상태여도 수동 선택은 가능합니다. DMM에서 서로 다른 유효한 설치가 여러 개 발견되면 **찾기**로 선택하십시오. 이전 배포 패키지에는 이 자동 탐색이 없을 수 있습니다.

1. 압축을 푼 `SongPrismVR.Installer.exe`를 실행합니다.
2. 언어와 게임 폴더를 선택하고 설치 버전·한글 패치 상태를 확인합니다.
3. 상태에 따라 **설치**, **업데이트**, **다시 설치**를 누릅니다.
4. 완료 후 **설정 열기**를 사용합니다. 설정 앱을 열면 자동 업데이트도 확인합니다.

깨끗한 게임 폴더에도 설치할 수 있습니다. 기존 `scsp-localify`가 있으면 `version.dll`, 설정·번역 및 보호 대상 파일을 보존합니다. 기존 Dobby와 VR 설정도 유지합니다. 설치기는 Localify 번역 데이터를 설치하지 않습니다.

Localify를 사용한다면 실행 전 `게임 폴더\scsp-config.json`의 Free Camera를 끕니다.

```json
"baseFreeCamera": {
  "enable": false
}
```

기존 항목을 수정하고 파일 전체를 위 조각으로 바꾸지 마십시오. SCSP VR은 이 값을 자동으로 켜거나 끄고 복구하지 않습니다.

| 설치 경로 — 게임 폴더 기준 | 용도 |
|---|---|
| `vrmod/tools/SongPrismVR.Configurator.exe` | 설정과 자동 업데이트 |
| `vrmod/config/settings.json` | 업데이트·제거 후에도 보존되는 VR 설정 |
| `vrmod/runtime/` | VR 런타임 파일 |
| `vrmod/logs/runtime-bootstrap.jsonl` | 런타임 진단 로그 |
| `vrmod/install-state.json` | 설치기가 관리하는 파일·백업 기록 |
| `vrmod/rollback/` | 교체 파일과 이전 설치 상태 |

## 업데이트

**자동:** 게임을 종료하고 설치된 설정 앱을 엽니다. 시작 시 GitHub를 확인하며, 대상 새 릴리스가 있으면 ZIP을 다운로드·검증한 뒤 별도 폴더의 설치기를 실행합니다. 설정 앱은 닫혔다가 설치 후 다시 열립니다. **확인 창 없이 진행하며 안정판과 프리릴리스 모두 대상입니다.** 안정판 전용 업데이트가 아닙니다. 상태 표시줄에 진행·오류가 표시됩니다. **업데이트 확인**으로 다시 확인할 수 있으며, 게임 실행 중이면 종료한 뒤 다시 확인해야 설치됩니다.

**수동:** 새 ZIP과 체크섬을 받아 다른 폴더에 풀고, 새 설치기에서 같은 게임 폴더를 선택해 **업데이트** 또는 **다시 설치**합니다. 기존 설정은 유지되고, 이전 설정 파일에 없는 항목은 호환 기본값을 사용합니다. 월드 드래그는 새 기본값에서 켜지지만 기존의 명시적 `false` 설정은 유지합니다. [조작 안내](USAGE.md)를 확인하십시오.

GitHub에 연결할 수 있어야 다운로드됩니다. 실패하면 `%LOCALAPPDATA%\SongPrismVR\update.log`를 확인하거나 수동 방법을 사용하십시오. 게임 실행 중 런타임 파일을 교체하지 마십시오.

<a id="openxr-runtimes"></a>
## OpenXR 런타임

헤드셋·컨트롤러를 연결한 후 데스크톱의 DMM 런처로 게임을 실행합니다. 모드는 활성 PC OpenXR 런타임을 사용합니다. 배포 ZIP을 설치하는 것만으로 PC VR 연결이 설정되지는 않습니다.

| 사용할 연결 방식 | 확인할 설정 |
|---|---|
| Virtual Desktop / VDXR | Virtual Desktop으로 연결하고 PC Streamer에서 Virtual Desktop OpenXR / VDXR 선택 |
| SteamVR OpenXR | SteamVR 연결을 시작하고 SteamVR를 활성 OpenXR 런타임으로 지정 |
| Meta Quest Link / Air Link | Link로 연결하고 PC 프로그램에서 Meta의 OpenXR 런타임 선택 |

위 표는 일반적인 연결 설정 안내입니다. 저장소에는 SCSP VR 사용자 실기에서 사용한 헤드셋·런타임 조합이 명시되어 있지 않습니다. **세 경로를 검증 완료 호환성 목록으로 해석하지 마십시오.** 구현 대상은 D3D12를 지원하는 OpenXR와 Touch 계열 입력입니다. [검증 범위](../DEVELOPMENT.md)를 참고하십시오.

포인터 입력을 위해 게임 창을 포그라운드에 둡니다. 평면 패널을 먼저 확인한 뒤 지원되는 3D 장면에서 양안 표시를 확인합니다. 증상별 해결법은 [README](../../README.ko.md)를 참고하십시오.

<a id="uninstall-and-rollback"></a>
## 제거와 롤백

1. 게임을 종료하고 압축을 푼 배포 폴더의 설치기를 실행합니다.
2. 설치한 게임 폴더를 선택합니다.
3. **직전 버전으로 복귀**가 표시되면 현재 설치 단계를 제거하고 그 전 설치를 복원합니다. 전부 제거하려면 필요에 따라 반복합니다.
4. 이전 설치가 없을 때 **제거**를 실행하면 모드 소유 파일을 제거하고 설치 전 파일을 복원합니다.

VR 설정·로그·기존 Localify 파일은 남깁니다. 제거 전 변경·누락 파일 및 백업 누락을 검사하며, 문제가 있으면 설치 상태를 유지하고 파일 제거를 진행하지 않습니다. `vrmod/install-state.json`과 `vrmod/rollback/`을 보존하십시오. 상태나 백업이 없다면 로더를 추측으로 지우지 말고 오류를 보고하십시오. 롤백에는 정상적인 이전 설치 백업이 필요합니다.

게임 업데이트 뒤에는 [Releases](https://github.com/deadpixel134/scsp-vr/releases)를 확인하고 VR을 사용하십시오. 런타임 초기화 실패 시 일반 게임 실행을 계속하도록 설계했으나, 모든 실패 경로가 사용자 VR 실기로 검증된 것은 아닙니다.
