let draft;
let dirty = false;
const status = document.getElementById('status');
const saveStatus = document.getElementById('save-status');
function showState(state) {
  status.textContent = state.message || 'Enable V3 to begin.';
  document.getElementById('connection').textContent = state.connected ? 'Desktop shortcuts active' : state.enabled ? 'Needs attention' : 'Desktop shortcuts off';
  document.getElementById('dot').classList.toggle('connected', Boolean(state.connected));
  document.getElementById('pause').textContent = state.playback === 'paused' ? 'Resume' : 'Pause / resume';
}
function fill(settings) {
  dirty = false;
  draft = BridgeConfig.normalize(settings);
  for (const action of BridgeConfig.actions) document.getElementById(`key-${action}`).value = draft[action].label;
  document.getElementById('showOnRead').checked = draft.showOnRead;
  document.getElementById('notifications').checked = draft.notifications;
  document.getElementById('placement').value = draft.placement;
  document.getElementById('playerSize').value = draft.playerSize;
  for (const name of ['fadeWhenIdle', 'positionLocked', 'cleanupUrls', 'cleanupSymbols', 'cleanupCode']) document.getElementById(name).checked = draft[name];
  positionHint();
}
function positionHint() { document.getElementById('position-hint').textContent = document.getElementById('positionLocked').checked ? 'Dragging is locked. Corner presets and reset still work.' : draft?.customPosition ? 'Your dragged position is saved. Reset below to return to a corner.' : 'Drag the Read label to move the player anywhere. Its position is remembered.'; }
function explain(message, error = false) { saveStatus.textContent = message; saveStatus.classList.toggle('error', error); }
async function send(message) {
  const reply = await chrome.runtime.sendMessage(message);
  if (reply?.ok === false) throw new Error(reply.message);
  return reply;
}
send({ type: 'getState' }).then(state => { showState(state); fill(state.settings); }).catch(error => { status.textContent = error.message; });
chrome.storage.onChanged.addListener(changes => { if (changes.status) showState(changes.status.newValue); });
chrome.storage.onChanged.addListener(changes => {
  if (changes.settings && draft) {
    if (!dirty) fill(changes.settings.newValue);
    else { draft.customPosition = changes.settings.newValue.customPosition ?? null; positionHint(); }
  }
});
document.getElementById('preferences').addEventListener('change', () => { dirty = true; });
document.getElementById('positionLocked').addEventListener('change', positionHint);
document.getElementById('placement').addEventListener('change', () => {
  if (!draft) return;
  draft.customPosition = null; positionHint(); explain('Click Save preferences to move to the selected corner.');
});
document.getElementById('reset-position').addEventListener('click', () => {
  if (!draft) return;
  draft.customPosition = null; positionHint(); explain('Click Save preferences to reset the player to the selected corner.');
  dirty = true;
});
for (const type of ['enable', 'disable', 'player', 'pause', 'stop', 'test', 'voices']) {
  document.getElementById(type).addEventListener('click', async () => {
    const button = document.getElementById(type); button.disabled = true;
    try { const state = await send({ type }); if (state) showState(state); }
    catch (error) { status.textContent = error.message; }
    finally { button.disabled = false; }
  });
}
for (const action of BridgeConfig.actions) {
  const input = document.getElementById(`key-${action}`);
  input.addEventListener('keydown', event => {
    if (event.key === 'Tab') return;
    event.preventDefault();
    if (!draft || ['Control', 'Alt', 'Shift', 'Meta'].includes(event.key)) return;
    if (event.key === 'Escape') { input.blur(); return; }
    const key = event.code === 'Space' ? 32 : /^F([1-9]|1[0-2])$/.test(event.key) ? 111 + Number(event.key.slice(1))
      : /^Digit[0-9]$/.test(event.code) ? Number(event.code.slice(5)) + 48
      : /^[a-z]$/i.test(event.key) ? event.key.toUpperCase().charCodeAt(0) : 0;
    const shortcut = { modifiers: (event.altKey ? 1 : 0) | (event.ctrlKey ? 2 : 0) | (event.shiftKey ? 4 : 0), key };
    if (event.metaKey || !(shortcut.modifiers & 3) || !key) { explain('Use Ctrl or Alt plus a letter, number, F1–F12, or Space.', true); return; }
    draft[action] = { ...shortcut, label: BridgeConfig.label(shortcut) };
    dirty = true;
    input.value = draft[action].label; explain('Unsaved changes. Click Save preferences to apply.');
  });
}
document.getElementById('preferences').addEventListener('submit', async event => {
  event.preventDefault();
  const save = document.getElementById('save'); save.disabled = true;
  try {
    draft.showOnRead = document.getElementById('showOnRead').checked;
    draft.notifications = document.getElementById('notifications').checked;
    draft.placement = document.getElementById('placement').value;
    draft.playerSize = document.getElementById('playerSize').value;
    for (const name of ['fadeWhenIdle', 'positionLocked', 'cleanupUrls', 'cleanupSymbols', 'cleanupCode']) draft[name] = document.getElementById(name).checked;
    const result = await send({ type: 'saveSettings', settings: BridgeConfig.normalize(draft) });
    fill(result.settings); explain(result.message);
  } catch (error) { explain(error.message, true); }
  finally { save.disabled = false; }
});
document.getElementById('reset').addEventListener('click', () => {
  fill(BridgeConfig.defaults); dirty = true; explain('Defaults selected. Click Save preferences to apply.');
});
