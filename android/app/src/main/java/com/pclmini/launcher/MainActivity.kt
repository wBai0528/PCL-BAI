package com.pclmini.launcher

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.foundation.layout.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.pclmini.launcher.ui.*

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContent {
            MaterialTheme(
                colorScheme = lightColorScheme(
                    primary = Accent,
                    onPrimary = Color.White,
                    primaryContainer = AccentLight,
                    background = BgPage,
                    surface = Color.White,
                )
            ) {
                MainScaffold()
            }
        }
    }
}

private enum class Tab(val label: String) {
    VERSION("版本"),
    BUGS("问题库"),
    SETTINGS("设置"),
}

@Composable
private fun MainScaffold() {
    var tab by remember { mutableStateOf(Tab.VERSION) }

    Scaffold(
        containerColor = BgPage,
        bottomBar = {
            NavigationBar(containerColor = Color.White, tonalElevation = 3.dp) {
                Tab.entries.forEach { t ->
                    NavigationBarItem(
                        selected = tab == t,
                        onClick = { tab = t },
                        icon = {
                            Text(
                                when (t) {
                                    Tab.VERSION -> "▦"
                                    Tab.BUGS -> "⚠"
                                    Tab.SETTINGS -> "⚙"
                                },
                                fontSize = 18.sp,
                                fontWeight = FontWeight.Bold,
                            )
                        },
                        label = { Text(t.label, fontSize = 11.sp) },
                    )
                }
            }
        }
    ) { inner ->
        Box(Modifier.padding(inner)) {
            when (tab) {
                Tab.VERSION -> VersionScreen()
                Tab.BUGS -> BugScreen()
                Tab.SETTINGS -> SettingsScreen()
            }
        }
    }
}
