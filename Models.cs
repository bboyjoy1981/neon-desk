using System.ComponentModel;
using System.Runtime.CompilerServices;
namespace NeonDesk;

public static class Format
{
    public static string Bytes(double n) => Math.Abs(n) >= 1073741824 ? $"{n / 1073741824:0.00} GB" : Math.Abs(n) >= 1048576 ? $"{n / 1048576:0.0} MB" : $"{n / 1024:0.0} KB";
}
public sealed record AppGroup(string Name, string Description, string[] Roots, string[] Processes);
public sealed record CacheRule(string App, string Name, string Root, string[] Processes, int MinimumAgeDays = 0, string[]? Extensions = null);
public sealed record ScannedFile(string Path, long Length, long ModifiedUtc);
public sealed record CacheCategory(string Name, List<CacheRow> Rows)
{
    public override string ToString() => $"{Name}\n{Format.Bytes(Rows.Sum(r=>r.Bytes))} · {Rows.Count} 项";
}
public sealed class CacheRow : INotifyPropertyChanged
{
    public required CacheRule Rule { get; init; }
    public required List<ScannedFile> Files { get; init; }
    public int Errors { get; init; }
    public bool Running { get; init; }
    public string App => Rule.App;
    public string Name => Rule.Name;
    public string Drive => System.IO.Path.GetPathRoot(Rule.Root)?.TrimEnd('\\') ?? "";
    public string Path => Rule.Root;
    public long Bytes => Files.Sum(f => f.Length);
    public string Size => Format.Bytes(Bytes);
    public string FileCount => Files.Count.ToString("N0");
    public bool Selectable => Files.Count > 0 && !Running;
    public string Status => Running ? "请先退出该软件" : Errors > 0 ? $"部分不可读（{Errors}）" : Rule.MinimumAgeDays > 0 ? $"仅 {Rule.MinimumAgeDays} 天前文件" : "缓存 · 可选择";
    private bool _selected;
    public bool Selected { get => _selected; set { _selected = value && Selectable; PropertyChanged?.Invoke(this, new(nameof(Selected))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
}
public sealed record RootUsage(string App, string Path, long Bytes, int Errors)
{
    public string Size => Format.Bytes(Bytes);
    public string Status => Errors == 0 ? "扫描完成" : $"部分不可读：{Errors}";
    public string Change { get; init; } = "首次扫描";
}
public sealed record AppUsage(string Name, string Description, long Bytes, long CacheBytes, int Errors)
{
    public string Size => Format.Bytes(Bytes);
    public string CacheSize => Format.Bytes(CacheBytes);
    public string ScanStatus => Errors == 0 ? "完整" : $"{Errors} 项不可读";
}
public sealed record ScanReport(DateTimeOffset Time, List<AppUsage> Apps, List<RootUsage> Roots, List<CacheRow> Caches, List<RootUsage> VolumeRoots, string[] Drives);
public sealed record DriveMetric(string Name,long Used,long Total)
{
    public string FreeText => $"可用 {Format.Bytes(Total-Used)}";
    public string TotalText => $"总容量 {Format.Bytes(Total)}";
    public double UsedPercent => Total>0?Used*100d/Total:0;
}
public sealed record GrowthRow(string Name, long Bytes, long? Previous, bool Complete = true)
{
    public string Size => Format.Bytes(Bytes);
    public string Change => !Complete ? "扫描不完整" : Previous is null ? "首次扫描" : (Bytes - Previous.Value > 0 ? "+" : "") + Format.Bytes(Bytes - Previous.Value);
    public string Direction => !Complete ? "不进行增长比较" : Previous is null ? "已建立基线" : Bytes > Previous ? "占用增长" : Bytes < Previous ? "占用减少" : "无变化";
}
public sealed record ProcessRow(int Id, string Name, long Memory) { public string Size => Format.Bytes(Memory); }
public sealed record CleanupSummary(DateTimeOffset Time, int Deleted, int Skipped, long Bytes, string[] Apps, bool Cancelled = false);
