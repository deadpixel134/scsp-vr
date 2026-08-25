using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Doorstop;

internal sealed class D3D11TextureLease : IDisposable
{
    private IntPtr _texture;

    internal D3D11TextureLease(IntPtr texture, string sourceName)
    {
        _texture = texture;
        SourceName = sourceName;
    }

    public IntPtr Texture => Volatile.Read(ref _texture);

    public string SourceName { get; }

    public void Dispose()
    {
        IntPtr texture = Interlocked.Exchange(ref _texture, IntPtr.Zero);
        if (texture != IntPtr.Zero)
        {
            _ = Marshal.Release(texture);
        }
    }
}

internal sealed class D3D11StereoTextureLease : IDisposable
{
    private IntPtr _leftTexture;
    private IntPtr _rightTexture;
    private IntPtr _uiWorldTexture;

    internal D3D11StereoTextureLease(
        IntPtr leftTexture,
        IntPtr rightTexture,
        long publishedTimestamp,
        long sequence,
        long presentationGeneration,
        bool requiresDynamicUi,
        IntPtr uiWorldTexture,
        OpenXrStereoStateSnapshot? renderState,
        bool usesWorldSpace)
    {
        _leftTexture = leftTexture;
        _rightTexture = rightTexture;
        PublishedTimestamp = publishedTimestamp;
        Sequence = sequence;
        PresentationGeneration = presentationGeneration;
        RequiresDynamicUi = requiresDynamicUi;
        _uiWorldTexture = uiWorldTexture;
        RenderState = renderState;
        UsesWorldSpace = usesWorldSpace;
    }

    public IntPtr LeftTexture => Volatile.Read(ref _leftTexture);

    public IntPtr RightTexture => Volatile.Read(ref _rightTexture);

    public long PublishedTimestamp { get; }

    public long Sequence { get; }

    public long PresentationGeneration { get; }

    public bool RequiresDynamicUi { get; }

    public IntPtr UiWorldTexture => Volatile.Read(ref _uiWorldTexture);

    public OpenXrStereoStateSnapshot? RenderState { get; }

    public bool UsesWorldSpace { get; }

    public void Dispose()
    {
        IntPtr left = Interlocked.Exchange(ref _leftTexture, IntPtr.Zero);
        IntPtr right = Interlocked.Exchange(ref _rightTexture, IntPtr.Zero);
        IntPtr uiWorld = Interlocked.Exchange(ref _uiWorldTexture, IntPtr.Zero);
        UnityRenderSourceRegistry.ReleaseStereoTextureLease(left, right, uiWorld);
    }
}

internal static class UnityRenderSourceRegistry
{
    private static readonly object Sync = new();
    private static readonly Guid Id3D12Resource =
        new("696442be-a72e-4059-bc79-5b5c98040fad");
    private static IntPtr _liveWorldTexture;
    private static IntPtr _liveUiTexture;
    private static string _uiSourceName = string.Empty;
    private static long _uiUpdatedMilliseconds;
    private static string _sourceName = string.Empty;
    private static long _updatedMilliseconds;
    private static IntPtr _stereoLeftTexture;
    private static IntPtr _stereoRightTexture;
    private static long _stereoUpdatedMilliseconds;
    private static long _stereoPublishedTimestamp;
    private static long _stereoSequence;
    private static long _stereoPresentationGeneration;
    private static IntPtr _lastPublishedStereoLeft;
    private static IntPtr _lastPublishedStereoRight;
    private static long _lastPublishedStereoGeneration;
    private static bool _stereoRequiresDynamicUi;
    private static IntPtr _stereoUiWorldTexture;
    private static OpenXrStereoStateSnapshot? _stereoRenderState;
    private static bool _stereoUsesWorldSpace;
    private static readonly Dictionary<IntPtr, int> StereoLeaseCounts = new();

    public static void UpdateLiveWorldTexture(IntPtr texture, string sourceName)
    {
        if (texture == IntPtr.Zero)
        {
            ClearLiveWorldTexture();
            return;
        }

        Guid interfaceId = Id3D12Resource;
        int queryResult = Marshal.QueryInterface(texture, ref interfaceId, out IntPtr d3d12Resource);
        if (queryResult < 0 || d3d12Resource == IntPtr.Zero)
        {
            ClearLiveWorldTexture();
            return;
        }

        lock (Sync)
        {
            if (_liveWorldTexture != d3d12Resource)
            {
                IntPtr previous = _liveWorldTexture;
                _liveWorldTexture = d3d12Resource;
                if (previous != IntPtr.Zero)
                {
                    _ = Marshal.Release(previous);
                }
            }
            else
            {
                _ = Marshal.Release(d3d12Resource);
            }

            _sourceName = sourceName;
            _updatedMilliseconds = Environment.TickCount64;
        }
    }

    public static void ClearLiveWorldTexture()
    {
        lock (Sync)
        {
            IntPtr previous = _liveWorldTexture;
            _liveWorldTexture = IntPtr.Zero;
            _sourceName = string.Empty;
            _updatedMilliseconds = 0;
            if (previous != IntPtr.Zero)
            {
                _ = Marshal.Release(previous);
            }
        }
    }

    public static bool TouchLiveWorldTexture(string sourceName)
    {
        lock (Sync)
        {
            if (_liveWorldTexture == IntPtr.Zero)
            {
                return false;
            }

            _sourceName = sourceName;
            _updatedMilliseconds = Environment.TickCount64;
            return true;
        }
    }

    public static D3D11TextureLease? AcquireLiveWorldTexture(int maximumAgeMilliseconds)
    {
        lock (Sync)
        {
            if (_liveWorldTexture == IntPtr.Zero ||
                Environment.TickCount64 - _updatedMilliseconds > maximumAgeMilliseconds)
            {
                return null;
            }

            _ = Marshal.AddRef(_liveWorldTexture);
            return new D3D11TextureLease(_liveWorldTexture, _sourceName);
        }
    }

    public static void UpdateLiveUiTexture(IntPtr texture, string sourceName)
    {
        if (texture == IntPtr.Zero)
        {
            return;
        }

        Guid interfaceId = Id3D12Resource;
        int queryResult = Marshal.QueryInterface(texture, ref interfaceId, out IntPtr d3d12Resource);
        if (queryResult < 0 || d3d12Resource == IntPtr.Zero)
        {
            return;
        }

        lock (Sync)
        {
            IntPtr previous = _liveUiTexture;
            _liveUiTexture = d3d12Resource;
            _uiSourceName = sourceName;
            _uiUpdatedMilliseconds = Environment.TickCount64;
            if (previous != IntPtr.Zero)
            {
                _ = Marshal.Release(previous);
            }
        }
    }

    public static D3D11TextureLease? AcquireLiveUiTexture(int maximumAgeMilliseconds)
    {
        lock (Sync)
        {
            if (_liveUiTexture == IntPtr.Zero ||
                Environment.TickCount64 - _uiUpdatedMilliseconds > maximumAgeMilliseconds)
            {
                return null;
            }

            _ = Marshal.AddRef(_liveUiTexture);
            return new D3D11TextureLease(_liveUiTexture, _uiSourceName);
        }
    }

    public static bool TouchLiveUiTexture(string sourceName)
    {
        lock (Sync)
        {
            if (_liveUiTexture == IntPtr.Zero)
            {
                return false;
            }

            _uiSourceName = sourceName;
            _uiUpdatedMilliseconds = Environment.TickCount64;
            return true;
        }
    }

    public static void ClearLiveUiTexture()
    {
        lock (Sync)
        {
            IntPtr previous = _liveUiTexture;
            _liveUiTexture = IntPtr.Zero;
            _uiSourceName = string.Empty;
            _uiUpdatedMilliseconds = 0;
            if (previous != IntPtr.Zero)
            {
                _ = Marshal.Release(previous);
            }
        }
    }

    public static void UpdateStereoTextures(
        IntPtr leftTexture,
        IntPtr rightTexture,
        long presentationGeneration = 0,
        bool requiresDynamicUi = false,
        OpenXrStereoStateSnapshot? renderState = null,
        bool usesWorldSpace = false)
    {
        if (leftTexture == IntPtr.Zero || rightTexture == IntPtr.Zero)
        {
            return;
        }
        if (presentationGeneration > 0 &&
            !D3D12DeviceCapture.IsPresentationGenerationCurrent(presentationGeneration))
        {
            return;
        }

        Guid interfaceId = Id3D12Resource;
        int leftResult = Marshal.QueryInterface(
            leftTexture,
            ref interfaceId,
            out IntPtr leftD3D12Resource);
        interfaceId = Id3D12Resource;
        int rightResult = Marshal.QueryInterface(
            rightTexture,
            ref interfaceId,
            out IntPtr rightD3D12Resource);
        if (leftResult < 0 || rightResult < 0 ||
            leftD3D12Resource == IntPtr.Zero || rightD3D12Resource == IntPtr.Zero)
        {
            if (leftD3D12Resource != IntPtr.Zero)
            {
                _ = Marshal.Release(leftD3D12Resource);
            }
            if (rightD3D12Resource != IntPtr.Zero)
            {
                _ = Marshal.Release(rightD3D12Resource);
            }
            return;
        }

        lock (Sync)
        {
            IntPtr uiWorldTexture = IntPtr.Zero;
            if (requiresDynamicUi)
            {
                bool freshM6World = _liveWorldTexture != IntPtr.Zero &&
                    Environment.TickCount64 - _updatedMilliseconds <= 1_500 &&
                    _sourceName.StartsWith("M6_NONLIVE|", StringComparison.Ordinal);
                if (!freshM6World)
                {
                    _ = Marshal.Release(leftD3D12Resource);
                    _ = Marshal.Release(rightD3D12Resource);
                    return;
                }
                uiWorldTexture = _liveWorldTexture;
                _ = Marshal.AddRef(uiWorldTexture);
            }
            if (presentationGeneration > 0 &&
                _lastPublishedStereoGeneration > 0 &&
                _lastPublishedStereoGeneration != presentationGeneration &&
                (leftD3D12Resource == _lastPublishedStereoLeft ||
                 rightD3D12Resource == _lastPublishedStereoRight))
            {
                _ = Marshal.Release(leftD3D12Resource);
                _ = Marshal.Release(rightD3D12Resource);
                if (uiWorldTexture != IntPtr.Zero)
                {
                    _ = Marshal.Release(uiWorldTexture);
                }
                return;
            }
            IntPtr previousLeft = _stereoLeftTexture;
            IntPtr previousRight = _stereoRightTexture;
            IntPtr previousUiWorld = _stereoUiWorldTexture;
            _stereoLeftTexture = leftD3D12Resource;
            _stereoRightTexture = rightD3D12Resource;
            _stereoUpdatedMilliseconds = Environment.TickCount64;
            _stereoPublishedTimestamp = Stopwatch.GetTimestamp();
            _stereoSequence++;
            _stereoPresentationGeneration = presentationGeneration;
            if (presentationGeneration > 0)
            {
                _lastPublishedStereoLeft = leftD3D12Resource;
                _lastPublishedStereoRight = rightD3D12Resource;
                _lastPublishedStereoGeneration = presentationGeneration;
            }
            _stereoRequiresDynamicUi = requiresDynamicUi;
            _stereoUiWorldTexture = uiWorldTexture;
            _stereoRenderState = renderState;
            _stereoUsesWorldSpace = usesWorldSpace;
            if (previousLeft != IntPtr.Zero)
            {
                _ = Marshal.Release(previousLeft);
            }
            if (previousRight != IntPtr.Zero)
            {
                _ = Marshal.Release(previousRight);
            }
            if (previousUiWorld != IntPtr.Zero)
            {
                _ = Marshal.Release(previousUiWorld);
            }
        }
    }

    public static D3D11StereoTextureLease? AcquireStereoTextures(
        int maximumAgeMilliseconds,
        long presentationGeneration = 0)
    {
        lock (Sync)
        {
            if (_stereoLeftTexture == IntPtr.Zero || _stereoRightTexture == IntPtr.Zero ||
                Environment.TickCount64 - _stereoUpdatedMilliseconds > maximumAgeMilliseconds ||
                (presentationGeneration > 0 &&
                    (_stereoPresentationGeneration != presentationGeneration ||
                     !D3D12DeviceCapture.IsPresentationGenerationCurrent(
                         presentationGeneration))))
            {
                return null;
            }

            _ = Marshal.AddRef(_stereoLeftTexture);
            _ = Marshal.AddRef(_stereoRightTexture);
            if (_stereoUiWorldTexture != IntPtr.Zero)
            {
                _ = Marshal.AddRef(_stereoUiWorldTexture);
            }
            IncrementStereoLeaseCount(_stereoLeftTexture);
            IncrementStereoLeaseCount(_stereoRightTexture);
            return new D3D11StereoTextureLease(
                _stereoLeftTexture,
                _stereoRightTexture,
                _stereoPublishedTimestamp,
                _stereoSequence,
                _stereoPresentationGeneration,
                _stereoRequiresDynamicUi,
                _stereoUiWorldTexture,
                _stereoRenderState,
                _stereoUsesWorldSpace);
        }
    }

    public static bool HasFreshStereoTextures(int maximumAgeMilliseconds)
    {
        lock (Sync)
        {
            return _stereoLeftTexture != IntPtr.Zero &&
                _stereoRightTexture != IntPtr.Zero &&
                Environment.TickCount64 - _stereoUpdatedMilliseconds <= maximumAgeMilliseconds;
        }
    }

    public static bool CanWriteStereoTextures(
        IntPtr leftTexture,
        IntPtr rightTexture,
        long presentationGeneration = 0)
    {
        if (leftTexture == IntPtr.Zero || rightTexture == IntPtr.Zero)
        {
            return false;
        }

        lock (Sync)
        {
            if (presentationGeneration > 0 &&
                !D3D12DeviceCapture.IsPresentationGenerationCurrent(presentationGeneration))
            {
                return false;
            }
            if (leftTexture == _stereoLeftTexture || rightTexture == _stereoRightTexture)
            {
                return false;
            }

            return !StereoLeaseCounts.ContainsKey(leftTexture) &&
                !StereoLeaseCounts.ContainsKey(rightTexture);
        }
    }

    public static bool TryRetireUnleasedStereoPublication(
        long presentationGeneration = 0)
    {
        IntPtr previousLeft;
        IntPtr previousRight;
        IntPtr previousUiWorld;
        lock (Sync)
        {
            if (_stereoLeftTexture == IntPtr.Zero ||
                _stereoRightTexture == IntPtr.Zero)
            {
                return false;
            }
            if (presentationGeneration > 0 &&
                (_stereoPresentationGeneration != presentationGeneration ||
                 !D3D12DeviceCapture.IsPresentationGenerationCurrent(
                     presentationGeneration)))
            {
                return false;
            }
            if (StereoLeaseCounts.ContainsKey(_stereoLeftTexture) ||
                StereoLeaseCounts.ContainsKey(_stereoRightTexture))
            {
                return false;
            }

            previousLeft = _stereoLeftTexture;
            previousRight = _stereoRightTexture;
            _stereoLeftTexture = IntPtr.Zero;
            _stereoRightTexture = IntPtr.Zero;
            _stereoUpdatedMilliseconds = 0;
            _stereoPublishedTimestamp = 0;
            _stereoPresentationGeneration = 0;
            _stereoRequiresDynamicUi = false;
            previousUiWorld = _stereoUiWorldTexture;
            _stereoUiWorldTexture = IntPtr.Zero;
            _stereoRenderState = null;
            _stereoUsesWorldSpace = false;
        }

        _ = Marshal.Release(previousLeft);
        _ = Marshal.Release(previousRight);
        if (previousUiWorld != IntPtr.Zero)
        {
            _ = Marshal.Release(previousUiWorld);
        }
        return true;
    }

    internal static void ReleaseStereoTextureLease(
        IntPtr leftTexture,
        IntPtr rightTexture,
        IntPtr uiWorldTexture)
    {
        lock (Sync)
        {
            DecrementStereoLeaseCount(leftTexture);
            DecrementStereoLeaseCount(rightTexture);
        }

        if (leftTexture != IntPtr.Zero)
        {
            _ = Marshal.Release(leftTexture);
        }
        if (rightTexture != IntPtr.Zero)
        {
            _ = Marshal.Release(rightTexture);
        }
        if (uiWorldTexture != IntPtr.Zero)
        {
            _ = Marshal.Release(uiWorldTexture);
        }
    }

    private static void IncrementStereoLeaseCount(IntPtr texture)
    {
        StereoLeaseCounts.TryGetValue(texture, out int count);
        StereoLeaseCounts[texture] = checked(count + 1);
    }

    private static void DecrementStereoLeaseCount(IntPtr texture)
    {
        if (texture == IntPtr.Zero || !StereoLeaseCounts.TryGetValue(texture, out int count))
        {
            return;
        }

        if (count <= 1)
        {
            _ = StereoLeaseCounts.Remove(texture);
        }
        else
        {
            StereoLeaseCounts[texture] = count - 1;
        }
    }

    public static bool TouchStereoTextures(long presentationGeneration = 0)
    {
        lock (Sync)
        {
            if (_stereoLeftTexture == IntPtr.Zero || _stereoRightTexture == IntPtr.Zero)
            {
                return false;
            }
            if (presentationGeneration > 0 &&
                (_stereoPresentationGeneration != presentationGeneration ||
                 !D3D12DeviceCapture.IsPresentationGenerationCurrent(
                     presentationGeneration)))
            {
                return false;
            }

            _stereoUpdatedMilliseconds = Environment.TickCount64;
            return true;
        }
    }

    public static void ClearStereoTextures()
    {
        lock (Sync)
        {
            IntPtr previousLeft = _stereoLeftTexture;
            IntPtr previousRight = _stereoRightTexture;
            _stereoLeftTexture = IntPtr.Zero;
            _stereoRightTexture = IntPtr.Zero;
            _stereoUpdatedMilliseconds = 0;
            _stereoPublishedTimestamp = 0;
            _stereoPresentationGeneration = 0;
            _stereoRequiresDynamicUi = false;
            IntPtr previousUiWorld = _stereoUiWorldTexture;
            _stereoUiWorldTexture = IntPtr.Zero;
            _stereoRenderState = null;
            _stereoUsesWorldSpace = false;
            if (previousLeft != IntPtr.Zero)
            {
                _ = Marshal.Release(previousLeft);
            }
            if (previousRight != IntPtr.Zero)
            {
                _ = Marshal.Release(previousRight);
            }
            if (previousUiWorld != IntPtr.Zero)
            {
                _ = Marshal.Release(previousUiWorld);
            }
        }
    }

    public static bool HasActiveStereoTextureLeases()
    {
        lock (Sync)
        {
            return StereoLeaseCounts.Count != 0;
        }
    }
}
