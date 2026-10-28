const fs = require('fs');
const path = require('path');
const vm = require('vm');
const assert = require('assert/strict');
const base = path.resolve(__dirname, '../extension');
const listeners = {}, ports = [], sent = [], nativeSent = [], notices = [], opened = [];
const saved = {};
let deliveryError;
let rejectConfig = false;
const chrome = {
  runtime: {
    id: 'v3-companion', lastError: undefined,
    onStartup: { addListener(fn) { listeners.startup = fn; } },
    onMessage: { addListener(fn) { listeners.message = fn; } },
    onMessageExternal: { addListener(fn) { listeners.external = fn; } },
    connectNative(name) {
      assert.equal(name, 'local.read_aloud_desktop_v3');
      const port = {
        onMessage: { addListener(fn) { port.message = fn; } },
        onDisconnect: { addListener(fn) { port.closed = fn; } },
        disconnect() { port.disconnected = true; },
        postMessage(message) {
          nativeSent.push(message);
          if (message.type === 'configure') queueMicrotask(() => port.message({
            type: 'configured', requestId: message.requestId, ok: !rejectConfig, active: true,
            settings: message.settings, message: rejectConfig ? 'Shortcut is already in use. Your previous shortcuts are still active.' : 'Desktop shortcuts are ready.'
          }));
        }
      };
      ports.push(port); return port;
    },
    sendMessage(id, message, callback) {
      if (id !== 'hdhinadidafjejdhmfkjgnolgimiaplp') {
        chrome.runtime.lastError = { message: 'Compatible reader is not installed.' }; callback(); chrome.runtime.lastError = undefined; return;
      }
      assert.equal(id, 'hdhinadidafjejdhmfkjgnolgimiaplp');
      sent.push(JSON.parse(JSON.stringify(message)));
      chrome.runtime.lastError = deliveryError; callback(); chrome.runtime.lastError = undefined;
    }
  },
  storage: { local: {
    async get(keys) { return Object.fromEntries((Array.isArray(keys) ? keys : [keys]).map(key => [key, saved[key]])); },
    async set(values) { Object.assign(saved, values); }
  } },
  action: { setBadgeText() {}, setBadgeBackgroundColor() {} },
  notifications: { create(id, message, callback) { notices.push(message); callback(); } },
  tabs: { async create(value) { opened.push(value); } }
};
const sandbox = { chrome, TextEncoder, setTimeout, clearTimeout };
const context = vm.createContext(sandbox);
context.importScripts = (...files) => files.forEach(file => vm.runInContext(fs.readFileSync(path.join(base, file), 'utf8'), context));
vm.runInContext(fs.readFileSync(path.join(base, 'background.js'), 'utf8'), context);
const tick = () => new Promise(resolve => setImmediate(resolve));
const copy = value => JSON.parse(JSON.stringify(value));
async function request(message) {
  return new Promise(resolve => assert.equal(listeners.message(message, { id: 'v3-companion' }, resolve), true));
}
(async () => {
  await tick();
  const defaults = copy(context.BridgeConfig.defaults);
  assert.equal(defaults.playerSize, 'small');
  assert.equal(defaults.fadeWhenIdle, true);
  assert.equal(defaults.autoHide, true); assert.equal(defaults.autoHideSeconds, 5);
  const beforeAutoHide = copy(defaults); delete beforeAutoHide.autoHide; delete beforeAutoHide.autoHideSeconds;
  assert.equal(context.BridgeConfig.normalize(beforeAutoHide).autoHide, true, 'Existing users must get the new default');
  assert.equal(context.BridgeConfig.normalize(beforeAutoHide).autoHideSeconds, 5);
  assert.equal(defaults.positionLocked, false);
  const raw = '  Français — العربية *** https://example.com  ';
  assert.equal(context.TextCleanup.clean(raw, defaults), raw, 'Cleanup must be completely opt-in');
  const cleanup = { ...defaults, cleanupUrls: true, cleanupSymbols: true, cleanupCode: true };
  const sample = 'Read [the guide](https://example.com/a).\nhttps://example.com/path\n***\n```js\nalert("skip this");\n```\nKeep Français — العربية and 3.14.';
  const cleaned = context.TextCleanup.clean(sample, cleanup);
  assert.ok(cleaned.includes('the guide.') && cleaned.includes('Français — العربية') && cleaned.includes('3.14'));
  assert.ok(!/https|alert|\*\*\*|```/.test(cleaned));
  assert.equal(context.TextCleanup.clean('Before\n~~~python\nskip()\n~~~\nAfter', cleanup), 'Before\n\nAfter');
  assert.ok(context.TextCleanup.clean('Before\n```js\nunclosed()', cleanup).includes('unclosed()'), 'Unclosed fences must not discard the rest of a passage');
  assert.ok(context.TextCleanup.clean('Use `inline()` code.', cleanup).includes('`inline()`'));
  const oldSettings = copy(defaults); delete oldSettings.placement;
  assert.equal(context.BridgeConfig.normalize(oldSettings).placement, 'bottom-right');
  assert.equal(context.BridgeConfig.normalize(oldSettings).customPosition, null);
  await request({ type: 'enable' });
  const first = ports[0];
  await first.message({ type: 'hello' }); await tick();
  assert.equal(saved.status.connected, true);
  await request({ type: 'saveSettings', settings: cleanup });
  await first.message({ type: 'command', method: 'play', text: sample, newSession: true }); await tick();
  assert.equal(sent.at(-1).text, cleaned);
  assert.equal(nativeSent.filter(m => m.type === 'session').at(-1).text, cleaned, 'Displayed passage and spoken passage must be identical');
  const beforeEmpty = sent.length;
  await first.message({ type: 'command', method: 'play', text: 'https://example.com\n***', newSession: true }); await tick();
  assert.equal(sent.length, beforeEmpty);
  assert.match(saved.status.message, /entire selection/);
  await first.message({ type: 'command', method: 'play', text: 'https://replay.example', newSession: false }); await tick();
  assert.equal(sent.at(-1).text, 'https://replay.example', 'Sentence replays must not run cleanup again');
  await request({ type: 'saveSettings', settings: defaults });
  await first.message({ type: 'preference', name: 'playerSize', value: 'large' }); await tick();
  assert.equal(saved.settings.playerSize, 'large');
  await first.message({ type: 'preference', name: 'positionLocked', value: true }); await tick();
  await first.message({ type: 'position', x: 40, y: 90, physical: true });
  assert.equal(saved.settings.customPosition, null, 'Locked position must ignore drag messages');
  await first.message({ type: 'preference', name: 'playerSize', value: 'huge' }); await tick();
  assert.equal(saved.settings.playerSize, 'large', 'Invalid menu preferences must not be persisted');
  await first.message({ type: 'preference', name: 'fadeWhenIdle', value: 'false' }); await tick();
  assert.equal(saved.settings.fadeWhenIdle, true, 'On/off preferences must require booleans');
  await first.message({ type: 'preference', name: 'autoHideSeconds', value: 3 }); await tick();
  assert.equal(saved.settings.autoHideSeconds, 3, 'The tray menu must save the selected delay');
  await first.message({ type: 'preference', name: 'autoHide', value: false }); await tick();
  assert.equal(saved.settings.autoHide, false, 'Automatic hiding must be switchable off');
  await first.message({ type: 'preference', name: 'autoHideSeconds', value: 4 }); await tick();
  assert.equal(saved.settings.autoHideSeconds, 3, 'Unsupported delays must not overwrite the saved choice');
  await first.message({ type: 'preference', name: 'autoHideSeconds', value: '5' }); await tick();
  assert.equal(saved.settings.autoHideSeconds, 3, 'Delay values must be numeric');
  await first.message({ type: 'preference', name: 'autoHide', value: 'false' }); await tick();
  assert.equal(saved.settings.autoHide, false, 'The hide switch must require a boolean');
  await request({ type: 'saveSettings', settings: defaults });
  await first.message({ type: 'command', method: 'play', text: 'Français — العربية', newSession: true }); await tick();
  assert.deepEqual(sent.at(-1), { method: 'play', text: 'Français — العربية' });
  assert.equal(nativeSent.filter(m => m.type === 'session').at(-1).text, 'Français — العربية');
  assert.equal(saved.status.hasPassage, true);
  await request({ type: 'pause' }); assert.equal(sent.at(-1).method, 'pause');
  await request({ type: 'pause' }); assert.equal(sent.at(-1).method, 'resume');
  await request({ type: 'stop' }); assert.equal(sent.at(-1).method, 'stop');
  await request({ type: 'pause' }); assert.equal(nativeSent.at(-1).type, 'restart');
  const duplicate = copy(defaults); duplicate.stop = duplicate.read;
  assert.equal((await request({ type: 'saveSettings', settings: duplicate })).ok, false);
  const oversized = copy(defaults); oversized.read.modifiers = 4294967297;
  assert.equal((await request({ type: 'saveSettings', settings: oversized })).ok, false);
  const custom = copy(defaults); custom.read = { modifiers: 7, key: 82 };
  custom.placement = 'top-right';
  let result = await request({ type: 'saveSettings', settings: custom });
  assert.equal(result.ok, true); assert.equal(saved.settings.read.label, 'Ctrl + Alt + Shift + R');
  assert.equal(saved.settings.placement, 'top-right');
  const configureCount = nativeSent.filter(m => m.type === 'configure').length;
  await first.message({ type: 'position', x: -40, y: 90, physical: true });
  assert.deepEqual(copy(saved.settings.customPosition), { x: -40, y: 90, physical: true });
  assert.equal(nativeSent.filter(m => m.type === 'configure').length, configureCount, 'Saving a drag must not re-register shortcuts');
  const badPosition = copy(custom); badPosition.customPosition = { x: 1.5, y: 90 };
  assert.equal((await request({ type: 'saveSettings', settings: badPosition })).ok, false);
  assert.deepEqual(copy(saved.settings.customPosition), { x: -40, y: 90, physical: true });
  assert.equal(nativeSent.filter(m => m.type === 'configure').at(-1).settings.placement, 'top-right');
  const invalidPlacement = copy(custom); invalidPlacement.placement = 'bottom-left';
  assert.equal((await request({ type: 'saveSettings', settings: invalidPlacement })).ok, false);
  assert.equal(saved.settings.placement, 'top-right');
  await first.message({ type: 'command', method: 'setPlacement', text: 'bottom-right' }); await tick();
  assert.equal(saved.settings.placement, 'bottom-right', 'Position control in the player must persist the corner');
  assert.equal(saved.settings.customPosition, null, 'Selecting a corner must reset the dragged position');
  rejectConfig = true;
  result = await request({ type: 'saveSettings', settings: defaults });
  assert.equal(result.ok, false); assert.equal(saved.settings.read.key, 82);
  assert.equal(saved.status.connected, true);
  rejectConfig = false;
  deliveryError = { message: 'Could not establish connection. Receiving end does not exist.' };
  await request({ type: 'test' });
  assert.match(saved.status.message, /Could not reach Read Aloud/);
  assert.ok(notices.some(notice => /Could not reach/.test(notice.message)));
  deliveryError = { message: 'The message port closed before a response was received.' };
  await request({ type: 'test' }); assert.equal(saved.status.playback, 'playing');
  deliveryError = undefined;
  await request({ type: 'voices' }); assert.match(opened.at(-1).url, /chrome:\/\/extensions/);
  const quiet = copy(custom); quiet.notifications = false;
  await request({ type: 'saveSettings', settings: quiet });
  const count = notices.length;
  await first.message({ type: 'error', message: 'A quiet error.' });
  assert.equal(notices.length, count);
  assert.ok(!JSON.stringify(saved).includes('Français'), 'Passage must not be persisted');
  await request({ type: 'disable' }); assert.equal(first.disconnected, true);
  await request({ type: 'enable' }); const second = ports[1];
  await second.message({ type: 'hello' }); await tick();
  first.closed(); assert.equal(saved.status.connected, true);
  await second.message({ type: 'command', method: 'play', text: '', newSession: true }); await tick();
  assert.match(saved.status.message, /Highlight some text/);
  await second.message({ type: 'command', method: 'disable' }); await tick();
  assert.equal(second.disconnected, true); assert.equal(saved.enabled, false);
  await listeners.startup(); await tick(); assert.equal(ports.length, 2, 'Tray disable must prevent automatic reconnection');
  console.log('PASS: cleanup opt-in, Unicode, same displayed/spoken text, empty cleanup, tray preferences/disable, all playback/position/settings/error/reconnect checks');
})().catch(error => { console.error(error); process.exitCode = 1; });
