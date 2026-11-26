const DesktopPreview = (function () {
  let audio;
  async function playTone(tone, volume) {
    if (!window.AudioContext) throw new Error("此浏览器不支持提示音");
    if (!audio) audio = new AudioContext();
    if (audio.state === "suspended") await audio.resume();
    const notes = { soft: [620], chime: [660, 880], clear: [990] }[tone];
    notes.forEach((hz, i) => {
      const at = audio.currentTime + i * 0.12,
        osc = audio.createOscillator(),
        gain = audio.createGain();
      osc.type = "sine";
      osc.frequency.value = hz;
      gain.gain.setValueAtTime(0, at);
      gain.gain.linearRampToValueAtTime((volume / 100) * 0.13, at + 0.012);
      gain.gain.linearRampToValueAtTime(0, at + 0.16);
      osc.connect(gain).connect(audio.destination);
      osc.onended = () => {
        osc.disconnect();
        gain.disconnect();
      };
      osc.start(at);
      osc.stop(at + 0.17);
    });
  }
  function sessionPin() {
    let salt, verifier;
    async function derive(pin, nonce) {
      const raw = new TextEncoder().encode(pin);
      const key = await crypto.subtle.importKey("raw", raw, "PBKDF2", false, ["deriveBits"]);
      raw.fill(0);
      const bits = await crypto.subtle.deriveBits(
        { name: "PBKDF2", salt: nonce, iterations: 120000, hash: "SHA-256" },
        key,
        256,
      );
      return new Uint8Array(bits);
    }
    return {
      async set(pin) {
        const fresh = crypto.getRandomValues(new Uint8Array(16));
        verifier = await derive(pin, fresh);
        salt = fresh;
      },
      async matches(pin) {
        if (!verifier) return false;
        const candidate = await derive(pin, salt);
        let difference = 0;
        for (let i = 0; i < verifier.length; i++) difference |= verifier[i] ^ candidate[i];
        candidate.fill(0);
        return difference === 0;
      },
      clear() {
        verifier?.fill(0);
        verifier = undefined;
        salt = undefined;
      },
    };
  }
  const detailGap = 8;
  function detailWidth(host) {
    return Math.min(340, Math.max(1, Math.floor((host.clientWidth - detailGap) / 2)));
  }
  function setZoom(panel, referenceWidth) {
    const zoom = Math.min(panel.clientWidth / referenceWidth, panel.clientHeight / 618);
    if (zoom > 0) panel.style.setProperty("--calendar-zoom", zoom);
  }
  function place(host, point, size) {
    const panel = host.querySelector(".month-window");
    if (!panel) return;
    const details = host.querySelector(".detail-window");
    const side = details ? detailWidth(host) : 0;
    const reserved = details ? side + detailGap : 0;
    Object.assign(panel.style, {
      width: size ? size.width + "px" : "",
      height: size ? size.height + "px" : "",
      maxWidth: details ? host.clientWidth - reserved + "px" : "",
      position: "",
      left: "",
      top: "",
    });
    if (point || details) {
      const natural = boundsInHost(host, panel);
      const left = Math.max(reserved, Math.min(point?.x ?? natural.left, host.clientWidth - panel.offsetWidth));
      const top = Math.max(0, Math.min(point?.y ?? natural.top, host.clientHeight - panel.offsetHeight));
      panel.style.position = "absolute";
      panel.style.left = left + "px";
      panel.style.top = top + "px";
      if (details) {
        Object.assign(details.style, {
          left: left - reserved + "px",
          top: top + "px",
          width: side + "px",
          height: panel.offsetHeight + "px",
        });
        setZoom(details, 338);
      }
    }
    setZoom(panel, 418);
  }
  function boundsInHost(host, panel) {
    const bounds = panel.getBoundingClientRect(),
      frame = host.getBoundingClientRect();
    return {
      left: bounds.left - frame.left,
      top: bounds.top - frame.top,
      right: bounds.right - frame.left,
      bottom: bounds.bottom - frame.top,
    };
  }
  function bindDrag(host, onChange) {
    let active = null;
    function resize(bounds, edge, dx, dy) {
      let { left, top, right, bottom } = bounds;
      const minWidth = Math.min(340, right - left),
        minHeight = Math.min(450, bottom - top);
      if (edge === "start") {
        const limit = host.querySelector(".detail-window") ? detailWidth(host) + detailGap : 0;
        left = Math.max(limit, Math.min(left + dx, right - minWidth));
        top = Math.max(0, Math.min(top + dy, bottom - minHeight));
      } else {
        right = Math.min(host.clientWidth, Math.max(right + dx, left + minWidth));
        bottom = Math.min(host.clientHeight, Math.max(bottom + dy, top + minHeight));
      }
      onChange({ x: left, y: top }, { width: right - left, height: bottom - top });
    }
    host.addEventListener("pointerdown", (ev) => {
      if (ev.button !== 0 || active) return;
      const grip = ev.target.closest("[data-resize]");
      const handle = grip || ev.target.closest(".month-hero");
      if (!handle || (!grip && ev.target.closest("button,input,select"))) return;
      active = {
        id: ev.pointerId,
        x: ev.clientX,
        y: ev.clientY,
        bounds: boundsInHost(host, handle.closest(".flyout")),
        edge: grip?.dataset.resize,
        moved: false,
      };
      host.setPointerCapture(ev.pointerId);
    });
    host.addEventListener("pointermove", (ev) => {
      if (active?.id !== ev.pointerId) return;
      const dx = ev.clientX - active.x,
        dy = ev.clientY - active.y;
      if (!active.moved && Math.abs(dx) + Math.abs(dy) <= 3) return;
      active.moved = true;
      if (active.edge) resize(active.bounds, active.edge, dx, dy);
      else onChange({ x: active.bounds.left + dx, y: active.bounds.top + dy });
    });
    const end = (ev) => {
      if (active?.id === ev.pointerId) active = null;
    };
    host.addEventListener("pointerup", end);
    host.addEventListener("lostpointercapture", end);
    host.addEventListener("keydown", (ev) => {
      const grip = ev.target.closest("[data-resize]");
      const arrows = { ArrowLeft: [-12, 0], ArrowRight: [12, 0], ArrowUp: [0, -12], ArrowDown: [0, 12] };
      if (!grip || !Object.hasOwn(arrows, ev.key)) return;
      ev.preventDefault();
      resize(boundsInHost(host, grip.closest(".flyout")), grip.dataset.resize, ...arrows[ev.key]);
    });
  }
  return { playTone, sessionPin, place, bindDrag };
})();
