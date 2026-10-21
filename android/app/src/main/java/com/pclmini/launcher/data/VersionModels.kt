package com.pclmini.launcher.data

/**
 * 版本类型。核心修正点：正式版与快照必须严格区分，
 * 旧版实现只靠 id 正则判断，导致 24w14a 之类被误判。
 * 依据是版本清单里的 type 字段（release / snapshot / old_beta / old_alpha）。
 */
enum class VersionType(val apiValue: String, val label: String) {
    RELEASE("release", "正式版"),
    SNAPSHOT("snapshot", "快照"),
    OLD_BETA("old_beta", "远古 Beta"),
    OLD_ALPHA("old_alpha", "远古 Alpha"),
    UNKNOWN("", "未知");

    /** 快照类（含远古版本）不参与家族匹配，避免把 1.21 的条目套到 24w14a 上。 */
    val isSnapshotLike: Boolean get() = this == SNAPSHOT || this == OLD_BETA || this == OLD_ALPHA

    companion object {
        fun fromApi(value: String?): VersionType = when (value?.lowercase()) {
            "release" -> RELEASE
            "snapshot" -> SNAPSHOT
            "old_beta" -> OLD_BETA
            "old_alpha" -> OLD_ALPHA
            else -> UNKNOWN
        }
    }
}

/** 版本清单条目。 */
data class VersionEntry(
    val id: String,
    val type: VersionType,
    val url: String,
    val releaseTime: String,
    val sha1: String? = null,
) {
    /** 大版本号：1.21.4 的 pre/rc 后缀去掉后取前三段。 */
    val majorTag: String get() = VersionUtil.majorTag(id, type)

    /** 家族：1.21.4 → 1.21。快照返回自身类型标签。 */
    val family: String get() = VersionUtil.family(id, type)

    val displayDate: String get() = VersionUtil.shortDate(releaseTime)
}

/** 模组加载器。 */
enum class ModLoader(val label: String, val shortName: String) {
    VANILLA("原版", "原版"),
    FORGE("Forge", "Forge"),
    NEOFORGE("NeoForge", "NeoForge"),
    FABRIC("Fabric", "Fabric"),
    QUILT("Quilt", "Quilt"),
    OPTIFINE("OptiFine", "OptiFine"),
    LITELOADER("LiteLoader", "LiteLoader"),
    ;

    val isModded: Boolean get() = this != VANILLA
}

/** 加载器版本条目（如 Forge 1.20.1 对应的 47.2.0）。 */
data class LoaderVersion(
    val loader: ModLoader,
    val version: String,
    val mcVersion: String,
    /** 是否推荐/稳定版。 */
    val isRecommended: Boolean = false,
    /** 展示用的附加信息，如渠道、构建号。 */
    val note: String? = null,
)

/** 可下载的版本组合，例如「1.20.1 + Forge 47.2.0」。 */
data class VersionCombo(
    val mcVersion: String,
    val mcType: VersionType,
    val loader: ModLoader = ModLoader.VANILLA,
    val loaderVersion: String? = null,
) {
    val label: String get() = if (loader.isModded && loaderVersion != null)
        "$mcVersion · ${loader.shortName} $loaderVersion"
    else
        mcVersion
}

object VersionUtil {
    /** 24w14a、1.21.4-pre1、1.21-rc1 这类后缀统一去掉。 */
    fun majorTag(id: String, type: VersionType): String {
        if (id.isBlank()) return "未知"
        if (type == VersionType.OLD_ALPHA) return "Alpha"
        if (type == VersionType.OLD_BETA) return "Beta"
        if (type == VersionType.SNAPSHOT) return "快照"

        var s = id
        for (marker in listOf("-pre", "-rc", "-Pre", "-Rc")) {
            val i = s.indexOf(marker)
            if (i > 0) { s = s.substring(0, i); break }
        }
        val parts = s.split(".")
        return if (parts.size >= 3) "${parts[0]}.${parts[1]}.${parts[2]}" else s
    }

    fun family(id: String, type: VersionType): String {
        if (type.isSnapshotLike) return type.label
        val tag = majorTag(id, type)
        val parts = tag.split(".")
        return if (parts.size >= 2) "${parts[0]}.${parts[1]}" else tag
    }

    /** "2026-09-15T19:23:02+08:00" → "2026-09-15"。 */
    fun shortDate(iso: String?): String {
        if (iso.isNullOrBlank()) return ""
        return if (iso.length >= 10) iso.substring(0, 10) else iso
    }

    /** 数值化版本比较，用于排序。 */
    fun compare(a: String, b: String): Int {
        val pa = a.split(".")
        val pb = b.split(".")
        for (i in 0 until maxOf(pa.size, pb.size)) {
            val sa = pa.getOrNull(i) ?: "0"
            val sb = pb.getOrNull(i) ?: "0"
            val ia = sa.toLongOrNull()
            val ib = sb.toLongOrNull()
            val c = if (ia != null && ib != null) ia.compareTo(ib) else sa.compareTo(sb, true)
            if (c != 0) return c
        }
        return 0
    }
}
