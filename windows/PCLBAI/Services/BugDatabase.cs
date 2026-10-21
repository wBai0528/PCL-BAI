using PCLBAI.Models;

namespace PCLBAI.Services;

/// <summary>严重程度。</summary>
public enum BugLevel
{
    Common,
    Major,
    Severe
}

/// <summary>一条已知问题。字段含义与手机版 BugItem 完全一致。</summary>
public sealed class BugEntry
{
    public string Title { get; init; }
    public BugLevel Level { get; init; }

    /// <summary>精确大版本，如 1.21.4；为空表示按 family 匹配。</summary>
    public string McVersion { get; init; }

    /// <summary>家族，如 1.21。</summary>
    public string Family { get; init; }

    /// <summary>仅对某类版本生效（正式版/快照/远古）；null 表示不限。</summary>
    public string TypeFilter { get; init; }

    /// <summary>仅对这些加载器生效；空表示通用。</summary>
    public ModLoader[] Loaders { get; init; } = Array.Empty<ModLoader>();

    public string Desc { get; init; }
    public string Cause { get; init; }
    public string Fix { get; init; }

    public string LevelName => Level switch
    {
        BugLevel.Severe => "严重",
        BugLevel.Major => "影响较大",
        _ => "常见"
    };

    public string LevelColor => Level switch
    {
        BugLevel.Severe => "#D9480F",
        BugLevel.Major => "#E67700",
        _ => "#2B8A3E"
    };

    public string LoaderTags => Loaders.Length == 0
        ? "通用"
        : string.Join("/", Loaders.Select(l => l.ShortName()));

    public string DescText => string.IsNullOrWhiteSpace(Desc) ? "（暂无）" : Desc;
    public string CauseText => string.IsNullOrWhiteSpace(Cause) ? "（暂无）" : Cause;
    public string FixText => string.IsNullOrWhiteSpace(Fix) ? "（暂无）" : Fix;
}

/// <summary>
/// 内嵌 Bug 知识库（与手机版 App 共用同一份内容）。
///
/// 相比上一版修好了两点：
/// 1. 正式版与快照分开索引：快照只命中 TypeFilter == "snapshot" 的条目；
/// 2. 覆盖到每个大版本的正式版共性问题与 Forge/NeoForge/Fabric 等加载器坑，
///    不再只剩「快照不向前兼容」一条。
/// </summary>
public static class BugDatabase
{
    private static readonly List<BugEntry> All = new()
    {
        // ============ 1.21.x ============
        new() {
            Title = "需要 Java 21，用 Java 17 直接闪退", Level = BugLevel.Severe,
            McVersion = "1.21", Family = "1.21",
            Desc = "点击启动后窗口一闪而过，日志出现 UnsupportedClassVersionError。",
            Cause = "1.21 起官方把最低运行时提升到 Java 21。",
            Fix = "安装 Java 21（Microsoft OpenJDK 21 / Temurin 21），并在启动器里把 Java 版本切到 21。"
        },
        new() {
            Title = "模组集体失效：注册表与数据组件结构变化", Level = BugLevel.Major,
            McVersion = "1.21.2", Family = "1.21",
            Desc = "1.21.1 时期的模组在 1.21.2+ 加载直接崩溃或静默不生效。",
            Cause = "1.21.2 起物品改用数据组件系统，注册表签名也变了。",
            Fix = "换成对应小版本的模组；模组没更新就停在 1.21.1。",
            Loaders = new[] { ModLoader.Forge, ModLoader.NeoForge, ModLoader.Fabric, ModLoader.Quilt }
        },
        new() {
            Title = "Forge 在 1.21 需要 51.x 以上，旧版本装不上", Level = BugLevel.Major,
            McVersion = "1.21", Family = "1.21",
            Desc = "用 Forge 47.x 安装 1.21 会提示找不到版本或安装失败。",
            Cause = "Forge 为 1.21 单开了 51.x/52.x 分支，1.20.1 的 47.x 不兼容。",
            Fix = "选 Forge 1.21 对应的 51.x 及以上；1.21.4 用 54.x。",
            Loaders = new[] { ModLoader.Forge }
        },
        new() {
            Title = "NeoForge 与 Forge 不能同时装", Level = BugLevel.Severe,
            McVersion = "1.21", Family = "1.21",
            Desc = "同一个版本目录同时装 Forge 和 NeoForge，启动即崩。",
            Cause = "两者都替换主类并抢注同名库，互斥。",
            Fix = "每个游戏版本目录只装一种加载器，分开建版本。",
            Loaders = new[] { ModLoader.NeoForge, ModLoader.Forge }
        },
        new() {
            Title = "新版渲染管线导致部分老显卡闪退或黑屏", Level = BugLevel.Major,
            McVersion = "1.21.6", Family = "1.21",
            Desc = "1.21.6 之后在部分 Intel 核显、老 AMD 驱动上画面全黑或直接退出。",
            Cause = "新渲染管线依赖较新的 OpenGL 能力与驱动实现。",
            Fix = "更新显卡驱动；不行就在视频设置里关掉高级渲染，或降到 1.21.5。"
        },
        new() {
            Title = "服务端与客户端必须同一小版本", Level = BugLevel.Common,
            McVersion = "1.21.8", Family = "1.21",
            Desc = "1.21.8 连 1.21.7 服务器提示「版本不符」。",
            Cause = "Mojang 在小版本里调整了协议号。",
            Fix = "两端小版本保持一致，或用 ViaVersion 类插件做协议兼容。"
        },
        new() {
            Title = "升级后世界数据不可回退（1.21.9）", Level = BugLevel.Severe,
            McVersion = "1.21.9", Family = "1.21",
            Desc = "在 1.21.9 打开过的存档，旧版本客户端再也打不开。",
            Cause = "新增区块方块与实体类型抬高了 Level.dat 版本号。",
            Fix = "升级前先完整备份 saves 目录；要回退只能靠备份。"
        },

        // ============ 1.20.x ============
        new() {
            Title = "需要 Java 17，Java 8 启动即崩", Level = BugLevel.Severe,
            McVersion = "1.20", Family = "1.20",
            Desc = "用 Java 8 启动 1.20 会在日志里报 UnsupportedClassVersionError。",
            Cause = "1.18 起官方要求 Java 17，1.20 沿用。",
            Fix = "安装 Java 17 或更高（1.20.5 起推荐 21）。"
        },
        new() {
            Title = "1.20.5 数据组件上线，旧命令与数据包大面积失效", Level = BugLevel.Major,
            McVersion = "1.20.5", Family = "1.20",
            Desc = "/give、战利品表、数据包里的 NBT 写法全部报错。",
            Cause = "物品改为数据组件系统，替代原有 tag 结构。",
            Fix = "改用新组件语法；数据包要针对 1.20.5+ 重写 pack_format。"
        },
        new() {
            Title = "Forge 47.x 只认 1.20.1，装 1.20.4 会失败", Level = BugLevel.Common,
            McVersion = "1.20", Family = "1.20",
            Desc = "1.20.4 找不到 Forge 版本，或装上后崩溃。",
            Cause = "Forge 在 1.20.2+ 短暂改版（48.x/49.x），生态几乎都停在 1.20.1。",
            Fix = "玩 Forge 模组建议直接用 1.20.1 + Forge 47.x。",
            Loaders = new[] { ModLoader.Forge }
        },
        new() {
            Title = "Fabric 需要 Fabric API，否则绝大多数模组不生效", Level = BugLevel.Common,
            McVersion = "1.20", Family = "1.20",
            Desc = "只装 Fabric Loader 后进游戏，模组列表为空或报缺失依赖。",
            Cause = "Fabric 生态的公共接口都在 Fabric API 模组里。",
            Fix = "同时下载与游戏版本匹配的 Fabric API 放进 mods 目录。",
            Loaders = new[] { ModLoader.Fabric, ModLoader.Quilt }
        },

        // ============ 1.19.x ============
        new() {
            Title = "首次进世界极慢、内存暴涨", Level = BugLevel.Major,
            McVersion = "1.19", Family = "1.19",
            Desc = "1.19 开新区块时卡顿严重，视距调高容易内存溢出。",
            Cause = "噪声洞穴与深暗之域抬高世界生成开销。",
            Fix = "视距降到 8~10，内存分配 4GB 以上，别在高视距下跑图。"
        },
        new() {
            Title = "聊天举报机制引发隐私顾虑", Level = BugLevel.Common,
            McVersion = "1.19", Family = "1.19",
            Desc = "1.19.1 起聊天带签名，部分服务器与玩家不愿使用。",
            Cause = "官方为在线聊天加了消息签名与举报入口。",
            Fix = "单机与局域网不受影响；服务器端可配置关闭。"
        },

        // ============ 1.18.x ============
        new() {
            Title = "Java 17 是硬性要求", Level = BugLevel.Severe,
            McVersion = "1.18", Family = "1.18",
            Desc = "Java 8 启动 1.18 直接报错退出。",
            Cause = "运行时依赖升级到 Java 17。",
            Fix = "配置 Java 17。"
        },
        new() {
            Title = "1.17 升级到 1.18 存档边界突兀、性能下降", Level = BugLevel.Major,
            McVersion = "1.18", Family = "1.18",
            Desc = "新老区块交界处出现明显悬崖断面，帧数下降。",
            Cause = "世界高度从 256 扩到 384，地形生成算法整体更换。",
            Fix = "属正常现象；可接受断面或开新存档，升级前务必备份。"
        },

        // ============ 1.17.x ============
        new() {
            Title = "需要 Java 16 及以上", Level = BugLevel.Severe,
            McVersion = "1.17", Family = "1.17",
            Desc = "Java 8 启动 1.17 会失败。",
            Cause = "1.17 把最低运行时提到 Java 16。",
            Fix = "直接装 Java 17，1.17~1.20.4 都能用。"
        },
        new() {
            Title = "洞穴与山崖更新后同画质帧数下降", Level = BugLevel.Common,
            McVersion = "1.17", Family = "1.17",
            Desc = "老机器升级到 1.17 后明显变卡。",
            Cause = "新方块与渲染内容增加。",
            Fix = "降低视距与模拟距离，关云和粒子效果。"
        },

        // ============ 1.16.x ============
        new() {
            Title = "下界更新的旧存档，出生点附近仍是旧地形", Level = BugLevel.Common,
            McVersion = "1.16", Family = "1.16",
            Desc = "从 1.15 升级后，下界出生点周围看不到新群系。",
            Cause = "已生成区块不会自动重新生成。",
            Fix = "属正常行为，去未加载过的区块即可看到新地形。"
        },

        // ============ 1.14.x ============
        new() {
            Title = "村庄与掠夺更新后，旧资源包与旧模组基本全废", Level = BugLevel.Major,
            McVersion = "1.14", Family = "1.14",
            Desc = "材质缺失、模型错乱，模组全部无法加载。",
            Cause = "材质目录结构与模型格式重写。",
            Fix = "使用 1.14 专用资源包；模组需对应版本重发。"
        },

        // ============ 1.13.x ============
        new() {
            Title = "扁平化更新：旧存档方块可能变空气", Level = BugLevel.Severe,
            McVersion = "1.13", Family = "1.13",
            Desc = "带模组的 1.12 存档升级到 1.13 后，部分模组方块消失。",
            Cause = "数字 ID 换成命名空间 ID，方块注册表彻底变化。",
            Fix = "升级前必须备份；带模组的存档不要直接升 1.13。"
        },
        new() {
            Title = "资源体积大增，内存不足会频繁卡顿", Level = BugLevel.Common,
            McVersion = "1.13", Family = "1.13",
            Desc = "1.13 起启动后卡顿明显。",
            Cause = "新增大量材质与模型，加载压力变大。",
            Fix = "内存分配到 2GB 以上。"
        },

        // ============ 1.12 / 1.8 ============
        new() {
            Title = "1.12.2 是模组生态的经典停靠点", Level = BugLevel.Common,
            McVersion = "1.12", Family = "1.12",
            Desc = "大批老整合包停在 1.12.2，用不上新版本特性。",
            Cause = "历史上模组生态在此长期沉淀。",
            Fix = "玩新内容用新版；玩老整合包就固定在 1.12.2 + Forge 14.23.5.2860。"
        },
        new() {
            Title = "1.8.9 是 PvP 与小游戏常用版本，已停止维护", Level = BugLevel.Common,
            McVersion = "1.8", Family = "1.8",
            Desc = "1.8 客户端无法进新协议服务器。",
            Cause = "协议与客户端行为在此版本被长期固化。",
            Fix = "进对应服务器时手动选 1.8.9；装模组用 Forge 11.15.1.2318。"
        },

        // ============ 快照专用 ============
        new() {
            Title = "快照存档不保证向前兼容", Level = BugLevel.Severe,
            TypeFilter = "snapshot",
            Desc = "本周快照打开过的世界，下周快照可能直接崩。",
            Cause = "快照随时修改世界格式，不作兼容承诺。",
            Fix = "快照只用独立目录另开存档，绝不用快照打开正式版存档。"
        },
        new() {
            Title = "快照不能装 Forge/NeoForge", Level = BugLevel.Major,
            TypeFilter = "snapshot",
            Desc = "快照版本在加载器列表里找不到可用版本。",
            Cause = "加载器通常只跟进正式版，快照支持滞后数周甚至不做。",
            Fix = "玩模组请用正式版；快照只做原版体验。",
            Loaders = new[] { ModLoader.Forge, ModLoader.NeoForge }
        },
        new() {
            Title = "快照的 Java 要求会提前提高", Level = BugLevel.Major,
            TypeFilter = "snapshot",
            Desc = "某次快照突然要求更高 Java 版本，旧运行时启动失败。",
            Cause = "官方常在下个大版本的快照期就抬升最低 Java 要求。",
            Fix = "保持 Java 21 常备；启动失败先看日志里的 UnsupportedClassVersionError。"
        },
        new() {
            Title = "快照协议与正式版不通，联机需两端同快照", Level = BugLevel.Common,
            TypeFilter = "snapshot",
            Desc = "快照客户端连正式版服务器直接拒绝。",
            Cause = "快照协议号与正式版不同。",
            Fix = "联机双方使用同一快照版本号。"
        },

        // ============ 远古版本 ============
        new() {
            Title = "远古版本需 Java 8 与旧版启动参数", Level = BugLevel.Major,
            TypeFilter = "old_beta",
            Desc = "Beta 版本用现代 Java 或现行参数无法启动。",
            Cause = "旧版 LWJGL 与 Java 8+ 不兼容，参数名也不同。",
            Fix = "用 Java 8 并让启动器按旧格式生成参数。"
        },
        new() {
            Title = "远古版本需 Java 8 与旧版启动参数", Level = BugLevel.Major,
            TypeFilter = "old_alpha",
            Desc = "Alpha 版本无法用现代 Java 直接启动。",
            Cause = "旧版 LWJGL 与 Java 8+ 不兼容。",
            Fix = "用 Java 8 并配合旧版启动参数。"
        },

        // ============ 通用加载器坑 ============
        new() {
            Title = "Forge 安装后缺少 libraries 导致启动即崩", Level = BugLevel.Major,
            Desc = "日志报 NoClassDefFoundError，指向 net.minecraftforge 下的类。",
            Cause = "安装时网络中断，Forge 的库文件没下全。",
            Fix = "重新跑一次 Forge 安装，或用启动器的「验证游戏文件」补齐库。",
            Loaders = new[] { ModLoader.Forge }
        },
        new() {
            Title = "OptiFine 与 Forge/NeoForge 冲突", Level = BugLevel.Major,
            Desc = "同时装 OptiFine 和 Forge 后启动崩溃或渲染异常。",
            Cause = "OptiFine 会替换渲染类，与部分模组及新版渲染管线冲突。",
            Fix = "新版用 Sodium/Iris 替代；必须用 OptiFine 时选对应的 Forge 兼容版本。",
            Loaders = new[] { ModLoader.OptiFine }
        },
        new() {
            Title = "LiteLoader 已停止维护，新版本不可用", Level = BugLevel.Common,
            Desc = "1.13 以后找不到 LiteLoader 版本。",
            Cause = "项目自 1.12.2 之后停止更新。",
            Fix = "1.12.2 以内可选择；更高版本改用其他加载器。",
            Loaders = new[] { ModLoader.LiteLoader }
        }
    };

    public static int TotalCount => All.Count;

    /// <summary>
    /// 按版本 + 加载器筛选。
    /// 关键：快照/远古只取 TypeFilter 完全匹配的条目，不会被正式版条目串入。
    /// </summary>
    public static List<BugEntry> Query(string mcVersion, string type, ModLoader loader)
    {
        var isSnapshotLike = type is "snapshot" or "old_beta" or "old_alpha";
        var tag = VersionUtil.GetMajorTag(mcVersion);
        var family = VersionUtil.GetFamily(mcVersion);

        return All.Where(item =>
        {
            // 1. 类型筛选
            if (item.TypeFilter != null && item.TypeFilter != type) return false;

            // 2. 版本筛选：精确版本 或 家族 任一命中即可
            //    注意不能写成 if/else——McVersion="1.21" 的条目在查 1.21.4 时
            //    精确值对不上，但仍应通过 Family="1.21" 命中。
            var exactHit = item.McVersion != null &&
                           (item.McVersion == tag || item.McVersion == mcVersion);

            var familyHit = item.Family != null && !isSnapshotLike && item.Family == family;

            // 无版本限定：带类型限定的只在对应类型下生效，其余不参与
            var typelessHit = item.McVersion == null && item.Family == null &&
                              item.TypeFilter != null && item.TypeFilter == type;

            if (!exactHit && !familyHit && !typelessHit) return false;

            // 3. 加载器筛选
            return item.Loaders.Length == 0 || item.Loaders.Contains(loader);
        }).ToList();
    }
}
