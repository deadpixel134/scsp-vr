using System.Text.Json;
using BepInEx.Logging;
using SongPrismVR.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Video;

namespace SongPrismVR.Diagnostic;

public sealed class DiagnosticBehaviour : MonoBehaviour
{
    private const float PollIntervalSeconds = 1.0f;
    private const float HeartbeatIntervalSeconds = 10.0f;

    private readonly SceneClassifier _classifier = new();
    private ManualLogSource? _log;
    private string? _outputPath;
    private float _nextPollAt;
    private float _nextHeartbeatAt;
    private string _previousScene = string.Empty;
    private string _previousRenderSignature = string.Empty;
    private string _previousEventSignature = string.Empty;
    private OrientationKind _previousOrientation = OrientationKind.Unknown;
    private int _stableFrameCount;

    public DiagnosticBehaviour(IntPtr pointer)
        : base(pointer)
    {
    }

    public void Initialize(ManualLogSource log, string outputPath)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _outputPath = outputPath ?? throw new ArgumentNullException(nameof(outputPath));
        DontDestroyOnLoad(gameObject);
        WriteLifecycleEvent("diagnostic-start");
    }

    private void Update()
    {
        if (_log is null || _outputPath is null || Time.unscaledTime < _nextPollAt)
        {
            return;
        }

        _nextPollAt = Time.unscaledTime + PollIntervalSeconds;

        try
        {
            DiagnosticSnapshot snapshot = CaptureSnapshot();
            bool heartbeatDue = Time.unscaledTime >= _nextHeartbeatAt;
            string eventSignature = snapshot.EventSignature();
            if (heartbeatDue || !string.Equals(eventSignature, _previousEventSignature, StringComparison.Ordinal))
            {
                AppendJsonLine(snapshot);
                _previousEventSignature = eventSignature;
                _nextHeartbeatAt = Time.unscaledTime + HeartbeatIntervalSeconds;
            }
        }
        catch (Exception exception)
        {
            _log.LogError($"Diagnostic capture failed: {exception}");
        }
    }

    private DiagnosticSnapshot CaptureSnapshot()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        string sceneName = activeScene.IsValid() ? activeScene.name : "<invalid>";
        int width = Screen.width;
        int height = Screen.height;
        OrientationKind orientation = GetOrientation(width, height);

        Camera[] cameras = Camera.allCameras;
        Canvas[] canvases = Resources.FindObjectsOfTypeAll<Canvas>();
        VideoPlayer[] videoPlayers = Resources.FindObjectsOfTypeAll<VideoPlayer>();

        List<string> cameraNames = new();
        HashSet<string> markerNames = new(StringComparer.Ordinal);
        int worldCameraCount = 0;
        bool hasCinemachineOutput = false;
        bool hasLiveCameraMarker = false;
        bool hasUguissMarker = false;
        bool hasUrpCameraData = false;

        foreach (Camera camera in cameras)
        {
            if (camera is null || !camera.enabled || !camera.gameObject.activeInHierarchy)
            {
                continue;
            }

            string cameraName = camera.name ?? "<unnamed>";
            cameraNames.Add(cameraName);
            if (camera.cameraType == CameraType.Game && camera.cullingMask != 0)
            {
                worldCameraCount++;
            }

            foreach (Component component in camera.gameObject.GetComponents<Component>())
            {
                if (component is null)
                {
                    continue;
                }

                string typeName = component.GetType().FullName ?? component.GetType().Name;
                if (IsInterestingMarker(typeName))
                {
                    markerNames.Add(typeName);
                }

                hasCinemachineOutput |= Contains(typeName, "Cinemachine");
                hasLiveCameraMarker |= Contains(typeName, "LiveCamera") || Contains(typeName, "VLSRPCamera");
                hasUguissMarker |= Contains(typeName, "Uguiss");
                hasUrpCameraData |= Contains(typeName, "UniversalAdditionalCameraData");
            }
        }

        int activeCanvasCount = 0;
        int overlayCanvasCount = 0;
        foreach (Canvas canvas in canvases)
        {
            if (canvas is null || !canvas.enabled || !canvas.gameObject.activeInHierarchy)
            {
                continue;
            }

            activeCanvasCount++;
            if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                overlayCanvasCount++;
            }
        }

        int playingVideoCount = 0;
        foreach (VideoPlayer player in videoPlayers)
        {
            if (player is not null && player.enabled && player.gameObject.activeInHierarchy && player.isPlaying)
            {
                playingVideoCount++;
            }
        }

        cameraNames.Sort(StringComparer.Ordinal);
        string renderSignature = string.Join("|", new[]
        {
            sceneName,
            width.ToString(),
            height.ToString(),
            string.Join(",", cameraNames),
            activeCanvasCount.ToString(),
            playingVideoCount.ToString()
        });

        bool sceneChanging = _previousScene.Length > 0 && !string.Equals(sceneName, _previousScene, StringComparison.Ordinal);
        bool orientationChanging = _previousOrientation != OrientationKind.Unknown && orientation != _previousOrientation;
        bool renderTargetsStable = string.Equals(renderSignature, _previousRenderSignature, StringComparison.Ordinal);
        _stableFrameCount = renderTargetsStable ? _stableFrameCount + 1 : 0;

        RenderObservation observation = new()
        {
            IsOrientationChanging = orientationChanging,
            IsSceneChanging = sceneChanging,
            AreRenderTargetsStable = renderTargetsStable,
            StableFrameCount = _stableFrameCount,
            HasFullScreenVideo = playingVideoCount > 0,
            HasActiveWebView = false,
            HasLiveCameraMarker = hasLiveCameraMarker,
            HasUguissMarker = hasUguissMarker,
            HasCinemachineOutput = hasCinemachineOutput,
            HasValidWorldCamera = worldCameraCount > 0,
            IsUrpCameraStackValid = hasUrpCameraData || worldCameraCount == 1,
            UiCanvasDominates = worldCameraCount == 0 || overlayCanvasCount > 0 && activeCanvasCount >= worldCameraCount * 2,
            IsProfileApproved = false,
            WorldCameraCount = worldCameraCount
        };

        ClassificationResult classification = _classifier.Classify(observation);
        _previousScene = sceneName;
        _previousOrientation = orientation;
        _previousRenderSignature = renderSignature;

        return new DiagnosticSnapshot
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            Scene = sceneName,
            Width = width,
            Height = height,
            Orientation = orientation.ToString(),
            ScreenOrientation = Screen.orientation.ToString(),
            CameraCount = cameras.Length,
            ActiveCameraNames = cameraNames,
            WorldCameraCount = worldCameraCount,
            ActiveCanvasCount = activeCanvasCount,
            OverlayCanvasCount = overlayCanvasCount,
            PlayingVideoCount = playingVideoCount,
            MarkerTypeNames = markerNames.OrderBy(name => name, StringComparer.Ordinal).ToList(),
            StableFrameCount = _stableFrameCount,
            Context = classification.Context.ToString(),
            RecommendedMode = classification.Mode.ToString(),
            Reason = classification.Reason
        };
    }

    private void WriteLifecycleEvent(string eventName)
    {
        AppendJsonLine(new
        {
            timestampUtc = DateTimeOffset.UtcNow,
            eventName,
            pluginVersion = DiagnosticPlugin.PluginVersion
        });
    }

    private void AppendJsonLine<T>(T value)
    {
        if (_outputPath is null)
        {
            return;
        }

        string json = JsonSerializer.Serialize(value, JsonOptions.Instance);
        File.AppendAllText(_outputPath, json + Environment.NewLine);
    }

    private static OrientationKind GetOrientation(int width, int height)
    {
        if (width <= 0 || height <= 0 || width == height)
        {
            return OrientationKind.Unknown;
        }

        return height > width ? OrientationKind.Portrait : OrientationKind.Landscape;
    }

    private static bool IsInterestingMarker(string typeName) =>
        Contains(typeName, "Camera") ||
        Contains(typeName, "Uguiss") ||
        Contains(typeName, "Cinemachine") ||
        Contains(typeName, "RenderPipeline");

    private static bool Contains(string value, string fragment) =>
        value.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0;
}
