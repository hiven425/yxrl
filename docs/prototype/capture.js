const CapturePreview = (function createPreview() {
  "use strict";
  const { e, icon, settingsFrame } = CalendarViews;
  const defaults = {
    start: "Ctrl+1",
    clipboard: "Ctrl+2",
    pin: "Ctrl+T",
    ocr: "Shift+C",
    translate: "Ctrl+Q",
  };
  const labels = {
    start: "开始截图",
    clipboard: "钉住剪贴板图片",
    pin: "钉住选区",
    ocr: "提取文字",
    translate: "打开翻译",
  };
  const names = {
    rect: "矩形",
    arrow: "箭头",
    pen: "画笔",
    text: "文字",
    mosaic: "马赛克",
  };
  const glyphs = {
    rect: "□",
    arrow: "↗",
    pen: "✎",
    text: "T",
    mosaic: "▦",
    undo: "↶",
    redo: "↷",
  };
  const actions = {
    undo: "撤销",
    redo: "重做",
    ocr: "提取文字",
    translate: "翻译",
    pin: "钉住",
    save: "保存",
    copy: "复制",
    close: "取消",
  };
  const samples = [
    ["项目周会", "Project review"],
    ["Friday, 14:30", "周五 14:30"],
    ["Room B · Design review", "B 会议室 · 设计评审"],
  ];
  let keys,
    enabled,
    endpoint,
    model,
    language,
    clipboard,
    editor,
    recording,
    menu,
    panel,
    announce,
    go;
  const pins = new Set();
  function reset() {
    finish();
    panel?.remove();
    panel = null;
    menu?.remove();
    menu = null;
    for (const pin of pins) pin.remove();
    pins.clear();
    keys = { ...defaults };
    enabled = true;
    endpoint = "";
    model = "";
    language = "简体中文";
    clipboard = null;
    recording = null;
  }
  function button(id, label, short = label) {
    return `<button type="button" class="shot-btn" data-shot="${id}" aria-label="${label}" title="${label}">${short}</button>`;
  }
  function settings() {
    const rows = Object.entries(keys)
      .map(
        ([id, key]) =>
          `<div class="shot-setting-row"><span>${labels[id]}<small>${id === "start" || id === "clipboard" ? "全局" : "截图工具内"}</small></span>${button("record-" + id, labels[id] + "快捷键", recording === id ? "请按组合键" : e(key || "已停用"))}${button("clear-" + id, "停用" + labels[id], "停用")}</div>`,
      )
      .join("");
    return settingsFrame(
      "截图与翻译",
      "captureSettings",
      `<label class="shot-setting-row"><b>启用截图工具</b><input id="shot-enabled" type="checkbox" ${enabled ? "checked" : ""}></label><h3>快捷键</h3>${rows}${button("restore", "恢复默认快捷键")}<h3>翻译服务</h3><label class="field">API 基础地址<input id="shot-url" placeholder="http://localhost:11434/v1" value="${e(endpoint)}"></label><label class="field">模型名<input id="shot-model" value="${e(model)}"></label><label class="field">API 密钥（可选）<input type="password" autocomplete="off" placeholder="原型使用演示样例" disabled></label>`,
    );
  }
  function element(tag, cls, parent) {
    const el = document.createElement(tag);
    el.className = cls;
    parent?.append(el);
    return el;
  }
  function canvas(w, h) {
    const c = document.createElement("canvas");
    c.width = Math.max(1, w);
    c.height = Math.max(1, h);
    return c;
  }
  const desk = () => document.querySelector("#desktop");
  function sample(w, h) {
    const c = canvas(w, h),
      g = c.getContext("2d");
    const dark = document.documentElement.dataset.theme === "dark";
    const gradient = g.createLinearGradient(0, 0, w, h);
    gradient.addColorStop(0, dark ? "#182b38" : "#cfe1ec");
    gradient.addColorStop(1, dark ? "#325165" : "#8dacc3");
    g.fillStyle = gradient;
    g.fillRect(0, 0, w, h);
    const width = Math.min(420, w - 32),
      height = Math.min(224, h - 64),
      x = Math.round((w - width) / 2),
      y = Math.max(16, Math.round((h - height) / 2) - 26);
    g.fillStyle = dark ? "#24313c" : "#f9fbfd";
    g.beginPath();
    g.roundRect(x, y, width, height, 12);
    g.fill();
    g.fillStyle = dark ? "#9cb8ce" : "#496478";
    g.font = "13px Segoe UI, Microsoft YaHei UI";
    g.fillText("11 月 26 日    /    今日安排", x + 24, y + 36);
    g.strokeStyle = dark ? "#536675" : "#d8e2e9";
    g.beginPath();
    g.moveTo(x + 24, y + 53);
    g.lineTo(x + width - 24, y + 53);
    g.stroke();
    const words = samples.map(([s], i) => {
      const tx = x + 24,
        ty = y + 92 + i * 38;
      g.font =
        (i === 0 ? "600 24px" : "18px") + " Segoe UI, Microsoft YaHei UI";
      g.fillStyle = dark ? "#edf3f8" : "#253c4c";
      g.fillText(s, tx, ty);
      return { s, x: tx, y: ty - 15, w: g.measureText(s).width, h: 19 };
    });
    return { image: c, words, window: { x, y, w: width, h: height } };
  }
  function rectangle(a, b) {
    return {
      x: Math.min(a.x, b.x),
      y: Math.min(a.y, b.y),
      w: Math.abs(b.x - a.x),
      h: Math.abs(b.y - a.y),
    };
  }
  const clamp = (v, min, max) => Math.max(min, Math.min(v, max));
  const inside = (r, p) =>
    r && p.x >= r.x && p.y >= r.y && p.x <= r.x + r.w && p.y <= r.y + r.h;
  const overlaps = (a, b) =>
    a.x < b.x + b.w && a.x + a.w > b.x && a.y < b.y + b.h && a.y + a.h > b.y;
  function paint(g, m) {
    const a = m.points[0],
      b = m.points.at(-1),
      r = rectangle(a, b);
    g.save();
    g.lineWidth = 3;
    g.strokeStyle = g.fillStyle = "#cd422f";
    g.lineCap = "round";
    if (m.kind === "rect") g.strokeRect(r.x, r.y, r.w, r.h);
    if (m.kind === "pen" || m.kind === "arrow") {
      g.beginPath();
      g.moveTo(a.x, a.y);
      for (const p of m.points.slice(1)) g.lineTo(p.x, p.y);
      g.stroke();
      if (m.kind === "arrow") {
        let t = Math.atan2(b.y - a.y, b.x - a.x);
        g.beginPath();
        g.moveTo(b.x, b.y);
        g.lineTo(b.x - 14 * Math.cos(t - 0.45), b.y - 14 * Math.sin(t - 0.45));
        g.lineTo(b.x - 14 * Math.cos(t + 0.45), b.y - 14 * Math.sin(t + 0.45));
        g.fill();
      }
    }
    if (m.kind === "text") {
      g.font = "bold 18px Segoe UI, Microsoft YaHei UI";
      g.fillText(m.label || "", a.x, a.y + 17);
    }
    if (m.kind === "mosaic" && r.w && r.h) {
      const tmp = canvas(Math.ceil(r.w / 10), Math.ceil(r.h / 10));
      tmp
        .getContext("2d")
        .drawImage(g.canvas, r.x, r.y, r.w, r.h, 0, 0, tmp.width, tmp.height);
      g.imageSmoothingEnabled = false;
      g.drawImage(tmp, r.x, r.y, r.w, r.h);
    }
    g.restore();
  }
  function flat(ed) {
    const r = ed.sel,
      image = canvas(r.w, r.h),
      g = image.getContext("2d");
    g.drawImage(ed.source.image, r.x, r.y, r.w, r.h, 0, 0, r.w, r.h);
    g.translate(-r.x, -r.y);
    ed.marks.forEach((m) => paint(g, m));
    const masks = ed.marks
      .filter((m) => m.kind === "mosaic")
      .map((m) => rectangle(m.points[0], m.points.at(-1)));
    const words = ed.source.words
      .filter((s) => overlaps(r, s) && !masks.some((m) => overlaps(m, s)))
      .map((s) => ({ ...s, x: s.x - r.x, y: s.y - r.y }));
    return { image, words };
  }
  function handles(r) {
    return ["nw", "n", "ne", "w", "e", "sw", "s", "se"].map((k) => ({
      k,
      x: r.x + (k.includes("w") ? 0 : k.includes("e") ? r.w : r.w / 2),
      y: r.y + (k.includes("n") ? 0 : k.includes("s") ? r.h : r.h / 2),
    }));
  }
  function draw() {
    if (!editor) return;
    const ed = editor,
      { c, bar, info, sel: r } = ed,
      g = c.getContext("2d");
    g.drawImage(ed.source.image, 0, 0);
    bar.hidden = info.hidden = !r;
    if (!r) {
      g.fillStyle = "#10182055";
      g.fillRect(0, 0, c.width, c.height);
      return;
    }
    g.save();
    g.beginPath();
    g.rect(r.x, r.y, r.w, r.h);
    g.clip();
    ed.marks.forEach((m) => paint(g, m));
    if (ed.pending) paint(g, ed.pending);
    g.restore();
    if (!ed.pin) {
      g.fillStyle = "#1018208c";
      g.beginPath();
      g.rect(0, 0, c.width, c.height);
      g.rect(r.x, r.y, r.w, r.h);
      g.fill("evenodd");
      g.strokeStyle = "#168be2";
      g.lineWidth = 2;
      g.strokeRect(r.x, r.y, r.w, r.h);
      for (const p of handles(r)) {
        g.fillStyle = "#fff";
        g.fillRect(p.x - 3, p.y - 3, 6, 6);
        g.strokeRect(p.x - 3, p.y - 3, 6, 6);
      }
    }
    info.textContent = `${r.x}, ${r.y}   ·   ${r.w} × ${r.h} px`;
    const view = c.getBoundingClientRect(),
      scaleX = view.width / c.width,
      scaleY = view.height / c.height;
    const bottom = (r.y + r.h) * scaleY;
    const barY =
      bottom + 8 + bar.offsetHeight < ed.layer.clientHeight
        ? bottom + 8
        : Math.max(2, r.y * scaleY - bar.offsetHeight - 8);
    bar.style.left =
      clamp(r.x * scaleX, 2, ed.layer.clientWidth - bar.offsetWidth - 2) + "px";
    bar.style.top = barY + "px";
    info.style.left =
      clamp(r.x * scaleX, 2, ed.layer.clientWidth - info.offsetWidth - 2) +
      "px";
    info.style.top = Math.max(2, r.y * scaleY - info.offsetHeight - 5) + "px";
    bar.querySelectorAll("[data-shot]").forEach((el) => {
      if (names[el.dataset.shot])
        el.setAttribute("aria-pressed", String(ed.tool === el.dataset.shot));
    });
  }
  function finish() {
    if (editor) {
      editor.layer.remove();
      editor = null;
    }
  }
  function start(pin = null) {
    if (!enabled) return announce("截图工具已关闭");
    if (editor) return;
    const layer = element("section", "shot-editor", desk());
    layer.tabIndex = -1;
    layer.setAttribute("aria-label", "截图选区");
    const source = pin
        ? pin.shot
        : sample(layer.clientWidth, layer.clientHeight),
      c = canvas(source.image.width, source.image.height);
    layer.append(c);
    const info = element("output", "shot-info", layer),
      bar = element("div", "shot-bar", layer);
    bar.setAttribute("role", "toolbar");
    bar.innerHTML =
      Object.entries({ ...names, ...actions })
        .filter(([k]) => !pin || k !== "pin")
        .map(([k, v]) => button(k, v, glyphs[k] || v))
        .join("") + (pin ? button("apply", "完成标注") : "");
    editor = {
      pin,
      layer,
      c,
      source,
      info,
      bar,
      marks: [],
      redo: [],
      sel: pin ? { x: 0, y: 0, w: c.width, h: c.height } : null,
      tool: null,
      pending: null,
    };
    function point(ev) {
      const v = c.getBoundingClientRect();
      return {
        x: clamp(
          Math.round(((ev.clientX - v.left) * c.width) / v.width),
          0,
          c.width - 1,
        ),
        y: clamp(
          Math.round(((ev.clientY - v.top) * c.height) / v.height),
          0,
          c.height - 1,
        ),
      };
    }
    let drag = null;
    c.onpointerdown = (ev) => {
      const ed = editor,
        p = point(ev),
        r = ed.sel;
      c.setPointerCapture(ev.pointerId);
      if (ed.tool && inside(r, p)) {
        if (ed.tool === "text") {
          const input = element("input", "shot-label", layer);
          input.setAttribute("aria-label", "标注文字");
          input.style.left = ev.offsetX + "px";
          input.style.top = ev.offsetY + "px";
          input.focus();
          const commit = () => {
            if (input.value) {
              ed.marks.push({ kind: "text", points: [p], label: input.value });
              ed.redo = [];
            }
            input.remove();
            draw();
          };
          input.onkeydown = (k) => {
            if (k.key === "Enter") {
              k.preventDefault();
              commit();
            }
            if (k.key === "Escape") {
              k.stopPropagation();
              input.remove();
            }
          };
          return;
        }
        ed.pending = { kind: ed.tool, points: [p, p] };
        drag = { type: "mark", p };
        return;
      }
      if (ed.pin) return;
      const h =
        r &&
        handles(r).find(
          (pt) => Math.abs(pt.x - p.x) < 7 && Math.abs(pt.y - p.y) < 7,
        );
      drag = {
        type: h ? h.k : inside(r, p) ? "move" : "new",
        p,
        r: r && { ...r },
      };
    };
    c.onpointermove = (ev) => {
      if (!drag) return;
      const ed = editor,
        p = point(ev),
        a = drag.p,
        r = drag.r;
      if (drag.type === "mark") {
        if (ed.pending.kind === "pen") ed.pending.points.push(p);
        else ed.pending.points[1] = p;
      } else if (drag.type === "new") ed.sel = rectangle(a, p);
      else if (drag.type === "move")
        ed.sel = {
          ...r,
          x: clamp(r.x + p.x - a.x, 0, c.width - r.w),
          y: clamp(r.y + p.y - a.y, 0, c.height - r.h),
        };
      else {
        const k = drag.type;
        ed.sel = rectangle(
          { x: k.includes("w") ? p.x : r.x, y: k.includes("n") ? p.y : r.y },
          {
            x: k.includes("e") ? p.x : r.x + r.w,
            y: k.includes("s") ? p.y : r.y + r.h,
          },
        );
      }
      draw();
    };
    c.onpointerup = (ev) => {
      if (!drag) return;
      const ed = editor;
      if (ed.pending) {
        ed.marks.push(ed.pending);
        ed.pending = null;
        ed.redo = [];
      } else if (
        drag.type === "new" &&
        (!ed.sel || ed.sel.w < 3 || ed.sel.h < 3)
      ) {
        ed.sel = inside(source.window, point(ev))
          ? { ...source.window }
          : { x: 0, y: 0, w: c.width, h: c.height };
      }
      drag = null;
      draw();
    };
    layer.focus();
    draw();
  }
  function pinImage(shot) {
    if (!shot) return announce("剪贴板中没有图片");
    const box = element("article", "shot-pin", desk());
    box.tabIndex = 0;
    box.setAttribute("aria-label", "桌面贴图");
    box.shot = shot;
    pins.add(box);
    const img = element("img", "", box);
    img.src = shot.image.toDataURL();
    img.alt = "已钉住的选区";
    img.draggable = false;
    box.style.width =
      Math.min(shot.image.width, desk().clientWidth * 0.8) + "px";
    box.style.left = 8 + pins.size * 2 + "%";
    box.style.top = 15 + pins.size * 3 + "%";
    const tools = element("div", "shot-pin-actions", box);
    tools.innerHTML =
      button("edit-pin", "标注") +
      button("ocr", "提取文字") +
      button("translate", "翻译") +
      button("save", "保存") +
      button("copy", "复制") +
      button("close", "关闭贴图", "×");
    img.onpointerdown = (ev) => {
      const p = {
        x: ev.clientX - box.offsetLeft,
        y: ev.clientY - box.offsetTop,
      };
      img.setPointerCapture(ev.pointerId);
      box.focus();
      img.onpointermove = (v) => {
        box.style.left = v.clientX - p.x + "px";
        box.style.top = v.clientY - p.y + "px";
      };
      img.onpointerup = () => {
        img.onpointermove = null;
      };
    };
    box.onwheel = (ev) => {
      ev.preventDefault();
      box.style.width =
        clamp(
          box.clientWidth * (ev.deltaY < 0 ? 1.1 : 1 / 1.1),
          50,
          desk().clientWidth * 2,
        ) + "px";
    };
    box.focus();
  }
  function textPanel(shot, translate) {
    panel?.remove();
    panel = element("section", "shot-panel", desk());
    panel.setAttribute("aria-label", "文字面板");
    panel.innerHTML = `<header><b>${shot ? "截图文字" : "粘贴文字翻译"}</b>${button("close-panel", "关闭文字面板", "×")}</header><div class="shot-columns">${shot ? '<div class="shot-original"><h3>原图</h3><img alt="识别原图"></div>' : ""}<div class="shot-fields"><label class="field">原文<textarea class="shot-source" rows="4"></textarea></label><div class="shot-row">${button("copy-text", "复制原文")}<label>目标语言 <input class="shot-language" value="${e(language)}"></label>${button("send", "翻译")}</div><label class="field">译文<textarea class="shot-result" rows="3" readonly></textarea></label>${button("copy-result", "复制译文")}<small role="status" class="shot-status"></small></div></div>`;
    if (shot) {
      panel.querySelector("img").src = shot.image.toDataURL();
      panel.querySelector(".shot-source").value = shot.words
        .map((w) => w.s)
        .join("\n");
    }
    panel.querySelector(".shot-status").textContent =
      shot && !shot.words.length ? "演示选区中没有文字" : "离线演示样例";
    panel.querySelector(translate ? ".shot-language" : ".shot-source").focus();
  }
  function execute(id, from) {
    const pin = from?.closest(".shot-pin"),
      ed = editor;
    if (id === "menu") {
      if (menu) {
        menu.remove();
        menu = null;
        return;
      }
      menu = element("div", "shot-menu", desk());
      menu.setAttribute("role", "menu");
      menu.innerHTML =
        button("start", "开始截图") +
        button("clipboard", "钉住剪贴板图片") +
        button("manual", "粘贴文字翻译") +
        button("settings", "截图与翻译设置");
      const p = from.getBoundingClientRect(),
        d = desk().getBoundingClientRect();
      menu.style.right = Math.max(4, d.right - p.right) + "px";
      menu.style.bottom = Math.max(4, d.bottom - p.top + 4) + "px";
      menu.querySelector("button").focus();
      return;
    }
    menu?.remove();
    menu = null;
    if (id === "settings") {
      go("captureSettings");
      return;
    }
    if (
      id === "restore" ||
      id.startsWith("clear-") ||
      id.startsWith("record-")
    ) {
      if (id === "restore") keys = { ...defaults };
      else if (id.startsWith("clear-")) keys[id.slice(6)] = "";
      recording = id.startsWith("record-") ? id.slice(7) : null;
      go("captureSettings");
      return;
    }
    if (id === "close-panel") {
      panel?.remove();
      panel = null;
      return;
    }
    if (id === "close") {
      if (ed) finish();
      else if (pin) {
        pin.remove();
        pins.delete(pin);
      }
      return;
    }
    if (!enabled) return announce("截图工具已关闭");
    if (id === "start") return start();
    if (id === "clipboard") return pinImage(clipboard);
    if (id === "edit-pin") return start(pin);
    if (id === "manual") return textPanel(null, false);
    if (id === "copy-text" || id === "copy-result" || id === "send") {
      const src = panel.querySelector(
        id === "copy-result" ? ".shot-result" : ".shot-source",
      );
      const value =
        src.value.slice(src.selectionStart, src.selectionEnd) || src.value;
      if (id !== "send") {
        if (value)
          navigator.clipboard
            ?.writeText(value)
            .catch(() => announce("请选中文字后复制"));
        return;
      }
      language = panel.querySelector(".shot-language").value.trim();
      const map = new Map(
        samples.flatMap(([a, b]) => [
          [a, b],
          [b, a],
        ]),
      );
      const english = /^(en|english|英语|英文)/i.test(language),
        chinese = /^(zh|简体中文|中文|汉语)/i.test(language);
      const lines = value.split("\n");
      const match = (english || chinese) && lines.every((s) => map.has(s));
      panel.querySelector(".shot-result").value = match
        ? lines
            .map((s) =>
              /[\u3400-\u9fff]/.test(s) === chinese ? s : map.get(s),
            )
            .join("\n")
        : "";
      panel.querySelector(".shot-status").textContent = match
        ? "离线演示译文"
        : "离线原型仅提供示例句译文，桌面版可翻译自定义原文";
      return;
    }
    if (names[id] && ed) {
      ed.tool = ed.tool === id ? null : id;
      draw();
      return;
    }
    if (ed && (id === "undo" || id === "redo")) {
      const a = id === "undo" ? ed.marks : ed.redo,
        b = id === "undo" ? ed.redo : ed.marks;
      if (a.length) b.push(a.pop());
      draw();
      return;
    }
    if (!ed?.sel && !pin) return;
    const shot = ed ? flat(ed) : pin.shot;
    if (id === "apply") {
      ed.pin.shot = shot;
      ed.pin.querySelector("img").src = shot.image.toDataURL();
      finish();
      return;
    }
    if (id === "save") {
      const a = document.createElement("a");
      a.download = "YXRL-截图.png";
      a.href = shot.image.toDataURL();
      a.click();
    }
    if (id === "copy") {
      clipboard = shot;
      announce("已复制到演示剪贴板");
    }
    if (ed) finish();
    if (id === "pin") pinImage(shot);
    if (id === "ocr" || id === "translate") textPanel(shot, id === "translate");
  }
  function keyboard(ev) {
    const target = ev.target,
      base = ev.key.length === 1 ? ev.key.toUpperCase() : ev.key;
    const chord = [
      ev.ctrlKey ? "Ctrl" : "",
      ev.altKey ? "Alt" : "",
      ev.shiftKey ? "Shift" : "",
      ev.metaKey ? "Win" : "",
      base,
    ]
      .filter(Boolean)
      .join("+");
    if (recording) {
      ev.preventDefault();
      ev.stopImmediatePropagation();
      if (["Control", "Shift", "Alt", "Meta"].includes(base)) return;
      if (base !== "Escape") {
        if (
          !chord.includes("+") ||
          Object.entries(keys).some(
            ([id, key]) => id !== recording && key === chord,
          )
        ) {
          announce("请使用未占用的组合键");
          return;
        }
        keys[recording] = chord;
      }
      recording = null;
      go("captureSettings");
      return;
    }
    if (target.closest("input,textarea,select,[contenteditable=true]")) return;
    const pin = target.closest(".shot-pin");
    let id =
      enabled &&
      Object.keys(keys).find(
        (k) =>
          keys[k] === chord &&
          (["start", "clipboard"].includes(k) || editor?.sel || pin),
      );
    if (base === "Escape" && (editor || panel || menu || pin))
      id = panel ? "close-panel" : menu ? "menu" : "close";
    if (
      editor?.sel &&
      ["Enter", "Ctrl+C", "Ctrl+S", "Ctrl+Z", "Ctrl+Y"].includes(chord)
    )
      id = {
        Enter: "copy",
        "Ctrl+C": "copy",
        "Ctrl+S": "save",
        "Ctrl+Z": "undo",
        "Ctrl+Y": "redo",
      }[chord];
    if (pin && base === " ") id = "edit-pin";
    if (id) {
      ev.preventDefault();
      ev.stopImmediatePropagation();
      execute(id, target);
    }
  }
  function connect(options) {
    ({ announce, go } = options);
    reset();
    document.addEventListener("click", (ev) => {
      const b = ev.target.closest("[data-shot]");
      if (b) {
        ev.preventDefault();
        execute(b.dataset.shot, b);
      } else if (menu) {
        menu.remove();
        menu = null;
      }
    });
    document.addEventListener("keydown", keyboard, true);
    document.addEventListener("change", (ev) => {
      const el = ev.target;
      if (el.id === "shot-enabled") enabled = el.checked;
      if (el.id === "shot-url") endpoint = el.value;
      if (el.id === "shot-model") model = el.value;
    });
  }
  return { settings, connect, reset, busy: () => !!(editor || menu || panel) };
})();
