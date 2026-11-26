const SettingsPages = (function () {
  const V = CalendarViews,
    M = CalendarModel;
  const categories = { default: "默认", work: "工作", personal: "个人" };
  const tones = { soft: "轻点", chime: "双音", clear: "清脆" };
  function toggle(group, key, title, value, extra = "") {
    const id = `pref-${group}-${key}`;
    return `<div class="option-row"><label class="option-label" for="${id}">${title}</label><div class="row-controls">${extra}<button type="button" role="switch" class="switch" id="${id}" data-pref="${group}.${key}" aria-label="${title}" aria-checked="${value}"><i></i></button></div></div>`;
  }
  function link(action, label) {
    return `<button type="button" class="text-button" data-action="${action}">${label}</button>`;
  }
  function select(group, key, title, values, selected) {
    return `<label class="option-row"><span>${title}</span><select data-choice="${group}.${key}">${V.options(values, selected)}</select></label>`;
  }
  function section(title, body) {
    return `<section class="option-group"><h3>${title}</h3>${body}</section>`;
  }
  function calendar(state) {
    const c = state.calendar,
      t = (key, title, extra) => toggle("calendar", key, title, c[key], extra);
    const weekOptions = Object.fromEntries([...M.weekdayNames].map((v, i) => [i, "星期" + v]));
    const group = section(
      "日期与日历内容",
      [
        t("showLunar", "显示农历和节日信息"),
        t("showAdjustments", "显示调休安排"),
        t("showOutside", "显示非当前月日期"),
        t("snapMonths", "滚动后对齐月份"),
        t("hour12", "使用 12 小时制"),
        t("showWeeks", "显示周数", link("week-range", c.weekRange ? "已设日期范围" : "自定日期范围")),
        select("calendar", "weekStart", "一周开始于", weekOptions, c.weekStart),
        t("showWeather", "显示天气预报"),
      ].join(""),
    );
    const cli = section(
      "CLI 命令行",
      select("calendar", "cliCategory", "新建日程的默认日历分类", categories, c.cliCategory) +
        `<div class="option-note">${link("cli-example", "试建命令行示例日程")}</div>`,
    );
    const cards = [
      ["tint", "浅色标记"],
      ["solid", "实色填充"],
    ]
      .map(
        ([v, label]) =>
          `<button type="button" class="agenda-style ${v}" data-agenda-style="${v}" aria-pressed="${c.itemStyle === v}"><strong>${label}</strong>${[
            ["项目讨论", "09:00"],
            ["阅读时间", "15:00"],
            ["整理资料", "17:30"],
          ]
            .map(([n, time], i) => `<span class="sample-${i}">${n}<small>${time}</small></span>`)
            .join("")}</button>`,
      )
      .join("");
    return V.settingsFrame(
      "日历设置",
      "calendarSettings",
      group + cli + section("日程内容", `<div class="agenda-styles">${cards}</div>`),
    );
  }
  function general(state) {
    const g = state.general,
      t = (key, title, extra) => toggle("general", key, title, g[key], extra);
    return V.settingsFrame(
      "软件设置",
      "general",
      section(
        "启动与常驻",
        t("autoStart", "随系统自动运行", link("login-preview", "模拟登录")) +
          t("trayIcon", "在系统托盘显示图标"),
      ) +
        section(
          "窗口行为",
          t("closeOnBlur", "点击月历外自动收起") +
            t("closeRelated", "关闭日历时同时关闭其他窗口") +
            t("rememberPosition", "记忆上次日历位置"),
        ) +
        section("隐私保护", t("privacy", "启用隐私锁", g.privacy ? link("lock-now", "立即锁定") : "")) +
        section("操作反馈", t("completionSound", "待办完成提示音", link("sound-settings", "声音设置"))),
    );
  }
  function weekForm(range) {
    return `<form id="range-form"><div class="panel-head"><h2>周数日期范围</h2>${V.button("dismiss", "关闭", "close")}</div><div class="form-body">
      <label class="field">开始日期<input type="date" name="from" required min="2026-01-01" max="2027-12-31" value="${V.e(range?.from || "2026-09-01")}"></label>
      <label class="field">结束日期<input type="date" name="to" required min="2026-01-01" max="2027-12-31" value="${V.e(range?.to || "2027-01-15")}"></label>
      <p class="muted" id="range-description">保留 ISO 周号。只有与起止日期相交的星期行显示周数。</p><p class="form-error" role="alert"></p>
      <div class="form-actions"><button class="text-button" type="button" data-action="reset-range">显示全年周数</button><span class="spacer"></span><button type="submit" class="primary">应用范围</button></div></div></form>`;
  }
  function soundForm(state) {
    const g = state.general;
    return `<div class="panel-head"><h2>声音设置</h2>${V.button("dismiss", "关闭", "close")}</div><div class="form-body"><label class="field">提示音<select id="sound-tone">${V.options(tones, g.tone)}</select></label><label class="field">音量 <output id="sound-level">${g.volume}%</output><input id="sound-volume" type="range" min="0" max="100" step="5" value="${g.volume}"></label><div class="form-actions"><button class="secondary" type="button" data-action="preview-sound">${V.icon("sound", 15)} 试听</button><button class="primary" type="button" data-action="dismiss">完成</button></div></div>`;
  }
  function pinForm(mode) {
    const setup = mode === "setup",
      title = setup ? "启用隐私锁" : mode === "disable" ? "关闭隐私锁" : "解锁个人日历";
    return `<form id="pin-form"><div class="panel-head"><h2>${title}</h2>${V.button("dismiss", "关闭", "close")}</div><div class="form-body"><p class="muted">${setup ? "输入本次演示专用的 4 至 8 位数字。刷新页面后清除。" : "请输入本次演示设置的 PIN。"}</p><label class="field">演示 PIN<input name="pin" type="password" inputmode="numeric" pattern="[0-9]{4,8}" minlength="4" maxlength="8" autocomplete="off" required></label>${setup ? '<label class="field">再输入一次<input name="confirmation" type="password" inputmode="numeric" pattern="[0-9]{4,8}" maxlength="8" autocomplete="off" required></label>' : ""}<p class="form-error" role="alert"></p><div class="form-actions"><button class="secondary" type="button" data-action="dismiss">取消</button><button class="primary" type="submit">${setup ? "启用" : title}</button></div></div></form>`;
  }
  return { calendar, general, weekForm, soundForm, pinForm, categories, tones };
})();
