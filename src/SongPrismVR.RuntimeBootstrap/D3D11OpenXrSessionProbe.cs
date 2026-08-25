using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Doorstop;

internal sealed class D3D11OpenXrSessionProbeResult
{
    public int SessionCreateResult { get; init; }
    public bool SessionCreated { get; init; }
    public string Stage { get; init; } = string.Empty;
    public string? Error { get; init; }
}

internal static class D3D11OpenXrSessionProbe
{
    private const int XrSuccess = 0;
    private const int XrTypeExtensionProperties = 2;
    private const int XrTypeInstanceCreateInfo = 3;
    private const int XrTypeSystemGetInfo = 4;
    private const int XrTypeInstanceProperties = 32;
    private const int XrTypeViewConfigurationView = 41;
    private const int XrTypeGraphicsBindingD3D11Khr = 1000027000;
    private const int XrTypeGraphicsRequirementsD3D11Khr = 1000027002;
    private const int XrTypeDebugUtilsMessengerCreateInfoExt = 1000048000;
    private const int XrFormFactorHeadMountedDisplay = 1;
    private const int XrPrimaryStereoViewConfiguration = 2;
    private const int MaxExtensionNameSize = 128;
    private const int MaxApplicationNameSize = 128;
    private const int MaxEngineNameSize = 128;
    private const int MaxRuntimeNameSize = 128;
    private const ulong XrDebugUtilsMessageSeverityErrorBitExt = 0x0000000000001000;
    private const ulong XrDebugUtilsMessageSeverityWarningBitExt = 0x0000000000000100;
    private const ulong XrDebugUtilsMessageSeverityInfoBitExt = 0x0000000000000010;
    private const ulong XrDebugUtilsMessageTypeGeneralBitExt = 0x0000000000000001;
    private const ulong XrDebugUtilsMessageTypeValidationBitExt = 0x0000000000000002;
    private const ulong XrDebugUtilsMessageTypePerformanceBitExt = 0x0000000000000004;
    private const string DebugUtilsExtensionName = "XR_EXT_debug_utils";
    private static readonly Guid IdxgiDevice = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
    private static readonly DebugUtilsMessengerCallbackDelegate DebugUtilsMessengerCallback =
        OnDebugUtilsMessage;

    public static D3D11OpenXrSessionProbeResult Run()
    {
        try
        {
            string loaderPath = FindLoader();
            Append("d3d11-openxr-session-probe-start", null, new()
            {
                ["loaderPath"] = loaderPath
            });
            IntPtr loader = NativeLibrary.Load(loaderPath);
            try
            {
                IReadOnlyList<string> extensions = EnumerateExtensions(loader);
                Append("d3d11-openxr-session-probe-extensions", null, new()
                {
                    ["extensionCount"] = extensions.Count,
                    ["hasD3D11Enable"] = extensions.Contains(
                        "XR_KHR_D3D11_enable",
                        StringComparer.Ordinal)
                });
                if (!extensions.Contains("XR_KHR_D3D11_enable", StringComparer.Ordinal))
                {
                    throw new InvalidOperationException(
                        "The active runtime does not advertise XR_KHR_D3D11_enable.");
                }

                return RunInstance(loader, extensions);
            }
            finally
            {
                NativeLibrary.Free(loader);
            }
        }
        catch (Exception exception)
        {
            Append("d3d11-openxr-session-probe-failure", exception);
            return new D3D11OpenXrSessionProbeResult
            {
                SessionCreateResult = int.MinValue,
                Stage = "exception",
                Error = exception.Message
            };
        }
    }

    private static D3D11OpenXrSessionProbeResult RunInstance(
        IntPtr loader,
        IReadOnlyList<string> extensions)
    {
        CreateInstanceDelegate createInstance = LoadExport<CreateInstanceDelegate>(
            loader,
            "xrCreateInstance");
        DestroyInstanceDelegate destroyInstance = LoadExport<DestroyInstanceDelegate>(
            loader,
            "xrDestroyInstance");
        GetInstancePropertiesDelegate getInstanceProperties =
            LoadExport<GetInstancePropertiesDelegate>(loader, "xrGetInstanceProperties");
        GetSystemDelegate getSystem = LoadExport<GetSystemDelegate>(loader, "xrGetSystem");
        GetInstanceProcAddrDelegate getInstanceProcAddr =
            LoadExport<GetInstanceProcAddrDelegate>(loader, "xrGetInstanceProcAddr");

        bool hasDebugUtils =
            Environment.GetEnvironmentVariable("SONGPRISM_VR_ENABLE_DEBUG_UTILS") == "1" &&
            extensions.Contains(
                DebugUtilsExtensionName,
                StringComparer.Ordinal);
        List<string> enabledExtensions = new() { "XR_KHR_D3D11_enable" };
        if (hasDebugUtils)
        {
            enabledExtensions.Add(DebugUtilsExtensionName);
        }

        IntPtr extensionNamesPointer = Marshal.AllocHGlobal(
            enabledExtensions.Count * IntPtr.Size);
        List<IntPtr> extensionNamePointers = new(enabledExtensions.Count);
        IntPtr instance = IntPtr.Zero;
        IntPtr debugMessenger = IntPtr.Zero;
        try
        {
            for (int index = 0; index < enabledExtensions.Count; index++)
            {
                IntPtr extensionName = Marshal.StringToCoTaskMemUTF8(enabledExtensions[index]);
                extensionNamePointers.Add(extensionName);
                Marshal.WriteIntPtr(extensionNamesPointer, index * IntPtr.Size, extensionName);
            }

            XrInstanceCreateInfo createInfo = new()
            {
                Type = XrTypeInstanceCreateInfo,
                ApplicationInfo = new XrApplicationInfo
                {
                    ApplicationName = FixedUtf8("SongPrismVROpenXrGraphicsProbe", MaxApplicationNameSize),
                    ApplicationVersion = 1,
                    EngineName = FixedUtf8("Python", MaxEngineNameSize),
                    EngineVersion = 1,
                    ApiVersion = MakeVersion(1, 0, 0)
                },
                EnabledExtensionCount = (uint)enabledExtensions.Count,
                EnabledExtensionNames = extensionNamesPointer
            };
            int createResult = createInstance(ref createInfo, out instance);
            Append("d3d11-openxr-session-probe-instance-result", null, new()
            {
                ["createResult"] = createResult,
                ["instanceCreated"] = instance != IntPtr.Zero
            });
            if (createResult != XrSuccess || instance == IntPtr.Zero)
            {
                return new D3D11OpenXrSessionProbeResult
                {
                    SessionCreateResult = createResult,
                    Stage = "create-instance"
                };
            }

            if (hasDebugUtils)
            {
                debugMessenger = CreateDebugUtilsMessenger(
                    loader,
                    instance,
                    getInstanceProcAddr);
            }

            XrSystemGetInfo systemInfo = new()
            {
                Type = XrTypeSystemGetInfo,
                FormFactor = XrFormFactorHeadMountedDisplay
            };
            int systemResult = getSystem(instance, ref systemInfo, out ulong systemId);
            Append("d3d11-openxr-session-probe-system-result", null, new()
            {
                ["systemResult"] = systemResult
            });
            if (systemResult != XrSuccess)
            {
                return new D3D11OpenXrSessionProbeResult
                {
                    SessionCreateResult = systemResult,
                    Stage = "get-system"
                };
            }

            EnsureViewConfiguration(loader, instance, systemId);

            XrGraphicsRequirementsD3D11 requirements = QueryD3D11Requirements(
                loader,
                instance,
                systemId);
            Append("d3d11-openxr-session-probe-requirements", null, new()
            {
                ["runtimeName"] = "VirtualDesktopXR",
                ["requiredAdapterLuid"] =
                    $"0x{requirements.AdapterLuid.HighPart:x8}:{requirements.AdapterLuid.LowPart:x8}",
                ["requiredMinFeatureLevel"] = requirements.MinFeatureLevel
            });

            IntPtr device = CreateD3D11Device(out IntPtr context, out int deviceResult, out int featureLevel);
            Append("d3d11-openxr-session-probe-device", null, new()
            {
                ["createDeviceResult"] = deviceResult,
                ["featureLevel"] = featureLevel,
                ["devicePointer"] = $"0x{device.ToInt64():x}",
                ["contextPointer"] = $"0x{context.ToInt64():x}",
                ["adapter"] = DescribeD3D11Adapter(device)
            });
            if (device == IntPtr.Zero)
            {
                return new D3D11OpenXrSessionProbeResult
                {
                    SessionCreateResult = deviceResult,
                    Stage = "d3d11-create-device"
                };
            }

            IntPtr session = IntPtr.Zero;
            try
            {
                CreateSessionDelegate createSession = ResolveCreateSession(
                    loader,
                    instance,
                    getInstanceProcAddr);
                XrGraphicsBindingD3D11 binding = new()
                {
                    Type = XrTypeGraphicsBindingD3D11Khr,
                    Device = device
                };
                IntPtr bindingPointer = Marshal.AllocHGlobal(Marshal.SizeOf<XrGraphicsBindingD3D11>());
                try
                {
                    Marshal.StructureToPtr(binding, bindingPointer, fDeleteOld: false);
                    IntPtr sessionCreateInfoPointer = Marshal.AllocHGlobal(
                        Marshal.SizeOf<XrSessionCreateInfo>());
                    int sessionCreateResult;
                    try
                    {
                        XrSessionCreateInfo sessionCreateInfo = new()
                        {
                            Type = 8,
                            Next = bindingPointer,
                            SystemId = systemId
                        };
                        Marshal.StructureToPtr(
                            sessionCreateInfo,
                            sessionCreateInfoPointer,
                            fDeleteOld: false);
                        sessionCreateResult = createSession(
                            instance,
                            sessionCreateInfoPointer,
                            out session);
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(sessionCreateInfoPointer);
                    }
                    Append("d3d11-openxr-session-probe-result", null, new()
                    {
                        ["sessionCreateResult"] = sessionCreateResult,
                        ["sessionCreated"] = session != IntPtr.Zero
                    });
                    return new D3D11OpenXrSessionProbeResult
                    {
                        SessionCreateResult = sessionCreateResult,
                        SessionCreated = session != IntPtr.Zero,
                        Stage = session != IntPtr.Zero ? "created" : "create-session"
                    };
                }
                finally
                {
                    Marshal.FreeHGlobal(bindingPointer);
                }
            }
            finally
            {
                if (session != IntPtr.Zero)
                {
                    DestroySessionDelegate destroySession = LoadExport<DestroySessionDelegate>(
                        loader,
                        "xrDestroySession");
                    _ = destroySession(session);
                }
                if (context != IntPtr.Zero)
                {
                    _ = Marshal.Release(context);
                }
                _ = Marshal.Release(device);
            }
        }
        finally
        {
            if (debugMessenger != IntPtr.Zero)
            {
                DestroyDebugUtilsMessenger(
                    loader,
                    instance,
                    debugMessenger);
            }
            if (instance != IntPtr.Zero)
            {
                _ = destroyInstance(instance);
            }
            Marshal.FreeHGlobal(extensionNamesPointer);
            foreach (IntPtr extensionName in extensionNamePointers)
            {
                Marshal.FreeCoTaskMem(extensionName);
            }
        }
    }

    private static void EnsureViewConfiguration(
        IntPtr loader,
        IntPtr instance,
        ulong systemId)
    {
        EnumerateViewConfigurationViewsDelegate enumerate =
            LoadExport<EnumerateViewConfigurationViewsDelegate>(
                loader,
                "xrEnumerateViewConfigurationViews");
        Check(
            enumerate(
                instance,
                systemId,
                XrPrimaryStereoViewConfiguration,
                0,
                out uint count,
                IntPtr.Zero),
            "count OpenXR stereo views");
        if (count == 0 || count > 16)
        {
            throw new InvalidOperationException($"Invalid OpenXR stereo view count: {count}.");
        }

        int elementSize = Marshal.SizeOf<XrViewConfigurationView>();
        IntPtr buffer = Marshal.AllocHGlobal(checked((int)count * elementSize));
        try
        {
            for (int index = 0; index < count; index++)
            {
                Marshal.StructureToPtr(
                    new XrViewConfigurationView { Type = XrTypeViewConfigurationView },
                    IntPtr.Add(buffer, index * elementSize),
                    fDeleteOld: false);
            }

            Check(
                enumerate(
                    instance,
                    systemId,
                    XrPrimaryStereoViewConfiguration,
                    count,
                    out uint written,
                    buffer),
                "enumerate OpenXR stereo views");
            if (written == 0)
            {
                throw new InvalidOperationException("OpenXR returned no stereo views.");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static XrGraphicsRequirementsD3D11 QueryD3D11Requirements(
        IntPtr loader,
        IntPtr instance,
        ulong systemId)
    {
        GetInstanceProcAddrDelegate getInstanceProcAddr = LoadExport<GetInstanceProcAddrDelegate>(
            loader,
            "xrGetInstanceProcAddr");
        IntPtr functionName = Marshal.StringToCoTaskMemUTF8("xrGetD3D11GraphicsRequirementsKHR");
        try
        {
            Check(
                getInstanceProcAddr(instance, functionName, out IntPtr function),
                "resolve xrGetD3D11GraphicsRequirementsKHR");
            if (function == IntPtr.Zero)
            {
                throw new MissingMethodException("xrGetD3D11GraphicsRequirementsKHR resolved to null.");
            }
            GetD3D11GraphicsRequirementsDelegate getRequirements =
                Marshal.GetDelegateForFunctionPointer<GetD3D11GraphicsRequirementsDelegate>(function);
            XrGraphicsRequirementsD3D11 requirements = new()
            {
                Type = XrTypeGraphicsRequirementsD3D11Khr
            };
            Check(getRequirements(instance, systemId, ref requirements), "query D3D11 requirements");
            return requirements;
        }
        finally
        {
            Marshal.FreeCoTaskMem(functionName);
        }
    }

    private static CreateSessionDelegate ResolveCreateSession(
        IntPtr loader,
        IntPtr instance,
        GetInstanceProcAddrDelegate getInstanceProcAddr)
    {
        IntPtr functionName = Marshal.StringToCoTaskMemUTF8("xrCreateSession");
        try
        {
            Check(getInstanceProcAddr(instance, functionName, out IntPtr function), "resolve xrCreateSession");
            if (function == IntPtr.Zero)
            {
                throw new MissingMethodException("xrCreateSession resolved to null.");
            }
            return Marshal.GetDelegateForFunctionPointer<CreateSessionDelegate>(function);
        }
        finally
        {
            Marshal.FreeCoTaskMem(functionName);
        }
    }

    private static IntPtr CreateDebugUtilsMessenger(
        IntPtr loader,
        IntPtr instance,
        GetInstanceProcAddrDelegate getInstanceProcAddr)
    {
        IntPtr functionName = Marshal.StringToCoTaskMemUTF8("xrCreateDebugUtilsMessengerEXT");
        try
        {
            int resolveResult = getInstanceProcAddr(instance, functionName, out IntPtr function);
            if (resolveResult != XrSuccess || function == IntPtr.Zero)
            {
                Append("d3d11-openxr-debug-messenger-unavailable", null, new()
                {
                    ["resolveResult"] = resolveResult,
                    ["functionResolved"] = function != IntPtr.Zero
                });
                return IntPtr.Zero;
            }

            CreateDebugUtilsMessengerDelegate create =
                Marshal.GetDelegateForFunctionPointer<CreateDebugUtilsMessengerDelegate>(function);
            XrDebugUtilsMessengerCreateInfo createInfo = new()
            {
                Type = XrTypeDebugUtilsMessengerCreateInfoExt,
                MessageSeverity =
                    XrDebugUtilsMessageSeverityErrorBitExt |
                    XrDebugUtilsMessageSeverityWarningBitExt |
                    XrDebugUtilsMessageSeverityInfoBitExt,
                MessageTypes =
                    XrDebugUtilsMessageTypeGeneralBitExt |
                    XrDebugUtilsMessageTypeValidationBitExt |
                    XrDebugUtilsMessageTypePerformanceBitExt,
                UserCallback = Marshal.GetFunctionPointerForDelegate(DebugUtilsMessengerCallback)
            };
            int createResult = create(instance, ref createInfo, out IntPtr messenger);
            Append("d3d11-openxr-debug-messenger-result", null, new()
            {
                ["createResult"] = createResult,
                ["messengerCreated"] = messenger != IntPtr.Zero
            });
            return createResult == XrSuccess ? messenger : IntPtr.Zero;
        }
        catch (Exception exception)
        {
            Append("d3d11-openxr-debug-messenger-failure", exception);
            return IntPtr.Zero;
        }
        finally
        {
            Marshal.FreeCoTaskMem(functionName);
        }
    }

    private static void DestroyDebugUtilsMessenger(
        IntPtr loader,
        IntPtr instance,
        IntPtr messenger)
    {
        if (instance == IntPtr.Zero || messenger == IntPtr.Zero)
        {
            return;
        }

        IntPtr functionName = Marshal.StringToCoTaskMemUTF8("xrDestroyDebugUtilsMessengerEXT");
        try
        {
            GetInstanceProcAddrDelegate getInstanceProcAddr =
                LoadExport<GetInstanceProcAddrDelegate>(loader, "xrGetInstanceProcAddr");
            if (getInstanceProcAddr(instance, functionName, out IntPtr function) != XrSuccess ||
                function == IntPtr.Zero)
            {
                return;
            }

            DestroyDebugUtilsMessengerDelegate destroy =
                Marshal.GetDelegateForFunctionPointer<DestroyDebugUtilsMessengerDelegate>(function);
            _ = destroy(messenger);
        }
        catch
        {
            // Best-effort cleanup; do not mask the original probe result.
        }
        finally
        {
            Marshal.FreeCoTaskMem(functionName);
        }
    }

    private static int OnDebugUtilsMessage(
        ulong severity,
        ulong types,
        IntPtr callbackData,
        IntPtr userData)
    {
        try
        {
            if (callbackData == IntPtr.Zero)
            {
                return 0;
            }

            XrDebugUtilsMessengerCallbackData data =
                Marshal.PtrToStructure<XrDebugUtilsMessengerCallbackData>(callbackData);
            Append("openxr-debug-utils-message", null, new()
            {
                ["severity"] = $"0x{severity:x16}",
                ["types"] = $"0x{types:x16}",
                ["messageId"] = ReadUtf8(data.MessageId),
                ["functionName"] = ReadUtf8(data.FunctionName),
                ["message"] = ReadUtf8(data.Message)
            });
        }
        catch
        {
            // Debug callbacks must never throw back into the OpenXR runtime.
        }

        return 0;
    }

    private static string ReadUtf8(IntPtr pointer) =>
        pointer == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUTF8(pointer) ?? string.Empty;

    private static IntPtr CreateD3D11Device(
        out IntPtr immediateContext,
        out int result,
        out int featureLevel)
    {
        immediateContext = IntPtr.Zero;
        featureLevel = 0;
        IntPtr d3d11 = NativeLibrary.Load("d3d11.dll");
        try
        {
            D3D11CreateDeviceDelegate create = Marshal.GetDelegateForFunctionPointer<D3D11CreateDeviceDelegate>(
                NativeLibrary.GetExport(d3d11, "D3D11CreateDevice"));
            int[] featureLevels = { 0xB000 };
            IntPtr featureLevelsPointer = Marshal.AllocHGlobal(featureLevels.Length * sizeof(int));
            try
            {
                for (int index = 0; index < featureLevels.Length; index++)
                {
                    Marshal.WriteInt32(featureLevelsPointer, index * sizeof(int), featureLevels[index]);
                }
                result = create(
                    IntPtr.Zero,
                    1,
                    IntPtr.Zero,
                    0x20,
                    featureLevelsPointer,
                    (uint)featureLevels.Length,
                    7,
                    out IntPtr device,
                    out featureLevel,
                    out immediateContext);
                return result >= 0 ? device : IntPtr.Zero;
            }
            finally
            {
                Marshal.FreeHGlobal(featureLevelsPointer);
            }
        }
        finally
        {
            // Keep d3d11.dll loaded until xrCreateSession has consumed the device.
        }
    }

    private static string DescribeD3D11Adapter(IntPtr device)
    {
        if (device == IntPtr.Zero)
        {
            return "null";
        }

        try
        {
            Guid idxgiDevice = IdxgiDevice;
            int queryResult = Marshal.QueryInterface(device, ref idxgiDevice, out IntPtr dxgiDevice);
            if (queryResult < 0 || dxgiDevice == IntPtr.Zero)
            {
                return $"query-interface-failed=0x{queryResult:x8}";
            }

            try
            {
                IntPtr dxgiDeviceVtable = Marshal.ReadIntPtr(dxgiDevice);
                GetAdapterDelegate getAdapter = Marshal.GetDelegateForFunctionPointer<GetAdapterDelegate>(
                    Marshal.ReadIntPtr(dxgiDeviceVtable, 7 * IntPtr.Size));
                int adapterResult = getAdapter(dxgiDevice, out IntPtr adapter);
                if (adapterResult < 0 || adapter == IntPtr.Zero)
                {
                    return $"get-adapter-failed=0x{adapterResult:x8}";
                }

                try
                {
                    IntPtr adapterVtable = Marshal.ReadIntPtr(adapter);
                    GetAdapterDescriptionDelegate getDescription =
                        Marshal.GetDelegateForFunctionPointer<GetAdapterDescriptionDelegate>(
                            Marshal.ReadIntPtr(adapterVtable, 8 * IntPtr.Size));
                    int descriptionSize = Marshal.SizeOf<DxgiAdapterDescription>();
                    IntPtr descriptionPointer = Marshal.AllocHGlobal(descriptionSize);
                    try
                    {
                        int descriptionResult = getDescription(adapter, descriptionPointer);
                        if (descriptionResult < 0)
                        {
                            return $"get-description-failed=0x{descriptionResult:x8}";
                        }

                        DxgiAdapterDescription description =
                            Marshal.PtrToStructure<DxgiAdapterDescription>(descriptionPointer);
                        return $"name={description.Description};vendor=0x{description.VendorId:x4};" +
                            $"device=0x{description.DeviceId:x4};" +
                            $"luid=0x{description.AdapterLuid.HighPart:x8}:{description.AdapterLuid.LowPart:x8}";
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(descriptionPointer);
                    }
                }
                finally
                {
                    _ = Marshal.Release(adapter);
                }
            }
            finally
            {
                _ = Marshal.Release(dxgiDevice);
            }
        }
        catch (Exception exception)
        {
            return $"adapter-query-error:{exception.GetType().Name}:{exception.Message}";
        }
    }

    private static IReadOnlyList<string> EnumerateExtensions(IntPtr loader)
    {
        EnumerateInstanceExtensionPropertiesDelegate enumerate =
            LoadExport<EnumerateInstanceExtensionPropertiesDelegate>(
                loader,
                "xrEnumerateInstanceExtensionProperties");
        Check(enumerate(IntPtr.Zero, 0, out uint count, IntPtr.Zero), "count instance extensions");
        int elementSize = Marshal.SizeOf<XrExtensionProperties>();
        IntPtr buffer = Marshal.AllocHGlobal(checked((int)count * elementSize));
        try
        {
            for (int index = 0; index < count; index++)
            {
                Marshal.StructureToPtr(
                    new XrExtensionProperties
                    {
                        Type = XrTypeExtensionProperties,
                        ExtensionName = new byte[MaxExtensionNameSize]
                    },
                    IntPtr.Add(buffer, index * elementSize),
                    fDeleteOld: false);
            }
            Check(enumerate(IntPtr.Zero, count, out uint written, buffer), "enumerate instance extensions");
            List<string> names = new();
            for (int index = 0; index < written; index++)
            {
                XrExtensionProperties extension = Marshal.PtrToStructure<XrExtensionProperties>(
                    IntPtr.Add(buffer, index * elementSize));
                names.Add(DecodeFixedUtf8(extension.ExtensionName));
            }
            return names;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string FindLoader()
    {
        string? gameRootOverride = Environment.GetEnvironmentVariable("SONGPRISM_VR_GAME_ROOT");
        if (!string.IsNullOrWhiteSpace(gameRootOverride) &&
            Directory.Exists(gameRootOverride))
        {
            string overrideLoaderPath = Path.Combine(
                gameRootOverride,
                "vrmod",
                "runtime",
                "openxr_loader.dll");
            if (File.Exists(overrideLoaderPath))
            {
                return overrideLoaderPath;
            }
        }

        string? executable = Process.GetCurrentProcess().MainModule?.FileName;
        string gameRoot = executable is null
            ? Directory.GetCurrentDirectory()
            : Path.GetDirectoryName(executable) ?? Directory.GetCurrentDirectory();
        string loaderPath = Path.Combine(gameRoot, "vrmod", "runtime", "openxr_loader.dll");
        if (!File.Exists(loaderPath))
        {
            throw new FileNotFoundException("OpenXR loader is missing.", loaderPath);
        }
        return loaderPath;
    }

    private static T LoadExport<T>(IntPtr loader, string name) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(loader, name));

    private static void Check(int result, string operation)
    {
        if (result != XrSuccess)
        {
            throw new InvalidOperationException($"{operation} failed: {result}.");
        }
    }

    private static byte[] FixedUtf8(string value, int length)
    {
        byte[] buffer = new byte[length];
        Encoding.UTF8.GetBytes(value, 0, value.Length, buffer, 0);
        return buffer;
    }

    private static string DecodeFixedUtf8(byte[] value)
    {
        int length = Array.IndexOf(value, (byte)0);
        if (length < 0)
        {
            length = value.Length;
        }
        return Encoding.UTF8.GetString(value, 0, length);
    }

    private static ulong MakeVersion(ushort major, ushort minor, uint patch) =>
        ((ulong)major << 48) | ((ulong)minor << 32) | patch;

    private static void Append(string eventName, Exception? exception, Dictionary<string, object>? fields = null)
    {
        string reason = fields is null
            ? string.Empty
            : string.Join(";", fields.Select(pair => $"{pair.Key}={pair.Value}"));
        RuntimeProbe.Append(
            RuntimeProbe.GetLogPath(),
            new ProbeEvent
            {
                TimestampUtc = DateTimeOffset.UtcNow,
                Event = eventName,
                BootstrapVersion = RuntimeProbe.BootstrapVersion,
                ProcessId = Environment.ProcessId,
                Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                ErrorType = exception?.GetType().FullName,
                Error = exception?.ToString(),
                Reason = reason
            });
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrExtensionProperties
    {
        public int Type;
        public IntPtr Next;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MaxExtensionNameSize)]
        public byte[] ExtensionName;
        public uint ExtensionVersion;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrApplicationInfo
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MaxApplicationNameSize)]
        public byte[] ApplicationName;
        public uint ApplicationVersion;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MaxEngineNameSize)]
        public byte[] EngineName;
        public uint EngineVersion;
        public ulong ApiVersion;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrInstanceCreateInfo
    {
        public int Type;
        public IntPtr Next;
        public ulong CreateFlags;
        public XrApplicationInfo ApplicationInfo;
        public uint EnabledApiLayerCount;
        public IntPtr EnabledApiLayerNames;
        public uint EnabledExtensionCount;
        public IntPtr EnabledExtensionNames;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrInstanceProperties
    {
        public int Type;
        public IntPtr Next;
        public ulong RuntimeVersion;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MaxRuntimeNameSize)]
        public byte[] RuntimeName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrSystemGetInfo
    {
        public int Type;
        public IntPtr Next;
        public int FormFactor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrGraphicsRequirementsD3D11
    {
        public int Type;
        public IntPtr Next;
        public Luid AdapterLuid;
        public int MinFeatureLevel;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrViewConfigurationView
    {
        public int Type;
        public IntPtr Next;
        public uint RecommendedImageRectWidth;
        public uint MaxImageRectWidth;
        public uint RecommendedImageRectHeight;
        public uint MaxImageRectHeight;
        public uint RecommendedSwapchainSampleCount;
        public uint MaxSwapchainSampleCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrDebugUtilsMessengerCallbackData
    {
        public int Type;
        public IntPtr Next;
        public IntPtr MessageId;
        public IntPtr FunctionName;
        public IntPtr Message;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrDebugUtilsMessengerCreateInfo
    {
        public int Type;
        public IntPtr Next;
        public ulong MessageSeverity;
        public ulong MessageTypes;
        public IntPtr UserCallback;
        public IntPtr UserData;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrGraphicsBindingD3D11
    {
        public int Type;
        public IntPtr Next;
        public IntPtr Device;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrSessionCreateInfo
    {
        public int Type;
        public IntPtr Next;
        public ulong CreateFlags;
        public ulong SystemId;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int EnumerateInstanceExtensionPropertiesDelegate(
        IntPtr layerName,
        uint capacityInput,
        out uint countOutput,
        IntPtr properties);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int CreateInstanceDelegate(ref XrInstanceCreateInfo createInfo, out IntPtr instance);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int DestroyInstanceDelegate(IntPtr instance);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetInstancePropertiesDelegate(IntPtr instance, ref XrInstanceProperties properties);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetSystemDelegate(IntPtr instance, ref XrSystemGetInfo getInfo, out ulong systemId);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetInstanceProcAddrDelegate(IntPtr instance, IntPtr name, out IntPtr function);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetD3D11GraphicsRequirementsDelegate(
        IntPtr instance,
        ulong systemId,
        ref XrGraphicsRequirementsD3D11 requirements);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int EnumerateViewConfigurationViewsDelegate(
        IntPtr instance,
        ulong systemId,
        int viewConfigurationType,
        uint viewCapacityInput,
        out uint viewCountOutput,
        IntPtr views);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int CreateSessionDelegate(
        IntPtr instance,
        IntPtr createInfo,
        out IntPtr session);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int DestroySessionDelegate(IntPtr session);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int DebugUtilsMessengerCallbackDelegate(
        ulong severity,
        ulong types,
        IntPtr callbackData,
        IntPtr userData);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int CreateDebugUtilsMessengerDelegate(
        IntPtr instance,
        ref XrDebugUtilsMessengerCreateInfo createInfo,
        out IntPtr messenger);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int DestroyDebugUtilsMessengerDelegate(IntPtr messenger);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetAdapterDelegate(IntPtr dxgiDevice, out IntPtr adapter);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetAdapterDescriptionDelegate(IntPtr adapter, IntPtr description);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int D3D11CreateDeviceDelegate(
        IntPtr adapter,
        int driverType,
        IntPtr software,
        uint flags,
        IntPtr featureLevels,
        uint featureLevelCount,
        uint sdkVersion,
        out IntPtr device,
        out int featureLevel,
        out IntPtr immediateContext);
}
