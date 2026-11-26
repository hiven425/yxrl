const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");

const root = path.join(__dirname, "..");
const data = JSON.parse(fs.readFileSync(path.join(root, "src/Yxrl/Calendar/CalendarData.json"), "utf8"));
const script = fs.readFileSync(path.join(root, "docs/prototype/model.js"), "utf8");
const reference = vm.runInNewContext(script + "\nCalendarModel");

test("原生时钟、月历与离线原型使用相同的内置节日规则", () => {
  const rules = Array.from(reference.initialState().rules, (rule) => {
    const item = { id: rule.id, name: rule.name };
    if (rule.eve) return { ...item, kind: "eve" };
    if (rule.kind !== "solar") item.kind = rule.kind;
    for (const key of ["month", "day", "nth", "dayOfWeek"]) {
      if (rule[key] !== undefined) item[key] = rule[key];
    }
    return item;
  });
  assert.deepEqual(data.festivals, rules);
});

test("两版对年度通知覆盖的每个日期给出相同的休班标记", () => {
  for (let day = "2026-01-01"; day <= "2027-12-31"; day = reference.plusDays(day, 1)) {
    const native = data.workdays.includes(day) ? "班" :
      data.breaks.some(([first, last]) => first <= day && day <= last) ? "休" : "";
    assert.equal(native, reference.dayMark(day), day);
  }
});
