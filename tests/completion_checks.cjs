const fs = require('fs');
const path = require('path');
const vm = require('vm');
const assert = require('assert/strict');
const root = path.resolve(__dirname, '..');
const BRIDGE = JSON.parse(fs.readFileSync(path.join(root, 'identity.json'))).id;
const READER = JSON.parse(fs.readFileSync(path.join(root, 'compatibility/reader-identity.json'))).id;
const ORIGINAL = 'hdhinadidafjejdhmfkjgnolgimiaplp';
const native = [], messages = [], saved = {}, documents = [], notices = [];
const handlers = {};
let readerInstalled = true, delayedAcceptance = false, deferredReply, originalCommands = 0;
const tick = () => new Promise(resolve => setImmediate(resolve));
const flush = async () => { await tick(); await tick(); };
let bridge, reader, player;

function internalEnd(message) {
  return new Promise(resolve => handlers.readerInternal(message, { id: READER }, resolve));
}
const readerApi = {
  runtime: {
    id: READER,
    onMessageExternal: { addListener(fn) { handlers.readerExternal = fn; } },
    onMessage: { addListener(fn) { handlers.readerInternal = fn; } },
    async sendMessage(id, message) {
      assert.equal(id, BRIDGE);
      handlers.bridgeExternal(message, { id: READER }, () => {});
    }
  }
};
// Use the actual generated player's playText function when the copy is available.
const playerFile = path.join(root, 'compatible-reader/js/player.js');
let playSource;
if (fs.existsSync(playerFile)) {
  playSource = fs.readFileSync(playerFile, 'utf8').match(/function playText\(text, opts\)[\s\S]*?(?=function playTab\()/)[0];
  assert.ok(playSource.includes('desktopSessionId'), 'The prepared player must report its actual document end');
} else {
  playSource = `function playText(text, opts) { openDoc(text, err => {
    brapi.runtime.sendMessage({dest:'desktopBridge',method:'ended',sessionId:opts.desktopSessionId,error:err?{message:err.message}:null}).catch(()=>{});
  }); return activeDoc.play(); }`;
}
player = vm.createContext({
  activeDoc: null, playbackError: null,
  SimpleSource: function (paragraphs, options) { this.paragraphs = paragraphs; },
  openDoc(source, end) {
    const doc = { source, end, play: () => Promise.resolve() };
    player.activeDoc = doc; documents.push(doc);
  },
  handleError() {}, closeDoc() { player.activeDoc = null; },
  brapi: { runtime: { sendMessage: internalEnd } }
});
vm.runInContext(playSource, player);
reader = vm.createContext({
  brapi: readerApi, TextEncoder,
  playText(text, options) { player.activeDoc = null; return player.playText(text, options); }
});
vm.runInContext(fs.readFileSync(path.join(root, 'compatibility/desktop-bridge.js'), 'utf8'), reader);
const chrome = {
  runtime: {
    id: BRIDGE, lastError: undefined,
    onMessageExternal: { addListener(fn) { handlers.bridgeExternal = fn; } },
    onMessage: { addListener(fn) { handlers.bridge = fn; } },
    onStartup: { addListener() {} },
    sendMessage(id, message, reply) {
      messages.push({ id, ...message });
      if (id === ORIGINAL) { originalCommands++; reply(); return; }
      assert.equal(id, READER);
      if (!readerInstalled) {
        chrome.runtime.lastError = { message: 'Reader not installed.' }; reply(); chrome.runtime.lastError = undefined; return;
      }
      if (message.method === 'desktopHello' || message.method === 'desktopPlay') {
        handlers.readerExternal(message, { id: BRIDGE }, response => {
          if (delayedAcceptance && message.method === 'desktopPlay') deferredReply = () => reply(response);
          else reply(response);
        });
      } else { assert.ok(['pause','resume','stop'].includes(message.method)); reply(); }
    },
    connectNative() {
      const port = {
        onMessage: { addListener(fn) { handlers.native = fn; } },
        onDisconnect: { addListener(fn) { handlers.disconnected = fn; } }, disconnect() {},
        postMessage(message) {
          native.push(message);
          if (message.type === 'configure') queueMicrotask(() => handlers.native({ type:'configured',requestId:message.requestId,ok:true,active:true,settings:message.settings,message:'Ready.' }));
        }
      }; return port;
    }
  },
  storage: { local: { async get() { return {}; }, async set(values) { Object.assign(saved, values); } } },
  action: { setBadgeText() {}, setBadgeBackgroundColor() {} },
  notifications: { create(id, notice, reply) { notices.push(notice); reply(); } },
  tabs: { async create() {} }
};
bridge = vm.createContext({ chrome, TextEncoder, setTimeout, clearTimeout });
bridge.importScripts = (...files) => files.forEach(file => vm.runInContext(fs.readFileSync(path.join(root,'extension',file),'utf8'),bridge));
vm.runInContext(fs.readFileSync(path.join(root,'extension/background.js'),'utf8'),bridge);
const request = message => new Promise(resolve => handlers.bridge(message,{id:BRIDGE},resolve));
const play = async text => {
  await handlers.native({type:'command',method:'play',text,newSession:true}); await flush();
  return documents.at(-1);
};
const finishes = () => native.filter(message => message.type === 'finished');
(async () => {
  await flush(); await request({type:'enable'}); await handlers.native({type:'hello'}); await flush();
  const first = await play('A long passage. Another paragraph.');
  assert.equal(saved.status.playback,'playing'); assert.equal(finishes().length,0);
  await request({type:'pause'}); assert.equal(saved.status.playback,'paused'); assert.equal(finishes().length,0,'Pause must never hide the player');
  await request({type:'pause'}); assert.equal(saved.status.playback,'playing');
  const sessionId = messages.findLast(message => message.method === 'desktopPlay').sessionId;
  handlers.bridgeExternal({type:'readerCompleted',sessionId},{id:ORIGINAL},()=>{});
  handlers.bridgeExternal({type:'readerCompleted',sessionId:'old_session'},{id:READER},()=>{});
  assert.equal(finishes().length,0,'Foreign and stale finish events must be ignored');
  first.end(); await flush();
  assert.equal(finishes().length,1); assert.equal(finishes()[0].sessionId,sessionId);
  assert.equal(saved.status.playback,'stopped'); assert.equal(saved.status.message,'Reading finished.');
  first.end(); await flush(); assert.equal(finishes().length,1,'A duplicate finish must be ignored');
  const second = await play('Previous selection.'); const third = await play('Current selection.');
  second.end(); await flush(); assert.equal(finishes().length,1,'An old selection must not hide a newer one');
  third.end(); await flush(); assert.equal(finishes().length,2);
  const failure = await play('A provider error.'); failure.end(new Error('Voice unavailable.')); await flush();
  assert.equal(finishes().length,2,'A provider error must keep controls available'); assert.match(saved.status.message,/could not finish/);
  assert.ok(!JSON.stringify(saved).includes('Voice unavailable'),'Raw provider errors must not be persisted');
  const stopped = await play('Stopped manually.'); await request({type:'stop'}); stopped.end(); await flush();
  assert.equal(finishes().length,2,'Stop cancels a pending natural-completion event');
  delayedAcceptance = true;
  const short = await play('Hi.'); short.end(); await flush();
  assert.equal(finishes().length,2,'An end before play acceptance must wait for that acceptance');
  delayedAcceptance = false; deferredReply(); await flush(); assert.equal(finishes().length,3); assert.equal(saved.status.playback,'stopped');
  const disabled = await play('Disable before finishing.'); await request({type:'disable'}); disabled.end(); await flush();
  assert.equal(finishes().length,3,'Disabling must cancel completion tracking');
  await request({type:'enable'}); await handlers.native({type:'hello'}); await flush();
  readerInstalled = false; await play('Original reader still works.');
  assert.ok(originalCommands > 0); assert.equal(saved.status.completionSupported,false); assert.equal(finishes().length,3,'The original reader must not simulate completion');
  const before = documents.length;
  handlers.readerExternal({method:'desktopPlay',text:'Blocked',sessionId:'bad_sender'},{id:ORIGINAL},()=>{}); await flush();
  assert.equal(documents.length,before,'Only Desktop V3 may use the compatible-reader API');
  await request({type:'disable'});
  assert.ok(!JSON.stringify(saved).includes('Current selection.'),'Passage text must not be saved');
  console.log('PASS: real player-end hook, finish-only hiding, pause/resume, errors, stale/foreign/duplicate events, early completion, stop/disable, stock-reader fallback');
})().catch(error => { console.error(error); process.exitCode=1; });
