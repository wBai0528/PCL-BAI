using System.IO;
using System.Text.Json;

namespace PCLBAI.Models;

/// <summary>Minecraft 版本清单中的一个条目（version_manifest_v2.json 的 versions 数组项）。</summary>
public sealed class VersionEntry
{
    public string Id { get; set; }
    public string Type { get; set; }
    public string Url { get; set; }
    public string Sha1 { get; set; }
    public DateTime ReleaseTime { get; set; }

    /// <summary>是否为正式版。</summary>
    public bool IsRelease => string.Equals(Type, "release", StringComparison.OrdinalIgnoreCase);

    /// <summary>中文类型名，用于界面展示。</summary>
    public string TypeName => Type switch
    {
        "release" => "正式版",
        "snapshot" => "快照",
        "old_beta" => "远古 Beta",
        "old_alpha" => "远古 Alpha",
        _ => "未知"
    };

    /// <summary>类型文字颜色：正式版绿、快照橙，一眼可分。</summary>
    public string TypeColor => Type switch
    {
        "release" => "#1E7B34",
        "snapshot" => "#B35C00",
        "old_beta" => "#4A3AB8",
        "old_alpha" => "#555555",
        _ => "#555555"
    };

    /// <summary>1.21.4 这类大版本号；快照返回 "快照"。</summary>
    public string MajorTag => VersionUtil.GetMajorTag(Id);

    /// <summary>如 1.21.4。</summary>
    public string Display => Id;

    public override string ToString() => Id;
}

/// <summary>需要下载/校验的一个文件。</summary>
public sealed class FileEntry
{
    public string Url { get; init; }
    /// <summary>本地绝对路径。</summary>
    public string LocalPath { get; init; }
    /// <summary>SHA1（小写 hex），可能为 null 表示该文件不提供校验。</summary>
    public string Sha1 { get; init; }
    /// <summary>期望大小，-1 表示未知。</summary>
    public long Size { get; init; } = -1;
    /// <summary>分组，用于进度展示：Client / Libraries / Assets。</summary>
    public string Group { get; init; } = "Libraries";
    /// <summary>展示名。</summary>
    public string DisplayName { get; init; }

    public string Display => DisplayName ?? Path.GetFileName(LocalPath);
    public override string ToString() => $"{Group}/{Display}";
}

/// <summary>单个文件校验结果。</summary>
public sealed class VerifyItem
{
    public FileEntry Entry { get; init; }
    /// <summary>OK / Missing / SizeMismatch / HashMismatch / Error</summary>
    public string State { get; init; }
    public long ActualSize { get; init; } = -1;
    public string ActualSha1 { get; init; }
    public string Message { get; init; }

    public bool IsOk => State == "OK";

    public string StateName => State switch
    {
        "OK" => "正常",
        "Missing" => "缺失",
        "SizeMismatch" => "大小不符",
        "HashMismatch" => "校验失败",
        "Error" => "读取失败",
        _ => State
    };

    public string Detail => State switch
    {
        "OK" => $"大小 {SizeText(ActualSize)} | SHA1 {ActualSha1}",
        "Missing" => "文件不存在",
        "SizeMismatch" => $"期望 {SizeText(Entry.Size)}，实际 {SizeText(ActualSize)}",
        "HashMismatch" => $"期望 SHA1 {Entry.Sha1}，实际 {ActualSha1}",
        _ => Message ?? ""
    };

    /// <summary>相对游戏根目录的可读路径。</summary>
    public string RelativePath { get; init; }

    public static string SizeText(long bytes)
    {
        if (bytes < 0) return "未知";
        if (bytes < 1024) return bytes + " B";
        if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.0") + " KB";
        if (bytes < 1024L * 1024 * 1024) return (bytes / 1024.0 / 1024).ToString("0.00") + " MB";
        return (bytes / 1024.0 / 1024 / 1024).ToString("0.00") + " GB";
    }
}

/// <summary>整体校验报告。</summary>
public sealed class VerifyReport
{
    public string VersionName { get; init; }
    public DateTime FinishedAt { get; init; } = DateTime.Now;
    public List<VerifyItem> Items { get; } = new();

    public int Total => Items.Count;
    public int OkCount => Items.Count(i => i.IsOk);
    public int BadCount => Items.Count(i => !i.IsOk);

    public IEnumerable<VerifyItem> BadItems => Items.Where(i => !i.IsOk);

    public string Summary => BadCount == 0
        ? $"全部 {Total} 个文件校验通过"
        : $"{Total} 个文件中 {OkCount} 个正常，{BadCount} 个存在问题";
}

/// <summary>工具类。</summary>
public static class VersionUtil
{
    /// <summary>
    /// 从版本号提取大版本标签。
    /// 例：1.21.4 → 1.21.4；1.21.4-pre1 → 1.21.4；1.21 → 1.21；26.3 → 26.3；24w14a → 快照。
    /// 不能用「是否以 1. 开头」判断——新版已经到 26.x，那样会把所有新版本都当成快照。
    /// </summary>
    public static string GetMajorTag(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return "未知";

        // 远古版本
        if (id.StartsWith("a1.", StringComparison.OrdinalIgnoreCase)) return "Alpha";
        if (id.StartsWith("b1.", StringComparison.OrdinalIgnoreCase)) return "Beta";

        // 快照形如 24w14a、1.21-pre1 之外的 xxwxxa
        if (id.Length > 2 && char.IsDigit(id[0]) && id.Contains('w') && !id.Contains('.')) return "快照";

        var s = id;
        // 去掉 -pre / -rc 等后缀
        var dash = s.IndexOf('-');
        if (dash > 0) s = s[..dash];

        var parts = s.Split('.');
        // 纯数字版本号：1.21.4 取三段，1.21 取两段，26.3 取两段
        if (parts.Length >= 3 && long.TryParse(parts[2], out _))
            return $"{parts[0]}.{parts[1]}.{parts[2]}";
        if (parts.Length >= 2 && long.TryParse(parts[1], out _))
            return $"{parts[0]}.{parts[1]}";
        return s;
    }

    /// <summary>把 1.21.4 归到 1.21（大版本家族）；26.3 归到 26.3。</summary>
    public static string GetFamily(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return "未知";
        var tag = GetMajorTag(id);
        if (tag is "Alpha" or "Beta" or "快照") return tag;
        var parts = tag.Split('.');
        return parts.Length >= 2 ? $"{parts[0]}.{parts[1]}" : tag;
    }

    /// <summary>判断是否为 1.x.y 形式的原版版本号。</summary>
    public static bool IsVanillaNumbered(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        if (id.StartsWith("a1.", StringComparison.OrdinalIgnoreCase)) return true;
        if (id.StartsWith("b1.", StringComparison.OrdinalIgnoreCase)) return true;
        return char.IsDigit(id[0]) && id.Contains('.');
    }

    /// <summary>版本号比较（仅用于排序展示，尽力而为）。</summary>
    public static int CompareVersion(string a, string b)
    {
        var pa = (a ?? "").Split('.');
        var pb = (b ?? "").Split('.');
        for (var i = 0; i < Math.Max(pa.Length, pb.Length); i++)
        {
            var sa = i < pa.Length ? pa[i] : "0";
            var sb = i < pb.Length ? pb[i] : "0";
            if (long.TryParse(sa, out var ia) && long.TryParse(sb, out var ib))
            {
                if (ia != ib) return ia.CompareTo(ib);
            }
            else
            {
                var c = string.Compare(sa, sb, StringComparison.OrdinalIgnoreCase);
                if (c != 0) return c;
            }
        }
        return 0;
    }
}
