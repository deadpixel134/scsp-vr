# SCSP VR v0.1.2

이번 릴리스는 [GitHub Issue #1](https://github.com/deadpixel134/scsp-vr/issues/1)에서 요청된 VR 표시 및 조작 개선을 구현합니다.

## 주요 변경사항

- **포토 스튜디오 3D 스테레오 지원**
  - 포토 스튜디오의 안정된 월드 카메라와 UI 구성을 인식해 OpenXR 양안 렌더링으로 표시합니다.
- **3D 라이브 일시 정지 중 스테레오 유지**
  - 일시 정지로 활성 카메라가 잠시 검색되지 않아도 승인된 라이브 스테레오 소스를 유지합니다.
  - 라이브 장면이나 카메라가 실제로 바뀐 경우에는 기존 안전 전환 절차를 사용합니다.
- **Grip/Trigger Space Drag 이동**
  - Grip 또는 Trigger를 누른 채 손을 움직여 월드를 잡아 끄는 방식으로 이동할 수 있습니다.
  - 활성화 버튼과 움직임을 추적할 손을 왼손·오른손별로 독립 설정할 수 있으며 양손 설정도 지원합니다.
  - 기존 썸스틱 이동과 함께 사용할 수 있고, 시야 회전은 별도 옵션으로 제어합니다.

## 호환성 주의사항

`scsp-localify`의 **Free Camera** 옵션은 VR 렌더링과 충돌합니다. 게임 실행 전에 `scsp-config.json`에서 다음 값을 확인해 주세요.

```json
"baseFreeCamera": {
  "enable": false
}
```

SCSP VR은 이 값을 자동으로 변경하거나 게임 종료 후 복구하지 않습니다.

## 설치 및 업데이트

1. `SongPrismVR-v0.1.2.zip`과 `SongPrismVR-v0.1.2.zip.sha256`을 함께 받습니다.
2. ZIP을 완전히 압축 해제한 뒤 게임이 종료된 상태에서 `SongPrismVR.Installer.exe`를 실행합니다.
3. 설치 후 `vrmod/tools/SongPrismVR.Configurator.exe`의 조작 탭에서 월드 드래그 입력을 설정합니다.

기존 VR 설정 파일은 새 필드가 없어도 호환되며, 월드 드래그는 기본적으로 비활성화됩니다.
