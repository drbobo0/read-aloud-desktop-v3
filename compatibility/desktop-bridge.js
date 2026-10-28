// Completion adapter for the separate Read Aloud copy. The original reader is unchanged.
(() => {
  const BRIDGE_ID = 'jglajehjfkcdfjinbeepbjihpgdfnefg';
  const failureMessage = 'Read Aloud could not finish the passage. Open the compatible reader for details.';
  const validSession = id => typeof id === 'string' && /^[a-zA-Z0-9_-]{1,100}$/.test(id);
  const relay = message => brapi.runtime.sendMessage(BRIDGE_ID, message).catch(() => {});

  brapi.runtime.onMessageExternal.addListener((request, sender, reply) => {
    if (sender.id !== BRIDGE_ID || !request) return;
    if (request.method === 'desktopHello') {
      reply({ ok: true, protocol: 1, completionEvents: true });
    } else if (request.method === 'desktopPlay') {
      if (!validSession(request.sessionId) || typeof request.text !== 'string' || !request.text.trim()
          || new TextEncoder().encode(request.text).length > 500000) {
        reply({ ok: false, message: 'Choose a valid passage to read.' }); return;
      }
      const sessionId = request.sessionId;
      // The player reports this token from its document-end callback, across every voice engine.
      Promise.resolve().then(() => playText(request.text, { desktopSessionId: sessionId }))
        .catch(() => relay({ type: 'readerFailed', sessionId, message: failureMessage }));
      reply({ ok: true, protocol: 1, sessionId });
    }
  });

  brapi.runtime.onMessage.addListener((request, sender, reply) => {
    if (!request || request.dest !== 'desktopBridge' || request.method !== 'ended') return;
    if (sender.id !== brapi.runtime.id || !validSession(request.sessionId)) return;
    const message = request.error
      ? { type: 'readerFailed', sessionId: request.sessionId, message: failureMessage }
      : { type: 'readerCompleted', sessionId: request.sessionId };
    relay(message).then(() => reply({ ok: true }));
    return true;
  });
})();
