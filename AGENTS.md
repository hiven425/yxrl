# AGENTS.md

YXRL 是个人自用的 Windows 中文桌面日历。用户左键点击任务栏原时间位置打开月历，点日期在月历左侧展开详情，用于查看黄历、管理日程和待办。产品采用无广告设计，日常操作和个人数据均在本机完成。

## 项目约定

- 从 [项目入口](docs/README.md) 开始。模块职责和术语见 [项目架构](docs/architecture.md)，字段和默认值见 [模块配置](docs/configuration.md)，界面行为见 [UI 与交互](docs/ui-interactions.md)。
- 首版目标为 Windows 11 25H2 x64，技术基线为 C#、项目本地 .NET 10 SDK、WPF、`lunar-csharp` 1.6.9。可交互原型用原生 HTML、CSS 和 JavaScript，双击即可离线打开。依据仓库实际文件报告当前实现阶段。
- 主入口为任务栏原时钟。默认「星期秒钟」，支持九种预设和上下两行自由格式。点击弹出月历，悬停保持桌面状态。空间不足时在原位缩小文字并折行，上下行内容同时可见。
- UI 经 ViewModel 和应用用例访问历法、个人事项和天气。公农历、节气和宜忌由历法模块计算。可开关的节日名称与国务院公布的休班安排分开建模。任务栏、月历和日详情共用同一份已启用节日结果。
- 日历设置的起止日期是 ISO 周号显示范围。范围内保留 ISO 周号，切换一周起始日仅改变排布。软件设置覆盖当前用户登录启动、辅助托盘图标、窗口行为、隐私锁和待办提示音。浏览器演示不写 Windows 启动项、不请求系统凭据。
- 日程、待办、自定义节日和配置保留在当前 Windows 用户目录。仅城市查询、天气和空气质量使用网络。天气显示数据时间，空气质量显示美标 AQI 口径。个人版界面全部空间用于日期和个人安排。
- 变更时钟样式、节日规则、设置字段或交互后，同步相关文档与可点原型。界面图由同版本原型导出。用 `2026-11-26` 和 `2026-10-10` 回归节日联动与补班标记。
- 浏览器原型、数据回算和 Windows 真机分别记录验证证据。真机入口按约定临时正常退出优效日历，测完退出新应用、重开优效，保留其数据和登录启动设置。

## 通用协作约定

Respond in Chinese by default. Lead with the answer or recommendation and the reason that matters. Use the project's terminology. Mention uncertainty when it affects the decision. Give progress updates for material findings, blockers, or decisions the user needs to make.

### Scope and decisions

- For a review or discussion, give findings and a recommendation. When asked to implement a straightforward, self-contained change, make it and verify it directly. Follow project conventions for routine, reversible decisions.
- If the work would affect another module, caller, shared contract, or existing behavior beyond what the user has agreed to, or if requirements conflict, pause before editing. Check the relevant facts, explain the impact, recommend an approach, and ask a focused question. Wait for the user's decision before making the affected changes.
- Within the agreed scope, update callers, types, configuration, docs, and examples that the change would otherwise break. Fix regressions introduced by the change. Leave unrelated work for a separate task.
- Establish local behavior from the relevant code, callers, configuration, and runtime evidence. Consult version-matched primary sources when external behavior is unclear, the answer depends on current information, or the user asks for research.

### Implementation

- Use the target module's existing environment, lockfile, and scripts. On Windows, default to PowerShell. Use system Python for standalone scripts unless the task specifies another interpreter.
- Follow existing module boundaries and naming. Reuse code with the same business meaning. Add helpers, configuration options, or extension points when they solve a current problem. Remove imports, variables, and private helpers made unused by this change.
- Validate unknown input at the boundary that owns it. Downstream code can rely on that guarantee while it holds. Limit compatibility branches to supported callers or stored data. If sources disagree, establish which one owns the value.
- Handle a concrete failure where the code can recover, compensate, or translate it into a documented error. Otherwise propagate it with useful context. Keep failures distinguishable from valid empty results. Use a fallback only when the contract defines it. Retry only transient failures when the operation is safe to repeat, with a bounded number of attempts.
- Use the language or framework's resource cleanup mechanisms. Log events that help diagnose a failure. Use comments for constraints or reasons that the code cannot express clearly.

### Verification and delivery

- Run the smallest existing check that covers the original issue and affected behavior, plus any checks the project requires. Verify the outcome the user asked for. For example, when confirming a database write, read back the record for that operation.
- Use temporary reproductions when helpful. Keep a new test, fixture, or verification script when requested, required by the project, or needed to catch a specific regression the existing checks would miss.
- Rerun affected checks after a fix. If fixes keep failing, trace the actual call path. Reuse evidence that still applies to the final change. Skip checksums for ordinary file reads and edits.
- Report the result, where to find it, and the evidence relevant to acceptance. Identify any requested outcome that remains unverified and why.

### Git and authorization

- Work in the repository that owns the target files. Check its branch and working tree. Preserve staged, unstaged, and untracked user work.
- Unless the user directs otherwise, after a feature or fix passes the applicable checks, commit only the task changes and push the task branch to its agreed remote without asking again. Follow the project's release process for shared and release branches. Confirm before force-pushing or rewriting shared history.
- Use the project's normal setup command to restore locked dependencies in its local environment. Before adding or upgrading dependencies, bulk deletion or overwriting, database migrations, writing to flash storage, production configuration changes, permission or persistent environment changes, or irreversible scripts, explain the target, scope, and impact and obtain confirmation. Once confirmed, proceed unless those terms change.
- Access sensitive data only as needed for the task. Keep credentials, personal information, and production data out of code, logs, and replies.
