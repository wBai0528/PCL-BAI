using System.IO;
using System.Text.Json;
using PCLBAI.Models;

namespace PCLBAI.Services;

/// <summary>
/// 版本 Json 解析与文件清单构建。
/// 规则判断、库路径拼接、Assets 布局均对齐 PCL2 源码
/// （ModMinecraft.vb：McJsonRuleCheck / McLibGet / McAssetsListGet / McLibListGetWithJson）。
/// </summary>
public static class VersionParser
{
    /// <summary>读取版本清单（version_manifest_v2）。</summary>
    public static List<VersionEntry> ParseManifest(JsonElement root)
    {
        var list = new List<VersionEntry>();
        if (!root.TryGetProperty("versions", out var versions) || versions.ValueKind != JsonValueKind.Array)
            return list;

        foreach (var v in versions.EnumerateArray())
        {
            var id = GetString(v, "id");
            if (string.IsNullOrEmpty(id)) continue;
            DateTime.TryParse(GetString(v, "releaseTime"), out var time);
            list.Add(new VersionEntry
            {
                Id = id,
                Type = GetString(v, "type"),
                Url = GetString(v, "url"),
                Sha1 = GetString(v, "sha1"),
                ReleaseTime = time
            });
        }
        return list;
    }

    public static string GetString(JsonElement e, string name)
        => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    public static long GetLong(JsonElement e, string name, long def = -1)
    {
        if (!e.TryGetProperty(name, out var p)) return def;
        return p.ValueKind switch
        {
            JsonValueKind.Number when p.TryGetInt64(out var v) => v,
            JsonValueKind.String when long.TryParse(p.GetString(), out var v) => v,
            _ => def
        };
    }

    /// <summary>
    /// 规则判断，移植自 PCL2 的 McJsonRuleCheck：
    /// 无 rules 默认需要；windows 视为匹配；x86 / arm 架构视为不匹配；
    /// 带 features（demo、quickPlay）的项一律视为不匹配。
    /// </summary>
    public static bool RuleCheck(JsonElement element)
    {
        if (!element.TryGetProperty("rules", out var rules) || rules.ValueKind != JsonValueKind.Array)
            return true;

        var required = false;
        foreach (var rule in rules.EnumerateArray())
        {
            var isRight = true;

            if (rule.TryGetProperty("os", out var os) && os.ValueKind == JsonValueKind.Object)
            {
                var osName = GetString(os, "name");
                if (!string.IsNullOrEmpty(osName))
                {
                    if (osName == "unknown")
                    {
                        // 不限制
                    }
                    else if (osName == "windows")
                    {
                        // 我们只支持 Windows，版本条件不做严格匹配
                    }
                    else
                    {
                        isRight = false;
                    }
                }

                var arch = GetString(os, "arch");
                if (!string.IsNullOrEmpty(arch))
                {
                    isRight = isRight && arch != "x86" && !arch.Contains("arm");
                }
            }

            if (rule.TryGetProperty("features", out var features) && features.ValueKind == JsonValueKind.Object)
            {
                // demo 用户、quickPlay 等特性都不启用
                isRight = false;
            }

            var action = GetString(rule, "action");
            if (action == "allow")
            {
                if (isRight) required = true;
            }
            else
            {
                if (isRight) required = false;
            }
        }
        return required;
    }

    /// <summary>移植自 PCL2 的 McLibGet：net.minecraftforge:forge:1.21.4-54.0.34:client → 对应本地路径。</summary>
    public static string LibPath(string librariesRoot, string name)
    {
        var s = name.Split(':');
        if (s.Length < 3) return Path.Combine(librariesRoot, name.Replace(':', '_') + ".jar");
        var dir = Path.Combine(librariesRoot, s[0].Replace('.', Path.DirectorySeparatorChar), s[1], s[2]);
        return Path.Combine(dir, string.Join("-", s.Skip(1)) + ".jar");
    }

    /// <summary>
    /// 构建指定版本的全部文件清单。
    /// </summary>
    /// <param name="gameFolder">游戏目录（.minecraft）</param>
    /// <param name="versionName">版本名</param>
    /// <param name="versionJsonBytes">版本 Json 原始字节</param>
    /// <param name="librariesRoot">libraries 根目录，默认 gameFolder\libraries</param>
    /// <param name="skipAssets">是否跳过资源文件</param>
    /// <param name="assetsIndexBytes">可选：已下载的 assetIndex 内容</param>
    public static VersionFileList Build(
        string gameFolder,
        string versionName,
        byte[] versionJsonBytes,
        string librariesRoot = null,
        bool skipAssets = false,
        byte[] assetsIndexBytes = null)
    {
        librariesRoot ??= Path.Combine(gameFolder, "libraries");
        var result = new VersionFileList { VersionName = versionName };

        using var doc = JsonDocument.Parse(versionJsonBytes);
        var root = doc.RootElement;

        // ---------- 1. 客户端 jar ----------
        string clientUrl = null, clientSha1 = null;
        var clientSize = -1L;
        if (root.TryGetProperty("downloads", out var downloads) &&
            downloads.ValueKind == JsonValueKind.Object &&
            downloads.TryGetProperty("client", out var client) &&
            client.ValueKind == JsonValueKind.Object)
        {
            clientUrl = GetString(client, "url");
            clientSha1 = GetString(client, "sha1");
            clientSize = GetLong(client, "size");
        }

        if (!string.IsNullOrEmpty(clientUrl))
        {
            var jarPath = Path.Combine(gameFolder, "versions", versionName, versionName + ".jar");
            result.Client = new FileEntry
            {
                Url = clientUrl,
                LocalPath = jarPath,
                Sha1 = clientSha1?.ToLowerInvariant(),
                Size = clientSize,
                Group = "Client",
                DisplayName = versionName + ".jar"
            };
        }

        // ---------- 2. 支持库 ----------
        if (root.TryGetProperty("libraries", out var libs) && libs.ValueKind == JsonValueKind.Array)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var lib in libs.EnumerateArray())
            {
                if (lib.ValueKind != JsonValueKind.Object) continue;
                if (!RuleCheck(lib)) continue;

                var name = GetString(lib, "name");
                var rootUrl = GetString(lib, "url");

                JsonElement? artifact = null;
                JsonElement? nativeData = null;
                string nativeClassifier = null;

                if (lib.TryGetProperty("downloads", out var ld) && ld.ValueKind == JsonValueKind.Object)
                {
                    if (ld.TryGetProperty("artifact", out var art) && art.ValueKind == JsonValueKind.Object)
                    {
                        artifact = art;
                    }
                    if (lib.TryGetProperty("natives", out var natives) && natives.ValueKind == JsonValueKind.Object)
                    {
                        nativeClassifier = GetString(natives, "windows");
                        if (!string.IsNullOrEmpty(nativeClassifier) &&
                            ld.TryGetProperty("classifiers", out var classifiers) &&
                            classifiers.ValueKind == JsonValueKind.Object &&
                            classifiers.TryGetProperty(nativeClassifier, out var nd) &&
                            nd.ValueKind == JsonValueKind.Object)
                        {
                            nativeData = nd;
                        }
                    }
                }
                else if (lib.TryGetProperty("natives", out var natives2) && natives2.ValueKind == JsonValueKind.Object)
                {
                    nativeClassifier = GetString(natives2, "windows");
                }

                string url, path, sha1;
                long size = -1;

                if (nativeData.HasValue)
                {
                    var nd = nativeData.Value;
                    url = GetString(nd, "url") ?? rootUrl;
                    var p = GetString(nd, "path");
                    path = p != null
                        ? Path.Combine(librariesRoot, p.Replace('/', Path.DirectorySeparatorChar))
                        : LibPath(librariesRoot, name).Replace(".jar", "-" + nativeClassifier + ".jar")
                            .Replace("${arch}", Environment.Is64BitOperatingSystem ? "64" : "32");
                    sha1 = GetString(nd, "sha1");
                    size = GetLong(nd, "size");
                }
                else if (nativeClassifier != null && artifact == null)
                {
                    path = LibPath(librariesRoot, name).Replace(".jar", "-" + nativeClassifier + ".jar")
                        .Replace("${arch}", Environment.Is64BitOperatingSystem ? "64" : "32");
                    url = rootUrl == null ? null : rootUrl + (name ?? "").Replace(':', '/') + "-" + nativeClassifier + ".jar";
                    sha1 = null;
                }
                else if (artifact.HasValue)
                {
                    var art = artifact.Value;
                    url = GetString(art, "url");
                    var p = GetString(art, "path");
                    path = p != null
                        ? Path.Combine(librariesRoot, p.Replace('/', Path.DirectorySeparatorChar))
                        : LibPath(librariesRoot, name);
                    sha1 = GetString(art, "sha1");
                    size = GetLong(art, "size");
                }
                else if (!string.IsNullOrEmpty(name))
                {
                    // 只有 name，没有 downloads：自己拼 maven 路径
                    path = LibPath(librariesRoot, name);
                    var sp = name.Split(':');
                    var fileName = string.Join("-", sp.Skip(1)) + ".jar";
                    var rel = string.Join("/", sp[0].Split('.')) + "/" + sp[1] + "/" + sp[2] + "/" + fileName;
                    url = rootUrl == null ? null : rootUrl.TrimEnd('/') + "/" + rel;
                    sha1 = null;
                }
                else
                {
                    continue;
                }

                if (url == null) continue;
                if (!seen.Add(path)) continue;

                result.Files.Add(new FileEntry
                {
                    Url = url,
                    LocalPath = path,
                    Sha1 = sha1?.ToLowerInvariant(),
                    Size = size,
                    Group = "Libraries",
                    DisplayName = Path.GetFileName(path)
                });
            }
        }

        // ---------- 3. 资源文件索引 ----------
        if (!skipAssets)
        {
            string indexId = null, indexUrl = null;
            long indexSize = -1;
            if (root.TryGetProperty("assetIndex", out var ai) && ai.ValueKind == JsonValueKind.Object)
            {
                indexId = GetString(ai, "id");
                indexUrl = GetString(ai, "url");
                indexSize = GetLong(ai, "size");
            }
            else if (root.TryGetProperty("assets", out var assetsName) && assetsName.ValueKind == JsonValueKind.String)
            {
                indexId = assetsName.GetString();
            }

            if (!string.IsNullOrEmpty(indexId))
            {
                var indexPath = Path.Combine(gameFolder, "assets", "indexes", indexId + ".json");
                if (indexUrl != null)
                {
                    result.AssetIndex = new FileEntry
                    {
                        Url = indexUrl,
                        LocalPath = indexPath,
                        Sha1 = null,
                        Size = indexSize,
                        Group = "Index",
                        DisplayName = indexId + ".json"
                    };
                }

                // 如果调用方已提供索引内容，就直接解析
                if (assetsIndexBytes is not { Length: > 0 }) assetsIndexBytes = null;
                if (assetsIndexBytes == null && File.Exists(indexPath))
                {
                    try
                    {
                        assetsIndexBytes = File.ReadAllBytes(indexPath);
                    }
                    catch
                    {
                        // 忽略
                    }
                }

                // 注意：只有能解析出 objects 的才算有效索引内容
                if (assetsIndexBytes != null)
                {
                    try
                    {
                        var parsed = ParseAssets(gameFolder, assetsIndexBytes, indexId);
                        if (parsed.Count > 0) result.Files.AddRange(parsed);
                    }
                    catch
                    {
                        // 索引内容无效则跳过，不影响其他文件的清单
                    }
                }
            }
        }

        return result;
    }

    /// <summary>解析资源索引，生成 assets/objects/xx/hash 的下载列表。</summary>
    public static List<FileEntry> ParseAssets(string gameFolder, byte[] indexBytes, string indexId)
    {
        var list = new List<FileEntry>();
        using var doc = JsonDocument.Parse(indexBytes);
        var root = doc.RootElement;

        var mapToResources = root.TryGetProperty("map_to_resources", out var mtr) && mtr.ValueKind == JsonValueKind.True;
        var isVirtual = root.TryGetProperty("virtual", out var v) && v.ValueKind == JsonValueKind.True;

        if (!root.TryGetProperty("objects", out var objects) || objects.ValueKind != JsonValueKind.Object)
            return list;

        foreach (var obj in objects.EnumerateObject())
        {
            var hash = GetString(obj.Value, "hash");
            if (string.IsNullOrEmpty(hash)) continue;
            var size = GetLong(obj.Value, "size", 0);

            string localPath;
            if (mapToResources)
            {
                localPath = Path.Combine(gameFolder, "resources", obj.Name.Replace('/', Path.DirectorySeparatorChar));
            }
            else if (isVirtual)
            {
                localPath = Path.Combine(gameFolder, "assets", "virtual", "legacy", obj.Name.Replace('/', Path.DirectorySeparatorChar));
            }
            else
            {
                localPath = Path.Combine(gameFolder, "assets", "objects", hash[..2], hash);
            }

            list.Add(new FileEntry
            {
                Url = $"https://resources.download.minecraft.net/{hash[..2]}/{hash}",
                LocalPath = localPath,
                Sha1 = hash.ToLowerInvariant(),
                Size = size,
                Group = "Assets",
                DisplayName = obj.Name
            });
        }
        return list;
    }
}

/// <summary>一个版本的完整文件清单。</summary>
public sealed class VersionFileList
{
    public string VersionName { get; init; }
    public FileEntry Client { get; set; }
    public FileEntry AssetIndex { get; set; }
    public List<FileEntry> Files { get; } = new();

    public IEnumerable<FileEntry> AllFiles
    {
        get
        {
            if (AssetIndex != null) yield return AssetIndex;
            foreach (var f in Files) yield return f;
        }
    }

    public int Count => Files.Count + (AssetIndex != null ? 1 : 0);
}
