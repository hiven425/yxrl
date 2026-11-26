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
  function place(host, point) {
    const panel = host.querySelector(".month-window,.detail-window");
    if (!point || !panel) return;
    const left = Math.max(0, Math.min(point.x, host.clientWidth - panel.offsetWidth));
    const top = Math.max(0, Math.min(point.y, host.clientHeight - panel.offsetHeight));
    Object.assign(panel.style, { position: "absolute", left: left + "px", top: top + "px" });
  }
  function bindDrag(host, onMove) {
    let active = null;
    host.addEventListener("pointerdown", (ev) => {
      const handle = ev.target.closest(".month-hero,.detail-window .panel-head");
      if (!handle || ev.target.closest("button,input,select") || ev.button !== 0) return;
      const panel = handle.closest(".flyout").getBoundingClientRect(),
        frame = host.getBoundingClientRect();
      active = {
        id: ev.pointerId,
        x: ev.clientX,
        y: ev.clientY,
        left: panel.left - frame.left,
        top: panel.top - frame.top,
      };
      host.setPointerCapture(ev.pointerId);
    });
    host.addEventListener("pointermove", (ev) => {
      if (active?.id !== ev.pointerId) return;
      const dx = ev.clientX - active.x,
        dy = ev.clientY - active.y;
      if (Math.abs(dx) + Math.abs(dy) > 3) onMove({ x: active.left + dx, y: active.top + dy });
    });
    const end = (ev) => {
      if (active?.id === ev.pointerId) active = null;
    };
    host.addEventListener("pointerup", end);
    host.addEventListener("lostpointercapture", end);
  }
  return { playTone, sessionPin, place, bindDrag };
})();
