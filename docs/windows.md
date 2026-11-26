# Windows 桌面试用版

在 Windows 11 x64 上双击 `dist/win-x64/Yxrl.exe`。它包含 .NET 桌面运行时，以普通用户身份运行。首次解包运行时可能需要几秒。原时间位置已由优效接管时，请先从优效菜单正常退出。若任务栏自动隐藏，将鼠标移到屏幕边缘，系统任务栏展开后即可看到两行时钟。

## 在任务栏使用

- 左键点击时间打开本月，再点一次或按 Esc 收起。点窗口外也会收起月历。悬停时保持桌面状态，右键和相邻系统图标保留 Windows 操作。
- 点击日期查看农历、干支、节日、节气和当日宜忌。左右翻月，「今天」回到系统日期。方向键选日，Page Up / Page Down 翻月，Home 回到今天。
- 点月历右上角齿轮选择九种时钟预设或「自定义两行格式」。可以试用上行 `HH:mm:ss`、下行 `ddd {农历}`。预览满意后点「应用设置」。宽度取 Windows 提供的原时钟边界，放不下时保留时间，附加内容每四秒轮换。
- 「月历显示」可设置农历、休班、邻月日期、12 小时制、周首日和 ISO 周号范围。范围包含起止两天，范围内保留原周号。中国休班数据覆盖 2026、2027 两年，普通周末以颜色标出。
- 「天气城市」支持搜索、选中城市和保存。选好城市后向 Open-Meteo 获取气温及美标 AQI，各自保留最近成功的数据和时间。首次运行的城市为空。
- 再次双击同一个 EXE 会打开现有实例的设置。点「退出日历」释放任务栏位置，Windows 原时钟随即恢复。启动项由用户自己的 Windows 设置管理，本次试用默认手动启动。

时钟和月历偏好保存在 `%LOCALAPPDATA%\Yxrl\settings.json`。天气缓存为同一目录的 `weather-cache.json`。当配置无法读取时，设置页显示路径与错误并保留原文件。试用版覆盖任务栏、时钟样式、月历和日期查看。日程、待办、后台提醒、节日管理编辑器及软件设置的完整体验仍可在[交互原型](prototype/index.html)中查看。

## 本机验收

Windows 11 25H2 x64（26200.7840），屏幕缩放 150%，任务栏自动隐藏。展开后，Explorer 的原时钟和 YXRL 窗口均位于物理像素 `(2939, 1848)`，宽 115、高 72。悬停保持收起，真实左键打开月历，再点收起。右键出现 Windows 的「调整日期和时间」「通知设置」，点击旁边的系统图标仍由 Windows 处理。

<img src="screens/windows-clock.png" width="90" alt="实机任务栏原位置的两行时钟">　<img src="screens/windows-month.png" width="330" alt="点击真实任务栏时间打开的 2026 年 11 月月历">

在发布版中查看了 11 月 26 日的感恩节、农历十月十八，以及 10 月 10 日的补班标记。将下行改为 `ddd {农历}`，退出进程后重新启动，预览读回「周四 十月十八」。验收结束后恢复了原先运行的优效，并确认两者同时启动时 YXRL 会提示原时间位置已被占用。

天气单独做了联网验收。直连 Open-Meteo 时，深圳城市搜索、气温和美标 AQI 均已获取并从缓存读回。本机默认代理的 TLS 握手失败。应用沿用系统网络设置，城市搜索失败时可稍后重试，等候期间仍可使用时钟及离线月历。

## 从源码生成同样的单文件

1. 取 `global.json` 指定的微软 .NET 10 SDK 的 **Windows x64 Binaries (zip)**。解包到仓库 `.tools/dotnet`，使其中直接包含 `dotnet.exe`。下载来源为[微软 .NET 10 下载页](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)。
2. 在仓库根目录运行下列 PowerShell 命令。发布脚本按 NuGet 锁文件还原，生成自包含单文件。

```powershell
.\.tools\dotnet\dotnet.exe --version
.\scripts\publish.ps1
.\dist\win-x64\Yxrl.exe
```

开发者可单独运行历法、配置及双行格式回归。最后一条命令读取当前桌面已展开的原生时钟边界，供适配不同 Windows 构建时使用。

```powershell
.\.tools\dotnet\dotnet.exe run --project tests/Yxrl.Checks -c Release
node --test docs/prototype/tests/week-range.test.cjs tests/catalog-parity.test.cjs
.\.tools\dotnet\dotnet.exe run --project tests/Yxrl.Checks -c Release -- --taskbar
```

锁定的历法库、.NET 运行时和 WPF 的许可文本内嵌在 EXE，可在设置中的「开源许可」查看。
