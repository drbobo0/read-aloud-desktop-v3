importScripts('config.js', 'cleanup.js', 'reader-config.js');
const TARGET = 'hdhinadidafjejdhmfkjgnolgimiaplp';
const HOST = 'local.read_aloud_desktop_v3';
let readerTarget = TARGET;
let completionSupported = false;
let activeReading;
let readingSerial = 0;
let settings = BridgeConfig.normalize(BridgeConfig.defaults);
let nativePort;
let transportReady = false;
let showWhenReady = false;
let lastNotice = { message: '', time: 0 };
let serial = 0;
const pending = new Map();
let state = { connected: false, enabled: false, hasPassage: false, playback: 'stopped', message: 'Enable V3 after disabling V2’s desktop shortcuts.' };

function postNative(message) {
  if (!nativePort || !transportReady) return false;
  try { nativePort.postMessage(message); return true; } catch { return false; }
}
function update(values) {
  state = { ...state, ...values };
  // The passage itself stays in memory; only settings and compact status are persisted.
  chrome.storage.local.set({ status: state });
  chrome.action.setBadgeText({ text: state.connected ? 'V3' : 'OFF' });
  chrome.action.setBadgeBackgroundColor({ color: state.connected ? '#6864ff' : '#6b7280' });
  postNative({ type: 'status', message: state.message, playback: state.playback });
}
function notify(message, force = false) {
  if (!settings.notifications && !force) return;
  const now = Date.now();
  if (lastNotice.message === message && now - lastNotice.time < 4000) return;
  lastNotice = { message, time: now };
  chrome.notifications.create('read-aloud-desktop-v3', {
    type: 'basic', iconUrl: 'icon128.png', title: 'Read Aloud Desktop V3', message, silent: true
  }, () => { void chrome.runtime.lastError; });
}
function problem(message) { update({ message }); notify(message); }
async function chooseReader() {
  const supported = await new Promise(resolve => {
    chrome.runtime.sendMessage(CompatibleReaderId, { method: 'desktopHello' }, result => {
      const error = chrome.runtime.lastError;
      resolve(!error && result?.ok === true && result.protocol === 1 && result.completionEvents === true);
    });
  });
  readerTarget = supported ? CompatibleReaderId : TARGET;
  completionSupported = supported;
  return supported;
}
chrome.runtime.onMessageExternal.addListener((message, sender, reply) => {
  if (sender.id !== CompatibleReaderId || !message || !activeReading || message.sessionId !== activeReading.id) return;
  if (!['readerCompleted', 'readerFailed'].includes(message.type)) return;
  // Hold an end event that arrives before the play acceptance callback.
  if (!activeReading.accepted) activeReading.pendingEnd = message;
  else finishReading(message);
  reply({ ok: true });
});
function finishReading(message) {
  if (!activeReading || activeReading.id !== message.sessionId) return;
  activeReading = undefined;
  if (message.type === 'readerFailed') {
    update({ playback: 'stopped' }); problem(message.message || 'Reading failed.'); return;
  }
  update({ playback: 'stopped', message: 'Reading finished.' });
  postNative({ type: 'finished', sessionId: message.sessionId });
}
function sendToReadAloud(command) {
  return sendPlayback(command);
}
async function sendPlayback(command) {
  let session;
  if (command.method === 'play') {
    activeReading = undefined;
    await chooseReader();
    if (completionSupported) {
      session = { id: Date.now().toString(36) + '_' + (++readingSerial), accepted: false };
      activeReading = session;
      postNative({ type: 'playbackSession', sessionId: session.id });
    }
  } else if (command.method === 'stop') activeReading = undefined;
  return new Promise(resolve => {
    const message = { method: command.method };
    if (message.method === 'play') {
      message.text = command.text;
      if (session) { message.method = 'desktopPlay'; message.sessionId = session.id; }
    }
    chrome.runtime.sendMessage(readerTarget, message, result => {
      const error = chrome.runtime.lastError;
      if ((error && (session || !/message port closed|channel closed/i.test(error.message))) || (session && (result?.ok !== true || result.sessionId !== session.id))) {
        if (activeReading === session) activeReading = undefined;
        update({ playback: 'stopped' });
        problem('Could not reach Read Aloud. Keep the reader enabled in this Chrome profile. ' + (error?.message || result?.message || 'The compatible reader did not accept the passage.'));
        resolve(false);
      } else {
        const playback = { play: 'playing', pause: 'paused', resume: 'playing', stop: 'stopped' }[command.method];
        const message = { play: 'Text sent to Read Aloud.', pause: 'Pause sent to Read Aloud.', resume: 'Resume sent to Read Aloud.', stop: 'Stop sent to Read Aloud.' }[command.method];
        update({ playback, message, completionSupported });
        if (session && activeReading === session) {
          session.accepted = true;
          if (session.pendingEnd) finishReading(session.pendingEnd);
        }
        resolve(true);
      }
    });
  });
}
let commandQueue = Promise.resolve();
function command(input) {
  commandQueue = commandQueue.then(() => handleCommand(input)).catch(error => problem(error.message));
  return commandQueue;
}
async function handleCommand(input) {
  if (input.method === 'disable') { await disable(); return; }
  if (input.method === 'setPreference') {
    const allowed = ['playerSize', 'placement', 'fadeWhenIdle', 'autoHide', 'autoHideSeconds', 'positionLocked', 'cleanupUrls', 'cleanupSymbols', 'cleanupCode'];
    if (!allowed.includes(input.name)) { problem('Unknown preference.'); return; }
    const candidate = { ...settings, [input.name]: input.value };
    if (input.name === 'placement') candidate.customPosition = null;
    const result = await saveSettings(candidate);
    if (!result.ok) problem(result.message);
    return;
  }
  if (input.method === 'setPlacement') {
    const result = await saveSettings({ ...settings, placement: input.text, customPosition: null });
    if (!result.ok) problem(result.message);
    return;
  }
  if (input.method === 'settings') {
    if (!state.hasPassage || state.playback === 'stopped') await chooseReader();
    await chrome.tabs.create({ url: `chrome://extensions/?id=${readerTarget}` });
    problem('Open Read Aloud’s Extension options to change its voice or speed.');
    return;
  }
  if (input.method === 'togglePause') {
    if (!state.hasPassage) { problem('Read a passage first: select text and press ' + settings.read.label + '.'); return; }
    if (state.playback === 'stopped') { postNative({ type: 'restart' }); return; }
    return sendToReadAloud({ method: state.playback === 'paused' ? 'resume' : 'pause' });
  }
  if (!['play', 'pause', 'resume', 'stop'].includes(input.method)) return;
  if (input.method === 'play') {
    if (typeof input.text !== 'string' || !input.text.trim()) { problem('Highlight some text before reading.'); return; }
    if (new TextEncoder().encode(input.text).length > 500000) { problem('Select a shorter passage (under 500 KB).'); return; }
    if (input.newSession) {
      input = { ...input, text: TextCleanup.clean(input.text, settings) };
      if (!input.text.trim()) { problem('Text cleanup removed this entire selection. Turn off a cleanup option or select other text.'); return; }
      const sent = postNative({ type: 'session', text: input.text, show: settings.showOnRead });
      update({ hasPassage: sent });
    }
  }
  return sendToReadAloud(input);
}
function configure(candidate) {
  return new Promise(resolve => {
    if (!transportReady) { resolve({ ok: false, message: 'The Windows helper is not connected. Click Enable / retry.' }); return; }
    const requestId = String(++serial);
    const timer = setTimeout(() => {
      pending.delete(requestId);
      resolve({ ok: false, message: 'The Windows helper did not respond. Disable V3, then enable it again.' });
    }, 5000);
    pending.set(requestId, { resolve, timer });
    if (!postNative({ type: 'configure', settings: candidate, requestId })) {
      clearTimeout(timer); pending.delete(requestId);
      resolve({ ok: false, message: 'The Windows helper disconnected. Click Enable / retry.' });
    }
  });
}
async function connect() {
  if (nativePort) {
    if (transportReady) {
      const result = await configure(settings);
      if (!result.ok) problem(result.message);
    }
    return;
  }
  update({ connected: false, message: 'Connecting to the V3 Windows helper…' });
  const port = chrome.runtime.connectNative(HOST);
  nativePort = port;
  port.onMessage.addListener(async message => {
    if (nativePort !== port) return;
    if (message.type === 'hello') {
      transportReady = true;
      update({ hasPassage: false, playback: 'stopped' });
      const result = await configure(settings);
      if (!result.ok) problem(result.message);
      if (showWhenReady) { showWhenReady = false; postNative({ type: 'player' }); }
    } else if (message.type === 'configured') {
      update({ connected: Boolean(message.active), message: message.message });
      const request = pending.get(message.requestId);
      if (request) { clearTimeout(request.timer); pending.delete(message.requestId); request.resolve(message); }
    } else if (message.type === 'position') {
      if (settings.positionLocked) return;
      try {
        settings = BridgeConfig.normalize({ ...settings, customPosition: { x: message.x, y: message.y, physical: message.physical === true } });
        await chrome.storage.local.set({ settings });
        update({ message: 'Player position saved. Drag the Read label to move it again.' });
      } catch (error) { problem(error.message); }
    } else if (message.type === 'preference') command({ ...message, method: 'setPreference' });
    else if (message.type === 'command') command(message);
    else if (message.type === 'error' || message.type === 'warning') problem(message.message);
  });
  port.onDisconnect.addListener(() => {
    const error = chrome.runtime.lastError;
    if (nativePort !== port) return;
    nativePort = undefined; transportReady = false;
    activeReading = undefined;
    for (const request of pending.values()) { clearTimeout(request.timer); request.resolve({ ok: false, message: 'The Windows helper disconnected.' }); }
    pending.clear();
    const message = error ? error.message + ' Run V3’s Install.cmd, then click Enable / retry.' : 'V3 desktop shortcuts disconnected. Click Enable / retry.';
    update({ connected: false, hasPassage: false, playback: 'stopped', message });
    if (state.enabled) notify(message);
  });
}
async function saveSettings(candidate) {
  const clean = BridgeConfig.normalize(candidate);
  let applied = clean;
  if (nativePort && transportReady) {
    const result = await configure(clean);
    if (!result.ok) { problem(result.message); return result; }
    applied = BridgeConfig.normalize(result.settings || clean);
  } else if (state.enabled) {
    return { ok: false, message: 'Connect the Windows helper before changing active shortcuts, or disable V3 first.' };
  }
  settings = applied;
  await chrome.storage.local.set({ settings });
  update({ message: state.connected ? 'Preferences saved.' : 'Preferences saved. Enable V3 to activate the shortcuts.' });
  return { ok: true, settings, message: state.message };
}
async function disable() {
  activeReading = undefined;
  state.enabled = false; await chrome.storage.local.set({ enabled: false });
  if (state.hasPassage) await sendToReadAloud({ method: 'stop' });
  const oldPort = nativePort; nativePort = undefined; transportReady = false;
  if (oldPort) oldPort.disconnect();
  for (const request of pending.values()) { clearTimeout(request.timer); request.resolve({ ok: false, message: 'V3 is disabled.' }); }
  pending.clear();
  update({ connected: false, hasPassage: false, playback: 'stopped', message: 'V3 disabled. Its shortcuts have been released.' });
}
const boot = chrome.storage.local.get(['settings', 'enabled']).then(saved => {
  try { settings = BridgeConfig.normalize(saved.settings || BridgeConfig.defaults); } catch { settings = BridgeConfig.normalize(BridgeConfig.defaults); }
  state.enabled = saved.enabled === true;
});
chrome.runtime.onStartup.addListener(() => boot.then(() => { if (state.enabled) connect(); }));
chrome.runtime.onMessage.addListener((message, sender, reply) => {
  if (sender.id !== chrome.runtime.id) return;
  boot.then(async () => {
    if (message.type === 'getState') return { ...state, settings };
    if (message.type === 'enable') {
      state.enabled = true; await chrome.storage.local.set({ enabled: true }); await connect();
    } else if (message.type === 'disable') {
      await command({ method: 'disable' });
    } else if (message.type === 'saveSettings') return saveSettings(message.settings);
    else if (message.type === 'test') await command({ method: 'play', text: 'Read Aloud Desktop version three is connected. You can pause, resume, and choose where to start.', newSession: true });
    else if (message.type === 'pause') await command({ method: 'togglePause' });
    else if (message.type === 'stop') await command({ method: 'stop' });
    else if (message.type === 'voices') await command({ method: 'settings' });
    else if (message.type === 'player') {
      if (!postNative({ type: 'player' })) {
        if (state.enabled) { showWhenReady = true; await connect(); }
        else problem('Enable V3 before opening the floating player.');
      }
    }
    return { ...state, settings };
  }).then(reply, error => reply({ ok: false, message: error.message }));
  return true;
});
boot.then(() => { if (state.enabled) connect(); });
