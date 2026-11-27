const CapturePreview = (function createPreview() {
  "use strict";
  const { e, settingsFrame } = CalendarViews;
  const defaults = {
    start: "Ctrl+1",
    clipboard: "Ctrl+2",
    pin: "Ctrl+T",
    ocr: "Shift+C",
    translate: "Ctrl+Q",
    record: "G",
    scroll: "L",
    restorePin: "Ctrl+3",
    document: "Shift+E",
    table: "Shift+Q",
    formula: "Shift+F",
  };
  const labels = {
    start: "开始截图",
    clipboard: "钉住剪贴板内容",
    pin: "钉住选区",
    ocr: "提取文字",
    translate: "打开翻译",
    record: "录制屏幕",
    scroll: "滚动长截图",
    restorePin: "恢复关闭的贴图",
    document: "文档识别",
    table: "图片转表格",
    formula: "公式识别",
  };
  const names = {
    rect: "矩形",
    ellipse: "椭圆",
    arrow: "箭头",
    line: "线段",
    pen: "画笔",
    highlight: "荧光笔",
    text: "添加文字",
    number: "序号",
    mosaic: "马赛克",
  };
  const svgIcon = (body, size = 15) =>
    `<svg aria-hidden="true" focusable="false" width="${size}" height="${size}" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.75" stroke-linecap="round" stroke-linejoin="round">${body}</svg>`;
  const glyphs = {
    rect: svgIcon('<path d="M4 6h16v12H4z"/>'),
    ellipse: svgIcon('<ellipse cx="12" cy="12" rx="8" ry="6.5"/>'),
    arrow: svgIcon('<path d="M5 19 19 5M11 5h8v8"/>'),
    line: svgIcon('<path d="M5 19 19 5"/>'),
    pen: svgIcon('<path d="M16 4l4 4L8 20H4v-4Z M13.5 6.5l4 4"/>'),
    highlight: svgIcon('<path d="M9 11l4 4 7-7-4-4Z M8 12l-4 4 4 4 4-4 M4 21h9"/>'),
    text: svgIcon('<path d="M5 6h14M12 6v13M8.5 19h7"/>'),
    number: svgIcon('<circle cx="12" cy="12" r="8.5"/><path d="M10.3 9.5 12.5 8v8M10.2 16h4.6"/>'),
    mosaic: svgIcon('<path d="M4 4h16v16H4zM4 9.3h16M4 14.7h16M9.3 4v16M14.7 4v16"/>'),
    undo: svgIcon('<path d="M9 14 4 9l5-5M4 9h10.5a5.5 5.5 0 0 1 0 11H11"/>', 14),
    redo: svgIcon('<path d="M15 14l5-5-5-5M20 9H9.5a5.5 5.5 0 0 0 0 11H13"/>', 14),
    close: svgIcon('<path d="M6 6l12 12M18 6 6 18"/>', 13.5),
    refresh: svgIcon('<path d="M20 11a8 8 0 1 0-1.8 5M20 4v7h-7"/>', 13),
    clearMarks: "清空",
  };
  const actions = {
    undo: "撤销",
    redo: "重做",
    clearMarks: "清空标注",
    "recognition-menu": "识别",
    translate: "翻译",
    pin: "钉住",
    scroll: "长截图",
    record: "录屏",
    save: "保存",
    copy: "复制",
    close: "取消",
  };
  const paletteColors = [
    ["红", "#d4402f"],
    ["蓝", "#1967ba"],
    ["绿", "#087960"],
    ["黄", "#f8c226"],
    ["白", "#fff"],
    ["黑", "#000"],
  ];
  const samples = [
    ["项目周会", "Project review"],
    ["Friday, 14:30", "周五 14:30"],
    ["Room B · Design review", "B 会议室 · 设计评审"],
  ];
  const languages = ["简体中文", "繁體中文", "英文", "日文", "韩文", "法文", "德文", "西班牙语", "欧洲葡萄牙语", "印度尼西亚语", "马来语", "意大利语", "俄语", "泰语", "越南语", "阿拉伯语", "土耳其语"];
  const languageOptions = languages.map(name => `<option value="${name}"></option>`).join("");
  const recognitionKinds = ["ocr", "document", "table", "formula"];
  const installed = new Set();
  let keys,
    enabled,
    endpoint,
    model,
    language,
    apiStatus,
    clipboard,
    editor,
    recording,
    recordingClip,
    recoverySample,
    scrollClip,
    menu,
    panel,
    selHistory,
    closedPins,
    announce,
    go;
  const pins = new Set();
  function reset() {
    finish();
    closeRecording();
    desk()?.querySelectorAll(".shot-record-frame").forEach(el => el.remove());
    recoverySample = null;
    closeScroll();
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
    apiStatus = { tone: "muted", text: "空配置下也可使用截图、贴图和离线识别" };
    installed.clear();
    clipboard = null;
    recording = null;
    selHistory = [];
    closedPins = [];
  }
  function button(id, label, short = label, cls = "shot-btn") {
    return `<button type="button" class="${cls}" data-shot="${id}" aria-label="${label}" title="${label}">${short}</button>`;
  }
  function settings() {
    const rows = Object.entries(keys)
      .map(([id, key]) => {
        const isGlobal = ["start", "clipboard", "restorePin"].includes(id);
        const rec = recording === id;
        return `<div class="shot-setting-row"><span class="shot-setting-title">${labels[id]}<em class="shot-scope" data-global="${isGlobal}">${isGlobal ? "全局" : "工具内"}</em></span><div class="shot-row-controls">${button("record-" + id, labels[id] + "快捷键", rec ? "请按快捷键…" : e(key || "未设置"), "shot-btn shot-keycap" + (rec ? " is-recording" : ""))}${button("clear-" + id, "停用" + labels[id], "停用", "shot-btn secondary")}</div></div>`;
      })
      .join("");
    return settingsFrame(
      "截图与翻译",
      "captureSettings",
      `<section class="option-group"><div class="option-row"><span class="option-label shot-enable-label"><strong>启用截图工具</strong><small class="shot-status">开启后可通过全局快捷键（Ctrl+1 截图、Ctrl+2 贴图、Ctrl+3 恢复）与月历入口呼出</small></span><label class="shot-switch-wrap"><input id="shot-enabled" type="checkbox" class="shot-switch-input" ${enabled ? "checked" : ""} aria-label="启用截图工具"><span class="shot-switch-track" aria-hidden="true"><i></i></span></label></div><div class="shot-ocr-banner"><i aria-hidden="true"></i><span>已内置 PP-OCRv5 离线文字识别模型，无需网络或 API 即可直接提取与校对中英文。</span></div></section>
       <section class="option-group shot-shortcuts-group" data-enabled="${enabled}"><div class="shot-group-head"><div><h3>快捷键</h3><small class="shot-status">点击按键帽后按下新组合键，按 Esc 取消录入</small></div>${button("restore", "恢复默认快捷键", "恢复默认快捷键", "shot-btn secondary")}</div>${rows}</section>
       <section class="option-group"><div class="shot-group-head"><div><h3>翻译服务（可选）</h3><small class="shot-status">兼容 OpenAI Chat Completions 接口，密钥保存在 Windows 凭据管理器。</small></div><div class="shot-preset-chips">${button("preset-ollama", "填充 Ollama 本机默认参数", "Ollama 本机", "shot-btn secondary shot-preset-chip")}${button("preset-openai", "填充 OpenAI 官方模板", "OpenAI", "shot-btn secondary shot-preset-chip")}</div></div><div class="shot-api-grid"><label class="field span-2">API 基础地址<input id="shot-url" placeholder="http://localhost:11434/v1" value="${e(endpoint)}"></label><label class="field">模型名<input id="shot-model" placeholder="qwen2.5:7b" value="${e(model)}"></label><label class="field">默认目标语言<input id="shot-default-lang" list="shot-default-lang-options" placeholder="简体中文" value="${e(language)}"><datalist id="shot-default-lang-options">${languageOptions}</datalist></label><label class="field span-2">API 密钥（可选）<input type="password" autocomplete="off" placeholder="原型使用演示样例，桌面版存本机凭据" disabled></label></div><div class="shot-api-test-row">${button("test-api", "测试连接", "测试连接", "shot-btn secondary")}<span class="shot-api-status" data-tone="${apiStatus.tone}">${e(apiStatus.text)}</span></div></section>`,
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
      g.font = (i === 0 ? "600 24px" : "18px") + " Segoe UI, Microsoft YaHei UI";
      g.fillStyle = dark ? "#edf3f8" : "#253c4c";
      g.fillText(s, tx, ty);
      return { s, x: tx, y: ty - 19, w: g.measureText(s).width + 4, h: 25 };
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
  function sampleTarget(ed, point, elementOnly) {
    const { window: sampleWindow, words } = ed.source;
    return (elementOnly && words.find(box => inside(box, point))) ||
      (inside(sampleWindow, point) ? sampleWindow : { x: 0, y: 0, w: ed.c.width, h: ed.c.height });
  }
  const clamp = (v, min, max) => Math.max(min, Math.min(v, max));
  const inside = (r, p) =>
    r && p.x >= r.x && p.y >= r.y && p.x <= r.x + r.w && p.y <= r.y + r.h;
  const overlaps = (a, b) =>
    a.x < b.x + b.w && a.x + a.w > b.x && a.y < b.y + b.h && a.y + a.h > b.y;
  function distSeg(p, a, b) {
    const dx = b.x - a.x, dy = b.y - a.y, l2 = dx * dx + dy * dy;
    if (l2 < 1e-4) return Math.hypot(p.x - a.x, p.y - a.y);
    const t = clamp(((p.x - a.x) * dx + (p.y - a.y) * dy) / l2, 0, 1);
    return Math.hypot(p.x - (a.x + t * dx), p.y - (a.y + t * dy));
  }
  function paint(g, m, offset = { x: 0, y: 0 }) {
    const a = m.points[0],
      b = m.points.at(-1),
      r = rectangle(a, b);
    g.save();
    const color = m.color || "#d4402f";
    const size = m.size || (m.kind === "text" ? 20 : m.kind === "number" ? 28 : m.kind === "highlight" ? 18 : 3);
    g.lineWidth = size;
    g.strokeStyle = g.fillStyle = color;
    g.lineCap = "round";
    if (m.kind === "rect") g.strokeRect(r.x, r.y, r.w, r.h);
    if (m.kind === "ellipse" && r.w && r.h) {
      g.beginPath();
      g.ellipse(r.x + r.w / 2, r.y + r.h / 2, r.w / 2, r.h / 2, 0, 0, Math.PI * 2);
      g.stroke();
    }
    if (["pen", "arrow", "line", "highlight"].includes(m.kind)) {
      if (m.kind === "highlight") g.globalAlpha = .35;
      g.beginPath();
      g.moveTo(a.x, a.y);
      for (const p of m.points.slice(1)) g.lineTo(p.x, p.y);
      g.stroke();
      if (m.kind === "arrow") arrowHead(g, a, b);
    }
    if (m.kind === "text") {
      const lines = m.label.split("\n");
      g.font = markFont(m, size);
      const box = markBounds(g, m);
      if (m.background) {
        g.fillStyle = color === "#fff" || color === "#f8c226" ? "#172530" : "#fff";
        g.fillRect(box.x, box.y, box.w, box.h);
      }
      g.fillStyle = color;
      lines.forEach((line, i) => {
        const y = a.y + size + i * size * 1.25;
        if (m.outline) {
          g.lineWidth = 2;
          g.strokeStyle = color === "#fff" ? "#172530" : "#fff";
          g.strokeText(line, a.x + 4, y);
        }
        g.fillText(line, a.x + 4, y);
      });
    }
    if (m.kind === "number") {
      if (Math.hypot(b.x - a.x, b.y - a.y) > size / 2 + 5) {
        g.lineWidth = Math.max(2, size / 10);
        g.beginPath(); g.moveTo(a.x, a.y); g.lineTo(b.x, b.y); g.stroke();
        arrowHead(g, a, b);
      }
      g.beginPath();
      if (m.square) g.roundRect(a.x - size / 2, a.y - size / 2, size, size, size / 5);
      else g.arc(a.x, a.y, size / 2, 0, 2 * Math.PI);
      g.fill();
      g.fillStyle = ["#fff", "#f8c226"].includes(color) ? "#172530" : "#fff";
      g.textAlign = "center";
      g.textBaseline = "middle";
      g.font = `bold ${size * Math.min(.54, 1.35 / String(m.number).length)}px Segoe UI`;
      g.fillText(m.number, a.x, a.y);
      if (m.label) {
        g.textAlign = "start";
        g.textBaseline = "alphabetic";
        paint(g, { ...m, kind: "text", points: [{ x: a.x + size / 2 + 5, y: a.y - size / 2 }],
          size: Math.max(10, Math.round(size * .55)), background: true });
      }
    }
    if (m.kind === "mosaic" && r.w && r.h) {
      const tmp = canvas(Math.ceil(r.w / 10), Math.ceil(r.h / 10));
      tmp
        .getContext("2d")
        .drawImage(
          g.canvas,
          r.x - offset.x,
          r.y - offset.y,
          r.w,
          r.h,
          0,
          0,
          tmp.width,
          tmp.height,
        );
      g.imageSmoothingEnabled = false;
      g.drawImage(tmp, r.x, r.y, r.w, r.h);
    }
    g.restore();
  }
  function arrowHead(g, a, b) {
    const angle = Math.atan2(b.y - a.y, b.x - a.x);
    g.beginPath();
    g.moveTo(b.x, b.y);
    g.lineTo(b.x - 14 * Math.cos(angle - .45), b.y - 14 * Math.sin(angle - .45));
    g.lineTo(b.x - 14 * Math.cos(angle + .45), b.y - 14 * Math.sin(angle + .45));
    g.fill();
  }
  function markBounds(g, m) {
    const [a, b] = [m.points[0], m.points.at(-1)], size = m.size || (m.kind === "text" ? 20 : m.kind === "number" ? 28 : 3);
    if (m.kind === "number") return { x: a.x - size / 2, y: a.y - size / 2, w: size, h: size };
    if (m.kind === "text") {
      g.save();
      g.font = markFont(m, size);
      const lines = m.label.split("\n");
      const w = Math.max(...lines.map(line => g.measureText(line).width)) + 8;
      g.restore();
      return { x: a.x, y: a.y, w, h: lines.length * size * 1.25 };
    }
    const r = rectangle(a, b), pad = Math.max(4, size);
    return { x: r.x - pad / 2, y: r.y - pad / 2, w: r.w + pad, h: r.h + pad };
  }
  function markHandles(m) {
    if (!m) return [];
    if (["line", "arrow"].includes(m.kind))
      return [{ k: "start", ...m.points[0] }, { k: "end", ...m.points.at(-1) }];
    if (["rect", "ellipse", "mosaic"].includes(m.kind)) {
      const r = rectangle(m.points[0], m.points.at(-1));
      return [
        { k: "nw", x: r.x, y: r.y },
        { k: "ne", x: r.x + r.w, y: r.y },
        { k: "sw", x: r.x, y: r.y + r.h },
        { k: "se", x: r.x + r.w, y: r.y + r.h },
      ];
    }
    return [];
  }
  function hitMark(g, kind, p, list) {
    if (["pen", "highlight"].includes(kind)) return null;
    return list.findLast(m => {
      if (m.kind !== kind) return false;
      if (kind === "text" || kind === "number") return inside(markBounds(g, m), p);
      const tol = Math.max(8, (m.size || 3) + 5);
      if (kind === "line" || kind === "arrow") return distSeg(p, m.points[0], m.points.at(-1)) <= tol;
      const r = rectangle(m.points[0], m.points.at(-1));
      const outer = { x: r.x - tol, y: r.y - tol, w: r.w + tol * 2, h: r.h + tol * 2 };
      if (!inside(outer, p)) return false;
      if (kind === "mosaic" || r.w <= tol * 2 || r.h <= tol * 2) return true;
      const inner = { x: r.x + tol, y: r.y + tol, w: r.w - tol * 2, h: r.h - tol * 2 };
      return !inside(inner, p);
    }) || null;
  }
  const markFont = (mark, size) => `${mark.italic ? "italic " : ""}${mark.bold ? "bold " : ""}${size}px ${mark.font || "Segoe UI, Microsoft YaHei UI"}`;
  const nextNumber = ed => ed.next || Math.min(9999, Math.max(0, ...ed.marks.map(m => m.number || 0)) + 1);
  function changeMarks(ed, next) {
    ed.undo.push(ed.marks);
    ed.marks = next;
    ed.redo = [];
  }
  function editMark(ed, before, after) {
    changeMarks(ed, after ? ed.marks.map(m => m === before ? after : m) : ed.marks.filter(m => m !== before));
    ed.selected = after;
  }
  function flat(ed) {
    const r = ed.sel,
      image = canvas(r.w, r.h),
      g = image.getContext("2d");
    g.drawImage(ed.source.image, r.x, r.y, r.w, r.h, 0, 0, r.w, r.h);
    const original = canvas(r.w, r.h);
    original.getContext("2d").drawImage(image, 0, 0);
    g.translate(-r.x, -r.y);
    ed.marks.forEach((m) => paint(g, m, r));
    const masks = ed.marks
      .filter((m) => m.kind === "mosaic")
      .map((m) => rectangle(m.points[0], m.points.at(-1)));
    const originalWords = ed.source.words.filter(s => overlaps(r, s));
    const localWords = list => list.map((s) => ({ ...s, x: s.x - r.x, y: s.y - r.y }));
    return {
      image,
      words: localWords(originalWords.filter(s => !masks.some(m => overlaps(m, s)))),
      original: { image: original, words: localWords(originalWords) },
      marks: ed.marks.map(m => ({ ...m, points: m.points.map(p => ({ x: p.x - r.x, y: p.y - r.y })) })),
    };
  }
  function handles(r) {
    return ["nw", "n", "ne", "w", "e", "sw", "s", "se"].map((k) => ({
      k,
      x: r.x + (k.includes("w") ? 0 : k.includes("e") ? r.w : r.w / 2),
      y: r.y + (k.includes("n") ? 0 : k.includes("s") ? r.h : r.h / 2),
    }));
  }
  function drawRoundHandle(g, x, y) {
    g.save();
    g.beginPath();
    g.arc(x, y + 0.8, 4.4, 0, Math.PI * 2);
    g.fillStyle = "rgb(14 22 32 / 28%)";
    g.fill();
    g.beginPath();
    g.arc(x, y, 3.8, 0, Math.PI * 2);
    g.fillStyle = "#fff";
    g.fill();
    g.lineWidth = 1.5;
    g.strokeStyle = "#168be2";
    g.stroke();
    g.restore();
  }
  function draw() {
    if (!editor) return;
    const ed = editor,
      { c, bar, info, sel: r } = ed,
      g = c.getContext("2d");
    g.drawImage(ed.source.image, 0, 0);
    const showChrome = !ed.barsHidden && !!r;
    bar.hidden = info.hidden = !showChrome;
    if (!r) {
      g.fillStyle = "#10182055";
      g.fillRect(0, 0, c.width, c.height);
      if (ed.hover) {
        g.strokeStyle = "#168be2";
        g.lineWidth = 2;
        g.strokeRect(ed.hover.x, ed.hover.y, ed.hover.w, ed.hover.h);
      }
    } else {
      g.save();
      g.beginPath();
      g.rect(r.x, r.y, r.w, r.h);
      g.clip();
      ed.marks.forEach(m => paint(g, m === ed.selected && ed.moving && ed.pending ? ed.pending : m));
      if (ed.pending && !ed.moving) paint(g, ed.pending);
      if (ed.selected) {
        const activeMark = ed.duplicating ? ed.selected : (ed.pending || ed.selected);
        const box = markBounds(g, activeMark);
        g.lineWidth = 1.5;
        g.strokeStyle = "#168be2";
        g.strokeRect(box.x, box.y, box.w, box.h);
        for (const h of markHandles(activeMark)) {
          drawRoundHandle(g, h.x, h.y);
        }
      }
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
          drawRoundHandle(g, p.x, p.y);
        }
      }
    }
    if (ed.loupe && !ed.pin && ed.pointer)
      drawLoupe(g, ed.source.image, ed.pointer, r);
    if (!r) return;

    info.firstChild.textContent = `${r.x}, ${r.y}   ·   ${r.w} × ${r.h} px`;
    ed.styles.hidden = !ed.tool || ed.tool === "mosaic";
    ed.styles.querySelectorAll("[data-mark-kind]").forEach(control => control.hidden = control.dataset.markKind !== ed.tool);
    const count = ed.styles.querySelector("#shot-serial");
    if (document.activeElement !== count) count.value = ed.selected?.number || nextNumber(ed);
    ed.styles.querySelector("#shot-serial-label").textContent = ed.selected ? "当前号" : "下一号";
    const configured = ed.selected || ed.appearance;
    ed.styles.querySelector('[data-shot="number-note"]').hidden = !ed.selected || ed.selected.kind !== "number";
    const activeColor = configured.color || "#d4402f";
    ed.styles.querySelectorAll(".shot-swatch").forEach(sw => sw.setAttribute("aria-pressed", String(sw.dataset.swatch === activeColor)));
    for (const key of ["size", "font", "bold", "italic", "outline", "background", "square"]) {
      const control = ed.styles.querySelector(`[data-appearance="${key}"]`);
      if (control.type === "checkbox") control.checked = !!configured[key];
      else control.value = configured[key] || ({ size: ed.tool === "number" ? 28 : 20, font: "" })[key];
    }
    const clearBtn = bar.querySelector('[data-shot="clearMarks"]');
    if (clearBtn) clearBtn.disabled = !ed.marks.length;
    bar.querySelector('[data-shot="undo"]').disabled = !ed.undo.length;
    bar.querySelector('[data-shot="redo"]').disabled = !ed.redo.length;

    const view = c.getBoundingClientRect(),
      scaleX = view.width / c.width,
      scaleY = view.height / c.height;
    const bottom = (r.y + r.h) * scaleY;
    const barY =
      bottom + 8 + bar.offsetHeight < ed.layer.clientHeight
        ? bottom + 8
        : Math.max(2, r.y * scaleY - bar.offsetHeight - 8);
    bar.style.left = clamp(r.x * scaleX, 2, ed.layer.clientWidth - bar.offsetWidth - 2) + "px";
    bar.style.top = barY + "px";
    info.style.left = clamp(r.x * scaleX, 2, ed.layer.clientWidth - info.offsetWidth - 2) + "px";
    let infoY = r.y * scaleY - info.offsetHeight - 5;
    if (infoY < 2 || (infoY < barY + bar.offsetHeight && infoY + info.offsetHeight > barY))
      infoY = r.y * scaleY + 4;
    info.style.top = clamp(infoY, 2, ed.layer.clientHeight - info.offsetHeight - 2) + "px";
    bar.querySelectorAll("[data-shot]").forEach((el) => {
      if (names[el.dataset.shot])
        el.setAttribute("aria-pressed", String(ed.tool === el.dataset.shot));
    });
  }
  function drawLoupe(g, imgCanvas, p, sel) {
    g.save();
    g.strokeStyle = "#1893eeaa";
    g.lineWidth = 1;
    g.setLineDash([4, 3]);
    g.beginPath();
    g.moveTo(0, p.y); g.lineTo(g.canvas.width, p.y);
    g.moveTo(p.x, 0); g.lineTo(p.x, g.canvas.height);
    if (sel) {
      g.moveTo(sel.x + sel.w / 2, sel.y); g.lineTo(sel.x + sel.w / 2, sel.y + sel.h);
      g.moveTo(sel.x, sel.y + sel.h / 2); g.lineTo(sel.x + sel.w, sel.y + sel.h / 2);
    }
    g.stroke();
    g.setLineDash([]);
    const size = 114, infoH = 52;
    const lx = clamp(p.x + 16 + size <= g.canvas.width - 4 ? p.x + 16 : p.x - size - 16, 4, g.canvas.width - size - 4);
    const ly = clamp(p.y + 16 + size + infoH <= g.canvas.height - 4 ? p.y + 16 : p.y - size - infoH - 16, 4, g.canvas.height - size - infoH - 4);
    g.fillStyle = "rgb(22 30 38 / 92%)";
    g.beginPath();
    g.roundRect(lx, ly, size, size + infoH, 6);
    g.fill();
    g.imageSmoothingEnabled = false;
    const sx = clamp(p.x - 5, 0, Math.max(0, imgCanvas.width - 11));
    const sy = clamp(p.y - 5, 0, Math.max(0, imgCanvas.height - 11));
    g.drawImage(imgCanvas, sx, sy, 11, 11, lx + 1, ly + 1, size - 2, size - 2);
    g.strokeStyle = "#1893ee";
    const pixel = (size - 2) / 11;
    g.strokeRect(lx + 1 + (p.x - sx) * pixel, ly + 1 + (p.y - sy) * pixel, pixel, pixel);
    const val = imgCanvas.getContext("2d").getImageData(p.x, p.y, 1, 1).data;
    const color = colorValues(val)[editor.colorFormat || 0];
    g.fillStyle = "#fff";
    g.font = "11px Segoe UI";
    g.fillText(`${p.x},${p.y}`, lx + 6, ly + size + 13);
    g.fillText(color, lx + 6, ly + size + 28, size - 12);
    g.fillStyle = "#bcc8d4";
    g.font = "10px Segoe UI";
    g.fillText("C 复制 · Shift 切换", lx + 6, ly + size + 44);
    g.restore();
  }
  function colorValues([red, green, blue]) {
    const [r, g, b] = [red, green, blue].map(n => n / 255);
    const max = Math.max(r, g, b), min = Math.min(r, g, b);
    const delta = max - min, light = (max + min) / 2;
    let hue = 0;
    if (delta) {
      if (max === r) hue = (g - b) / delta;
      else if (max === g) hue = (b - r) / delta + 2;
      else hue = (r - g) / delta + 4;
    }
    const degrees = Math.round((hue * 60 + 360) % 360) % 360;
    const percent = n => Math.round(n * 100) + "%";
    return [
      "#" + [red, green, blue].map(n => n.toString(16).padStart(2, "0")).join("").toUpperCase(),
      `rgb(${red}, ${green}, ${blue})`,
      `hsv(${degrees}, ${percent(max ? delta / max : 0)}, ${percent(max)})`,
      `hsl(${degrees}, ${percent(delta ? delta / (1 - Math.abs(2 * light - 1)) : 0)}, ${percent(light)})`,
    ];
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
        ? pin.shot.original
        : sample(layer.clientWidth, layer.clientHeight),
      c = canvas(source.image.width, source.image.height);
    layer.append(c);
    const info = element("output", "shot-info", layer);
    info.innerHTML = `<span></span>${!pin ? button("refresh", "刷新画面 (F5)", glyphs.refresh, "shot-btn shot-refresh-btn") : ""}`;
    const bar = element("div", "shot-bar", layer);
    const choices = element("div", "shot-tool-buttons", bar);
    choices.setAttribute("role", "toolbar");
    const toolEntries = Object.entries(names).map(([k, v]) => button(k, v, glyphs[k] || v, "shot-btn shot-tool-icon"));
    const actionEntries = Object.entries(actions)
      .filter(([k]) => !pin || !["pin", "record", "scroll"].includes(k))
      .map(([k, v]) => {
        if (k === "undo" || k === "redo") return button(k, v, glyphs[k], "shot-btn shot-tool-icon");
        if (k === "close") return pin ? button("apply", "完成标注") : button(k, v, glyphs.close, "shot-btn shot-tool-icon");
        return button(k, v, glyphs[k] || v, k === "copy" ? "shot-btn primary" : "shot-btn");
      });
    choices.innerHTML =
      toolEntries.join("") +
      '<span class="shot-sep" aria-hidden="true"></span>' +
      actionEntries.slice(0, 3).join("") +
      '<span class="shot-sep" aria-hidden="true"></span>' +
      actionEntries.slice(3).join("");
    const styles = element("div", "shot-style-options", bar);
    const swatches = paletteColors.map(([n, hex]) => `<button type="button" class="shot-swatch" data-swatch="${hex}" aria-label="颜色 ${n}" title="${n}"><i style="background:${hex}"></i></button>`).join("");
    styles.innerHTML = `<span class="shot-swatches">${swatches}</span>
      <label data-mark-kind="number"><span id="shot-serial-label">下一号</span> <input id="shot-serial" type="number" min="1" max="9999" aria-label="标注序号"></label>
      <label>大小 <select data-appearance="size" aria-label="标注大小">${[2,3,6,12,16,20,24,28,32,40,48,64,80].map(value => `<option>${value}</option>`).join("")}</select></label>
      <label data-mark-kind="text">字体 <select data-appearance="font" aria-label="标注字体"><option value="">系统字体</option><option>Segoe UI</option><option>Microsoft YaHei UI</option><option>Consolas</option></select></label>
      <label data-mark-kind="text" class="shot-pill-opt"><input type="checkbox" data-appearance="bold"><span>粗体</span></label>
      <label data-mark-kind="text" class="shot-pill-opt"><input type="checkbox" data-appearance="italic"><span>斜体</span></label>
      <label data-mark-kind="text" class="shot-pill-opt"><input type="checkbox" data-appearance="outline"><span>描边</span></label>
      <label data-mark-kind="text" class="shot-pill-opt"><input type="checkbox" data-appearance="background"><span>背景</span></label>
      <label data-mark-kind="number" class="shot-pill-opt"><input type="checkbox" data-appearance="square"><span>方形</span></label>
      ${button("number-note", "编辑编号说明", "编号说明")}`;
    editor = {
      pin,
      layer,
      c,
      source,
      info,
      bar,
      styles,
      marks: pin?.shot.marks || [],
      undo: [],
      redo: [],
      sel: pin ? { x: 0, y: 0, w: c.width, h: c.height } : null,
      tool: null,
      pending: null,
      selected: null,
      appearance: {},
      next: null,
      barsHidden: false,
      loupe: false,
      hexColor: true,
      historyIndex: -1,
    };
    styles.onclick = ev => {
      const sw = ev.target.closest(".shot-swatch");
      if (!sw) return;
      const color = sw.dataset.swatch;
      editor.appearance = { ...editor.appearance, color };
      if (editor.selected) editMark(editor, editor.selected, { ...editor.selected, color });
      draw();
    };
    styles.onchange = ev => {
      const ed = editor, input = ev.target, key = input.dataset.appearance;
      if (input.id === "shot-serial") {
        const number = Number(input.value);
        if (Number.isInteger(number) && number >= 1 && number <= 9999) {
          if (ed.selected) editMark(ed, ed.selected, { ...ed.selected, number });
          else ed.next = number;
        }
      } else if (key) {
        const value = input.type === "checkbox" ? input.checked : key === "size" ? Number(input.value) : input.value;
        ed.appearance = { ...ed.appearance, [key]: value };
        if (ed.selected) editMark(ed, ed.selected, { ...ed.selected, [key]: value });
      }
      draw();
    };
    function point(ev) {
      const v = c.getBoundingClientRect();
      return {
        x: clamp(Math.round(((ev.clientX - v.left) * c.width) / v.width), 0, c.width - 1),
        y: clamp(Math.round(((ev.clientY - v.top) * c.height) / v.height), 0, c.height - 1),
      };
    }
    let drag = null;
    c.onpointerdown = (ev) => {
      const ed = editor,
        p = point(ev),
        r = ed.sel;
      if (ed.tool && inside(r, p)) {
        const mh = markHandles(ed.selected).find(h => Math.hypot(h.x - p.x, h.y - p.y) <= 8);
        if (mh) {
          drag = { type: "mark-handle", k: mh.k, p };
          c.setPointerCapture(ev.pointerId);
          draw();
          return;
        }
        const hit = hitMark(c.getContext("2d"), ed.tool, p, ed.marks);
        ed.selected = hit;
        if (hit) {
          if (ev.altKey) {
            drag = { type: "duplicate", p };
            ed.duplicating = true;
            ed.moving = false;
            ed.pending = { ...hit, number: hit.kind === "number" ? nextNumber(ed) : hit.number };
          } else {
            drag = { type: "edit", p };
            ed.moving = true;
          }
          c.setPointerCapture(ev.pointerId);
          draw();
          return;
        }
        if (ed.tool === "text") {
          textEditor(ed, p);
          return;
        }
        ed.pending = { ...ed.appearance, kind: ed.tool, points: [p, p], number: ed.tool === "number" ? nextNumber(ed) : 0 };
        drag = { type: "mark", p };
        c.setPointerCapture(ev.pointerId);
        return;
      }
      if (ed.pin) return;
      ed.selected = null;
      c.setPointerCapture(ev.pointerId);
      const h = r && handles(r).find((pt) => Math.abs(pt.x - p.x) < 7 && Math.abs(pt.y - p.y) < 7);
      drag = {
        type: h ? h.k : inside(r, p) ? "move" : "new",
        p,
        r: r && { ...r },
      };
    };
    c.onpointermove = (ev) => {
      if (editor) editor.pointer = point(ev);
      if (!drag) {
        if (editor && !editor.sel) {
          editor.hover = sampleTarget(editor, editor.pointer, ev.ctrlKey);
          draw();
        } else if (editor?.loupe) draw();
        return;
      }
      const ed = editor,
        p = point(ev),
        a = drag.p,
        r = drag.r;
      if (drag.type === "edit" || drag.type === "duplicate") {
        let dx = p.x - a.x, dy = p.y - a.y;
        if (ev.shiftKey) {
          if (Math.abs(dx) >= Math.abs(dy)) dy = 0; else dx = 0;
        }
        ed.pending = {
          ...ed.selected,
          number: drag.type === "duplicate" && ed.selected.kind === "number" ? nextNumber(ed) : ed.selected.number,
          points: ed.selected.points.map(pt => ({ x: pt.x + dx, y: pt.y + dy })),
        };
      } else if (drag.type === "mark-handle") {
        const m = ed.selected;
        if (["line", "arrow"].includes(m.kind)) {
          ed.pending = { ...m, points: drag.k === "start" ? [p, m.points.at(-1)] : [m.points[0], p] };
        } else {
          const rect = rectangle(m.points[0], m.points.at(-1));
          const fx = drag.k.includes("w") ? rect.x + rect.w : rect.x;
          const fy = drag.k.includes("n") ? rect.y + rect.h : rect.y;
          ed.pending = { ...m, points: [{ x: fx, y: fy }, p] };
        }
      } else if (drag.type === "mark") {
        if (["pen", "highlight"].includes(ed.pending.kind)) ed.pending.points.push(p);
        else ed.pending.points[1] = p;
      } else if (drag.type === "new") {
        let ep = p;
        if (ev.shiftKey) {
          const s = Math.max(Math.abs(p.x - a.x), Math.abs(p.y - a.y));
          ep = { x: clamp(a.x + (p.x >= a.x ? s : -s), 0, c.width), y: clamp(a.y + (p.y >= a.y ? s : -s), 0, c.height) };
        }
        ed.sel = rectangle(a, ep);
      } else if (drag.type === "move") {
        let dx = p.x - a.x, dy = p.y - a.y;
        if (ev.shiftKey) {
          if (Math.abs(dx) >= Math.abs(dy)) dy = 0; else dx = 0;
        }
        ed.sel = { ...r, x: clamp(r.x + dx, 0, c.width - r.w), y: clamp(r.y + dy, 0, c.height - r.h) };
      } else {
        const k = drag.type;
        if (ev.altKey) {
          const cx = r.x + r.w / 2, cy = r.y + r.h / 2;
          const hw = (k.includes("w") || k.includes("e")) ? clamp(Math.abs(p.x - cx), 2, Math.min(cx, c.width - cx)) : r.w / 2;
          const hh = (k.includes("n") || k.includes("s")) ? clamp(Math.abs(p.y - cy), 2, Math.min(cy, c.height - cy)) : r.h / 2;
          ed.sel = rectangle({ x: Math.round(cx - hw), y: Math.round(cy - hh) }, { x: Math.round(cx + hw), y: Math.round(cy + hh) });
        } else {
          ed.sel = rectangle(
            { x: k.includes("w") ? p.x : r.x, y: k.includes("n") ? p.y : r.y },
            { x: k.includes("e") ? p.x : r.x + r.w, y: k.includes("s") ? p.y : r.y + r.h },
          );
        }
      }
      draw();
    };
    c.onpointerup = (ev) => {
      if (!drag) return;
      const ed = editor;
      if (ed.pending) {
        if (ed.moving || drag.type === "mark-handle") editMark(ed, ed.selected, ed.pending);
        else if (ed.duplicating) {
          if (Math.hypot(ed.pending.points[0].x - ed.selected.points[0].x, ed.pending.points[0].y - ed.selected.points[0].y) >= 2) {
            changeMarks(ed, [...ed.marks, ed.pending]);
            ed.selected = ed.pending;
          }
        } else {
          changeMarks(ed, [...ed.marks, ed.pending]);
          if (ed.pending.kind === "number" && ed.next) ed.next = Math.min(9999, ed.next + 1);
        }
        ed.pending = null;
      } else if (drag.type === "new" && (!ed.sel || ed.sel.w < 3 || ed.sel.h < 3)) {
        ed.sel = { ...sampleTarget(ed, point(ev), ev.ctrlKey) };
      }
      ed.moving = false;
      ed.duplicating = false;
      drag = null;
      draw();
    };
    c.ondblclick = ev => {
      const ed = editor;
      if (!ed) return;
      const p = point(ev);
      if (["text", "number"].includes(ed.tool)) {
        const hit = hitMark(c.getContext("2d"), ed.tool, p, ed.marks);
        if (hit) { textEditor(ed, p, hit); return; }
      }
      if (!ed.tool && !ed.pin && inside(ed.sel, p)) execute("copy");
    };
    c.onwheel = ev => {
      const ed = editor;
      if (!ed || ed.pin || ed.tool || !ed.sel) return;
      ev.preventDefault();
      const factor = ev.deltaY < 0 ? 1.05 : 1 / 1.05;
      const nw = clamp(Math.round(ed.sel.w * factor), 8, c.width);
      const nh = clamp(Math.round(ed.sel.h * factor), 8, c.height);
      const cx = ed.sel.x + ed.sel.w / 2, cy = ed.sel.y + ed.sel.h / 2;
      ed.sel = { x: clamp(Math.round(cx - nw / 2), 0, c.width - nw), y: clamp(Math.round(cy - nh / 2), 0, c.height - nh), w: nw, h: nh };
      draw();
    };
    layer.focus();
    draw();
  }
  function textEditor(ed, point, mark = null) {
    ed.layer.querySelector(".shot-label")?.remove();
    const box = element("div", "shot-label", ed.layer);
    box.style.left = clamp(point.x, 8, ed.layer.clientWidth - 316) + "px";
    box.style.top = clamp(point.y, 8, ed.layer.clientHeight - 145) + "px";
    const text = element("textarea", "", box);
    text.rows = 4;
    text.maxLength = 4000;
    text.value = mark?.label || "";
    text.setAttribute("aria-label", "标注文字");
    const confirm = element("button", "shot-btn secondary", box);
    confirm.textContent = "确认文字 (Ctrl+Enter)";
    confirm.onclick = () => {
      if (text.value.trim()) {
        const updated = mark ? { ...mark, label: text.value } : { ...ed.appearance, kind: "text", label: text.value, points: [point] };
        if (mark) editMark(ed, mark, updated);
        else changeMarks(ed, [...ed.marks, updated]);
      } else if (mark) editMark(ed, mark, mark.kind === "number" ? { ...mark, label: "" } : null);
      box.remove();
      ed.layer.focus();
      draw();
    };
    text.onkeydown = ev => {
      if (ev.ctrlKey && ev.key === "Enter") { ev.preventDefault(); confirm.click(); }
      if (ev.key === "Escape") { box.remove(); ed.layer.focus(); }
    };
    text.focus();
  }
  function pinImage(shot, opts = {}) {
    if (!shot) return announce("剪贴板中没有图片");
    const box = element("article", "shot-pin", desk());
    box.tabIndex = 0;
    box.setAttribute("aria-label", "桌面贴图");
    box.shot = shot;
    box.pinState = {
      opacity: opts.opacity ?? 1,
      locked: false,
      topmost: true,
      passthrough: false,
      thumb: false,
      thumbRect: null,
      zoom: 1,
    };
    pins.add(box);
    const cvs = element("canvas", "", box);
    const hud = element("span", "shot-pin-hud", box);
    hud.hidden = true;
    const tools = element("div", "shot-pin-actions", box);
    tools.innerHTML =
      button("edit-pin", "标注") +
      button("recognition-menu", "识别") +
      button("translate", "翻译") +
      button("pin-top", "置顶 (T)", "置顶") +
      button("pin-lock", "锁定 (L)", "锁定") +
      button("pin-pass", "鼠标穿透", "穿透") +
      button("pin-thumb", "缩略图 (R)", "缩略图") +
      button("save", "保存") +
      button("copy", "复制") +
      button("close", "关闭贴图", "×");
    let hudTimer = null;
    function flashHud(msg) {
      hud.textContent = msg;
      hud.hidden = false;
      clearTimeout(hudTimer);
      hudTimer = setTimeout(() => { hud.hidden = true; }, 1300);
    }
    box.flashHud = flashHud;
    function redrawPin() {
      const st = box.pinState;
      const src = box.shot.image;
      const vp = st.thumb && st.thumbRect ? st.thumbRect : { x: 0, y: 0, w: src.width, h: src.height };
      cvs.width = vp.w;
      cvs.height = vp.h;
      cvs.getContext("2d").drawImage(src, vp.x, vp.y, vp.w, vp.h, 0, 0, vp.w, vp.h);
      box.style.width = Math.round(vp.w * st.zoom) + "px";
      box.style.opacity = String(st.opacity);
      box.dataset.topmost = String(st.topmost);
      box.dataset.locked = String(st.locked);
      box.dataset.passthrough = String(st.passthrough);
      tools.querySelector('[data-shot="pin-top"]')?.setAttribute("aria-pressed", String(st.topmost));
      tools.querySelector('[data-shot="pin-lock"]')?.setAttribute("aria-pressed", String(st.locked));
      tools.querySelector('[data-shot="pin-pass"]')?.setAttribute("aria-pressed", String(st.passthrough));
      tools.querySelector('[data-shot="pin-thumb"]')?.setAttribute("aria-pressed", String(st.thumb));
    }
    box.redrawPin = redrawPin;
    box.style.left = opts.left ?? (8 + pins.size * 2 + "%");
    box.style.top = opts.top ?? (15 + pins.size * 3 + "%");
    redrawPin();
    cvs.onpointerdown = (ev) => {
      if (ev.button === 1) {
        ev.preventDefault();
        if (box.pinState.locked) return flashHud("已锁定，请先解锁");
        box.pinState.zoom = 1;
        redrawPin();
        return flashHud("100%");
      }
      if (ev.button !== 0) return;
      if (box.pinState.locked) return flashHud("已锁定，点击解锁后再移动");
      const st = box.pinState;
      if (st.thumb && ev.shiftKey && st.thumbRect) {
        const startX = ev.clientX, startY = ev.clientY, origin = { ...st.thumbRect };
        cvs.setPointerCapture(ev.pointerId);
        cvs.onpointermove = v => {
          const dx = Math.round((startX - v.clientX) / st.zoom);
          const dy = Math.round((startY - v.clientY) / st.zoom);
          st.thumbRect = {
            ...origin,
            x: clamp(origin.x + dx, 0, box.shot.image.width - origin.w),
            y: clamp(origin.y + dy, 0, box.shot.image.height - origin.h),
          };
          redrawPin();
        };
        cvs.onpointerup = () => { cvs.onpointermove = null; };
        return;
      }
      const p = { x: ev.clientX - box.offsetLeft, y: ev.clientY - box.offsetTop };
      cvs.setPointerCapture(ev.pointerId);
      box.focus();
      cvs.onpointermove = (v) => {
        box.style.left = v.clientX - p.x + "px";
        box.style.top = v.clientY - p.y + "px";
      };
      cvs.onpointerup = () => { cvs.onpointermove = null; };
    };
    cvs.ondblclick = ev => {
      if (box.pinState.locked) return flashHud("已锁定，请先解锁");
      if (ev.shiftKey) return togglePinThumb(box);
      execute("close", box);
    };
    box.onwheel = (ev) => {
      ev.preventDefault();
      const st = box.pinState;
      if (ev.ctrlKey) {
        st.opacity = clamp(Math.round((st.opacity + (ev.deltaY < 0 ? 0.05 : -0.05)) * 20) / 20, 0.1, 1);
        redrawPin();
        return flashHud(`透明度 ${Math.round(st.opacity * 100)}%`);
      }
      if (st.locked) return flashHud("已锁定，请先解锁");
      const step = ev.altKey ? 0.01 : 0.10;
      st.zoom = clamp(st.zoom + (ev.deltaY < 0 ? step : -step), 0.15, 4);
      redrawPin();
      flashHud(`${Math.round(st.zoom * 100)}%`);
    };
    box.focus();
  }
  function togglePinThumb(pin) {
    const st = pin.pinState, img = pin.shot.image;
    st.thumb = !st.thumb;
    if (st.thumb && !st.thumbRect)
      st.thumbRect = { x: Math.round(img.width * 0.15), y: Math.round(img.height * 0.15), w: Math.max(40, Math.round(img.width * 0.65)), h: Math.max(30, Math.round(img.height * 0.6)) };
    pin.redrawPin();
    pin.flashHud(st.thumb ? "缩略图模式 (Shift+拖动平移)" : "退出缩略图");
  }
  function textPanel(shot, translate) {
    panel?.remove();
    panel = element("section", "shot-panel", desk());
    panel.setAttribute("aria-label", "文字面板");
    const layoutBar = shot
      ? `<div class="shot-layout-bar"><div class="shot-layout-left"><span class="shot-bar-label">排版</span><select class="shot-para" aria-label="段落模式"><option value="smart">智能分段</option><option value="merge">合并换行</option><option value="keep">保留换行</option></select><select class="shot-punct" aria-label="标点转换"><option value="keep">原标点</option><option value="cjk">中文标点</option><option value="en">英文标点</option></select><label class="shot-inline-opt"><input type="checkbox" class="shot-nospace">去空格</label><span class="shot-sep" aria-hidden="true"></span><span class="shot-bar-label">字号</span><div class="segments shot-size-seg" role="group" aria-label="字号"><button type="button" data-font-size="small" aria-pressed="false">小</button><button type="button" data-font-size="medium" aria-pressed="true">中</button><button type="button" data-font-size="large" aria-pressed="false">大</button></div></div><div class="shot-layout-right"><label class="shot-pill-toggle"><input type="checkbox" class="shot-show-orig" checked><span>原图</span></label><label class="shot-pill-toggle"><input type="checkbox" class="shot-panel-pin"><span>置顶</span></label></div></div>`
      : `<div class="shot-layout-bar"><div class="shot-layout-left"><span class="shot-bar-label">粘贴或输入文本，使用已配置的模型接口进行翻译</span></div><div class="shot-layout-right"><label class="shot-pill-toggle"><input type="checkbox" class="shot-panel-pin"><span>置顶</span></label></div></div>`;
    panel.innerHTML = `<header><b>${shot ? "截图提取与翻译" : "粘贴文字翻译"}</b>${button("close-panel", "关闭文字面板", glyphs.close, "shot-btn shot-tool-icon")}</header>${layoutBar}<div class="shot-columns">${shot ? '<div class="shot-original"><div class="shot-col-head"><div class="shot-col-title"><h3>识别原图</h3><span class="shot-count-pill" id="shot-ocr-count"></span></div></div><div class="shot-original-stage"><img alt="识别原图"><canvas></canvas></div><small class="shot-pane-hint">点选或拖框文字块可同步右侧选区</small></div>' : ""}<div class="shot-fields"><div class="shot-col-head"><div class="shot-col-title"><label for="shot-source-area">${shot ? "识别与校对原文" : "原文"}</label><span class="shot-count-pill" id="shot-src-count"></span></div><div class="shot-col-actions">${button("copy-text", "复制原文", "复制原文", "shot-btn secondary")}</div></div><textarea id="shot-source-area" class="shot-source size-medium" rows="4" placeholder="在此校对或输入需要翻译的内容"></textarea><div class="shot-translate-bridge"><label class="shot-bridge-lang"><span>目标语言</span><input class="shot-language" list="shot-lang-presets" value="${e(language)}"></label><datalist id="shot-lang-presets">${languageOptions}</datalist>${button("send", "翻译", "翻译", "shot-btn primary")}</div><div class="shot-col-head"><div class="shot-col-title"><label for="shot-result-area">译文</label><span class="shot-count-pill" id="shot-dst-count"></span></div><div class="shot-col-actions">${button("copy-result", "复制译文", "复制译文", "shot-btn secondary")}</div></div><textarea id="shot-result-area" class="shot-result size-medium" rows="3" readonly placeholder="选择目标语言后点击「翻译」，优先翻译当前选中内容"></textarea></div></div><footer class="shot-panel-footer"><div class="shot-status-wrap"><i class="shot-status-dot" data-state="muted" aria-hidden="true"></i><small role="status" class="shot-status"></small></div><div class="shot-footer-right">${shot ? '<label class="shot-inline-opt"><input type="checkbox" class="shot-copy-close">复制全部后关闭</label>' : ""}<span class="shot-esc-hint">Esc 关闭</span></div></footer>`;

    const srcArea = panel.querySelector(".shot-source");
    const dstArea = panel.querySelector(".shot-result");
    const srcCount = panel.querySelector("#shot-src-count");
    const dstCount = panel.querySelector("#shot-dst-count");
    const ocrCount = panel.querySelector("#shot-ocr-count");
    const copySrcBtn = panel.querySelector('[data-shot="copy-text"]');
    const copyDstBtn = panel.querySelector('[data-shot="copy-result"]');
    const statusDot = panel.querySelector(".shot-status-dot");
    let spans = [], hi = new Set();

    function updateCounts() {
      const selLen = Math.max(0, srcArea.selectionEnd - srcArea.selectionStart);
      const srcLen = srcArea.value.length;
      if (srcCount) {
        srcCount.textContent = selLen > 0 ? `已选 ${selLen} 字` : `${srcLen} 字`;
        srcCount.hidden = srcLen === 0;
      }
      if (copySrcBtn) copySrcBtn.disabled = srcLen === 0;
      const dstLen = dstArea.value.length;
      if (dstCount) {
        dstCount.textContent = `${dstLen} 字`;
        dstCount.hidden = dstLen === 0;
      }
      if (copyDstBtn) copyDstBtn.disabled = dstLen === 0;
      if (ocrCount && shot) {
        ocrCount.textContent = hi.size > 0 ? `已选 ${hi.size} / ${shot.words.length}` : `${shot.words.length} 处文本`;
        ocrCount.hidden = !shot.words.length;
      }
    }
    panel.setStatus = (msg, tone = "muted") => {
      panel.querySelector(".shot-status").textContent = msg;
      if (statusDot) statusDot.dataset.state = tone;
    };

    function formatWords() {
      if (!shot) return;
      const para = panel.querySelector(".shot-para")?.value || "smart";
      const nospace = panel.querySelector(".shot-nospace")?.checked;
      const punct = panel.querySelector(".shot-punct")?.value || "keep";
      let out = "";
      spans = [];
      shot.words.forEach((w, i) => {
        let seg = w.s;
        if (punct === "cjk") seg = seg.replace(/,/g, "，").replace(/\./g, "。");
        else if (punct === "en") seg = seg.replace(/，/g, ",").replace(/。/g, ".");
        if (nospace) seg = seg.replace(/\s+/g, "");
        if (i > 0) out += para === "merge" ? (nospace ? "" : " ") : "\n";
        const start = out.length;
        out += seg;
        spans.push({ idx: i, start, len: seg.length });
      });
      srcArea.value = out;
      drawBoxes();
      updateCounts();
    }
    function drawBoxes() {
      if (!shot) return;
      const stage = panel.querySelector(".shot-original-stage");
      const cvs = stage?.querySelector("canvas");
      if (!cvs) return;
      cvs.width = shot.image.width;
      cvs.height = shot.image.height;
      const ctx = cvs.getContext("2d");
      ctx.clearRect(0, 0, cvs.width, cvs.height);
      shot.words.forEach((w, i) => {
        const active = hi.has(i);
        ctx.fillStyle = active ? "rgb(24 147 238 / 34%)" : "rgb(0 95 184 / 10%)";
        ctx.strokeStyle = active ? "#1893ee" : "rgb(0 95 184 / 45%)";
        ctx.lineWidth = active ? 2 : 1;
        ctx.beginPath();
        ctx.roundRect(w.x, w.y, w.w, w.h, 3);
        ctx.fill();
        ctx.stroke();
      });
    }
    panel.querySelectorAll("[data-font-size]").forEach(btn => {
      btn.onclick = () => {
        const sz = btn.dataset.fontSize;
        panel.querySelectorAll("[data-font-size]").forEach(b => b.setAttribute("aria-pressed", String(b === btn)));
        for (const area of [srcArea, dstArea]) {
          area.classList.remove("size-small", "size-medium", "size-large");
          area.classList.add("size-" + sz);
        }
      };
    });
    srcArea.oninput = () => {
      dstArea.value = "";
      updateCounts();
    };
    if (shot) {
      const imgEl = panel.querySelector("img");
      imgEl.src = shot.image.toDataURL();
      imgEl.onload = drawBoxes;
      formatWords();
      panel.querySelector(".shot-para").onchange = formatWords;
      panel.querySelector(".shot-punct").onchange = formatWords;
      panel.querySelector(".shot-nospace").onchange = formatWords;
      panel.querySelector(".shot-show-orig").onchange = ev => {
        panel.querySelector(".shot-original").hidden = !ev.target.checked;
      };
      srcArea.onselect = srcArea.onmouseup = srcArea.onkeyup = () => {
        const s = srcArea.selectionStart, end = srcArea.selectionEnd;
        hi = new Set(s === end ? [] : spans.filter(sp => sp.start < end && sp.start + sp.len > s).map(sp => sp.idx));
        drawBoxes();
        updateCounts();
      };
      const cvs = panel.querySelector(".shot-original-stage canvas");
      cvs.onclick = ev => {
        const rect = cvs.getBoundingClientRect();
        const px = (ev.clientX - rect.left) * cvs.width / rect.width;
        const py = (ev.clientY - rect.top) * cvs.height / rect.height;
        const idx = shot.words.findIndex(w => inside(w, { x: px, y: py }));
        if (idx >= 0 && spans[idx]) {
          hi = new Set([idx]);
          drawBoxes();
          srcArea.focus();
          srcArea.setSelectionRange(spans[idx].start, spans[idx].start + spans[idx].len);
          updateCounts();
        }
      };
    }
    panel.querySelector(".shot-panel-pin").onchange = ev => {
      panel.dataset.pinned = String(ev.target.checked);
    };
    updateCounts();
    panel.setStatus(
      shot && !shot.words.length ? "演示选区中没有文字" : shot ? `识别完成（${shot.words.length} 处）· 离线图文选区联动` : "请输入或粘贴需要翻译的文字",
      shot && !shot.words.length ? "muted" : "positive",
    );
    panel.querySelector(translate ? ".shot-language" : ".shot-source").focus();
  }
  function openRecognitionMenu(from) {
    menu?.remove();
    menu = element("div", "shot-menu", desk());
    const box = from.getBoundingClientRect(), desktop = desk().getBoundingClientRect();
    menu.style.zIndex = "60";
    menu.style.right = Math.max(4, desktop.right - box.right) + "px";
    menu.style.bottom = Math.max(4, desktop.bottom - box.top + 4) + "px";
    menu.innerHTML = recognitionKinds.map(kind => button(kind, labels[kind])).join("");
    menu.sourcePin = from.closest(".shot-pin");
    menu.querySelector("button").focus();
  }
  function recognitionSample(kind, rows) {
    const paper = canvas(406, kind === "table" ? 156 : 122);
    const context = paper.getContext("2d");
    context.fillStyle = "#fff";
    context.fillRect(0, 0, paper.width, paper.height);
    context.fillStyle = "#24313c";
    if (kind === "table") {
      context.font = "17px Segoe UI, Microsoft YaHei UI";
      context.strokeStyle = "#c0cfdb";
      rows.forEach((row, r) => row.forEach((value, c) => {
        const x = 15 + c * 188, y = 15 + r * 42;
        context.strokeRect(x, y, 188, 42);
        context.fillText(value, x + 12, y + 27);
      }));
    } else {
      context.font = "32px Cambria Math";
      context.fillText("a", 32, 65);
      context.fillText("= a", 72, 65);
      context.fillText("+ (n−1)d", 139, 65);
      context.font = "19px Cambria Math";
      context.fillText("n", 50, 74);
      context.fillText("1", 122, 74);
    }
    return paper;
  }
  function recognitionPanel(shot, kind) {
    panel?.remove();
    panel = element("section", "shot-panel shot-recognition-panel", desk());
    panel.setAttribute("aria-label", labels[kind]);
    const view = panel;
    const samplesByKind = {
      document: "项目周会\n\n周五 14:30\nB 会议室 · 设计评审",
      formula: "a_{n} = a_{1} + (n-1)d",
      table: [["编号", "项目"], ["0012", "会议预约"], ["0013", ""]],
    };
    const canExport = kind !== "formula";
    view.innerHTML = `<header><b>${labels[kind]}</b>${button("close-panel", "关闭", glyphs.close)}</header>
      <div class="shot-columns"><section class="shot-original"><h3>${kind === "document" ? "原图" : "示例原图"}</h3><img alt="识别原图"></section>
      <section class="shot-fields"><h3>识别结果</h3><div class="shot-recognition-editor"></div></section></div>
      <footer><span role="status" class="shot-status">离线原型</span><div class="shot-recognition-actions"></div></footer>`;
    const original = kind === "document" ? shot.image : recognitionSample(kind, samplesByKind.table);
    view.querySelector("img").src = original.toDataURL();
    const status = view.querySelector("[role=status]");
    const editorSlot = view.querySelector(".shot-recognition-editor");
    const actionSlot = view.querySelector(".shot-recognition-actions");
    const size = { document: "约 7 MB", table: "约 8 MB", formula: "约 120 MB" }[kind];
    function showResult() {
      installed.add(kind);
      editorSlot.replaceChildren();
      actionSlot.replaceChildren();
      let value;
      if (kind === "table") {
        const table = element("table", "shot-recognition-grid", editorSlot);
        samplesByKind.table.forEach((row, r) => {
          const tr = table.insertRow();
          row.forEach((text, c) => {
            const input = element("input", "", tr.insertCell());
            input.value = text;
            input.setAttribute("aria-label", `第 ${r + 1} 行第 ${c + 1} 列`);
          });
        });
        value = () => [...table.rows].map(row => [...row.cells].map(cell => cell.firstChild.value).join("\t")).join("\n");
      } else {
        const field = element("textarea", "shot-source", editorSlot);
        field.setAttribute("aria-label", kind === "formula" ? "LaTeX 源码" : "文档内容");
        field.value = samplesByKind[kind];
        value = () => field.value;
      }
      status.textContent = "演示结果，可对照原图校对";
      const copy = element("button", "shot-btn", actionSlot);
      copy.textContent = kind === "formula" ? "复制 LaTeX" : "复制";
      copy.onclick = async () => {
        try {
          await navigator.clipboard.writeText(value());
          status.textContent = "已复制";
        } catch {
          status.textContent = "剪贴板不可用，请选中结果后复制";
        }
      };
      if (canExport) {
        const save = element("button", "shot-btn primary", actionSlot);
        save.textContent = kind === "table" ? "导出 Excel" : "导出 Word";
        save.onclick = () => { status.textContent = "桌面版会导出校对后的文件"; };
      }
    }
    if (installed.has(kind)) return showResult();
    const prompt = element("p", "shot-status", editorSlot);
    prompt.textContent = `首次使用需下载本机模型（${size}），安装后可断网识别。`;
    const install = element("button", "shot-btn primary", actionSlot);
    install.textContent = "下载模型";
    let pending;
    install.onclick = () => {
      if (pending) {
        clearTimeout(pending);
        pending = null;
        status.textContent = "下载已取消";
        install.textContent = "重试";
        return;
      }
      status.textContent = "模拟下载中…";
      install.textContent = "取消下载";
      pending = setTimeout(() => { if (view.isConnected) showResult(); }, 850);
    };
  }
  function pinClipboardValue(value, previous = {}) {
    const color = /^#[0-9a-f]{6}$/i.test(value);
    const card = element("article", "shot-pin shot-value-pin", desk());
    card.tabIndex = 0;
    card.setAttribute("aria-label", color ? "颜色贴图" : "文字贴图");
    card.style.left = previous.left || "24px";
    card.style.top = previous.top || "54px";
    const header = element("header", "", card);
    header.textContent = color ? "颜色贴图" : "文字贴图";
    const close = element("button", "shot-btn", header);
    close.textContent = "关闭";
    function dismiss() {
      closedPins.push({ value, left: card.style.left, top: card.style.top });
      if (closedPins.length > 10) closedPins.shift();
      pins.delete(card);
      card.remove();
    }
    close.onclick = dismiss;
    card.onkeydown = ev => {
      if (ev.key !== "Escape") return;
      ev.stopPropagation();
      dismiss();
    };
    header.onpointerdown = ev => {
      if (ev.target === close) return;
      const origin = { x: ev.clientX - card.offsetLeft, y: ev.clientY - card.offsetTop };
      header.setPointerCapture(ev.pointerId);
      header.onpointermove = move => {
        card.style.left = clamp(move.clientX - origin.x, 0, Math.max(0, desk().clientWidth - card.offsetWidth)) + "px";
        card.style.top = clamp(move.clientY - origin.y, 0, Math.max(0, desk().clientHeight - card.offsetHeight)) + "px";
      };
      header.onlostpointercapture = () => { header.onpointermove = null; };
    };
    if (color) {
      const swatch = element("div", "shot-value-swatch", card);
      swatch.style.background = value;
      const channels = [1, 3, 5].map(start => Number.parseInt(value.slice(start, start + 2), 16));
      for (const code of colorValues(channels)) {
        const copy = element("button", "shot-btn", card);
        copy.textContent = code;
        copy.onclick = async () => {
          try { await navigator.clipboard.writeText(code); announce("已复制 " + code); }
          catch { announce("剪贴板不可用"); }
        };
      }
    } else {
      const body = element("div", "shot-value-text", card);
      body.textContent = value;
    }
    pins.add(card);
    card.focus();
  }
  function closeRecording() {
    if (!recordingClip) return;
    clearInterval(recordingClip.timer);
    recordingClip.frame?.remove();
    recordingClip.view.remove();
    recordingClip = null;
  }
  function createRecordFrame(rect) {
    if (!rect) return null;
    const host = desk();
    if (!host) return null;
    const hw = host.clientWidth || 926, hh = host.clientHeight || 780;
    const outset = 3;
    const left = Math.max(0, Math.round(rect.x - outset));
    const top = Math.max(0, Math.round(rect.y - outset));
    const right = Math.min(hw, Math.round(rect.x + rect.w + outset));
    const bottom = Math.min(hh, Math.round(rect.y + rect.h + outset));
    const frame = element("div", "shot-record-frame", host);
    frame.setAttribute("aria-hidden", "true");
    frame.dataset.stage = "ready";
    frame.style.left = left + "px";
    frame.style.top = top + "px";
    frame.style.width = Math.max(12, right - left) + "px";
    frame.style.height = Math.max(12, bottom - top) + "px";
    frame.innerHTML = `<i class="c tl"></i><i class="c tr"></i><i class="c bl"></i><i class="c br"></i><span class="shot-record-badge">录制范围 · ${Math.round(rect.w)} × ${Math.round(rect.h)}</span><span class="shot-record-countdown" hidden></span>`;
    if (top < 28) frame.querySelector(".shot-record-badge").dataset.inside = "true";
    return frame;
  }
  function positionRecorder(view, rect, stage) {
    if (!rect || stage === "review") {
      view.style.top = "";
      view.style.left = "";
      view.style.transform = "";
      return;
    }
    const host = desk();
    if (!host) return;
    const hw = host.clientWidth || 926, hh = host.clientHeight || 780;
    const vw = view.offsetWidth || 568, vh = view.offsetHeight || 360;
    const candidates = [
      { left: clamp(rect.x, 8, Math.max(8, hw - vw - 8)), top: rect.y + rect.h + 12 },
      { left: clamp(rect.x, 8, Math.max(8, hw - vw - 8)), top: rect.y - vh - 12 },
      { left: rect.x + rect.w + 12, top: clamp(rect.y, 8, Math.max(8, hh - vh - 8)) },
      { left: rect.x - vw - 12, top: clamp(rect.y, 8, Math.max(8, hh - vh - 8)) },
    ];
    for (const pos of candidates) {
      const fits = pos.left >= 4 && pos.top >= 4 && pos.left + vw <= hw - 4 && pos.top + vh <= hh - 4;
      const overlap = !(pos.left + vw <= rect.x - 4 || pos.left >= rect.x + rect.w + 4 || pos.top + vh <= rect.y - 4 || pos.top >= rect.y + rect.h + 4);
      if (fits && !overlap) {
        view.style.left = Math.round(pos.left) + "px";
        view.style.top = Math.round(pos.top) + "px";
        view.style.transform = "none";
        return;
      }
    }
    view.style.top = "";
    view.style.left = "";
    view.style.transform = "";
  }
  function recordingPanel(shot, recovered = null, rect = null) {
    closeRecording();
    const areaRect = rect || shot?.rect || null;
    if (shot && areaRect) shot.rect = areaRect;
    const frame = createRecordFrame(areaRect);
    const view = element("section", "shot-recorder", desk());
    view.setAttribute("aria-label", "屏幕录制演示");
    const sizePill = areaRect ? `<span class="shot-count-pill">${Math.round(areaRect.w)} × ${Math.round(areaRect.h)} px</span>` : "";
    view.innerHTML = `<header><div class="shot-col-title"><b>屏幕录制</b>${sizePill}</div>${button("video-close", "关闭录屏", glyphs.close, "shot-btn shot-tool-icon")}</header>
      <div class="shot-video-image"><img alt="录制区域"><svg class="shot-video-annotations" viewBox="0 0 100 100" preserveAspectRatio="none" aria-hidden="true"></svg><div class="shot-crop-box" hidden><i></i><i></i><i></i><i></i></div><span class="shot-video-position" hidden></span><kbd class="shot-video-key" hidden>Ctrl+T</kbd><span class="shot-video-camera" hidden>摄像头示例</span><span class="shot-progress-pill" hidden>00:00</span><div class="shot-progress-bar" hidden><i></i></div></div>
      <div class="shot-rec-card shot-video-options">
        <div class="shot-rec-params">
          <label class="field">帧率<select id="shot-fps">${[5, 16, 24, 30, 60].map(value => `<option ${value === 24 ? "selected" : ""}>${value}</option>`).join("")}</select></label>
          <label class="field">延时（秒）<select id="shot-delay">${[0, 1, 2, 3, 5, 10].map(value => `<option>${value}</option>`).join("")}</select></label>
          <label class="field">模式<select id="shot-mode"><option>普通录制</option><option>快速保存 MP4</option></select></label>
        </div>
        <div class="shot-rec-toggles">
          <label class="shot-inline-opt"><input type="checkbox" checked>显示光标</label>
          <label class="shot-inline-opt"><input type="checkbox" id="shot-video-system">系统声音</label>
          <label class="shot-inline-opt"><input type="checkbox" id="shot-video-mic">麦克风</label>
          <label class="shot-inline-opt"><input type="checkbox" id="shot-video-mouse">鼠标点击和滚轮</label>
          <label class="shot-inline-opt"><input type="checkbox" id="shot-video-keys">显示快捷键</label>
          <label class="shot-inline-opt"><input type="checkbox" id="shot-video-camera">摄像头</label>
          <label class="shot-inline-opt"><input type="checkbox" id="shot-video-hw">硬件加速</label>
        </div>
        <label class="shot-rec-sub shot-microphone-field" hidden>麦克风设备 <select aria-label="麦克风设备"><option>Windows 默认麦克风</option><option>内置麦克风阵列（示例）</option></select></label>
        <div class="shot-rec-sub shot-camera-fields" hidden>
          <label>摄像头设备 <select><option>本机摄像头（示例）</option></select></label>
          <label>位置 <select id="shot-camera-corner" aria-label="摄像头位置"><option>右下</option><option>左下</option><option>右上</option><option>左上</option></select></label>
          <label>大小 <select id="shot-camera-size" aria-label="摄像头大小"><option>小</option><option selected>中</option><option>大</option></select></label>
          <label class="shot-inline-opt"><input type="checkbox" id="shot-camera-circle">圆形</label>
          <label class="shot-inline-opt"><input type="checkbox" id="shot-camera-mirror" checked>镜像</label>
        </div>
        <div class="shot-rec-sub shot-quick-fields" hidden>
          <label>导出画质 <select id="shot-quick-quality" aria-label="快速录制画质"><option>轻量</option><option selected>标准</option><option>高清</option></select></label>
          <label class="shot-inline-opt"><input type="checkbox" id="shot-quick-autosave">直接存至视频目录</label>
        </div>
      </div>
      <div class="shot-rec-sub shot-live-annotate-bar" hidden>
        ${[["pen", "画笔"], ["arrow", "箭头"], ["rect", "矩形"], ["number", "序号"]].map(([t, lbl], i) => `<button type="button" class="shot-btn secondary${i === 0 ? " is-active" : ""}" data-live-tool="${t}">${lbl}</button>`).join("")}
        ${["#e03131", "#0b75d1", "#2b8a3e", "#f08c00"].map((c, i) => `<button type="button" class="shot-btn secondary shot-live-swatch${i === 0 ? " is-active" : ""}" data-live-color="${c}" aria-label="标注颜色"><i style="background:${c}"></i></button>`).join("")}
        ${button("video-ann-undo", "撤销标注", "撤销", "shot-btn secondary")}${button("video-ann-clear", "清空标注", "清空", "shot-btn secondary")}${button("video-ann-done", "切回穿透", "穿透", "shot-btn secondary")}
      </div>
      <div class="shot-rec-card shot-video-trim" hidden>
        <label class="shot-trim-seek"><span>回放进度</span><input id="shot-playhead" class="shot-slider" type="range" min="0" step="0.1"></label>
        <div class="shot-trim-visual"><span class="shot-trim-span"></span><input id="shot-trim-start" class="shot-slider" type="range" min="0" value="0" step="0.1" aria-label="剪辑开始滑块"><input id="shot-trim-end" class="shot-slider" type="range" min="0" value="1" step="0.1" aria-label="剪辑结束滑块"></div>
        <div class="shot-trim-row">
          <label>开始（秒）<input id="shot-video-start-sec" type="number" min="0" value="0" step=".1"></label>
          <label>结束（秒）<input id="shot-video-end" type="number" min="0" step=".1"></label>
          <label>倍速<select id="shot-speed">${[.2, .25, .5, .75, 1, 1.25, 1.5, 2].map(speed => `<option ${speed === 1 ? "selected" : ""}>${speed}×</option>`).join("")}</select></label>
          <label>导出格式<select id="shot-export-format"><option>MP4</option><option>GIF</option><option>WebP</option></select></label>
        </div>
        <div class="shot-trim-row">
          ${["左", "上", "右", "下"].map(side => `<label>裁${side}<input type="number" min="0" value="0" data-crop="${side}"></label>`).join("")}
          ${button("video-reset-crop", "重置裁剪", "重置", "shot-btn secondary")}
        </div>
      </div>
      <div class="shot-rec-card shot-video-mixer" hidden>
        <span class="shot-mixer-title">音轨、动效与导出选项</span>
        ${[["system","系统声音"],["mic","麦克风"]].map(([id,label]) => `<div data-track="${id}"><label>${label}<input class="shot-slider" type="range" min="0" max="100" value="100" aria-label="${label}音量"></label><output>100%</output><label class="shot-inline-opt"><input type="checkbox" aria-label="${label}静音">静音</label></div>`).join("")}
        <label class="shot-inline-opt" data-effect="mouse"><input type="checkbox" checked>鼠标动画</label>
        <label class="shot-inline-opt" data-effect="keys"><input type="checkbox" checked>快捷键动画</label>
        <label class="shot-inline-opt">显示进度<select id="shot-progress-style" aria-label="显示进度"><option>无进度</option><option>底部进度条</option><option>计时文字</option><option>倒计时</option></select></label>
        <label class="shot-inline-opt">画质<select id="shot-export-quality" aria-label="导出画质"><option>轻量</option><option selected>标准</option><option>高清</option></select></label>
        <label class="shot-inline-opt"><input type="checkbox" id="shot-export-hw">硬件加速</label>
        <label class="shot-inline-opt"><input type="checkbox" id="shot-close-after">导出后关闭</label>
      </div>
      <div class="shot-panel-footer shot-rec-footer">
        <div class="shot-status-wrap"><i class="shot-status-dot" data-state="accent" aria-hidden="true"></i><p class="shot-video-status" role="status"></p></div>
        <div class="shot-row">${button("video-start", "开始录制", "开始录制", "shot-btn primary")}${button("video-pause", "暂停", "暂停", "shot-btn secondary")}${button("video-annotate", "边录边标", "标注", "shot-btn secondary")}${button("video-redo", "重新录制", "重录", "shot-btn secondary")}${button("video-save", "另存为", "另存为", "shot-btn primary")}${button("video-quicksave", "快速保存", "快存", "shot-btn secondary")}${button("video-copyfile", "复制文件", "复制文件", "shot-btn secondary")}${button("video-openfolder", "打开位置", "打开位置", "shot-btn secondary")}${button("video-repeat", "重新录制", "重新录制", "shot-btn secondary")}${button("video-interrupt", "模拟中断", "模拟中断", "shot-btn secondary")}</div>
      </div>`;
    view.querySelector("img").src = shot.image.toDataURL();
    recordingClip = { view, frame, rect: areaRect, shot, stage: recovered ? "review" : "ready", elapsed: recovered?.elapsed || 0, left: 0, position: 0,
      trimStart: 0, trimEnd: recovered?.elapsed || 0, options: recovered?.options || { cameraSize: 1, mirror: true }, annotating: false, liveMarks: [], savedOnce: false, timer: null };
    if (recovered) {
      view.querySelector("#shot-video-end").value = recovered.elapsed.toFixed(1);
      view.querySelector("#shot-playhead").max = recovered.elapsed.toFixed(1);
      view.querySelector("#shot-trim-start").max = recovered.elapsed.toFixed(1);
      view.querySelector("#shot-trim-end").max = recovered.elapsed.toFixed(1);
      view.querySelector("#shot-trim-end").value = recovered.elapsed.toFixed(1);
      recordingClock(recordingClip);
    }
    view.querySelector(".shot-video-options").onchange = () => {
      recordingClip.options = Object.fromEntries(["system", "mic", "mouse", "keys", "camera"].map(
        value => [value, view.querySelector("#shot-video-" + value).checked]));
      recordingClip.options.corner = view.querySelector("#shot-camera-corner").selectedIndex;
      recordingClip.options.cameraSize = view.querySelector("#shot-camera-size").selectedIndex;
      recordingClip.options.circle = view.querySelector("#shot-camera-circle").checked;
      recordingClip.options.mirror = view.querySelector("#shot-camera-mirror").checked;
      recordingClip.options.quickMode = view.querySelector("#shot-mode").selectedIndex === 1;
      updateRecording();
    };
    view.querySelector(".shot-video-mixer").onchange = updateRecording;
    view.querySelectorAll("[data-track] input[type=range]").forEach(range => range.oninput = () =>
      range.closest("[data-track]").querySelector("output").value = range.value + "%");
    view.querySelectorAll("[data-live-tool]").forEach(btn => btn.onclick = () => {
      view.querySelectorAll("[data-live-tool]").forEach(el => el.classList.toggle("is-active", el === btn));
    });
    view.querySelectorAll("[data-live-color]").forEach(btn => btn.onclick = () => {
      view.querySelectorAll("[data-live-color]").forEach(el => el.classList.toggle("is-active", el === btn));
    });
    view.querySelector("#shot-playhead").oninput = ev => {
      recordingClip.position = Number(ev.target.value);
      updateRecording();
    };
    const startRange = view.querySelector("#shot-trim-start"), endRange = view.querySelector("#shot-trim-end");
    const startNum = view.querySelector("#shot-video-start-sec"), endNum = view.querySelector("#shot-video-end");
    const syncTrimInputs = (source) => {
      let s = Math.max(0, Number(source === "range" ? startRange.value : startNum.value));
      let e = Math.max(s + 0.1, Number(source === "range" ? endRange.value : endNum.value));
      s = Math.min(s, Math.max(0, e - 0.1));
      e = Math.min(recordingClip.elapsed, Math.max(s + 0.1, e));
      recordingClip.trimStart = s;
      recordingClip.trimEnd = e;
      startNum.value = startRange.value = s.toFixed(1);
      endNum.value = endRange.value = e.toFixed(1);
      updateRecording();
    };
    startRange.oninput = endRange.oninput = () => syncTrimInputs("range");
    startNum.onchange = endNum.onchange = () => syncTrimInputs("num");
    view.querySelector(".shot-video-trim").onchange = (ev) => {
      if (ev.target.dataset?.crop) {
        const inset = ["上", "右", "下", "左"].map(side => Math.max(0, Number(view.querySelector(`[data-crop="${side}"]`).value)));
        view.querySelector("img").style.clipPath = `inset(${inset.map(n => n + "px").join(" ")})`;
        updateRecording();
      }
    };
    updateRecording();
    view.querySelector('[data-shot="video-start"]').focus();
  }
  function formatClock(sec) {
    const s = Math.max(0, Math.floor(sec));
    const m = Math.floor(s / 60), r = s % 60;
    return String(m).padStart(2, "0") + ":" + String(r).padStart(2, "0");
  }
  function renderLiveAnnotations(clip) {
    const svg = clip.view.querySelector(".shot-video-annotations");
    if (!svg) return;
    if (!clip.liveMarks?.length) { svg.innerHTML = ""; return; }
    svg.innerHTML = clip.liveMarks.map(m => {
      if (m.tool === "arrow")
        return `<line x1="22" y1="30" x2="54" y2="48" stroke="${m.color}" stroke-width="2.2" stroke-linecap="round"/><polygon points="55,48.5 47,49 51,42" fill="${m.color}"/>`;
      if (m.tool === "rect")
        return `<rect x="52" y="20" width="24" height="20" rx="2" fill="none" stroke="${m.color}" stroke-width="2"/>`;
      if (m.tool === "number")
        return `<circle cx="34" cy="64" r="5" fill="${m.color}" stroke="#fff" stroke-width="1"/><text x="34" y="66" text-anchor="middle" font-size="6" font-weight="700" fill="#fff">1</text>`;
      return `<path d="M16,58 Q28,42 40,56 T62,48" fill="none" stroke="${m.color}" stroke-width="2.2" stroke-linecap="round"/>`;
    }).join("");
  }
  function updateRecording() {
    const clip = recordingClip, { view, stage } = clip;
    const paused = stage === "paused", review = stage === "review", live = stage === "live";
    const playhead = view.querySelector("#shot-playhead");
    playhead.value = clip.position.toFixed(1);
    view.querySelector(".shot-video-options").hidden = stage !== "ready";
    view.querySelector(".shot-camera-fields").hidden = !clip.options.camera;
    view.querySelector(".shot-microphone-field").hidden = !clip.options.mic;
    const quickFields = view.querySelector(".shot-quick-fields");
    if (quickFields) quickFields.hidden = !clip.options.quickMode;
    const inset = view.querySelector(".shot-video-camera");
    inset.hidden = !clip.options.camera;
    inset.dataset.corner = clip.options.corner || 0;
    inset.dataset.size = clip.options.cameraSize ?? 1;
    inset.classList.toggle("round", !!clip.options.circle);
    inset.classList.toggle("mirrored", clip.options.mirror !== false);
    const annBar = view.querySelector(".shot-live-annotate-bar");
    if (annBar) annBar.hidden = !(clip.annotating && (live || paused));
    view.querySelector(".shot-video-trim").hidden = !review;
    const mixer = view.querySelector(".shot-video-mixer");
    mixer.hidden = !review;
    mixer.querySelectorAll("[data-track],[data-effect]").forEach(row => row.hidden = !clip.options[row.dataset.track || row.dataset.effect]);
    const cropBox = view.querySelector(".shot-crop-box");
    const crops = ["左", "上", "右", "下"].map(side => Math.max(0, Number(view.querySelector(`[data-crop="${side}"]`)?.value || 0)));
    const hasCrop = crops.some(v => v > 0);
    if (cropBox) {
      cropBox.hidden = !review;
      cropBox.dataset.cropped = String(hasCrop);
      cropBox.style.inset = review ? `${Math.min(40, crops[1])}px ${Math.min(40, crops[2])}px ${Math.min(40, crops[3])}px ${Math.min(40, crops[0])}px` : "0";
    }
    const trimSpan = view.querySelector(".shot-trim-span");
    if (trimSpan && clip.elapsed > 0) {
      const sPct = clamp(clip.trimStart / clip.elapsed, 0, 1) * 100;
      const ePct = clamp((clip.trimEnd || clip.elapsed) / clip.elapsed, 0, 1) * 100;
      trimSpan.style.left = sPct + "%";
      trimSpan.style.width = Math.max(2, ePct - sPct) + "%";
    }
    const pStyle = view.querySelector("#shot-progress-style")?.selectedIndex || 0;
    const pBar = view.querySelector(".shot-progress-bar"), pPill = view.querySelector(".shot-progress-pill");
    const frac = clip.elapsed > 0 ? clamp(clip.position / clip.elapsed, 0, 1) : 0;
    if (pBar) {
      pBar.hidden = !(review && pStyle === 1);
      pBar.firstElementChild.style.width = (frac * 100).toFixed(1) + "%";
    }
    if (pPill) {
      pPill.hidden = !(review && (pStyle === 2 || pStyle === 3));
      pPill.textContent = formatClock(pStyle === 2 ? clip.position : Math.max(0, clip.elapsed - clip.position));
    }
    const primary = view.querySelector('[data-shot="video-start"]');
    primary.hidden = stage === "delay";
    const title = review ? (clip.playing ? "暂停播放" : "播放") : live || paused ? "停止录制" : "开始录制";
    primary.textContent = title;
    primary.setAttribute("aria-label", title);
    primary.className = live || paused ? "shot-btn danger-fill" : review ? "shot-btn secondary" : "shot-btn primary";
    for (const [id, visible] of [
      ["pause", live || paused],
      ["annotate", live || paused],
      ["redo", live || paused],
      ["save", review],
      ["quicksave", review],
      ["copyfile", review],
      ["openfolder", review && clip.savedOnce],
      ["repeat", review],
      ["interrupt", live || paused]
    ]) {
      const control = view.querySelector(`[data-shot="video-${id}"]`);
      if (!control) continue;
      control.hidden = !visible;
      if (id === "pause") {
        control.textContent = paused ? "继续" : "暂停";
        control.setAttribute("aria-label", control.textContent);
      }
      if (id === "annotate") control.classList.toggle("is-active", !!clip.annotating);
    }
    const label = stage === "delay" ? `${Math.max(1, Math.ceil(clip.left))} 秒后开始录制` :
      clip.annotating && (live || paused) ? "标注绘制中" :
      live ? "正在录制" : paused ? "已暂停" : review ? "回放" : "就绪";
    view.querySelector("header b").textContent = review ? "录屏回放" : "屏幕录制";
    const status = view.querySelector(".shot-video-status");
    status.textContent = `${label} · ${(review ? clip.position : clip.elapsed).toFixed(1)} 秒 · 离线演示`;
    status.dataset.live = String(live);
    const dot = view.querySelector(".shot-rec-footer .shot-status-dot");
    if (dot) dot.dataset.state = clip.annotating && (live || paused) ? "accent" : live ? "danger" : paused || stage === "delay" ? "warning" : review ? "positive" : "accent";
    view.querySelector(".shot-video-image").dataset.stage = stage;
    if (clip.frame) {
      clip.frame.hidden = review;
      clip.frame.dataset.stage = stage;
      const badge = clip.frame.querySelector(".shot-record-badge");
      if (badge && clip.rect) {
        const prefix = live ? "● 录制中" : paused ? "已暂停" : stage === "delay" ? "准备录制" : "录制范围";
        badge.textContent = `${prefix} · ${Math.round(clip.rect.w)} × ${Math.round(clip.rect.h)}`;
      }
      const cd = clip.frame.querySelector(".shot-record-countdown");
      if (cd) {
        cd.hidden = stage !== "delay";
        if (stage === "delay") cd.textContent = String(Math.max(1, Math.ceil(clip.left)));
      }
    }
    positionRecorder(view, clip.rect, stage);
    const marker = view.querySelector(".shot-video-position");
    marker.hidden = !(clip.options.mouse && (review || live || paused)) ||
      (review && !mixer.querySelector('[data-effect="mouse"] input').checked);
    const key = view.querySelector(".shot-video-key");
    key.hidden = !(clip.options.keys && (review || live || paused)) ||
      (review && !mixer.querySelector('[data-effect="keys"] input').checked);
    marker.style.left = 21 + ((review ? clip.position : clip.elapsed) * 12) % 58 + "%";
    marker.style.top = 48 + Math.sin((review ? clip.position : clip.elapsed) * 1.8) * 12 + "%";
  }
  function videoAction(id) {
    const clip = recordingClip;
    if (id === "video-recover") {
      if (!recoverySample) return announce("没有可恢复的录屏");
      const restored = recoverySample;
      recoverySample = null;
      return recordingPanel(restored.shot, restored);
    }
    if (!clip) return;
    if (id === "video-close") return closeRecording();
    if (id === "video-repeat") return recordingPanel(clip.shot);
    if (id === "video-redo") {
      clearInterval(clip.timer);
      clip.elapsed = 0;
      clip.position = 0;
      clip.liveMarks = [];
      clip.annotating = false;
      clip.stage = Number(clip.view.querySelector("#shot-delay").value) ? "delay" : "live";
      clip.left = Number(clip.view.querySelector("#shot-delay").value);
      renderLiveAnnotations(clip);
      recordingClock(clip);
      updateRecording();
      return;
    }
    if (id === "video-annotate" || id === "video-ann-done") {
      clip.annotating = !clip.annotating;
      if (clip.annotating && !clip.liveMarks.length) {
        const tool = clip.view.querySelector("[data-live-tool].is-active")?.dataset.liveTool || "pen";
        const color = clip.view.querySelector("[data-live-color].is-active")?.dataset.liveColor || "#e03131";
        clip.liveMarks.push({ tool, color });
        renderLiveAnnotations(clip);
      }
      updateRecording();
      return;
    }
    if (id === "video-ann-undo") {
      clip.liveMarks.pop();
      renderLiveAnnotations(clip);
      return;
    }
    if (id === "video-ann-clear") {
      clip.liveMarks = [];
      renderLiveAnnotations(clip);
      return;
    }
    if (id === "video-reset-crop") {
      clip.view.querySelectorAll("[data-crop]").forEach(el => { el.value = "0"; });
      clip.view.querySelector("img").style.clipPath = "";
      updateRecording();
      return;
    }
    if (id === "video-interrupt") {
      recoverySample = { shot: clip.shot, elapsed: clip.elapsed, options: { ...clip.options } };
      closeRecording();
      return announce("已留下可恢复的录屏片段");
    }
    if (id === "video-save" || id === "video-quicksave") {
      clip.savedOnce = true;
      updateRecording();
      announce(id === "video-quicksave" ? "离线演示：已快速保存至 视频\\YXRL" : "离线原型已完成导出流程，桌面版生成实际文件");
      if (clip.view.querySelector("#shot-close-after")?.checked) closeRecording();
      return;
    }
    if (id === "video-copyfile") {
      return announce("离线演示：已将导出文件复制到剪贴板");
    }
    if (id === "video-openfolder") {
      return announce("离线演示：将在资源管理器定位输出文件");
    }
    if (id === "video-pause") clip.stage = clip.stage === "paused" ? "live" : "paused";
    if (id === "video-start") {
      if (clip.stage === "ready") {
        clip.left = Number(clip.view.querySelector("#shot-delay").value);
        clip.stage = clip.left ? "delay" : "live";
        recordingClock(clip);
      } else if (clip.stage === "review") {
        if (clip.position >= clip.elapsed) clip.position = 0;
        clip.playing = !clip.playing;
      } else if (clip.stage === "live" || clip.stage === "paused") {
        if (clip.view.querySelector("#shot-mode").selectedIndex === 1) {
          closeRecording();
          return announce("离线演示：快速录制已完成");
        }
        clip.annotating = false;
        clip.stage = "review";
        clip.trimStart = 0;
        clip.trimEnd = clip.elapsed;
        clip.view.querySelector("#shot-video-start-sec").value = "0";
        clip.view.querySelector("#shot-video-end").value = clip.elapsed.toFixed(1);
        clip.view.querySelector("#shot-playhead").max = clip.elapsed.toFixed(1);
        clip.view.querySelector("#shot-trim-start").max = clip.elapsed.toFixed(1);
        clip.view.querySelector("#shot-trim-end").max = clip.elapsed.toFixed(1);
        clip.view.querySelector("#shot-trim-end").value = clip.elapsed.toFixed(1);
        const save = clip.view.querySelector('[data-shot="video-save"]');
        save.hidden = false;
        save.focus();
      }
    }
    updateRecording();
  }
  function recordingClock(clip) {
    let previous = performance.now();
    clip.timer = setInterval(() => {
      const now = performance.now(), delta = (now - previous) / 1000;
      previous = now;
      if (clip.stage === "delay" && (clip.left -= delta) <= 0) clip.stage = "live";
      else if (clip.stage === "live") clip.elapsed += delta;
      else if (clip.stage === "review" && clip.playing) {
        clip.position = Math.min(clip.trimEnd || clip.elapsed, clip.position + delta * parseFloat(clip.view.querySelector("#shot-speed").value));
        if (clip.position >= (clip.trimEnd || clip.elapsed)) clip.playing = false;
      } else if (clip.stage === "paused" || clip.stage === "review") return;
      updateRecording();
    }, 100);
  }
  function closeScroll() {
    if (!scrollClip) return;
    clearInterval(scrollClip.autoTimer);
    scrollClip.frame?.remove();
    scrollClip.view.remove();
    scrollClip = null;
  }
  function scrollingPanel(shot) {
    closeScroll();
    const frame = shot?.rect ? createRecordFrame(shot.rect) : null;
    if (frame) {
      frame.dataset.stage = "scrolling";
      const badge = frame.querySelector(".shot-record-badge");
      if (badge) badge.textContent = "长截图区域";
    }
    const view = element("section", "shot-recorder", desk());
    view.setAttribute("aria-label", "长截图演示");
    view.innerHTML = `<header><div class="shot-col-title"><b>长截图</b><span class="shot-count-pill" id="shot-scroll-count">1 屏</span></div>${button("scroll-close", "关闭长截图", glyphs.close, "shot-btn shot-tool-icon")}</header>
      <div class="shot-rec-card shot-scroll-bar">
        <div class="shot-video-options">
          <label>滚动方向 <select id="shot-scroll-axis"><option>纵向向下</option><option>横向向右</option></select></label>
          <label class="shot-inline-opt"><input id="shot-scroll-auto" type="checkbox" checked>滚动后自动拼接</label>
        </div>
      </div>
      <div class="shot-scroll-stage">
        <div class="shot-scroll-source" tabindex="0" aria-label="可滚动示例区域"></div>
      </div>
      <div class="shot-panel-footer shot-rec-footer">
        <div class="shot-status-wrap"><i class="shot-status-dot" data-state="positive" aria-hidden="true"></i><p class="shot-video-status" role="status"></p></div>
        <div class="shot-row">${button("scroll-add", "拼接下一屏", "拼接下一屏", "shot-btn secondary")}${button("scroll-auto", "自动滚动", "自动滚动", "shot-btn secondary")}${button("scroll-reset", "清空重截", "清空重截", "shot-btn secondary")}${button("scroll-finish", "完成长截图", "完成长截图", "shot-btn primary")}${button("scroll-copy", "复制长截图", "复制", "shot-btn secondary")}${button("scroll-save", "保存长截图", "保存", "shot-btn secondary")}</div>
      </div>`;
    const images = view.querySelector(".shot-scroll-source");
    for (let page = 1; page <= 5; page++) {
      const canvasPreview = canvas(shot.image.width, shot.image.height);
      const g = canvasPreview.getContext("2d");
      g.drawImage(shot.image, 0, 0);
      g.font = "bold 16px Segoe UI";
      g.fillStyle = "#e8f4ff";
      g.fillRect(8, 8, 79, 25);
      g.fillStyle = "#174473";
      g.fillText("第 " + page + " 屏", 17, 26);
      canvasPreview.setAttribute("aria-label", "示例第 " + page + " 屏");
      images.append(canvasPreview);
    }
    scrollClip = { view, frame, images, horizontal: false, shot, page: 0, autoRolling: false, autoTimer: null };
    function update() {
      const n = scrollClip.page + 1;
      const badge = view.querySelector("#shot-scroll-count");
      if (badge) badge.textContent = `${n} 屏`;
      view.querySelector("p").textContent = `${n} 屏已拼接 · 离线演示`;
      const autoBtn = view.querySelector('[data-shot="scroll-auto"]');
      if (autoBtn) {
        autoBtn.textContent = scrollClip.autoRolling ? "停止滚动" : "自动滚动";
        autoBtn.classList.toggle("is-active", scrollClip.autoRolling);
      }
      if (scrollClip.frame) {
        const fb = scrollClip.frame.querySelector(".shot-record-badge");
        if (fb) fb.textContent = scrollClip.autoRolling ? "自动滚动中 · 按 A 停止" : "长截图区域";
      }
    }
    scrollClip.update = update;
    view.querySelector("select").onchange = ev => {
      scrollClip.horizontal = ev.target.selectedIndex === 1;
      images.classList.toggle("horizontal", scrollClip.horizontal);
      scrollAction("scroll-reset");
    };
    images.onscroll = () => {
      if (!view.querySelector("#shot-scroll-auto").checked) return;
      const position = scrollClip.horizontal ? images.scrollLeft : images.scrollTop;
      const span = scrollClip.horizontal ? images.firstElementChild.offsetWidth : images.firstElementChild.offsetHeight;
      scrollClip.page = Math.min(4, Math.max(scrollClip.page, Math.round(position / span)));
      update();
    };
    update();
    positionRecorder(view, shot?.rect, "ready");
    images.focus();
  }
  function scrollAction(id) {
    if (!scrollClip) return;
    if (id === "scroll-close") return closeScroll();
    const { view, images, horizontal, shot } = scrollClip;
    if (id === "scroll-reset") {
      clearInterval(scrollClip.autoTimer);
      scrollClip.autoRolling = false;
      scrollClip.page = 0;
      images.scrollTo(0, 0);
      scrollClip.update?.();
      view.querySelector("p").textContent = "已清空重截 · 1 屏已就绪";
      return;
    }
    if (id === "scroll-auto") {
      scrollClip.autoRolling = !scrollClip.autoRolling;
      clearInterval(scrollClip.autoTimer);
      if (scrollClip.autoRolling) {
        scrollClip.autoTimer = setInterval(() => {
          if (!scrollClip || scrollClip.page >= 4) {
            clearInterval(scrollClip?.autoTimer);
            if (scrollClip) { scrollClip.autoRolling = false; scrollClip.update?.(); }
            return;
          }
          scrollClip.page++;
          const span = horizontal ? images.firstElementChild.offsetWidth : images.firstElementChild.offsetHeight;
          images.scrollTo(horizontal ? span * scrollClip.page : 0, horizontal ? 0 : span * scrollClip.page);
          scrollClip.update?.();
        }, 420);
      }
      scrollClip.update?.();
      return;
    }
    if (id === "scroll-add") {
      const position = horizontal ? images.scrollLeft : images.scrollTop;
      const span = horizontal ? images.firstElementChild.offsetWidth : images.firstElementChild.offsetHeight;
      scrollClip.page = Math.min(4, Math.max(scrollClip.page, Math.round(position / span)));
      scrollClip.update?.();
    }
    if (id === "scroll-copy" || id === "scroll-save") {
      return announce(id === "scroll-copy" ? `离线演示：已复制 ${scrollClip.page + 1} 屏长截图` : `离线演示：已保存 ${scrollClip.page + 1} 屏 PNG`);
    }
    if (id === "scroll-finish") {
      const count = scrollClip.page + 1;
      const w = shot.image.width, h = shot.image.height;
      const image = canvas(horizontal ? w * count : w, horizontal ? h : h * count);
      const painter = image.getContext("2d");
      for (let i = 0; i < count; i++)
        painter.drawImage(images.children[i], horizontal ? w * i : 0, horizontal ? 0 : h * i);
      closeScroll();
      pinImage({ image, words: [], original: { image, words: [] }, marks: [] });
    }
  }
  function execute(id, from) {
    const pin = from?.closest(".shot-pin") || from?.closest(".shot-menu")?.sourcePin,
      ed = editor;
    if (id === "recognition-menu") return openRecognitionMenu(from);
    if (id === "menu") {
      if (menu) { menu.remove(); menu = null; return; }
      menu = element("div", "shot-menu", desk());
      menu.setAttribute("role", "menu");
      menu.innerHTML =
        button("start", "开始截图") +
        button("video-select", "录制屏幕") +
        button("video-recover", "恢复录屏") +
        button("clipboard", "钉住剪贴板内容") +
        button("demo-text-pin", "试用文字贴图") +
        button("demo-color-pin", "试用颜色贴图") +
        button("restorePin", "恢复关闭的贴图") +
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
    if (id === "settings") { go("captureSettings"); return; }
    if (id === "preset-ollama" || id === "preset-openai") {
      if (id === "preset-ollama") { endpoint = "http://localhost:11434/v1"; model = "qwen2.5:7b"; }
      else { endpoint = "https://api.openai.com/v1"; model = "gpt-4o-mini"; }
      apiStatus = { tone: "muted", text: "已填入示例参数，可点击「测试连接」验证" };
      go("captureSettings");
      return;
    }
    if (id === "test-api") {
      const ok = !!(endpoint && model);
      apiStatus = ok
        ? { tone: "positive", text: "离线原型已校验地址与模型格式，桌面版支持真实连调" }
        : { tone: "danger", text: "请先填写 API 基础地址与模型名" };
      go("captureSettings");
      return announce(ok ? "离线原型已校验接口地址格式，桌面版支持实际连调" : "请先填写 API 基础地址与模型名");
    }
    if (id === "restore" || id.startsWith("clear-") || id.startsWith("record-")) {
      if (id === "restore") keys = { ...defaults };
      else if (id.startsWith("clear-")) keys[id.slice(6)] = "";
      recording = id.startsWith("record-") ? id.slice(7) : null;
      go("captureSettings");
      return;
    }
    if (id === "close-panel") { panel?.remove(); panel = null; return; }
    if (id === "close") {
      if (ed?.selected) { ed.selected = null; draw(); }
      else if (ed) finish();
      else if (pin) {
        if (pin.pinState.locked) return pin.flashHud("已锁定，请先解锁");
        closedPins.push({ shot: pin.shot, opacity: pin.pinState.opacity, left: pin.style.left, top: pin.style.top });
        if (closedPins.length > 10) closedPins.shift();
        pin.remove();
        pins.delete(pin);
      }
      return;
    }
    if (pin && id === "pin-top") {
      pin.pinState.topmost = !pin.pinState.topmost;
      pin.redrawPin();
      return pin.flashHud(pin.pinState.topmost ? "已置顶" : "已取消置顶");
    }
    if (pin && id === "pin-lock") {
      pin.pinState.locked = !pin.pinState.locked;
      pin.redrawPin();
      return pin.flashHud(pin.pinState.locked ? "已锁定" : "已解锁");
    }
    if (pin && id === "pin-pass") {
      pin.pinState.passthrough = !pin.pinState.passthrough;
      if (pin.pinState.passthrough && pin.pinState.opacity > 0.5) pin.pinState.opacity = 0.5;
      else if (!pin.pinState.passthrough) pin.pinState.opacity = 1;
      pin.redrawPin();
      return pin.flashHud(pin.pinState.passthrough ? "已启用鼠标穿透（再点穿透退出）" : "已退出鼠标穿透");
    }
    if (pin && id === "pin-thumb") return togglePinThumb(pin);
    if (id === "restorePin") {
      const last = closedPins.pop();
      if (!last) return announce("没有已关闭的贴图");
      if (last.value) return pinClipboardValue(last.value, last);
      return pinImage(last.shot, last);
    }
    if (id === "refresh" && ed && !ed.pin) {
      ed.source = sample(ed.c.width, ed.c.height);
      draw();
      return announce("已刷新桌面截图画面");
    }
    if (id.startsWith("video-") && id !== "video-select") return videoAction(id);
    if (id.startsWith("scroll-")) return scrollAction(id);
    if (!enabled) return announce("截图工具已关闭");
    if (id === "start") return start();
    if (id === "video-select") { start(); return announce("选择要录制的区域，再点击录屏"); }
    if (id === "demo-text-pin") return pinClipboardValue("项目周会\n周五 14:30 · B 会议室");
    if (id === "demo-color-pin") return pinClipboardValue("#0485F7");
    if (id === "clipboard") return typeof clipboard === "string" ? pinClipboardValue(clipboard) : pinImage(clipboard);
    if (id === "edit-pin") return start(pin);
    if (id === "manual") return textPanel(null, false);
    if (id === "copy-text" || id === "copy-result" || id === "send") {
      const src = panel.querySelector(id === "copy-result" ? ".shot-result" : ".shot-source");
      const all = src.selectionStart === src.selectionEnd;
      const value = all ? src.value : src.value.slice(src.selectionStart, src.selectionEnd);
      if (id !== "send") {
        if (value) {
          navigator.clipboard?.writeText(value).catch(() => announce("请选中文字后复制"));
          panel.setStatus ? panel.setStatus("已复制文字到剪贴板", "positive") : (panel.querySelector(".shot-status").textContent = "已复制文字");
          if (id === "copy-text" && all && panel.querySelector(".shot-copy-close")?.checked) {
            panel.remove(); panel = null;
          }
        }
        return;
      }
      language = panel.querySelector(".shot-language").value.trim();
      const map = new Map(samples.flatMap(([a, b]) => [[a, b], [b, a]]));
      const english = /^(en|english|英语|英文)/i.test(language),
        chinese = /^(zh|简体中文|中文|汉语)/i.test(language);
      const lines = value.split("\n");
      const match = (english || chinese) && lines.every((s) => map.has(s));
      const dstArea = panel.querySelector(".shot-result");
      dstArea.value = match
        ? lines.map((s) => /[\u3400-\u9fff]/.test(s) === chinese ? s : map.get(s)).join("\n")
        : "";
      dstArea.dispatchEvent(new Event("input"));
      const dstCount = panel.querySelector("#shot-dst-count");
      const copyDstBtn = panel.querySelector('[data-shot="copy-result"]');
      if (dstCount) { dstCount.textContent = `${dstArea.value.length} 字`; dstCount.hidden = !dstArea.value.length; }
      if (copyDstBtn) copyDstBtn.disabled = !dstArea.value.length;
      const msg = match ? "翻译完成（离线演示译文）" : "离线原型仅提供示例句译文，桌面版可翻译自定义原文";
      panel.setStatus ? panel.setStatus(msg, match ? "positive" : "muted") : (panel.querySelector(".shot-status").textContent = msg);
      return;
    }
    if (names[id] && ed) {
      ed.tool = ed.tool === id ? null : id;
      ed.appearance = {};
      ed.selected = null;
      draw();
      return;
    }
    if (id === "number-note" && ed?.selected?.kind === "number")
      return textEditor(ed, ed.selected.points[0], ed.selected);
    if (ed && id === "clearMarks") {
      if (ed.marks.length) changeMarks(ed, []);
      ed.selected = ed.next = null;
      draw();
      return;
    }
    if (ed && (id === "undo" || id === "redo")) {
      const a = id === "undo" ? ed.undo : ed.redo,
        b = id === "undo" ? ed.redo : ed.undo;
      if (a.length) { b.push(ed.marks); ed.marks = a.pop(); }
      ed.selected = ed.next = null;
      draw();
      return;
    }
    if (!ed?.sel && !pin) return;
    if (ed && !ed.pin && ed.sel && ["copy", "save", "pin", "ocr", "translate"].includes(id)) {
      selHistory.push({ ...ed.sel });
      if (selHistory.length > 20) selHistory.shift();
    }
    const shot = ed ? flat(ed) : pin.shot;
    if (id === "record" && ed && !ed.pin) {
      const box = ed.c.getBoundingClientRect(), d = desk().getBoundingClientRect();
      const sx = box.width / ed.c.width, sy = box.height / ed.c.height;
      const rect = {
        x: Math.round(box.left - d.left + ed.sel.x * sx),
        y: Math.round(box.top - d.top + ed.sel.y * sy),
        w: Math.round(ed.sel.w * sx),
        h: Math.round(ed.sel.h * sy),
      };
      shot.rect = rect;
      finish();
      return recordingPanel(shot, null, rect);
    }
    if (id === "scroll" && ed && !ed.pin) {
      const box = ed.c.getBoundingClientRect(), d = desk().getBoundingClientRect();
      const sx = box.width / ed.c.width, sy = box.height / ed.c.height;
      shot.rect = {
        x: Math.round(box.left - d.left + ed.sel.x * sx),
        y: Math.round(box.top - d.top + ed.sel.y * sy),
        w: Math.round(ed.sel.w * sx),
        h: Math.round(ed.sel.h * sy),
      };
      finish();
      return scrollingPanel(shot);
    }
    if (id === "apply") {
      ed.pin.shot = shot;
      ed.pin.redrawPin();
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
    if (recognitionKinds.includes(id) && id !== "ocr") recognitionPanel(shot, id);
  }
  function keyboard(ev) {
    const target = ev.target,
      base = /^Digit[0-9]$/.test(ev.code) ? ev.code.at(-1) : ev.key.length === 1 ? ev.key.toUpperCase() : ev.key;
    const chord = [
      ev.ctrlKey ? "Ctrl" : "",
      ev.altKey ? "Alt" : "",
      ev.shiftKey ? "Shift" : "",
      ev.metaKey ? "Win" : "",
      base,
    ].filter(Boolean).join("+");
    if (recording) {
      ev.preventDefault();
      ev.stopImmediatePropagation();
      if (["Control", "Shift", "Alt", "Meta"].includes(base)) return;
      if (base !== "Escape") {
        if (
          (!chord.includes("+") && !["record", "scroll"].includes(recording)) ||
          !/^(?:(?:Ctrl|Alt|Shift|Win)\+)*(?:[A-Z0-9]|F(?:[1-9]|1[0-9]|2[0-4]))$/.test(chord) ||
          ["C", "Ctrl+C", "Ctrl+S", "Ctrl+Z", "Ctrl+Y", "Ctrl+Shift+Z"].includes(chord) ||
          Object.entries(keys).some(([id, key]) => id !== recording && key === chord)
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
    if (editor && !editor.pin && base === "Alt") {
      editor.loupe = true;
      draw();
      return;
    }
    if (editor && editor.loupe && base === "Shift" && !ev.repeat) {
      editor.colorFormat = ((editor.colorFormat || 0) + 1) % 4;
      draw();
      return;
    }
    const pin = target.closest(".shot-pin");
    let id =
      enabled &&
      Object.keys(keys).find(
        (k) =>
          keys[k] === chord &&
          (["start", "clipboard", "restorePin"].includes(k) || (["record", "scroll", "pin"].includes(k) ? editor?.sel && !editor.pin : editor?.sel || pin)),
      );
    if (base === "Escape" && (editor || panel || menu || pin || recordingClip || scrollClip))
      id = scrollClip ? "scroll-close" : recordingClip ? "video-close" : panel ? "close-panel" : menu ? "menu" : "close";
    if (!id && scrollClip && chord === "A" && !ev.repeat) {
      ev.preventDefault();
      ev.stopImmediatePropagation();
      return scrollAction("scroll-auto");
    }
    if (!id && editor?.sel && (chord === "Shift+4" || chord === "Shift+8"))
      id = chord === "Shift+4" ? "number" : "text";
    if (!id && editor && !editor.pin && base === "F5") id = "refresh";
    if (!id && editor && !editor.pin && ["R", "Shift+R"].includes(chord) && selHistory.length) {
      editor.historyIndex = ev.shiftKey
        ? Math.max(0, editor.historyIndex - 1)
        : Math.min(selHistory.length - 1, editor.historyIndex + 1);
      editor.sel = { ...selHistory[selHistory.length - 1 - editor.historyIndex] };
      draw();
      ev.preventDefault();
      return;
    }
    if (!id && ["C", "Alt+C"].includes(chord) && editor && !editor.pin && editor.pointer) {
      const { x, y } = editor.pointer;
      const values = editor.source.image.getContext("2d").getImageData(x, y, 1, 1).data;
      const formats = colorValues(values);
      const color = formats[editor.colorFormat || 0];
      clipboard = formats[0];
      navigator.clipboard?.writeText(color).catch(() => announce("已复制到演示剪贴板 " + color));
      finish();
      announce("取色 " + color);
      ev.preventDefault();
      return;
    }
    if (["Delete", "Backspace"].includes(base) && editor?.selected) {
      editMark(editor, editor.selected, null);
      draw();
      ev.preventDefault();
      return;
    }
    if (editor && chord === "Ctrl+Shift+Z") {
      ev.preventDefault();
      return execute("clearMarks");
    }
    if (!id && editor && !editor.pin && ["Ctrl+A", "Ctrl+Shift+A"].includes(chord)) {
      editor.sel = { x: 0, y: 0, w: editor.c.width, h: editor.c.height };
      draw();
      ev.preventDefault();
      return;
    }
    if (editor?.sel && ["Enter", "Ctrl+C", "Ctrl+S", "Ctrl+Z", "Ctrl+Y"].includes(chord))
      id = { Enter: "copy", "Ctrl+C": "copy", "Ctrl+S": "save", "Ctrl+Z": "undo", "Ctrl+Y": "redo" }[chord];
    if (editor && !editor.pin && base === "Enter" && !editor.sel) {
      editor.sel = { ...editor.source.window };
      draw();
      ev.preventDefault();
      ev.stopImmediatePropagation();
      return;
    }
    if (pin && !editor && ["T", "L", "R"].includes(chord)) {
      id = { T: "pin-top", L: "pin-lock", R: "pin-thumb" }[chord];
    }
    const offsets = { ArrowLeft: [-1, 0], ArrowRight: [1, 0], ArrowUp: [0, -1], ArrowDown: [0, 1] };
    if (pin && !editor && offsets[chord] && !pin.pinState.locked) {
      const [dx, dy] = offsets[chord];
      pin.style.left = (pin.offsetLeft + dx) + "px";
      pin.style.top = (pin.offsetTop + dy) + "px";
      ev.preventDefault();
      return;
    }
    if (editor?.sel && !editor.pin && offsets[base]) {
      const [dx, dy] = offsets[base], r = editor.sel;
      if (ev.shiftKey && !ev.ctrlKey) {
        if (dx < 0) r.x = Math.min(r.x + 1, r.x + r.w - 1), r.w--;
        if (dx > 0) r.w = Math.max(1, r.w - 1);
        if (dy < 0) r.y = Math.min(r.y + 1, r.y + r.h - 1), r.h--;
        if (dy > 0) r.h = Math.max(1, r.h - 1);
      } else if (ev.ctrlKey && !ev.shiftKey) {
        if (dx < 0 && r.x > 0) { r.x--; r.w++; }
        if (dx > 0 && r.x + r.w < editor.c.width) r.w++;
        if (dy < 0 && r.y > 0) { r.y--; r.h++; }
        if (dy > 0 && r.y + r.h < editor.c.height) r.h++;
      } else {
        r.x = clamp(r.x + dx, 0, editor.c.width - r.w);
        r.y = clamp(r.y + dy, 0, editor.c.height - r.h);
      }
      draw();
      ev.preventDefault();
      ev.stopImmediatePropagation();
      return;
    }
    if (base === " ") {
      if (editor && !editor.pin && editor.sel) {
        editor.barsHidden = !editor.barsHidden;
        draw();
        ev.preventDefault();
        return;
      }
      if (pin || editor?.pin) id = editor?.pin ? "apply" : "edit-pin";
    }
    if (id) {
      ev.preventDefault();
      ev.stopImmediatePropagation();
      execute(id, target);
    }
  }
  function keyup(ev) {
    if (editor && !editor.pin && ev.key === "Alt") {
      editor.loupe = false;
      draw();
    }
  }
  function connect(options) {
    ({ announce, go } = options);
    reset();
    document.addEventListener("click", (ev) => {
      const b = ev.target.closest("[data-shot]");
      if (b) {
        if (b.tagName === "A") ev.preventDefault();
        execute(b.dataset.shot, b);
      } else if (menu) {
        menu.remove();
        menu = null;
      }
    });
    document.addEventListener("keydown", keyboard, true);
    document.addEventListener("keyup", keyup, true);
    document.addEventListener("change", (ev) => {
      const el = ev.target;
      if (el.id === "shot-enabled") {
        enabled = el.checked;
        const grp = document.querySelector(".shot-shortcuts-group");
        if (grp) grp.dataset.enabled = String(enabled);
      }
      if (el.id === "shot-url") endpoint = el.value;
      if (el.id === "shot-model") model = el.value;
      if (el.id === "shot-default-lang") language = el.value.trim() || "简体中文";
    });
  }
  return { settings, connect, reset, busy: () => !!(editor || menu || panel || recordingClip || scrollClip) };
})();
