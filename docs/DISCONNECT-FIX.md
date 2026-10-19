# 蓝牙断开闪退：复现、修复与验证

用户实测：另一台 Windows 11 电脑通过蓝牙连接正常，点击断开后应用闪退；Wi-Fi 未实测。WO Mic 输出显示“启用虚拟麦克风失败”，扬声器输出可以正常播放手机声音。

## 已复现的根因

对当前使用的 InTheHand.Net.Bluetooth 4.2.5 进行本机探针测试，不连接手机、不录音、不播放声音：

- 同一 BluetoothClient 第二次 Dispose 触发 NullReferenceException。
- 模拟生产代码的父 CancellationToken + linked timeout token，两条取消回调都 Dispose 同一 BluetoothClient；Cancel 因回调异常抛 AggregateException。

原 Receiver 同时在父 token 回调、超时 token 回调和最终 Dispose 中释放蓝牙连接。UI 的断开按钮直接调用 Receiver.Dispose，没有捕获取消回调异常，因而会成为 WPF 未处理异常。该失败模式与用户的蓝牙断开闪退吻合；没有取得另一台电脑的原始崩溃堆栈，不能声称已核对该进程的历史异常事件。

失败证据：`artifacts/tests/disconnect-baseline.json`，两个场景均失败。

## 修复

- 一个 DisposeOnce 所有权对象供取消、超时和最终清理共同使用，Interlocked.Exchange 保证底层连接仅释放一次。
- RequestStop 幂等；清理错误记录日志，不从断开按钮抛出。
- 生产接收循环始终执行传输清理，避免在停止请求与连接初始化交错时留下监听端口。
- WaveOut 和新的虚拟驱动输出也采用幂等释放。
- UI 会话错误、清理错误、Win32 错误码和未处理异常记录到 `%LOCALAPPDATA%\LinkMic\logs\linkmic.log`。不会记录音频或配对码。

## 回归

同一个真实蓝牙库探针使用生产 DisposeOnce 后，两项失败转为通过。证据：`artifacts/tests/disconnect-after.json`。

WPF 回归直接触发实际按钮和关闭事件，使用真实 Receiver、TCP loopback 和替代物理音频输出的计数接收器，覆盖：等待连接、配对中、播放中、对端断开、关闭窗口、启动前停止及缺失自有驱动。重复点击停止仍只释放一次，捕获未处理 Dispatcher 异常数为零。证据：`artifacts/tests/ui-disconnect-results.json`。

用户随后确认，在另一台原故障 Windows 11 电脑使用 0.2.0 经蓝牙和扬声器复测，断开已不再闪退。正常 phone-wire 协议没有变化，现有 Android 0.1.0 APK 可以继续配合 Windows 0.2.0 使用。

## 实机复测

1. 退出旧 Windows 客户端，用 0.2.0 通过蓝牙连接手机，先选择扬声器或现有虚拟音频线缆。
2. 等待有声音后断开；重复连接/断开，并尝试手机主动停止、电脑关闭窗口。
3. 如仍有异常，提供 `linkmic.log` 中对应时间段及故障电脑的 Windows 版本。该日志位于故障电脑，当前开发电脑没有它。

自有驱动尚未安装或没有完成签名时，选择它会显示明确的打开设备错误；这不代表蓝牙连接发生错误。
