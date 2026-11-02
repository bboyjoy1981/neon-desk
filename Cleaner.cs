namespace NeonDesk;
public static class Cleaner
{
    public static CleanupSummary Clean(CacheRow[] selected,IProgress<string> progress,CancellationToken token)
    {
        int deleted=0,skipped=0;long bytes=0;
        // Rebuild the trusted catalogue, rather than trusting a deserialized/UI supplied rule.
        var allowed=Rules.GetCaches().ToDictionary(r=>r.Root,StringComparer.OrdinalIgnoreCase);
        foreach(var row in selected)
        {
            if(token.IsCancellationRequested) break;
            if(!allowed.TryGetValue(row.Path,out var rule)||Scanner.IsRunning(rule.Processes)) {skipped+=row.Files.Count;progress.Report(row.App+" 正在运行或路径已变化，已跳过。");continue;}
            progress.Report("正在清理 "+row.App+" / "+row.Name);
            foreach(var file in row.Files)
            {
                if(token.IsCancellationRequested) return new(DateTimeOffset.Now,deleted,skipped,bytes,selected.Select(r=>r.App).Distinct().ToArray(),true);
                try{if(Safety.DeleteCacheFile(file,rule)){deleted++;bytes+=file.Length;}else skipped++;}
                catch{skipped++;}
            }
        }
        return new(DateTimeOffset.Now,deleted,skipped,bytes,selected.Select(r=>r.App).Distinct().ToArray(),token.IsCancellationRequested);
    }
}
