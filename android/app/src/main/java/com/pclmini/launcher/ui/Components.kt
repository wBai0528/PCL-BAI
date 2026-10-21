package com.pclmini.launcher.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.pclmini.launcher.data.BugItem
import com.pclmini.launcher.data.BugLevel
import com.pclmini.launcher.data.ModLoader
import com.pclmini.launcher.data.VersionEntry
import com.pclmini.launcher.data.VersionType

/** 主题色。 */
val Accent = Color(0xFF1D6FC4)
val AccentLight = Color(0xFFE8F1FB)
val TextMain = Color(0xFF1F2328)
val TextSub = Color(0xFF6B7280)
val BgPage = Color(0xFFF5F6F8)
val BorderColor = Color(0xFFE1E4E8)

/** 版本类型徽章颜色，正式版与快照必须一眼能分清。 */
@Composable
fun TypeBadge(type: VersionType) {
    val (bg, fg) = when (type) {
        VersionType.RELEASE -> Color(0xFFE6F4EA) to Color(0xFF1E7B34)
        VersionType.SNAPSHOT -> Color(0xFFFFF4E5) to Color(0xFFB35C00)
        VersionType.OLD_BETA -> Color(0xFFEDEBFF) to Color(0xFF4A3AB8)
        VersionType.OLD_ALPHA -> Color(0xFFF0F0F0) to Color(0xFF555555)
        VersionType.UNKNOWN -> Color(0xFFF0F0F0) to Color(0xFF555555)
    }
    Box(
        modifier = Modifier
            .clip(RoundedCornerShape(4.dp))
            .background(bg)
            .padding(horizontal = 6.dp, vertical = 1.dp)
    ) {
        Text(type.label, color = fg, fontSize = 10.sp, fontWeight = FontWeight.Medium)
    }
}

/** 加载器徽章。 */
@Composable
fun LoaderBadge(loader: ModLoader) {
    val (bg, fg) = when (loader) {
        ModLoader.VANILLA -> Color(0xFFF0F2F5) to Color(0xFF6B7280)
        ModLoader.FORGE -> Color(0xFFE3F0FF) to Color(0xFF1B5FA8)
        ModLoader.NEOFORGE -> Color(0xFFFDECE3) to Color(0xFFB34A00)
        ModLoader.FABRIC -> Color(0xFFEAF6F0) to Color(0xFF1A7F4B)
        ModLoader.QUILT -> Color(0xFFF3EAFB) to Color(0xFF6B2FA0)
        else -> Color(0xFFF0F2F5) to Color(0xFF6B7280)
    }
    Box(
        modifier = Modifier
            .clip(RoundedCornerShape(4.dp))
            .background(bg)
            .padding(horizontal = 6.dp, vertical = 1.dp)
    ) {
        Text(loader.shortName, color = fg, fontSize = 10.sp, fontWeight = FontWeight.Medium)
    }
}

/** 严重程度徽章。 */
@Composable
fun LevelBadge(level: BugLevel) {
    Box(
        modifier = Modifier
            .clip(RoundedCornerShape(4.dp))
            .background(Color(level.colorHex))
            .padding(horizontal = 6.dp, vertical = 1.dp)
    ) {
        Text(level.label, color = Color.White, fontSize = 10.sp, fontWeight = FontWeight.Medium)
    }
}

/** 版本列表项。 */
@Composable
fun VersionRow(
    entry: VersionEntry,
    selected: Boolean,
    onClick: () -> Unit,
) {
    Surface(
        onClick = onClick,
        color = if (selected) AccentLight else Color.White,
        shape = RoundedCornerShape(8.dp),
        modifier = Modifier
            .fillMaxWidth()
            .padding(vertical = 2.dp),
    ) {
        Row(
            modifier = Modifier.padding(horizontal = 12.dp, vertical = 10.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Column(Modifier.weight(1f)) {
                Text(
                    entry.id,
                    fontSize = 15.sp,
                    fontWeight = FontWeight.SemiBold,
                    color = if (selected) Accent else TextMain,
                )
                Spacer(Modifier.height(3.dp))
                Row(verticalAlignment = Alignment.CenterVertically) {
                    TypeBadge(entry.type)
                    Spacer(Modifier.width(6.dp))
                    Text(entry.displayDate, fontSize = 11.sp, color = TextSub)
                }
            }
            Box(
                modifier = Modifier
                    .clip(RoundedCornerShape(4.dp))
                    .background(Color(0xFFF1F3F5))
                    .padding(horizontal = 6.dp, vertical = 2.dp)
            ) {
                Text(entry.majorTag, fontSize = 10.sp, color = TextSub)
            }
        }
    }
}

/** 一条 Bug 的列表项。 */
@Composable
fun BugRow(item: BugItem, selected: Boolean, onClick: () -> Unit) {
    Surface(
        onClick = onClick,
        color = if (selected) AccentLight else Color.White,
        shape = RoundedCornerShape(8.dp),
        modifier = Modifier
            .fillMaxWidth()
            .padding(vertical = 2.dp),
    ) {
        Column(Modifier.padding(horizontal = 12.dp, vertical = 10.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                LevelBadge(item.level)
                Spacer(Modifier.width(6.dp))
                Text(
                    item.title,
                    fontSize = 14.sp,
                    fontWeight = FontWeight.SemiBold,
                    color = TextMain,
                    modifier = Modifier.weight(1f),
                )
            }
            Spacer(Modifier.height(4.dp))
            Text(item.desc, fontSize = 12.sp, color = TextSub, lineHeight = 17.sp)
            if (item.loaders.isNotEmpty()) {
                Spacer(Modifier.height(5.dp))
                Row {
                    item.loaders.forEach { l ->
                        LoaderBadge(l)
                        Spacer(Modifier.width(4.dp))
                    }
                }
            }
        }
    }
}

/** 设置项分组标题。 */
@Composable
fun SectionTitle(text: String) {
    Text(
        text,
        fontSize = 13.sp,
        fontWeight = FontWeight.SemiBold,
        color = TextSub,
        modifier = Modifier.padding(horizontal = 4.dp, vertical = 6.dp),
    )
}
