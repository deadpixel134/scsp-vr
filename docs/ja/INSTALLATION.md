[English](../en/INSTALLATION.md) | [한국어](../ko/INSTALLATION.md) | [日本語](../ja/INSTALLATION.md) · [README](../../README.ja.md) · [操作方法](USAGE.md)

# インストール・更新・復旧

## インストール前に

Windows x64のDMM PC版を使用します。`imasscprism.exe`、`GameAssembly.dll`、`UnityPlayer.dll`があるフォルダーを探してください。DMMランチャーのフォルダーではなく、このゲームフォルダーを選択します。導入・更新・削除・設定保存の前にはゲームを終了してください。

[Releases](https://github.com/deadpixel134/scsp-vr/releases)から`SongPrismVR-v*.zip`一式と対応する`.zip.sha256`を入手し、リリースノートを読みます。ZIP全体をゲームフォルダー外へ展開し、インストーラー・manifest・payloadを一緒に置きます。EXEだけをゲームフォルダーにコピーしたり、ZIP内から実行したりしないでください。

ZIPを手動で検証する場合は、PowerShellで実際のファイルパスを指定します。

```powershell
Get-FileHash -LiteralPath 'C:\Downloads\SongPrismVR-vX.Y.Z.zip' -Algorithm SHA256
```

出力されたハッシュを対応する`.sha256`の値と比較し、異なる場合は再ダウンロードします。インストーラーは別途manifestに従ってパッケージ内のファイルを検証しますが、手動で入手したZIPと隣のチェックサムファイルを自動比較するものではありません。

## インストール

このソースのインストーラーは、実行ファイルの親フォルダーと有効な作業フォルダーを優先し、続いてDMMの導入一覧を読みます。`imasscprism.exe`、`GameAssembly.dll`、`UnityPlayer.dll`がそろうパスだけを自動入力します。DMM設定がない場合や破損・ロック中でも手動選択できます。異なる有効な導入先が複数あれば**参照**で選んでください。以前の配布パッケージにはこの探索がない場合があります。

1. 展開した`SongPrismVR.Installer.exe`を実行します。
2. 言語とゲームフォルダーを選び、導入済みバージョンとLocalifyの状態を確認します。
3. 状態に応じて**インストール**、**アップデート**、**再インストール**を選びます。
4. 完了後に**設定を開く**を使います。設定アプリの起動時には自動更新も確認します。

Localifyのない環境にも導入できます。既存の`scsp-localify`がある場合、`version.dll`、設定・翻訳などの保護対象ファイルを保持します。既存のDobbyとVR設定も保持します。Localifyの翻訳データを導入するものではありません。

Localifyを使う場合は、起動前に`ゲームフォルダー\scsp-config.json`のFree Cameraを無効にします。

```json
"baseFreeCamera": {
  "enable": false
}
```

既存の設定項目を編集してください。この断片でファイル全体を置き換えないでください。SCSP VRはこの値を自動で変更・復元しません。

| ゲームフォルダーからの相対パス | 用途 |
|---|---|
| `vrmod/tools/SongPrismVR.Configurator.exe` | 設定と自動更新 |
| `vrmod/config/settings.json` | 更新・削除後も保持するVR設定 |
| `vrmod/runtime/` | VRランタイムファイル |
| `vrmod/logs/runtime-bootstrap.jsonl` | ランタイム診断ログ |
| `vrmod/install-state.json` | 管理するファイルとバックアップの記録 |
| `vrmod/rollback/` | 置き換えたファイルと以前の導入状態 |

## 更新

**自動：** ゲームを終了し、導入済みの設定アプリを開きます。起動時にGitHubを確認し、対象の新しいリリースがあればZIPをダウンロード・検証して別フォルダーのインストーラーを起動します。設定アプリは閉じ、導入後に再起動します。**確認画面はなく、安定版とプレリリースの両方が対象です。** 安定版専用の更新ではありません。進行状況とエラーはステータスバーに表示します。**更新を確認**で再確認でき、ゲーム実行中は終了後に再確認して導入します。

**手動：** 新しいZIPとチェックサムを入手して別フォルダーへ展開し、新しいインストーラーで同じゲームフォルダーを選んで**アップデート**または**再インストール**します。既存設定を保持し、古い設定にない項目は互換性のある初期値を使います。ワールドドラッグは新しい初期値で有効ですが、既存の明示的な`false`設定は保持します。[操作方法](USAGE.md)を参照してください。

ダウンロードにはGitHubへの接続が必要です。失敗した場合は`%LOCALAPPDATA%\SongPrismVR\update.log`を確認するか手動更新を使用します。ゲーム実行中にランタイムファイルを置き換えないでください。

<a id="openxr-runtimes"></a>
## OpenXRランタイム

ヘッドセットとコントローラーを接続してから、デスクトップのDMMランチャーでゲームを起動します。ModはアクティブなPC OpenXRランタイムを使います。配布ZIPの導入だけではPC VR接続は設定されません。

| 使用する接続 | 確認する設定 |
|---|---|
| Virtual Desktop / VDXR | Virtual Desktopで接続し、PC StreamerでVirtual Desktop OpenXR / VDXRを選択 |
| SteamVR OpenXR | SteamVR接続を開始し、SteamVRをアクティブなOpenXRランタイムに設定 |
| Meta Quest Link / Air Link | Link接続を確立し、PCソフトでMetaのOpenXRランタイムを選択 |

これは一般的な接続設定の案内です。記録されたSCSP VR実機確認で使用したヘッドセットとランタイムの組み合わせは、リポジトリに明記されていません。**3経路は動作確認済みの互換性リストではありません。** 実装対象はD3D12対応OpenXRとTouch系の入力です。[確認範囲](../DEVELOPMENT.md)を参照してください。

ポインター入力のためゲームを最前面にします。平面パネルを確認してから、対応する3Dシーンでステレオを確認します。症状別の対処は[README](../../README.ja.md)を参照してください。

<a id="uninstall-and-rollback"></a>
## アンインストールとロールバック

1. ゲームを終了し、展開した配布フォルダーのインストーラーを起動します。
2. 導入先のゲームフォルダーを選びます。
3. **前のバージョンに戻す**が表示される場合は現在の導入段階を除き、その前の状態を復元します。全削除する場合は必要に応じて繰り返します。
4. 以前の導入がない状態で**アンインストール**を選ぶと、Mod所有のファイルを削除し、導入前のファイルを復元します。

VR設定・ログ・既存のLocalifyファイルを保持します。削除前にファイル変更・欠落・バックアップ不足を確認し、問題があれば導入状態を維持してファイル削除を進めません。`vrmod/install-state.json`と`vrmod/rollback/`を保持してください。状態記録やバックアップを失った場合、推測でローダーを削除せずエラーを報告してください。ロールバックには利用可能な以前の導入バックアップが必要です。

ゲーム更新後は[Releases](https://github.com/deadpixel134/scsp-vr/releases)を確認してからVRを使用してください。初期化失敗時も通常のゲームを継続する設計ですが、すべての失敗経路がVR実機で確認されたものではありません。
