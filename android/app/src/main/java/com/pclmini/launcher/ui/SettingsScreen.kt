package com.pclmini.launcher.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
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
import com.pclmini.launcher.data.ModLoader

/** 设置与说明页。 */
@Composable
fun SettingsScreen() {
    val settings = LauncherApp.instance.settings
    var source by remember { mutableStateOf(settings.downloadSource) }
    var releaseOnly by remember { mutableStateOf(settings.releaseOnly) }

    Column(
        Modifier
            .fillMaxSize()
            .background(BgPage)
            .verticalScroll(rememberScrollState())
    ) {
        Column(
            Modifier
                .fillMaxWidth()
                .background(Color.White)
                .padding(16.dp)
        ) {
            Text("设置", fontSize = 19.sp, fontWeight = FontWeight.Bold, color = Accent)
        }

        Spacer(Modifier.height(10.dp))

        Card(
            modifier = Modifier.fillMaxWidth().padding(horizontal = 12.dp),
            colors = CardDefaults.cardColors(containerColor = Color.White),
            shape = RoundedCornerShape(12.dp),
        ) {
            Column(Modifier.padding(14.dp)) {
                Text("下载源", fontSize = 14.sp, fontWeight = FontWeight.SemiBold)
                Spacer(Modifier.height(6.dp))
                listOf(
                    0 to "自动（镜像优先，失败回落官方）",
                    1 to "仅 BMCLAPI 镜像",
                    2 to "仅 Mojang 官方",
                ).forEach { (v, label) ->
                    Row(
                        verticalAlignment = Alignment.CenterVertically,
                        modifier = Modifier.fillMaxWidth().padding(vertical = 2.dp),
                    ) {
                        RadioButton(
                            selected = source == v,
                            onClick = { source = v; settings.downloadSource = v },
                        )
                        Text(label, fontSize = 13.sp)
                    }
                }
            }
        }

        Spacer(Modifier.height(10.dp))

        Card(
            modifier = Modifier.fillMaxWidth().padding(horizontal = 12.dp),
            colors = CardDefaults.cardColors(containerColor = Color.White),
            shape = RoundedCornerShape(12.dp),
        ) {
            Column(Modifier.padding(14.dp)) {
                Text("版本列表", fontSize = 14.sp, fontWeight = FontWeight.SemiBold)
                Spacer(Modifier.height(6.dp))
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Checkbox(
                        checked = releaseOnly,
                        onCheckedChange = { releaseOnly = it; settings.releaseOnly = it },
                    )
                    Text("默认只看正式版（快照仍需手动切换筛选）", fontSize = 13.sp)
                }
            }
        }

        Spacer(Modifier.height(10.dp))

        Card(
            modifier = Modifier.fillMaxWidth().padding(horizontal = 12.dp),
            colors = CardDefaults.cardColors(containerColor = Color.White),
            shape = RoundedCornerShape(12.dp),
        ) {
            Column(Modifier.padding(14.dp)) {
                Text("支持的模组加载器", fontSize = 14.sp, fontWeight = FontWeight.SemiBold)
                Spacer(Modifier.height(8.dp))
                listOf(
                    ModLoader.FORGE to "1.0 起全版本支持，1.20.1 用 47.x，1.21 用 51.x+",
                    ModLoader.NEOFORGE to "1.20.2 起支持，NeoForge 改名后独立分支",
                    ModLoader.FABRIC to "1.14 起支持，需另装 Fabric API",
                    ModLoader.QUILT to "1.14 起支持，基于 Fabric 生态",
                    ModLoader.OPTIFINE to "全版本，但与部分模组及新版渲染管线冲突",
                    ModLoader.LITELOADER to "仅 1.12.2 及以下，已停止维护",
                ).forEach { (l, desc) ->
                    Row(Modifier.padding(vertical = 4.dp)) {
                        LoaderBadge(l)
                        Spacer(Modifier.width(8.dp))
                        Text(desc, fontSize = 12.sp, color = TextSub, lineHeight = 16.sp)
                    }
                }
            }
        }

        Spacer(Modifier.height(10.dp))

        // 按《PCL 分发有限许可》重度使用条款第 3 条：关于页首位给出署名与赞助链接
        Card(
            modifier = Modifier.fillMaxWidth().padding(horizontal = 12.dp),
            colors = CardDefaults.cardColors(containerColor = Color(0xFFE8F1FB)),
            shape = RoundedCornerShape(12.dp),
        ) {
            Column(Modifier.padding(14.dp)) {
                Text("署名与致谢", fontSize = 14.sp, fontWeight = FontWeight.SemiBold)
                Spacer(Modifier.height(6.dp))
                Text(
                    "本软件（PCL-BAI）是第三方基于 PCL（Plain Craft Launcher）独立进行二次创作的产物，" +
                        "并非 PCL 官方出品，与龙腾猫跃及 PCL 官方团队无隶属关系。\n\n" +
                        "原项目：PCL（Plain Craft Launcher）\n" +
                        "原作者：龙腾猫跃\n" +
                        "源码：https://github.com/Meloong-Git/PCL\n" +
                        "赞助链接：https://afdian.com/a/LTCat\n\n" +
                        "如果觉得 PCL 好用，请前往上方链接支持原作者。",
                    fontSize = 12.sp,
                    color = TextMain,
                    lineHeight = 18.sp,
                )
            }
        }

        Spacer(Modifier.height(10.dp))

        Card(
            modifier = Modifier.fillMaxWidth().padding(horizontal = 12.dp),
            colors = CardDefaults.cardColors(containerColor = Color.White),
            shape = RoundedCornerShape(12.dp),
        ) {
            Column(Modifier.padding(14.dp)) {
                Text("关于本应用", fontSize = 14.sp, fontWeight = FontWeight.SemiBold)
                Spacer(Modifier.height(6.dp))
                Text(
                    "这是 PCL-BAI 的手机版界面预览。当前版本只实现「版本浏览 / 正式版与快照区分 / " +
                        "模组加载器版本查询 / 大版本问题详解」四部分，下载与文件校验功能尚未接入。\n\n" +
                        "正式版与快照依据版本清单的 type 字段判断，不靠版本号正则猜测，" +
                        "因此 24w14a、1.21-pre1 这类都能正确归类。",
                    fontSize = 12.sp,
                    color = TextSub,
                    lineHeight = 18.sp,
                )
            }
        }

        Spacer(Modifier.height(10.dp))

        Card(
            modifier = Modifier.fillMaxWidth().padding(horizontal = 12.dp),
            colors = CardDefaults.cardColors(containerColor = Color.White),
            shape = RoundedCornerShape(12.dp),
        ) {
            Column(Modifier.padding(14.dp)) {
                Text("数据来源", fontSize = 14.sp, fontWeight = FontWeight.SemiBold)
                Spacer(Modifier.height(6.dp))
                Text(
                    "版本清单：BMCLAPI 镜像 / Mojang 官方\n" +
                        "Forge：BMCLAPI /forge/promotions\n" +
                        "NeoForge：BMCLAPI maven-metadata\n" +
                        "Fabric：BMCLAPI /fabric-meta\n\n" +
                        "镜像改写规则与桌面版一致，参考 PCL2 源码的下载源实现。",
                    fontSize = 12.sp,
                    color = TextSub,
                    lineHeight = 18.sp,
                )
            }
        }

        Spacer(Modifier.height(24.dp))
    }
}
