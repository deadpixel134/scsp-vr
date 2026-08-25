[한국어](README.md) | [English](README.en.md) | [日本語](README.ja.md)

# SCSP VR

制作者: [@TBluebox12](https://x.com/TBluebox12)\
Arca.live VRチャンネル: [VRチャンネル](https://arca.live/b/vrshits)\
支援: [buymeacoffee.com/vrshits](https://buymeacoffee.com/vrshits)\

**アイドルマスター シャイニーカラーズ Song for Prism** DMM PC版向けの非公式OpenXR VR Modです。

[不具合報告](https://github.com/deadpixel134/scsp-vr/issues)

## 現在のリリース

現在の公開ビルドはプレリリース [`v0.1.1-preview.1`](https://github.com/deadpixel134/scsp-vr/releases/tag/v0.1.1-preview.1) です。Windows x64、DMM PC版、OpenXRランタイムを対象としています。

このプレリリースは実際のゲームとHMDで引き続き検証中です。ゲームやOpenXRランタイムの更新により動作が変わる可能性があるため、インストール前にリリースノートをご確認ください。

## 主な機能

- ゲームカメラをOpenXRステレオビューとしてレンダリング
- 縦画面・横画面・ライブシーンに応じたVR表示
- OpenXRコントローラーポインターとVR移動・視点設定
- 韓国語・英語・日本語対応の設定アプリとインストーラー
- `scsp-localify` の `version.dll`、設定、翻訳データを保持した共存インストール
- 韓国語パッチのないクリーンなゲームフォルダーにもインストール可能
- SHA-256検証、ロールバック、変更済みファイル保護を備えたインストール・削除
- GitHubの安定版とプレリリースに対応する自動更新

## インストール

1. [Releases](https://github.com/deadpixel134/scsp-vr/releases) から最新の `SongPrismVR-v*.zip` と同名の `.sha256` ファイルをダウンロードします。
2. ZIPを任意のフォルダーへ完全に展開します。
3. ゲームを終了し、`SongPrismVR.Installer.exe` を実行します。
4. `imasscprism.exe` があるゲームフォルダーを確認し、**インストール**を選択します。
5. インストール後、ゲームフォルダー内の `vrmod/tools/SongPrismVR.Configurator.exe` でOpenXRと操作を設定できます。

更新が見つかると、設定アプリはリリースZIPをダウンロードしてSHA-256を検証し、別のステージングフォルダーからインストーラーを起動します。ゲーム実行中は更新しません。

## アンインストールとLocalify共存

インストーラーの**アンインストール**を使用してください。インストール前のファイルはロールバック用に保持され、ユーザーが変更したファイルを推測で削除しません。`scsp-localify` の有無にかかわらずインストールでき、既存のLocalifyファイルとVR設定を保持します。

## 重要な制限事項

- DMM PC版専用です。Steam版・モバイル版には対応していません。
- プレリリースのため、すべてのHMD、OpenXRランタイム、グラフィック設定の組み合わせを保証しません。
- ゲーム更新後は、互換性が確認されるまでModを削除するか新しいリリースをお待ちください。
- VRランタイムの初期化失敗時もゲームを続行する設計ですが、すべての失敗経路が実機承認済みではありません。
- ゲームファイル、ゲームアセット、`scsp-localify` の翻訳データはリポジトリやリリースに含みません。

## 開発

管理・インストールポリシーのテスト:

```powershell
dotnet run --project tests/SongPrismVR.Management.Tests/SongPrismVR.Management.Tests.csproj -c Release
```

コアポリシーのテスト:

```powershell
dotnet run --project tests/SongPrismVR.Core.Tests/SongPrismVR.Core.Tests.csproj -c Release
```

配布ビルドには、対応ゲームワークスペース内で `scripts/Build-DistributionPackage.ps1` を使用します。ゲームおよび第三者バイナリはリポジトリへコミットしません。

## ライセンスとクレジット

SCSP VRのソースは [GNU General Public License v3.0](LICENSE) で配布されます。同梱されるOpenXR Loader、.NET Runtime、Unity Doorstop、Dobbyにはそれぞれのライセンスが適用されます。正確な出典と条件は [CREDITS.md](CREDITS.md) および [THIRD_PARTY_NOTICES.txt](release-assets/THIRD_PARTY_NOTICES.txt) をご確認ください。

本プロジェクトは非公式のファンプロジェクトであり、Bandai Namco Entertainment、Bandai Namco Studios、THE IDOLM@STER、その他の権利者とは提携しておらず、承認も受けていません。ゲーム名、キャラクター、ロゴ、商標、ゲームデータの権利は各権利者に帰属します。
