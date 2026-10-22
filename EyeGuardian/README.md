# 护眼精灵 EyeSprite v0.2

Windows 护眼提醒工具（EyesGuard 的同款独立实现）。托盘常驻，按护眼节奏提醒休息，休息时全屏遮罩。

默认节奏：**每 30 分钟短休 1 分钟，每 60 分钟长休 5 分钟**（设置里可改，全部按分钟调节）。

## 安装

双击 `dist\EyeSprite-Setup-0.2.exe` 即装（**无需管理员权限**，不弹 UAC）：

- 安装到 `%LOCALAPPDATA%\Programs\EyeSprite`（`EyeSprite.exe` 145 MB + `Uninstall.exe` 16 KB），自带运行时，目标机不需要装 .NET
- 安装包 60 MB（载荷是 zip 压缩的），但**单文件 exe 刻意不启用程序集压缩**：压缩版会让常驻内存从 100 MB 涨到 227 MB，用磁盘换内存不划算
- 自动创建桌面与开始菜单快捷方式，并在「设置 → 应用」里登记为「护眼精灵 0.2.0」
- 卸载：设置 → 应用 → 护眼精灵 → 卸载，或直接运行安装目录下的 `Uninstall.exe`（统计与个人设置默认保留在 `%LOCALAPPDATA%\EyeSprite`）

## 运行（开发）

```bash
dotnet run                      # 开发调试
dotnet build -c Release         # 产出 bin/Release/net9.0-windows/EyeSprite.exe
dotnet publish -c Release -r win-x64 --self-contained \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
# 单文件 145 MB、免装运行时。不要加 -p:EnableCompressionInSingleFile=true：
# 体积降到 68 MB，但常驻内存会从 100 MB 涨到 227 MB（实测）。
```

需要 .NET 9 SDK；关闭窗口即最小化到托盘，右键托盘图标可暂停 / 立即休息 / 退出。

## 功能

- 短休 / 长休双档计时，**绝对时间点**推算剩余，不漂移，含睡眠唤醒补偿
- 空闲检测（默认关闭，开启后展开原理说明）：`GetLastInputInfo` 读最后一次键鼠输入时间；
  叠加「音频播放 / 前台全屏」不判定（避免看视频被误判）、锁屏立即判定；
  离开满设定时长再回来视为已经休息过，两个周期重新开始，短暂离开则补 60 秒缓冲
- 全屏休息遮罩：每个显示器一个窗口，物理像素铺满，呼吸环 + 倒计时环 + **当前时间**，按屏幕尺寸自动缩放
- 休息画面可**预览**：点概览的圆环或「预览休息画面」，10 秒演示，不写统计
- 强制休息模式（不可关闭）+ 普通模式（Esc / 右键 / 按钮跳过）
- 托盘：进度环图标随状态变色、tooltip 倒计时、右键菜单
- 统计：按天明细、今日四项、近 7 天柱状图、连续达标天数
- 设置：4 个节奏参数（间隔按 1 分钟、长休时长按 5 分钟加减，界面不出现「秒」）、6 个行为开关、16 条提示语管理、界面语言，改动即时生效
- **休息提示语**：16 条原创文案（眨眼 / 远眺 / 提肛 / 呼吸 / 肩颈 / 起身 / 深蹲 / 眼球画 8 字 / 敷眼 / 揉太阳穴 / 腰背 / 手腕 / 喝水 / 走动 / 调焦 / 哈欠），可逐条勾选、就地改写、增删自定义，或一键恢复系统默认
- **多语言**：简体中文 / English，默认跟随系统语言，切换即时生效（XAML 用 DynamicResource，换字典不重建窗口）
- 开机自启（HKCU Run，键缺失时自动创建）、单实例互斥、关机/注销前先落盘
- 低占用：主窗收起时不做界面刷新；托盘图标按 1% 进度量化重绘（静置 CPU 0.23%、工作集约 100 MB）

## 借鉴了什么、优化了什么

**没有拷贝上游任何源码。** 上游 [avestura/EyesGuard](https://github.com/avestura/EyesGuard) 是 F# + Avalonia 的跨平台项目（MIT），本项目是 C# + WPF 的独立实现。

借鉴的只有产品思路与交互：

- 短休 / 长休的双档节奏
- 「强制休息」不可跳过的做法
- 托盘常驻 + 进度环
- 「人不在电脑前就别计时」这个想法
- 统计口径（短休 / 长休 / 跳过 / 用眼时长）

没有借鉴：代码、图标、品牌名、任何一句文案（16 条提示语全部原创；图标由脚本生成）。

相对上游做的优化：

| 方面 | 上游 | 本项目 |
|---|---|---|
| 计时模型 | 每秒 tick 递减，会漂移 | 绝对到期时间点推算，跳变 > 2 分钟直接丢弃 |
| 多显示器 | 只盖主屏 | 每屏一个遮罩窗，按物理像素 `SetWindowPos` |
| 空闲检测 | 单一输入信号，阈值写死，只冻结计时 | 输入 / 音频峰值 / 前台全屏三路叠加 + 锁屏即时；离开够久回来**重新开始周期** |
| 文字清晰度 | 分层窗口灰度抗锯齿 | `WindowChrome` + ClearType |
| 多语言 | 类型提供器，加语言要重编译 | 运行时换字典，不重启 |
| 提示语 | 固定几条 | 16 条可勾选 / 改写 / 增删 / 恢复默认 |
| 资源占用 | 高频重建托盘图标 | 量化重绘 + 隐藏即停刷新 + 复用几何 + 显式释放 COM |
| 交付 | 无安装程序 | 安装器 + 独立卸载器 + 应用列表登记 |

## 目录

```
App.xaml.cs              启动编排 / 语言装载 / 300ms 主循环 / 休息窗与预览管理
Services/GuardEngine.cs  ★ 计时状态机（无 UI 依赖）
Services/TipCatalog.cs   提示语目录（内置条目 / 勾选 / 改写 / 目录升级补入）
Services/Loc.cs          运行时多语言（解析设置 → 换 Lang 字典 → 提供 T/F）
Services/TrayService.cs  NotifyIcon + 运行时绘制图标 + 菜单多语言
Services/IdleDetector.cs 空闲检测
Models/                  设置与统计数据模型
Themes/Styles.xaml       配色与控件样式（绿色主导）
Themes/Lang.zh-CN.xaml   中文文案（全部 UI 字符串）
Themes/Lang.en-US.xaml   英文文案
Views/                   主窗、概览、统计、设置、休息遮罩
```

## 打包安装程序

```bash
cd dist/setup/uninst && dotnet build -c Release    # 独立卸载器 -> Uninstall.exe（16 KB）
# 载荷 = app/EyeSprite.exe + app/Uninstall.exe
cd .. && dotnet build Installer.csproj -c Release    # -> dist/EyeSprite-Setup-0.1.exe
```

安装器是 .NET Framework 4.8 单文件程序，把 `payload.zip` 作为嵌入资源，解压到
`%LOCALAPPDATA%\Programs\EyeSprite`，写 HKCU 卸载项与两处快捷方式。
卸载器不能删掉自己所在的目录，所以先把自身复制到 `%TEMP%` 再由副本清理。

## 加一门语言

1. 复制 `Themes/Lang.en-US.xaml` 为 `Themes/Lang.ja-JP.xaml`，翻译值（key 不要动）
2. 在 `Services/Loc.Apply` 里放宽文化判断，并在设置页加一个 `RadioButton`（Tag 填 `ja-JP`）
3. 不需要改任何 XAML —— 界面文字全部走 `DynamicResource`

## 部署到 GitHub Pages

`docs/` 就是站点根目录，可直接作为 Pages 源：

```bash
git add docs && git commit -m "docs: 护眼精灵 0.2" && git push
# 仓库 Settings → Pages → Source: Deploy from a branch → main / docs
```

- `docs/index.html` 是首页（拆解报告，单文件、无外链、图片全部 base64 内联，
  断网也能打开；1.2 MB）
- `docs/.nojekyll` 关闭 Jekyll 处理，避免文件名或目录被过滤
- 已用本地 HTTP 服务验证：`/`、`/index.html`、`/screenshots/*.png` 均 200

## 数据位置

`%LocalAppData%\EyeSprite\settings.json`、`stats.json`、`app.log`

## 开发期自检

```bash
EyeSprite.exe --shot      # 渲染中文各页面 PNG 到数据目录后自动退出
EyeSprite.exe --shot-en   # 英文界面（截图后还原语言设置）
EyeSprite.exe --probe     # 输出空闲四信号（idle/locked/audio/fullscreen）到 probe.txt
```
