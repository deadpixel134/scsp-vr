using System.Globalization;
using SongPrismVR.Core;

namespace SongPrismVR.Configurator;

internal enum UiLanguage
{
    Korean,
    English,
    Japanese
}

internal static class UiText
{
    private static readonly IReadOnlyDictionary<UiLanguage, IReadOnlyDictionary<string, string>> Resources =
        new Dictionary<UiLanguage, IReadOnlyDictionary<string, string>>
        {
            [UiLanguage.Korean] = Korean(),
            [UiLanguage.English] = English(),
            [UiLanguage.Japanese] = Japanese()
        };

    public static UiLanguage CurrentLanguage { get; private set; }

    public static void Initialize()
    {
        ValidateResources();
        CurrentLanguage = LoadLanguage();
    }

    public static string Get(string key)
    {
        if (Resources[CurrentLanguage].TryGetValue(key, out string? value))
        {
            return value;
        }
        throw new InvalidOperationException($"Missing UI text: {CurrentLanguage}/{key}");
    }

    public static string Format(string key, params object[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), arguments);

    public static void SetLanguage(UiLanguage language)
    {
        CurrentLanguage = language;
        try
        {
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SongPrismVR");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "ui-language.txt"), Code(language));
        }
        catch
        {
            // Language switching still works for this process if preference persistence fails.
        }
    }

    public static string Choice(VrHand value) => Get(value switch
    {
        VrHand.Left => "ChoiceLeft",
        VrHand.Right => "ChoiceRight",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    });

    public static string Choice(PanelToggleBinding value) => Get(value switch
    {
        PanelToggleBinding.Grip => "ChoiceGrip",
        PanelToggleBinding.PrimaryFace => "ChoicePrimaryFace",
        PanelToggleBinding.SecondaryFace => "ChoiceSecondaryFace",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    });

    public static string Choice(FaceButtonBinding value) => Get(value switch
    {
        FaceButtonBinding.Primary => "ChoicePrimaryFace",
        FaceButtonBinding.Secondary => "ChoiceSecondaryFace",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    });

    public static string Choice(VrViewTurnMode value) => Get(value switch
    {
        VrViewTurnMode.Smooth => "ChoiceViewTurnSmooth",
        VrViewTurnMode.Snap => "ChoiceViewTurnSnap",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    });

    public static string VisualEffect(string value) => Get(value switch
    {
        VrVisualEffectModes.Approved => "ChoiceVfxApproved",
        VrVisualEffectModes.AllOn => "ChoiceVfxAllOn",
        VrVisualEffectModes.AllOff => "ChoiceVfxAllOff",
        VrVisualEffectModes.Manual => "ChoiceVfxManual",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    });

    private static UiLanguage LoadLanguage()
    {
        try
        {
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SongPrismVR");
            string path = Path.Combine(directory, "ui-language.txt");
            if (!File.Exists(path))
            {
                path = Path.Combine(directory, "configurator-language.txt");
            }
            if (File.Exists(path))
            {
                return Parse(File.ReadAllText(path).Trim());
            }
        }
        catch
        {
            // Fall back to the Windows UI culture.
        }

        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName switch
        {
            "ja" => UiLanguage.Japanese,
            "en" => UiLanguage.English,
            _ => UiLanguage.Korean
        };
    }

    private static UiLanguage Parse(string value) => value.ToLowerInvariant() switch
    {
        "ko" => UiLanguage.Korean,
        "en" => UiLanguage.English,
        "ja" => UiLanguage.Japanese,
        _ => UiLanguage.Korean
    };

    private static string Code(UiLanguage language) => language switch
    {
        UiLanguage.Korean => "ko",
        UiLanguage.English => "en",
        UiLanguage.Japanese => "ja",
        _ => "ko"
    };

    private static void ValidateResources()
    {
        HashSet<string> reference = Resources[UiLanguage.Korean].Keys.ToHashSet(StringComparer.Ordinal);
        foreach ((UiLanguage language, IReadOnlyDictionary<string, string> resource) in Resources)
        {
            HashSet<string> keys = resource.Keys.ToHashSet(StringComparer.Ordinal);
            if (!reference.SetEquals(keys))
            {
                throw new InvalidOperationException($"Localization key mismatch: {language}");
            }
            if (resource.Any(pair => string.IsNullOrWhiteSpace(pair.Value)))
            {
                throw new InvalidOperationException($"Empty localization value: {language}");
            }
        }
    }

    private static Dictionary<string, string> Korean() => new()
    {
        ["AppTitle"] = "SongPrism VR 설정",
        ["GameFolder"] = "게임 폴더",
        ["Browse"] = "찾기...",
        ["TabRender"] = "VR/렌더",
        ["TabSpatial"] = "공간/크기",
        ["SpatialLive"] = "라이브 몰입형",
        ["SpatialNonLive"] = "비라이브 몰입형",
        ["SpatialCharacterWorldSize"] = "캐릭터/월드 크기 (%)",
        ["SpatialEyeOffset"] = "눈 간격",
        ["SpatialHeadTranslation"] = "머리 이동",
        ["SpatialLocomotion"] = "스틱 이동",
        ["SpatialAutoEye"] = "자동",
        ["SpatialAutoHead"] = "자동",
        ["SpatialAutoLocomotion"] = "자동",
        ["SpatialDescription"] = "100%는 기존 동작입니다. 자동 모드는 크기의 역수로 보정합니다. 저장 후 게임을 재시작하세요.",
        ["RuntimeStatus"] = "사용 여부",
        ["RuntimeEnabled"] = "VR 모드 사용",
        ["EyeScale"] = "눈별 렌더 배율",
        ["EyeScaleWarningElevated"] = "주의: OpenXR 권장 해상도를 초과합니다. 1.00 대비 픽셀 부하가 약 {0}%이며 3D 양안 렌더링에만 적용됩니다.",
        ["EyeScaleWarningHigh"] = "높은 GPU 부하: 1.00 대비 픽셀 부하가 약 {0}%입니다. 프레임 저하와 VRAM 사용량 증가가 발생할 수 있습니다.",
        ["EyeScaleWarningExtreme"] = "매우 높은 GPU 부하: 1.00 대비 픽셀 부하가 약 {0}%입니다. GPU 드라이버 중단(TDR)이나 게임 종료가 발생할 수 있으므로 단계적으로 올리세요.",
        ["WorldEyeScale"] = "월드 눈 간격 배율",
        ["LivePositionPolicy"] = "라이브 위치 기준",
        ["SynchronizeLivePositionEnabled"] = "게임 카메라 위치에 동기화 (기본 꺼짐)",
        ["VfxMode"] = "VFX 모드",
        ["VfxPostProcessing"] = "후처리",
        ["VfxVlBloom"] = "VL Bloom",
        ["VfxVlBloomIntensity"] = "VL Bloom 강도 (%)",
        ["VfxVlBloomDiffusion"] = "VL Bloom 확산 단계",
        ["VfxVlDof"] = "VL 심도 효과",
        ["VfxVlTextureBlur"] = "VL 텍스처 블러",
        ["VfxVlStarStreak"] = "VL 스타 스트릭",
        ["VfxVlFlare"] = "VL 플레어",
        ["EffectEnabled"] = "사용",
        ["TabPanel"] = "손 패널",
        ["PanelHand"] = "패널 손",
        ["PointerHand"] = "포인터 손",
        ["InitialState"] = "초기 상태",
        ["StartEnabled"] = "시작할 때 손 패널 표시",
        ["PanelDirection"] = "패널 방향",
        ["ViewerFacing"] = "항상 플레이어를 향함",
        ["OffsetX"] = "위치 X (m)",
        ["OffsetY"] = "위치 Y (m)",
        ["OffsetZ"] = "위치 Z (m)",
        ["MaximumWidth"] = "최대 너비 (m)",
        ["MaximumHeight"] = "최대 높이 (m)",
        ["RotationPitch"] = "회전 Pitch (도)",
        ["RotationYaw"] = "회전 Yaw (도)",
        ["RotationRoll"] = "회전 Roll (도)",
        ["VisibilityHysteresis"] = "시야 이탈 유예 (ms)",
        ["ToggleButton"] = "패널 ON/OFF 버튼",
        ["TabInput"] = "조작",
        ["ClickButton"] = "클릭 버튼",
        ["BackButton"] = "뒤로가기 버튼",
        ["Trigger"] = "트리거",
        ["TriggerEnabled"] = "트리거 클릭 사용",
        ["Scroll"] = "스크롤",
        ["ScrollEnabled"] = "스틱 스크롤 사용",
        ["ScrollSensitivity"] = "스크롤 감도",
        ["Locomotion"] = "스틱 이동",
        ["LocomotionEnabled"] = "3D 이동 및 시야 회전 사용",
        ["LocomotionHand"] = "이동 손 (반대 손은 시야 회전)",
        ["LocomotionSpeed"] = "이동 속도 (m/s)",
        ["ViewTurnMode"] = "시야 회전 방식",
        ["ViewTurnSpeed"] = "시야 회전 속도 (도/s)",
        ["ViewSnapAngle"] = "스냅 회전 각도",
        ["InputSafety"] = "입력 안전",
        ["RequireFocus"] = "게임이 활성 창일 때만 입력",
        ["Save"] = "저장",
        ["Reload"] = "다시 읽기",
        ["Defaults"] = "기본값",
        ["Export"] = "내보내기...",
        ["Import"] = "가져오기...",
        ["CheckUpdates"] = "업데이트 확인",
        ["StatusReady"] = "준비",
        ["StatusLoaded"] = "설정을 읽었습니다.",
        ["StatusSaved"] = "설정을 저장했습니다.",
        ["StatusReloaded"] = "설정을 다시 읽었습니다.",
        ["StatusDefaults"] = "기본값을 불러왔습니다. 적용하려면 저장을 누르세요.",
        ["StatusImported"] = "설정을 가져왔습니다. 게임 폴더에 적용하려면 저장을 누르세요.",
        ["StatusExported"] = "설정을 내보냈습니다.",
        ["StatusLanguageChanged"] = "표시 언어를 변경했습니다.",
        ["UpdateChecking"] = "GitHub에서 업데이트를 확인하는 중...",
        ["UpdateCurrent"] = "현재 v{0}이 최신 버전입니다.",
        ["UpdateAvailable"] = "v{0} 업데이트를 찾았습니다.",
        ["UpdateDownloading"] = "v{0} 업데이트를 다운로드하는 중...",
        ["UpdateVerifying"] = "다운로드한 업데이트를 검증하는 중...",
        ["UpdateInstalling"] = "v{0} 업데이트를 설치합니다. 설정 프로그램을 다시 시작합니다...",
        ["UpdateDeferredGameRunning"] = "v{0} 업데이트가 있지만 게임 실행 중에는 설치할 수 없습니다. 게임 종료 후 다시 확인하세요.",
        ["UpdateCompleted"] = "v{0} 자동 업데이트를 완료했습니다.",
        ["UpdateFailed"] = "자동 업데이트 실패: {0}",
        ["UpdateLauncherFailed"] = "자동 업데이트 설치 프로그램을 시작하지 못했습니다.",
        ["ErrorPrefix"] = "오류: {0}",
        ["SelectGameFolder"] = "imasscprism.exe가 있는 폴더를 선택하세요.",
        ["JsonOpenFilter"] = "JSON 설정 (*.json)|*.json|모든 파일 (*.*)|*.*",
        ["JsonSaveFilter"] = "JSON 설정 (*.json)|*.json",
        ["InvalidSettingsFile"] = "설정 파일에 유효하지 않은 값이 있습니다: {0}",
        ["GameExeMissing"] = "선택한 폴더에서 imasscprism.exe를 찾지 못했습니다.",
        ["GameRunning"] = "게임이 실행 중입니다. 게임을 완전히 종료한 뒤 설정을 저장해 주세요.",
        ["InvalidSettingsToSave"] = "저장할 수 없는 설정입니다: {0}",
        ["SettingsPathNoParent"] = "설정 경로의 상위 폴더가 없습니다.",
        ["ChoiceLeft"] = "왼손",
        ["ChoiceRight"] = "오른손",
        ["ChoiceGrip"] = "그립",
        ["ChoicePrimaryFace"] = "주 표면 버튼 (A/X)",
        ["ChoiceSecondaryFace"] = "보조 표면 버튼 (B/Y)",
        ["ChoiceViewTurnSmooth"] = "부드러운 연속 회전",
        ["ChoiceViewTurnSnap"] = "스냅 회전",
        ["ChoiceVfxApproved"] = "기존 URP VFX (비권장)",
        ["ChoiceVfxAllOn"] = "모든 VFX 켜기",
        ["ChoiceVfxAllOff"] = "모든 VFX 끄기",
        ["ChoiceVfxManual"] = "수동 설정"
    };

    private static Dictionary<string, string> English() => new()
    {
        ["AppTitle"] = "SongPrism VR Settings",
        ["GameFolder"] = "Game folder",
        ["Browse"] = "Browse...",
        ["TabRender"] = "VR / Rendering",
        ["TabSpatial"] = "Space / Scale",
        ["SpatialLive"] = "Live immersive",
        ["SpatialNonLive"] = "Non-live immersive",
        ["SpatialCharacterWorldSize"] = "Character / world size (%)",
        ["SpatialEyeOffset"] = "Eye offset",
        ["SpatialHeadTranslation"] = "Head movement",
        ["SpatialLocomotion"] = "Thumbstick movement",
        ["SpatialAutoEye"] = "Automatic",
        ["SpatialAutoHead"] = "Automatic",
        ["SpatialAutoLocomotion"] = "Automatic",
        ["SpatialDescription"] = "100% matches the existing behavior. Automatic mode uses the inverse size multiplier. Restart the game after saving.",
        ["RuntimeStatus"] = "Status",
        ["RuntimeEnabled"] = "Enable VR mode",
        ["EyeScale"] = "Per-eye render scale",
        ["EyeScaleWarningElevated"] = "Caution: This exceeds the OpenXR-recommended resolution. Pixel load is about {0}% of 1.00 and affects 3D stereo rendering only.",
        ["EyeScaleWarningHigh"] = "High GPU load: Pixel load is about {0}% of 1.00. Frame rate may drop and VRAM use may increase.",
        ["EyeScaleWarningExtreme"] = "Extreme GPU load: Pixel load is about {0}% of 1.00. A GPU timeout (TDR) or game exit may occur; increase the value gradually.",
        ["WorldEyeScale"] = "World eye-offset scale",
        ["LivePositionPolicy"] = "Live position policy",
        ["SynchronizeLivePositionEnabled"] = "Synchronize to game camera position (off by default)",
        ["VfxMode"] = "VFX mode",
        ["VfxPostProcessing"] = "Post-processing",
        ["VfxVlBloom"] = "VL Bloom",
        ["VfxVlBloomIntensity"] = "VL Bloom intensity (%)",
        ["VfxVlBloomDiffusion"] = "VL Bloom diffusion step",
        ["VfxVlDof"] = "VL depth of field",
        ["VfxVlTextureBlur"] = "VL texture blur",
        ["VfxVlStarStreak"] = "VL star streak",
        ["VfxVlFlare"] = "VL flare",
        ["EffectEnabled"] = "Enabled",
        ["TabPanel"] = "Hand panel",
        ["PanelHand"] = "Panel hand",
        ["PointerHand"] = "Pointer hand",
        ["InitialState"] = "Initial state",
        ["StartEnabled"] = "Show the hand panel on startup",
        ["PanelDirection"] = "Panel orientation",
        ["ViewerFacing"] = "Always face the player",
        ["OffsetX"] = "Position X (m)",
        ["OffsetY"] = "Position Y (m)",
        ["OffsetZ"] = "Position Z (m)",
        ["MaximumWidth"] = "Maximum width (m)",
        ["MaximumHeight"] = "Maximum height (m)",
        ["RotationPitch"] = "Pitch rotation (degrees)",
        ["RotationYaw"] = "Yaw rotation (degrees)",
        ["RotationRoll"] = "Roll rotation (degrees)",
        ["VisibilityHysteresis"] = "Out-of-view grace period (ms)",
        ["ToggleButton"] = "Panel ON/OFF button",
        ["TabInput"] = "Input",
        ["ClickButton"] = "Click button",
        ["BackButton"] = "Back button",
        ["Trigger"] = "Trigger",
        ["TriggerEnabled"] = "Enable trigger click",
        ["Scroll"] = "Scrolling",
        ["ScrollEnabled"] = "Enable thumbstick scrolling",
        ["ScrollSensitivity"] = "Scroll sensitivity",
        ["Locomotion"] = "Thumbstick locomotion",
        ["LocomotionEnabled"] = "Enable 3D movement and view rotation",
        ["LocomotionHand"] = "Movement hand (other hand turns view)",
        ["LocomotionSpeed"] = "Movement speed (m/s)",
        ["ViewTurnMode"] = "View turn mode",
        ["ViewTurnSpeed"] = "View turn speed (deg/s)",
        ["ViewSnapAngle"] = "Snap turn angle",
        ["InputSafety"] = "Input safety",
        ["RequireFocus"] = "Inject input only while the game is focused",
        ["Save"] = "Save",
        ["Reload"] = "Reload",
        ["Defaults"] = "Defaults",
        ["Export"] = "Export...",
        ["Import"] = "Import...",
        ["CheckUpdates"] = "Check for updates",
        ["StatusReady"] = "Ready",
        ["StatusLoaded"] = "Settings loaded.",
        ["StatusSaved"] = "Settings saved.",
        ["StatusReloaded"] = "Settings reloaded.",
        ["StatusDefaults"] = "Defaults loaded. Click Save to apply them.",
        ["StatusImported"] = "Settings imported. Click Save to apply them to the game folder.",
        ["StatusExported"] = "Settings exported.",
        ["StatusLanguageChanged"] = "Display language changed.",
        ["UpdateChecking"] = "Checking GitHub for updates...",
        ["UpdateCurrent"] = "v{0} is up to date.",
        ["UpdateAvailable"] = "Update v{0} is available.",
        ["UpdateDownloading"] = "Downloading update v{0}...",
        ["UpdateVerifying"] = "Verifying the downloaded update...",
        ["UpdateInstalling"] = "Installing update v{0}. The settings app will restart...",
        ["UpdateDeferredGameRunning"] = "Update v{0} is available, but it cannot be installed while the game is running. Close the game and check again.",
        ["UpdateCompleted"] = "Automatic update to v{0} completed.",
        ["UpdateFailed"] = "Automatic update failed: {0}",
        ["UpdateLauncherFailed"] = "The automatic update installer could not be started.",
        ["ErrorPrefix"] = "Error: {0}",
        ["SelectGameFolder"] = "Select the folder containing imasscprism.exe.",
        ["JsonOpenFilter"] = "JSON settings (*.json)|*.json|All files (*.*)|*.*",
        ["JsonSaveFilter"] = "JSON settings (*.json)|*.json",
        ["InvalidSettingsFile"] = "The settings file contains invalid values: {0}",
        ["GameExeMissing"] = "imasscprism.exe was not found in the selected folder.",
        ["GameRunning"] = "The game is running. Fully close it before saving settings.",
        ["InvalidSettingsToSave"] = "These settings cannot be saved: {0}",
        ["SettingsPathNoParent"] = "The settings path has no parent folder.",
        ["ChoiceLeft"] = "Left hand",
        ["ChoiceRight"] = "Right hand",
        ["ChoiceGrip"] = "Grip",
        ["ChoicePrimaryFace"] = "Primary face button (A/X)",
        ["ChoiceSecondaryFace"] = "Secondary face button (B/Y)",
        ["ChoiceViewTurnSmooth"] = "Smooth continuous turn",
        ["ChoiceViewTurnSnap"] = "Snap turn",
        ["ChoiceVfxApproved"] = "Legacy URP VFX (not recommended)",
        ["ChoiceVfxAllOn"] = "All VFX on",
        ["ChoiceVfxAllOff"] = "All VFX off",
        ["ChoiceVfxManual"] = "Manual settings"
    };

    private static Dictionary<string, string> Japanese() => new()
    {
        ["AppTitle"] = "SongPrism VR 設定",
        ["GameFolder"] = "ゲームフォルダー",
        ["Browse"] = "参照...",
        ["TabRender"] = "VR・レンダリング",
        ["TabSpatial"] = "空間・サイズ",
        ["SpatialLive"] = "ライブ没入型",
        ["SpatialNonLive"] = "非ライブ没入型",
        ["SpatialCharacterWorldSize"] = "キャラクター／ワールドサイズ (%)",
        ["SpatialEyeOffset"] = "眼間オフセット",
        ["SpatialHeadTranslation"] = "頭部移動",
        ["SpatialLocomotion"] = "スティック移動",
        ["SpatialAutoEye"] = "自動",
        ["SpatialAutoHead"] = "自動",
        ["SpatialAutoLocomotion"] = "自動",
        ["SpatialDescription"] = "100%は従来動作です。自動ではサイズの逆数で補正します。保存後にゲームを再起動してください。",
        ["RuntimeStatus"] = "使用設定",
        ["RuntimeEnabled"] = "VRモードを使用",
        ["EyeScale"] = "片目レンダー倍率",
        ["EyeScaleWarningElevated"] = "注意：OpenXRの推奨解像度を超えています。ピクセル負荷は1.00の約{0}%で、3Dステレオ描画のみに適用されます。",
        ["EyeScaleWarningHigh"] = "GPU負荷が高い設定です。ピクセル負荷は1.00の約{0}%です。フレームレート低下やVRAM使用量増加の可能性があります。",
        ["EyeScaleWarningExtreme"] = "GPU負荷が非常に高い設定です。ピクセル負荷は1.00の約{0}%です。GPUタイムアウト（TDR）やゲーム終了の可能性があるため、段階的に上げてください。",
        ["WorldEyeScale"] = "ワールド眼間オフセット倍率",
        ["LivePositionPolicy"] = "ライブ位置基準",
        ["SynchronizeLivePositionEnabled"] = "ゲームカメラ位置に同期（デフォルトOFF）",
        ["VfxMode"] = "VFXモード",
        ["VfxPostProcessing"] = "ポストプロセス",
        ["VfxVlBloom"] = "VL Bloom",
        ["VfxVlBloomIntensity"] = "VL Bloom 強度 (%)",
        ["VfxVlBloomDiffusion"] = "VL Bloom 拡散ステップ",
        ["VfxVlDof"] = "VL 被写界深度",
        ["VfxVlTextureBlur"] = "VL テクスチャブラー",
        ["VfxVlStarStreak"] = "VL スターストリーク",
        ["VfxVlFlare"] = "VL フレア",
        ["EffectEnabled"] = "使用",
        ["TabPanel"] = "ハンドパネル",
        ["PanelHand"] = "パネルを持つ手",
        ["PointerHand"] = "ポインターを操作する手",
        ["InitialState"] = "初期状態",
        ["StartEnabled"] = "起動時にハンドパネルを表示",
        ["PanelDirection"] = "パネルの向き",
        ["ViewerFacing"] = "常にプレイヤーの方を向く",
        ["OffsetX"] = "位置 X (m)",
        ["OffsetY"] = "位置 Y (m)",
        ["OffsetZ"] = "位置 Z (m)",
        ["MaximumWidth"] = "最大幅 (m)",
        ["MaximumHeight"] = "最大高さ (m)",
        ["RotationPitch"] = "ピッチ回転 (度)",
        ["RotationYaw"] = "ヨー回転 (度)",
        ["RotationRoll"] = "ロール回転 (度)",
        ["VisibilityHysteresis"] = "視野外表示猶予 (ms)",
        ["ToggleButton"] = "パネル表示切替ボタン",
        ["TabInput"] = "操作",
        ["ClickButton"] = "クリックボタン",
        ["BackButton"] = "戻るボタン",
        ["Trigger"] = "トリガー",
        ["TriggerEnabled"] = "トリガークリックを使用",
        ["Scroll"] = "スクロール",
        ["ScrollEnabled"] = "スティックスクロールを使用",
        ["ScrollSensitivity"] = "スクロール感度",
        ["Locomotion"] = "スティック移動",
        ["LocomotionEnabled"] = "3D移動と視点回転を使用",
        ["LocomotionHand"] = "移動側（反対側は視点回転）",
        ["LocomotionSpeed"] = "移動速度 (m/s)",
        ["ViewTurnMode"] = "視点回転方式",
        ["ViewTurnSpeed"] = "視点回転速度 (度/s)",
        ["ViewSnapAngle"] = "スナップ回転角度",
        ["InputSafety"] = "入力の安全設定",
        ["RequireFocus"] = "ゲームがアクティブな時のみ入力",
        ["Save"] = "保存",
        ["Reload"] = "再読み込み",
        ["Defaults"] = "初期値",
        ["Export"] = "エクスポート...",
        ["Import"] = "インポート...",
        ["CheckUpdates"] = "更新を確認",
        ["StatusReady"] = "準備完了",
        ["StatusLoaded"] = "設定を読み込みました。",
        ["StatusSaved"] = "設定を保存しました。",
        ["StatusReloaded"] = "設定を再読み込みしました。",
        ["StatusDefaults"] = "初期値を読み込みました。適用するには保存を押してください。",
        ["StatusImported"] = "設定をインポートしました。ゲームフォルダーに適用するには保存を押してください。",
        ["StatusExported"] = "設定をエクスポートしました。",
        ["StatusLanguageChanged"] = "表示言語を変更しました。",
        ["UpdateChecking"] = "GitHubで更新を確認しています...",
        ["UpdateCurrent"] = "現在のv{0}は最新です。",
        ["UpdateAvailable"] = "v{0}の更新があります。",
        ["UpdateDownloading"] = "v{0}をダウンロードしています...",
        ["UpdateVerifying"] = "ダウンロードした更新を検証しています...",
        ["UpdateInstalling"] = "v{0}をインストールします。設定アプリを再起動します...",
        ["UpdateDeferredGameRunning"] = "v{0}の更新がありますが、ゲーム実行中はインストールできません。ゲーム終了後に再確認してください。",
        ["UpdateCompleted"] = "v{0}への自動更新が完了しました。",
        ["UpdateFailed"] = "自動更新に失敗しました: {0}",
        ["UpdateLauncherFailed"] = "自動更新インストーラーを起動できませんでした。",
        ["ErrorPrefix"] = "エラー: {0}",
        ["SelectGameFolder"] = "imasscprism.exeがあるフォルダーを選択してください。",
        ["JsonOpenFilter"] = "JSON設定 (*.json)|*.json|すべてのファイル (*.*)|*.*",
        ["JsonSaveFilter"] = "JSON設定 (*.json)|*.json",
        ["InvalidSettingsFile"] = "設定ファイルに無効な値があります: {0}",
        ["GameExeMissing"] = "選択したフォルダーにimasscprism.exeが見つかりません。",
        ["GameRunning"] = "ゲームが実行中です。完全に終了してから設定を保存してください。",
        ["InvalidSettingsToSave"] = "この設定は保存できません: {0}",
        ["SettingsPathNoParent"] = "設定パスに親フォルダーがありません。",
        ["ChoiceLeft"] = "左手",
        ["ChoiceRight"] = "右手",
        ["ChoiceGrip"] = "グリップ",
        ["ChoicePrimaryFace"] = "メインフェイスボタン (A/X)",
        ["ChoiceSecondaryFace"] = "サブフェイスボタン (B/Y)",
        ["ChoiceViewTurnSmooth"] = "スムーズ連続回転",
        ["ChoiceViewTurnSnap"] = "スナップ回転",
        ["ChoiceVfxApproved"] = "従来URP VFX（非推奨）",
        ["ChoiceVfxAllOn"] = "すべてのVFXをオン",
        ["ChoiceVfxAllOff"] = "すべてのVFXをオフ",
        ["ChoiceVfxManual"] = "手動設定"
    };
}
