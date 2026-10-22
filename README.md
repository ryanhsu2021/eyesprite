# 护眼精灵 EyeSprite

Windows 护眼提醒工具：托盘常驻，按护眼节奏提醒休息，休息时全屏遮罩。
默认 **每 30 分钟短休 1 分钟、每 60 分钟长休 5 分钟**。

当前版本 **v0.2**（首个带安装程序的版本为 0.1）。

- 下载安装包：[EyeSprite-Setup-0.2.exe](https://github.com/ryanhsu2021/eyesprite/releases/download/v0.2/EyeSprite-Setup-0.2.exe)（60 MB）
- 在线文档：[ryanhsu2021.github.io/eyesprite](https://ryanhsu2021.github.io/eyesprite/)
- 全部版本：[Releases](https://github.com/ryanhsu2021/eyesprite/releases)

![概览](docs/screenshots/shot-overview.png)

## 特性

- 短休 / 长休双档计时，按**绝对时间点**推算剩余，不漂移，含睡眠唤醒补偿
- 全屏休息遮罩：每屏一个窗口、物理像素铺满、呼吸环 + 倒计时环 + 当前时间，可点圆环预览
- 强制休息（不可跳过）或普通模式（Esc / 右键 / 按钮跳过）
- 空闲检测（**默认关闭**，开启后展开原理说明）：输入空闲 + 音频播放 + 前台全屏三路信号，锁屏即时判定；离开够久回来直接开始新一轮
- 16 条原创休息提示语，可逐条勾选、就地改写、增删自定义、一键恢复默认
- 简体中文 / English 运行时切换，默认跟随系统
- 统计：今日四项、近 7 天柱状图、累计与连续达标
- 托盘进度环图标、开机自启、单实例、异常日志

## 安装

双击 `EyeSprite-Setup-0.2.exe`（60 MB，**无需管理员权限**，不弹 UAC）：

- 装到 `%LOCALAPPDATA%\Programs\EyeSprite`，自带 .NET 运行时，目标机不用装 .NET
- 自动建桌面 / 开始菜单快捷方式，登记到「设置 › 应用」，可一键卸载
- 卸载不删设置与统计（`%LOCALAPPDATA%\EyeSprite`），重装即恢复

## 文档

- 拆解报告与同款方案：`docs/index.html`（直接作为 GitHub Pages 首页，单文件、无外链）
- 开发 / 打包 / 加语言：`EyeGuardian/README.md`

## 构建

```bash
cd EyeGuardian
dotnet build -c Release        # bin/Release/net9.0-windows/EyeSprite.exe
dotnet publish -c Release -r win-x64 --self-contained \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

需要 .NET 9 SDK。点关闭即收进托盘，右键托盘图标可暂停 / 立即休息 / 退出。

## 借鉴与致谢

产品思路参考 [EyesGuard](https://github.com/avestura/EyesGuard)（MIT）：短休 / 长休双档节奏、强制休息、
托盘常驻、空闲检测。本项目是独立的 C#/WPF 实现，**未拷贝其任何源码、图标或文案**。
详见 `LICENSE` 与报告第 10 节「借鉴了什么、优化了什么」。

## License

MIT
