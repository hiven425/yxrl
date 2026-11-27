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
  };
  const labels = {
    start: "开始截图",
    clipboard: "钉住剪贴板图片",
    pin: "钉住选区",
    ocr: "提取文字",
    translate: "打开翻译",
    record: "录制屏幕",
    scroll: "滚动长截图",
    restorePin: "恢复关闭的贴图",
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
  const glyphs = {
    rect: "□",
    ellipse: "○",
    arrow: "↗",
    line: "／",
    pen: "✎",
    text: "文字",
    highlight: "荧光",
    mosaic: "▦",
    undo: "↶",
    redo: "↷",
    clearMarks: "清空",
  };
  const actions = {
    undo: "撤销",
    redo: "重做",
    clearMarks: "清空标注",
    ocr: "提取文字",
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
  let keys,
    enabled,
    endpoint,
    model,
    language,
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
        return `<div class="shot-setting-row"><span class="shot-setting-title">${labels[id]}<em class="shot-scope" data-global="${isGlobal}">${isGlobal ? "全局" : "工具内"}</em></span>${button("record-" + id, labels[id] + "快捷键", recording === id ? "请按快捷键" : e(key || "已停用"), "shot-btn secondary")}${button("clear-" + id, "停用" + labels[id], "停用", "shot-btn secondary")}</div>`;
      })
      .join("");
    return settingsFrame(
      "截图与翻译",
      "captureSettings",
      `<section class="option-group"><label class="option-row"><span class="option-label shot-enable-label"><span>启用截图工具</span><small class="shot-status">控制全局热键（Ctrl+1 截图、Ctrl+2 贴图、Ctrl+3 恢复贴图）与月历底栏入口</small></span><input id="shot-enabled" type="checkbox" ${enabled ? "checked" : ""}></label></section>
       <section class="option-group"><h3>快捷键</h3>${rows}<div style="margin-top:8px">${button("restore", "恢复默认快捷键", "恢复默认快捷键", "shot-btn secondary")}</div></section>
       <section class="option-group"><h3>翻译服务</h3><p class="shot-status" style="margin:2px 0 8px">兼容 OpenAI Chat Completions 接口，密钥保存在 Windows 凭据管理器。</p><label class="field">API 基础地址<input id="shot-url" placeholder="http://localhost:11434/v1" value="${e(endpoint)}"></label><label class="field">模型名<input id="shot-model" placeholder="qwen2.5:7b" value="${e(model)}"></label><label class="field">API 密钥（可选）<input type="password" autocomplete="off" placeholder="原型使用演示样例" disabled></label><div style="margin-top:8px">${button("test-api", "测试连接", "测试连接", "shot-btn secondary")}</div></section>`,
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
          g.fillStyle = "#fff";
          g.fillRect(h.x - 3, h.y - 3, 6, 6);
          g.strokeRect(h.x - 3, h.y - 3, 6, 6);
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
          g.fillStyle = "#fff";
          g.fillRect(p.x - 3, p.y - 3, 6, 6);
          g.strokeRect(p.x - 3, p.y - 3, 6, 6);
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
    const size = 99, infoH = 38;
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
    g.strokeRect(lx + 1 + (p.x - sx) * 9, ly + 1 + (p.y - sy) * 9, 9, 9);
    const val = imgCanvas.getContext("2d").getImageData(p.x, p.y, 1, 1).data;
    const color = editor.hexColor === false
      ? `${val[0]},${val[1]},${val[2]}`
      : "#" + [...val.slice(0, 3)].map(v => v.toString(16).padStart(2, "0")).join("").toUpperCase();
    g.fillStyle = "#fff";
    g.font = "11px Segoe UI";
    g.fillText(`${p.x},${p.y} ${color}`, lx + 6, ly + size + 14);
    g.fillStyle = "#bcc8d4";
    g.font = "10px Segoe UI";
    g.fillText("C 复制 · Shift 切换", lx + 6, ly + size + 29);
    g.restore();
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
    info.innerHTML = `<span></span>${!pin ? button("refresh", "刷新画面 (F5)", "↻") : ""}`;
    const bar = element("div", "shot-bar", layer);
    const choices = element("div", "shot-tool-buttons", bar);
    choices.setAttribute("role", "toolbar");
    const toolEntries = Object.entries(names).map(([k, v]) => button(k, v, glyphs[k] || v));
    const actionEntries = Object.entries(actions)
      .filter(([k]) => !pin || !["pin", "record", "scroll"].includes(k))
      .map(([k, v]) => button(k, v, glyphs[k] || v));
    choices.innerHTML =
      toolEntries.join("") +
      '<span class="shot-sep" aria-hidden="true"></span>' +
      actionEntries.slice(0, 3).join("") +
      '<span class="shot-sep" aria-hidden="true"></span>' +
      actionEntries.slice(3).join("") +
      (pin ? button("apply", "完成标注") : "");
    const styles = element("div", "shot-style-options", bar);
    const swatches = paletteColors.map(([n, hex]) => `<button type="button" class="shot-swatch" data-swatch="${hex}" aria-label="颜色 ${n}" title="${n}"><i style="background:${hex}"></i></button>`).join("");
    styles.innerHTML = `<span class="shot-swatches">${swatches}</span>
      <label data-mark-kind="number"><span id="shot-serial-label">下一号</span> <input id="shot-serial" type="number" min="1" max="9999" aria-label="标注序号"></label>
      <label>大小 <select data-appearance="size" aria-label="标注大小">${[2,3,6,12,16,20,24,28,32,40,48,64,80].map(value => `<option>${value}</option>`).join("")}</select></label>
      <label data-mark-kind="text">字体 <select data-appearance="font" aria-label="标注字体"><option value="">系统字体</option><option>Segoe UI</option><option>Microsoft YaHei UI</option><option>Consolas</option></select></label>
      <label data-mark-kind="text"><input type="checkbox" data-appearance="bold"> 粗体</label>
      <label data-mark-kind="text"><input type="checkbox" data-appearance="italic"> 斜体</label>
      <label data-mark-kind="text"><input type="checkbox" data-appearance="outline"> 描边</label>
      <label data-mark-kind="text"><input type="checkbox" data-appearance="background"> 背景</label>
      <label data-mark-kind="number"><input type="checkbox" data-appearance="square"> 方形</label>
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
        if (editor?.loupe) draw();
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
        ed.sel = inside(source.window, point(ev))
          ? { ...source.window }
          : { x: 0, y: 0, w: c.width, h: c.height };
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
      button("ocr", "提取文字") +
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
      ? `<div class="shot-layout-bar"><label>排版 <select class="shot-para"><option value="smart">智能分段</option><option value="merge">合并换行</option><option value="keep">保留换行</option></select></label><label><input type="checkbox" class="shot-nospace"> 去空格</label><label>标点 <select class="shot-punct"><option value="keep">原标点</option><option value="cjk">中文标点</option><option value="en">英文标点</option></select></label><label><input type="checkbox" class="shot-show-orig" checked> 原图</label><label><input type="checkbox" class="shot-copy-close"> 复制后关闭</label><label><input type="checkbox" class="shot-panel-pin"> 置顶</label></div>`
      : `<div class="shot-layout-bar"><label><input type="checkbox" class="shot-panel-pin"> 置顶</label></div>`;
    panel.innerHTML = `<header><b>${shot ? "截图文字" : "粘贴文字翻译"}</b>${button("close-panel", "关闭文字面板", "×")}</header>${layoutBar}<div class="shot-columns">${shot ? '<div class="shot-original"><h3>原图（划选高亮对应原文）</h3><div class="shot-original-stage"><img alt="识别原图"><canvas></canvas></div></div>' : ""}<div class="shot-fields"><label class="field">原文<textarea class="shot-source size-medium" rows="4"></textarea></label><div class="shot-row">${button("copy-text", "复制原文", "复制原文", "shot-btn secondary")}<label>目标语言 <input class="shot-language" value="${e(language)}"></label>${button("send", "翻译")}</div><label class="field">译文<textarea class="shot-result size-medium" rows="3" readonly></textarea></label><div class="shot-row">${button("copy-result", "复制译文", "复制译文", "shot-btn secondary")}</div><small role="status" class="shot-status"></small></div></div>`;

    const srcArea = panel.querySelector(".shot-source");
    let spans = [], hi = new Set();
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
        ctx.fillRect(w.x, w.y, w.w, w.h);
        ctx.strokeRect(w.x, w.y, w.w, w.h);
      });
    }
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
        }
      };
    }
    panel.querySelector(".shot-panel-pin").onchange = ev => {
      panel.dataset.pinned = String(ev.target.checked);
    };
    panel.querySelector(".shot-status").textContent =
      shot && !shot.words.length ? "演示选区中没有文字" : "离线演示样例 · 支持图文选区联动";
    panel.querySelector(translate ? ".shot-language" : ".shot-source").focus();
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
    view.innerHTML = `<header><b>屏幕录制</b>${button("video-close", "关闭录屏", "×")}</header>
      <div class="shot-video-image"><img alt="录制区域"><span class="shot-video-position" hidden></span><kbd class="shot-video-key" hidden>Ctrl+T</kbd><span class="shot-video-camera" hidden>摄像头示例</span></div>
      <div class="shot-video-options"><label>帧率 <select id="shot-fps">${[5, 16, 24, 30, 60].map(value => `<option ${value === 24 ? "selected" : ""}>${value}</option>`).join("")}</select></label>
      <label>延时（秒） <select id="shot-delay">${[0, 1, 2, 3, 5, 10].map(value => `<option>${value}</option>`).join("")}</select></label>
      <label>模式 <select id="shot-mode"><option>普通录制</option><option>快速保存 MP4</option></select></label>
      <label><input type="checkbox" checked> 显示光标</label><label><input type="checkbox" id="shot-video-system"> 系统声音</label><label><input type="checkbox" id="shot-video-mic"> 麦克风</label>
      <label><input type="checkbox" id="shot-video-mouse"> 鼠标点击和滚轮</label><label><input type="checkbox" id="shot-video-keys"> 显示快捷键</label>
      <label><input type="checkbox" id="shot-video-camera"> 摄像头</label>
      <label class="shot-microphone-field" hidden>麦克风设备 <select aria-label="麦克风设备"><option>Windows 默认麦克风</option><option>内置麦克风阵列（示例）</option></select></label>
      <div class="shot-camera-fields" hidden><label>摄像头设备 <select><option>本机摄像头（示例）</option></select></label>
      <label>位置 <select id="shot-camera-corner" aria-label="摄像头位置"><option>右下</option><option>左下</option><option>右上</option><option>左上</option></select></label><label><input type="checkbox" id="shot-camera-circle"> 圆形</label></div></div>
      <div class="shot-video-trim" hidden><label>回放进度 <input id="shot-playhead" type="range" min="0" step="0.1"></label>
      <label>开始（秒） <input type="number" min="0" value="0" step=".1"></label><label>结束（秒） <input id="shot-video-end" type="number" min="0" step=".1"></label>
      <label>倍速 <select id="shot-speed">${[.25,.5,1,1.25,1.5,2].map(speed => `<option ${speed === 1 ? "selected" : ""}>${speed}</option>`).join("")}</select></label>
      <label>导出格式 <select><option>MP4</option><option>GIF</option><option>WebP</option></select></label>
      ${["左", "上", "右", "下"].map(side => `<label>裁${side} <input type="number" min="0" value="0" data-crop="${side}"></label>`).join("")}</div>
      <fieldset class="shot-video-mixer" hidden><legend>音轨与动画</legend>
      ${[["system","系统声音"],["mic","麦克风"]].map(([id,label]) => `<div data-track="${id}"><label>${label} <input type="range" min="0" max="100" value="100" aria-label="${label}音量"></label><output>100%</output><label><input type="checkbox" aria-label="${label}静音"> 静音</label></div>`).join("")}
      <label data-effect="mouse"><input type="checkbox" checked> 鼠标动画</label><label data-effect="keys"><input type="checkbox" checked> 快捷键动画</label></fieldset>
      <div class="shot-row">${button("video-start", "开始录制")}${button("video-pause", "暂停")}${button("video-save", "另存为")}${button("video-repeat", "重新录制")}${button("video-interrupt", "模拟中断")}</div>
      <p class="shot-video-status" role="status"></p>`;
    view.querySelector("img").src = shot.image.toDataURL();
    recordingClip = { view, frame, rect: areaRect, shot, stage: recovered ? "review" : "ready", elapsed: recovered?.elapsed || 0, left: 0, position: 0,
      options: recovered?.options || {}, timer: null };
    if (recovered) {
      view.querySelector("#shot-video-end").value = recovered.elapsed.toFixed(1);
      view.querySelector("#shot-playhead").max = recovered.elapsed.toFixed(1);
      recordingClock(recordingClip);
    }
    view.querySelector(".shot-video-options").onchange = () => {
      recordingClip.options = Object.fromEntries(["system", "mic", "mouse", "keys", "camera"].map(
        value => [value, view.querySelector("#shot-video-" + value).checked]));
      recordingClip.options.corner = view.querySelector("#shot-camera-corner").selectedIndex;
      recordingClip.options.circle = view.querySelector("#shot-camera-circle").checked;
      updateRecording();
    };
    view.querySelector(".shot-video-mixer").onchange = updateRecording;
    view.querySelectorAll("[data-track] input[type=range]").forEach(range => range.oninput = () =>
      range.closest("[data-track]").querySelector("output").value = range.value + "%");
    view.querySelector("#shot-playhead").oninput = ev => {
      recordingClip.position = Number(ev.target.value);
      updateRecording();
    };
    view.querySelector(".shot-video-trim").onchange = () => {
      const inset = ["上", "右", "下", "左"].map(side => Math.max(0, Number(view.querySelector(`[data-crop="${side}"]`).value)));
      view.querySelector("img").style.clipPath = `inset(${inset.map(n => n + "px").join(" ")})`;
    };
    updateRecording();
    view.querySelector('[data-shot="video-start"]').focus();
  }
  function updateRecording() {
    const clip = recordingClip, { view, stage } = clip;
    const paused = stage === "paused", review = stage === "review", live = stage === "live";
    const playhead = view.querySelector("#shot-playhead");
    playhead.value = clip.position.toFixed(1);
    view.querySelector(".shot-video-options").hidden = stage !== "ready";
    view.querySelector(".shot-camera-fields").hidden = !clip.options.camera;
    view.querySelector(".shot-microphone-field").hidden = !clip.options.mic;
    const inset = view.querySelector(".shot-video-camera");
    inset.hidden = !clip.options.camera;
    inset.dataset.corner = clip.options.corner || 0;
    inset.classList.toggle("round", !!clip.options.circle);
    view.querySelector(".shot-video-trim").hidden = !review;
    const mixer = view.querySelector(".shot-video-mixer");
    mixer.hidden = !review || !["system", "mic", "mouse", "keys"].some(key => clip.options[key]);
    mixer.querySelectorAll("[data-track],[data-effect]").forEach(row => row.hidden = !clip.options[row.dataset.track || row.dataset.effect]);
    const primary = view.querySelector('[data-shot="video-start"]');
    primary.hidden = stage === "delay";
    const title = review ? (clip.playing ? "暂停播放" : "播放") : live || paused ? "停止录制" : "开始录制";
    primary.textContent = title;
    primary.setAttribute("aria-label", title);
    for (const [id, visible] of [["pause", live || paused], ["save", review], ["repeat", review], ["interrupt", live || paused]]) {
      const control = view.querySelector(`[data-shot="video-${id}"]`);
      control.hidden = !visible;
      if (id === "pause") {
        control.textContent = paused ? "继续" : "暂停";
        control.setAttribute("aria-label", control.textContent);
      }
    }
    const label = stage === "delay" ? `${Math.max(1, Math.ceil(clip.left))} 秒后开始录制` :
      live ? "正在录制" : paused ? "已暂停" : review ? "回放" : "就绪";
    view.querySelector("header b").textContent = review ? "录屏回放" : "屏幕录制";
    const status = view.querySelector(".shot-video-status");
    status.textContent = `${label} · ${(review ? clip.position : clip.elapsed).toFixed(1)} 秒 · 离线演示`;
    status.dataset.live = String(live);
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
    if (id === "video-interrupt") {
      recoverySample = { shot: clip.shot, elapsed: clip.elapsed, options: { ...clip.options } };
      closeRecording();
      return announce("已留下可恢复的录屏片段");
    }
    if (id === "video-save") return announce("离线原型已完成导出流程，桌面版生成实际文件");
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
        clip.stage = "review";
        clip.view.querySelector("#shot-video-end").value = clip.elapsed.toFixed(1);
        clip.view.querySelector("#shot-playhead").max = clip.elapsed.toFixed(1);
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
        clip.position = Math.min(clip.elapsed, clip.position + delta * Number(clip.view.querySelector("#shot-speed").value));
        if (clip.position >= clip.elapsed) clip.playing = false;
      } else if (clip.stage === "paused" || clip.stage === "review") return;
      updateRecording();
    }, 100);
  }
  function closeScroll() {
    scrollClip?.view.remove();
    scrollClip = null;
  }
  function scrollingPanel(shot) {
    closeScroll();
    const view = element("section", "shot-recorder", desk());
    view.setAttribute("aria-label", "长截图演示");
    view.innerHTML = `<header><b>长截图</b>${button("scroll-close", "关闭长截图", "×")}</header>
      <div class="shot-video-options"><label>滚动方向 <select id="shot-scroll-axis"><option>纵向向下</option><option>横向向右</option></select></label>
      <label><input id="shot-scroll-auto" type="checkbox" checked> 滚动后自动拼接</label></div>
      <div class="shot-scroll-source" tabindex="0" aria-label="可滚动示例区域"></div>
      <div class="shot-row">${button("scroll-add", "拼接下一屏")}${button("scroll-finish", "完成长截图")}</div>
      <p class="shot-video-status" role="status"></p>`;
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
    scrollClip = { view, images, horizontal: false, shot, page: 0 };
    function update() {
      view.querySelector("p").textContent = `${scrollClip.page + 1} 屏已拼接 · 离线演示`;
    }
    view.querySelector("select").onchange = ev => {
      scrollClip.horizontal = ev.target.selectedIndex === 1;
      images.classList.toggle("horizontal", scrollClip.horizontal);
      scrollClip.page = 0;
      images.scrollTo(0, 0);
      update();
    };
    images.onscroll = () => {
      if (!view.querySelector("#shot-scroll-auto").checked) return;
      const position = scrollClip.horizontal ? images.scrollLeft : images.scrollTop;
      const span = scrollClip.horizontal ? images.firstElementChild.offsetWidth : images.firstElementChild.offsetHeight;
      scrollClip.page = Math.min(4, Math.max(scrollClip.page, Math.round(position / span)));
      update();
    };
    update();
    images.focus();
  }
  function scrollAction(id) {
    if (!scrollClip) return;
    if (id === "scroll-close") return closeScroll();
    const { view, images, horizontal, shot } = scrollClip;
    if (id === "scroll-add") {
      const position = horizontal ? images.scrollLeft : images.scrollTop;
      const span = horizontal ? images.firstElementChild.offsetWidth : images.firstElementChild.offsetHeight;
      scrollClip.page = Math.min(4, Math.max(scrollClip.page, Math.round(position / span)));
      view.querySelector("p").textContent = `${scrollClip.page + 1} 屏已拼接 · 离线演示`;
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
    const pin = from?.closest(".shot-pin"),
      ed = editor;
    if (id === "menu") {
      if (menu) { menu.remove(); menu = null; return; }
      menu = element("div", "shot-menu", desk());
      menu.setAttribute("role", "menu");
      menu.innerHTML =
        button("start", "开始截图") +
        button("video-select", "录制屏幕") +
        button("video-recover", "恢复录屏") +
        button("clipboard", "钉住剪贴板图片") +
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
    if (id === "test-api") return announce(endpoint && model ? "离线原型已校验接口地址格式，桌面版支持实际连调" : "请先填写 API 基础地址与模型名");
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
    if (id === "clipboard") return pinImage(clipboard);
    if (id === "edit-pin") return start(pin);
    if (id === "manual") return textPanel(null, false);
    if (id === "copy-text" || id === "copy-result" || id === "send") {
      const src = panel.querySelector(id === "copy-result" ? ".shot-result" : ".shot-source");
      const all = src.selectionStart === src.selectionEnd;
      const value = all ? src.value : src.value.slice(src.selectionStart, src.selectionEnd);
      if (id !== "send") {
        if (value) {
          navigator.clipboard?.writeText(value).catch(() => announce("请选中文字后复制"));
          panel.querySelector(".shot-status").textContent = "已复制文字";
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
      panel.querySelector(".shot-result").value = match
        ? lines.map((s) => /[\u3400-\u9fff]/.test(s) === chinese ? s : map.get(s)).join("\n")
        : "";
      panel.querySelector(".shot-status").textContent = match
        ? "离线演示译文"
        : "离线原型仅提供示例句译文，桌面版可翻译自定义原文";
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
    if (id === "scroll" && ed && !ed.pin) { finish(); return scrollingPanel(shot); }
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
      editor.hexColor = !editor.hexColor;
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
    if (!id && chord === "C" && editor && !editor.pin && editor.pointer) {
      const { x, y } = editor.pointer;
      const values = editor.source.image.getContext("2d").getImageData(x, y, 1, 1).data;
      const color = "#" + [...values.slice(0, 3)].map(value => value.toString(16).padStart(2, "0")).join("").toUpperCase();
      navigator.clipboard?.writeText(color).catch(() => {});
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
      if (el.id === "shot-enabled") enabled = el.checked;
      if (el.id === "shot-url") endpoint = el.value;
      if (el.id === "shot-model") model = el.value;
    });
  }
  return { settings, connect, reset, busy: () => !!(editor || menu || panel || recordingClip || scrollClip) };
})();
