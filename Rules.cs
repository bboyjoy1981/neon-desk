using System.IO;
namespace NeonDesk;

public static class Rules
{
    private static readonly string Local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static readonly string Roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    private static readonly string User = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public static readonly AppGroup[] Apps =
    [
        new("Codex", "聊天与运行时受保护", [Path.Combine(User,".codex"),Path.Combine(User,".cache","codex-runtimes"),Path.Combine(Roaming,"Codex")], ["Codex","codex"]),
        new("飞书", "保留聊天、附件与登录数据", [Path.Combine(Roaming,"LarkShell"),Path.Combine(Local,"Feishu")], ["Feishu","Lark","LarkShell"]),
        new("剪映", "保留草稿、素材和工程", [Path.Combine(Local,"JianyingPro")], ["JianyingPro","CapCut"]),
        new("豆包", "保留对话与登录状态", [Path.Combine(Local,"Doubao")], ["Doubao"]),
        new("WPS", "含其他金山组件；保护云文档", [Path.Combine(Roaming,"kingsoft"),Path.Combine(Local,"kingsoft")], ["wps","wpp","et","wpspdf","wpscloudsvr"])
    ];
    public static AppGroup[] GetApps()
    {
        var profiles=new HashSet<string>(StringComparer.OrdinalIgnoreCase){User};
        foreach(var drive in DriveCatalog.ReadyLocal())
        {
            profiles.Add(Path.Combine(drive.RootDirectory.FullName,"Users",Environment.UserName));
            profiles.Add(Path.Combine(drive.RootDirectory.FullName,"360MoveData","Users",Environment.UserName));
        }
        var groups=Apps.Select(app=>app with {Roots=app.Roots.ToArray()}).ToList();
        foreach(var profile in profiles.Where(p=>!p.Equals(User,StringComparison.OrdinalIgnoreCase)))
        {
            var local=Path.Combine(profile,"AppData","Local");var roaming=Path.Combine(profile,"AppData","Roaming");
            AddRoots("Codex",Path.Combine(profile,".codex"),Path.Combine(profile,".cache","codex-runtimes"),Path.Combine(roaming,"Codex"));
            AddRoots("飞书",Path.Combine(roaming,"LarkShell"),Path.Combine(local,"Feishu"));
            AddRoots("剪映",Path.Combine(local,"JianyingPro"));AddRoots("豆包",Path.Combine(local,"Doubao"));
            AddRoots("WPS",Path.Combine(roaming,"kingsoft"),Path.Combine(local,"kingsoft"));
        }
        var tempRoots=new HashSet<string>(StringComparer.OrdinalIgnoreCase){Path.Combine(Local,"Temp"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"Temp")};
        foreach(var drive in DriveCatalog.ReadyLocal())foreach(var name in new[]{"Temp","TMP"})tempRoots.Add(Path.Combine(drive.RootDirectory.FullName,name));
        groups.Add(new("全盘临时文件","仅展示占用；未知临时内容不自动删除",tempRoots.ToArray(),[]));
        var localRoots=profiles.Select(p=>Path.Combine(p,"AppData","Local")).Append(Local).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        groups.Add(new("下载缓存","npm / pip 可重新下载的缓存",localRoots.SelectMany(p=>new[]{Path.Combine(p,"npm-cache","_cacache"),Path.Combine(p,"pip","cache")}).ToArray(),[]));
        groups.Add(new("图形缓存","已识别着色器缓存；保留最近七天",localRoots.SelectMany(p=>new[]{Path.Combine(p,"D3DSCache"),Path.Combine(p,"NVIDIA","DXCache"),Path.Combine(p,"NVIDIA","GLCache")}).ToArray(),[]));
        return groups.ToArray();
        void AddRoots(string app,params string[] roots)
        {
            int index=groups.FindIndex(g=>g.Name==app);
            groups[index]=groups[index] with {Roots=groups[index].Roots.Concat(roots).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()};
        }
    }
    public static bool Eligible(ScannedFile file,CacheRule rule)
    {
        if(Safety.IsProtected(file.Path))return false;
        if(rule.Extensions is not null&&!rule.Extensions.Contains(Path.GetExtension(file.Path),StringComparer.OrdinalIgnoreCase))return false;
        try {return rule.MinimumAgeDays==0||DateTime.FromFileTimeUtc(file.ModifiedUtc)<DateTime.UtcNow.AddDays(-rule.MinimumAgeDays);}
        catch(ArgumentOutOfRangeException){return false;}
    }
    public static List<CacheRule> GetCaches()
    {
        var result = new List<CacheRule>();
        void Add(string app, string name, string root) { if (Directory.Exists(root)) result.Add(new(app,name,Path.GetFullPath(root),Apps.Single(a=>a.Name==app).Processes)); }
        void Chromium(string app,string root)
        {
            foreach(var cache in new[]{"Cache","Code Cache","GPUCache","DawnGraphiteCache","DawnWebGPUCache"}) Add(app,cache,Path.Combine(root,cache));
        }
        Chromium("Codex",Path.Combine(Roaming,"Codex","web","Codex","Default"));
        var lark=Path.Combine(Roaming,"LarkShell");
        foreach(var name in new[]{"CodeCache","ShaderCache","GrShaderCache","GraphiteDawnCache"}) Add("飞书",name,Path.Combine(lark,name));
        Chromium("飞书",Path.Combine(lark,"Default"));
        Chromium("飞书",Path.Combine(Local,"Feishu","User Data","Default"));
        Chromium("剪映",Path.Combine(Local,"JianyingPro","User Data","Default"));
        Chromium("豆包",Path.Combine(Local,"Doubao","User Data","Default"));
        // WPS shares its parent with antivirus and offline documents: never delete that parent.
        foreach(var root in new[]{Path.Combine(Roaming,"kingsoft","wps","addons","webview","Default"),Path.Combine(Local,"kingsoft","WPS Office","User Data","Default")}) Chromium("WPS",root);
        // Additional local volumes may hold relocated app profiles. Only exact known
        // current-user layouts are recognized; discovery never turns arbitrary caches into rules.
        foreach(var app in GetApps().Where(a=>a.Name is "Codex" or "飞书" or "剪映" or "豆包" or "WPS"))
        foreach(var root in app.Roots)
        {
            string leaf=Path.GetFileName(root);
            if(app.Name=="Codex"&&leaf.Equals("Codex",StringComparison.OrdinalIgnoreCase))Chromium(app.Name,Path.Combine(root,"web","Codex","Default"));
            if(app.Name=="飞书"&&leaf.Equals("LarkShell",StringComparison.OrdinalIgnoreCase))
            {
                Chromium(app.Name,Path.Combine(root,"Default"));
                foreach(var cache in new[]{"CodeCache","ShaderCache","GrShaderCache","GraphiteDawnCache"})Add(app.Name,cache,Path.Combine(root,cache));
            }
            if(app.Name is "飞书" or "剪映" or "豆包")Chromium(app.Name,Path.Combine(root,"User Data","Default"));
            if(app.Name=="WPS")
            {
                Chromium(app.Name,Path.Combine(root,"WPS Office","User Data","Default"));
                Chromium(app.Name,Path.Combine(root,"wps","addons","webview","Default"));
            }
        }
        foreach(var root in GetApps().Single(a=>a.Name=="全盘临时文件").Roots)
        {
            // Ordinary Temp contents may contain recovery drafts. Only recognize
            // the explicit, GUID-named Codex search-index layout, never generic *.tmp.
            try
            {
                foreach(var index in Directory.EnumerateDirectories(root,"codex-index-*",SearchOption.TopDirectoryOnly).Take(2000))
                {
                    if(Guid.TryParse(Path.GetFileName(index)["codex-index-".Length..],out _))
                        result.Add(new("Codex","过期代码索引",Path.GetFullPath(index),Apps.Single(a=>a.Name=="Codex").Processes,7));
                }
            }
            catch(IOException){ }catch(UnauthorizedAccessException){ }
        }
        foreach(var root in GetApps().Single(a=>a.Name=="下载缓存").Roots)
            if(Directory.Exists(root))result.Add(new("下载缓存",root.Contains("npm-cache")?"npm 下载缓存":"pip 下载缓存",Path.GetFullPath(root),root.Contains("npm-cache")?["node"]:["python","pythonw","pip"],7));
        foreach(var root in GetApps().Single(a=>a.Name=="图形缓存").Roots)
            if(Directory.Exists(root))result.Add(new("图形缓存","着色器缓存",Path.GetFullPath(root),[],7));
        return result.DistinctBy(r=>r.Root,StringComparer.OrdinalIgnoreCase).ToList();
    }
}
