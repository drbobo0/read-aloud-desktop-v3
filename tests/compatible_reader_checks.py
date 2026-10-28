"""Verify the separately prepared reader and, optionally, its unchanged source package."""
import argparse
import base64
import hashlib
import json
from pathlib import Path

root = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser()
parser.add_argument('--source', type=Path)
args = parser.parse_args()
copy = root / 'compatible-reader'
manifest = json.loads((copy / 'manifest.json').read_text(encoding='utf-8-sig'))
identity = json.loads((root / 'compatibility/reader-identity.json').read_text())
digest = hashlib.sha256(base64.b64decode(manifest['key'])).digest()[:16]
reader_id = ''.join(chr(97 + nibble) for byte in digest for nibble in (byte >> 4, byte & 15))
bridge_id = json.loads((root / 'identity.json').read_text())['id']
assert reader_id == identity['id'] and reader_id not in (bridge_id, 'hdhinadidafjejdhmfkjgnolgimiaplp')
assert manifest['externally_connectable'] == {'ids': [bridge_id]}
assert not manifest.get('update_url') and not (copy / '_metadata').exists()
assert not any('suggested_key' in value for value in manifest['commands'].values())
assert (copy / 'js/desktop-bridge.js').read_bytes() == (root / 'compatibility/desktop-bridge.js').read_bytes()
assert (copy / 'ReadAloud-LICENSE.txt').read_bytes() == (root / 'compatibility/ReadAloud-LICENSE.txt').read_bytes()
assert 'sessionId: opts.desktopSessionId' in (copy / 'js/player.js').read_text(encoding='utf-8-sig')
assert 'js/desktop-bridge.js' in (copy / 'background.js').read_text(encoding='utf-8-sig')
if args.source:
    source = args.source.resolve()
    assert copy.resolve() != source
    original = json.loads((source / 'manifest.json').read_text(encoding='utf-8-sig'))
    assert set(manifest['permissions']) == set(original['permissions'])
    assert manifest['host_permissions'] == original['host_permissions']
    assert manifest['optional_host_permissions'] == original['optional_host_permissions']
    changed = []
    for item in source.rglob('*'):
        relative = item.relative_to(source)
        if not item.is_file() or relative.parts[0] == '_metadata':
            continue
        assert (copy / relative).is_file(), relative
        if item.read_bytes() != (copy / relative).read_bytes():
            changed.append(relative.as_posix())
    assert set(changed) == {'manifest.json', 'background.js', 'js/events.js', 'js/player.js'}, changed
    assert 'desktopSessionId' not in (source / 'js/player.js').read_text(encoding='utf-8-sig')
    assert 'desktop-bridge.js' not in (source / 'background.js').read_text(encoding='utf-8-sig')
    print('PASS: only the four intended files differ from the original package; permissions and original code stay separate')
print('PASS: separate reader identity, trusted bridge only, no updater/shortcut conflicts, completion hook and original MIT notice')
