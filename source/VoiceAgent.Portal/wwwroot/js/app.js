// Small browser helpers for the portal: service worker updates, scrolling and the test call audio.
window.voiceAgent = (() => {
    let waitingWorker = null;

    async function registerUpdateNotifier(dotnetRef) {
        if (!('serviceWorker' in navigator)) return;
        const registration = await navigator.serviceWorker.getRegistration();
        if (!registration) return;

        const notify = worker => {
            waitingWorker = worker;
            dotnetRef.invokeMethodAsync('OnUpdateAvailable');
        };

        if (registration.waiting && navigator.serviceWorker.controller) notify(registration.waiting);

        registration.addEventListener('updatefound', () => {
            const worker = registration.installing;
            worker?.addEventListener('statechange', () => {
                if (worker.state === 'installed' && navigator.serviceWorker.controller) notify(worker);
            });
        });

        // Check for a new version every 10 minutes while the portal stays open.
        setInterval(() => registration.update(), 10 * 60 * 1000);
    }

    function applyUpdate() {
        if (!waitingWorker) { location.reload(); return; }
        navigator.serviceWorker.addEventListener('controllerchange', () => location.reload(), { once: true });
        waitingWorker.postMessage({ type: 'SKIP_WAITING' });
    }

    function scrollToBottom(id) {
        const el = document.getElementById(id);
        if (el) el.scrollTop = el.scrollHeight;
    }

    // ---- Browser test call: microphone -> 24 kHz PCM16 -> ACS-style JSON over a WebSocket, and back. ----
    const SAMPLE_RATE = 24000;
    const workletSource = `
        class Capture extends AudioWorkletProcessor {
            constructor() { super(); this.buffer = []; }
            process(inputs) {
                const input = inputs[0][0];
                if (input) {
                    for (let i = 0; i < input.length; i++) this.buffer.push(input[i]);
                    if (this.buffer.length >= 480) { // 20 ms at 24 kHz
                        this.port.postMessage(new Float32Array(this.buffer));
                        this.buffer = [];
                    }
                }
                return true;
            }
        }
        registerProcessor('capture', Capture);`;

    let call = null;

    function toBase64Pcm16(floats) {
        const bytes = new Uint8Array(floats.length * 2);
        const view = new DataView(bytes.buffer);
        for (let i = 0; i < floats.length; i++) {
            const s = Math.max(-1, Math.min(1, floats[i]));
            view.setInt16(i * 2, s < 0 ? s * 0x8000 : s * 0x7fff, true);
        }
        let binary = '';
        for (let i = 0; i < bytes.length; i++) binary += String.fromCharCode(bytes[i]);
        return btoa(binary);
    }

    function fromBase64Pcm16(b64) {
        const binary = atob(b64);
        const view = new DataView(new ArrayBuffer(binary.length));
        for (let i = 0; i < binary.length; i++) view.setUint8(i, binary.charCodeAt(i));
        const floats = new Float32Array(binary.length / 2);
        for (let i = 0; i < floats.length; i++) floats[i] = view.getInt16(i * 2, true) / 0x8000;
        return floats;
    }

    function play(floats) {
        const ctx = call.context;
        const buffer = ctx.createBuffer(1, floats.length, SAMPLE_RATE);
        buffer.copyToChannel(floats, 0);
        const source = ctx.createBufferSource();
        source.buffer = buffer;
        source.connect(ctx.destination);
        const startAt = Math.max(ctx.currentTime, call.playhead);
        source.start(startAt);
        call.playhead = startAt + buffer.duration;
        call.sources.add(source);
        source.onended = () => call?.sources.delete(source);
    }

    function stopPlayback() {
        if (!call) return;
        for (const s of call.sources) { try { s.stop(); } catch { } }
        call.sources.clear();
        call.playhead = call.context.currentTime;
    }

    async function start(dotnetRef, wsUrl) {
        await stop();
        const stream = await navigator.mediaDevices.getUserMedia({
            audio: { echoCancellation: true, noiseSuppression: true, channelCount: 1 },
        });
        const context = new AudioContext({ sampleRate: SAMPLE_RATE });
        const moduleUrl = URL.createObjectURL(new Blob([workletSource], { type: 'application/javascript' }));
        await context.audioWorklet.addModule(moduleUrl);

        const socket = new WebSocket(wsUrl);
        const source = context.createMediaStreamSource(stream);
        const capture = new AudioWorkletNode(context, 'capture');
        call = { dotnetRef, stream, context, socket, source, capture, playhead: 0, sources: new Set(), ended: false };

        capture.port.onmessage = e => {
            if (socket.readyState === WebSocket.OPEN) {
                socket.send(JSON.stringify({ kind: 'AudioData', audioData: { data: toBase64Pcm16(e.data), silent: false } }));
            }
        };
        source.connect(capture);

        socket.onmessage = e => {
            const msg = JSON.parse(e.data);
            // The server uses the ACS SDK's outbound format ("Kind", "AudioData", "Data"); accept either casing.
            const kind = msg.kind ?? msg.Kind;
            const audio = msg.audioData ?? msg.AudioData;
            const data = audio?.data ?? audio?.Data;
            if (kind === 'TestCallStarted') dotnetRef.invokeMethodAsync('OnCallStarted', msg.callId);
            else if (kind === 'AudioData' && data) play(fromBase64Pcm16(data));
            else if (kind === 'StopAudio') stopPlayback();
        };
        socket.onclose = () => finish(null);
        socket.onerror = () => finish('The connection to the server failed.');
    }

    async function finish(error) {
        if (!call || call.ended) return;
        const c = call;
        c.ended = true;
        call = null;
        try { c.source.disconnect(); c.capture.disconnect(); } catch { }
        c.stream.getTracks().forEach(t => t.stop());
        try { await c.context.close(); } catch { }
        if (c.socket.readyState === WebSocket.OPEN) c.socket.close();
        c.dotnetRef.invokeMethodAsync('OnCallEnded', error);
    }

    async function stop() { await finish(null); }

    return { registerUpdateNotifier, applyUpdate, scrollToBottom, testCall: { start, stop } };
})();
