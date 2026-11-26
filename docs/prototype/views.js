const CalendarViews = (function () {
  const M = CalendarModel,
    icon = Icons.icon;
  const e = (value) =>
    String(value ?? "").replace(
      /[&<>"']/g,
      (char) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[char],
    );
  const screens = [
    ["clock", "clock", "任务栏时钟"],
    ["month", "calendar", "月历"],
    ["day", "day", "当日详情"],
    ["editor", "edit", "事项编辑"],
    ["styles", "sliders", "时钟样式"],
    ["festivals", "flag", "节日管理"],
    ["calendarSettings", "calendar", "日历设置"],
    ["general", "settings", "软件设置"],
  ];
  const kindNames = { solar: "公历", lunar: "农历", dynamic: "动态" };
  const repeats = { none: "不重复", daily: "每天", weekly: "每周" };
  const reminders = { off: "不提醒", at: "到点提醒", 10: "提前 10 分钟", 30: "提前 30 分钟" };
  function options(entries, selected) {
    return Object.entries(entries)
      .map(
        ([value, label]) =>
          `<option value="${e(value)}"${String(selected) === value ? " selected" : ""}>${e(label)}</option>`,
      )
      .join("");
  }
  function button(action, label, glyph, classes = "icon-button") {
    return `<button type="button" class="${classes}" data-action="${action}" title="${e(label)}" aria-label="${e(label)}">${icon(glyph)}</button>`;
  }
  function navbar() {
    const group = (name, start, end) =>
      `<h2>${name}</h2>${screens
        .slice(start, end)
        .map(
          ([id, glyph, label]) =>
            `<button type="button" data-page="${id}">${icon(glyph)}<span>${label}</span></button>`,
        )
        .join("")}`;
    return group("日历", 0, 4) + group("设置", 4, 8);
  }
  function weatherGlyph(scene = "unknown") {
    return `<span class="weather-glass" data-weather-scene="${e(scene)}" aria-hidden="true">
      <span class="glass-sun"><i class="glass-rays"></i><i class="glass-orb"></i></span>
      <span class="glass-moon"><i></i><i></i></span>
      <span class="glass-twilight"><i class="glass-orb"></i></span>
      <span class="glass-horizon"></span>
      <span class="glass-cloud"></span>
      <span class="glass-rain"><i></i><i></i><i></i><i></i></span>
      <span class="glass-snow"><i>❄</i><i>❄</i><i>❄</i></span>
      <span class="glass-bolt"></span>
      <span class="glass-fog"></span>
      <span class="glass-wind"></span>
      <span class="glass-unknown"></span>
    </span>`;
  }
  function weatherBadge(state) {
    const w = state.network === "empty" ? null : M.weatherSamples[state.city];
    const scene = M.weatherScene(state, "10:32:01");
    const status = state.network === "cached" ? "缓存 10:25" : "";
    const preview = w && state.weatherAnimation !== "auto" && state.network !== "cached"
      ? M.weatherAnimations[state.weatherAnimation] : "";
    const meta = !w
      ? e(state.city || "选择城市") + " · 暂无数据"
      : preview
        ? e(preview)
        : e(state.city) + (status ? " · " + status : "");
    return `<button class="weather-badge" type="button" data-action="weather" aria-label="城市与天气设置">${weatherGlyph(scene)}<span><span class="weather-badge-head"><strong>${w ? w.temp + "°" : "—"}</strong>${w ? `<em>${e(w.condition)}</em>` : ""}</span><small>${meta}</small></span></button>`;
  }
  function occurrenceRow(item, date, cal) {
    const done = item.done.includes(date);
    const prefix =
      item.kind === "todo"
        ? `<input class="todo-check" type="checkbox" data-done="${e(item.id)}" aria-label="${e((done ? "恢复" : "完成") + "待办 " + item.title)}" ${done ? "checked" : ""}>`
        : "";
    return `<div class="occurrence ${done ? "completed" : ""} ${item.kind === "event" ? "appointment" : ""}" data-category="${e(item.category || "default")}">${prefix}<button type="button" data-edit="${e(item.id)}" class="item-edit" title="编辑${item.kind === "todo" ? "待办" : "日程"}"><span>${e(item.title)}</span><small>${item.repeat !== "none" ? e(repeats[item.repeat]) + " · " : ""}${e(M.timeLabel(item.time, cal.hour12) || "全天")}</small></button></div>`;
  }
  function emptyItems() {
    return '<p class="empty-line">当天还没有安排</p>';
  }
  function monthGrid(state, detailOpen) {
    const cal = state.calendar,
      rows = M.weeksInView(state.month, cal.weekStart, state.scrollStart);
    const heads = Array.from({ length: 7 }, (_, i) => (i + cal.weekStart) % 7);
    return `<div class="month-grid ${cal.showWeeks ? "" : "no-week"}" data-start="${rows[0][0]}" role="group" aria-label="月历日期">${cal.showWeeks ? '<span class="week-column weekday-head">周</span>' : ""}${heads.map((n) => `<span class="weekday-head ${n === 0 || n === 6 ? "weekend" : ""}">${M.weekdayNames[n]}</span>`).join("")}
      ${rows
        .map(
          (week) =>
            (cal.showWeeks
              ? `<span class="week-column week-count">${M.displayWeek(week[0], cal) ?? ""}</span>`
              : "") +
            week
              .map((key) => {
                if (!cal.showOutside && key.slice(0, 7) !== state.month)
                  return '<span class="date-placeholder" aria-hidden="true"></span>';
                const l = M.lunar(key),
                  mark = cal.showAdjustments ? M.dayMark(key) : "",
                  fest = M.festivalsOn(state.rules, key);
                const label = cal.showLunar
                  ? fest[0]?.name || l.term || (l.day === 1 ? l.monthName : l.dayName)
                  : "";
                const cl = [
                  "date-cell",
                  key === M.today ? "today" : "",
                  key === state.selected ? "selected" : "",
                  key.slice(0, 7) !== state.month ? "adjacent" : "",
                  [0, 6].includes(M.dateOf(key).getUTCDay()) ? "weekend" : "",
                ].join(" ");
                const disabled = key.slice(0, 7) < M.firstMonth || key.slice(0, 7) > M.lastMonth;
                const expanded = detailOpen && key === state.selected;
                const reminders = state.dateReminders.filter(item => item.date === key).length;
                const aria = M.displayDate(key, true) + " 星期" + M.weekday(key) + " " + label +
                  (mark ? " " + mark : "") + (reminders ? " · " + reminders + " 条日期提醒" : "");
                return `<button type="button" class="${cl}" data-date="${key}" ${disabled ? "disabled" : ""} ${key === M.today ? 'aria-current="date"' : ""} aria-expanded="${expanded}"${expanded ? ' aria-controls="day-detail"' : ""} aria-label="${e(aria)}"><span class="date-main">${Number(key.slice(-2))}</span><small class="${fest.length || l.term ? "observance" : ""}">${e(label)}</small>${mark ? `<span class="day-mark ${mark === "班" ? "work" : ""}">${mark}</span>` : ""}${reminders ? '<span class="date-reminder-dot" aria-hidden="true"></span>' : ""}</button>`;
              })
              .join(""),
        )
        .join("")}</div>`;
  }
  function resizeGrips() {
    return ["start", "end"].map((edge, index) => {
      const corner = ["左上角", "右下角"][index];
      return `<button type="button" class="calendar-grip" data-resize="${edge}"
        aria-label="从${corner}调整窗口大小" title="拖动或按方向键调整窗口大小"></button>`;
    }).join("");
  }
  function month(state, detailOpen = false) {
    const entries = M.itemsOn(state.items, state.selected);
    return `<section class="flyout month-window" aria-label="月历弹窗"><div class="calendar-surface">
      <header class="month-hero"><div><div class="digital"><em data-period></em><span data-hour-minute></span><small data-seconds></small></div><p>${M.displayDate(M.today, true)}<span>星期${M.weekday(M.today)}</span></p></div>${state.calendar.showWeather ? weatherBadge(state) : ""}</header>
      <div class="month-tool"><h2>${Number(state.month.slice(0, 4))}<small> 年 </small>${Number(state.month.slice(5))}<small> 月</small></h2><div><button class="today-btn" type="button" data-action="today">今天</button>${button("prev", "上个月", "left")}${button("next", "下个月", "right")}</div></div>
      ${monthGrid(state, detailOpen)}
      <div class="mini-agenda"><header><h3>${state.selected === M.today ? "今日安排" : M.displayDate(state.selected) + "安排"}</h3><button type="button" class="text-button" data-page="day">查看详情 ${icon("right", 14)}</button></header>${
        entries.length
          ? entries
              .slice(0, 2)
              .map((i) => occurrenceRow(i, state.selected, state.calendar))
              .join("")
          : emptyItems()
      }</div>
      <footer class="fly-foot"><button type="button" class="primary" data-action="add">${icon("plus", 16)} 添加事项</button>${button("settings", "打开设置", "settings")}</footer>
      </div>${resizeGrips()}</section>`;
  }
  function day(state) {
    const key = state.selected,
      l = M.lunar(key),
      festivals = M.festivalsOn(state.rules, key),
      mark = state.calendar.showAdjustments ? M.dayMark(key) : "",
      entries = M.itemsOn(state.items, key);
    const tag = (t) => `<span class="tag">${e(t)}</span>`;
    return `<section id="day-detail" class="flyout detail-window" aria-label="当日详情"><div class="calendar-surface"><header class="panel-head"><h2>当日详情</h2>${button("close-day", "关闭日详情", "close")}</header>
      <div class="day-scroll"><div class="day-overview"><div class="date-tile"><small>${Number(key.slice(5, 7))}月</small><strong>${Number(key.slice(-2))}</strong></div><div><h3>${M.displayDate(key, true)}</h3><p>星期${M.weekday(key)}${M.weekForDate(key, state.calendar) === null ? "" : " · 第 " + M.weekForDate(key, state.calendar) + " 周"}</p><div class="tags">${festivals.map((f) => tag(f.name)).join("")}${l.term ? tag(l.term) : ""}${mark ? tag(mark === "班" ? "调休上班" : "放假") : ""}</div></div></div>
      <section class="detail-section almanac"><h3>农历${l.monthName}${l.dayName}</h3><p class="muted">${e(l.ganzhi)}</p><div class="auspice"><b>宜</b><span>${e(l.yi || "—")}</span></div><div class="auspice avoid"><b>忌</b><span>${e(l.ji || "—")}</span></div></section>
      <section class="detail-section"><header class="section-title"><h3>日程与待办</h3><span>${entries.length} 项</span></header>${entries.length ? entries.map((i) => occurrenceRow(i, key, state.calendar)).join("") : emptyItems()}</section></div>
      <footer class="fly-foot"><button class="primary" type="button" data-action="add">${icon("plus", 16)} 添加事项</button></footer>
      </div></section>`;
  }
  function editor(item) {
    const isNew = !item.id;
    return `<section class="flyout editor-window" aria-label="事项编辑"><header class="panel-head">${button("cancel-item", "返回", "back")}<h2>${isNew ? "添加事项" : "编辑事项"}</h2>${button("cancel-item", "取消编辑", "close")}</header>
      <form id="item-form" class="form-body"><fieldset class="type-picker"><legend>事项类型</legend>${[
        ["event", "calendar", "日程"],
        ["todo", "task", "待办"],
      ]
        .map(
          ([id, g, label]) =>
            `<label><input type="radio" name="kind" value="${id}" ${item.kind === id ? "checked" : ""}><span>${icon(g, 16)} ${label}</span></label>`,
        )
        .join("")}</fieldset>
      <label class="field">名称<input name="title" maxlength="100" required placeholder="输入事项名称" value="${e(item.title)}"></label>
      <label class="field">日历分类<select name="category">${options({ default: "默认", work: "工作", personal: "个人" }, item.category || "default")}</select></label>
      <label class="field">日期<input type="date" name="date" required min="2026-01-01" max="2027-12-31" value="${e(item.date)}"></label>
      <div class="field-pair"><label class="field">时间<input type="time" name="time" value="${e(item.time || "09:00")}" ${item.time ? "required" : "disabled"}></label><label class="inline-check"><input type="checkbox" name="allDay" ${item.time ? "" : "checked"}> 全天</label></div>
      <div class="field-pair"><label class="field">重复<select name="repeat">${options(repeats, item.repeat)}</select></label><label class="field">提醒<select name="reminder">${options(reminders, item.reminder)}</select></label></div>
      <p class="muted form-hint" id="item-hint">${item.id && item.repeat !== "none" ? "编辑将应用于整个重复系列。" : "全天事项的提醒基准时间为 09:00。"}</p><p class="form-error" role="alert"></p>
      <div class="form-actions">${item.id ? `<button type="button" class="text-button danger" data-action="delete-item">${icon("trash", 15)} 删除</button>` : ""}<span class="spacer"></span><button type="button" class="secondary" data-action="cancel-item">取消</button><button type="submit" class="primary">保存事项</button></div></form></section>`;
  }
  function settingsFrame(title, active, body) {
    return `<section class="settings-window" aria-label="${title}窗口"><header class="settings-bar"><span>${icon("settings", 17)} 设置</span>${button("return", "关闭设置", "close")}</header><div class="settings-layout"><nav class="settings-nav" aria-label="设置页面">${[
      ["general", "settings", "软件设置"],
      ["calendarSettings", "calendar", "日历设置"],
      ["styles", "sliders", "任务栏时钟"],
      ["festivals", "flag", "节日管理"],
    ]
      .map(
        ([id, g, label]) =>
          `<button type="button" data-page="${id}" ${id === active ? 'aria-current="page"' : ""}>${icon(g, 17)}${label}</button>`,
      )
      .join(
        "",
      )}<button type="button" class="nav-dialog" data-action="weather" aria-haspopup="dialog">${icon("cloud", 17)}城市与天气</button></nav><div class="settings-body"><h2>${title}</h2>${body}</div></div></section>`;
  }
  const galleryExample = M.partsFor(M.initialState(), "12:25:31");
  function styles(state, draft, stamp) {
    const sample = draft.gallery === "example";
    const p = sample ? galleryExample : M.partsFor({ ...state, target: draft.target }, stamp);
    const cards = M.presets
      .map(([id, label]) => {
        const lines = M.clockLines(id, p, draft.formats);
        return `<button type="button" class="style-card" data-preset="${id}" aria-pressed="${draft.preset === id}"><span class="preset-name">${label}</span><span data-upper>${e(lines[0])}</span><small data-lower>${e(lines[1])}</small><i aria-hidden="true">${icon("check", 11)}</i></button>`;
      })
      .join("");
    const enabled = state.rules.filter((r) => r.enabled);
    return settingsFrame(
      "任务栏时钟",
      "styles",
      `<div class="gallery-heading"><h3>时钟样式</h3><div class="segments" role="group" aria-label="样式预览时间"><button type="button" data-gallery-mode="example" aria-pressed="${sample}">示例日期</button><button type="button" data-gallery-mode="current" aria-pressed="${!sample}">当前时间</button></div></div>
      <div class="styles-grid" aria-label="九种样式">${cards}</div><p class="gallery-note muted">${sample ? "示例取 2026 年 11 月 26 日，天气使用深圳样例。" : "使用原型的当前演示时间和所选城市。"}</p><div class="custom-head"><label><input type="radio" name="custom-style" id="custom-style" ${draft.preset === "custom" ? "checked" : ""}> 自定义格式</label><button type="button" data-action="format-help" class="text-button">${icon("info", 14)} 格式说明</button></div>
      <div class="field-pair custom-fields"><label class="field">上行<input id="format-top" maxlength="64" value="${e(draft.formats[0])}"></label><label class="field">下行<input id="format-bottom" maxlength="64" value="${e(draft.formats[1])}"></label></div>
      <p class="gallery-note muted">任务栏原位空间不足时，会缩小文字并折行。</p>
      ${draft.preset === "countdown" ? `<label class="field countdown-target">倒数节日<select id="festival-target"><option value="">选择节日</option>${enabled.map((r) => `<option value="${e(r.id)}" ${r.id === draft.target ? "selected" : ""}>${e(r.name)}</option>`).join("")}</select></label>` : ""}
      <div class="apply-bar"><div><small>当前时间预览</small><div id="format-preview"></div></div><button class="primary" type="button" data-action="apply-style">应用样式</button></div><p class="form-error" id="format-error" role="alert"></p>`,
    );
  }
  function festivalRows(state, kind, query) {
    const q = query.trim().toLowerCase();
    const rules = state.rules.filter(
      (r) =>
        r.kind === kind && [r.name, r.alias].filter(Boolean).some((v) => v.toLowerCase().includes(q)),
    );
    return rules.length
      ? rules
          .map(
            (r) =>
              `<div class="festival-row"><div><strong>${e(r.name)}</strong>${r.alias ? ` <small>（${e(r.alias)}）</small>` : ""}<p>${e(M.ruleText(r))}</p></div><span class="spacer"></span>${r.custom ? `<button class="icon-button" type="button" data-festival-edit="${e(r.id)}" aria-label="编辑${e(r.name)}">${icon("edit", 15)}</button>` : ""}<button type="button" role="switch" aria-checked="${r.enabled}" aria-label="${e(r.name)}" data-festival-toggle="${e(r.id)}" class="switch"><i></i></button></div>`,
          )
          .join("")
      : '<p class="empty-line">没有匹配的节日，可以换个名称搜索或新增节日。</p>';
  }
  function festivals(state, kind, query) {
    return settingsFrame(
      "节日管理",
      "festivals",
      `<div class="fest-tools"><div class="segments" aria-label="节日类型">${Object.entries(kindNames)
        .map(
          ([id, name]) =>
            `<button type="button" data-kind="${id}" aria-pressed="${kind === id}">${name}</button>`,
        )
        .join(
          "",
        )}</div><label class="search-box">${icon("search", 16)}<input id="festival-search" type="search" aria-label="搜索节日" placeholder="搜索节日" value="${e(query)}"></label></div><div class="festival-list" id="festival-list">${festivalRows(state, kind, query)}</div><footer class="festival-footer"><span>节日显示同步到月历和时钟</span><button class="primary" type="button" data-action="new-festival">${icon("plus", 15)} 新增节日</button></footer>`,
    );
  }
  function festivalForm(rule) {
    const nums = (n, label) =>
      Object.fromEntries(Array.from({ length: n }, (_, i) => [i + 1, i + 1 + label]));
    return `<form id="festival-form"><header class="panel-head"><h2>${rule.id ? "编辑" : "新增"}节日</h2>${button("dismiss", "关闭", "close")}</header><div class="form-body"><label class="field">名称<input name="name" maxlength="30" required value="${e(rule.name)}"></label><label class="field">别名<input name="alias" maxlength="30" value="${e(rule.alias)}"></label>
      <label class="field">规则<select name="kind">${options(kindNames, rule.kind)}</select></label>
      <div class="field-pair"><label class="field">月份<select name="month">${options(nums(12, "月"), rule.month)}</select></label><label class="field" id="festival-day">日期<input type="number" name="day" min="1" max="31" value="${rule.day || 1}"></label></div>
      <div class="field-pair" id="dynamic-rule"><label class="field">顺序<select name="nth">${options({ 1: "第一个", 2: "第二个", 3: "第三个", 4: "第四个", 5: "第五个", "-1": "最后一个" }, rule.nth || 1)}</select></label><label class="field">星期<select name="dayOfWeek">${options(Object.fromEntries([...M.weekdayNames].map((v, i) => [i, "星期" + v])), rule.dayOfWeek ?? 0)}</select></label></div>
      <p class="form-error" role="alert"></p><div class="form-actions">${rule.id ? button("delete-festival", "删除这个节日", "trash", "icon-button danger") : ""}<span class="spacer"></span><button class="secondary" type="button" data-action="dismiss">取消</button><button class="primary" type="submit">保存节日</button></div></div></form>`;
  }
  return {
    e,
    screens,
    icon,
    options,
    button,
    navbar,
    weatherGlyph,
    month,
    day,
    editor,
    styles,
    settingsFrame,
    festivals,
    festivalRows,
    festivalForm,
  };
})();
