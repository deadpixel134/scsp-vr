using System.Runtime.InteropServices;
using System.Text;

namespace Doorstop;

[StructLayout(LayoutKind.Explicit, Size = 24)]
internal struct D3D11ShaderResourceViewDescription
{
    [FieldOffset(0)]
    public int Format;

    [FieldOffset(4)]
    public int ViewDimension;

    [FieldOffset(8)]
    public uint MostDetailedMip;

    [FieldOffset(12)]
    public uint MipLevels;
}

[StructLayout(LayoutKind.Sequential)]
internal struct D3D11Viewport
{
    public float TopLeftX;
    public float TopLeftY;
    public float Width;
    public float Height;
    public float MinDepth;
    public float MaxDepth;
}

internal sealed class D3D11VerticalBlitter : IDisposable
{
    private const int CreateShaderResourceViewVtableIndex = 7;
    private const int CreateVertexShaderVtableIndex = 12;
    private const int CreatePixelShaderVtableIndex = 15;
    private const int CreateDeferredContextVtableIndex = 27;
    private const int PsSetShaderResourcesVtableIndex = 8;
    private const int PsSetShaderVtableIndex = 9;
    private const int VsSetShaderVtableIndex = 11;
    private const int DrawVtableIndex = 13;
    private const int IaSetInputLayoutVtableIndex = 17;
    private const int IaSetPrimitiveTopologyVtableIndex = 24;
    private const int OmSetRenderTargetsVtableIndex = 33;
    private const int RsSetViewportsVtableIndex = 44;
    private const int ExecuteCommandListVtableIndex = 58;
    private const int FinishCommandListVtableIndex = 114;

    private const string ShaderSource = """
        Texture2D<float4> SourceTexture : register(t0);
        Texture2D<float4> WorldTexture : register(t1);

        float4 VSMain(uint vertexId : SV_VertexID) : SV_POSITION
        {
            float2 positions[3] = {
                float2(-1.0, -1.0),
                float2(-1.0,  3.0),
                float2( 3.0, -1.0)
            };
            return float4(positions[vertexId], 0.0, 1.0);
        }

        float4 PSMain(float4 position : SV_POSITION) : SV_TARGET
        {
            uint width;
            uint height;
            SourceTexture.GetDimensions(width, height);
            uint x = min((uint)position.x, width - 1);
            uint y = min((uint)position.y, height - 1);
            return SourceTexture.Load(int3(x, height - 1 - y, 0));
        }

        float3 SrgbToLinear(float3 color)
        {
            float3 low = color / 12.92;
            float3 high = pow((color + 0.055) / 1.055, 2.4);
            return lerp(high, low, step(color, 0.04045));
        }

        float3 LinearToSrgb(float3 color)
        {
            float3 low = color * 12.92;
            float3 high = (1.055 * pow(color, 1.0 / 2.4)) - 0.055;
            return lerp(high, low, step(color, 0.0031308));
        }

        float4 PSEyeMain(float4 position : SV_POSITION) : SV_TARGET
        {
            uint width;
            uint height;
            SourceTexture.GetDimensions(width, height);
            uint x = min((uint)position.x, width - 1);
            uint y = min((uint)position.y, height - 1);
            float4 color = SourceTexture.Load(int3(x, height - 1 - y, 0));
            float3 linearColor = SrgbToLinear(saturate(color.rgb));
            linearColor *= 1.148698355;
            color.rgb = LinearToSrgb(saturate(linearColor));
            return color;
        }

        float4 PSUiMain(float4 position : SV_POSITION) : SV_TARGET
        {
            uint width;
            uint height;
            SourceTexture.GetDimensions(width, height);
            uint x = min((uint)position.x, width - 1);
            uint y = min((uint)position.y, height - 1);
            float4 color = SourceTexture.Load(int3(x, height - 1 - y, 0));
            float peak = max(color.r, max(color.g, color.b));
            color.a = peak <= (3.0 / 255.0) ? 0.0 : color.a;
            return color;
        }

        float4 LoadWorldBilinear(float2 uv)
        {
            uint width;
            uint height;
            WorldTexture.GetDimensions(width, height);
            float2 pixel = (saturate(uv) * float2(width, height)) - 0.5;
            int2 lower = int2(floor(pixel));
            float2 blend = frac(pixel);
            int2 maximum = int2((int)width - 1, (int)height - 1);
            int2 p00 = clamp(lower, int2(0, 0), maximum);
            int2 p10 = clamp(lower + int2(1, 0), int2(0, 0), maximum);
            int2 p01 = clamp(lower + int2(0, 1), int2(0, 0), maximum);
            int2 p11 = clamp(lower + int2(1, 1), int2(0, 0), maximum);
            float4 top = lerp(
                WorldTexture.Load(int3(p00, 0)),
                WorldTexture.Load(int3(p10, 0)),
                blend.x);
            float4 bottom = lerp(
                WorldTexture.Load(int3(p01, 0)),
                WorldTexture.Load(int3(p11, 0)),
                blend.x);
            return lerp(top, bottom, blend.y);
        }

        float4 PSUiDifferenceMain(float4 position : SV_POSITION) : SV_TARGET
        {
            uint width;
            uint height;
            SourceTexture.GetDimensions(width, height);
            uint x = min((uint)position.x, width - 1);
            uint y = min((uint)position.y, height - 1);
            float4 composite = SourceTexture.Load(int3(x, y, 0));
            float2 uv = (float2(x, y) + 0.5) / float2(width, height);
            float4 worldDirect = LoadWorldBilinear(uv);
            float4 worldFlipped = LoadWorldBilinear(float2(uv.x, 1.0 - uv.y));
            float directDifference = max(
                abs(composite.r - worldDirect.r),
                max(
                    abs(composite.g - worldDirect.g),
                    abs(composite.b - worldDirect.b)));
            float flippedDifference = max(
                abs(composite.r - worldFlipped.r),
                max(
                    abs(composite.g - worldFlipped.g),
                    abs(composite.b - worldFlipped.b)));
            float difference = min(directDifference, flippedDifference);
            float alpha = smoothstep(4.0 / 255.0, 24.0 / 255.0, difference);
            return float4(composite.rgb, alpha);
        }
        """;

    private readonly IntPtr _device;
    private readonly IntPtr _compilerLibrary;
    private readonly IntPtr _deferredContext;
    private readonly IntPtr _vertexShader;
    private readonly IntPtr _pixelShader;
    private readonly IntPtr _eyePixelShader;
    private readonly IntPtr _uiPixelShader;
    private readonly IntPtr _uiDifferencePixelShader;
    private readonly Dictionary<IntPtr, IntPtr> _sourceViews = new();
    private readonly Dictionary<IntPtr, IntPtr> _destinationViews = new();
    private bool _disposed;

    public D3D11VerticalBlitter(IntPtr device)
    {
        _device = device != IntPtr.Zero
            ? device
            : throw new ArgumentNullException(nameof(device));
        _compilerLibrary = NativeLibrary.Load("d3dcompiler_47.dll");
        try
        {
            _vertexShader = CreateShader("VSMain", "vs_5_0", vertexShader: true);
            _pixelShader = CreateShader("PSMain", "ps_5_0", vertexShader: false);
            _eyePixelShader = CreateShader("PSEyeMain", "ps_5_0", vertexShader: false);
            _uiPixelShader = CreateShader("PSUiMain", "ps_5_0", vertexShader: false);
            _uiDifferencePixelShader = CreateShader(
                "PSUiDifferenceMain",
                "ps_5_0",
                vertexShader: false);
            CreateDeferredContextDelegate createDeferred = GetMethod<CreateDeferredContextDelegate>(
                _device,
                CreateDeferredContextVtableIndex);
            int result = createDeferred(_device, 0, out _deferredContext);
            if (result < 0 || _deferredContext == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    $"ID3D11Device::CreateDeferredContext failed: HRESULT=0x{result:x8}.");
            }
        }
        catch
        {
            if (_uiDifferencePixelShader != IntPtr.Zero)
            {
                D3D11Interop.Release(_uiDifferencePixelShader);
            }

            if (_uiPixelShader != IntPtr.Zero)
            {
                D3D11Interop.Release(_uiPixelShader);
            }

            if (_eyePixelShader != IntPtr.Zero)
            {
                D3D11Interop.Release(_eyePixelShader);
            }

            if (_pixelShader != IntPtr.Zero)
            {
                D3D11Interop.Release(_pixelShader);
            }

            if (_vertexShader != IntPtr.Zero)
            {
                D3D11Interop.Release(_vertexShader);
            }

            NativeLibrary.Free(_compilerLibrary);
            throw;
        }
    }

    public void BlitFlipped(
        IntPtr immediateContext,
        IntPtr destination,
        IntPtr source,
        uint width,
        uint height,
        int resourceFormat,
        bool transparentBlack = false,
        bool brightenVrEye = false)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(D3D11VerticalBlitter));
        }
        IntPtr sourceView = GetOrCreateSourceView(source, resourceFormat);
        IntPtr destinationView = GetOrCreateDestinationView(destination, resourceFormat);

        IaSetInputLayoutDelegate setInputLayout = GetMethod<IaSetInputLayoutDelegate>(
            _deferredContext,
            IaSetInputLayoutVtableIndex);
        IaSetPrimitiveTopologyDelegate setTopology = GetMethod<IaSetPrimitiveTopologyDelegate>(
            _deferredContext,
            IaSetPrimitiveTopologyVtableIndex);
        VsSetShaderDelegate setVertexShader = GetMethod<VsSetShaderDelegate>(
            _deferredContext,
            VsSetShaderVtableIndex);
        PsSetShaderDelegate setPixelShader = GetMethod<PsSetShaderDelegate>(
            _deferredContext,
            PsSetShaderVtableIndex);
        PsSetShaderResourcesDelegate setSource = GetMethod<PsSetShaderResourcesDelegate>(
            _deferredContext,
            PsSetShaderResourcesVtableIndex);
        OmSetRenderTargetsDelegate setTarget = GetMethod<OmSetRenderTargetsDelegate>(
            _deferredContext,
            OmSetRenderTargetsVtableIndex);
        RsSetViewportsDelegate setViewport = GetMethod<RsSetViewportsDelegate>(
            _deferredContext,
            RsSetViewportsVtableIndex);
        DrawDelegate draw = GetMethod<DrawDelegate>(_deferredContext, DrawVtableIndex);

        setInputLayout(_deferredContext, IntPtr.Zero);
        setTopology(_deferredContext, 4);
        setVertexShader(_deferredContext, _vertexShader, IntPtr.Zero, 0);
        IntPtr selectedPixelShader = transparentBlack
            ? _uiPixelShader
            : brightenVrEye
                ? _eyePixelShader
                : _pixelShader;
        setPixelShader(_deferredContext, selectedPixelShader, IntPtr.Zero, 0);

        IntPtr singlePointer = Marshal.AllocHGlobal(IntPtr.Size);
        try
        {
            Marshal.WriteIntPtr(singlePointer, sourceView);
            setSource(_deferredContext, 0, 1, singlePointer);
            Marshal.WriteIntPtr(singlePointer, destinationView);
            setTarget(_deferredContext, 1, singlePointer, IntPtr.Zero);

            D3D11Viewport viewport = new()
            {
                Width = width,
                Height = height,
                MinDepth = 0f,
                MaxDepth = 1f
            };
            setViewport(_deferredContext, 1, ref viewport);
            draw(_deferredContext, 3, 0);

            Marshal.WriteIntPtr(singlePointer, IntPtr.Zero);
            setSource(_deferredContext, 0, 1, singlePointer);
            setTarget(_deferredContext, 0, IntPtr.Zero, IntPtr.Zero);
        }
        finally
        {
            Marshal.FreeHGlobal(singlePointer);
        }

        FinishCommandListDelegate finish = GetMethod<FinishCommandListDelegate>(
            _deferredContext,
            FinishCommandListVtableIndex);
        int finishResult = finish(_deferredContext, 0, out IntPtr commandList);
        if (finishResult < 0 || commandList == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"ID3D11DeviceContext::FinishCommandList failed: HRESULT=0x{finishResult:x8}.");
        }

        try
        {
            ExecuteCommandListDelegate execute = GetMethod<ExecuteCommandListDelegate>(
                immediateContext,
                ExecuteCommandListVtableIndex);
            execute(immediateContext, commandList, 1);
        }
        finally
        {
            D3D11Interop.Release(commandList);
        }
    }

    public void BlitUiDifference(
        IntPtr immediateContext,
        IntPtr destination,
        IntPtr compositeSource,
        IntPtr worldSource,
        uint width,
        uint height,
        int compositeFormat,
        int worldFormat)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(D3D11VerticalBlitter));
        }

        IntPtr compositeView = GetOrCreateSourceView(
            compositeSource,
            compositeFormat);
        IntPtr worldView = GetOrCreateSourceView(worldSource, worldFormat);
        IntPtr destinationView = GetOrCreateDestinationView(
            destination,
            compositeFormat);
        IaSetInputLayoutDelegate setInputLayout = GetMethod<IaSetInputLayoutDelegate>(
            _deferredContext,
            IaSetInputLayoutVtableIndex);
        IaSetPrimitiveTopologyDelegate setTopology = GetMethod<IaSetPrimitiveTopologyDelegate>(
            _deferredContext,
            IaSetPrimitiveTopologyVtableIndex);
        VsSetShaderDelegate setVertexShader = GetMethod<VsSetShaderDelegate>(
            _deferredContext,
            VsSetShaderVtableIndex);
        PsSetShaderDelegate setPixelShader = GetMethod<PsSetShaderDelegate>(
            _deferredContext,
            PsSetShaderVtableIndex);
        PsSetShaderResourcesDelegate setSources = GetMethod<PsSetShaderResourcesDelegate>(
            _deferredContext,
            PsSetShaderResourcesVtableIndex);
        OmSetRenderTargetsDelegate setTarget = GetMethod<OmSetRenderTargetsDelegate>(
            _deferredContext,
            OmSetRenderTargetsVtableIndex);
        RsSetViewportsDelegate setViewport = GetMethod<RsSetViewportsDelegate>(
            _deferredContext,
            RsSetViewportsVtableIndex);
        DrawDelegate draw = GetMethod<DrawDelegate>(_deferredContext, DrawVtableIndex);

        setInputLayout(_deferredContext, IntPtr.Zero);
        setTopology(_deferredContext, 4);
        setVertexShader(_deferredContext, _vertexShader, IntPtr.Zero, 0);
        setPixelShader(
            _deferredContext,
            _uiDifferencePixelShader,
            IntPtr.Zero,
            0);

        IntPtr pointers = Marshal.AllocHGlobal(2 * IntPtr.Size);
        try
        {
            Marshal.WriteIntPtr(pointers, compositeView);
            Marshal.WriteIntPtr(pointers, IntPtr.Size, worldView);
            setSources(_deferredContext, 0, 2, pointers);
            Marshal.WriteIntPtr(pointers, destinationView);
            setTarget(_deferredContext, 1, pointers, IntPtr.Zero);

            D3D11Viewport viewport = new()
            {
                Width = width,
                Height = height,
                MinDepth = 0f,
                MaxDepth = 1f
            };
            setViewport(_deferredContext, 1, ref viewport);
            draw(_deferredContext, 3, 0);

            Marshal.WriteIntPtr(pointers, IntPtr.Zero);
            Marshal.WriteIntPtr(pointers, IntPtr.Size, IntPtr.Zero);
            setSources(_deferredContext, 0, 2, pointers);
            setTarget(_deferredContext, 0, IntPtr.Zero, IntPtr.Zero);
        }
        finally
        {
            Marshal.FreeHGlobal(pointers);
        }

        FinishCommandListDelegate finish = GetMethod<FinishCommandListDelegate>(
            _deferredContext,
            FinishCommandListVtableIndex);
        int finishResult = finish(_deferredContext, 0, out IntPtr commandList);
        if (finishResult < 0 || commandList == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"ID3D11DeviceContext::FinishCommandList failed: HRESULT=0x{finishResult:x8}.");
        }

        try
        {
            ExecuteCommandListDelegate execute = GetMethod<ExecuteCommandListDelegate>(
                immediateContext,
                ExecuteCommandListVtableIndex);
            execute(immediateContext, commandList, 1);
        }
        finally
        {
            D3D11Interop.Release(commandList);
        }
    }

    public void ResetViews()
    {
        foreach (IntPtr view in _sourceViews.Values)
        {
            D3D11Interop.Release(view);
        }

        foreach (IntPtr view in _destinationViews.Values)
        {
            D3D11Interop.Release(view);
        }

        _sourceViews.Clear();
        _destinationViews.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ResetViews();
        D3D11Interop.Release(_deferredContext);
        D3D11Interop.Release(_uiDifferencePixelShader);
        D3D11Interop.Release(_uiPixelShader);
        D3D11Interop.Release(_eyePixelShader);
        D3D11Interop.Release(_pixelShader);
        D3D11Interop.Release(_vertexShader);
        NativeLibrary.Free(_compilerLibrary);
    }

    private IntPtr CreateShader(string entryPoint, string target, bool vertexShader)
    {
        D3DCompileDelegate compile = Marshal.GetDelegateForFunctionPointer<D3DCompileDelegate>(
            NativeLibrary.GetExport(_compilerLibrary, "D3DCompile"));
        byte[] sourceBytes = Encoding.UTF8.GetBytes(ShaderSource);
        GCHandle pinned = GCHandle.Alloc(sourceBytes, GCHandleType.Pinned);
        IntPtr blob = IntPtr.Zero;
        IntPtr errors = IntPtr.Zero;
        try
        {
            int result = compile(
                pinned.AddrOfPinnedObject(),
                (UIntPtr)sourceBytes.Length,
                "SongPrismVR.VerticalFlip.hlsl",
                IntPtr.Zero,
                IntPtr.Zero,
                entryPoint,
                target,
                0,
                0,
                out blob,
                out errors);
            if (result < 0 || blob == IntPtr.Zero)
            {
                string detail = errors == IntPtr.Zero ? string.Empty : ReadBlobString(errors);
                throw new InvalidOperationException(
                    $"D3DCompile {entryPoint}/{target} failed: HRESULT=0x{result:x8}; {detail}");
            }

            GetBlobPointerDelegate getPointer = GetMethod<GetBlobPointerDelegate>(blob, 3);
            GetBlobSizeDelegate getSize = GetMethod<GetBlobSizeDelegate>(blob, 4);
            IntPtr bytecode = getPointer(blob);
            UIntPtr bytecodeLength = getSize(blob);
            if (vertexShader)
            {
                CreateVertexShaderDelegate create = GetMethod<CreateVertexShaderDelegate>(
                    _device,
                    CreateVertexShaderVtableIndex);
                CheckCreate(
                    create(_device, bytecode, bytecodeLength, IntPtr.Zero, out IntPtr shader),
                    shader,
                    "CreateVertexShader");
                return shader;
            }

            CreatePixelShaderDelegate createPixel = GetMethod<CreatePixelShaderDelegate>(
                _device,
                CreatePixelShaderVtableIndex);
            CheckCreate(
                createPixel(_device, bytecode, bytecodeLength, IntPtr.Zero, out IntPtr pixelShader),
                pixelShader,
                "CreatePixelShader");
            return pixelShader;
        }
        finally
        {
            pinned.Free();
            D3D11Interop.Release(errors);
            D3D11Interop.Release(blob);
        }
    }

    private IntPtr GetOrCreateSourceView(IntPtr texture, int resourceFormat)
    {
        if (_sourceViews.TryGetValue(texture, out IntPtr existing))
        {
            return existing;
        }

        CreateShaderResourceViewDelegate create = GetMethod<CreateShaderResourceViewDelegate>(
            _device,
            CreateShaderResourceViewVtableIndex);
        D3D11ShaderResourceViewDescription description = new()
        {
            Format = RawViewFormat(resourceFormat),
            ViewDimension = 4,
            MostDetailedMip = 0,
            MipLevels = 1
        };
        int result = create(_device, texture, ref description, out IntPtr view);
        CheckCreate(result, view, "CreateShaderResourceView");
        _sourceViews.Add(texture, view);
        return view;
    }

    private IntPtr GetOrCreateDestinationView(IntPtr texture, int resourceFormat)
    {
        if (_destinationViews.TryGetValue(texture, out IntPtr existing))
        {
            return existing;
        }

        IntPtr view = D3D11Interop.CreateRenderTargetView(
            _device,
            texture,
            RawViewFormat(resourceFormat));
        _destinationViews.Add(texture, view);
        return view;
    }

    private static int RawViewFormat(int resourceFormat) => resourceFormat switch
    {
        27 or 28 or 29 => 28,
        87 or 90 or 91 => 87,
        _ => throw new NotSupportedException($"Unsupported flip-blit format: {resourceFormat}.")
    };

    private static string ReadBlobString(IntPtr blob)
    {
        GetBlobPointerDelegate getPointer = GetMethod<GetBlobPointerDelegate>(blob, 3);
        GetBlobSizeDelegate getSize = GetMethod<GetBlobSizeDelegate>(blob, 4);
        int length = checked((int)getSize(blob).ToUInt64());
        return length == 0
            ? string.Empty
            : Marshal.PtrToStringAnsi(getPointer(blob), length)?.TrimEnd('\0') ?? string.Empty;
    }

    private static void CheckCreate(int result, IntPtr value, string operation)
    {
        if (result < 0 || value == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"ID3D11Device::{operation} failed: HRESULT=0x{result:x8}.");
        }
    }

    private static TDelegate GetMethod<TDelegate>(IntPtr instance, int vtableIndex)
        where TDelegate : Delegate
    {
        IntPtr vtable = Marshal.ReadIntPtr(instance);
        IntPtr method = Marshal.ReadIntPtr(vtable, checked(vtableIndex * IntPtr.Size));
        return Marshal.GetDelegateForFunctionPointer<TDelegate>(method);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int D3DCompileDelegate(
        IntPtr sourceData,
        UIntPtr sourceDataSize,
        [MarshalAs(UnmanagedType.LPStr)] string sourceName,
        IntPtr defines,
        IntPtr include,
        [MarshalAs(UnmanagedType.LPStr)] string entryPoint,
        [MarshalAs(UnmanagedType.LPStr)] string target,
        uint flags1,
        uint flags2,
        out IntPtr code,
        out IntPtr errorMessages);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int CreateDeferredContextDelegate(IntPtr device, uint flags, out IntPtr context);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int CreateVertexShaderDelegate(
        IntPtr device,
        IntPtr bytecode,
        UIntPtr bytecodeLength,
        IntPtr classLinkage,
        out IntPtr shader);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int CreatePixelShaderDelegate(
        IntPtr device,
        IntPtr bytecode,
        UIntPtr bytecodeLength,
        IntPtr classLinkage,
        out IntPtr shader);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int CreateShaderResourceViewDelegate(
        IntPtr device,
        IntPtr resource,
        ref D3D11ShaderResourceViewDescription description,
        out IntPtr view);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void IaSetInputLayoutDelegate(IntPtr context, IntPtr inputLayout);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void IaSetPrimitiveTopologyDelegate(IntPtr context, int topology);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void VsSetShaderDelegate(
        IntPtr context,
        IntPtr shader,
        IntPtr classInstances,
        uint classInstanceCount);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void PsSetShaderDelegate(
        IntPtr context,
        IntPtr shader,
        IntPtr classInstances,
        uint classInstanceCount);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void PsSetShaderResourcesDelegate(
        IntPtr context,
        uint startSlot,
        uint viewCount,
        IntPtr views);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void OmSetRenderTargetsDelegate(
        IntPtr context,
        uint renderTargetViewCount,
        IntPtr renderTargetViews,
        IntPtr depthStencilView);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void RsSetViewportsDelegate(
        IntPtr context,
        uint viewportCount,
        ref D3D11Viewport viewports);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void DrawDelegate(IntPtr context, uint vertexCount, uint startVertexLocation);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int FinishCommandListDelegate(
        IntPtr context,
        int restoreDeferredContextState,
        out IntPtr commandList);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void ExecuteCommandListDelegate(
        IntPtr context,
        IntPtr commandList,
        int restoreContextState);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr GetBlobPointerDelegate(IntPtr blob);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate UIntPtr GetBlobSizeDelegate(IntPtr blob);
}
