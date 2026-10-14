[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Executable,
    [string]$Arguments = '',
    [string]$LogPath = 'artifacts/ui-test.log',
    [int]$TimeoutSeconds = 180
)
$ErrorActionPreference = 'Stop'
# A separate desktop renders real HWNDs without showing, focusing or manipulating user windows.
# Only the process created here is terminated if it exceeds the test timeout.
Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
public static class IsolatedLauncherTests {
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    struct Startup {
        public int cb; public string reserved, desktop, title;
        public int x,y,cx,cy,charsX,charsY,fill,flags; public short show, reserved2;
        public IntPtr reservedPointer, input, output, error;
    }
    [StructLayout(LayoutKind.Sequential)] struct ProcessInfo { public IntPtr process, thread; public uint pid, tid; }
    [StructLayout(LayoutKind.Sequential)] struct Security { public int length; public IntPtr descriptor; public int inherit; }
    [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern IntPtr CreateDesktop(string name, IntPtr device, IntPtr mode, int flags, uint access, IntPtr security);
    [DllImport("user32.dll")] static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool CreateProcess(string app, StringBuilder command, IntPtr processSecurity, IntPtr threadSecurity, bool inherit, uint flags, IntPtr environment, string directory, ref Startup startup, out ProcessInfo process);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern IntPtr CreateFile(string path, uint access, uint share, ref Security security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr handle, uint ms);
    [DllImport("kernel32.dll")] static extern bool GetExitCodeProcess(IntPtr handle, out uint code);
    [DllImport("kernel32.dll")] static extern bool TerminateProcess(IntPtr handle, uint code);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    public static int Run(string exe, string args, string log, int seconds) {
        var name = "GemmaLauncherTests-" + Guid.NewGuid().ToString("N");
        var desktop = CreateDesktop(name, IntPtr.Zero, IntPtr.Zero, 0, 0x10000000, IntPtr.Zero);
        if(desktop == IntPtr.Zero) throw new Win32Exception();
        var sa = new Security { length=Marshal.SizeOf<Security>(), inherit=1 };
        var output = CreateFile(log, 0x40000000, 3, ref sa, 2, 0x80, IntPtr.Zero);
        var pi = new ProcessInfo();
        try {
            if(output == new IntPtr(-1)) throw new Win32Exception();
            var si = new Startup { cb=Marshal.SizeOf<Startup>(), desktop=name, flags=0x100, output=output, error=output };
            if(!CreateProcess(exe, new StringBuilder("\""+exe+"\" "+args), IntPtr.Zero, IntPtr.Zero, true, 0x08000000, IntPtr.Zero, System.IO.Path.GetDirectoryName(exe), ref si, out pi)) throw new Win32Exception();
            if(WaitForSingleObject(pi.process, checked((uint)seconds*1000)) != 0) {
                TerminateProcess(pi.process, 124); WaitForSingleObject(pi.process, 5000); return 124;
            }
            GetExitCodeProcess(pi.process, out var code); return (int)code;
        } finally {
            if(pi.thread != IntPtr.Zero) CloseHandle(pi.thread);
            if(pi.process != IntPtr.Zero) CloseHandle(pi.process);
            if(output != new IntPtr(-1)) CloseHandle(output);
            CloseDesktop(desktop);
        }
    }
}
'@
$testExe = (Resolve-Path -LiteralPath $Executable).Path
$testLog = [IO.Path]::GetFullPath($LogPath)
$null = New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($testLog))
$result = [IsolatedLauncherTests]::Run($testExe, $Arguments, $testLog, $TimeoutSeconds)
Get-Content -LiteralPath $testLog
if ($result -ne 0) { throw "Isolated test process exited with code $result. Log: $testLog" }
