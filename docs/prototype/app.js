(function () {
  "use strict";
  const M = CalendarModel,
    V = CalendarViews,
    $ = (selector) => document.querySelector(selector);
  const host = $("#window-host"),
    dialog = $("#modal"),
    tray = $("#tray-clock");
  const routeIds = new Set(V.screens.map((row) => row[0]));
  const start = Date.now();
  let state = M.initialState(),
    page = "month",
    returnFromEditor = "day",
    returnFromSettings = "month";
  let draft,
    draftItem,
    festivalKind = "solar",
    query = "",
    festivalId = null,
    toastTimer;
  const settingsIds = new Set(["general", "calendarSettings", "styles", "festivals"]);
  const pinVault = DesktopPreview.sessionPin();
  let locked = false,
    pinMode = "",
    unlockTarget = "",
    otherWindow = null,
    livePosition = null,
    savedPosition = null,
    calendarSize = null,
    scrollTimer,
    wheelDistance = 0;
  const initialHash = location.hash.slice(1);
  const themeFromSystem = matchMedia("(prefers-color-scheme: dark)");
  const dateReminderPreview = ReminderPreview.connect({
    host, dialog, menu: $("#date-reminder-menu"), popup: $("#date-reminder-popup"),
    showDialog: (title, body) => showDialog(simpleDialog(title, body)),
    entries: () => state.dateReminders,
    now: () => new Date(M.today + "T" + tick().stamp).getTime(),
    refresh: render,
    locked: () => locked
  });

  function announce(message) {
    clearTimeout(toastTimer);
    const el = $("#announce");
    el.textContent = message;
    el.classList.add("show");
    toastTimer = setTimeout(() => el.classList.remove("show"), 2200);
  }
  function focusAfter(selector) {
    $(selector)?.focus({ preventScroll: true });
  }
  function paintTheme() {
    document.documentElement.dataset.theme =
      state.theme === "system" ? (themeFromSystem.matches ? "dark" : "light") : state.theme;
  }
  function newItem() {
    return {
      kind: "event",
      category: "default",
      title: "",
      date: state.selected,
      time: "09:00",
      repeat: "none",
      reminder: "off",
      done: [],
    };
  }
  function newDraft() {
    return { preset: state.preset, target: state.target, formats: [...state.formats], gallery: "example" };
  }
  function syncRail() {
    $("#navigation")
      .querySelectorAll("[data-page]")
      .forEach((el) => {
        if (el.dataset.page === page) el.setAttribute("aria-current", "page");
        else el.removeAttribute("aria-current");
      });
    $("#mobile-page").value = page;
    $("#theme").value = state.theme;
    $("#network").value = state.network;
    $("#clock-width").value = state.width;
    tray.setAttribute("aria-expanded", String(["month", "day"].includes(page)));
    $("#optional-tray").hidden = !state.general.trayIcon;
    document.documentElement.dataset.itemStyle = state.calendar.itemStyle;
    document.body.dataset.page = page;
    document.documentElement.dataset.width = state.width;
  }
  function render() {
    const screens = {
      clock: () => "",
      month: () => V.month(state),
      day: () => V.month(state, true) + V.day(state),
      editor: () => V.editor(draftItem),
      styles: () => V.styles(state, draft, tick().stamp),
      festivals: () => V.festivals(state, festivalKind, query),
      calendarSettings: () => SettingsPages.calendar(state),
      general: () => SettingsPages.general(state),
    };
    host.innerHTML = screens[page]();
    if (page === "month" || page === "day") {
      $('[data-action="prev"]').disabled = state.month === M.firstMonth;
      $('[data-action="next"]').disabled = state.month === M.lastMonth;
      const active =
        host.querySelector(`[data-date="${state.selected}"]`) ||
        host.querySelector("[data-date]:not(:disabled)");
      host.querySelectorAll("[data-date]").forEach((el) => {
        el.tabIndex = el === active ? 0 : -1;
      });
    }
    syncRail();
    paintTheme();
    updateClock();
    placeCalendar();
  }
  function placeCalendar() {
    DesktopPreview.place(host, livePosition || (state.general.rememberPosition ? savedPosition : null), calendarSize);
  }
  function go(next, { focus = true, preserveDraft = false } = {}) {
    dateReminderPreview.closeMenu();
    if (!routeIds.has(next)) next = "month";
    if (next === "clock" && !locked && state.general.privacy && page !== "clock") {
      lockNow();
      return;
    }
    if (next === "clock" && !state.general.rememberPosition) livePosition = null;
    if (locked && next !== "clock") {
      unlockTarget = next;
      askPin("unlock");
      return;
    }
    if (next !== "month" && next !== "day") {
      clearTimeout(scrollTimer);
      wheelDistance = 0;
    }
    if (next === "editor" && !preserveDraft) {
      returnFromEditor = page === "month" ? "month" : "day";
      draftItem = newItem();
    }
    if (settingsIds.has(next)) {
      if (!settingsIds.has(page)) returnFromSettings = ["day", "month"].includes(page) ? page : "month";
      otherWindow = next;
    }
    if (next === "styles" && !preserveDraft) draft = newDraft();
    page = next;
    if (location.hash !== "#" + page) history.replaceState(null, "", "#" + page);
    render();
    if (focus) {
      if (page === "clock") tray.focus({ preventScroll: true });
      else if (page === "editor") focusAfter('#item-form [name="title"]');
      else {
        host.tabIndex = -1;
        host.focus({ preventScroll: true });
      }
    }
  }
  function tick() {
    const elapsed = Math.max(0, Math.floor((Date.now() - start) / 1000));
    const total = (10 * 3600 + 32 * 60 + 1 + elapsed) % 86400;
    return {
      stamp: [Math.floor(total / 3600), Math.floor(total / 60) % 60, total % 60].map(M.two).join(":"),
    };
  }
  function fitTrayClock(lines) {
    const top = $("#tray-line1"), bottom = $("#tray-line2");
    function setLine(element, line) {
      if (element.textContent === line) return;
      const parts = line.match(/\S+|\s+/gu) ?? [];
      element.replaceChildren(...parts.map(part => {
        if (!part.trim()) return part;
        const word = document.createElement("span");
        word.className = "clock-word";
        word.textContent = part;
        return word;
      }));
    }
    setLine(top, lines[0]);
    setLine(bottom, lines[1]);
    const css = getComputedStyle(tray);
    const height = tray.clientHeight - parseFloat(css.paddingTop) - parseFloat(css.paddingBottom);
    const fits = (size) => {
      tray.style.setProperty("--clock-font-size", size + "px");
      return top.getBoundingClientRect().height + bottom.getBoundingClientRect().height <= height;
    };
    if (height <= 0 || fits(13)) return;
    let smallest = 1, largest = 13;
    while (largest - smallest > 0.2) {
      const candidate = (smallest + largest) / 2;
      if (fits(candidate)) smallest = candidate;
      else largest = candidate;
    }
    fits(smallest);
  }
  function updateClock() {
    const { stamp } = tick(),
      p = M.partsFor(state, stamp);
    dateReminderPreview.tick(new Date(M.today + "T" + stamp).getTime());
    const lines = locked
      ? [p.short, M.displayDate(M.today)]
      : M.clockLines(state.preset, p, state.formats);
    if (state.network === "cached" && ["weather", "forecast"].includes(state.preset))
      lines[1] += " 缓存";
    if (!lines.some(Boolean)) lines[0] = p.stamp;
    fitTrayClock(lines);
    tray.setAttribute(
      "aria-label",
      "任务栏时钟，" + lines.filter(Boolean).join("，") + "，打开或收起月历",
    );
    if ($("[data-hour-minute]")) {
      $("[data-period]").textContent = state.calendar.hour12
        ? Number(p.hour) < 12
          ? "上午"
          : "下午"
        : "";
      $("[data-hour-minute]").textContent = state.calendar.hour12
        ? (Number(p.hour) % 12 || 12) + ":" + p.minute
        : stamp.slice(0, 5);
      $("[data-seconds]").textContent = ":" + p.second;
    }
    if (page === "styles") updatePreview(stamp);
  }
  function updatePreview(stamp = tick().stamp) {
    const target = $("#format-preview"),
      error = $("#format-error"),
      apply = $('[data-action="apply-style"]');
    const parts = M.partsFor({ ...state, target: draft.target }, stamp);
    if (draft.gallery === "current") {
      host.querySelectorAll("[data-preset]").forEach((card) => {
        const [upper, lower] = M.clockLines(card.dataset.preset, parts, draft.formats);
        card.querySelector("[data-upper]").textContent = upper;
        card.querySelector("[data-lower]").textContent = lower;
      });
    }
    try {
      if (draft.preset === "custom" && !draft.formats.some((f) => f.trim()))
        throw new Error("请至少填写一行时钟格式");
      const lines = M.clockLines(
        draft.preset,
        parts,
        draft.formats,
      );
      target.textContent = lines.filter(Boolean).join("\n");
      error.textContent = "";
      apply.disabled = false;
    } catch (problem) {
      target.textContent = "—";
      error.textContent = problem.message;
      apply.disabled = true;
    }
  }
  function showDialog(html, focusSelector) {
    dialog.innerHTML = html;
    if (!dialog.open) dialog.showModal();
    if (focusSelector) dialog.querySelector(focusSelector)?.focus();
  }
  function simpleDialog(title, body) {
    return `<div class="panel-head"><h2>${V.e(title)}</h2>${V.button("dismiss", "关闭", "close")}</div><div class="dialog-body">${body}</div>`;
  }
  function showWeather() {
    const w = state.network === "empty" ? null : M.weatherSamples[state.city];
    const status = state.network === "cached" ? "离线缓存" : "演示天气";
    const details = w
      ? `<div class="weather-detail">${V.icon(w.condition === "晴" ? "sun" : w.condition === "阴" ? "cloud" : "partly", 45)}<strong>${w.temp}°</strong><div>${w.condition}<small>${w.low}° ～ ${w.high}°</small></div></div><p>美标 AQI ${w.aqi} · ${w.quality}</p><p class="muted">${status} · 11月26日 10:25</p>`
      : `<p class="empty-line">${state.city ? "天气暂不可用，尚无该城市的缓存。" : "选择演示城市后可以查看天气。"}</p>`;
    const cities = V.options({ "": "未设置", 深圳: "深圳", 北京: "北京", 杭州: "杭州" }, state.city);
    showDialog(
      simpleDialog(
        "城市与天气",
        `<label class="field">演示城市<select id="city">${cities}</select></label>
        <label><input type="checkbox" id="direct-weather"${state.directWeatherConnection ? " checked" : ""}> 直连天气服务（演示）</label>${details}`,
      ),
    );
  }
  function showFestival(id = null) {
    festivalId = id;
    const existing = state.rules.find((r) => r.id === id);
    const rule = existing || {
      name: "",
      alias: "",
      kind: festivalKind,
      month: 11,
      day: 26,
      nth: 4,
      dayOfWeek: 4,
    };
    showDialog(V.festivalForm(rule), '[name="name"]');
    syncFestivalFields();
  }
  function syncFestivalFields() {
    const f = $("#festival-form");
    if (!f) return;
    const kind = f.elements.kind.value,
      isDynamic = kind === "dynamic";
    $("#festival-day").hidden = isDynamic;
    $("#dynamic-rule").hidden = !isDynamic;
    f.elements.day.disabled = isDynamic;
    f.elements.day.max =
      kind === "lunar" ? 30 : new Date(Date.UTC(2024, Number(f.elements.month.value), 0)).getUTCDate();
  }
  function refreshRules(focusId) {
    $("#festival-list").innerHTML = V.festivalRows(state, festivalKind, query);
    if (focusId) focusAfter(`[data-festival-toggle="${CSS.escape(focusId)}"]`);
    updateClock();
  }
  function selectDay(key, detail) {
    if (key.slice(0, 7) < M.firstMonth || key.slice(0, 7) > M.lastMonth) return;
    const closeDetails = detail && page === "day" && key === state.selected;
    state.selected = key;
    state.month = key.slice(0, 7);
    state.scrollStart = null;
    if (detail) go(closeDetails ? "month" : "day", { focus: false });
    else render();
    focusAfter(`[data-date="${key}"]`);
  }
  function closeDay() {
    go("month", { focus: false });
    focusAfter('.month-grid [tabindex="0"]');
  }
  function stepMonth(step) {
    const next = M.monthOffset(state.month, step);
    if (next < M.firstMonth || next > M.lastMonth) return;
    const last = Number(M.plusDays(M.monthOffset(next, 1) + "-01", -1).slice(-2));
    state.scrollStart = null;
    state.month = next;
    state.selected = next + "-" + M.two(Math.min(last, Number(state.selected.slice(-2))));
    render();
    focusAfter(
      `[data-action="${step === 1 ? "next" : "prev"}"]:not(:disabled), [data-date="${state.selected}"]`,
    );
  }
  function openItem(id) {
    const item = state.items.find((it) => it.id === id);
    if (!item) return;
    returnFromEditor = page === "month" ? "month" : "day";
    draftItem = { ...item, done: [...item.done] };
    go("editor", { preserveDraft: true });
  }
  function saveItem(form) {
    const fields = form.elements,
      title = fields.title.value.trim();
    if (!title) {
      form.querySelector(".form-error").textContent = "请填写事项名称";
      fields.title.focus();
      return;
    }
    const data = {
      id: draftItem.id || crypto.randomUUID(),
      title,
      kind: fields.kind.value,
      category: fields.category.value,
      date: fields.date.value,
      time: fields.allDay.checked ? "" : fields.time.value,
      repeat: fields.repeat.value,
      reminder: fields.reminder.value,
      done: [],
    };
    if (draftItem.kind === data.kind && data.kind === "todo")
      data.done = draftItem.done.filter((d) => M.occurs(data, d));
    const index = state.items.findIndex((it) => it.id === data.id);
    if (index >= 0) state.items[index] = data;
    else state.items.push(data);
    const selected = index >= 0 && M.occurs(data, state.selected) ? state.selected : data.date;
    state.selected = selected;
    state.month = selected.slice(0, 7);
    state.scrollStart = null;
    go(returnFromEditor);
    announce("事项已保存");
  }
  function saveFestival(form) {
    const f = form.elements,
      name = f.name.value.trim(),
      kind = f.kind.value;
    const error = form.querySelector(".form-error");
    if (!name) {
      error.textContent = "请填写节日名称";
      f.name.focus();
      return;
    }
    if (state.rules.some((r) => r.id !== festivalId && r.name === name)) {
      error.textContent = "已有同名节日，请换一个名称";
      return;
    }
    const existing = state.rules.find((r) => r.id === festivalId);
    const rule = {
      id: festivalId || crypto.randomUUID(),
      custom: true,
      enabled: existing?.enabled ?? true,
      name,
      alias: f.alias.value.trim(),
      kind,
      month: Number(f.month.value),
    };
    if (kind === "dynamic") {
      rule.nth = Number(f.nth.value);
      rule.dayOfWeek = Number(f.dayOfWeek.value);
    } else rule.day = Number(f.day.value);
    if (existing) state.rules[state.rules.indexOf(existing)] = rule;
    else state.rules.push(rule);
    festivalKind = kind;
    query = "";
    dialog.close();
    go("festivals");
    announce("节日已保存");
  }
  function confirmDelete(type) {
    const label = type === "item" ? "事项" : "节日";
    const body =
      type === "item" && draftItem.repeat !== "none"
        ? "这会同时删除整个重复系列。"
        : "确认删除这条" + label + "？";
    showDialog(
      simpleDialog(
        "删除" + label,
        `<p>${body}</p><div class="form-actions"><button type="button" class="secondary" data-action="cancel-delete">取消</button><button type="button" class="primary delete-confirm" data-action="confirm-${type}">删除</button></div>`,
      ),
      '[data-action="cancel-delete"]',
    );
  }
  function askPin(mode) {
    pinMode = mode;
    showDialog(SettingsPages.pinForm(mode), '[name="pin"]');
  }
  async function savePin(form) {
    const button = form.querySelector('[type="submit"]');
    if (button.disabled) return;
    const error = form.querySelector(".form-error"),
      input = form.elements.pin,
      mode = pinMode;
    const pin = input.value;
    if (mode === "setup" && pin !== form.elements.confirmation.value) {
      error.textContent = "两次输入不一致";
      return;
    }
    button.disabled = true;
    try {
      if (mode === "setup") {
        await pinVault.set(pin);
        state.general.privacy = true;
        dialog.close();
        render();
        announce("隐私锁已启用");
      } else if (await pinVault.matches(pin)) {
        dialog.close();
        if (mode === "disable") {
          pinVault.clear();
          state.general.privacy = false;
          render();
        } else {
          locked = false;
          go(unlockTarget || "month");
        }
      } else {
        error.textContent = "PIN 不正确";
        input.select();
      }
    } catch {
      error.textContent = "浏览器无法完成本地验证，请用新版 Edge 或 Chrome 打开原型。";
    } finally {
      button.disabled = false;
    }
  }
  function lockNow() {
    locked = true;
    otherWindow = null;
    livePosition = null;
    go("clock");
    announce("个人日历已锁定");
  }
  function closeCalendar() {
    if (state.general.privacy) {
      lockNow();
      return;
    }
    if (!state.general.rememberPosition) livePosition = null;
    if (state.general.closeRelated) otherWindow = null;
    go(otherWindow || "clock");
  }
  function resetDemo() {
    if (dialog.open) dialog.close();
    dateReminderPreview.reset();
    clearTimeout(scrollTimer);
    locked = false;
    otherWindow = null;
    livePosition = null;
    savedPosition = null;
    calendarSize = null;
    pinVault.clear();
    state = M.initialState();
    festivalKind = "solar";
    query = "";
    go("month");
    announce("演示已重置");
  }
  function toggleOption(control) {
    const [group, key] = control.split(".");
    if (group === "general" && key === "privacy") {
      askPin(state.general.privacy ? "disable" : "setup");
      return;
    }
    state[group][key] = !state[group][key];
    if (key === "rememberPosition" && !state.general.rememberPosition) {
      savedPosition = null;
      livePosition = null;
    }
    if (key === "snapMonths") state.scrollStart = null;
    render();
    focusAfter(`[data-pref="${control}"]`);
  }
  function saveRange(form) {
    const from = form.elements.from.value,
      to = form.elements.to.value;
    if (from > to) {
      form.querySelector(".form-error").textContent = "结束日期需等于或晚于开始日期";
      return;
    }
    state.calendar.weekRange = { from, to };
    dialog.close();
    render();
    announce("周数显示范围已更新");
  }
  function playFeedback() {
    DesktopPreview.playTone(state.general.tone, state.general.volume).catch((problem) =>
      announce(problem.message || "无法播放提示音"),
    );
  }
  function scrollCalendar(event) {
    if ((page !== "month" && page !== "day") || !event.target.closest(".month-grid") || event.ctrlKey || !event.deltaY)
      return;
    event.preventDefault();
    wheelDistance += event.deltaY * (event.deltaMode === 1 ? 16 : event.deltaMode === 2 ? 240 : 1);
    if (Math.abs(wheelDistance) < 40) return;
    const steps =
      Math.sign(wheelDistance) * Math.min(3, Math.max(1, Math.floor(Math.abs(wheelDistance) / 80)));
    wheelDistance = 0;
    const start = M.startOfWeek(state.month + "-01", state.calendar.weekStart);
    const proposed = M.plusDays(state.scrollStart || start, steps * 7);
    const min = M.startOfWeek(M.firstMonth + "-01", state.calendar.weekStart),
      max = M.startOfWeek(M.lastMonth + "-01", state.calendar.weekStart);
    const bounded = proposed < min ? min : proposed > max ? max : proposed;
    state.scrollStart = bounded;
    state.month = M.plusDays(bounded, 17).slice(0, 7);
    render();
    clearTimeout(scrollTimer);
    if (state.calendar.snapMonths)
      scrollTimer = setTimeout(() => {
        if (page === "month" || page === "day") {
          state.scrollStart = null;
          render();
        }
      }, 230);
  }
  function cliExample() {
    const label = SettingsPages.categories[state.calendar.cliCategory];
    const body = `<p>示例命令省略分类参数，将使用「${V.e(label)}」。</p><p><code>Yxrl.exe add --title "项目讨论" --date ${state.selected} --time 09:00</code></p><div class="form-actions"><button class="secondary" type="button" data-action="dismiss">取消</button><button class="primary" type="button" data-action="run-cli">在原型中创建</button></div>`;
    showDialog(simpleDialog("命令行默认分类", body));
  }
  const actions = {
    "close-day": closeDay,
    return: () => {
      otherWindow = null;
      go(returnFromSettings);
    },
    settings: () => go("styles"),
    add: () => go("editor"),
    prev: () => stepMonth(-1),
    next: () => stepMonth(1),
    today: () => selectDay(M.today, false),
    "cancel-item": () => go(returnFromEditor),
    dismiss: () => dialog.close(),
    "new-festival": () => showFestival(),
    weather: showWeather,
    environment: () =>
      showDialog(
        simpleDialog(
          "演示环境",
          '<div class="form-body"><label class="field">外观<select id="mobile-theme">' +
            V.options({ system: "跟随系统", light: "浅色", dark: "深色" }, state.theme) +
            "</select></label>" +
            '<label class="field">时钟宽度<select id="mobile-clock-width">' +
            V.options({ normal: "标准", narrow: "窄" }, state.width) +
            "</select></label>" +
            '<label class="field">天气状态<select id="mobile-network">' +
            V.options(
              { online: "在线样例", cached: "离线有缓存", empty: "离线无缓存" },
              state.network,
            ) +
            "</select></label>" +
            '<button type="button" class="secondary" data-action="reset">重置演示</button></div>',
        ),
      ),
    reset: resetDemo,
    "week-range": () => showDialog(SettingsPages.weekForm(state.calendar.weekRange), '[name="from"]'),
    "reset-range": () => {
      state.calendar.weekRange = null;
      dialog.close();
      render();
      announce("已恢复全年 ISO 周数");
    },
    "sound-settings": () => showDialog(SettingsPages.soundForm(state)),
    "preview-sound": playFeedback,
    "lock-now": lockNow,
    "login-preview": () =>
      showDialog(
        simpleDialog(
          "模拟登录",
          state.general.autoStart
            ? "<p>当前设置会让日历随用户登录启动。</p>"
            : "<p>当前设置需要手动打开日历。</p>",
        ),
      ),
    "cli-example": cliExample,
    "run-cli": () => {
      dialog.close();
      returnFromEditor = "day";
      draftItem = { ...newItem(), title: "项目讨论", category: state.calendar.cliCategory };
      go("editor", { preserveDraft: true });
      $("#item-form").requestSubmit();
    },
    "format-help": () =>
      showDialog(
        simpleDialog(
          "格式说明",
          '<p class="muted">上下两行可分别组合以下字段。</p><dl class="help-list"><dt>时间</dt><dd>HH:mm:ss · h:mm tt</dd><dt>公历</dt><dd>yyyy-M-d · ddd</dd><dt>历法</dt><dd>{农历} {节日} {节气} {周数} {时辰} {倒数}</dd><dt>天气</dt><dd>{城市} {天气} {温度范围} {美标AQI}</dd></dl><p>示例模板 <code>hh:ff:ss 星期W JJ</code></p><p class="muted">本原型约定 ff 为分钟、W 为星期序字、JJ 为当天已启用节日和节气。</p>',
        ),
      ),
    "apply-style": () => {
      state.preset = draft.preset;
      state.target = draft.target;
      state.formats = [...draft.formats];
      updateClock();
      announce("已应用到任务栏时钟");
    },
    "delete-item": () => confirmDelete("item"),
    "delete-festival": () => confirmDelete("festival"),
    "cancel-delete": () => {
      if (page === "festivals") showFestival(festivalId);
      else dialog.close();
    },
    "confirm-item": () => {
      state.items = state.items.filter((it) => it.id !== draftItem.id);
      dialog.close();
      go(returnFromEditor);
      announce("事项已删除");
    },
    "confirm-festival": () => {
      state.rules = state.rules.filter((r) => r.id !== festivalId);
      if (state.target === festivalId) state.target = "";
      dialog.close();
      go("festivals");
      announce("节日已删除");
    },
  };

  document.addEventListener("click", (event) => {
    const btn = event.target.closest("button");
    if (!btn) {
      if (
        !dialog.open &&
        event.target.closest(".desk") &&
        !event.target.closest(".window-host,.mobile-nav") &&
        state.general.closeOnBlur &&
        ["month", "day"].includes(page)
      )
        closeCalendar();
      return;
    }
    const d = btn.dataset;
    if (btn === tray || btn.id === "optional-tray") {
      if (page === "editor") {
        announce("请先保存或取消当前编辑");
        return;
      }
      if (["month", "day"].includes(page)) closeCalendar();
      else go("month");
      return;
    }
    if (btn.id === "reset-demo") {
      actions.reset();
      return;
    }
    if (d.page) {
      go(d.page);
      return;
    }
    if (d.action) {
      actions[d.action]?.();
      return;
    }
    if (d.pref) {
      toggleOption(d.pref);
      return;
    }
    if (d.agendaStyle) {
      state.calendar.itemStyle = d.agendaStyle;
      render();
      focusAfter(`[data-agenda-style="${d.agendaStyle}"]`);
      return;
    }
    if (d.date) {
      selectDay(d.date, true);
      return;
    }
    if (d.edit) {
      openItem(d.edit);
      return;
    }
    if (d.galleryMode) {
      draft.gallery = d.galleryMode;
      render();
      focusAfter(`[data-gallery-mode="${draft.gallery}"]`);
      return;
    }
    if (d.preset) {
      draft.preset = d.preset;
      render();
      focusAfter(`[data-preset="${d.preset}"]`);
      return;
    }
    if (d.kind) {
      festivalKind = d.kind;
      render();
      focusAfter(`[data-kind="${d.kind}"]`);
      return;
    }
    if (d.festivalToggle) {
      const rule = state.rules.find((r) => r.id === d.festivalToggle);
      rule.enabled = !rule.enabled;
      if (!rule.enabled && state.target === rule.id) state.target = "";
      refreshRules(rule.id);
      return;
    }
    if (d.festivalEdit) showFestival(d.festivalEdit);
  });
  document.addEventListener("change", (event) => {
    const el = event.target,
      control = el.id.replace(/^mobile-/, "");
    if (control === "theme") {
      state.theme = el.value;
      syncRail();
      paintTheme();
      return;
    }
    if (control === "network") {
      state.network = el.value;
      if (page === "editor") {
        syncRail();
        updateClock();
      } else render();
      return;
    }
    if (control === "clock-width") {
      state.width = el.value;
      syncRail();
      updateClock();
      return;
    }
    if (el.id === "mobile-page") {
      go(el.value);
      return;
    }
    if (el.dataset.choice) {
      const [group, key] = el.dataset.choice.split(".");
      state[group][key] = key === "weekStart" ? Number(el.value) : el.value;
      if (key === "weekStart") state.scrollStart = null;
      render();
      focusAfter('[data-choice="' + el.dataset.choice + '"]');
      return;
    }
    if (el.id === "sound-tone") {
      state.general.tone = el.value;
      return;
    }
    if (el.id === "direct-weather") {
      state.directWeatherConnection = el.checked;
      return;
    }
    if (el.id === "city") {
      state.city = el.value;
      if (page === "editor") updateClock();
      else render();
      showWeather();
      $("#city").focus();
      return;
    }
    if (el.id === "custom-style") {
      draft.preset = "custom";
      render();
      focusAfter("#format-top");
      return;
    }
    if (el.id === "festival-target") {
      draft.target = el.value;
      render();
      focusAfter("#festival-target");
      return;
    }
    if (el.dataset.done) {
      const item = state.items.find((it) => it.id === el.dataset.done);
      item.done = el.checked
        ? [...new Set([...item.done, state.selected])]
        : item.done.filter((k) => k !== state.selected);
      if (el.checked && state.general.completionSound) playFeedback();
      render();
      focusAfter(`[data-done="${CSS.escape(item.id)}"]`);
      return;
    }
    if (el.form?.id === "item-form" && el.name === "allDay") {
      const time = el.form.elements.time;
      time.disabled = el.checked;
      time.required = !el.checked;
      return;
    }
    if (el.form?.id === "festival-form") syncFestivalFields();
  });
  document.addEventListener("input", (event) => {
    const el = event.target;
    if (el.id === "sound-volume") {
      state.general.volume = Number(el.value);
      $("#sound-level").textContent = el.value + "%";
      return;
    }
    if (el.id === "festival-search") {
      query = el.value;
      refreshRules();
      return;
    }
    if (el.id === "format-top" || el.id === "format-bottom") {
      draft.preset = "custom";
      draft.formats = [$("#format-top").value, $("#format-bottom").value];
      $("#custom-style").checked = true;
      host.querySelectorAll("[data-preset]").forEach((b) => b.setAttribute("aria-pressed", "false"));
      updatePreview();
    }
  });
  document.addEventListener("submit", (event) => {
    event.preventDefault();
    if (event.target.id === "item-form") saveItem(event.target);
    if (event.target.id === "festival-form") saveFestival(event.target);
    if (event.target.id === "range-form") saveRange(event.target);
    if (event.target.id === "pin-form") void savePin(event.target);
  });
  document.addEventListener("keydown", (event) => {
    if (dialog.open) return;
    if (event.key === "Escape") {
      if (page === "editor") go(returnFromEditor);
      else if (settingsIds.has(page)) actions.return();
      else if (page === "day") closeDay();
      else if (page !== "clock") closeCalendar();
      return;
    }
    const cell = event.target.closest("[data-date]");
    const offsets = { ArrowLeft: -1, ArrowRight: 1, ArrowUp: -7, ArrowDown: 7 };
    if (cell && Object.hasOwn(offsets, event.key)) {
      event.preventDefault();
      selectDay(M.plusDays(cell.dataset.date, offsets[event.key]), false);
    }
  });

  $("#navigation").innerHTML = V.navbar();
  $("#mobile-page").innerHTML = V.screens
    .map(([id, , label]) => `<option value="${id}">${label}</option>`)
    .join("");
  $(".caption-monitor").innerHTML = V.icon("monitor", 15);
  $("#reset-demo").innerHTML = V.icon("refresh", 15) + " 重置演示";
  $(".taskbar-apps").innerHTML =
    V.icon("windows", 23) +
    V.icon("search", 20) +
    V.icon("folder", 25) +
    `<span class="taskbar-calendar">${V.icon("calendar", 22)}</span>`;
  $(".tray-symbols").innerHTML =
    V.icon("up", 13) + V.icon("wifi", 16) + V.icon("sound", 16) + V.icon("battery", 19);
  $(".tray-bell").innerHTML = V.icon("bell", 15);
  $("#optional-tray").innerHTML = V.icon("calendar", 16);
  host.addEventListener("wheel", scrollCalendar, { passive: false });
  DesktopPreview.bindDrag(host, (point, size) => {
    livePosition = point;
    if (state.general.rememberPosition) savedPosition = point;
    if (size) calendarSize = size;
    placeCalendar();
  });
  window.addEventListener("blur", () => {
    if (!dialog.open && state.general.closeOnBlur && ["month", "day"].includes(page)) closeCalendar();
  });
  themeFromSystem.addEventListener("change", paintTheme);
  window.addEventListener("resize", () => {
    updateClock();
    placeCalendar();
  });
  window.addEventListener("hashchange", () => go(location.hash.slice(1), { focus: false }));
  go(routeIds.has(initialHash) ? initialHash : "month", { focus: false });
  setInterval(updateClock, 1000);
})();
