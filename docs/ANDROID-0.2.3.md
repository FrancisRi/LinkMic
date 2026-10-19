# Android 0.2.3：按钮对齐与中英文

覆盖安装 `artifacts/LinkMic-Android-0.2.3-debug.apk`，无需卸载旧版。包名和签名保持一致，保留已记住电脑、设备身份及连接偏好。Windows 继续使用 0.3.2，驱动无需重装。

## 按钮对齐

Android 15 的原生控件测试复现中文“蓝牙”和拉丁文字“Wi-Fi”的文字基线相差 1px，父布局默认基线对齐使 Wi-Fi 按钮整体下移 1px。修复关闭 MaterialButtonToggleGroup 的基线对齐，让两个按钮共享父布局测量后的高度，保留字体放大时的自适应尺寸。

SDK 28 和 35 分别检查四种字体、14/18/22sp 三种字号，连续切换蓝牙 → Wi-Fi → 蓝牙，并检查界面重建后的上下边界一致、触摸高度至少 48dp。英文按钮内边距也已收紧，保证 360dp 小屏、1.3 倍字体下完整显示 Bluetooth。测量记录见 `artifacts/tests/android-mode-button-bounds-sdk28.txt` 和 `android-mode-button-bounds-sdk35.txt`。

## 跟随系统语言

手机系统语言列表的第一项为中文时使用中文，包括不同中文地区；第一项为其他任何语言时使用英文。即使第二语言是中文，也保持英文。中文资源目前为简体中文，没有应用内语言开关。

首页、提示、设备管理、配对/连接错误、前台录音通知及通知操作均已翻译。电脑的真实名称保持原样。切换系统语言时，界面按系统配置重建，保留输入和连接方式；运行中的服务保留连接起点，状态和通知重新翻译。应用只调整自身语言上下文，不修改手机语言设置。

连接状态使用资源标识，配对错误使用稳定的错误代码。业务逻辑不再通过中文文案判断连接成功或需要配对。中英文均随 APK 打包，未来构建 App Bundle 也不拆分语言资源，以免切换时缺少翻译。

## 验证与复测

27 项 Android 自动测试覆盖语言选择、中文地区、多语言优先级、阿拉伯语系统下的英文从左到右布局、活动会话的换语与状态保留、通知换语、错误翻译，以及既有设备管理、计时、旋转、小屏、深浅色、图标和按钮对齐。另执行 Java 客户端与 .NET 服务端的真实 TCP/配对回环，音频格式与 v2/v3 握手兼容检查通过。

预览来自实际 Android 控件渲染：

- 英文浅色：`artifacts/tests/android023-english-light.png`
- 英文深色：`artifacts/tests/android023-english-dark.png`
- 中文浅色：`artifacts/tests/android023-chinese-light.png`
- 小屏放大字体：`artifacts/tests/android-material3-compact-wifi.png`

Lint 与 APK 签名由 `scripts/verify-android.ps1` 检查，交付校验见 `artifacts/tests/release-032-android023-verification.json`。本轮没有启动 ADB 或操作物理手机；实际蓝牙录音和系统语言切换仍请在原手机复测，既有两小时实机结果不计作本版实机验证。

建议覆盖安装后切换蓝牙 / Wi-Fi，确认两段平齐及 IP 输入框按模式隐藏；再将系统首选语言改为英语或其他非中文语言，确认页面、设备菜单和通知为英文，改回中文后同步恢复。已经记住的电脑应继续免配对码连接。
