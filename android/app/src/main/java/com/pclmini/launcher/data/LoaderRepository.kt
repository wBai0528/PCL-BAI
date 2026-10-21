package com.pclmini.launcher.data

import org.json.JSONObject

/**
 * 加载器版本数据源。
 *
 * 各加载器接口不同：
 * - Forge:     promotions 接口给出 recommended / latest
 * - NeoForge:  maven-metadata 列出全部版本，按 MC 小版本前缀过滤
 * - Fabric:    meta v2 的 loader 列表（与 MC 版本无关）
 * - Quilt:     结构同 Fabric
 *
 * 快照版本统一不提供加载器：加载器只跟进正式版，
 * 这是「正式版和快照要区分」的一部分。
 */
object LoaderRepository {

    /** 该正式版是否理论上提供某加载器。 */
    fun isLoaderAvailable(loader: ModLoader, mcVersion: String, mcType: VersionType): Boolean {
        if (mcType != VersionType.RELEASE) return false
        val v = mcVersion.removePrefix("1.").toDoubleOrNull() ?: return false
        return when (loader) {
            ModLoader.VANILLA -> true
            ModLoader.FORGE -> v >= 1.0
            ModLoader.NEOFORGE -> v >= 20.2
            ModLoader.FABRIC -> v >= 14.0
            ModLoader.QUILT -> v >= 14.0
            ModLoader.OPTIFINE -> v >= 6.0
            ModLoader.LITELOADER -> v <= 12.2
        }
    }

    /**
     * 解析 Forge promotions：
     * { "promos": { "1.20.1-recommended": "47.2.0", "1.20.1-latest": "47.3.0" } }
     */
    fun parseForgePromotions(json: String, mcVersion: String): List<LoaderVersion> {
        val out = mutableListOf<LoaderVersion>()
        val promos = JSONObject(json).optJSONObject("promos") ?: return out
        val recommended = promos.optString("$mcVersion-recommended", "")
        val latest = promos.optString("$mcVersion-latest", "")

        if (recommended.isNotBlank()) {
            out += LoaderVersion(ModLoader.FORGE, recommended, mcVersion, true, "推荐")
        }
        if (latest.isNotBlank() && latest != recommended) {
            out += LoaderVersion(ModLoader.FORGE, latest, mcVersion, false, "最新")
        }
        return out
    }

    /**
     * 解析 NeoForge maven-metadata.xml，抽出与该 MC 版本匹配的版本号。
     * NeoForge 的版本号前缀是去掉 "1." 的 MC 版本，如 20.4.x / 21.1.x。
     */
    fun parseNeoForgeMaven(xml: String, mcVersion: String): List<LoaderVersion> {
        val prefix = mcVersion.removePrefix("1.") + "."
        return Regex("<version>([^<]+)</version>")
            .findAll(xml)
            .map { it.groupValues[1].trim() }
            .filter { it.startsWith(prefix) }
            .map { ver ->
                val beta = ver.contains("beta", ignoreCase = true)
                LoaderVersion(
                    loader = ModLoader.NEOFORGE,
                    version = ver,
                    mcVersion = mcVersion,
                    isRecommended = !beta,
                    note = if (beta) "Beta" else "稳定",
                )
            }
            .sortedWith(compareByDescending<LoaderVersion> { it.isRecommended }.thenByDescending { it.version })
            .take(6)
            .toList()
    }

    /**
     * 解析 Fabric meta v2：
     * { "loader": [ { "version": "0.15.11", "stable": true } ] }
     */
    fun parseFabric(json: String): List<LoaderVersion> {
        val out = mutableListOf<LoaderVersion>()
        val arr = JSONObject(json).optJSONArray("loader") ?: return out
        for (i in 0 until arr.length()) {
            val o = arr.optJSONObject(i) ?: continue
            val ver = o.optString("version", "")
            if (ver.isBlank()) continue
            val stable = o.optBoolean("stable", false)
            out += LoaderVersion(ModLoader.FABRIC, ver, "", stable, if (stable) "稳定" else "测试")
        }
        // 稳定版在前，各取前几个避免列表太长
        return (out.filter { it.isRecommended }.take(3) + out.filterNot { it.isRecommended }.take(2))
    }

    /** 展示用的下载地址提示。 */
    fun downloadHint(loader: ModLoader, mcVersion: String): String = when (loader) {
        ModLoader.FORGE -> "BMCLAPI /forge/minecraft/$mcVersion"
        ModLoader.NEOFORGE -> "BMCLAPI maven/net/neoforged/neoforge"
        ModLoader.FABRIC -> "BMCLAPI /fabric-meta/v2/versions/loader"
        ModLoader.QUILT -> "meta.quiltmc.org（暂未接入）"
        ModLoader.OPTIFINE -> "BMCLAPI /optifine/versionList"
        ModLoader.LITELOADER -> "BMCLAPI /maven/com/mumfrey/liteloader"
        ModLoader.VANILLA -> "Mojang / BMCLAPI"
    }
}
