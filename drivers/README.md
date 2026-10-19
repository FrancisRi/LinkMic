# LinkMic Virtual Microphone 驱动开发包

这里是真实的 Windows PortCls / WaveRT 捕获驱动源码，不是仅创建一个控制设备的占位实现。
基于微软 MS-PL 许可的 SimpleAudioSample 音频框架，移除扬声器端点注册与正弦测试音源，
以 LinkMic 原创的用户态 PCM 输入设备和有界非分页环缓冲取代测试音源。
没有使用 WO Mic 的驱动二进制、代码、设备名或 XOR 握手协议。

当前产物是 **未签名的开发构建**。已实际编译出 `.sys`，通过官方 INF 和签名准备检查，
但没有安装、加载、打开测试签名、修改 Secure Boot、安装证书或重启电脑。
编译和 INF 检查不能代替内核安全、WASAPI 录音与真实设备验证。

## 产物与支持范围

- `out/x64/LinkMicCapture.sys`：x64 内核驱动。
- `out/x64/LinkMicCapture.inf`：硬件 ID `ROOT\LinkMicCapture`，显示名称 `LinkMic Virtual Microphone`。
- `out/x64/LinkMicCapture.cat`：已生成但未签名的目录文件。
- `out/x64/sha256.json`：当前构建哈希。
- `include/LinkMicAbi.h`：公开的独立输入协议。
- `src/wavert/`：修改后的微软音频框架；`src/LinkMicControl.cpp` 和 `src/LinkMicRing.h` 是原创输入实现。

此开发包保守限定 **Windows 11 22H2 / build 22621 及以上的 x64 系统**，使用系统内置 KMDF 1.33。
KMDF 1.33 本身从 Windows 11 21H2 起已内置；本包尚未验证 21H2，因此 INF 不包含该版本。
Windows 10、ARM64、多实例安装不在当前构建范围内。

## 工作机制

Windows 的音频服务经 KS / WaveRT 看到一个 48,000 Hz、单声道、有符号 PCM16 捕获端点。
普通录音程序和会议软件从这个端点录音；Windows 自身的共享模式音频引擎负责应用需要的格式转换。
驱动只声明固定的原始格式与 Default / Raw 两种处理模式，不在内核中解码网络协议或做降噪。
手机音频先由 LinkMic 电脑端解码，随后每 20 ms 写入一帧 1,920 bytes PCM。

输入控制设备路径 `\\.\LinkMicCapture`，一个独占写者。
5 帧 / 9,600 bytes / 100 ms 缓冲置于驱动非分页数据区，用 spin lock 保护。
消费者尚未打开时，写入仍完整成功并丢弃最旧的数据；欠载时补零，避免把缓冲满解释为连接失败。
WaveRT 定时器恢复过晚时最多写一个 DMA 圈，丢弃不可观察的旧圈，避免长时间停留在 DPC。
流销毁时先取消并等待定时器回调结束，再释放 miniport 引用和流内存，避免沿用样例中相反的释放时序。
DMA 映射失败时回收 MDL 并返回错误；通知缓冲对齐后再验证整包 PCM、最多 96,000 bytes 和至少 1 ms 的通知间隔。
格式解析器严格检查 48k/mono/16bit、blockAlign=2、avgBytes=96000、PCM cbSize=0 或 Extensible cbSize=22，
以及声明大小、有效位数、mono channel mask 和 PCM 子类型；主机测试重放短包、零速率和非法 cbSize。
不保证无丢帧，当前也没有独立时钟漂移重采样器。

控制设备 ACL 允许 SYSTEM、管理员及本机交互式登录用户读写；不授予网络/匿名会话权限。
真正音频设备维持 PortCls 音频服务和受限应用所需的音频访问 ACL；控制注入与录音访问分别授权。

### ABI v1

打开：`CreateFileW`，`GENERIC_READ | GENERIC_WRITE`，share=0，`OPEN_EXISTING`。
只有固定 1,920-byte `WriteFile` 合法；其他长度返回错误，内核不保留用户指针。

|操作|数值|输入|输出|
|---|---|---|---|
|QUERY|`0x00226000`|空|八个 `uint32`，共 32 bytes|
|RESET|`0x0022A004`|空|空|
|WRITE|`WriteFile`|48 kHz mono PCM16 的 1,920 bytes|成功时 bytesWritten=1,920|

QUERY 字段依次为：`Size=32`、`Version=1`、`SampleRate=48000`、`Channels=1`、
`BitsPerSample=16`、`FrameBytes=1920`、`RingBytes=9600`、`Capabilities=1`。
Capabilities bit 0 表示溢出丢最旧且合法写入完整接收。数值均使用 Windows 小端布局。
未知 IOCTL 返回 `STATUS_INVALID_DEVICE_REQUEST`，RESET 和写入在锁内核对设备准备状态与句柄所有者。

### 生命周期

一次驱动加载只接纳一个 root 音频适配器。启动成功才接受 CREATE/WRITE/RESET。
STOP 禁止写入并清缓冲；同一个适配器重新启动可恢复现存写者。
REMOVE / SURPRISE_REMOVE 移除符号链接并仅调用一次 `IoDeleteDevice`。
旧控制句柄尚未关闭时，I/O 管理器保持对象 delete-pending；dispatch wrapper 和设备指针身份保留，
晚到 CLEANUP/CLOSE 仍由控制处理器接管，不会交给具有不同扩展布局的 PortCls。
保留的旧对象指针只用于身份比较，不在删除后解引用；驱动不在该加载周期内创建替代控制设备。
关闭/清理写者、RESET、STOP、REMOVE 均清空待采集数据。卸载再次调用清理函数是幂等的。

## 构建与检查

工作区工具链：MSVC 19.44.35229、Windows SDK/WDK NuGet 10.0.26100.6584、KMDF 1.33。
这些包被解压到 `<repo>\.tools\driver-kit`，没有更改 Visual Studio 全局安装。
从微软官方 VS 下载清单和 NuGet 下载；固定 URL / SHA-256 见 `toolchain-lock.json`。
首次下载所测 SHA-256 用于后续复现校验；工具包使用各自微软许可，不与项目驱动发布包一起再分发。

```powershell
# 当前工作区已经具备工具链。重新准备工具链时需要网络和约 1.5 GB 空间：
& .\scripts\bootstrap-toolchain.ps1
& .\scripts\test-driver.ps1
& .\scripts\build-driver.ps1
```

构建脚本直接调用工作区 cl/link，避免依赖不完整的全局 VS/WDK MSBuild 集成。
使用 `/kernel /GS /NXCOMPAT /DYNAMICBASE /INTEGRITYCHECK`，不关闭签名要求。
`InfVerif /w` 检查 Windows Driver INF；`Inf2Cat` 检查签名准备性并生成未签名 catalog。
`signability passed` 表示目录内容和 INF 满足该工具的检查，**不表示驱动已签名或已通过微软认证**。

主机测试实际编译原创 ring 及控制 dispatch 源文件，替换最小的内核 API 边界。
覆盖欠载补零、无消费者、半帧消费后溢出、1 万次回绕、重置、ABI、无效长度、独占写者、
连续完整写入、停止/重启、重复移除和删除后的迟到关闭路由。
这不是内核仿真器，不验证真实 IRQL、对象引用计数、并发抢占或 PortCls 调用约束。

## 下一步实机验证

现已增加 `testkit/` 与 `scripts/make-test-kit.ps1`，生成开发证书签名的独立测试包：
`../artifacts/LinkMic-Driver-TestKit-0.2.0-test.1-win11-x64.zip`。
SYS 嵌入签名后重新生成并签 CAT，不修改原 unsigned 构建。开发私钥只留本机忽略目录，不随包分发。
测试包中的 `README-TEST.md` 从只读环境报告开始，后续安装和回滚默认显示计划，显式 `-Apply` 才改变系统。
另附原生 WASAPI 合成音闭环与手机音频采集工具；开发签名不等于微软生产签名，真实设备验证仍待完成。

在可回滚的测试机/虚拟机先评审和签名这个具体包，再按微软驱动开发流程创建设备并安装。
`pnputil /add-driver ... /install` 单独运行不会自动创建这个 root 枚举设备，不能据此判断安装成功。
发布到普通启用 Secure Boot 的 Windows 电脑需要符合微软驱动签名政策的签名包；当前文件不能直接作为正式安装包交付。
本仓库故意没有执行会改变签名启动策略、写证书库或自动装载内核的安装脚本。

需实测：KS/音频端点枚举、Windows 录音机/会议应用 48k 单声道录音、无消费者持续喂音、
消费者中途打开/关闭、断开手机后的静音、重复断开/重连、设备禁用/启用、睡眠恢复、
开着控制句柄移除设备、Driver Verifier、HLK 音频和 HVCI/内存完整性检查。

## 上游与许可

微软源码固定于 commit `2dc3fd3a0cc84a2933f2194e7ec0871584979071`。
原样源码、原 MS-PL 许可和下载来源记录均保留于 `vendor/`。
原创 LinkMic 控制/缓冲/测试代码不包含商业驱动代码。
此驱动目录以附带的 MS-PL 许可分发；保留版权、专利、商标和归属声明。
重新分发修改后的相关源代码时应遵循 MS-PL 源码许可条件，二进制分发应采用与其兼容的许可。
SDK/WDK/MSVC 自身采用各自工具包许可，不包含在此驱动发布 ZIP 内。

- [官方 SimpleAudioSample](https://github.com/microsoft/Windows-driver-samples/tree/2dc3fd3a0cc84a2933f2194e7ec0871584979071/audio/simpleaudiosample)
- [WaveRT Port Driver](https://learn.microsoft.com/en-us/windows-hardware/drivers/audio/wavert-port-driver)
- [WDK NuGet 使用说明](https://learn.microsoft.com/en-us/windows-hardware/drivers/install-the-wdk-using-nuget)
- [IoDeleteDevice 对象删除约束](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/wdm/nf-wdm-iodeletedevice)
- [KMDF 版本历史](https://learn.microsoft.com/en-us/windows-hardware/drivers/wdf/kmdf-version-history)
- [Windows 驱动签名政策](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/kernel-mode-code-signing-policy--windows-vista-and-later-)

