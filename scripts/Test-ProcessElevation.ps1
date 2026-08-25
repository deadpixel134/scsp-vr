param(
    [Parameter(Mandatory = $true)]
    [int]$ProcessId
)

Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class TokenElevationProbe {
    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool GetTokenInformation(IntPtr TokenHandle, int TokenInformationClass, out int TokenInformation, int TokenInformationLength, out int ReturnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr hObject);

    public static bool IsElevated(int processId) {
        System.Diagnostics.Process process = System.Diagnostics.Process.GetProcessById(processId);
        IntPtr token = IntPtr.Zero;
        try {
            if (!OpenProcessToken(process.Handle, 0x0008, out token)) {
                return false;
            }
            int elevation = 0;
            int returnLength = 0;
            if (!GetTokenInformation(token, 20, out elevation, sizeof(int), out returnLength)) {
                return false;
            }
            return elevation != 0;
        }
        finally {
            if (token != IntPtr.Zero) {
                CloseHandle(token);
            }
            process.Dispose();
        }
    }
}
"@

$isElevated = [TokenElevationProbe]::IsElevated($ProcessId)
$process = Get-Process -Id $ProcessId -ErrorAction Stop
[pscustomobject]@{
    ProcessId = $ProcessId
    ProcessName = $process.ProcessName
    Path = $process.Path
    IsElevated = $isElevated
}
