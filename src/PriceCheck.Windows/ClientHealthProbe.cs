using System.Diagnostics;
using System.Runtime.InteropServices;
using PriceCheck.Contracts;

namespace PriceCheck.Windows;

public sealed record ClientHealth(bool Current, bool Responsive, bool FatalWindow, bool? Connected);

// OS transport/liveness only; no extra game reader or client memory access.
public static class ClientHealthProbe
{
    public static ClientHealth Read(ClientSession session)
    {
        if(!ClientProcessIdentity.IsCurrent(session)) return new(false,false,false,null);
        bool responsive=true,fatal=false;
        try
        {
            using var process=Process.GetProcessById(session.ProcessId);
            responsive=process.Responding;
            fatal=process.MainWindowTitle.Contains("Fatal error",StringComparison.OrdinalIgnoreCase);
        }
        catch { return new(false,false,false,null); }
        return new(true,responsive,fatal,HasConnection(session.ProcessId));
    }

    private static bool? HasConnection(int pid)
    {
        try
        {
            var ipv4=ReadTable(pid,2,24,20);
            var ipv6=ReadTable(pid,23,56,52);
            if(ipv4==true || ipv6==true) return true;
            return ipv4 is null || ipv6 is null ? null : false;
        }
        catch { return null; }
    }
    private static bool? ReadTable(int pid,int family,int rowSize,int pidOffset)
    {
        int size=0;
        var status=GetExtendedTcpTable(IntPtr.Zero,ref size,false,family,5,0); // OWNER_PID_ALL
        if(status!=122 && status!=0 || size<4 || size>16*1024*1024) return null;
        var buffer=Marshal.AllocHGlobal(size);
        try
        {
            if(GetExtendedTcpTable(buffer,ref size,false,family,5,0)!=0) return null;
            var count=Marshal.ReadInt32(buffer);
            if(count<0 || count>(size-4)/rowSize) return null;
            for(var i=0;i<count;i++)
            {
                var row=IntPtr.Add(buffer,4+i*rowSize);
                var stateOffset=family==2?0:48;
                if(Marshal.ReadInt32(row,pidOffset)==pid && Marshal.ReadInt32(row,stateOffset)==5) return true;
            }
            return false;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    [DllImport("iphlpapi.dll")]
    private static extern uint GetExtendedTcpTable(IntPtr table,ref int size,[MarshalAs(UnmanagedType.Bool)] bool ordered,int family,int tableClass,uint reserved);
}
