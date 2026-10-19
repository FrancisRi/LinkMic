# LinkMic：使用官方签名 VB-CABLE

本版采用与 MicYou Windows 版相同的虚拟音频线缆方案：VB-CABLE。
当前配套 Windows 0.3.3 和 Android 0.2.3；新版电脑降噪见 [RNNoise 说明](RNNOISE.md)，已安装驱动无需重装。名称列表与选择遗忘见 [更新说明](PAIRED-DEVICES.md)，安卓语言及按钮对齐见 [Android 0.2.3](ANDROID-0.2.3.md)。
音频经现有 Windows 标准播放接口送入 CABLE Input，再由 CABLE Output 录音端提供给会议软件。
桌面端的 Fluent 2、跟随系统深浅色及蓝牙断开修复继续保留。

## 在之前的 Windows 11 测试电脑安装

1. 解压整个当前 Windows 发布 ZIP（当前为 `LinkMic-Windows-0.3.3-win-x64.zip`），关闭正在运行的旧 LinkMic。
2. 在 `drivers/vb-cable` 中，**另外解压原版** `VBCABLE_Driver_Pack45.zip` 到独立文件夹。
3. 阅读原版 `readme.txt`，右键 `VBCABLE_Setup_x64.exe` → 以管理员身份运行，点击 Install Driver。
4. 按原厂要求重启电脑，再打开新版 `LinkMic.exe`。
5. LinkMic 选择 `CABLE Input · VB-CABLE 虚拟麦克风`。安装后才启动 LinkMic 时会优先选中它；应用已打开则点击“刷新设备”后选择。
6. 按原来的方法通过蓝牙连接手机。在 Windows 录音机或会议软件中，选择 `CABLE Output (VB-Audio Virtual Cable)` 作为麦克风，对着手机讲话。

名称中的 Input / Output 是相对于音频线缆：LinkMic 写入 Input，会议软件读取 Output。
请选普通 `CABLE Input`，不需要选另外的 `CABLE In 16 Ch`。
不用更改 Windows 默认播放设备；其他程序的声音不会因此全部进入麦克风。

本包选用 Pack45 内的 Windows 10/11 x64 驱动 3.3.1.7。
`vbaudio_cable64_win10.cat` 的签名人为 Microsoft Windows Hardware Compatibility Publisher；
驱动 SYS 及对应 INF 的目录成员验证、SignTool `/kp` 内核策略验证通过，安装程序 `/pa` 验证通过。
签名核验记录和 SHA-256 在同目录中。
**这条方案不要求启用 TESTSIGNING、导入开发证书或关闭 Secure Boot。**
实际安装和端到端录音仍需在测试电脑验证；微软签名验证不等于已完成这台电脑上的音频测试。

## 当前测试启动设置

用户最新报告确认 Secure Boot 开启、testMode=false、系统卷 FullyDecrypted，且没有 LinkMic 自有驱动及开发证书信任。
按该状态可直接使用官方安装器，无需先执行之前 test.2 文档里的测试启动设置。

如果你在报告之后已经改过设置，先按原 test.2 文档完成已安装实验驱动和新增证书的回滚；
随后在管理员 PowerShell 关闭测试模式并重启，再在 UEFI 中恢复之前的 Secure Boot 设置。

```powershell
bcdedit /set testsigning off
```

此命令仅用于已启用过测试模式的情况；LinkMic 不自动执行。

## 复测和问题反馈

先验证蓝牙连接、讲话录音和断开，再验证重连、静音，以及关闭录音软件后重新打开。
仅升级电脑界面时驱动可沿用；使用免码重连和 Material 3 界面时手机需更新至 Android 0.2.0。需要 Wi-Fi 时再按使用指南切换连接方式。
声音应来自手机，停止手机录音后应安静。普通扬声器选项仍仅用于监听。

如果 LinkMic 没看到 CABLE Input，确认安装后已重启，再刷新设备；
如果设备被手动改名，可能不会自动识别，仍可在列表手动选择实际播放端。
接收音量条有活动但会议软件无声音时，确认会议软件选择的是 CABLE Output，且允许麦克风访问。

可在 PowerShell 生成只读设备列表（不录音、不播放、不安装）：

```powershell
Start-Process -FilePath .\LinkMic.exe -ArgumentList '--audio-devices', 'audio-devices.json' -Wait
```

提供 `audio-devices.json`、错误文字，以及 `%LOCALAPPDATA%\LinkMic\logs\linkmic.log` 即可。
移除 VB-CABLE 时，关闭音频应用，使用同一原版安装器的 Remove Driver 功能，然后重启。

## 原作者与许可证

VB-CABLE 由 VB-Audio / Vincent Burel 提供，不是 LinkMic 自有驱动。
来源：<https://vb-audio.com/Cable/> / <https://www.vb-cable.com/>。
**VB-CABLE 是 donationware，欢迎向原作者捐赠或购买许可证：<https://shop.vb-audio.com/>。**
原版 readme 允许原样复制和分发，要求保留来源与 donationware 提示；
本项目随包提供未修改的原版 ZIP，保留原名、签名和许可，由用户单独执行原厂安装器。
不替换 INF 中的名称、不重签 SYS/CAT、不将该包纳入自动或静默安装流程。
如果后续面向无法自行捐赠/付费的组织分发，应按原厂条款处理许可证。
原版 ZIP SHA-256：`b950e39f01af1d04ea623c8f6d8eb9b6ea5c477c637295fabf20631c85116bfb`。

选型核查：[Virtual-Audio-Driver](https://github.com/VirtualDrivers/Virtual-Audio-Driver) 当前 README 仍注明需测试签名，且未公开 Releases；
[MicYou](https://github.com/MicYou-Dev/MicYou) 在 Windows 上使用 VB-CABLE，本项目独立接入标准 Windows 音频接口，没有复制 MicYou 的 GPL 源码。
原厂说明：[VB-CABLE](https://vb-audio.com/Cable/)、[许可与分发](https://vb-audio.com/Services/licensing.htm)。
