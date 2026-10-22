# PCL-BAI

基于 [PCL（Plain Craft Launcher）](https://github.com/Meloong-Git/PCL) 的第三方二次创作启动器。

本仓库公开 PCL-BAI 的全部源代码，以遵守《PCL 存储库合理使用指南》重度使用条款第 3 条关于公开源代码的要求。

---

## 功能

| 功能 | Windows | Android |
| --- | --- | --- |
| 版本浏览（正式版 / 快照 / 远古版本区分） | ✅ | ✅ |
| 下载游戏文件（客户端 jar / 支持库 / 资源） | ✅ | ❌ |
| 验证游戏文件（SHA1 + 大小） | ✅ | ❌ |
| 模组加载器版本查询（Forge / NeoForge / Fabric） | ✅ | ✅ |
| Forge / NeoForge 自动安装 | ✅ | ❌ |
| 大版本 Bug 详解 | ✅ | ✅ |
| 启动游戏本体 | ❌ | ❌ |

**不登录账号、不启动游戏**（那部分目前没做）。

---

## 关于许可

本软件是**第三方基于 PCL 独立进行二次创作的产物，并非 PCL 官方出品**，与龙腾猫跃及 PCL 官方团队无隶属、合作或授权关系。

依《PCL 存储库合理使用指南》重度使用条款：

| 条款 | 落实情况 |
| --- | --- |
| 1. 第三方二次创作声明 | 见本文及软件「关于与许可」页 |
| 2. 名称以 Plain Craft Launcher (PCL) 开头 + 第三方后缀 | `PCL-BAI`，`-BAI` 表明第三方修改 |
| 3. 关于页首位署名 + 赞助链接 + **公开源代码** + 沿用本许可 | 已内置；源码即本仓库 |
| 4. McLaunchPrecheck 劝导逻辑 | 本软件**不含游戏启动功能**，不涉及 |
| 5. 无赞助解锁类似功能，主色相为蓝色 | 界面主色 `#1D6FC4`（蓝色），无任何付费解锁 |

- 原作者：**龙腾猫跃**
- 原项目：https://github.com/Meloong-Git/PCL
- 赞助原作者：https://afdian.com/a/LTCat
- 《PCL 分发有限许可》：https://shimo.im/docs/rGrd8pY8xWkt6ryW

完整条款见 [LICENCE](LICENCE)。

---

## 仓库结构

```
PCL-BAI/
├── LICENCE                    许可条款
├── 安卓apk
├── 引导程序
├── README.md
├── windows/
│   ├── PCLBAI/                 主程序（WPF, .NET 8）
│   │   ├── App.xaml
│   │   ├── MainWindow.xaml     三栏界面：版本列表 / 加载器 / 验证 / Bug 详解
│   │   ├── SelfTest.cs         无人值守自检
│   │   ├── UiSnapshot.cs       界面截图工具
│   │   ├── Models/
│   │   │   └── VersionModels.cs
│   │   └── Services/
│   │       ├── AppSettings.cs
│   │       ├── DownloadSource.cs    镜像改写规则 + Http 客户端
│   │       ├── VersionParser.cs     版本 Json 解析
│   │       ├── GameInstaller.cs     并发下载与 SHA1 校验
│   │       ├── LoaderService.cs     加载器查询 + Forge/NeoForge 安装
│   │       └── BugDatabase.cs       问题库
│   └── PCLBAIBoot/             引导器（自包含 + 裁剪，约 11 MB）
│       ├── Program.cs          下载运行时 / 解压 / 启动
│       └── ProgressWindow.cs   纯 Win32 进度窗口
└── android/
    └── app/src/main/java/com/pclmini/launcher/
        ├── MainActivity.kt
        ├── LauncherApp.kt
        ├── data/               版本清单 / 加载器 / 问题库
        └── ui/                 版本页 / 问题库页 / 设置页 / 组件
```

---

## 构建

### Windows

需要 .NET 8 SDK（Windows）。

```powershell
# 主程序（依赖框架构建，约 150 KB）
dotnet publish windows/PCLBAI/PCLBAI.csproj -c Release -r win-x64 `
    --self-contained false -p:UseAppHost=true -o dist/pkg

# 打包成引导器的内嵌资源
Compress-Archive -Path dist/pkg/* -DestinationPath dist/app.zip -Force

# 引导器（自包含 + 裁剪，约 11 MB）
dotnet publish windows/PCLBAIBoot/PCLBAIBoot.csproj -c Release `
    -p:AppPayload="<app.zip 的绝对路径>" -o dist/boot
```

产物：`dist/boot/PCLBAI.exe`

> 引导器不含 WinForms / WPF 依赖，因此可以 IL 裁剪（`NETSDK1175` 不会触发）。
> 主程序是 WPF，无法裁剪，这也是为什么把它剥离成 framework-dependent 构建。

### Android

需要 JDK 17 + Android SDK 34。

```powershell
cd android
./gradlew :app:assembleDebug
```

产物：`android/app/build/outputs/apk/debug/app-debug.apk`

---

## 自检与截图

```powershell
# 真实联网自检：版本清单 / 类型区分 / Json 解析 / 下载 / SHA1 / 加载器 / 问题库
dotnet build windows/PCLBAI/PCLBAI.csproj -c Release -p:SelfTest=true -o selftest
dotnet selftest/PCLBAI.dll

# 界面截图（无桌面会话也可用）
dotnet selftest/PCLBAI.dll --snapshot ui.png
```

---

## 实现要点

### 正式版与快照的区分

依据版本清单的 `type` 字段判断，**不靠版本号正则猜**：

- `1.21.4` → release（正式版）
- `24w14a` → snapshot（快照）
- `1.20.1-rc1` → snapshot（官方清单里 `type` 就是 snapshot，不是判断错误）
- `26.3` → release

`GetMajorTag` 用通用分段解析而非「是否以 `1.` 开头」，因为版本号已经到 `26.x` 了。

### 加载器只跟进正式版

选中快照时加载器选项会置灰并提示原因，不会给出错误组合。

### 镜像改写

参考 PCL2 源码的下载源实现：

| 资源 | 官方 | 镜像 |
| --- | --- | --- |
| Assets | `resources.download.minecraft.net` | `bmclapi2.bangbang93.com/assets` |
| Libraries | `libraries.minecraft.net` | `/maven`，其次 `/libraries` |
| 版本 Json / client jar | `piston-meta.mojang.com` | `bmclapi2.bangbang93.com` |

### 引导器为什么需要下载 Desktop Runtime

主程序是 WPF 应用，依赖 `Microsoft.WindowsDesktop.App`。若误下基础版 `Microsoft.NETCore.App`，症状是**进程能起来、响应正常，但窗口空白、内存只有 19 MB**。

---

## 已知限制

- 首次运行必须联网下载 35 MB 的 .NET Windows Desktop Runtime
- Fabric / Quilt / OptiFine 只查版本，不自动安装
- 问题库为人工整理，不保证覆盖每个小版本
- 安卓版不含下载与校验功能

---

## 致谢

- [Meloong-Git/PCL](https://github.com/Meloong-Git/PCL) —— 原项目，下载源改写规则与版本解析思路参考其实现
- [BMCLAPI](https://bmclapi2.bangbang93.com/) —— 国内镜像服务
- [bangbang93](https://github.com/bangbang93) —— BMCLAPI 作者
