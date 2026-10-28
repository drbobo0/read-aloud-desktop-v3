(function (root) {
  const defaults = {
    read: { modifiers: 1, key: 86, label: 'Alt + V' },
    pause: { modifiers: 3, key: 32, label: 'Ctrl + Alt + Space' },
    stop: { modifiers: 5, key: 86, label: 'Alt + Shift + V' },
    player: { modifiers: 3, key: 86, label: 'Ctrl + Alt + V' },
    showOnRead: true,
    notifications: true,
    placement: 'bottom-right',
    playerSize: 'small',
    fadeWhenIdle: true,
    autoHide: true,
    autoHideSeconds: 5,
    positionLocked: false,
    cleanupUrls: false,
    cleanupSymbols: false,
    cleanupCode: false,
    customPosition: null
  };
  const actions = ['read', 'pause', 'stop', 'player'];
  function label(shortcut) {
    const parts = [];
    if (shortcut.modifiers & 2) parts.push('Ctrl');
    if (shortcut.modifiers & 1) parts.push('Alt');
    if (shortcut.modifiers & 4) parts.push('Shift');
    parts.push(shortcut.key === 32 ? 'Space' : shortcut.key >= 112 ? `F${shortcut.key - 111}` : String.fromCharCode(shortcut.key));
    return parts.join(' + ');
  }
  function normalize(value) {
    if (!value || typeof value !== 'object') throw new Error('Choose shortcuts for all four actions.');
    const result = {};
    const used = new Set();
    for (const action of actions) {
      const shortcut = value[action];
      if (!shortcut || !Number.isInteger(shortcut.modifiers) || shortcut.modifiers < 1 || shortcut.modifiers > 7 || !(shortcut.modifiers & 3)) {
        throw new Error('Each shortcut needs Ctrl or Alt, with optional Shift.');
      }
      const key = shortcut.key;
      if (!Number.isInteger(key) || !(key === 32 || key >= 48 && key <= 57 || key >= 65 && key <= 90 || key >= 112 && key <= 123)) {
        throw new Error('Use a letter, number, F1–F12, or Space.');
      }
      const identity = `${shortcut.modifiers}:${key}`;
      if (used.has(identity)) throw new Error('Choose a different shortcut for each action.');
      used.add(identity);
      result[action] = { modifiers: shortcut.modifiers, key, label: label(shortcut) };
    }
    result.showOnRead = value.showOnRead !== false;
    result.notifications = value.notifications !== false;
    result.placement = value.placement ?? 'bottom-right';
    result.playerSize = value.playerSize ?? 'small';
    if (!['small', 'medium', 'large'].includes(result.playerSize)) throw new Error('Choose Small, Medium or Large for the player.');
    for (const name of ['fadeWhenIdle', 'autoHide', 'positionLocked', 'cleanupUrls', 'cleanupSymbols', 'cleanupCode']) {
      if (value[name] !== undefined && typeof value[name] !== 'boolean') throw new Error('Choose a valid on/off preference.');
      result[name] = value[name] ?? defaults[name];
    }
    result.autoHideSeconds = value.autoHideSeconds ?? defaults.autoHideSeconds;
    if (![3, 5].includes(result.autoHideSeconds)) throw new Error('Choose 3 or 5 seconds for automatic hiding.');
    if (!['bottom-right', 'top-right'].includes(result.placement)) throw new Error('Choose bottom right or top right for the player.');
    result.customPosition = null;
    if (value.customPosition != null) {
      const { x, y } = value.customPosition;
      if (!Number.isInteger(x) || !Number.isInteger(y) || Math.abs(x) > 100000 || Math.abs(y) > 100000) throw new Error('Choose a valid player position.');
      result.customPosition = { x, y, physical: value.customPosition.physical === true };
    }
    return result;
  }
  root.BridgeConfig = { defaults, actions, normalize, label };
})(globalThis);
