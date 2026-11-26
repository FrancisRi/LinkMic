# Windows 主程序界面

Windows 端采用 WinUI 3 和 Windows App SDK。侧边栏使用原生 `NavigationView`，提供连接与音频、使用指南、运行日志和设置。连接状态、开始接收和断开按钮固定在底部，切换页面时仍可操作。

标题栏、系统菜单、缩放边框和窗口按钮由 Windows 提供。窗口使用 Mica 背景，颜色、对比度和材质回退由系统决定。在「设置 → 应用主题」可选择浅色、深色或跟随 Windows，原有主题偏好继续有效。

窗口默认尺寸为 1080×800，最小尺寸为 880×640，按当前显示器 DPI 换算。小工作区会限制窗口尺寸，页面内容可滚动。

## 构建和验证

运行 `scripts/build.ps1 -Target Windows` 会将 x64 自包含程序输出到 `artifacts/windows-winui/`。完整解压 ZIP 后运行 `LinkMic.exe`，Windows App SDK 运行时随包提供。

运行 `scripts/verify-windows.ps1` 可检查配对存储、输出路由、RNNoise、主题切换及接收和断开。窗口检查覆盖原生框架和系统菜单样式、标题栏深浅色、最小化、最大化、还原，以及最小尺寸下的底部操作按钮。结果与预览写入 `artifacts/tests/`。

[Microsoft 的 WinUI 3 窗口与 Mica 指南](https://learn.microsoft.com/en-us/windows/apps/develop/ui/windows-app-sdk-app-structure)
