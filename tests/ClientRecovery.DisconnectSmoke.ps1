param([Parameter(Mandatory=$true)][int]$GamePid)
$ErrorActionPreference='Stop'
$profile=(Get-Content "$env:LOCALAPPDATA\PriceCheckCollector\profiles.json" -Raw | ConvertFrom-Json) |
    Where-Object { $_.Id -eq '6a0358a7-984d-4ef1-8570-283b79c3cf88' }
if($profile.LastProcessId -ne $GamePid -or !$profile.CollectionEnabled -or !$profile.AutoRestartEnabled) {
    throw 'Expected active Gamma profile with automatic recovery.'
}
$client=Get-Process -Id $GamePid
if($client.ProcessName -ne 'lu4' -and $client.ProcessName -ne 'lu4.bin') { throw 'Expected game process.' }
if($client.StartTime.ToUniversalTime() -ne ([DateTimeOffset]$profile.LastProcessStartUtc).UtcDateTime) {
    throw 'Game process generation differs from the profile.'
}
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class PriceCheckDisconnectSmoke {
  [DllImport("iphlpapi.dll")] static extern uint GetExtendedTcpTable(IntPtr table,ref int size,bool ordered,int family,int kind,uint reserved);
  [DllImport("iphlpapi.dll")] static extern uint SetTcpEntry(ref Row row);
  [StructLayout(LayoutKind.Sequential)] public struct Row { public uint state,localAddress,localPort,remoteAddress,remotePort; }
  public static uint CloseOnlyConnection(int pid) {
    int size=0;var code=GetExtendedTcpTable(IntPtr.Zero,ref size,false,2,5,0);
    if(code!=122 || size<4 || size>16777216) throw new Exception("Cannot size game TCP table.");
    var table=Marshal.AllocHGlobal(size);
    try {
      if(GetExtendedTcpTable(table,ref size,false,2,5,0)!=0) throw new Exception("Cannot read game TCP table.");
      int count=Marshal.ReadInt32(table),matches=0;Row chosen=new Row();
      if(count<0 || count>(size-4)/24) throw new Exception("Invalid TCP table.");
      for(int i=0;i<count;i++) {
        var row=IntPtr.Add(table,4+i*24);
        if(Marshal.ReadInt32(row,20)==pid && Marshal.ReadInt32(row)==5) {
          chosen=Marshal.PtrToStructure<Row>(row);matches++;
        }
      }
      if(matches!=1) throw new Exception("Expected exactly one established game connection.");
      chosen.state=12;
      return SetTcpEntry(ref chosen);
    } finally { Marshal.FreeHGlobal(table); }
  }
}
'@
$result=[PriceCheckDisconnectSmoke]::CloseOnlyConnection($GamePid)
if($result -ne 0) { throw "Controlled disconnect failed: Windows status $result; connection unchanged." }
[pscustomobject]@{at=(Get-Date).ToString('o');pid=$GamePid;slot=$profile.CharacterSlot;result='game_tcp_closed'} | ConvertTo-Json
