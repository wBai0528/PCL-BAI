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
import com.pclmini.launcher.data.*

/** 独立的问题库页面：按版本族分组浏览全部条目，不依赖先选版本。 */
@Composable
fun BugScreen() {
    var typeFilter by remember { mutableStateOf<VersionType?>(null) }
    var loaderFilter by remember { mutableStateOf<ModLoader?>(null) }

    // 把知识库整理成「版本族 → 条目」的展示结构
    val grouped = remember(typeFilter, loaderFilter) {
        buildGroups(typeFilter, loaderFilter)
    }

    Column(Modifier.fillMaxSize().background(BgPage)) {
        Column(
            Modifier
                .fillMaxWidth()
                .background(Color.White)
                .padding(horizontal = 14.dp, vertical = 10.dp)
        ) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text("版本问题库", fontSize = 19.sp, fontWeight = FontWeight.Bold, color = Accent)
                Spacer(Modifier.width(8.dp))
                Text("共 ${BugDatabase.totalCount} 条", fontSize = 11.sp, color = TextSub)
            }
            Spacer(Modifier.height(8.dp))

            Text("按版本类型", fontSize = 11.sp, color = TextSub)
            Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                listOf(
                    null to "全部",
                    VersionType.RELEASE to "正式版",
                    VersionType.SNAPSHOT to "快照",
                    VersionType.OLD_BETA to "远古",
                ).forEach { (t, label) ->
                    FilterChip(
                        selected = typeFilter == t,
                        onClick = { typeFilter = t },
                        label = { Text(label, fontSize = 11.sp) },
                    )
                }
            }
            Spacer(Modifier.height(4.dp))

            Text("按加载器", fontSize = 11.sp, color = TextSub)
            Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                listOf(
                    null to "全部",
                    ModLoader.FORGE to "Forge",
                    ModLoader.NEOFORGE to "NeoForge",
                    ModLoader.FABRIC to "Fabric",
                    ModLoader.OPTIFINE to "OptiFine",
                ).forEach { (l, label) ->
                    FilterChip(
                        selected = loaderFilter == l,
                        onClick = { loaderFilter = l },
                        label = { Text(label, fontSize = 11.sp) },
                    )
                }
            }
        }

        LazyColumn(
            modifier = Modifier.weight(1f).padding(horizontal = 10.dp),
            contentPadding = PaddingValues(vertical = 8.dp),
        ) {
            grouped.forEach { group ->
                item(key = "h-" + group.title) {
                    Row(
                        Modifier.fillMaxWidth().padding(start = 6.dp, top = 12.dp, bottom = 4.dp),
                        verticalAlignment = Alignment.CenterVertically,
                    ) {
                        Text(
                            group.title,
                            fontSize = 13.sp,
                            fontWeight = FontWeight.SemiBold,
                            color = TextMain,
                        )
                        Spacer(Modifier.width(6.dp))
                        Text("${group.items.size} 条", fontSize = 10.sp, color = TextSub)
                    }
                }
                items(group.items, key = { group.title + it.title + it.desc }) { b ->
                    BugItemCard(b)
                }
            }
            item { Spacer(Modifier.height(20.dp)) }
        }
    }
}

private data class BugGroupView(val title: String, val items: List<BugItem>)

private fun buildGroups(typeFilter: VersionType?, loaderFilter: ModLoader?): List<BugGroupView> {
    // 用代表版本去查询，保证分组内的条目和详情页一致
    val releaseTags = listOf(
        "1.21", "1.20", "1.19", "1.18", "1.17", "1.16", "1.14", "1.13", "1.12", "1.8",
    )

    val groups = mutableListOf<BugGroupView>()

    if (typeFilter == null || typeFilter == VersionType.RELEASE) {
        releaseTags.forEach { tag ->
            val items = BugDatabase.query(tag, VersionType.RELEASE, loaderFilter ?: ModLoader.VANILLA)
                .filter { loaderFilter == null || it.loaders.isEmpty() || loaderFilter in it.loaders }
            if (items.isNotEmpty()) groups += BugGroupView("Minecraft $tag 系列", items)
        }
    }

    if (typeFilter == null || typeFilter == VersionType.SNAPSHOT) {
        val items = BugDatabase.query("24w14a", VersionType.SNAPSHOT, loaderFilter ?: ModLoader.VANILLA)
        if (items.isNotEmpty()) groups += BugGroupView("快照版本（通用）", items)
    }

    if (typeFilter == null || typeFilter == VersionType.OLD_BETA) {
        val items = BugDatabase.query("b1.7.3", VersionType.OLD_BETA, loaderFilter ?: ModLoader.VANILLA)
        if (items.isNotEmpty()) groups += BugGroupView("远古版本", items)
    }

    return groups
}

@Composable
private fun BugItemCard(b: BugItem) {
    var expanded by remember { mutableStateOf(false) }
    Column(
        Modifier
            .fillMaxWidth()
            .padding(vertical = 3.dp)
            .clip(RoundedCornerShape(10.dp))
            .background(Color.White)
            .padding(12.dp)
    ) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            LevelBadge(b.level)
            Spacer(Modifier.width(6.dp))
            Text(b.title, fontSize = 14.sp, fontWeight = FontWeight.SemiBold, modifier = Modifier.weight(1f))
        }
        Spacer(Modifier.height(5.dp))
        Text(b.desc, fontSize = 12.sp, color = TextSub, lineHeight = 17.sp)

        if (expanded) {
            Spacer(Modifier.height(8.dp))
            LabeledBlock("原因", b.cause)
            Spacer(Modifier.height(6.dp))
            LabeledBlock("解决", b.fix)
        }

        Spacer(Modifier.height(4.dp))
        Row(verticalAlignment = Alignment.CenterVertically) {
            if (b.loaders.isNotEmpty()) {
                b.loaders.forEach { l -> LoaderBadge(l); Spacer(Modifier.width(4.dp)) }
            } else {
                Text("通用", fontSize = 10.sp, color = Color(0xFF9CA3AF))
            }
            Spacer(Modifier.weight(1f))
            TextButton(
                onClick = { expanded = !expanded },
                contentPadding = PaddingValues(horizontal = 6.dp, vertical = 0.dp),
                modifier = Modifier.height(24.dp),
            ) {
                Text(if (expanded) "收起" else "展开", fontSize = 11.sp, color = Accent)
            }
        }
    }
}

@Composable
private fun LabeledBlock(label: String, text: String) {
    Row {
        Box(
            Modifier
                .width(3.dp)
                .heightIn(min = 16.dp)
                .clip(RoundedCornerShape(2.dp))
                .background(Accent)
        )
        Spacer(Modifier.width(8.dp))
        Column {
            Text(label, fontSize = 11.sp, fontWeight = FontWeight.SemiBold, color = Accent)
            Text(text, fontSize = 12.sp, color = TextMain, lineHeight = 17.sp)
        }
    }
}
