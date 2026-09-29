#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][ValidateSet('Export','Import')][string]$Action,
    [Parameter(Mandatory=$true)][ValidateNotNullOrEmpty()][string]$Market,
    [Parameter(Mandatory=$true)][ValidateNotNullOrEmpty()][string]$Archive,
    [string]$DataRoot
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.IO.Compression
Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

public static class MarketHistorySqlite {
    [DllImport("winsqlite3", EntryPoint="sqlite3_open_v2", CallingConvention=CallingConvention.Cdecl)]
    static extern int Open(byte[] path, out IntPtr db, int flags, IntPtr vfs);
    [DllImport("winsqlite3", EntryPoint="sqlite3_close_v2", CallingConvention=CallingConvention.Cdecl)]
    static extern int Close(IntPtr db);
    [DllImport("winsqlite3", EntryPoint="sqlite3_busy_timeout", CallingConvention=CallingConvention.Cdecl)]
    static extern int BusyTimeout(IntPtr db, int ms);
    [DllImport("winsqlite3", EntryPoint="sqlite3_errmsg", CallingConvention=CallingConvention.Cdecl)]
    static extern IntPtr Error(IntPtr db);
    [DllImport("winsqlite3", EntryPoint="sqlite3_backup_init", CallingConvention=CallingConvention.Cdecl)]
    static extern IntPtr BackupInit(IntPtr dest, byte[] destName, IntPtr source, byte[] sourceName);
    [DllImport("winsqlite3", EntryPoint="sqlite3_backup_step", CallingConvention=CallingConvention.Cdecl)]
    static extern int BackupStep(IntPtr backup, int pages);
    [DllImport("winsqlite3", EntryPoint="sqlite3_backup_finish", CallingConvention=CallingConvention.Cdecl)]
    static extern int BackupFinish(IntPtr backup);
    [DllImport("winsqlite3", EntryPoint="sqlite3_prepare_v2", CallingConvention=CallingConvention.Cdecl)]
    static extern int Prepare(IntPtr db, byte[] sql, int bytes, out IntPtr statement, IntPtr tail);
    [DllImport("winsqlite3", EntryPoint="sqlite3_step", CallingConvention=CallingConvention.Cdecl)]
    static extern int Step(IntPtr statement);
    [DllImport("winsqlite3", EntryPoint="sqlite3_column_text", CallingConvention=CallingConvention.Cdecl)]
    static extern IntPtr ColumnText(IntPtr statement, int column);
    [DllImport("winsqlite3", EntryPoint="sqlite3_finalize", CallingConvention=CallingConvention.Cdecl)]
    static extern int Finalize(IntPtr statement);
    static byte[] Utf8(string text) { return Encoding.UTF8.GetBytes(text + "\0"); }
    static IntPtr Connect(string path, int flags) {
        IntPtr db;
        int result = Open(Utf8(path), out db, flags, IntPtr.Zero);
        if (result != 0) {
            string message = db == IntPtr.Zero ? "open failed" : Marshal.PtrToStringAnsi(Error(db));
            if (db != IntPtr.Zero) Close(db);
            throw new InvalidDataException("SQLite " + result + ": " + message);
        }
        BusyTimeout(db, 5000);
        return db;
    }
    static string Scalar(IntPtr db, string sql) {
        IntPtr statement;
        int result = Prepare(db, Utf8(sql), -1, out statement, IntPtr.Zero);
        if (result != 0) throw new InvalidDataException("SQLite query failed: " + result);
        try {
            result = Step(statement);
            if (result != 100) throw new InvalidDataException("SQLite query returned no row: " + result);
            return Marshal.PtrToStringAnsi(ColumnText(statement, 0));
        } finally { Finalize(statement); }
    }
    public static void Validate(string path) {
        IntPtr db = Connect(path, 1);
        try {
            if (Scalar(db, "PRAGMA quick_check") != "ok" || Scalar(db, "PRAGMA user_version") != "1" ||
                Scalar(db, "SELECT count(*) FROM sqlite_master WHERE type='table' AND name IN ('traders','checks','snapshots','outbox')") != "4")
                throw new InvalidDataException("Market database integrity or schema check failed.");
        } finally { Close(db); }
    }
    public static void Snapshot(string sourcePath, string destinationPath) {
        IntPtr source = Connect(sourcePath, 1);
        try {
            IntPtr destination = Connect(destinationPath, 2 | 4);
            try {
                IntPtr backup = BackupInit(destination, Utf8("main"), source, Utf8("main"));
                if (backup == IntPtr.Zero) throw new InvalidDataException("SQLite backup initialization failed.");
                try {
                    int result;
                    int retries = 0;
                    while ((result = BackupStep(backup, 256)) != 101) {
                        if (result == 0) continue;
                        if ((result == 5 || result == 6) && ++retries <= 50) { Thread.Sleep(100); continue; }
                        throw new InvalidDataException("SQLite backup failed: " + result);
                    }
                } finally { BackupFinish(backup); }
            } finally { Close(destination); }
        } finally { Close(source); }
        Validate(destinationPath);
    }
}
'@

$key = $Market.Normalize([Text.NormalizationForm]::FormKC).Trim().ToUpperInvariant()
if (-not $key) { throw 'Market name is empty.' }
$sha = [Security.Cryptography.SHA256]::Create()
try { $hash = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($key))).Replace('-','').Substring(0,24) }
finally { $sha.Dispose() }
$collection = if ($DataRoot) { [IO.Path]::GetFullPath($DataRoot) } else {
    Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'PriceCheckCollector\collection'
}
$markets = Join-Path $collection 'markets'
$marketDirectory = Join-Path $markets $hash
$database = Join-Path $marketDirectory 'market.sqlite3'
$archivePath = [IO.Path]::GetFullPath($Archive)
$maxBytes = 16GB
function Assert-MarketPath([string]$path) {
    $root = [IO.Path]::GetFullPath($markets).TrimEnd('\') + '\'
    if (-not [IO.Path]::GetFullPath($path).StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Market path escapes the selected history directory.'
    }
}

if ($Action -eq 'Export') {
    if (-not (Test-Path -LiteralPath $database -PathType Leaf)) { throw "No local history for market $Market." }
    if (Test-Path -LiteralPath $archivePath) { throw 'Archive already exists; choose a new path.' }
    $archiveParent = Split-Path -Parent $archivePath
    [void][IO.Directory]::CreateDirectory($archiveParent)
    $stage = Join-Path $archiveParent ('market-history-stage-' + [guid]::NewGuid().ToString('N'))
    [void][IO.Directory]::CreateDirectory($stage)
    try {
        $snapshot = Join-Path $stage 'market.sqlite3'
        [MarketHistorySqlite]::Snapshot($database, $snapshot)
        $item = Get-Item -LiteralPath $snapshot
        if ($item.Length -gt $maxBytes) { throw 'Market database exceeds the archive size limit.' }
        $manifest = [ordered]@{ format='PriceCheckMarketHistory'; schema=1; market=$key;
            databaseBytes=$item.Length; databaseSha256=(Get-FileHash -LiteralPath $snapshot -Algorithm SHA256).Hash;
            createdUtc=[DateTimeOffset]::UtcNow.ToString('o') }
        $manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $stage 'manifest.json') -Encoding UTF8
        $zip = [IO.Compression.ZipFile]::Open($archivePath, [IO.Compression.ZipArchiveMode]::Create)
        try {
            [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, (Join-Path $stage 'manifest.json'), 'manifest.json')
            [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $snapshot, 'market.sqlite3', [IO.Compression.CompressionLevel]::Optimal)
        } finally { $zip.Dispose() }
        Write-Output "Exported $key history: $archivePath"
    } catch {
        if (Test-Path -LiteralPath $archivePath -PathType Leaf) { Remove-Item -LiteralPath $archivePath -Force }
        throw
    } finally {
        if ($stage.StartsWith($archiveParent.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $stage)) {
            Remove-Item -LiteralPath $stage -Recurse -Force
        }
    }
    return
}

if (-not $DataRoot -and (Get-Process -Name 'PriceCheck.Collector' -ErrorAction SilentlyContinue)) {
    throw 'Close PriceCheck Collector before importing market history.'
}
if (-not (Test-Path -LiteralPath $archivePath -PathType Leaf)) { throw 'History archive does not exist.' }
[void][IO.Directory]::CreateDirectory($markets)
$stage = Join-Path $markets ('import-' + [guid]::NewGuid().ToString('N'))
Assert-MarketPath $stage
Assert-MarketPath $marketDirectory
[void][IO.Directory]::CreateDirectory($stage)
try {
    $zip = [IO.Compression.ZipFile]::OpenRead($archivePath)
    try {
        $entries = @($zip.Entries)
        if ($entries.Count -ne 2 -or @($entries | Where-Object { $_.FullName -notin @('manifest.json','market.sqlite3') }).Count -ne 0) {
            throw 'Unexpected history archive contents.'
        }
        $manifestEntry = $zip.GetEntry('manifest.json')
        $databaseEntry = $zip.GetEntry('market.sqlite3')
        if (-not $manifestEntry -or -not $databaseEntry -or $manifestEntry.Length -gt 65536 -or
            $databaseEntry.Length -le 0 -or $databaseEntry.Length -gt $maxBytes) { throw 'Invalid history archive entries.' }
        $reader = New-Object IO.StreamReader($manifestEntry.Open())
        try { $manifest = $reader.ReadToEnd() | ConvertFrom-Json }
        finally { $reader.Dispose() }
        if ($manifest.format -ne 'PriceCheckMarketHistory' -or $manifest.schema -ne 1 -or $manifest.market -cne $key -or
            $manifest.databaseBytes -ne $databaseEntry.Length -or $manifest.databaseSha256 -notmatch '^[A-Fa-f0-9]{64}$') {
            throw 'History archive does not match this market or format.'
        }
        $stagedDatabase = Join-Path $stage 'market.sqlite3'
        $inputStream = $databaseEntry.Open()
        $outputStream = [IO.File]::Create($stagedDatabase)
        try {
            $buffer = New-Object byte[] 65536
            [long]$written = 0
            while (($count = $inputStream.Read($buffer,0,$buffer.Length)) -gt 0) {
                $written += $count
                if ($written -gt $maxBytes) { throw 'History archive exceeds size limit.' }
                $outputStream.Write($buffer,0,$count)
            }
        } finally { $outputStream.Dispose(); $inputStream.Dispose() }
        if ($written -ne $databaseEntry.Length -or
            (Get-FileHash -LiteralPath $stagedDatabase -Algorithm SHA256).Hash -ne $manifest.databaseSha256) {
            throw 'History archive checksum mismatch.'
        }
        [MarketHistorySqlite]::Validate($stagedDatabase)
    } finally { $zip.Dispose() }
    if ((Test-Path -LiteralPath $marketDirectory) -and
        ((Get-Item -LiteralPath $marketDirectory).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw 'Refusing to replace a linked market directory.'
    }
    $backup = $null
    if (Test-Path -LiteralPath $marketDirectory) {
        $backup = Join-Path $markets ($hash + '.before-import-' + [DateTimeOffset]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0,6))
        Assert-MarketPath $backup
        Move-Item -LiteralPath $marketDirectory -Destination $backup
    }
    try { Move-Item -LiteralPath $stage -Destination $marketDirectory }
    catch {
        if ($backup -and (Test-Path -LiteralPath $backup)) { Move-Item -LiteralPath $backup -Destination $marketDirectory }
        throw
    }
    Write-Output "Imported $key history: $database"
    if ($backup) { Write-Output "Previous history backup: $backup" }
} finally {
    if ($stage.StartsWith($markets.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $stage)) {
        Remove-Item -LiteralPath $stage -Recurse -Force
    }
}
