using System.Diagnostics;
using System.IO;
namespace NeonDesk;

public static class Scanner
{
    public static bool IsRunning(string[] names)
    {
        foreach(var process in Process.GetProcesses())
        {
            using(process) { try { if(names.Contains(process.ProcessName,StringComparer.OrdinalIgnoreCase)) return true; } catch { } }
        }
        return false;
    }
    private static (long Bytes,int Errors,List<ScannedFile> Files) Walk(string root,bool catalog,CancellationToken token,CacheRule? rule=null,IProgress<string>? progress=null)
    {
        long bytes=0;int errors=0,count=0;long reported=Environment.TickCount64;var files=new List<ScannedFile>();var pending=new Stack<string>();
        try { if((File.GetAttributes(root)&FileAttributes.Directory)==0)return (0,1,files); }
        catch(FileNotFoundException){return (0,0,files);}
        catch(DirectoryNotFoundException){return (0,0,files);}
        catch(IOException){return (0,1,files);}
        catch(UnauthorizedAccessException){return (0,1,files);}
        if(!Safety.SafePath(root,root)) return (0,1,files);
        pending.Push(root);
        while(pending.TryPop(out var directory))
        {
            token.ThrowIfCancellationRequested();
            if(!Safety.SafePath(directory,root)){errors++;continue;}
            try
            {
                foreach(var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos())
                {
                    token.ThrowIfCancellationRequested();
                    if(++count>1_000_000) {errors++;return (bytes,errors,files);}
                    if(count%2048==0&&Environment.TickCount64-reported>350)
                    {
                        progress?.Report($"扫描 {root} · 已处理 {count:N0} 项");reported=Environment.TickCount64;
                    }
                    try
                    {
                        if((entry.Attributes&FileAttributes.ReparsePoint)!=0){errors++;continue;}
                        if((entry.Attributes&FileAttributes.Directory)!=0) pending.Push(entry.FullName);
                        else
                        {
                            var file=(FileInfo)entry;bytes+=file.Length;
                            var metadata=catalog?new ScannedFile(file.FullName,file.Length,file.LastWriteTimeUtc.ToFileTimeUtc()):null;
                            if(catalog&&rule is not null&&metadata is not null&&Rules.Eligible(metadata,rule))
                            {
                                if(files.Count>=100_000){errors++;continue;}
                                files.Add(metadata);
                            }
                        }
                    }
                    catch(IOException){errors++;}catch(UnauthorizedAccessException){errors++;}
                }
            }
            catch(IOException){errors++;}catch(UnauthorizedAccessException){errors++;}
        }
        return (bytes,errors,files);
    }
    public static ScanReport Scan(IProgress<string> progress,CancellationToken token)
    {
        var roots=new List<RootUsage>();var caches=new List<CacheRow>();var apps=new List<AppUsage>();var volumeRoots=new List<RootUsage>();
        var drives=DriveCatalog.ReadyLocal();
        var rules=Rules.GetCaches();
        foreach(var app in Rules.GetApps())
        {
            progress.Report("正在分析 "+app.Name+" …");long bytes=0;int errors=0;
            foreach(var root in app.Roots)
            {
                var scan=Walk(root,false,token,progress:progress);bytes+=scan.Bytes;errors+=scan.Errors;
                if(scan.Errors>0||Directory.Exists(root)) roots.Add(new(app.Name,root,scan.Bytes,scan.Errors));
            }
            bool running=IsRunning(app.Processes);
            foreach(var rule in rules.Where(r=>r.App==app.Name))
            {
                var scan=Walk(rule.Root,true,token,rule,progress);
                caches.Add(new(){Rule=rule,Files=scan.Files,Errors=scan.Errors,Running=running});
            }
            apps.Add(new(app.Name,app.Description,bytes,caches.Where(c=>c.App==app.Name).Sum(c=>c.Bytes),errors));
        }
        foreach(var drive in drives)
        {
            token.ThrowIfCancellationRequested();string root=drive.RootDirectory.FullName;
            progress.Report("全盘分析 "+root+" …");long rootFiles=0;int rootErrors=0;
            if(!Safety.SafePath(root,root)){volumeRoots.Add(new("磁盘 "+root,root,0,1));continue;}
            try
            {
                foreach(var entry in new DirectoryInfo(root).EnumerateFileSystemInfos())
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        if((entry.Attributes&FileAttributes.ReparsePoint)!=0){rootErrors++;continue;}
                        if((entry.Attributes&FileAttributes.Directory)!=0)
                        {
                            var scan=Walk(entry.FullName,false,token,progress:progress);
                            volumeRoots.Add(new("磁盘 "+root.TrimEnd('\\'),entry.FullName,scan.Bytes,scan.Errors));
                        }
                        else rootFiles+=((FileInfo)entry).Length;
                    }
                    catch(IOException){rootErrors++;}catch(UnauthorizedAccessException){rootErrors++;}
                }
            }
            catch(IOException){rootErrors++;}catch(UnauthorizedAccessException){rootErrors++;}
            volumeRoots.Add(new("磁盘 "+root.TrimEnd('\\')+" / 根文件",root,rootFiles,rootErrors));
        }
        return new(DateTimeOffset.Now,apps,roots,caches,volumeRoots,drives.Select(d=>d.Name).ToArray());
    }
}
