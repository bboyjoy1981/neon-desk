using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
namespace NeonDesk;

public static class Safety
{
    private static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase)
    { ".db", ".sqlite", ".sqlite3", ".jsonl", ".zst", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".pdf", ".mp4", ".mov", ".wav", ".mp3", ".pem", ".key", ".pfx" };
    public static bool SafePath(string path, string root)
    {
        try
        {
            var full=Path.GetFullPath(path); var allowed=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
            if(!full.Equals(allowed,StringComparison.OrdinalIgnoreCase) && !full.StartsWith(allowed+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) return false;
            string? part=full;
            while(part is not null)
            {
                if((File.GetAttributes(part)&FileAttributes.ReparsePoint)!=0) return false;
                part=Path.GetDirectoryName(part);
            }
            return true;
        }
        catch { return false; }
    }
    public static bool IsProtected(string path) => Protected.Contains(Path.GetExtension(path)) || path.EndsWith("-wal",StringComparison.OrdinalIgnoreCase) || path.EndsWith("-shm",StringComparison.OrdinalIgnoreCase);
    public static bool DeleteCacheFile(ScannedFile file, CacheRule rule)
    {
        if(!Rules.Eligible(file,rule)||!SafePath(file.Path,rule.Root)) return false;
        // Open the exact leaf without following reparse points; omit share-delete so it
        // cannot be renamed/replaced while its final path and metadata are checked.
        using var handle=CreateFile(file.Path,0x10000|0x80,3,IntPtr.Zero,3,0x00200000,IntPtr.Zero);
        if(handle.IsInvalid || !GetFileInformationByHandle(handle,out var info)) return false;
        if((info.Attributes & (uint)(FileAttributes.ReparsePoint|FileAttributes.Directory))!=0) return false;
        long size=((long)info.SizeHigh<<32)|info.SizeLow;
        long modified=((long)info.Write.High<<32)|info.Write.Low;
        if(size!=file.Length||modified!=file.ModifiedUtc) return false;
        var name=new StringBuilder(32768);
        uint count=GetFinalPathNameByHandle(handle,name,(uint)name.Capacity,0);
        if(count==0||count>=name.Capacity) return false;
        var final=name.ToString(); if(final.StartsWith(@"\\?\")) final=final[4..];
        if(!final.Equals(Path.GetFullPath(file.Path),StringComparison.OrdinalIgnoreCase)||!SafePath(final,rule.Root)) return false;
        var disposition=new Disposition{Delete=1};
        return SetFileInformationByHandle(handle,4,ref disposition,1);
    }
    [StructLayout(LayoutKind.Sequential)] private struct Time { public uint Low,High; }
    [StructLayout(LayoutKind.Sequential)] private struct Info {public uint Attributes;public Time Creation,Access,Write;public uint Volume,SizeHigh,SizeLow,Links,IndexHigh,IndexLow;}
    [StructLayout(LayoutKind.Sequential,Pack=1)] private struct Disposition {public byte Delete;}
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern SafeFileHandle CreateFile(string name,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool GetFileInformationByHandle(SafeFileHandle handle,out Info info);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern uint GetFinalPathNameByHandle(SafeFileHandle handle,StringBuilder path,uint capacity,uint flags);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool SetFileInformationByHandle(SafeFileHandle handle,int kind,ref Disposition info,uint size);
}
