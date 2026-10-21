package com.pclmini.launcher

import android.app.Application
import android.content.Context
import android.content.SharedPreferences

/** 全局配置。 */
class AppSettings(context: Context) {
    private val sp: SharedPreferences =
        context.getSharedPreferences("pclbai", Context.MODE_PRIVATE)

    /** 游戏目录名（Android 侧习惯放在应用私有目录下）。 */
    var gameDirName: String
        get() = sp.getString("gameDir", ".minecraft") ?: ".minecraft"
        set(v) = sp.edit().putString("gameDir", v).apply()

    /** 0 自动（镜像优先）1 仅镜像 2 仅官方。 */
    var downloadSource: Int
        get() = sp.getInt("source", 0)
        set(v) = sp.edit().putInt("source", v).apply()

    var lastMcVersion: String
        get() = sp.getString("lastMc", "") ?: ""
        set(v) = sp.edit().putString("lastMc", v).apply()

    var lastLoader: String
        get() = sp.getString("lastLoader", "VANILLA") ?: "VANILLA"
        set(v) = sp.edit().putString("lastLoader", v).apply()

    /** 是否只看正式版。 */
    var releaseOnly: Boolean
        get() = sp.getBoolean("releaseOnly", false)
        set(v) = sp.edit().putBoolean("releaseOnly", v).apply()
}

class LauncherApp : Application() {
    companion object {
        lateinit var instance: LauncherApp
            private set
    }

    lateinit var settings: AppSettings
        private set

    override fun onCreate() {
        super.onCreate()
        instance = this
        settings = AppSettings(this)
    }
}
