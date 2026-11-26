window.ReminderPreview = (() => {
  const leadLimit = 30 * 24 * 60;
  const minute = 60000;
  const dateTime = (date, time) => new Date(date + "T" + time).getTime();
  const display = (value) => {
    const at = new Date(value);
    const two = CalendarModel.two;
    return at.getFullYear() + "-" + two(at.getMonth() + 1) + "-" + two(at.getDate()) +
      " " + two(at.getHours()) + ":" + two(at.getMinutes());
  };

  function connect({ host, dialog, menu, popup, showDialog, entries, now, refresh, locked }) {
    const V = CalendarViews;
    let nextId = 1;
    let activeCell;
    let visibleNotices = [];
    let wasLocked = false;

    function closeMenu(restoreFocus = false) {
      if (restoreFocus) activeCell?.focus();
      activeCell = null;
      menu.hidden = true;
    }

    function openMenu(cell, x, y) {
      const date = cell.dataset.date;
      const buttons = [["新建提醒…", null]];
      entries().filter(item => item.date === date).sort((a, b) => a.at - b.at).forEach(item => {
        buttons.push([item.time + "  " + item.title + (item.delivered ? " · 已提醒" : ""), item]);
      });
      menu.replaceChildren(...buttons.map(([label, entry]) => {
        const button = document.createElement("button");
        button.type = "button";
        button.setAttribute("role", "menuitem");
        button.textContent = label;
        button.addEventListener("click", () => {
          closeMenu();
          edit(date, entry);
        });
        return button;
      }));
      activeCell = cell;
      menu.hidden = false;
      menu.style.left = Math.max(4, Math.min(x, innerWidth - menu.offsetWidth - 4)) + "px";
      menu.style.top = Math.max(4, Math.min(y, innerHeight - menu.offsetHeight - 4)) + "px";
      menu.firstElementChild.focus();
    }

    function edit(date, original) {
      const today = CalendarModel.today;
      const near = display(now() + 15 * minute);
      const preset = date === today && near.startsWith(date) ? near.slice(11) : date === today ? "23:59" : "09:00";
      const choices = [0, 5, 10, 15, 30, 60, 1440].map(value => '<option value="' + value + '"></option>').join("");
      showDialog(original ? "编辑日期提醒" : "新建日期提醒",
        '<form class="date-reminder-form"><label class="field">标题<input name="title" maxlength="100" required value="' +
        V.e(original?.title || "") + '"></label><div class="field-pair"><label class="field">事项日期<input type="date" name="date" value="' +
        date + '" min="2026-01-01" max="2027-12-31" required></label><label class="field">事项时间<input type="time" name="time" value="' +
        (original?.time || preset) + '" required></label></div><label class="field">提前分钟数<input type="number" name="lead" min="0" max="' +
        leadLimit + '" list="date-leads" value="' + (original?.lead ?? 10) + '" required></label><datalist id="date-leads">' + choices +
        '</datalist><p class="reminder-preview" role="status"></p><p class="form-error" role="alert"></p><div class="form-actions">' +
        (original ? '<button type="button" class="secondary" name="remove">删除提醒</button>' : '') +
        '<button class="primary" type="submit">保存提醒</button></div></form>');
      const form = dialog.querySelector(".date-reminder-form");
      const fields = form.elements;
      const preview = form.querySelector(".reminder-preview");
      const error = form.querySelector(".form-error");

      function readDraft() {
        const key = fields.date.value, time = fields.time.value, lead = fields.lead.valueAsNumber;
        const at = dateTime(key, time);
        if (!Number.isFinite(at)) throw new Error("请选择有效的事项日期和时间。");
        if (!Number.isInteger(lead) || lead < 0 || lead > leadLimit) throw new Error("请填写有效的提前分钟数。");
        const changed = !original || original.at !== at || original.lead !== lead;
        if (changed && at <= now()) throw new Error("事项时间已过，请选择之后的时间。");
        return { id: original?.id ?? nextId, title: fields.title.value.trim(), date: key, time, at, lead,
          due: at - lead * minute, delivered: changed ? false : original.delivered };
      }

      function updatePreview() {
        try {
          const draft = readDraft();
          preview.textContent = (draft.delivered ? "已提醒 · 计划时间 " : "弹框时间 ") + display(draft.due) +
            (!draft.delivered && draft.due <= now() ? " · 保存后立即提醒" : "");
          error.textContent = "";
          form.querySelector('[type="submit"]').disabled = !draft.title;
        } catch (problem) {
          preview.textContent = "请填写事项日期和时间";
          error.textContent = problem.message;
          form.querySelector('[type="submit"]').disabled = true;
        }
      }
      form.addEventListener("input", updatePreview);
      form.addEventListener("submit", event => {
        event.preventDefault();
        event.stopPropagation();
        try {
          const draft = readDraft();
          if (!draft.title) return;
          const records = entries();
          if (original) records[records.indexOf(original)] = draft;
          else { records.push(draft); nextId++; }
          dialog.close();
          refresh();
        } catch (problem) {
          error.textContent = problem.message;
        }
      });
      fields.remove?.addEventListener("click", () => {
        if (!confirm("删除后将取消这条提醒。")) return;
        const records = entries();
        records.splice(records.indexOf(original), 1);
        dialog.close();
        refresh();
      });
      updatePreview();
      fields.title.focus();
    }

    host.addEventListener("contextmenu", event => {
      const cell = event.target.closest("button[data-date]:not(:disabled)");
      if (!cell) return;
      event.preventDefault();
      openMenu(cell, event.clientX, event.clientY);
    });
    host.addEventListener("keydown", event => {
      const cell = event.target.closest("button[data-date]:not(:disabled)");
      if (!cell || !(event.key === "ContextMenu" || event.shiftKey && event.key === "F10")) return;
      event.preventDefault();
      const slot = cell.getBoundingClientRect();
      openMenu(cell, slot.left, slot.bottom);
    });
    menu.addEventListener("keydown", event => {
      if (event.key === "Escape") {
        event.preventDefault();
        event.stopPropagation();
        closeMenu(true);
        return;
      }
      if (event.key !== "ArrowDown" && event.key !== "ArrowUp") return;
      event.preventDefault();
      const buttons = Array.from(menu.children);
      const shift = event.key === "ArrowDown" ? 1 : -1;
      buttons[(buttons.indexOf(document.activeElement) + buttons.length + shift) % buttons.length].focus();
    });
    document.addEventListener("pointerdown", event => {
      if (!menu.hidden && !menu.contains(event.target)) closeMenu();
    });

    function clearPopup() {
      visibleNotices = [];
      popup.hidden = true;
    }
    popup.querySelector("button").addEventListener("click", clearPopup);

    function tick(instant) {
      const due = entries().filter(item => !item.delivered && item.due <= instant).sort((a, b) => a.due - b.due);
      due.forEach(item => { item.delivered = true; });
      visibleNotices.push(...due);
      const isLocked = locked();
      if (!visibleNotices.length || (!due.length && isLocked === wasLocked)) return;
      wasLocked = isLocked;
      popup.querySelector(".notice-entries").innerHTML = isLocked ? "<p>有日期提醒待查看。</p>" : visibleNotices.map(item =>
        "<div><strong>" + V.e(item.title) + "</strong><p>" + display(item.at) + " · " +
          (item.lead ? "提前 " + item.lead + " 分钟" : "到点提醒") + "</p></div>").join("");
      popup.hidden = false;
    }

    return { closeMenu, tick, reset: () => { closeMenu(); clearPopup(); } };
  }

  return { connect };
})();
