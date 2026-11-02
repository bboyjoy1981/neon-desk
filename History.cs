using System.IO;
using System.Text.Json;
namespace NeonDesk;
public sealed record Snapshot(DateTimeOffset Time,Dictionary<string,long> Apps,Dictionary<string,long>? Roots=null);
public sealed class History
{
    public string Folder {get;}
    public History()
    {
        var portable=Path.Combine(AppContext.BaseDirectory,"data");
        try{Directory.CreateDirectory(portable);Folder=portable;}catch{Folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"NeonDesk");Directory.CreateDirectory(Folder);}
    }
    private List<T> Read<T>(string name) where T : class
    {
        var path=Path.Combine(Folder,name);
        try
        {
            if(!File.Exists(path)||new FileInfo(path).Length>4*1024*1024)return [];
            return (JsonSerializer.Deserialize<List<T>>(File.ReadAllText(path))??[]).OfType<T>().ToList();
        }
        catch{return [];}
    }
    private void Write<T>(string name,List<T> values)
    {
        var path=Path.Combine(Folder,name);var temp=path+".tmp";
        File.WriteAllText(temp,JsonSerializer.Serialize(values));File.Move(temp,path,true);
    }
    public (List<GrowthRow> Rows,DateTimeOffset? Previous,List<RootUsage> Roots) AddScan(ScanReport scan)
    {
        var snapshots=Read<Snapshot>("scans.json").Where(s=>s.Apps is not null).ToList();var previous=snapshots.LastOrDefault();
        var rows=scan.Apps.Select(app=>new GrowthRow(app.Name,app.Bytes,previous?.Apps.TryGetValue(app.Name,out var bytes)==true?bytes:null,app.Errors==0)).ToList();
        var allRoots=scan.Roots.Concat(scan.VolumeRoots).ToList();
        var roots=allRoots.Select(root=>root with {Change=root.Errors>0?"扫描不完整":previous?.Roots?.TryGetValue(root.Path,out var old)==true?(root.Bytes-old>0?"+":"")+Format.Bytes(root.Bytes-old):"首次扫描"}).ToList();
        snapshots.Add(new(scan.Time,scan.Apps.Where(a=>a.Errors==0).ToDictionary(a=>a.Name,a=>a.Bytes),allRoots.Where(r=>r.Errors==0).OrderByDescending(r=>r.Bytes).Take(500).DistinctBy(r=>r.Path,StringComparer.OrdinalIgnoreCase).ToDictionary(r=>r.Path,r=>r.Bytes,StringComparer.OrdinalIgnoreCase)));
        Write("scans.json",snapshots.TakeLast(12).ToList());return (rows,previous?.Time,roots);
    }
    public void AddCleanup(CleanupSummary summary){var rows=Read<CleanupSummary>("cleanup.json");rows.Add(summary);Write("cleanup.json",rows.TakeLast(50).ToList());}
    public List<CleanupSummary> Cleanups()=>Read<CleanupSummary>("cleanup.json").Where(r=>r.Deleted>=0&&r.Skipped>=0&&r.Bytes>=0).TakeLast(50).ToList();
}
