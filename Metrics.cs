using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
namespace NeonDesk;

public sealed record LiveMetrics(double? Cpu,double Memory,double? Gpu,double Upload,double Download,double? DiskRead,double? DiskWrite,List<DriveMetric> Drives);
public sealed class Metrics : IDisposable
{
    private ulong _idle,_kernel,_user;private bool _sampled;
    private readonly Dictionary<string,(long Sent,long Received)> _net=[];
    private long _lastTick=Environment.TickCount64;
    private IntPtr _query,_gpu,_diskRead,_diskWrite;
    public Metrics()
    {
        try
        {
            if(PdhOpenQuery(null,IntPtr.Zero,out _query)==0)
            {
                if(PdhAddEnglishCounter(_query,@"\GPU Engine(*)\Utilization Percentage",IntPtr.Zero,out _gpu)!=0)_gpu=IntPtr.Zero;
                if(PdhAddEnglishCounter(_query,@"\PhysicalDisk(_Total)\Disk Read Bytes/sec",IntPtr.Zero,out _diskRead)!=0)_diskRead=IntPtr.Zero;
                if(PdhAddEnglishCounter(_query,@"\PhysicalDisk(_Total)\Disk Write Bytes/sec",IntPtr.Zero,out _diskWrite)!=0)_diskWrite=IntPtr.Zero;
                PdhCollectQueryData(_query);
            }
        }
        catch{_gpu=IntPtr.Zero;_diskRead=IntPtr.Zero;_diskWrite=IntPtr.Zero;}
    }
    public LiveMetrics Read()
    {
        double? cpu=null;
        if(GetSystemTimes(out var idle,out var kernel,out var user))
        {
            ulong i=idle.Value,k=kernel.Value,u=user.Value;
            if(_sampled&&k>=_kernel&&u>=_user&&i>=_idle){double total=(k-_kernel)+(u-_user);if(total>0)cpu=Math.Clamp(100*(total-(i-_idle))/total,0,100);}
            _idle=i;_kernel=k;_user=u;_sampled=true;
        }
        var memory=new Memory{Length=(uint)Marshal.SizeOf<Memory>()};bool memoryOk=GlobalMemoryStatusEx(ref memory);
        long now=Environment.TickCount64;double seconds=Math.Max(.1,(now-_lastTick)/1000d);_lastTick=now;
        double sent=0,received=0;
        foreach(var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if(adapter.OperationalStatus!=OperationalStatus.Up||adapter.NetworkInterfaceType is not (NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211))continue;
            try
            {
                // Exclude tunnel/VPN and software-only adapters to avoid counting traffic twice.
                if(adapter.Description.Contains("virtual",StringComparison.OrdinalIgnoreCase)||adapter.Description.Contains("tailscale",StringComparison.OrdinalIgnoreCase)||adapter.Description.Contains("vpn",StringComparison.OrdinalIgnoreCase))continue;
                var info=adapter.GetIPStatistics();
                if(_net.TryGetValue(adapter.Id,out var previous)){sent+=Math.Max(0,info.BytesSent-previous.Sent)/seconds;received+=Math.Max(0,info.BytesReceived-previous.Received)/seconds;}
                _net[adapter.Id]=(info.BytesSent,info.BytesReceived);
            }
            catch { }
        }
        var drives=new List<DriveMetric>();foreach(var drive in DriveCatalog.ReadyLocal()){try{drives.Add(new(drive.Name.TrimEnd('\\'),drive.TotalSize-drive.TotalFreeSpace,drive.TotalSize));}catch{}}
        bool countersReady=_query!=IntPtr.Zero&&PdhCollectQueryData(_query)==0;
        return new(cpu,memoryOk?memory.Load:double.NaN,countersReady?ReadGpu():null,sent,received,countersReady?ReadCounter(_diskRead):null,countersReady?ReadCounter(_diskWrite):null,drives);
    }
    private static double? ReadCounter(IntPtr counter)
    {
        if(counter==IntPtr.Zero||PdhGetFormattedCounterValue(counter,0x200,out _,out var value)!=0||value.Status>1||!double.IsFinite(value.Double))return null;
        return Math.Max(0,value.Double);
    }
    private double? ReadGpu()
    {
        if(_query==IntPtr.Zero||_gpu==IntPtr.Zero)return null;
        try
        {
            uint bytes=0,count=0;PdhGetFormattedCounterArray(_gpu,0x200,ref bytes,ref count,IntPtr.Zero);
            if(bytes==0||bytes>16*1024*1024)return null;
            var buffer=Marshal.AllocHGlobal((int)bytes);
            try
            {
                if(PdhGetFormattedCounterArray(_gpu,0x200,ref bytes,ref count,buffer)!=0)return null;
                var engines=new Dictionary<string,double>();int stride=Marshal.SizeOf<CounterItem>();
                if((long)count*stride>bytes)return null;
                for(int index=0;index<count;index++)
                {
                    var item=Marshal.PtrToStructure<CounterItem>(buffer+index*stride);
                    if(item.Value.Status>1||!double.IsFinite(item.Value.Double))continue;
                    var name=Marshal.PtrToStringUni(item.Name)??"";int start=name.IndexOf("luid_",StringComparison.Ordinal);
                    if(start<0)continue;var key=name[start..];engines.TryGetValue(key,out double value);engines[key]=value+Math.Max(0,item.Value.Double);
                }
                return engines.Count>0?Math.Clamp(engines.Values.Max(),0,100):null;
            }
            finally{Marshal.FreeHGlobal(buffer);}
        }
        catch{return null;}
    }
    public void Dispose(){if(_query!=IntPtr.Zero){PdhCloseQuery(_query);_query=IntPtr.Zero;}}
    [StructLayout(LayoutKind.Sequential)] private struct FileTime {public uint Low,High;public readonly ulong Value=>((ulong)High<<32)|Low;}
    [StructLayout(LayoutKind.Sequential)] private struct Memory {public uint Length,Load;public ulong TotalPhysical,AvailablePhysical,TotalPage,AvailablePage,TotalVirtual,AvailableVirtual,Extended;}
    [StructLayout(LayoutKind.Sequential)] private struct CounterValue {public uint Status;public double Double;}
    [StructLayout(LayoutKind.Sequential)] private struct CounterItem {public IntPtr Name;public CounterValue Value;}
    [DllImport("kernel32.dll")]private static extern bool GetSystemTimes(out FileTime idle,out FileTime kernel,out FileTime user);
    [DllImport("kernel32.dll")]private static extern bool GlobalMemoryStatusEx(ref Memory memory);
    [DllImport("pdh.dll",CharSet=CharSet.Unicode)]private static extern uint PdhOpenQuery(string? source,IntPtr user,out IntPtr query);
    [DllImport("pdh.dll",CharSet=CharSet.Unicode)]private static extern uint PdhAddEnglishCounter(IntPtr query,string path,IntPtr user,out IntPtr counter);
    [DllImport("pdh.dll")]private static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll")]private static extern uint PdhGetFormattedCounterValue(IntPtr counter,uint format,out uint type,out CounterValue value);
    [DllImport("pdh.dll",CharSet=CharSet.Unicode,EntryPoint="PdhGetFormattedCounterArrayW")]private static extern uint PdhGetFormattedCounterArray(IntPtr counter,uint format,ref uint bytes,ref uint count,IntPtr buffer);
    [DllImport("pdh.dll")]private static extern uint PdhCloseQuery(IntPtr query);
}
