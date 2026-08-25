namespace SongPrismVR.Core;

public static class ComPointerGuard
{
    private const long MinimumWindowsApplicationAddress = 0x10000;

    public static bool IsPlausible(IntPtr pointer)
    {
        long value = pointer.ToInt64();
        return value >= MinimumWindowsApplicationAddress &&
            (value & (IntPtr.Size - 1)) == 0;
    }
}
