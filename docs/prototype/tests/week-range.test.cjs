const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");

const prototype = path.resolve(__dirname, "..");
const scripts = ["data/lunar-days.js", "model.js"].map((file) =>
  fs.readFileSync(path.join(prototype, file), "utf8"),
);
const calendar = vm.runInNewContext(scripts.join("\n") + "\nCalendarModel");
const range = { from: "2026-11-23", to: "2026-11-29" };

test("周一开头时日期范围保留 ISO 第 48 周", () => {
  const config = { showWeeks: true, weekStart: 1, weekRange: range };
  assert.equal(calendar.displayWeek("2026-11-16", config), null);
  assert.equal(calendar.displayWeek("2026-11-23", config), 48);
  assert.equal(calendar.displayWeek("2026-11-30", config), null);
});

test("周日开头的边界行取范围内实际日期的 ISO 周号", () => {
  const config = { showWeeks: true, weekStart: 0, weekRange: range };
  assert.equal(calendar.displayWeek("2026-11-22", config), 48);
  // 这一行只有 11 月 29 日在范围内，该日仍属于第 48 周。
  assert.equal(calendar.displayWeek("2026-11-29", config), 48);
  assert.equal(calendar.displayWeek("2026-12-06", config), null);
});

test("跨年保留 ISO 周年，关闭周数后详情和时钟也不显示周号", () => {
  const state = calendar.initialState();
  state.calendar.weekRange = { from: "2027-01-01", to: "2027-01-01" };
  assert.equal(calendar.weekForDate("2027-01-01", state.calendar), 53);
  assert.equal(calendar.weekForDate("2027-01-02", state.calendar), null);
  const outside = calendar.partsFor(state, "10:32:01");
  assert.equal(calendar.readFormat("{周数}", outside), "");
  assert.equal(calendar.clockLines("week", outside, state.formats)[0], "10:32:01");
  state.calendar.weekRange = null;
  state.calendar.showWeeks = false;
  assert.equal(calendar.displayWeek(calendar.today, state.calendar), null);
  assert.equal(calendar.weekForDate(calendar.today, state.calendar), null);
});
