package com.pclmini.launcher.data

import org.json.JSONObject
import java.net.HttpURLConnection
import java.net.URL

/**
 * 网络层：只用系统自带 HttpURLConnection，避免引入 OkHttp 等额外依赖，
 * 保持 APK 体积小、减少构建期依赖解析失败的风险。
 */
object Http {

    private const val UA = "PCLBAI-Android/1.0"

    fun getJson(urls: List<String>, timeoutMs: Int = 10000): String {
        var last: Exception? = null
        for (url in urls) {
            try {
                return get(url, timeoutMs)
            } catch (e: Exception) {
                last = e
            }
        }
        throw last ?: IllegalStateException("无可用地址")
    }

    fun get(url: String, timeoutMs: Int = 10000): String {
        val conn = (URL(url).openConnection() as HttpURLConnection).apply {
            requestMethod = "GET"
            connectTimeout = timeoutMs
            readTimeout = timeoutMs
            setRequestProperty("User-Agent", UA)
            instanceFollowRedirects = true
        }
        try {
            val code = conn.responseCode
            if (code !in 200..299) throw IllegalStateException("HTTP $code：$url")
            return conn.inputStream.bufferedReader().use { it.readText() }
        } finally {
            conn.disconnect()
        }
    }
}

/** 版本清单服务。 */
object VersionRepository {

    private const val OFFICIAL = "https://launchermeta.mojang.com/mc/game/version_manifest_v2.json"
    private const val MIRROR = "https://bmclapi2.bangbang93.com/mc/game/version_manifest_v2.json"
    private const val MIRROR_ALT = "https://bmclapi2.bangbang93.com/mc/game/version_manifest.json"

    fun loadManifest(source: Int): List<VersionEntry> {
        val urls = when (source) {
            1 -> listOf(MIRROR, MIRROR_ALT)
            2 -> listOf(OFFICIAL)
            else -> listOf(MIRROR, MIRROR_ALT, OFFICIAL)
        }
        val json = Http.getJson(urls, 15000)
        return parseManifest(json)
    }

    /**
     * 解析清单。
     * 注意：type 字段是区分正式版/快照的唯一可靠依据，
     * 不能靠版本号正则猜——否则 24w14a、1.21-pre1 都会判错。
     */
    fun parseManifest(json: String): List<VersionEntry> {
        val root = JSONObject(json)
        val arr = root.optJSONArray("versions") ?: return emptyList()
        val out = ArrayList<VersionEntry>(arr.length())
        for (i in 0 until arr.length()) {
            val o = arr.optJSONObject(i) ?: continue
            val id = o.optString("id", "")
            if (id.isBlank()) continue
            out += VersionEntry(
                id = id,
                type = VersionType.fromApi(o.optString("type", "")),
                url = o.optString("url", ""),
                releaseTime = o.optString("releaseTime", ""),
                sha1 = o.optString("sha1", "").takeIf { it.isNotBlank() },
            )
        }
        return out
    }
}
