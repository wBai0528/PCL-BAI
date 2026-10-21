package com.pclmini.launcher.data

/** 严重程度。 */
enum class BugLevel(val label: String, val colorHex: Long) {
    COMMON("常见", 0xFF2B8A3E),
    MAJOR("影响较大", 0xFFE67700),
    SEVERE("严重", 0xFFD9480F),
}

/**
 * 一条已知问题。
 *
 * @param mcVersion 精确大版本，如 "1.21.4"；为空表示不限版本
 * @param family    家族，如 "1.21"，同家族所有小版本命中
 * @param type      仅对某一类版本生效（正式版/快照），null 表示不限
 * @param loaders   仅对某类加载器生效，空集合表示原版通用
 */
data class BugItem(
    val title: String,
    val level: BugLevel,
    val desc: String,
    val cause: String,
    val fix: String,
    val mcVersion: String? = null,
    val family: String? = null,
    val type: VersionType? = null,
    val loaders: Set<ModLoader> = emptySet(),
) {
    val loaderTags: String
        get() = if (loaders.isEmpty()) "全部" else loaders.joinToString("/") { it.shortName }
}

/**
 * Bug 知识库。
 *
 * 修好的两件事：
 * 1. 正式版与快照分开索引——快照只命中 type==SNAPSHOT 的条目；
 * 2. 条目覆盖到每个大版本的正式版共性问题，以及 Forge/NeoForge/Fabric 等加载器坑，
 *    不再只剩「快照不向前兼容」一条。
 */
object BugDatabase {

    private val ALL = listOf(
        // ============ 1.21.x 正式版 ============
        bug(
            title = "需要 Java 21，用 Java 17 直接闪退",
            level = BugLevel.SEVERE, mcVersion = "1.21", family = "1.21",
            desc = "点击启动后窗口一闪而过，日志出现 UnsupportedClassVersionError。",
            cause = "1.21 起官方把最低运行时提升到 Java 21。",
            fix = "安装 Java 21（Microsoft OpenJDK 21 / Temurin 21），并在启动器里把 Java 版本切到 21。",
        ),
        bug(
            title = "模组集体失效：注册表与数据组件结构变化",
            level = BugLevel.MAJOR, mcVersion = "1.21.2", family = "1.21",
            desc = "1.21.1 时期的模组在 1.21.2+ 加载直接崩溃或静默不生效。",
            cause = "1.21.2 起物品改用数据组件系统，注册表签名也变了。",
            fix = "换成对应小版本的模组；模组没更新就停在 1.21.1。",
            loaders = setOf(ModLoader.FORGE, ModLoader.NEOFORGE, ModLoader.FABRIC, ModLoader.QUILT),
        ),
        bug(
            title = "Forge 在 1.21 需要 50.x 以上，旧版本装不上",
            level = BugLevel.MAJOR, mcVersion = "1.21", family = "1.21",
            desc = "用 Forge 47.x 安装 1.21 会提示找不到版本或安装失败。",
            cause = "Forge 为 1.21 单开了 51.x/52.x 分支，1.20.1 的 47.x 不兼容。",
            fix = "选 Forge 1.21 对应的 51.x 及以上；1.21.4 用 54.x。",
            loaders = setOf(ModLoader.FORGE),
        ),
        bug(
            title = "NeoForge 与 Forge 不能同时装",
            level = BugLevel.SEVERE, mcVersion = "1.21", family = "1.21",
            desc = "同一个版本目录同时装 Forge 和 NeoForge，启动即崩。",
            cause = "两者都替换主类并抢注同名库，互斥。",
            fix = "每个游戏版本目录只装一种加载器，分开建版本。",
            loaders = setOf(ModLoader.NEOFORGE, ModLoader.FORGE),
        ),
        bug(
            title = "新版渲染管线导致部分老显卡闪退或黑屏",
            level = BugLevel.MAJOR, mcVersion = "1.21.6", family = "1.21",
            desc = "1.21.6 之后在部分 Intel 核显、老 AMD 驱动上画面全黑或直接退出。",
            cause = "新渲染管线依赖较新的 OpenGL 能力与驱动实现。",
            fix = "更新显卡驱动；不行就在视频设置里关掉高级渲染，或降到 1.21.5。",
        ),
        bug(
            title = "服务端与客户端必须同一小版本",
            level = BugLevel.COMMON, mcVersion = "1.21.8", family = "1.21",
            desc = "1.21.8 连 1.21.7 服务器提示「版本不符」。",
            cause = "Mojang 在小版本里调整了协议号。",
            fix = "两端小版本保持一致，或用 ViaVersion 类插件做协议兼容。",
        ),
        bug(
            title = "升级后世界数据不可回退（1.21.9 铜器时代）",
            level = BugLevel.SEVERE, mcVersion = "1.21.9", family = "1.21",
            desc = "在 1.21.9 打开过的存档，旧版本客户端再也打不开。",
            cause = "新增区块方块与实体类型抬高了 Level.dat 版本号。",
            fix = "升级前先完整备份 saves 目录；要回退只能靠备份。",
        ),

        // ============ 1.20.x ============
        bug(
            title = "需要 Java 17，Java 8 启动即崩",
            level = BugLevel.SEVERE, mcVersion = "1.20", family = "1.20",
            desc = "用 Java 8 启动 1.20 会在日志里报 UnsupportedClassVersionError。",
            cause = "1.18 起官方要求 Java 17，1.20 沿用。",
            fix = "安装 Java 17 或更高（1.20.5 起推荐 21）。",
        ),
        bug(
            title = "1.20.5 数据组件上线，旧命令与数据包大面积失效",
            level = BugLevel.MAJOR, mcVersion = "1.20.5", family = "1.20",
            desc = "/give、战利品表、数据包里的 NBT 写法全部报错。",
            cause = "物品改为数据组件系统，替代原有 tag 结构。",
            fix = "改用新组件语法；数据包要针对 1.20.5+ 重写 pack_format。",
        ),
        bug(
            title = "Forge 47.x 只认 1.20.1，装 1.20.4 会失败",
            level = BugLevel.COMMON, mcVersion = "1.20", family = "1.20",
            desc = "1.20.4 找不到 Forge 版本，或装上后崩溃。",
            cause = "Forge 在 1.20.2+ 短暂改版（48.x/49.x），生态几乎都停在 1.20.1。",
            fix = "玩 Forge 模组建议直接用 1.20.1 + Forge 47.x。",
            loaders = setOf(ModLoader.FORGE),
        ),
        bug(
            title = "Fabric 需要 Fabric API，否则绝大多数模组不生效",
            level = BugLevel.COMMON, mcVersion = "1.20", family = "1.20",
            desc = "只装 Fabric Loader 后进游戏，模组列表为空或报缺失依赖。",
            cause = "Fabric 生态的公共接口都在 Fabric API 模组里。",
            fix = "同时下载与游戏版本匹配的 Fabric API 放进 mods 目录。",
            loaders = setOf(ModLoader.FABRIC, ModLoader.QUILT),
        ),

        // ============ 1.19.x ============
        bug(
            title = "首次进世界极慢、内存暴涨",
            level = BugLevel.MAJOR, mcVersion = "1.19", family = "1.19",
            desc = "1.19 开新区块时卡顿严重，视距调高容易内存溢出。",
            cause = "噪声洞穴与深暗之域抬高世界生成开销。",
            fix = "视距降到 8~10，内存分配 4GB 以上，别在高视距下跑图。",
        ),
        bug(
            title = "聊天举报机制引发隐私顾虑",
            level = BugLevel.COMMON, mcVersion = "1.19", family = "1.19",
            desc = "1.19.1 起聊天带签名，部分服务器与玩家不愿使用。",
            cause = "官方为在线聊天加了消息签名与举报入口。",
            fix = "单机与局域网不受影响；服务器端可配置关闭。",
        ),

        // ============ 1.18.x ============
        bug(
            title = "Java 17 是硬性要求",
            level = BugLevel.SEVERE, mcVersion = "1.18", family = "1.18",
            desc = "Java 8 启动 1.18 直接报错退出。",
            cause = "运行时依赖升级到 Java 17。",
            fix = "配置 Java 17。",
        ),
        bug(
            title = "1.17 升级到 1.18 存档边界突兀、性能下降",
            level = BugLevel.MAJOR, mcVersion = "1.18", family = "1.18",
            desc = "新老区块交界处出现明显悬崖断面，帧数下降。",
            cause = "世界高度从 256 扩到 384，地形生成算法整体更换。",
            fix = "属正常现象；可接受断面或开新存档，升级前务必备份。",
        ),

        // ============ 1.17.x ============
        bug(
            title = "需要 Java 16 及以上",
            level = BugLevel.SEVERE, mcVersion = "1.17", family = "1.17",
            desc = "Java 8 启动 1.17 会失败。",
            cause = "1.17 把最低运行时提到 Java 16。",
            fix = "直接装 Java 17，1.17~1.20.4 都能用。",
        ),
        bug(
            title = "洞穴与山崖更新后同画质帧数下降",
            level = BugLevel.COMMON, mcVersion = "1.17", family = "1.17",
            desc = "老机器升级到 1.17 后明显变卡。",
            cause = "新方块与渲染内容增加。",
            fix = "降低视距与模拟距离，关云和粒子效果。",
        ),

        // ============ 1.16.x ============
        bug(
            title = "下界更新的旧存档，出生点附近仍是旧地形",
            level = BugLevel.COMMON, mcVersion = "1.16", family = "1.16",
            desc = "从 1.15 升级后，下界出生点周围看不到新群系。",
            cause = "已生成区块不会自动重新生成。",
            fix = "属正常行为，去未加载过的区块即可看到新地形。",
        ),

        // ============ 1.14.x ============
        bug(
            title = "村庄与掠夺更新后，旧资源包与旧模组基本全废",
            level = BugLevel.MAJOR, mcVersion = "1.14", family = "1.14",
            desc = "材质缺失、模型错乱，模组全部无法加载。",
            cause = "材质目录结构与模型格式重写。",
            fix = "使用 1.14 专用资源包；模组需对应版本重发。",
        ),

        // ============ 1.13.x ============
        bug(
            title = "扁平化更新：旧存档方块可能变空气",
            level = BugLevel.SEVERE, mcVersion = "1.13", family = "1.13",
            desc = "带模组的 1.12 存档升级到 1.13 后，部分模组方块消失。",
            cause = "数字 ID 换成命名空间 ID，方块注册表彻底变化。",
            fix = "升级前必须备份；带模组的存档不要直接升 1.13。",
        ),
        bug(
            title = "资源体积大增，内存不足会频繁卡顿",
            level = BugLevel.COMMON, mcVersion = "1.13", family = "1.13",
            desc = "1.13 起启动后卡顿明显。",
            cause = "新增大量材质与模型，加载压力变大。",
            fix = "内存分配到 2GB 以上。",
        ),

        // ============ 1.12 / 1.8 ============
        bug(
            title = "1.12.2 是模组生态的经典停靠点",
            level = BugLevel.COMMON, mcVersion = "1.12", family = "1.12",
            desc = "大批老整合包停在 1.12.2，用不上新版本特性。",
            cause = "历史上模组生态在此长期沉淀。",
            fix = "玩新内容用新版；玩老整合包就固定在 1.12.2 + Forge 14.23.5.2860。",
        ),
        bug(
            title = "1.8.9 是 PvP 与小游戏常用版本，已停止维护",
            level = BugLevel.COMMON, mcVersion = "1.8", family = "1.8",
            desc = "1.8 客户端无法进新协议服务器。",
            cause = "协议与客户端行为在此版本被长期固化。",
            fix = "进对应服务器时手动选 1.8.9；装模组用 Forge 11.15.1.2318。",
        ),

        // ============ 快照专用（只对 type==SNAPSHOT 生效）============
        bug(
            title = "快照存档不保证向前兼容",
            level = BugLevel.SEVERE, type = VersionType.SNAPSHOT,
            desc = "本周快照打开过的世界，下周快照可能直接崩。",
            cause = "快照随时修改世界格式，不作兼容承诺。",
            fix = "快照只用独立目录另开存档，绝不用快照打开正式版存档。",
        ),
        bug(
            title = "快照不能装 Forge/NeoForge",
            level = BugLevel.MAJOR, type = VersionType.SNAPSHOT,
            desc = "快照版本在加载器列表里找不到可用版本。",
            cause = "加载器通常只跟进正式版，快照支持滞后数周甚至不做。",
            fix = "玩模组请用正式版；快照只做原版体验。",
            loaders = setOf(ModLoader.FORGE, ModLoader.NEOFORGE),
        ),
        bug(
            title = "快照的 Java 要求会提前提高",
            level = BugLevel.MAJOR, type = VersionType.SNAPSHOT,
            desc = "某次快照突然要求更高 Java 版本，旧运行时启动失败。",
            cause = "官方常在下个大版本的快照期就抬升最低 Java 要求。",
            fix = "保持 Java 21 常备；启动失败先看日志里的 UnsupportedClassVersionError。",
        ),
        bug(
            title = "快照协议与正式版不通，联机需两端同快照",
            level = BugLevel.COMMON, type = VersionType.SNAPSHOT,
            desc = "快照客户端连正式版服务器直接拒绝。",
            cause = "快照协议号与正式版不同。",
            fix = "联机双方使用同一快照版本号。",
        ),

        // ============ 远古版本 ============
        bug(
            title = "远古版本需 Java 8 与旧版启动参数",
            level = BugLevel.MAJOR, type = VersionType.OLD_BETA,
            desc = "Beta 版本用现代 Java 或现行参数无法启动。",
            cause = "旧版 LWJGL 与 Java 8+ 不兼容，参数名也不同。",
            fix = "用 Java 8 并让启动器按旧格式生成参数。",
        ),
        bug(
            title = "远古版本需 Java 8 与旧版启动参数",
            level = BugLevel.MAJOR, type = VersionType.OLD_ALPHA,
            desc = "Alpha 版本无法用现代 Java 直接启动。",
            cause = "旧版 LWJGL 与 Java 8+ 不兼容。",
            fix = "用 Java 8 并配合旧版启动参数。",
        ),

        // ============ 通用加载器坑 ============
        bug(
            title = "Forge 安装后缺少 libraries 导致启动即崩",
            level = BugLevel.MAJOR, loaders = setOf(ModLoader.FORGE),
            desc = "日志报 NoClassDefFoundError，指向 net.minecraftforge 下的类。",
            cause = "安装时网络中断，Forge 的库文件没下全。",
            fix = "重新跑一次 Forge 安装，或用启动器的「验证游戏文件」补齐库。",
        ),
        bug(
            title = "OptiFine 与 Forge/NeoForge 冲突",
            level = BugLevel.MAJOR, loaders = setOf(ModLoader.OPTIFINE),
            desc = "同时装 OptiFine 和 Forge 后启动崩溃或渲染异常。",
            cause = "OptiFine 会替换渲染类，与部分模组及新版渲染管线冲突。",
            fix = "新版用 Sodium/Iris 替代；必须用 OptiFine 时选对应的 Forge 兼容版本。",
        ),
        bug(
            title = "LiteLoader 已停止维护，新版本不可用",
            level = BugLevel.COMMON, loaders = setOf(ModLoader.LITELOADER),
            desc = "1.13 以后找不到 LiteLoader 版本。",
            cause = "项目自 1.12.2 之后停止更新。",
            fix = "1.12.2 以内可选择；更高版本改用其他加载器。",
        ),
    )

    private fun bug(
        title: String,
        level: BugLevel,
        desc: String,
        cause: String,
        fix: String,
        mcVersion: String? = null,
        family: String? = null,
        type: VersionType? = null,
        loaders: Set<ModLoader> = emptySet(),
    ) = BugItem(title, level, desc, cause, fix, mcVersion, family, type, loaders)

    /**
     * 按版本 + 加载器筛选。
     *
     * 关键修复：快照/远古版本只取 type 完全匹配的条目，
     * 不再像旧实现那样退化成「只要 family 对得上就算命中」，
     * 也不会把所有条目一股脑塞进任何版本。
     */
    fun query(
        mcVersion: String,
        mcType: VersionType,
        loader: ModLoader = ModLoader.VANILLA,
    ): List<BugItem> {
        val tag = VersionUtil.majorTag(mcVersion, mcType)
        val fam = VersionUtil.family(mcVersion, mcType)

        return ALL.filter { item ->
            // 1. 类型筛选：条目限定类型时必须完全一致
            if (item.type != null && item.type != mcType) return@filter false

            // 2. 版本筛选：精确版本 或 家族 任一命中即可
            //    注意不能写 when/else if——mcVersion="1.20" 的条目在查 1.20.1 时
            //    精确值对不上，但仍应通过 family="1.20" 命中（曾导致 1.20.1 显示 0 条）。
            val exactHit = item.mcVersion != null &&
                (item.mcVersion == tag || item.mcVersion == mcVersion)

            val familyHit = item.family != null &&
                !mcType.isSnapshotLike && item.family == fam

            // 无版本限定：带类型限定的只在对应类型下生效
            val typelessHit = item.mcVersion == null && item.family == null &&
                item.type != null && item.type == mcType

            if (!exactHit && !familyHit && !typelessHit) return@filter false

            // 3. 加载器筛选：条目不限加载器时所有加载器都能看到
            item.loaders.isEmpty() || loader in item.loaders
        }
    }

    /** 供界面统计：知识库覆盖的正式版大版本数。 */
    fun releaseVersionCount(): Int =
        ALL.mapNotNull { it.mcVersion ?: it.family }.distinct().size

    val totalCount: Int get() = ALL.size
}
