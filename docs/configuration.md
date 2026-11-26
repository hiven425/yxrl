# 模块配置

桌面应用以设置界面作为日常配置入口，字段表用于实现、排障和恢复。桌面试用版已读写时钟、月历和城市偏好，其余字段按下表的后续功能契约保留。当前 HTML 原型使用页内状态，刷新可恢复示例，在没有网络或开发环境的电脑上也能运行。

## 使用与开发环境

| 场景 | 配置方式 | 检查方法 |
| --- | --- | --- |
| 评审原型 | 整个 `prototype` 目录一起保留，用 Edge 或 Chrome 打开 `index.html`。脚本按普通本地脚本加载，样式与数据随目录分发。 | 浏览器设为离线后，依次打开八个界面、保存一条事项、刷新恢复样例。 |
| 开发 WPF 版 | 目标 `net10.0-windows`，开启 WPF，发布目标 `win-x64`。在项目本地解包微软 .NET 10 SDK，使用项目本地可执行文件运行还原和构建。原生工程落地时通过 `global.json` 记录实际使用的完整 SDK 版本。 | 用项目内 `dotnet --info` 核对 SDK 路径、主版本和 x64 架构；从实际项目入口执行还原与构建。 |
| 离线历法 | WPF 项目锁定 NuGet `lunar-csharp` `1.6.9` 并提交 NuGet 锁文件。按本地民用日期查询 `Solar.Lunar`。 | 2026-11-26 应得到十月十八、丙午年、己亥月、甲辰日。 |
| 个人电脑运行 | 桌面版按自包含 Windows x64 包发布，入口约定为 `Yxrl.exe`。当前仓库交付阶段见项目首页。 | 在目标电脑从普通用户身份启动，并检查原位时钟及退出恢复。 |

[.NET 自包含发布](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview) · [锁定的历法包](https://www.nuget.org/packages/lunar-csharp/1.6.9)

## 桌面设置字段

| 设置入口 | 字段与类型 | 初始值 | 生效与反馈 |
| --- | --- | --- | --- |
| 外观 | `appearance.theme`，枚举 `system/light/dark` | `system` | 立即更新面板，跟随系统时监听主题变化。 |
| 任务栏时钟 | `clock.enabled`，布尔 | `true` | 控件可辨识时启用原位置。关闭时释放显示及点击区域。 |
| 时钟样式 | `clock.preset`，下表标识或 `custom` | `seconds` | 先在设置页预览，点击「应用样式」后保存并更新任务栏。 |
| 自定义格式 | `clock.lines`，两项字符串数组，每项最多 64 字符 | `["HH:mm:ss","M月d日 ddd"]` | 可以留一行空白，至少有一行内容。未知字段显示错误并保留已应用样式。 |
| 节日倒数 | `clock.countdownFestivalId`，节日标识或 `null` | `spring` | 从启用的节日中选目标。关闭或删除目标后清空选择并提示重新选；节日当天显示「今天」。 |
| 节日管理 | `festivals.disabledIds`，标识数组；`festivals.customRules`，规则数组 | 两个空数组 | 按稳定标识保存开关，立即重算月历、日详情和时钟。 |
| 城市与天气 | `weather.location`，地点对象或 `null` | `null` | 用户选好城市后才请求。按地点标识取缓存；切换城市时旧请求结果只能写回原地点。 |
| 直连天气服务 | `weather.directConnection`，布尔值 | `false` | 默认使用系统代理。勾选后，当前搜索及应用设置后的天气和空气质量查询使用 HTTPS 直连。 |
| 事项编辑 | `repeat` 为 `none/daily/weekly`，`reminderMinutes` 为 `null/0/10/30` | 单次，无提醒 | 随事项保存。0 表示到点，正数为提前分钟数。 |
| 全天事项 | `time`，`HH:mm` 字符串或 `null` | 选择全天后为 `null` | 全天显示在列表前面。开启全天提醒时以系统当地 09:00 为基准。 |

任务栏宽度取系统原时钟的实际可用区域。原型的「标准／窄」仅用于复现布局，分别模拟 161 和 85 CSS 像素。长内容固定显示时间，其余信息每 4 秒轮换；空间足够时两行静态展示。原型默认城市深圳，便于评审天气界面。桌面版首次运行的城市为空。

### 日历设置

这些开关在确认选择后即时更新，存于 `settings.json` 的 `calendar` 对象。

| 设置 | 字段与初始值 | 实际作用 |
| --- | --- | --- |
| 显示农历和节日信息 | `showLunar: true` | 控制月历格第二行；主动打开日详情仍可查完整黄历。 |
| 显示调休安排 | `showAdjustments: true` | 显隐月份与详情的休班标记，底层调休数据保持独立。 |
| 显示非当前月日期 | `showOutside: false` | 关闭时邻月格留空；开启后显示并可进入相邻日期。 |
| 滚动后对齐月份 | `snapMonths: false` | 滚轮按周移动，默认保留停留位置。开启后停滚即对齐到当前月首行。翻月按钮始终按整月切换。 |
| 使用 12 小时制 | `hour12: false` | 控制弹窗主时钟、事项列表和内置时钟样式。手写格式中的 `HH` 或 `h` 按显式字段执行。 |
| 显示周数 | `showWeeks: true` | 控制周号栏、日详情周号、时钟周号及自定义 `{周数}`。 |
| 自定日期范围 | `weekRange: null` 或 `{"from":"2026-11-23","to":"2026-11-29"}` | 含两端，仅在指定日期范围内显示原有 ISO 周号；校验结束日期不早于开始日期。「显示全年周数」清除限制。 |
| 一周开始于 | `weekStart: 1`，0 至 6 | 周日为 0，默认周一。表头、日期格、方向键和整月对齐共用这一顺序。 |
| 显示天气预报 | `showWeather: true` | 控制月历顶部天气入口。可在设置页单独管理城市和天气时钟样式。 |
| CLI 新建日程默认分类 | `cliCategory: "default"`，可选 `work/personal` | 命令省略分类时取此值；「试建命令行示例日程」可在日详情或编辑器检查结果。 |
| 日程内容样式 | `itemStyle: "tint"` 或 `solid` | 切换分类浅色标记或实色填充，同时更新月历摘要与日详情。 |

**周号示例。** 2026-11-23 至 11-29 的每一天都属于 ISO 第 48 周。以周一开头时，仅该行显示 48。其他周首日可能把一个 ISO 周拆成两个可见行；每行取星期四的 ISO 周号，若星期四在范围外，则取本行与范围交集中离星期四最近日期的 ISO 周号。完全在范围外的行留空。日详情和时钟按各自具体日期判断。

### 软件设置

字段保存在 `general` 对象。浏览器原型中这些开关仅作用于模拟桌面。新旧软件切换验收保留优效原来的启动设置。

| 设置 | 字段与初始值 | 行为与配置反馈 |
| --- | --- | --- |
| 随系统自动运行 | `autoStart: false` | 由用户开启。原型「模拟登录」读回选择；桌面版注册成功后才显示开启，注册失败保留实际状态。 |
| 在系统托盘显示图标 | `trayIcon: false` | 控制一个可点击的辅助图标，任务栏原时钟入口继续可用。 |
| 失去焦点自动关闭日历窗口 | `closeOnBlur: true` | 点击桌面空白收起；关闭开关后保留面板。表单或应用内对话框延续当前操作。 |
| 关闭日历时同时关闭其他窗口 | `closeRelated: true` | 开启时一起收起关联设置，关闭开关后让已打开的设置窗口继续显示。隐私锁开启时锁定优先。 |
| 记忆上次日历位置 | `rememberPosition: false`、`lastPosition: null` | 可拖动月历顶部。开启后恢复上次窗口位置并限制在可见工作区；默认再次弹出仍靠近时钟。 |
| 启用隐私锁 | `privacy: false` | 开启后可立即锁定，或收起月历时锁定。桌面端请求 Windows 当前用户验证；原型要求设置 4 至 8 位演示 PIN，内存中仅留随机盐与派生值。 |
| 待办完成提示音 | `completionSound: true` | 仅勾为完成时播放。声音设置可选择轻点、双音、清脆并试听。 |
| 声音设置 | `tone: "soft"`，可选 `chime/clear`；`volume: 35` | 音量 0 至 100，试听使用当前音色和音量。 |

### 命令行新增日程的拟定契约

桌面版拟使用 `Yxrl.exe add --title "项目讨论" --date 2026-11-26 --time 09:00`，可加 `--category default|work|personal`。成功返回新事项标识；非法日期、未知分类或本地写入失败应给出错误并返回非零退出码。原型在「日历设置 → 试建命令行示例日程」展示同一输入的保存结果。当前桌面 EXE 提供任务栏和设置入口，事项命令仍在 HTML 原型中演示。

### 九种时钟预设

下表以 11 月 26 日 10:32:01 为例。「示例日期」卡片固定为同日 12:25:31，并使用内置节日和深圳天气样例。「当前时间」卡片及底部实际预览按当前时间、已启用节日和所选城市格式化。预览模式只在本次打开设置时有效，示例数据只用于卡片展示。

| 名称 | 标识 | 上行 | 下行 |
| --- | --- | --- | --- |
| 公历简约 | `simple` | 10:32 周四 | 2026-11-26 |
| 公农双历 | `dual` | 10:32 周四 | 11月26日 十月十八 |
| 星期秒钟 | `seconds` | 10:32:01 | 11月26日 周四 |
| 节日倒数 | `countdown` | 10:32 周四 | 距春节72天 |
| 节日节气 | `festive` | 10:32:01 周四 感恩节 | 11月26日 十月十八 |
| 当前天气 | `weather` | 10:32 多云 23° | 11月26日 周四 |
| 十二时辰 | `hours` | 上午10:32:01 巳时 | 11月26日 周四 |
| 周数日历 | `week` | 10:32:01 第48周 | 2026-11-26 周四 |
| 天气详情 | `forecast` | 10:32 深圳 多云 | 20～26° 美标AQI 42 |

### 自定义格式

| 字段 | 解释 |
| --- | --- |
| `HH`、`mm`、`ss` | 两位 24 小时、分钟、秒。`h` 搭配 `tt` 表示 12 小时和「上午／下午」。 |
| `yyyy`、`M`、`MM`、`d`、`dd`、`ddd` | 年、月、日和简写星期。双字母月日补零，`ddd` 如「周四」。 |
| `{农历}`、`{节日}`、`{节气}`、`{周数}`、`{时辰}`、`{倒数}` | 从同一日期视图读取。没有对应节日或节气时该字段显示为空。 |
| `{城市}`、`{天气}`、`{温度范围}`、`{美标AQI}` | 使用当前选中地点的天气快照，缺少某项数据时避免填入虚构数值。 |
| `hh:ff:ss 星期W JJ` | 支持截图中的输入写法。YXRL 将 `hh`、`ff`、`W`、`JJ` 分别解释为两位小时、分钟、星期序字、当日节日及节气。与优效完整格式方言的逐项比对留在真机验收。 |

## 节日与休班

| 规则 | 用户填写 | 实例与边界 |
| --- | --- | --- |
| 公历 `solar` | 名称、可选别名、月、日 | `2/14` 对应情人节。`2/29` 仅在闰年出现，非法月日不接受。 |
| 农历 `lunar` | 名称、可选别名、农历月、日 | 正月初一对应春节。普通月规则在常月匹配，闰月同日不重复；农历三十只在有三十的月份出现。内置除夕按次日是否正月初一确定。 |
| 动态 `dynamic` | 月份、第几个或最后一个、星期几 | 感恩节为 11 月第四个星期四。顺序支持 1 至 5 和 -1，星期值 0 代表周日；月份缺少第五次时当年跳过。 |
| 官方休班 | 随年度数据包维护 | 只有通知明确的放假和补班日期标「休／班」。同一天可同时出现节日名称和休班标记；没有已核对年度数据时不推算调休。 |

内置节日按标识开关，自定义节日可新增、修改、删除。搜索同时匹配名称和别名；多个名称落在同一天时日详情全部列出，月历格优先显示一个节日，其次节气，最后显示农历日。节日匹配示例与官方数据分别维护。[2026 年通知](https://www.beijing.gov.cn/zhengce/zhengcefagui/202511/t20251104_4258873.html) · [2027 年通知](https://www.dxh.gov.cn/ZWGK/zc_zwgk/ZFWJ/TZGG/202611/t20261106_2875104.shtml)

## 本地文件契约

桌面版根目录为 `%LOCALAPPDATA%\Yxrl\`。三个文件均使用 UTF-8，顶层含整数 `schemaVersion`。以下为 v1 设计示例，个人数据使用示例名称。原型不会创建这些文件。

**`settings.json`** 保存个人偏好与节日规则。示例中的「读书日」是每年 11 月第四个星期四。可通过开关独立控制内置感恩节。

```json
{
  "schemaVersion": 1,
  "appearance": { "theme": "system" },
  "general": {
    "autoStart": false, "trayIcon": false, "closeOnBlur": true, "closeRelated": true,
    "rememberPosition": false, "lastPosition": null, "privacy": false,
    "completionSound": true, "tone": "soft", "volume": 35
  },
  "calendar": {
    "showLunar": true, "showAdjustments": true, "showOutside": false,
    "snapMonths": false, "hour12": false, "showWeeks": true, "weekStart": 1,
    "weekRange": null, "showWeather": true, "cliCategory": "default", "itemStyle": "tint"
  },
  "clock": {
    "enabled": true,
    "preset": "seconds",
    "lines": ["HH:mm:ss", "M月d日 ddd"],
    "countdownFestivalId": "spring"
  },
  "festivals": {
    "disabledIds": ["thanks"],
    "customRules": [
      { "id": "demo-reading", "kind": "dynamic", "name": "读书日", "alias": "阅读日", "month": 11, "nth": 4, "dayOfWeek": 4 }
    ]
  },
  "weather": { "location": null, "directConnection": false }
}
```

**`planner.json`** 保存系列、待办完成日期及提醒收据。`date` 是系列起始日期，`completedDates` 以发生日期标识各次完成。提醒收据中的 UTC 到期时间用于区分重新排期后的提醒；本机展示使用本地时间。

```json
{
  "schemaVersion": 1,
  "items": [
    {
      "id": "demo-book",
      "kind": "todo",
      "category": "personal",
      "title": "归还图书馆借书",
      "date": "2026-11-26",
      "time": "16:30",
      "repeat": "weekly",
      "reminderMinutes": 10,
      "completedDates": ["2026-11-26"]
    }
  ],
  "reminders": {
    "lastCheckedAt": "2026-11-26T08:21:00Z",
    "delivered": [
      { "itemId": "demo-book", "occurrenceDate": "2026-11-26", "dueAt": "2026-11-26T08:20:00Z" }
    ]
  }
}
```

**`weather-cache.json`** 以地点 ID 为键，分别保存天气和空气质量。`observedAt` 是地点当地的供应方时刻，`fetchedAt` 是本机取回时刻。每项记录自己的 `source`，旧缓存缺少该字段时按 Open-Meteo 读回。

```json
{
  "1795565": {
    "current": { "observedAt": "2026-11-26T10:15", "fetchedAt": "2026-11-26T02:25:00Z", "temp": 23, "condition": "多云", "low": 20, "high": 26, "source": "Open-Meteo" },
    "air": { "observedAt": "2026-11-26T10:00", "fetchedAt": "2026-11-26T02:25:00Z", "aqi": 42, "source": "Open-Meteo" }
  }
}
```

首次启动且文件不存在时创建默认状态。遇到未知更高版本时，保持原文件并提示所需版本；某次写入失败时保留当前编辑内容和上次有效快照。个人事项保存失败要能与「当天没有安排」区分。

## 天气与外部连接

桌面版仅发送查询文字或所选城市经纬度。地点对象包含供应方地点标识、城市名、行政区、经纬度和 IANA 时区。搜索同名城市时显示行政区供选择。按所选城市时区取当日最高最低温，系统时钟和个人提醒按电脑本地时区工作。

| 用途 | 接口和字段 | 刷新与错误 |
| --- | --- | --- |
| 城市搜索 | `geocoding-api.open-meteo.com/v1/search`，`name`、`language=zh`；中文同名地点保留区分信息 | 用户提交后搜索。请求失败保留已选地点和查询文字。 |
| 当前与当日天气 | `api.open-meteo.com/v1/forecast`，`current=temperature_2m,weather_code`、`daily=temperature_2m_min,temperature_2m_max`、`timezone=auto` | 新地点或缓存超过 30 分钟时后台更新。换月、切换时钟样式共用快照。 |
| 空气质量 | `air-quality-api.open-meteo.com/v1/air-quality`，`current=us_aqi` | 独立缓存 60 分钟。界面显示「美标 AQI」和来源时间，0 是有效值。 |
| 备用天气与空气质量 | Visual Crossing Timeline 的 `currentConditions`、当日 `days` 和数值 `aqius` | 匿名 HTTPS 请求使用 gzip。一轮刷新至多发送一次，只查询尚缺的维度。 |

城市搜索先使用完整名称。以「市」结尾的名称查无结果时，再查询简称，例如「深圳市」和「深圳」。临时网络故障重试一次。单次 HTTP 请求超时为 8 秒。

已知经纬度后，同时查询到期的气温和美标 AQI。主源出错或等待满 3 秒即并行查询备用源，每项采用最先返回的有效结果。已取得两项数据或已切换地点时取消剩余请求。离线时显示已有数值、缓存标记和数据时间。缺项会明确显示暂不可用。[城市查询](https://open-meteo.com/en/docs/geocoding-api) · [天气预报](https://open-meteo.com/en/docs) · [空气质量及归属](https://open-meteo.com/en/docs/air-quality-api) · [Visual Crossing 免密钥接口](https://www.visualcrossing.com/resources/documentation/weather-api/free-weather-api-no-key/)

任务栏样式用到备用源数据时，下行附上 Visual Crossing Weather，空间不足时随其他信息轮显。月历中的来源名称可打开供应方网站。
