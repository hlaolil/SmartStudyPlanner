// Small browser helpers used by SmartStudyPlanner through JS interop.
window.ssp = (() => {
    let audioCtx = null;

    function tone(freq, start, duration, ctx) {
        const osc = ctx.createOscillator();
        const gain = ctx.createGain();
        osc.type = "sine";
        osc.frequency.value = freq;
        gain.gain.setValueAtTime(0.0001, ctx.currentTime + start);
        gain.gain.exponentialRampToValueAtTime(0.25, ctx.currentTime + start + 0.02);
        gain.gain.exponentialRampToValueAtTime(0.0001, ctx.currentTime + start + duration);
        osc.connect(gain).connect(ctx.destination);
        osc.start(ctx.currentTime + start);
        osc.stop(ctx.currentTime + start + duration + 0.05);
    }

    return {
        tzOffset: () => new Date().getTimezoneOffset(),
        load: key => {
            try { return localStorage.getItem(key); } catch { return null; }
        },
        save: (key, value) => { localStorage.setItem(key, value); },
        setTheme: theme => {
            try { localStorage.setItem("ssp.theme", theme); } catch { }
            applyTheme(theme);
        },
        getTheme: () => {
            try { return localStorage.getItem("ssp.theme") || "auto"; } catch { return "auto"; }
        },
        download: (filename, mime, content) => {
            const blob = new Blob([content], { type: mime });
            const url = URL.createObjectURL(blob);
            const a = document.createElement("a");
            a.href = url;
            a.download = filename;
            document.body.appendChild(a);
            a.click();
            a.remove();
            setTimeout(() => URL.revokeObjectURL(url), 1000);
        },
        chime: () => {
            try {
                audioCtx = audioCtx || new (window.AudioContext || window.webkitAudioContext)();
                tone(660, 0, 0.35, audioCtx);
                tone(880, 0.25, 0.35, audioCtx);
                tone(1320, 0.5, 0.6, audioCtx);
            } catch { }
        },
        unlockAudio: () => {
            try {
                audioCtx = audioCtx || new (window.AudioContext || window.webkitAudioContext)();
                if (audioCtx.state === "suspended") audioCtx.resume();
            } catch { }
        },
        requestNotifications: async () => {
            if (!("Notification" in window)) return "unsupported";
            if (Notification.permission === "granted") return "granted";
            return await Notification.requestPermission();
        },
        notify: (title, body) => {
            try {
                if ("Notification" in window && Notification.permission === "granted" && document.hidden) {
                    new Notification(title, { body, icon: "favicon.png" });
                }
            } catch { }
        },
        setTimerTitle: prefix => {
            const base = document.title.replace(/^(⏸ )?\d+:\d\d · /, "");
            document.title = prefix ? prefix + " · " + base : base;
        },
        confirm: message => window.confirm(message),
        focusModal: () => {
            const cards = document.querySelectorAll(".ssp-modal-card");
            const card = cards[cards.length - 1];
            if (!card || card.contains(document.activeElement)) return;
            const target = card.querySelector(".ssp-modal-body input:not([type=checkbox]):not([type=range]), .ssp-modal-body textarea, .ssp-modal-body select") || card;
            target.focus();
        },
        focus: selector => { const el = document.querySelector(selector); if (el) el.focus(); }
    };
})();

function applyTheme(theme) {
    const dark = theme === "dark" || (theme === "auto" && window.matchMedia("(prefers-color-scheme: dark)").matches);
    document.documentElement.setAttribute("data-bs-theme", dark ? "dark" : "light");
}
