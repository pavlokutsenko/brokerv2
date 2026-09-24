param([Parameter(Mandatory)][string]$Executable, [string]$AgentPath, [int]$ResumeSeconds = 0)
$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class EarlyProbeNative {
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    public struct StartupInfo {
        public int cb;
        public string reserved;
        public string desktop;
        public string title;
        public int x, y, xSize, ySize, xCountChars, yCountChars, fillAttribute, flags;
        public short showWindow, reserved2;
        public IntPtr reserved2Pointer, standardInput, standardOutput, standardError;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct ProcessInformation {
        public IntPtr process, thread;
        public int processId, threadId;
    }
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    public static extern bool CreateProcessW(string application, StringBuilder commandLine,
        IntPtr processAttributes, IntPtr threadAttributes, bool inheritHandles, int creationFlags,
        IntPtr environment, string currentDirectory, ref StartupInfo startup,
        out ProcessInformation information);
    [DllImport("ntdll.dll")]
    public static extern int NtQueryObject(IntPtr handle, int informationClass,
        IntPtr information, int length, out int returnLength);
    [DllImport("kernel32.dll", SetLastError=true)]
    public static extern bool TerminateProcess(IntPtr process, int exitCode);
    [DllImport("kernel32.dll")]
    public static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError=true)]
    public static extern IntPtr VirtualAllocEx(IntPtr process, IntPtr address, IntPtr size, int allocationType, int protection);
    [DllImport("kernel32.dll", SetLastError=true)]
    public static extern bool WriteProcessMemory(IntPtr process, IntPtr address, byte[] buffer, IntPtr size, out IntPtr written);
    [DllImport("kernel32.dll", SetLastError=true)]
    public static extern IntPtr CreateRemoteThread(IntPtr process, IntPtr attributes, IntPtr stackSize,
        IntPtr startAddress, IntPtr parameter, int flags, out int threadId);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)]
    public static extern IntPtr GetModuleHandleW(string name);
    [DllImport("psapi.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    public static extern int GetMappedFileNameW(IntPtr process, IntPtr address,
        StringBuilder fileName, int size);
    [DllImport("kernel32.dll", CharSet=CharSet.Ansi)]
    public static extern IntPtr GetProcAddress(IntPtr module, string name);
    [DllImport("kernel32.dll", SetLastError=true)]
    public static extern int WaitForSingleObject(IntPtr handle, int timeout);
    [DllImport("kernel32.dll", SetLastError=true)]
    public static extern bool GetExitCodeThread(IntPtr thread, out int code);
    [DllImport("kernel32.dll", SetLastError=true)]
    public static extern int ResumeThread(IntPtr thread);
    public static int GrantedAccess(IntPtr handle) {
        IntPtr buffer = Marshal.AllocHGlobal(56);
        try {
            int returned;
            int status = NtQueryObject(handle, 0, buffer, 56, out returned);
            if (status != 0) throw new InvalidOperationException("NtQueryObject status: 0x" + status.ToString("X8") + ", required=" + returned);
            return Marshal.ReadInt32(buffer, 4);
        } finally { Marshal.FreeHGlobal(buffer); }
    }
}
'@

$path = (Resolve-Path -LiteralPath $Executable).Path
$startup = New-Object EarlyProbeNative+StartupInfo
$startup.cb = [Runtime.InteropServices.Marshal]::SizeOf([type]'EarlyProbeNative+StartupInfo')
$commandLine = [Text.StringBuilder]::new('"' + $path + '"')
$information = New-Object EarlyProbeNative+ProcessInformation
$created = [EarlyProbeNative]::CreateProcessW($path, $commandLine, [IntPtr]::Zero,
    [IntPtr]::Zero, $false, 4, [IntPtr]::Zero, (Split-Path -Parent $path),
    [ref]$startup, [ref]$information)
if (-not $created) { throw "CreateProcessW failed: $([Runtime.InteropServices.Marshal]::GetLastWin32Error())" }
try {
    $processAccess = [EarlyProbeNative]::GrantedAccess($information.process)
    $threadAccess = [EarlyProbeNative]::GrantedAccess($information.thread)
    [pscustomobject]@{
        RootPid = $information.processId
        ProcessAccess = ('0x{0:X8}' -f $processAccess)
        ThreadAccess = ('0x{0:X8}' -f $threadAccess)
        CanCreateThread = [bool]($processAccess -band 0x2)
        CanWriteMemory = [bool]($processAccess -band 0x20)
        CanSetThreadContext = [bool]($threadAccess -band 0x10)
    }
    Get-Process -Id $information.processId -Module -ErrorAction SilentlyContinue |
        Where-Object { $_.ModuleName -in @('ntdll.dll','kernel32.dll','KernelBase.dll') } |
        Select-Object ModuleName,BaseAddress
    foreach($module in @('ntdll.dll','kernel32.dll','KernelBase.dll')) {
        $base = [EarlyProbeNative]::GetModuleHandleW($module)
        $fileName = [Text.StringBuilder]::new(520)
        $count = [EarlyProbeNative]::GetMappedFileNameW($information.process, $base,
            $fileName, $fileName.Capacity)
        [pscustomobject]@{ Module=$module; ParentBase=$base;
            ChildMapping=$(if($count){$fileName.ToString()}else{''}) }
    }
    if ($AgentPath) {
        $agent = (Resolve-Path -LiteralPath $AgentPath).Path
        $payload = [Text.Encoding]::Unicode.GetBytes($agent + [char]0)
        $remote = [EarlyProbeNative]::VirtualAllocEx($information.process, [IntPtr]::Zero,
            [IntPtr]::new($payload.Length), 0x3000, 4)
        if ($remote -eq [IntPtr]::Zero) { throw "VirtualAllocEx failed: $([Runtime.InteropServices.Marshal]::GetLastWin32Error())" }
        $written = [IntPtr]::Zero
        if (-not [EarlyProbeNative]::WriteProcessMemory($information.process, $remote, $payload,
            [IntPtr]::new($payload.Length), [ref]$written) -or $written.ToInt64() -ne $payload.Length) {
            throw "WriteProcessMemory failed: $([Runtime.InteropServices.Marshal]::GetLastWin32Error())"
        }
        $kernel32 = [EarlyProbeNative]::GetModuleHandleW('kernel32.dll')
        $loader = [EarlyProbeNative]::GetProcAddress($kernel32, 'LoadLibraryW')
        $threadId = 0
        $loaderThread = [EarlyProbeNative]::CreateRemoteThread($information.process, [IntPtr]::Zero,
            [IntPtr]::Zero, $loader, $remote, 0, [ref]$threadId)
        if ($loaderThread -eq [IntPtr]::Zero) { throw "CreateRemoteThread failed: $([Runtime.InteropServices.Marshal]::GetLastWin32Error())" }
        try {
            $wait = [EarlyProbeNative]::WaitForSingleObject($loaderThread, 10000)
            $code = 0
            [EarlyProbeNative]::GetExitCodeThread($loaderThread, [ref]$code) | Out-Null
            [pscustomobject]@{ LoaderWait=$wait; LoaderExit=('0x{0:X8}' -f $code); LoaderThreadId=$threadId }
        } finally { [EarlyProbeNative]::CloseHandle($loaderThread) | Out-Null }
    }
    if ($ResumeSeconds -gt 0) {
        $previous = [EarlyProbeNative]::ResumeThread($information.thread)
        if ($previous -lt 0) { throw "ResumeThread failed: $([Runtime.InteropServices.Marshal]::GetLastWin32Error())" }
        Start-Sleep -Seconds $ResumeSeconds
        Get-CimInstance Win32_Process -Filter "ParentProcessId=$($information.processId)" |
            Where-Object {$_.Name -eq 'lu4.bin'} | Select-Object ProcessId,ParentProcessId,Name
    }
} finally {
    if ($ResumeSeconds -gt 0) {
        Get-CimInstance Win32_Process -Filter "ParentProcessId=$($information.processId)" |
            Where-Object {$_.Name -eq 'lu4.bin'} | ForEach-Object {
                Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
            }
    }
    [EarlyProbeNative]::TerminateProcess($information.process, 0) | Out-Null
    [EarlyProbeNative]::CloseHandle($information.thread) | Out-Null
    [EarlyProbeNative]::CloseHandle($information.process) | Out-Null
}
