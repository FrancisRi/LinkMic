# Windows 0.2.2 界面

用户确认当前功能已完善，本版继续优化桌面窗口，不要求重装已有 VB-CABLE 或更换 Android APK。
解压 `LinkMic-Windows-0.2.2-win-x64.zip`，关闭旧版 LinkMic 后运行新版即可。主题偏好继续沿用原路径。

## 窗口与视觉

- 保留真正的 Windows 标题栏、系统菜单、最小化/最大化/关闭按钮及可缩放边框；未使用自绘窗口按钮或 `AllowsTransparency` 分层窗口。
- 使用 WPF 原生 Fluent 主题提供的 DWM Mica。移除原先覆盖整个客户区的不透明底色，让侧栏和窗口基底能呈现系统材质；内容区使用原生 Layer / Card 画刷分层。
- 主按钮、导航指示和原生控件跟随 Windows 强调色；文本、边框、高对比度和材质回退由框架资源提供，移除手工维护的固定主题调色板。
- 使用 Segoe UI Variable / Segoe UI 与中文字体回退、Segoe Fluent Icons，配套多尺寸 Windows 应用图标。图标源生成脚本为 `scripts/make-icon.ps1`，无需图片生成或外部运行时。
- 收紧卡片内边距和文字层级，移除大幅宣传区；手动蓝牙地址收进展开面板，常用设备选择保持可见。
- 左侧增加独立设置页；主题选项移至“设置”→“应用主题”。开始/断开和连接状态固定在底部，切换页面和滚动时仍可操作。
- 维持 PerMonitorV2 DPI 感知、布局像素对齐和系统窗口尺寸限制。默认 1080×800，最小 880×640；小工作区会按原逻辑收缩，内容自动滚动。

这是 WPF 的原生 Fluent 桌面实现，没有迁移到 WinUI 3；无需额外安装 Windows App SDK Runtime。
Mica 的实际壁纸色调、激活态、节能状态和材质回退由 Windows 决定，截图不保证在另一台电脑上有完全相同的颜色。

## 验证

运行 `scripts/verify-windows.ps1` 可复现构建、输出路由、主题及接收/断开检查。
本次 Release 构建零警告零错误，以下检查通过：

- 实际 HWND 的标题栏、缩放、最小化/最大化样式位保留，`AllowsTransparency=false`。
- 浅色与深色下 `DWMWA_SYSTEMBACKDROP_TYPE=2`（Mica），标题栏深色属性分别为 0 / 1，DWM 查询均成功。
- 实际窗口最大化→还原、四个导航页切换、880×640 下底部操作按钮不被裁切。
- 实际窗口截图 `artifacts/tests/windows11-light-window.png`、`windows11-dark-window.png` 和 `windows11-compact-window.png` 已检查；默认尺寸主要控件完整可见，紧凑窗口可滚动。
- 内容层深浅色预览及配对码文字对比度检查通过；透明文字按背景合成后，浅色约 15.15:1，深色约 17.04:1。
- 13 项输出路由检查、8 项真实 WPF 接收/断开检查与蓝牙库重复释放回归通过。接收期间进入设置、切换主题、返回连接页并断开，输出资源仍只释放一次。
- 真实 Java 编码器的 65 帧音频经 Windows Receiver 接收，接收期间切换主题仍通过；硬件输出由捕获测试接收器替代，未启动 ADB。

机器可读记录：`artifacts/tests/native-window-results.json`、`theme-results.json`、`ui-disconnect-results.json`、`output-routing-results.json`。
`PrintWindow` 只捕获本应用窗口，未截取用户整块桌面；截图可能使用中性色材质，不能证明壁纸取色。
未手动验证最大化按钮悬停的 Snap 布局浮层、跨物理显示器 DPI 切换，以及全局高对比度/强调色设置变化；本次没有为了测试更改这些全局设置。

## 技术依据

- [微软 WPF Fluent 主题与系统背景说明](https://github.com/dotnet/wpf/blob/main/Documentation/docs/using-fluent.md)
- [Windows 标题栏设计](https://learn.microsoft.com/en-us/windows/apps/design/basics/titlebar-design)
- [Windows 材质说明](https://learn.microsoft.com/en-us/windows/apps/develop/ui/materials)
- [DWM 窗口属性](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute)
