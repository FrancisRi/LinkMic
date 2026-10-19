# LinkMic 虚拟麦克风测试包 0.2.0-test.2

用于之前那台 Windows 11 测试电脑，要求 build 22621+、x64。
本包的 SYS 和 CAT 使用本项目开发证书测试签名，**不是微软正式签名**。
私钥和 PFX 不随包分发。构建电脑没有安装证书、驱动或更改启动设置。

## 第一步：只读检查

解压到测试电脑的独立普通本地文件夹（不要覆盖旧包的安装状态），打开**管理员 PowerShell**，进入该文件夹执行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Check-Environment.ps1
```

这个 ExecutionPolicy 参数仅作用于该 PowerShell 子进程，不改系统策略。
检查脚本只读取环境并在 `results/environment.json` 保存报告，不修改启动项、证书或驱动。
报告包含 `isAdministrator`、PowerShell 版本、固件类型和查询失败原因。
如果 Secure Boot 等字段仍为 null，保留 `secureBootDetails` / `bitLockerDetails` 的错误信息，不将未知状态视为关闭。
BitLocker 的保护状态 Off 不等于磁盘没有加密；本检查不会导出恢复密钥。
把 `environment.json` 返回给开发 agent，再确定后续操作。

test.2 修复 Windows PowerShell 5.1 空设备列表被误判为已有设备的问题；驱动源码和开发证书与 test.1 相同。
签名核验脚本也已通过 PowerShell 5.1 和 7 的兼容性验证。
如果旧包已经执行过安装，应继续使用旧目录的回滚脚本和 `test-state.json`，不要混用安装状态。

## 第二步：确认测试启动环境

默认 Windows 不加载这种开发签名驱动。需要在测试机启用 TESTSIGNING，并重启才生效。
若 Secure Boot 开启，可能需要在固件中调整；BitLocker/设备加密也可能要求恢复密钥。
这些会影响测试电脑的启动与安全配置，**此包不会自动处理**，请先审阅环境报告并确认测试方式。
也可在可回滚的专用测试系统中完成，代价是蓝牙设备连接可能需要额外配置。

在明确决定采用开发测试模式、已经处理 Secure Boot 和恢复密钥准备后，管理员手动执行：

```powershell
bcdedit /set testsigning on
```

重启测试电脑，然后重新运行只读检查，确认 `testMode=true`。
不要把内存完整性当作默认关闭项；本包同时签了 SYS，HVCI 是否兼容需保留状态进行验证。
如果策略阻止操作，保留错误信息，不通过删除 Code Integrity 策略等方式绕过。

## 第三步：明确执行安装

关闭旧 LinkMic 客户端。在管理员 PowerShell 先查看安装计划：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install-TestDriver.ps1
```

确认后执行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install-TestDriver.ps1 -Apply
```

只导入包内这一张测试公钥证书至 LocalMachine Root / TrustedPublisher，
创建一个 `ROOT\LinkMicCapture` 设备并安装对应驱动。不会修改其他麦克风或现有 WO Mic 驱动。
创建前保留 `test-state.json`，回滚时用于保留原先已有的证书信任。
已有未被本包记录的 LinkMic 设备时会拒绝覆盖，避免丢失已有安装。
安装不会自动重启；如返回 `rebootRequired=true`，手动重启后再测试。

## 第四步：不依赖手机的闭环音频测试

保持 LinkMic 桌面应用关闭，运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Run-AudioTest.ps1 -Mode Tone
```

工具通过自有控制通道写入 3 秒 440Hz 合成音，经真实 WASAPI 的 LinkMic 录音端点捕获 4 秒。
检查音调相关性和停止写入后补静音，保存 `results/tone.wav` 与 `tone-result.json`。
不向扬声器播放，也不会回退到默认麦克风。缺少或有多个 LinkMic 端点时直接报错。
测试通过表示控制写入→内核缓冲→系统录音路径通过本次检查，不代表所有内核压力测试通过。

## 第五步：手机蓝牙链路

打开 Windows 0.2.0 LinkMic，手机仍使用原 Android APK。蓝牙连接后选择 `LinkMic Virtual Microphone`。
手机端开始录音并对着手机讲话，再运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Run-AudioTest.ps1 -Mode Phone
```

这会明确录制虚拟端点 8 秒，保存 `capture.wav`，检测是否有非静音音频；请试听确认是手机声音。
随后在 Windows 录音机或会议软件选择同一端点，测试停止/重连、静音、先连接后开始录音和关闭录音再打开。

## 诊断与回滚

失败时保留 `results`、`test-state.json` 和故障电脑 `%LOCALAPPDATA%\LinkMic\logs\linkmic.log`。
安装错误还可提供 `C:\Windows\INF\setupapi.dev.log` 的对应时间段；不需要导出 BitLocker 密钥。

关闭 LinkMic 和所有录音工具，管理员先查看回滚计划，再按需要执行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Remove-TestDriver.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Remove-TestDriver.ps1 -Apply
```

只移除本包记录的 LinkMic root 设备及本包新加入的证书信任；保留已有证书和 Driver Store。
`environment.json` 记录设备使用的 `oemNN.inf`，若要清理 Driver Store，先确认该条目属于 LinkMic 再用 PnPUtil 单独删除。
如测试前 TESTSIGNING 为关闭，设备移除后手动 `bcdedit /set testsigning off` 并重启。
若测试期间调整过固件或加密保护，按测试前记录恢复。此包不自动改变它们。

依据：[微软测试签名说明](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/the-testsigning-boot-configuration-option)、
[WASAPI 捕获接口](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudiocaptureclient-getbuffer)。
驱动源码和微软样例 MS-PL 许可见上一版完整驱动源码包；本包附带同一许可证，新增测试工具和构建脚本源码在 `source/`。
这些构建脚本用于完整工程目录布局，重建时将它们放回 `drivers/scripts`，工具源码放回 `drivers/testkit`；便携 SDK/编译器不随包分发。
