/* 本地线形图标。只接受开发者维护的名称。 */
const Icons = (() => {
  const shapes = {
    capture: '<path d="M8 4H4v4m12-4h4v4M4 16v4h4m12-4v4h-4"/><rect x="7" y="8" width="10" height="8" rx="1"/>',
    calendar:
      '<rect x="3.5" y="5" width="17" height="15.5" rx="3"/><path d="M7.5 3v4m9-4v4M4 10h16m-12 4h2m4 0h2m-8 3h2"/>',
    clock: '<circle cx="12" cy="12" r="8.7"/><path d="M12 6.5v5.8l3.8 2.4"/>',
    day: '<rect x="4" y="4.5" width="16" height="16" rx="3"/><path d="M7.5 3v3m9-3v3M4 9h16m-12 4h6m-6 3.5h4"/>',
    edit: '<path d="m14.5 5.8 3.7 3.7m-12 9 3.8-.8L20 7.6a1.4 1.4 0 0 0 0-2l-1.6-1.7a1.4 1.4 0 0 0-2 0L6.3 14l-.9 4.5Z"/>',
    sliders:
      '<path d="M4 6.5h8m4 0h4M4 17.5h3m4 0h9"/><circle cx="14" cy="6.5" r="2.5"/><circle cx="9" cy="17.5" r="2.5"/>',
    flag: '<path d="M5.5 21V4.5c5-3 8 3 13 0v10c-5 3-8-3-13 0"/>',
    left: '<path d="m14.5 5.5-6.5 6.5 6.5 6.5"/>',
    right: '<path d="m9.5 5.5 6.5 6.5-6.5 6.5"/>',
    back: '<path d="m10 5-7 7 7 7M3 12h17"/>',
    down: '<path d="m6 9 6 6 6-6"/>',
    up: '<path d="m6 15 6-6 6 6"/>',
    close: '<path d="m6 6 12 12m0-12L6 18"/>',
    plus: '<path d="M12 5v14M5 12h14"/>',
    check: '<path d="m4.5 12.5 4.8 4.8L19.5 7"/>',
    settings:
      '<path d="m9.5 3-.8 2.2-2 .9-2.2-.5-1.8 3 1.5 1.7-.1 2.4-1.4 1.7 1.8 3 2.2-.4 2 .9.8 2.1h3.5l.8-2.1 2-.9 2.2.4 1.7-3-1.4-1.7v-2.4l1.4-1.7-1.7-3-2.2.5-2-.9L13 3z"/><circle cx="11.3" cy="11.7" r="3"/>',
    sun: '<circle cx="12" cy="12" r="4"/><path d="M12 2.5V5m0 14v2.5M2.5 12H5m14 0h2.5M5.2 5.2 7 7m10 10 1.8 1.8M5.2 18.8 7 17M17 7l1.8-1.8"/>',
    cloud:
      '<path d="M6.8 18.3h10.1a4.4 4.4 0 0 0 .2-8.8 6.1 6.1 0 0 0-11.8 1.4 3.8 3.8 0 0 0 1.5 7.4Z"/>',
    partly:
      '<g stroke="#bb790b" fill="#ffce65"><circle cx="15.4" cy="8.4" r="3.8"/><path d="M15.4 1v1.8M22.7 8.4h-1.6M20.7 3.1l-1.2 1.2M10.2 3.2l1.2 1.2"/></g><path d="M5.5 19h11a3.6 3.6 0 1 0-.6-7.1 5.5 5.5 0 0 0-10.4-.6 3.9 3.9 0 0 0 0 7.7Z" fill="#eaf4fd" stroke="#6d8fa9"/>',
    pin: '<path d="M19 10c0 4-7 11-7 11s-7-7-7-11a7 7 0 0 1 14 0Z"/><circle cx="12" cy="10" r="2"/>',
    pushpin: '<path d="m14 3 7 7-3 1-4 4v3l-2 1-3-3-5 5-1-1 5-5-3-3 1-2h3l4-4z"/>',
    bell: '<path d="M5 17h14c-1.4-1.8-2-3.2-2-6a5 5 0 0 0-10 0c0 2.8-.7 4.2-2 6Zm5 2a2 2 0 0 0 4 0"/>',
    monitor: '<rect x="3" y="4" width="18" height="13" rx="2"/><path d="M8 21h8m-4-4v4"/>',
    wifi: '<path d="M2.5 8a15 15 0 0 1 19 0M6 12a9.7 9.7 0 0 1 12 0m-9 4a4.8 4.8 0 0 1 6 0"/><circle cx="12" cy="19" r=".8"/>',
    sound: '<path d="M3.5 9h4l5-4v14l-5-4h-4zm12-1c2.5 2 2.5 6 0 8m2.5-10.5c4 3.3 4 9.7 0 13"/>',
    battery:
      '<rect x="2" y="7" width="17.5" height="10" rx="2"/><path d="M22 11v2M5 10v4m3-4v4m3-4v4m3-4v4"/>',
    windows:
      '<g fill="currentColor" stroke="none"><path d="M3 3h8v8H3zm10 0h8v8h-8zM3 13h8v8H3zm10 0h8v8h-8z"/></g>',
    folder:
      '<path d="M2.5 7h8l2 2h9v9a2 2 0 0 1-2 2h-15a2 2 0 0 1-2-2Z" fill="#f6c75e" stroke="#b78c38"/><path d="M3 7V5.5h7l2 2h8V9"/>',
    search: '<circle cx="10.5" cy="10.5" r="6"/><path d="m15 15 5.5 5.5"/>',
    moon: '<path d="M20.5 15.5A8.5 8.5 0 0 1 8.4 3.8a8.8 8.8 0 1 0 12.1 11.7Z"/>',
    refresh: '<path d="M20 10a8 8 0 1 0-1 7M20 4v6h-6"/>',
    list: '<path d="M9 6h11M9 12h11M9 18h11m-17-6 1.3 1.3L6 10m-3.8 8 1.2 1.2 2.6-3"/>',
    info: '<circle cx="12" cy="12" r="9"/><path d="M12 10.5v6m0-9.5v.1"/>',
    task: '<rect x="4" y="4" width="16" height="16" rx="4"/><path d="m8 12 2.6 2.8 5.3-5.6"/>',
    globe:
      '<circle cx="12" cy="12" r="9"/><ellipse cx="12" cy="12" rx="4" ry="9"/><path d="M3 12h18"/>',
    trash: '<path d="M4 6.5h16m-13 0 1 13h8l1-13M9.5 6.5V4h5v2.5m-4.5 4v5m3-5v5"/>',
  };
  function icon(name, size = 18) {
    return (
      '<svg aria-hidden="true" focusable="false" width="' +
      size +
      '" height="' +
      size +
      '" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.65" stroke-linecap="round" stroke-linejoin="round">' +
      shapes[name] +
      "</svg>"
    );
  }
  return { icon };
})();
