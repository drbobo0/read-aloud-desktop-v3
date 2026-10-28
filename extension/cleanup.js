(function (root) {
  function clean(text, settings) {
    if (!settings.cleanupUrls && !settings.cleanupSymbols && !settings.cleanupCode) return text;
    let result = text;
    if (settings.cleanupCode) {
      // Only complete fenced blocks are omitted. Unclosed fences and inline code remain readable.
      result = result.replace(/^[\t ]*(`{3,}|~{3,})[^\r\n]*\r?\n[\s\S]*?^[\t ]*\1[\t ]*(?=\r?$)/gm, '\n');
    }
    if (settings.cleanupUrls) {
      result = result.replace(/\[([^\]\r\n]+)\]\((?:https?:\/\/|www\.)[^\s)]+\)/giu, '$1');
      result = result.replace(/<(?:https?:\/\/|www\.)[^\s<>]+>/giu, '');
      result = result.replace(/\b(?:https?:\/\/|www\.)[^\s<>"\u2028\u2029]+/giu,
        url => url.match(/[.,!?;:)\]}]+$/u)?.[0] || '');
    }
    if (settings.cleanupSymbols) result = result.replace(/([\p{P}\p{S}])\1{2,}/gu, ' ');
    return result.replace(/\r\n?/g, '\n').replace(/[\t ]+/g, ' ').replace(/ *\n */g, '\n').replace(/\n{3,}/g, '\n\n').trim();
  }
  root.TextCleanup = { clean };
})(globalThis);
