"""Exercise the built helper's real protocol. No clipboard is read or written."""
import argparse
import base64
import concurrent.futures
import hashlib
import json
from pathlib import Path
import struct
import subprocess

ROOT = Path(__file__).resolve().parents[1]
EXE = ROOT / json.loads((ROOT / 'runtime.json').read_text())['helper']

class Host:
    def __init__(self, real=False):
        self.process = subprocess.Popen([str(EXE)] + ([] if real else ['--protocol-test']),
                                        stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        assert self.receive()['type'] == 'hello'
    def receive(self):
        def read():
            header = self.process.stdout.read(4)
            assert len(header) == 4, 'Missing message header'
            size, = struct.unpack('<I', header)
            assert 0 < size <= 1048576
            return json.loads(self.process.stdout.read(size))
        with concurrent.futures.ThreadPoolExecutor() as pool:
            try:
                return pool.submit(read).result(timeout=7)
            except BaseException:
                self.process.kill()
                raise
    def send(self, message, split=False):
        data = json.dumps(message, ensure_ascii=False).encode('utf-8')
        frame = struct.pack('<I', len(data)) + data
        for part in ([frame[:2], frame[2:7], frame[7:]] if split else [frame]):
            self.process.stdin.write(part)
            self.process.stdin.flush()
    def configure(self, settings):
        self.send({'type': 'configure', 'requestId': 'check', 'settings': settings})
        return self.receive()
    def close(self):
        if self.process.poll() is None:
            self.process.stdin.close()
            assert self.process.wait(timeout=7) == 0

def custom_settings(modifiers=7):
    return {name: {'modifiers': modifiers, 'key': 120 + i} for i, name in enumerate(['read', 'pause', 'stop', 'player'])}

parser = argparse.ArgumentParser()
parser.add_argument('--windows-hotkeys', action='store_true')
args = parser.parse_args()
host = Host()
try:
    host.send({'type': 'ping', 'unicode': 'Français — العربية'}, split=True)
    assert host.receive() == {'type': 'pong'}
    result = host.configure(custom_settings())
    assert result['ok'] and result['active']
    assert result['settings']['placement'] == 'bottom-right', 'Older settings must default to bottom right'
    assert result['settings']['customPosition'] is None, 'Older settings must start without a dragged position'
    assert result['settings']['playerSize'] == 'small'
    assert result['settings']['fadeWhenIdle'] is True
    assert result['settings']['positionLocked'] is False
    assert not any(result['settings'][name] for name in ('cleanupUrls', 'cleanupSymbols', 'cleanupCode'))
    preferences = custom_settings()
    preferences.update(playerSize='large', positionLocked=True, fadeWhenIdle=False, cleanupUrls=True, cleanupSymbols=True, cleanupCode=True)
    result = host.configure(preferences)
    assert result['ok'] and all(result['settings'][name] == value for name, value in preferences.items() if name not in ('read', 'pause', 'stop', 'player'))
    preferences['playerSize'] = 'huge'
    assert not host.configure(preferences)['ok']
    assert host.configure(custom_settings())['ok']
    top_settings = custom_settings()
    top_settings['placement'] = 'top-right'
    result = host.configure(top_settings)
    assert result['ok'] and result['settings']['placement'] == 'top-right'
    top_settings['placement'] = 'invalid-corner'
    assert not host.configure(top_settings)['ok']
    dragged = custom_settings()
    dragged['customPosition'] = {'x': -200, 'y': 150}
    result = host.configure(dragged)
    assert result['ok'] and result['settings']['customPosition'] == {'x': -200, 'y': 150, 'physical': False}
    dragged['customPosition']['x'] = 100001
    assert not host.configure(dragged)['ok']
    bad = custom_settings()
    bad['stop'] = bad['read']
    result = host.configure(bad)
    assert not result['ok'] and result['active'], result
    bad = custom_settings()
    bad['read']['modifiers'] = 0
    assert not host.configure(bad)['ok']
    assert not host.configure('malformed')['ok']
    assert not host.configure(None)['ok']
    host.send({'type': 'session', 'text': 'First sentence. Deuxième phrase! الثالث؟ Last line', 'show': False})
    host.send({'type': 'testMove', 'delta': 1})
    passage = host.receive()
    assert passage['count'] == 4 and passage['index'] == 1
    assert passage['tail'].startswith('Deuxième phrase!'), passage
    host.send({'type': 'testMove', 'delta': 100})
    assert host.receive()['tail'] == 'Last line'
    host.send({'type': 'testMove', 'delta': -100})
    assert host.receive()['index'] == 0
    host.send({'type': 'clear'})
    host.send({'type': 'testMove', 'delta': 1})
    assert host.receive()['count'] == 0
finally:
    host.close()
print('PASS: native split framing, Unicode, invalid/duplicate shortcuts, sentence navigation and pipe cleanup')

manifest = json.loads((ROOT / 'extension/manifest.json').read_text())
identity = json.loads((ROOT / 'identity.json').read_text())
key_digest = hashlib.sha256(base64.b64decode(manifest['key'])).digest()[:16]
actual_id = ''.join(chr(97 + nibble) for byte in key_digest for nibble in (byte >> 4, byte & 15))
assert actual_id == identity['id'] and len(actual_id) == 32
for folder in ('read-aloud-desktop', 'read-aloud-desktop-v2'):
    previous = ROOT.parent / folder / 'identity.json'
    if previous.is_file():
        assert actual_id != json.loads(previous.read_text())['id']
assert set(manifest['permissions']) == {'nativeMessaging', 'storage', 'notifications'}
assert not manifest.get('host_permissions') and not manifest.get('content_scripts')
assert (ROOT / 'extension/icon128.png').read_bytes().startswith(b'\x89PNG')
assert (ROOT / (EXE.name + '.config')).exists(), 'High-DPI runtime configuration must ship with the helper'
print('PASS: V3 identity, limited permissions, notification icon and high-DPI runtime configuration')
snapshots = [ROOT / 'tests' / name for name in ('v1-before.json', 'previous-versions-before.json')]
for snapshot in snapshots:
    if snapshot.is_file():
        for entry in json.loads(snapshot.read_text(encoding='utf-8-sig')):
            assert hashlib.sha256(Path(entry['Path']).read_bytes()).hexdigest().upper() == entry['Hash'], entry['Path']
if any(snapshot.is_file() for snapshot in snapshots):
    print('PASS: local previous-version file preservation checks')

if args.windows_hotkeys:
    first, second = Host(real=True), Host(real=True)
    try:
        original = custom_settings(7)  # Ctrl+Alt+Shift+F9..F12, temporary test keys
        occupied = custom_settings(7)
        for index, name in enumerate(['read', 'pause', 'stop', 'player']):
            occupied[name]['key'] = 116 + index  # Ctrl+Alt+Shift+F5..F8
        result = first.configure(original)
        assert result['ok'], result
        result = second.configure(occupied)
        assert result['ok'], result
        conflict = custom_settings(7)
        conflict['read'] = occupied['read']
        result = first.configure(conflict)
        assert not result['ok'] and result['active'], result
        assert 'previous shortcuts are still active' in result['message']
        assert first.configure(original)['ok']
        assert not second.configure(original)['ok'], 'First host must still own its original shortcuts'
    finally:
        first.close()
        second.close()
    print('PASS: real Windows shortcut registration, collision detection and rollback; all test shortcuts released')
