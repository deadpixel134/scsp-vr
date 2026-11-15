# SCSP VR v0.1.4

## 변경 사항

- **설치기에서 게임 설치 경로 자동 입력**: DMM의 설치 목록을 읽고 `imasscprism.exe`, `GameAssembly.dll`, `UnityPlayer.dll`이 모두 있는 경로만 사용합니다. 유효한 경로가 없거나 여러 개면 기존처럼 직접 선택합니다. DMM 설정과 게임 파일은 탐색 중 변경하지 않습니다.
- **월드 드래그 기본 켜짐**: 새 설치에서는 오른손 Grip을 누른 채 오른손을 움직여 위치를 미세 조정할 수 있습니다. 기존 설정에 `enabled: false`가 명시되어 있으면 그 값은 유지합니다. 항목이 없는 설정에는 새 기본값을 적용합니다.
- **사용 안내 정리**: 기본 README를 영어로 정리하고 한국어·일본어 설치·업데이트·실행·조작·복구 안내를 추가했습니다. 기존 영어 README 주소도 유지합니다.
- **조작 이미지 추가**: Meta 공식 Quest 3 Touch Plus 도해를 바탕으로 한·영·일 조작 이미지를 제공합니다. 이미지 하단 문구와 월드 드래그 켜짐·꺼짐 배지는 제외했습니다.
- 런타임 로그의 버전 표시를 릴리스 버전과 일치시켰습니다.

## 설치 및 업데이트

1. `SongPrismVR-v0.1.4.zip`과 `SongPrismVR-v0.1.4.zip.sha256`을 받습니다.
2. 게임을 완전히 종료하고 ZIP 전체를 게임 폴더 밖에 풉니다.
3. `SongPrismVR.Installer.exe`를 실행합니다. 자동 입력된 게임 경로를 확인하거나 **찾기**로 선택합니다.
4. **설치** 또는 **업데이트**를 누릅니다. 기존 VR 설정과 Localify 파일은 보존합니다.

설치된 설정 앱의 자동 업데이트도 사용할 수 있습니다. 앱을 열 때 확인하며, 안정판과 프리릴리스 모두 대상입니다. 게임 실행 중에는 설치를 미룹니다.

## 주의 사항과 검증 범위

- 기존 사용자가 월드 드래그를 사용하려면 설정 앱의 **조작 → 월드 드래그 사용**이 켜져 있는지 확인하십시오. 업데이트가 기존의 명시적 꺼짐 값을 덮어쓰지는 않습니다.
- `scsp-localify`의 Free Camera는 계속 호환되지 않습니다. 실행 전 `scsp-config.json`의 `baseFreeCamera.enable`을 `false`로 설정하십시오.
- OurStream 초기 대기 화면의 검은 표시와 초기 위치 문제, 기존 스테레오 표시 제한은 그대로입니다. 이번 변경은 해당 문제의 해결을 주장하지 않습니다.
- DMM 경로 탐색은 실제 런처 설정 형식을 확인하고 테스트 데이터로 검증했습니다. 현재 PC에는 SCSP 등록 항목이 없어 실제 설치 경로 자동 입력은 미확인입니다.
- 자동 테스트·배포 무결성·설치 및 제거 검증은 사용자 VR 실기 확인과 별개입니다. 이번 변경의 새 사용자 VR 실기 검증은 수행하지 않았습니다.

## 배포 검증

- 핵심 테스트 64개, 실제 배포 패키지 검증을 포함한 관리 테스트 13개 통과.
- 패키지 파일 200개 해시, 외부 라이브러리 로딩, 새 설치·제거, Localify 공존·기존 설정 보존·수정 파일 보호 검증 통과.
- 배포 설치기와 설정 앱의 다국어 리소스 검사 및 설치기 패키지 탐색 검사 통과.
- 게임 본체를 포함하지 않으며, 변경하지 않은 로더·외부 런타임 구성은 공개 v0.1.3 ZIP의 해시를 확인한 뒤 재사용했습니다.

## English

- The installer can prefill a DMM-registered game folder after validating all three required game files. Missing or ambiguous results retain manual selection.
- World drag is enabled in new defaults: hold right Grip and move the right hand. Existing explicit disabled settings are preserved; omitted fields use the new default.
- Added English/Korean/Japanese installation and controls guides, plus controller images based on Meta's official Quest 3 Touch Plus artwork. Removed image footers and world-drag state badges.
- Close the game, extract the ZIP outside the game directory, and run the installer against the same game folder. Existing VR settings and Localify files are preserved.
- Existing rendering limitations remain. Build/package checks are not new user VR acceptance; live DMM discovery on an SCSP installation has not been confirmed on this PC.

## 日本語

- DMMの導入一覧と必須ゲームファイル3点を確認し、ゲームフォルダーを自動入力します。未検出・複数候補の場合は手動で選択します。
- 新しい初期値ではワールドドラッグを有効にします。右Gripを押しながら右手を動かします。既存の明示的な無効設定は保持し、項目がなければ新しい初期値を使います。
- 英語・韓国語・日本語の導入／操作ガイドと、Meta公式Quest 3 Touch Plus図を使った操作画像を追加しました。画像の下部説明と状態バッジは削除しました。
- ゲームを終了し、ZIPをゲームフォルダー外へ展開して、以前と同じゲームフォルダーへインストールしてください。既存のVR設定とLocalifyファイルを保持します。
- 既存の描画制限は継続します。ビルド・配布検証は新たなVR実機確認ではなく、このPCではSCSPのDMM登録先を使った実検出も未確認です。
