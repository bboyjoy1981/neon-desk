using System.Diagnostics;
using System.IO;
using System.Text.Json;
namespace NeonDesk;
internal sealed class UsageObserver
{
    private readonly string _file;private readonly object _gate=new();private Dictionary<string,DateTimeOffset> _seen=new(StringComparer.OrdinalIgnoreCase);private DateTimeOffset _next;private int _running;
    internal UsageObserver(string folder){_file=Path.Combine(folder,"observed-app-usage.json");try{if(File.Exists(_file)&&new FileInfo(_file).Length<1024*1024)_seen=new(JsonSerializer.Deserialize<Dictionary<string,DateTimeOffset>>(File.ReadAllText(_file))??[],StringComparer.OrdinalIgnoreCase);}catch{}}
    internal void Observe()
    {
        if(DateTimeOffset.UtcNow<_next||Interlocked.Exchange(ref _running,1)!=0)return;_next=DateTimeOffset.UtcNow.AddSeconds(30);
        _=Task.Run(()=>{try{var now=DateTimeOffset.UtcNow;foreach(var p in Process.GetProcesses()){using(p){try{var path=p.MainModule?.FileName;if(path!=null)lock(_gate)_seen[path]=now;}catch{}}}Dictionary<string,DateTimeOffset> snapshot;lock(_gate){_seen=_seen.OrderByDescending(x=>x.Value).Take(3000).ToDictionary(x=>x.Key,x=>x.Value,StringComparer.OrdinalIgnoreCase);snapshot=new(_seen,StringComparer.OrdinalIgnoreCase);}File.WriteAllText(_file+".tmp",JsonSerializer.Serialize(snapshot));File.Move(_file+".tmp",_file,true);}catch{}finally{Interlocked.Exchange(ref _running,0);}});
    }
    internal DateTimeOffset? LastSeen(string root,string icon)
    {
        try{var exe=icon.Split(',')[0].Trim(' ','"');var prefix=string.IsNullOrWhiteSpace(root)?null:Path.GetFullPath(root).TrimEnd('\\')+"\\";lock(_gate){var dates=_seen.Where(x=>x.Key.Equals(exe,StringComparison.OrdinalIgnoreCase)||(prefix!=null&&x.Key.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))).Select(x=>(DateTimeOffset?)x.Value);return dates.Max();}}catch{return null;}
    }
}
