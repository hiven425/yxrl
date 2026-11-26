/* 日历计算与演示状态。所有日期键均为本地民用日期 yyyy-MM-dd。 */
const CalendarModel = (() => {
  const today = "2026-11-26";
  const firstMonth = "2026-01";
  const lastMonth = "2027-12";
  const weekdayNames = "日一二三四五六";
  const two = (value) => String(value).padStart(2, "0");
  const dateOf = (key) => new Date(key + "T12:00:00Z");
  const keyOf = (date) => date.toISOString().slice(0, 10);

  function plusDays(key, days) {
    const date = dateOf(key);
    date.setUTCDate(date.getUTCDate() + days);
    return keyOf(date);
  }

  function monthOffset(month, offset) {
    const date = dateOf(month + "-01");
    date.setUTCMonth(date.getUTCMonth() + offset);
    return keyOf(date).slice(0, 7);
  }

  function weekNumber(key) {
    const date = dateOf(key);
    date.setUTCDate(date.getUTCDate() + 3 - ((date.getUTCDay() + 6) % 7));
    const yearStart = Date.UTC(date.getUTCFullYear(), 0, 1);
    return Math.ceil((date.getTime() - yearStart) / 86400000 / 7);
  }

  function startOfWeek(key, startsOn = 1) {
    const difference = (dateOf(key).getUTCDay() - startsOn + 7) % 7;
    return plusDays(key, -difference);
  }
  function displayWeek(key, cal) {
    if (!cal.showWeeks) return null;
    const start = startOfWeek(key, cal.weekStart);
    if (cal.weekRange && (plusDays(start, 6) < cal.weekRange.from || start > cal.weekRange.to))
      return null;
    const thursday = plusDays(start, (4 - dateOf(start).getUTCDay() + 7) % 7);
    const range = cal.weekRange;
    const reference = !range
      ? thursday
      : thursday < range.from
        ? range.from
        : thursday > range.to
          ? range.to
          : thursday;
    return weekNumber(reference);
  }
  function weekForDate(key, cal) {
    if (!cal.showWeeks || (cal.weekRange && (key < cal.weekRange.from || key > cal.weekRange.to)))
      return null;
    return weekNumber(key);
  }
  function timeLabel(value, hour12) {
    if (!value || !hour12) return value;
    const [h, ...rest] = value.split(":");
    return (Number(h) < 12 ? "上午" : "下午") + (Number(h) % 12 || 12) + ":" + rest.join(":");
  }
  function weeksInView(month, startsOn = 1, visibleStart = null) {
    const first = month + "-01";
    const aligned = startOfWeek(first, startsOn);
    const start = visibleStart || aligned;
    const next = monthOffset(month, 1) + "-01";
    const weeks = [];
    const rowCount = Math.ceil(daysBetween(aligned, next) / 7);
    for (let i = 0; i < rowCount; i++) {
      const monday = plusDays(start, i * 7);
      weeks.push(Array.from({ length: 7 }, (_, index) => plusDays(monday, index)));
    }
    return weeks;
  }

  function displayDate(key, includeYear = false) {
    const date = dateOf(key);
    const prefix = includeYear ? date.getUTCFullYear() + "年" : "";
    return prefix + (date.getUTCMonth() + 1) + "月" + date.getUTCDate() + "日";
  }

  const weekday = (key) => weekdayNames[dateOf(key).getUTCDay()];
  const lunar = (key) => lunarDays[key];
  const daysBetween = (from, to) => Math.round((dateOf(to) - dateOf(from)) / 86400000);

  const solar = [
    ["newyear", "元旦", 1, 1],
    ["police", "中国人民警察节", 1, 10, "警察节"],
    ["valentine", "情人节", 2, 14],
    ["leifeng", "学雷锋纪念日", 3, 5, "学雷锋日"],
    ["women", "妇女节", 3, 8],
    ["trees", "植树节", 3, 12],
    ["rights", "消费者权益日", 3, 15, "消费者日"],
    ["fools", "愚人节", 4, 1],
    ["labour", "劳动节", 5, 1],
    ["youth", "五四青年节", 5, 4, "青年节"],
    ["children", "儿童节", 6, 1],
    ["teachers", "教师节", 9, 10],
    ["national", "国庆节", 10, 1],
    ["halloween-eve", "万圣夜", 10, 31],
    ["all-saints", "万圣节", 11, 1],
    ["christmas", "圣诞节", 12, 25],
  ];
  const lunarRules = [
    ["spring", "春节", 1, 1],
    ["lantern", "元宵节", 1, 15],
    ["dragon", "端午节", 5, 5],
    ["qixi", "七夕节", 7, 7],
    ["midautumn", "中秋节", 8, 15],
    ["double9", "重阳节", 9, 9],
    ["xiayuan", "下元节", 10, 15],
    ["laba", "腊八节", 12, 8],
  ];
  function rulesFrom(rows, kind) {
    return rows.map(([id, name, month, day, alias = ""]) => ({
      id,
      name,
      kind,
      month,
      day,
      alias,
      enabled: true,
    }));
  }
  const builtins = [
    ...rulesFrom(solar, "solar"),
    ...rulesFrom(lunarRules, "lunar"),
    { id: "eve", name: "除夕", kind: "lunar", eve: true, enabled: true },
    ...[
      ["mother", "母亲节", 5, 2, 0],
      ["father", "父亲节", 6, 3, 0],
      ["thanks", "感恩节", 11, 4, 4],
    ].map(([id, name, month, nth, dayOfWeek]) => ({
      id,
      name,
      kind: "dynamic",
      month,
      nth,
      dayOfWeek,
      enabled: true,
    })),
  ];

  function matchesFestival(rule, key) {
    const date = dateOf(key);
    if (rule.eve) {
      const next = lunar(plusDays(key, 1));
      return next?.month === 1 && next.day === 1;
    }
    if (rule.kind === "lunar") return lunar(key)?.month === rule.month && lunar(key)?.day === rule.day;
    if (rule.month !== date.getUTCMonth() + 1) return false;
    if (rule.kind === "solar") return date.getUTCDate() === rule.day;
    if (date.getUTCDay() !== rule.dayOfWeek) return false;
    return rule.nth === -1
      ? plusDays(key, 7).slice(0, 7) !== key.slice(0, 7)
      : Math.ceil(date.getUTCDate() / 7) === rule.nth;
  }

  const festivalsOn = (rules, key) =>
    rules.filter((rule) => rule.enabled && matchesFestival(rule, key));
  const lunarMonths = ["正", "二", "三", "四", "五", "六", "七", "八", "九", "十", "冬", "腊"];
  const lunarDigits = "一二三四五六七八九十";
  function lunarDayName(day) {
    if ([10, 20, 30].includes(day)) return { 10: "初十", 20: "二十", 30: "三十" }[day];
    return ["初", "十", "廿"][Math.floor(day / 10)] + lunarDigits[(day - 1) % 10];
  }
  function ruleText(rule) {
    if (rule.eve) return "农历正月初一的前一天";
    if (rule.kind === "lunar")
      return "每年农历" + lunarMonths[rule.month - 1] + "月" + lunarDayName(rule.day);
    if (rule.kind === "solar") return "每年" + rule.month + "月" + rule.day + "日";
    const ordinal = rule.nth === -1 ? "最后一个" : "第" + "一二三四五"[rule.nth - 1] + "个";
    return "每年" + rule.month + "月" + ordinal + "星期" + weekdayNames[rule.dayOfWeek];
  }

  // 国务院办公厅 2026、2027 年通知。休假区间与补班日期均为明示数据。
  const breaks = [
    ["2026-01-01", "2026-01-03"],
    ["2026-02-15", "2026-02-23"],
    ["2026-04-04", "2026-04-06"],
    ["2026-05-01", "2026-05-05"],
    ["2026-06-19", "2026-06-21"],
    ["2026-09-25", "2026-09-27"],
    ["2026-10-01", "2026-10-07"],
    ["2027-01-01", "2027-01-03"],
    ["2027-02-05", "2027-02-13"],
    ["2027-04-03", "2027-04-05"],
    ["2027-05-01", "2027-05-05"],
    ["2027-06-09", "2027-06-09"],
    ["2027-09-15", "2027-09-15"],
    ["2027-10-01", "2027-10-07"],
  ];
  const workdays = new Set([
    "2026-01-04",
    "2026-02-14",
    "2026-02-28",
    "2026-05-09",
    "2026-09-20",
    "2026-10-10",
    "2027-01-31",
    "2027-02-14",
    "2027-05-08",
    "2027-09-26",
    "2027-10-09",
  ]);
  function dayMark(key) {
    if (workdays.has(key)) return "班";
    return breaks.some(([first, last]) => first <= key && key <= last) ? "休" : "";
  }

  const weatherSamples = {
    深圳: { temp: 23, low: 20, high: 26, condition: "多云", aqi: 42, quality: "优", sunrise: "06:43", sunset: "17:40" },
    北京: { temp: 6, low: 0, high: 12, condition: "晴", aqi: 76, quality: "良", sunrise: "07:12", sunset: "16:50" },
    杭州: { temp: 14, low: 9, high: 17, condition: "阴", aqi: 55, quality: "良", sunrise: "06:34", sunset: "16:59" },
  };
  const weatherAnimations = {
    auto: "跟随城市与时间", sun: "晴天", sunrise: "日出", sunset: "日落", night: "晴夜",
    "partly-cloudy": "多云", "cloudy-night": "夜间多云", cloud: "阴天", rain: "下雨",
    snow: "下雪", thunder: "雷雨", fog: "雾", wind: "大风",
  };

  function weatherScene(state, stamp) {
    const sample = state.network === "empty" ? null : weatherSamples[state.city];
    if (!sample) return "unknown";
    if (state.weatherAnimation !== "auto") return state.weatherAnimation;
    if (sample.condition === "阴") return "cloud";
    const minutes = (time) => Number(time.slice(0, 2)) * 60 + Number(time.slice(3, 5));
    const now = minutes(stamp), rise = minutes(sample.sunrise), set = minutes(sample.sunset);
    if (Math.abs(now - rise) <= 30) return "sunrise";
    if (Math.abs(now - set) <= 30) return "sunset";
    const night = now < rise || now >= set;
    return sample.condition === "晴" ? (night ? "night" : "sun") : (night ? "cloudy-night" : "partly-cloudy");
  }

  function initialState() {
    return {
      selected: today,
      month: today.slice(0, 7),
      preset: "seconds",
      target: "spring",
      formats: ["HH:mm:ss", "M月d日 ddd"],
      rules: builtins.map((rule) => ({ ...rule })),
      city: "深圳",
      weatherAnimation: "auto",
      directWeatherConnection: false,
      network: "online",
      width: "normal",
      theme: "system",
      scrollStart: null,
      dateReminders: [],
      calendar: {
        showLunar: true,
        showAdjustments: true,
        showOutside: false,
        snapMonths: false,
        hour12: false,
        showWeeks: true,
        weekStart: 1,
        weekRange: null,
        showWeather: true,
        cliCategory: "default",
        itemStyle: "tint",
      },
      general: {
        autoStart: false,
        trayIcon: false,
        closeOnBlur: true,
        closeRelated: true,
        rememberPosition: false,
        privacy: false,
        completionSound: true,
        tone: "soft",
        volume: 35,
      },
      items: [
        {
          id: "review",
          kind: "event",
          category: "work",
          title: "周四项目例会",
          date: "2026-11-05",
          time: "10:00",
          repeat: "weekly",
          reminder: "10",
          done: [],
        },
        {
          id: "book",
          kind: "todo",
          category: "personal",
          title: "归还图书馆借书",
          date: today,
          time: "",
          repeat: "none",
          reminder: "off",
          done: [],
        },
      ],
    };
  }
  function occurs(item, date) {
    const offset = daysBetween(item.date, date);
    if (offset < 0) return false;
    return offset === 0 || item.repeat === "daily" || (item.repeat === "weekly" && offset % 7 === 0);
  }
  const itemsOn = (items, date) =>
    items.filter((it) => occurs(it, date)).sort((a, b) => a.time.localeCompare(b.time));
  function nextFestival(rule, from) {
    if (!rule?.enabled) return null;
    for (let key = from; key.slice(0, 7) <= lastMonth; key = plusDays(key, 1)) {
      const i = daysBetween(from, key);
      if (matchesFestival(rule, key)) return i === 0 ? "今天" + rule.name : "距" + rule.name + i + "天";
    }
    return "当前年份范围内暂无日期";
  }

  const presets = [
    ["simple", "公历简约"],
    ["dual", "公农双历"],
    ["seconds", "星期秒钟"],
    ["countdown", "节日倒数"],
    ["festive", "节日节气"],
    ["weather", "当前天气"],
    ["hours", "十二时辰"],
    ["week", "周数日历"],
    ["forecast", "天气详情"],
  ];
  const formatTokens = [
    "yyyy",
    "YYYY",
    "HH",
    "hh",
    "mm",
    "ff",
    "ss",
    "ddd",
    "MM",
    "dd",
    "JJ",
    "tt",
    "M",
    "d",
    "W",
    "h",
  ];
  function partsFor(state, stamp) {
    const [hour, minute, second] = stamp.split(":");
    const l = lunar(today);
    const festivals = festivalsOn(state.rules, today)
      .map((rule) => rule.name)
      .join("、");
    const data = state.network === "empty" ? null : weatherSamples[state.city];
    const term = l.term;
    const date = dateOf(today);
    return {
      hour,
      minute,
      second,
      short: timeLabel(hour + ":" + minute, state.calendar.hour12),
      stamp: timeLabel(stamp, state.calendar.hour12),
      rawStamp: stamp,
      term,
      festivals,
      holiday: [festivals, term].filter(Boolean).join(" "),
      week: "周" + weekday(today),
      iso: weekForDate(today, state.calendar),
      date: displayDate(today),
      numeric: date.getUTCFullYear() + "-" + (date.getUTCMonth() + 1) + "-" + date.getUTCDate(),
      lunar: l.monthName + l.dayName,
      branch: "子丑寅卯辰巳午未申酉戌亥"[Math.floor((Number(hour) + 1) / 2) % 12] + "时",
      data,
      city: state.city,
      weather: data ? data.condition + " " + data.temp + "°" : state.city ? "天气暂不可用" : "请选择城市",
      range: data ? data.low + "～" + data.high + "°" : "",
      air: data ? "美标AQI " + data.aqi : "",
      countdown:
        nextFestival(
          state.rules.find((r) => r.id === state.target),
          today,
        ) || "选择倒数节日",
    };
  }

  function readFormat(input, p) {
    const values = {
      yyyy: today.slice(0, 4),
      YYYY: today.slice(0, 4),
      MM: today.slice(5, 7),
      M: String(+today.slice(5, 7)),
      dd: today.slice(-2),
      d: String(+today.slice(-2)),
      ddd: p.week,
      W: weekday(today),
      HH: p.hour,
      hh: p.hour,
      mm: p.minute,
      ff: p.minute,
      ss: p.second,
      JJ: p.holiday,
      h: String(Number(p.hour) % 12 || 12),
      tt: Number(p.hour) < 12 ? "上午" : "下午",
      农历: p.lunar,
      节日: p.festivals,
      节气: p.term,
      城市: p.city,
      天气: p.weather,
      周数: p.iso === null ? "" : String(p.iso),
      时辰: p.branch,
      倒数: p.countdown,
      温度范围: p.range,
      美标AQI: p.air,
    };
    let output = "";
    for (let i = 0; i < input.length; ) {
      if (input[i] === "{") {
        const end = input.indexOf("}", i + 1),
          name = input.slice(i + 1, end);
        if (end < 0 || !Object.hasOwn(values, name)) throw new Error("请检查花括号中的字段名称");
        output += values[name];
        i = end + 1;
        continue;
      }
      const token = formatTokens.find((t) => input.startsWith(t, i));
      if (token) {
        output += values[token];
        i += token.length;
        continue;
      }
      if (/[A-Za-z{}]/.test(input[i])) throw new Error("无法识别格式字符 “" + input[i] + "”");
      output += input[i++];
    }
    return output.trim();
  }

  function clockLines(id, p, formats) {
    switch (id) {
      case "simple":
        return [p.short + " " + p.week, p.numeric];
      case "dual":
        return [p.short + " " + p.week, p.date + " " + p.lunar];
      case "seconds":
        return [p.stamp, p.date + " " + p.week];
      case "countdown":
        return [p.short + " " + p.week, p.countdown];
      case "festive":
        return [[p.stamp, p.week, p.holiday].filter(Boolean).join(" "), p.date + " " + p.lunar];
      case "weather":
        return [p.short + " " + p.weather, p.date + " " + p.week];
      case "hours":
        return [timeLabel(p.rawStamp, true) + " " + p.branch, p.date + " " + p.week];
      case "week":
        return [p.stamp + (p.iso === null ? "" : " 第" + p.iso + "周"), p.numeric + " " + p.week];
      case "forecast":
        return [
          p.short + " " + p.city + " " + (p.data?.condition || ""),
          [p.range, p.air].filter(Boolean).join(" ") || p.weather,
        ];
      case "custom":
        return formats.map((input) => readFormat(input, p));
      default:
        throw new Error("未知时钟样式");
    }
  }
  return {
    today,
    firstMonth,
    lastMonth,
    two,
    dateOf,
    keyOf,
    plusDays,
    monthOffset,
    weekNumber,
    weeksInView,
    startOfWeek,
    displayWeek,
    weekForDate,
    timeLabel,
    displayDate,
    weekday,
    lunar,
    festivalsOn,
    ruleText,
    dayMark,
    weatherSamples,
    weatherAnimations,
    weatherScene,
    initialState,
    itemsOn,
    occurs,
    presets,
    partsFor,
    clockLines,
    readFormat,
    weekdayNames,
  };
})();
