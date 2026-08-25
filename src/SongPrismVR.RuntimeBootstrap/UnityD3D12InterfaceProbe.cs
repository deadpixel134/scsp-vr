using System.Runtime.InteropServices;
using SongPrismVR.Core;

namespace Doorstop;

internal readonly record struct D3D12EyeCopyStatus(
    uint NativeSchema,
    uint State,
    uint Hresult,
    long PresentationGeneration,
    ulong Sequence,
    ulong CompletionFenceValue,
    ulong CompletedFenceValue)
{
    internal bool IsPending => State is 1 or 2;
    internal bool IsCompleted => State == 3;
    internal bool IsFailed => State == 4;
    internal bool IsCanceled => State == 5;
}

internal readonly record struct D3D12EyeCopyTelemetry(
    uint Flags,
    uint State,
    uint LastStage,
    uint Hresult,
    uint FailureStage,
    long PresentationGeneration,
    ulong Sequence,
    ulong TargetFenceValue,
    ulong FirstCompletedFenceValue,
    ulong LatestCompletedFenceValue);

internal sealed class UnityD3D12InterfaceProbe
{
    private const uint PluginLoadFlag = 0x00000001;
    private const uint RenderEventFlag = 0x00000002;
    private const uint InterfaceFoundFlag = 0x00000004;
    private const int ProbeEventId = 0x53505652;
    private const int ResultTimeoutMilliseconds = 2_000;
    private const int ProbeSnapshotSize = 72;
    private const uint EyeCopySchema = 2;
    private const int EyeCopyRequestSize = 80;
    private const int EyeCopyStatusSize = 48;
    private const int EyeCopyTelemetrySize = 72;
    private const string ProbeId = "PROBE-UNITY-D3D12-PLUGIN-HOOK-ABI-001";

    private static readonly object InstallLock = new();
    private static IntPtr _sharedLibrary;
    private static IntPtr _criLibrary;
    private static IntPtr _dobbyLibrary;
    private static UnityPluginLoadDelegate? _probeUnityPluginLoad;
    private static UnityPluginLoadDelegate? _criUnityPluginLoadOriginal;
    private static QueueEyeCopyDelegate? _queueEyeCopy;
    private static PollEyeCopyDelegate? _pollEyeCopy;
    private static GetEyeCopyTelemetryDelegate? _getEyeCopyTelemetry;
    private static CancelEyeCopyDelegate? _cancelEyeCopy;
    private static EyeCopyNeedsRenderEventDelegate? _eyeCopyNeedsRenderEvent;
    private static readonly UnityPluginLoadDelegate CriUnityPluginLoadReplacement =
        OnCriUnityPluginLoad;
    private static string? _earlyHookError;
    private static bool _earlyInstallAttempted;
    private static long _bridgePresentationGeneration;

    private IntPtr _library;
    private GetRenderEventFuncDelegate? _getRenderEventFunc;
    private GetProbeSnapshotDelegate? _getSnapshot;
    private long _generation;
    private long _scheduledMilliseconds;
    private bool _terminal;

    internal static void InstallEarly(string gameRoot, string logPath)
    {
        lock (InstallLock)
        {
            if (_earlyInstallAttempted)
            {
                return;
            }

            _earlyInstallAttempted = true;
            int hookResult = int.MinValue;
            IntPtr original = IntPtr.Zero;
            try
            {
                _sharedLibrary = NativeLibrary.Load(Path.Combine(
                    gameRoot,
                    "vrmod",
                    "runtime",
                    "SongPrismVR.UnityD3D12Probe.dll"));
                _probeUnityPluginLoad = Marshal.GetDelegateForFunctionPointer<UnityPluginLoadDelegate>(
                    NativeLibrary.GetExport(_sharedLibrary, "UnityPluginLoad"));

                _criLibrary = NativeLibrary.Load(Path.Combine(
                    gameRoot,
                    "imasscprism_Data",
                    "Plugins",
                    "x86_64",
                    "cri_ware_unity.dll"));
                IntPtr criPluginLoad = NativeLibrary.GetExport(_criLibrary, "UnityPluginLoad");

                _dobbyLibrary = NativeLibrary.Load(Path.Combine(
                    gameRoot,
                    "BepInEx",
                    "core",
                    "dobby.dll"));
                DobbyHookDelegate hook = Marshal.GetDelegateForFunctionPointer<DobbyHookDelegate>(
                    NativeLibrary.GetExport(_dobbyLibrary, "DobbyHook"));
                hookResult = hook(
                    criPluginLoad,
                    Marshal.GetFunctionPointerForDelegate(CriUnityPluginLoadReplacement),
                    out original);
                if (hookResult != 0 || original == IntPtr.Zero)
                {
                    throw new InvalidOperationException(
                        $"DobbyHook failed for cri_ware_unity!UnityPluginLoad: " +
                        $"result={hookResult}, original=0x{original.ToInt64():x}.");
                }

                _criUnityPluginLoadOriginal =
                    Marshal.GetDelegateForFunctionPointer<UnityPluginLoadDelegate>(original);
            }
            catch (Exception exception)
            {
                _earlyHookError = $"{exception.GetType().FullName}: {exception.Message}";
            }

            RuntimeProbe.Append(logPath, new ProbeEvent
            {
                TimestampUtc = DateTimeOffset.UtcNow,
                Event = "unity-d3d12-plugin-load-hook-status",
                BootstrapVersion = RuntimeProbe.BootstrapVersion,
                ProcessId = Environment.ProcessId,
                Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                Error = _earlyHookError,
                Reason = $"probeId={ProbeId};hookResult={hookResult};" +
                    $"original=0x{original.ToInt64():x};" +
                    $"installed={_criUnityPluginLoadOriginal is not null};" +
                    "target=cri_ware_unity!UnityPluginLoad;forwardOriginalFirst=true."
            });
        }
    }

    internal void Advance(
        Action<IntPtr, int> issuePluginEvent,
        string gameRoot,
        string logPath,
        long presentationGeneration)
    {
        if (presentationGeneration <= 0)
        {
            return;
        }

        if (_generation != presentationGeneration)
        {
            ResetForGeneration(presentationGeneration);
        }

        string stage = "initialize";
        try
        {
            if (_library == IntPtr.Zero)
            {
                stage = "resolve-native-probe";
                if (_sharedLibrary == IntPtr.Zero)
                {
                    InstallEarly(gameRoot, logPath);
                }
                _library = _sharedLibrary != IntPtr.Zero
                    ? _sharedLibrary
                    : throw new InvalidOperationException(
                        _earlyHookError ?? "The early Unity plugin hook was not installed.");
                _getRenderEventFunc = Marshal.GetDelegateForFunctionPointer<GetRenderEventFuncDelegate>(
                    NativeLibrary.GetExport(_library, "spvr_get_render_event_func"));
                _getSnapshot = Marshal.GetDelegateForFunctionPointer<GetProbeSnapshotDelegate>(
                    NativeLibrary.GetExport(_library, "spvr_get_probe_snapshot"));
                _queueEyeCopy = Marshal.GetDelegateForFunctionPointer<QueueEyeCopyDelegate>(
                    NativeLibrary.GetExport(_library, "spvr_queue_eye_copy"));
                _pollEyeCopy = Marshal.GetDelegateForFunctionPointer<PollEyeCopyDelegate>(
                    NativeLibrary.GetExport(_library, "spvr_poll_eye_copy"));
                _getEyeCopyTelemetry =
                    Marshal.GetDelegateForFunctionPointer<GetEyeCopyTelemetryDelegate>(
                        NativeLibrary.GetExport(_library, "spvr_get_eye_copy_telemetry"));
                _cancelEyeCopy = Marshal.GetDelegateForFunctionPointer<CancelEyeCopyDelegate>(
                    NativeLibrary.GetExport(_library, "spvr_cancel_eye_copy"));
                _eyeCopyNeedsRenderEvent =
                    Marshal.GetDelegateForFunctionPointer<EyeCopyNeedsRenderEventDelegate>(
                        NativeLibrary.GetExport(_library, "spvr_eye_copy_needs_render_event"));
                if (Marshal.SizeOf<UnityD3D12ProbeSnapshot>() != ProbeSnapshotSize)
                {
                    throw new InvalidOperationException(
                        "The managed Unity D3D12 probe snapshot ABI size is invalid.");
                }
                if (Marshal.SizeOf<EyeCopyRequest>() != EyeCopyRequestSize ||
                    Marshal.SizeOf<EyeCopyStatus>() != EyeCopyStatusSize ||
                    Marshal.SizeOf<EyeCopyTelemetry>() != EyeCopyTelemetrySize)
                {
                    throw new InvalidOperationException(
                        "The managed Unity D3D12 eye-copy ABI size is invalid.");
                }

                IntPtr renderEvent = _getRenderEventFunc();
                if (renderEvent == IntPtr.Zero)
                {
                    throw new InvalidOperationException(
                        "The native Unity D3D12 probe returned a null render-event callback.");
                }

                stage = "submit-managed-command-buffer";
                issuePluginEvent(renderEvent, ProbeEventId);
                _scheduledMilliseconds = Environment.TickCount64;
                return;
            }

            if (_terminal)
            {
                IssuePendingEyeCopyRenderEvent(issuePluginEvent);
                return;
            }

            stage = "read-native-snapshot";
            if (_getSnapshot is null ||
                !_getSnapshot(out UnityD3D12ProbeSnapshot snapshot))
            {
                throw new InvalidOperationException(
                    "The native Unity D3D12 probe snapshot was unavailable.");
            }

            bool renderEventObserved = (snapshot.Flags & RenderEventFlag) != 0;
            if (!renderEventObserved &&
                Environment.TickCount64 - _scheduledMilliseconds < ResultTimeoutMilliseconds)
            {
                return;
            }

            RecordTerminal(logPath, presentationGeneration, snapshot);
        }
        catch (Exception exception)
        {
            _terminal = true;
            RuntimeProbe.Append(logPath, new ProbeEvent
            {
                TimestampUtc = DateTimeOffset.UtcNow,
                Event = "unity-d3d12-native-interface-probe-result",
                BootstrapVersion = RuntimeProbe.BootstrapVersion,
                ProcessId = Environment.ProcessId,
                Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                D3D12PresentationGeneration = presentationGeneration,
                ErrorType = exception.GetType().FullName,
                Error = exception.ToString(),
                Reason = $"probeId={ProbeId};outcome=ERROR;stage={stage};" +
                    "eyeTexture=false;cloneSetup=false;stereoPump=false;copy=false."
            });
        }
    }

    private void IssuePendingEyeCopyRenderEvent(Action<IntPtr, int> issuePluginEvent)
    {
        if (_eyeCopyNeedsRenderEvent?.Invoke() != true || _getRenderEventFunc is null)
        {
            return;
        }

        IntPtr renderEvent = _getRenderEventFunc();
        if (renderEvent != IntPtr.Zero)
        {
            issuePluginEvent(renderEvent, ProbeEventId);
        }
    }

    private void RecordTerminal(
        string logPath,
        long presentationGeneration,
        UnityD3D12ProbeSnapshot snapshot)
    {
        bool generationCurrent =
            D3D12DeviceCapture.IsPresentationGenerationCurrent(presentationGeneration);
        bool pluginLoadObserved = (snapshot.Flags & PluginLoadFlag) != 0;
        bool renderEventObserved = (snapshot.Flags & RenderEventFlag) != 0;
        bool interfaceFound = (snapshot.Flags & InterfaceFoundFlag) != 0;
        bool bindingAcquired = false;
        bool devicePointerValid = ComPointerGuard.IsPlausible(snapshot.UnityDevice);
        bool queuePointerValid = ComPointerGuard.IsPlausible(snapshot.UnityCommandQueue);
        bool deviceIdentityQueryAttempted = false;
        bool deviceIdentityQueryCompleted = false;
        bool queueIdentityQueryAttempted = false;
        bool queueIdentityQueryCompleted = false;
        bool deviceMatch = false;
        bool queueMatch = false;
        Exception? identityFailure = null;

        if (pluginLoadObserved &&
            renderEventObserved &&
            interfaceFound &&
            generationCurrent &&
            D3D12DeviceCapture.TryAcquirePresentationBinding(
                out D3D12PresentationBindingLease binding))
        {
            bindingAcquired = true;
            using (binding)
            {
                generationCurrent = binding.Generation == presentationGeneration &&
                    D3D12DeviceCapture.IsPresentationGenerationCurrent(
                        presentationGeneration);
                if (generationCurrent)
                {
                    try
                    {
                        deviceIdentityQueryAttempted = devicePointerValid;
                        deviceMatch = deviceIdentityQueryAttempted &&
                            D3D12Interop.HaveSameComIdentity(
                                snapshot.UnityDevice,
                                binding.Device);
                        deviceIdentityQueryCompleted = deviceIdentityQueryAttempted;

                        if (deviceMatch && snapshot.HighestInterfaceVersion >= 4)
                        {
                            queueIdentityQueryAttempted = queuePointerValid;
                            queueMatch = queueIdentityQueryAttempted &&
                                D3D12Interop.HaveSameComIdentity(
                                    snapshot.UnityCommandQueue,
                                    binding.CommandQueue);
                            queueIdentityQueryCompleted = queueIdentityQueryAttempted;
                        }
                    }
                    catch (Exception exception)
                    {
                        identityFailure = exception;
                    }
                }
            }
        }

        string outcome = !renderEventObserved
            ? "RENDER_EVENT_NOT_OBSERVED"
            : !pluginLoadObserved
                ? "UNITY_PLUGIN_LOAD_NOT_OBSERVED"
                : !interfaceFound
                    ? "D3D12_INTERFACE_UNAVAILABLE"
                    : !generationCurrent
                        ? "GENERATION_RETIRED"
                        : !bindingAcquired
                            ? "PRESENTATION_BINDING_UNAVAILABLE"
                            : identityFailure is not null
                                ? "IDENTITY_QUERY_ERROR"
                            : !devicePointerValid
                                ? "DEVICE_POINTER_INVALID"
                            : !deviceMatch
                                ? "DEVICE_IDENTITY_MISMATCH"
                            : snapshot.HighestInterfaceVersion >= 4 && !queuePointerValid
                                ? "QUEUE_POINTER_INVALID"
                            : snapshot.HighestInterfaceVersion >= 4 && !queueMatch
                                ? "QUEUE_IDENTITY_MISMATCH"
                                : snapshot.HighestInterfaceVersion >= 4
                                    ? "DEVICE_QUEUE_MATCH"
                                    : "DEVICE_MATCH_QUEUE_API_UNAVAILABLE";

        if (outcome == "DEVICE_QUEUE_MATCH")
        {
            Volatile.Write(ref _bridgePresentationGeneration, presentationGeneration);
        }

        _terminal = true;
        RuntimeProbe.Append(logPath, new ProbeEvent
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            Event = "unity-d3d12-native-interface-probe-result",
            BootstrapVersion = RuntimeProbe.BootstrapVersion,
            ProcessId = Environment.ProcessId,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            D3D12PresentationGeneration = presentationGeneration,
            ErrorType = identityFailure?.GetType().FullName,
            Error = identityFailure?.ToString() ?? _earlyHookError,
            Reason = $"probeId={ProbeId};outcome={outcome};" +
                $"schema={snapshot.Schema};flags=0x{snapshot.Flags:x8};" +
                $"pluginLoadThread={snapshot.PluginLoadThreadId};" +
                $"renderEventThread={snapshot.RenderEventThreadId};" +
                $"interfaceVersion={snapshot.HighestInterfaceVersion};" +
                $"unityInterfaces=0x{snapshot.UnityInterfaces.ToInt64():x};" +
                $"unityD3D12=0x{snapshot.UnityD3D12Interface.ToInt64():x};" +
                $"unityDevice=0x{snapshot.UnityDevice.ToInt64():x};" +
                $"unityQueue=0x{snapshot.UnityCommandQueue.ToInt64():x};" +
                $"frameFence=0x{snapshot.UnityFrameFence.ToInt64():x};" +
                $"nextFrameFenceValue={snapshot.NextFrameFenceValue};" +
                $"bindingAcquired={bindingAcquired};" +
                $"devicePointerValid={devicePointerValid};" +
                $"queuePointerValid={queuePointerValid};" +
                $"deviceIdentityQueryAttempted={deviceIdentityQueryAttempted};" +
                $"deviceIdentityQueryCompleted={deviceIdentityQueryCompleted};" +
                $"queueIdentityQueryAttempted={queueIdentityQueryAttempted};" +
                $"queueIdentityQueryCompleted={queueIdentityQueryCompleted};" +
                $"deviceMatch={deviceMatch};queueMatch={queueMatch};" +
                $"generationCurrent={generationCurrent};" +
                "eyeTexture=false;cloneSetup=false;stereoPump=false;copy=false."
        });
    }

    private void ResetForGeneration(long generation)
    {
        Volatile.Write(ref _bridgePresentationGeneration, 0);
        _generation = generation;
        _scheduledMilliseconds = 0;
        _terminal = false;
    }

    internal static bool IsEyeCopyBridgeReady(long presentationGeneration) =>
        presentationGeneration > 0 &&
        Volatile.Read(ref _bridgePresentationGeneration) == presentationGeneration &&
        D3D12DeviceCapture.IsPresentationGenerationCurrent(presentationGeneration) &&
        _queueEyeCopy is not null && _pollEyeCopy is not null &&
        _cancelEyeCopy is not null && _eyeCopyNeedsRenderEvent is not null;

    internal static bool TryQueueEyeCopy(
        long presentationGeneration,
        ulong sequence,
        IntPtr sourceLeft,
        IntPtr sourceRight,
        IntPtr destinationLeft,
        IntPtr destinationRight,
        IntPtr uiCompositeSource = default,
        IntPtr uiWorldSource = default,
        IntPtr uiDestination = default)
    {
        if (!IsEyeCopyBridgeReady(presentationGeneration) || sequence == 0)
        {
            return false;
        }

        EyeCopyRequest request = new()
        {
            Schema = EyeCopySchema,
            PresentationGeneration = presentationGeneration,
            Sequence = sequence,
            SourceLeft = sourceLeft,
            SourceRight = sourceRight,
            DestinationLeft = destinationLeft,
            DestinationRight = destinationRight,
            UiCompositeSource = uiCompositeSource,
            UiWorldSource = uiWorldSource,
            UiDestination = uiDestination
        };
        return _queueEyeCopy?.Invoke(ref request) == true;
    }

    internal static bool TryPollEyeCopy(out D3D12EyeCopyStatus status)
    {
        status = default;
        if (_pollEyeCopy is null || !_pollEyeCopy(out EyeCopyStatus native))
        {
            return false;
        }

        status = new D3D12EyeCopyStatus(
            native.Schema,
            native.State,
            native.Hresult,
            native.PresentationGeneration,
            native.Sequence,
            native.FrameFenceValue,
            native.CompletedFenceValue);
        return native.Schema == EyeCopySchema;
    }

    internal static bool TryGetEyeCopyTelemetry(out D3D12EyeCopyTelemetry telemetry)
    {
        telemetry = default;
        if (_getEyeCopyTelemetry is null ||
            !_getEyeCopyTelemetry(out EyeCopyTelemetry native) ||
            native.Schema != 1)
        {
            return false;
        }

        telemetry = new D3D12EyeCopyTelemetry(
            native.Flags,
            native.State,
            native.LastStage,
            native.Hresult,
            native.FailureStage,
            native.PresentationGeneration,
            native.Sequence,
            native.TargetFenceValue,
            native.FirstCompletedFenceValue,
            native.LatestCompletedFenceValue);
        return true;
    }

    internal static bool TryCancelEyeCopy(long presentationGeneration, ulong sequence) =>
        _cancelEyeCopy?.Invoke(presentationGeneration, sequence) == true;

    private static void OnCriUnityPluginLoad(IntPtr unityInterfaces)
    {
        try
        {
            _criUnityPluginLoadOriginal?.Invoke(unityInterfaces);
        }
        finally
        {
            try
            {
                _probeUnityPluginLoad?.Invoke(unityInterfaces);
            }
            catch (Exception exception)
            {
                _earlyHookError = $"{exception.GetType().FullName}: {exception.Message}";
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct UnityD3D12ProbeSnapshot
    {
        public readonly uint Schema;
        public readonly uint Flags;
        public readonly uint PluginLoadThreadId;
        public readonly uint RenderEventThreadId;
        public readonly uint HighestInterfaceVersion;
        public readonly uint Reserved;
        public readonly IntPtr UnityInterfaces;
        public readonly IntPtr UnityD3D12Interface;
        public readonly IntPtr UnityDevice;
        public readonly IntPtr UnityCommandQueue;
        public readonly IntPtr UnityFrameFence;
        public readonly ulong NextFrameFenceValue;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EyeCopyRequest
    {
        public uint Schema;
        public uint Reserved;
        public long PresentationGeneration;
        public ulong Sequence;
        public IntPtr SourceLeft;
        public IntPtr SourceRight;
        public IntPtr DestinationLeft;
        public IntPtr DestinationRight;
        public IntPtr UiCompositeSource;
        public IntPtr UiWorldSource;
        public IntPtr UiDestination;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct EyeCopyStatus
    {
        public readonly uint Schema;
        public readonly uint State;
        public readonly uint Hresult;
        public readonly uint Reserved;
        public readonly long PresentationGeneration;
        public readonly ulong Sequence;
        public readonly ulong FrameFenceValue;
        public readonly ulong CompletedFenceValue;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct EyeCopyTelemetry
    {
        public readonly uint Schema;
        public readonly uint Flags;
        public readonly uint State;
        public readonly uint LastStage;
        public readonly uint Hresult;
        public readonly uint FailureStage;
        public readonly uint Reserved0;
        public readonly uint Reserved1;
        public readonly long PresentationGeneration;
        public readonly ulong Sequence;
        public readonly ulong TargetFenceValue;
        public readonly ulong FirstCompletedFenceValue;
        public readonly ulong LatestCompletedFenceValue;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr GetRenderEventFuncDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool GetProbeSnapshotDelegate(out UnityD3D12ProbeSnapshot snapshot);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool QueueEyeCopyDelegate(ref EyeCopyRequest request);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool PollEyeCopyDelegate(out EyeCopyStatus status);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool GetEyeCopyTelemetryDelegate(out EyeCopyTelemetry telemetry);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool CancelEyeCopyDelegate(long presentationGeneration, ulong sequence);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool EyeCopyNeedsRenderEventDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void UnityPluginLoadDelegate(IntPtr unityInterfaces);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int DobbyHookDelegate(
        IntPtr target,
        IntPtr replacement,
        out IntPtr original);
}
