const [, , wsUrl, timeoutArg] = process.argv;

if (!wsUrl) {
  console.error("Usage: node scripts/assert-renderer-ready.mjs <webSocketDebuggerUrl> [timeoutMs]");
  process.exit(1);
}

const timeoutMs = Number.parseInt(timeoutArg ?? "20000", 10);
const ws = new WebSocket(wsUrl);
const pending = new Map();
let nextId = 1;

function send(method, params = {}) {
  return new Promise((resolve, reject) => {
    const id = nextId++;
    pending.set(id, { resolve, reject });
    ws.send(JSON.stringify({ id, method, params }));
  });
}

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

async function readRendererState() {
  const evaluation = await send("Runtime.evaluate", {
    expression: `(() => {
      const hasError = Boolean(document.querySelector('.hero-card--error'));
      const text = document.body.innerText.replace(/\\s+/g, ' ').trim();
      return JSON.stringify({ title: document.title, hasError, text });
    })()`,
    returnByValue: true,
  });

  return JSON.parse(evaluation.result.value);
}

ws.onmessage = (event) => {
  const message = JSON.parse(event.data);
  if (!message.id) {
    return;
  }

  const pendingEntry = pending.get(message.id);
  if (!pendingEntry) {
    return;
  }

  pending.delete(message.id);
  if (message.error) {
    pendingEntry.reject(new Error(message.error.message));
    return;
  }

  pendingEntry.resolve(message.result);
};

ws.onerror = () => {
  console.error("WebSocket error while inspecting renderer state.");
  process.exit(1);
};

ws.onopen = async () => {
  try {
    await send("Runtime.enable");

    const deadline = Date.now() + timeoutMs;
    let payload = null;
    while (Date.now() < deadline) {
      payload = await readRendererState();
      if (payload.hasError) {
        throw new Error(`Renderer error page detected: ${payload.text}`);
      }

      if (payload.text.includes("CPA Runtime") && payload.text.includes("Codex")) {
        console.log(JSON.stringify(payload));
        ws.close();
        return;
      }

      await sleep(500);
    }

    throw new Error(
      `Renderer never reached ready state. Last content: ${payload ? payload.text : "(none)"}`,
    );
  } catch (error) {
    console.error(error.stack || String(error));
    process.exitCode = 1;
    ws.close();
  }
};

ws.onclose = () => {
  process.exit(process.exitCode || 0);
};
