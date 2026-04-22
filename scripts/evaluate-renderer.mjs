const [, , wsUrl, expression, timeoutArg] = process.argv;

if (!wsUrl || !expression) {
  console.error(
    "Usage: node scripts/evaluate-renderer.mjs <webSocketDebuggerUrl> <expression> [timeoutMs]",
  );
  process.exit(1);
}

const timeoutMs = Number.parseInt(timeoutArg ?? "30000", 10);
const ws = new WebSocket(wsUrl);
const pending = new Map();
let nextId = 1;
let timeoutHandle = null;

function send(method, params = {}) {
  return new Promise((resolve, reject) => {
    const id = nextId++;
    pending.set(id, { resolve, reject });
    ws.send(JSON.stringify({ id, method, params }));
  });
}

function cleanupAndExit(code) {
  if (timeoutHandle) {
    clearTimeout(timeoutHandle);
    timeoutHandle = null;
  }
  process.exit(code);
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
  console.error("WebSocket error while evaluating renderer expression.");
  cleanupAndExit(1);
};

ws.onopen = async () => {
  timeoutHandle = setTimeout(() => {
    console.error(`Timed out after ${timeoutMs}ms while evaluating renderer expression.`);
    try {
      ws.close();
    } finally {
      cleanupAndExit(1);
    }
  }, timeoutMs);

  try {
    await send("Runtime.enable");
    const evaluation = await send("Runtime.evaluate", {
      expression,
      awaitPromise: true,
      returnByValue: true,
    });

    if (evaluation.exceptionDetails) {
      throw new Error(evaluation.exceptionDetails.text || "Renderer expression threw.");
    }

    const value = evaluation.result.value;
    if (typeof value === "string") {
      console.log(value);
    } else if (typeof value !== "undefined") {
      console.log(JSON.stringify(value));
    }

    ws.close();
  } catch (error) {
    console.error(error.stack || String(error));
    process.exitCode = 1;
    ws.close();
  }
};

ws.onclose = () => {
  cleanupAndExit(process.exitCode || 0);
};
