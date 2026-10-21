package com.pclmini.launcher.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.pclmini.launcher.LauncherApp
import com.pclmini.launcher.data.*
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

/** 顶部筛选条件。 */
private enum class TypeFilter(val label: String, val type: VersionType?) {
    ALL("全部", null),
    RELEASE("正式版", VersionType.RELEASE),
    SNAPSHOT("快照", VersionType.SNAPSHOT),
    OLD("远古", VersionType.OLD_BETA),
}

@Composable
fun VersionScreen() {
    val app = LauncherApp.instance
    val settings = app.settings
    val scope = rememberCoroutineScope()

    var versions by remember { mutableStateOf<List<VersionEntry>>(emptyList()) }
    var loading by remember { mutableStateOf(true) }
    var error by remember { mutableStateOf<String?>(null) }
    var typeFilter by remember { mutableStateOf(TypeFilter.ALL) }
    var search by remember { mutableStateOf("") }

    var selected by remember { mutableStateOf<VersionEntry?>(null) }
    var loader by remember { mutableStateOf(ModLoader.VANILLA) }

    // 加载器版本缓存：mcVersion -> (loader -> 列表)
    var loaderCache by remember { mutableStateOf<Map<String, List<LoaderVersion>>>(emptyMap()) }
    var loaderLoading by remember { mutableStateOf(false) }
    var loaderError by remember { mutableStateOf<String?>(null) }

    // 首次进入：默认展示正式版优先，但不清空筛选，用户可以自己切
    LaunchedEffect(Unit) {
        try {
            val list = withContext(Dispatchers.IO) {
                VersionRepository.loadManifest(settings.downloadSource)
            }
            versions = list
            selected = list.firstOrNull { it.id == settings.lastMcVersion }
                ?: list.firstOrNull { it.type == VersionType.RELEASE }
            if (settings.releaseOnly) typeFilter = TypeFilter.RELEASE
        } catch (e: Exception) {
            error = e.message ?: "网络请求失败"
        } finally {
            loading = false
        }
    }

    // 切换版本后，若加载器需要在线拉版本号，就去拉
    LaunchedEffect(selected?.id, loader) {
        val v = selected ?: return@LaunchedEffect
        if (!loader.isModded) return@LaunchedEffect
        if (!LoaderRepository.isLoaderAvailable(loader, v.majorTag, v.type)) return@LaunchedEffect

        val key = "${v.majorTag}|${loader.name}"
        if (loaderCache.containsKey(key)) return@LaunchedEffect

        loaderLoading = true
        loaderError = null
        try {
            val list = withContext(Dispatchers.IO) { fetchLoaderVersions(loader, v.majorTag) }
            loaderCache = loaderCache + (key to list)
        } catch (e: Exception) {
            loaderError = e.message ?: "获取加载器版本失败"
        } finally {
            loaderLoading = false
        }
    }

    val filtered = remember(versions, typeFilter, search) {
        versions.filter { v ->
            val typeOk = when (typeFilter) {
                TypeFilter.ALL -> true
                TypeFilter.RELEASE -> v.type == VersionType.RELEASE
                TypeFilter.SNAPSHOT -> v.type == VersionType.SNAPSHOT
                TypeFilter.OLD -> v.type == VersionType.OLD_BETA || v.type == VersionType.OLD_ALPHA
            }
            val searchOk = search.isBlank() || v.id.contains(search, ignoreCase = true)
            typeOk && searchOk
        }
    }

    // 参数变化时重新计算 bug 列表（Compose 会自动重算）
    val bugs = remember(selected?.id, loader) {
        val v = selected ?: return@remember emptyList()
        BugDatabase.query(v.id, v.type, loader)
    }

    Column(Modifier.fillMaxSize().background(BgPage)) {

        // ---------- 顶部：标题 + 搜索 + 类型筛选 ----------
        Column(
            Modifier
                .fillMaxWidth()
                .background(Color.White)
                .padding(horizontal = 14.dp, vertical = 10.dp)
        ) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text("PCL-BAI", fontSize = 19.sp, fontWeight = FontWeight.Bold, color = Accent)
                Spacer(Modifier.width(8.dp))
                Text("手机版 · 下载 / 验证 / 版本详解", fontSize = 11.sp, color = TextSub)
            }
            Spacer(Modifier.height(8.dp))
            OutlinedTextField(
                value = search,
                onValueChange = { search = it },
                placeholder = { Text("搜索版本号，如 1.20.1 / 24w14a", fontSize = 13.sp) },
                singleLine = true,
                modifier = Modifier.fillMaxWidth(),
                shape = RoundedCornerShape(8.dp),
            )
            Spacer(Modifier.height(8.dp))
            // 正式版 / 快照 必须能明确区分开
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                TypeFilter.entries.forEach { f ->
                    FilterChip(
                        selected = typeFilter == f,
                        onClick = { typeFilter = f },
                        label = { Text(f.label, fontSize = 12.sp) },
                    )
                }
            }
        }

        if (loading) {
            Box(Modifier.fillMaxWidth().padding(30.dp), contentAlignment = Alignment.Center) {
                Column(horizontalAlignment = Alignment.CenterHorizontally) {
                    CircularProgressIndicator(Modifier.size(30.dp), color = Accent, strokeWidth = 3.dp)
                    Spacer(Modifier.height(10.dp))
                    Text("正在获取版本列表……", fontSize = 13.sp, color = TextSub)
                }
            }
        }

        error?.let {
            Box(Modifier.fillMaxWidth().padding(20.dp), contentAlignment = Alignment.Center) {
                Column(horizontalAlignment = Alignment.CenterHorizontally) {
                    Text("加载失败：$it", fontSize = 13.sp, color = Color(0xFFC92A2A))
                    Spacer(Modifier.height(8.dp))
                    Button(onClick = {
                        loading = true; error = null
                        scope.launch {
                            try {
                                versions = withContext(Dispatchers.IO) {
                                    VersionRepository.loadManifest(settings.downloadSource)
                                }
                                selected = versions.firstOrNull { it.type == VersionType.RELEASE }
                            } catch (e: Exception) {
                                error = e.message
                            } finally {
                                loading = false
                            }
                        }
                    }) { Text("重试") }
                }
            }
        }

        // ---------- 版本列表 ----------
        Text(
            "共 ${filtered.size} 个版本",
            fontSize = 11.sp,
            color = TextSub,
            modifier = Modifier.padding(start = 18.dp, top = 8.dp, bottom = 2.dp),
        )
        LazyColumn(
            modifier = Modifier.weight(1f).padding(horizontal = 10.dp),
            contentPadding = PaddingValues(bottom = 12.dp),
        ) {
            items(filtered, key = { it.id }) { v ->
                VersionRow(v, selected?.id == v.id) {
                    selected = v
                    settings.lastMcVersion = v.id
                }
            }
        }

        // ---------- 底部：所选版本详情 ----------
        selected?.let { v ->
            Surface(
                tonalElevation = 8.dp,
                shadowElevation = 12.dp,
                color = Color.White,
                modifier = Modifier.fillMaxWidth(),
            ) {
                Column(Modifier.padding(14.dp)) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Text(v.id, fontSize = 17.sp, fontWeight = FontWeight.Bold, color = TextMain)
                        Spacer(Modifier.width(6.dp))
                        TypeBadge(v.type)
                        Spacer(Modifier.width(6.dp))
                        Text("大版本 ${v.majorTag}", fontSize = 11.sp, color = TextSub)
                    }

                    Spacer(Modifier.height(10.dp))
                    SectionTitle("模组加载器")
                    Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                        listOf(ModLoader.VANILLA, ModLoader.FORGE, ModLoader.NEOFORGE, ModLoader.FABRIC).forEach { l ->
                            val available = LoaderRepository.isLoaderAvailable(l, v.majorTag, v.type)
                            FilterChip(
                                selected = loader == l,
                                enabled = available,
                                onClick = { if (available) loader = l },
                                label = { Text(l.shortName, fontSize = 11.sp) },
                            )
                        }
                    }
                    if (v.type != VersionType.RELEASE) {
                        Spacer(Modifier.height(4.dp))
                        Text(
                            "快照/远古版本不提供 Forge 等加载器版本",
                            fontSize = 11.sp,
                            color = Color(0xFFB35C00),
                        )
                    }

                    // 加载器版本
                    if (loader.isModded) {
                        Spacer(Modifier.height(8.dp))
                        when {
                            loaderLoading -> Row(verticalAlignment = Alignment.CenterVertically) {
                                CircularProgressIndicator(Modifier.size(14.dp), color = Accent, strokeWidth = 2.dp)
                                Spacer(Modifier.width(8.dp))
                                Text("正在获取 ${loader.shortName} 版本……", fontSize = 12.sp, color = TextSub)
                            }
                            loaderError != null -> Text(
                                "获取失败：$loaderError",
                                fontSize = 12.sp,
                                color = Color(0xFFC92A2A),
                            )
                            else -> {
                                val key = "${v.majorTag}|${loader.name}"
                                val list = loaderCache[key]
                                if (list.isNullOrEmpty()) {
                                    Text("该版本暂无 ${loader.shortName} 版本", fontSize = 12.sp, color = TextSub)
                                } else {
                                    Text("可用 ${loader.shortName} 版本：", fontSize = 12.sp, color = TextSub)
                                    Spacer(Modifier.height(4.dp))
                                    FlowRowSimple(list) {
                                        Text(
                                            "${it.version}${it.note?.let { n -> " ($n)" } ?: ""}",
                                            fontSize = 11.sp,
                                            color = Accent,
                                            modifier = Modifier
                                                .clip(RoundedCornerShape(6.dp))
                                                .background(AccentLight)
                                                .padding(horizontal = 8.dp, vertical = 4.dp),
                                        )
                                    }
                                }
                                Text(
                                    "下载源：" + LoaderRepository.downloadHint(loader, v.majorTag),
                                    fontSize = 10.sp,
                                    color = TextSub,
                                    modifier = Modifier.padding(top = 6.dp),
                                )
                            }
                        }
                    }

                    Spacer(Modifier.height(10.dp))
                    SectionTitle("该组合的已知问题（${bugs.size} 条）")

                    // bug 列表
                    if (bugs.isEmpty()) {
                        Text(
                            "暂无记录。可切换加载器或查看同家族的相邻版本。",
                            fontSize = 12.sp,
                            color = TextSub,
                            modifier = Modifier.padding(vertical = 8.dp),
                        )
                    } else {
                        LazyColumn(Modifier.heightIn(max = 220.dp)) {
                            items(bugs, key = { it.title + it.desc }) { b ->
                                BugCard(b)
                            }
                        }
                    }
                }
            }
        }
    }
}

/** 简化版流式布局（Compose 1.6 的 FlowRow 需要实验 API，这里用分块拼装代替）。 */
@Composable
private fun <T> FlowRowSimple(items: List<T>, content: @Composable (T) -> Unit) {
    Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
        items.chunked(2).forEach { row ->
            Row(horizontalArrangement = Arrangement.spacedBy(4.dp)) {
                row.forEach { content(it) }
            }
        }
    }
}

@Composable
private fun BugCard(b: BugItem) {
    var expanded by remember { mutableStateOf(false) }
    Column(
        Modifier
            .fillMaxWidth()
            .padding(vertical = 3.dp)
            .clip(RoundedCornerShape(8.dp))
            .background(Color(0xFFFAFBFC))
            .padding(10.dp)
    ) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            LevelBadge(b.level)
            Spacer(Modifier.width(6.dp))
            Text(b.title, fontSize = 13.sp, fontWeight = FontWeight.SemiBold, modifier = Modifier.weight(1f))
        }
        Spacer(Modifier.height(4.dp))
        Text(b.desc, fontSize = 12.sp, color = TextSub, lineHeight = 16.sp)

        TextButton(
            onClick = { expanded = !expanded },
            contentPadding = PaddingValues(horizontal = 0.dp, vertical = 2.dp),
            modifier = Modifier.height(26.dp),
        ) {
            Text(if (expanded) "收起" else "原因与解决", fontSize = 11.sp, color = Accent)
        }

        if (expanded) {
            Text("原因", fontSize = 11.sp, fontWeight = FontWeight.SemiBold, color = TextMain)
            Text(b.cause, fontSize = 12.sp, color = TextSub, lineHeight = 16.sp)
            Spacer(Modifier.height(4.dp))
            Text("解决", fontSize = 11.sp, fontWeight = FontWeight.SemiBold, color = TextMain)
            Text(b.fix, fontSize = 12.sp, color = TextSub, lineHeight = 16.sp)
        }
        if (b.loaders.isNotEmpty()) {
            Spacer(Modifier.height(4.dp))
            Row {
                b.loaders.forEach { LoaderBadge(it); Spacer(Modifier.width(4.dp)) }
            }
        }
    }
}

/** 按加载器去在线拉版本列表。 */
private fun fetchLoaderVersions(loader: ModLoader, mcVersion: String): List<LoaderVersion> {
    return when (loader) {
        ModLoader.FORGE -> {
            val urls = listOf(
                "https://bmclapi2.bangbang93.com/forge/promotions",
                "https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json",
            )
            val json = Http.getJson(urls, 12000)
            LoaderRepository.parseForgePromotions(json, mcVersion)
        }
        ModLoader.NEOFORGE -> {
            val urls = listOf(
                "https://bmclapi2.bangbang93.com/maven/net/neoforged/neoforge/maven-metadata.xml",
                "https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml",
            )
            // maven-metadata.xml 是 XML，这里只做简单的 <version> 抽取
            val xml = Http.getJson(urls, 12000)
            parseMavenVersions(xml, mcVersion)
        }
        ModLoader.FABRIC -> {
            val json = Http.getJson(
                listOf("https://bmclapi2.bangbang93.com/fabric-meta/v2/versions/loader"),
                12000,
            )
            LoaderRepository.parseFabric(json)
        }
        else -> emptyList()
    }
}

/** 从 maven-metadata.xml 里抽出与 mcVersion 匹配的 NeoForge 版本。 */
private fun parseMavenVersions(xml: String, mcVersion: String): List<LoaderVersion> {
    val prefix = mcVersion.removePrefix("1.") + "."
    return Regex("<version>([^<]+)</version>")
        .findAll(xml)
        .map { it.groupValues[1] }
        .filter { it.startsWith(prefix) }
        .map {
            LoaderVersion(
                ModLoader.NEOFORGE,
                it,
                mcVersion,
                isRecommended = !it.contains("beta", true),
                note = if (it.contains("beta", true)) "Beta" else "稳定",
            )
        }
        .sortedByDescending { it.version }
        .take(6)
        .toList()
}
