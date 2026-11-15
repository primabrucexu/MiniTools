const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const { JSDOM } = require('jsdom');
const script = fs.readFileSync(process.argv[2] || path.join(__dirname, '../douyin-live-clean.user.js'), 'utf8');
const LIVE = 'https://live.douyin.com/111111';
const FOLLOW = 'https://www.douyin.com/follow/live/111111';

function fixture(url = LIVE, options = {}) {
  const dom = new JSDOM('<!doctype html><html><body><div id="root"><div data-e2e="living-container"><div id="PlayerLayout"><div data-anchor-id="living-basic-player"><video></video></div></div><div id="DanmakuLayout"><canvas></canvas></div></div><div id="chatroom">聊天区</div></div></body></html>', { url, runScripts: 'outside-only', pretendToBeVisual: true });
  const w = dom.window, d = w.document;
  let now = 100000, serial = 0;
  const timers = new Map(), events = { calls: [], ui: [], warnings: [], infos: [] };
  w.Date.now = () => now;
  w.setInterval = (fn, delay) => { const id = ++serial; timers.set(id, { fn, delay, due: now + delay, repeat: true }); return id; };
  w.setTimeout = (fn, delay = 0) => { const id = ++serial; timers.set(id, { fn, delay, due: now + delay, repeat: false }); return id; };
  w.clearInterval = w.clearTimeout = id => timers.delete(id);
  w.console = { log() {}, info(...args) { events.infos.push(args); }, warn(...args) { events.warnings.push(args); }, error(...args) { events.warnings.push(args); } };
  Object.defineProperty(d, 'hidden', { value: false, configurable: true });
  Object.defineProperty(d, 'fullscreenElement', { value: options.browserFullscreen ? d.body : null, writable: true, configurable: true });
  Object.defineProperty(d, 'pictureInPictureElement', { value: options.pictureInPicture ? d.querySelector('video') : null, writable: true, configurable: true });
  w.Element.prototype.getBoundingClientRect = function () {
    const hidden = this.closest('[hidden]') || this.style.display === 'none';
    return { x: 0, y: 0, top: 0, left: 0, right: hidden ? 0 : 960, bottom: hidden ? 0 : 540, width: hidden ? 0 : 960, height: hidden ? 0 : 540 };
  };
  for (const type of ['mouseover', 'mouseenter', 'mouseout', 'mouseleave', 'click', 'keydown']) d.addEventListener(type, () => events.ui.push(type), true);
  const root = d.querySelector('[data-anchor-id="living-basic-player"]');
  const createStore = (config = {}) => {
    const webId = config.webId || '111111';
    const quality = {
      qualities: config.qualities || ['origin', 'fhd', 'hd', 'sd', 'ld'],
      manulQuality: config.currentQuality || 'hd',
      isAbrOpen: config.abr ?? false,
      realQualityMap: config.realQualityMap || {},
      realCurrentQuality: config.realCurrentQuality || config.realQualityMap?.[config.currentQuality || 'hd'] || config.currentQuality || 'hd',
    };
    const liveStream = {
      _config: { qualityLimit: config.qualityLimit },
      getQualityInfo() { return quality; },
      handleChangeQuality(key, callback) {
        events.calls.push({ kind: 'quality', key, webId, ms: now - 100000 });
        if (config.throwQuality) throw new Error('quality not ready');
        if (config.qualityNeverCompletes) return;
        const apply = () => {
          if (config.rejectQuality) { callback?.({ success: false }); return; }
          quality.manulQuality = key; quality.realCurrentQuality = quality.realQualityMap[key] || key; quality.isAbrOpen = false;
          callback?.({ success: true });
        };
        if (config.qualityDelay) w.setTimeout(apply, config.qualityDelay); else apply();
      },
    };
    const api = {
      roomStore: {
        roomInfo: { roomId: config.roomId || '9001', web_rid: webId },
        danmakuSwitch: config.danmaku ?? true,
        foldChatroom: config.foldChatroom ?? true,
        setFoldChatroom(value) {
          events.calls.push({ kind: 'foldChatroom', value, webId, ms: now - 100000 });
          this.foldChatroom = value;
        },
        setDanmakuSwitch(value) {
          events.calls.push({ kind: 'danmaku', value, webId, ms: now - 100000 });
          if (config.throwDanmaku) throw new Error('danmaku not ready');
          this.danmakuSwitch = value;
        },
      },
      giftStore: {
        effectSwitchStatus: config.effects ?? true,
        setEffectSwitchStatus(value) {
          events.calls.push({ kind: 'effects', value, webId, ms: now - 100000 });
          if (config.throwEffects) throw new Error('effects not ready');
          this.effectSwitchStatus = value;
        },
      },
      playerStore: {
        isTheaterOn: config.theater ?? false,
        isFullScreen: config.playerFullscreen ?? false,
        setIsTheaterOn(value) {
          events.calls.push({ kind: 'theater', value, webId, ms: now - 100000 });
          if (config.throwTheater) throw new Error('theater not ready');
          if (config.theaterNeverCompletes) return;
          const apply = () => { this.isTheaterOn = value; };
          if (config.theaterDelay) w.setTimeout(apply, config.theaterDelay); else apply();
        },
        playerInstance: {
          root: config.playerRoot || root,
          plugins: { danmu: { stop() { events.calls.push({ kind: 'danmuStop', webId, ms: now - 100000 }); } } },
          getPlugin(name) { return name === 'liveStream' && !config.pluginMissing ? liveStream : null; },
        },
      },
      paidLiveStore: { isPaidLive: config.paidLive ?? false, paidLiveAuth: { FullScreen: config.fullscreenAuth ?? 1 } },
    };
    const giftSetter = api.giftStore.setEffectSwitchStatus;
    const theaterSetter = api.playerStore.setIsTheaterOn;
    if (config.giftMissing) delete api.giftStore.setEffectSwitchStatus;
    if (config.theaterMissing) delete api.playerStore.setIsTheaterOn;
    return { api, quality, liveStream, config, giftSetter, theaterSetter };
  };
  const initial = createStore(options);
  if (!options.storeMissing) w.__STORE__ = initial.api;
  const originalBody = d.body.innerHTML;
  const advance = ms => {
    const end = now + ms;
    while (true) {
      const entry = [...timers].filter(([, timer]) => timer.due <= end).sort((a, b) => a[1].due - b[1].due)[0];
      if (!entry) break;
      const [id, timer] = entry; now = timer.due;
      if (timer.repeat) timer.due += timer.delay; else timers.delete(id);
      timer.fn();
    }
    now = end;
  };
  // Invoke the script's trusted-key handler in this controlled fixture; jsdom cannot create trusted browser events.
  const keyHandlers = [], addEventListener = d.addEventListener.bind(d);
  d.addEventListener = (type, listener, options) => {
    if (type === 'keydown') keyHandlers.push(listener);
    return addEventListener(type, listener, options);
  };
  const userKey = (code, target = d.body, modifiers = {}) => {
    const event = { code, key: code === 'Escape' ? 'Escape' : code.slice(-1).toLowerCase(), isTrusted: true, target, shiftKey: false, ctrlKey: false, metaKey: false, altKey: false, ...modifiers };
    keyHandlers.forEach(listener => listener.call(d, event));
  };
  w.eval(script);
  return { w, d, root, initial, createStore, events, advance, userKey, originalBody, close: () => w.close() };
}

const tests = [], test = (name, run) => tests.push([name, run]);
const calls = (f, kind) => f.events.calls.filter(call => call.kind === kind);
function allOffAndHighest(store) {
  assert.equal(store.api.roomStore.danmakuSwitch, false);
  assert.equal(store.api.giftStore.effectSwitchStatus, false);
  assert.equal(store.quality.manulQuality, store.quality.qualities[0]);
  assert.equal(store.quality.isAbrOpen, false);
}

for (const url of [LIVE, FOLLOW]) test(`same-pass native API initialization ${url}`, () => {
  const f = fixture(url); f.advance(1000); allOffAndHighest(f.initial);
  const updates = ['danmaku', 'effects', 'quality', 'theater'].map(kind => calls(f, kind));
  assert.ok(updates.every(group => group.length === 1));
  assert.equal(new Set(updates.map(group => group[0].ms)).size, 1, 'all ready methods should execute in one pass');
  assert.equal(calls(f, 'danmaku')[0].value, false); assert.equal(calls(f, 'effects')[0].value, false);
  assert.equal(calls(f, 'theater')[0].value, true); assert.equal(f.initial.api.playerStore.isTheaterOn, true);
  assert.equal(f.initial.api.roomStore.foldChatroom, false); assert.equal(calls(f, 'foldChatroom').length, 1);
  assert.deepEqual(f.events.ui, []); assert.equal(f.d.body.innerHTML, f.originalBody);
  console.log('TIMING API', JSON.stringify(updates.map(group => ({ kind: group[0].kind, ms: group[0].ms }))));
  f.close();
});

test('already desired states are not written again', () => {
  const f = fixture(LIVE, { danmaku: false, effects: false, currentQuality: 'origin', theater: true, foldChatroom: false }); f.advance(2000);
  assert.equal(calls(f, 'danmaku').length, 0); assert.equal(calls(f, 'effects').length, 0); assert.equal(calls(f, 'quality').length, 0);
  assert.equal(calls(f, 'theater').length, 0); assert.equal(calls(f, 'foldChatroom').length, 0);
  f.close();
});

test('highest displayed quality with ABR still selects fixed highest quality', () => {
  const f = fixture(LIVE, { currentQuality: 'origin', abr: true }); f.advance(2000);
  assert.equal(calls(f, 'quality').length, 1); assert.equal(f.initial.quality.isAbrOpen, false); f.close();
});

test('late gift setter does not delay danmaku or quality', () => {
  const f = fixture(LIVE, { giftMissing: true }); const gift = f.initial.api.giftStore;
  f.advance(2000);
  assert.equal(f.initial.api.roomStore.danmakuSwitch, false); assert.equal(f.initial.quality.manulQuality, 'origin');
  assert.equal(gift.effectSwitchStatus, true); gift.setEffectSwitchStatus = f.initial.giftSetter; f.advance(1000);
  allOffAndHighest(f.initial); assert.equal(calls(f, 'danmaku').length, 1); assert.equal(calls(f, 'quality').length, 1); f.close();
});

test('late liveStream plugin does not delay either switch', () => {
  const f = fixture(LIVE, { pluginMissing: true }); f.advance(2000);
  assert.equal(f.initial.api.roomStore.danmakuSwitch, false); assert.equal(f.initial.api.giftStore.effectSwitchStatus, false);
  assert.equal(calls(f, 'quality').length, 0); f.initial.config.pluginMissing = false; f.advance(1000);
  allOffAndHighest(f.initial); f.close();
});

test('asynchronous quality completion is not requested repeatedly', () => {
  const f = fixture(LIVE, { qualityDelay: 4000 }); f.advance(3000);
  assert.equal(calls(f, 'quality').length, 1); assert.equal(f.initial.quality.manulQuality, 'hd');
  assert.equal(f.initial.api.roomStore.danmakuSwitch, false); assert.equal(f.initial.api.giftStore.effectSwitchStatus, false);
  f.advance(3000); allOffAndHighest(f.initial); assert.equal(calls(f, 'quality').length, 1); f.close();
});

for (const fail of ['throwDanmaku', 'throwEffects', 'throwQuality', 'throwTheater']) test(`${fail} does not prevent the other native calls`, () => {
  const f = fixture(LIVE, { [fail]: true }); f.advance(1000);
  if (fail !== 'throwDanmaku') assert.equal(f.initial.api.roomStore.danmakuSwitch, false);
  if (fail !== 'throwEffects') assert.equal(f.initial.api.giftStore.effectSwitchStatus, false);
  if (fail !== 'throwQuality') assert.equal(f.initial.quality.manulQuality, 'origin');
  if (fail !== 'throwTheater') assert.equal(f.initial.api.playerStore.isTheaterOn, true);
  f.advance(32000); const count = f.events.calls.length; f.advance(10000);
  assert.equal(f.events.calls.length, count, 'failed setup must stop within the initialization window'); f.close();
});

test('manual changes after successful initialization remain intact', () => {
  const f = fixture(); f.advance(1000);
  f.initial.api.roomStore.danmakuSwitch = true; f.initial.api.giftStore.effectSwitchStatus = true;
  f.initial.api.playerStore.isTheaterOn = false;
  f.initial.quality.manulQuality = 'sd'; f.initial.quality.realCurrentQuality = 'sd'; f.advance(40000);
  assert.equal(f.initial.api.roomStore.danmakuSwitch, true); assert.equal(f.initial.api.giftStore.effectSwitchStatus, true);
  assert.equal(f.initial.quality.manulQuality, 'sd'); assert.equal(calls(f, 'quality').length, 1);
  assert.equal(f.initial.api.playerStore.isTheaterOn, false); assert.equal(calls(f, 'theater').length, 1); f.close();
});

test('non-live route stays unchanged and SPA entry initializes', () => {
  const f = fixture('https://www.douyin.com/follow'); f.advance(2000);
  assert.equal(f.events.calls.length, 0); f.w.history.pushState({}, '', '/follow/live/111111'); f.advance(1000);
  allOffAndHighest(f.initial); assert.equal(f.initial.api.playerStore.isTheaterOn, true); f.close();
});

test('URL ahead of store waits for matching web_rid without touching old room', () => {
  const f = fixture(FOLLOW); f.advance(1000);
  f.initial.api.roomStore.danmakuSwitch = true; f.initial.api.giftStore.effectSwitchStatus = true;
  f.initial.api.playerStore.isTheaterOn = false;
  f.initial.quality.manulQuality = 'sd'; const count = f.events.calls.length;
  f.w.history.pushState({}, '', '/follow/live/222222'); f.advance(2000);
  assert.equal(f.events.calls.length, count); assert.equal(f.initial.api.roomStore.danmakuSwitch, true);
  const next = f.createStore({ webId: '222222', roomId: '9002' }); f.w.__STORE__ = next.api; f.advance(1000);
  allOffAndHighest(next); assert.equal(f.initial.api.giftStore.effectSwitchStatus, true);
  assert.equal(f.initial.api.playerStore.isTheaterOn, false); assert.equal(next.api.playerStore.isTheaterOn, true); f.close();
});

test('room ID change in the same store resets completed features', () => {
  const f = fixture(); f.advance(1000);
  f.initial.api.roomStore.roomInfo.roomId = '9002';
  f.initial.api.roomStore.danmakuSwitch = true; f.initial.api.giftStore.effectSwitchStatus = true;
  f.initial.api.playerStore.isTheaterOn = false;
  f.initial.quality.manulQuality = 'sd'; f.initial.quality.realCurrentQuality = 'sd'; f.advance(1000);
  allOffAndHighest(f.initial);
  assert.equal(calls(f, 'danmaku').length, 2); assert.equal(calls(f, 'effects').length, 2); assert.equal(calls(f, 'quality').length, 2);
  assert.equal(f.initial.api.playerStore.isTheaterOn, true); assert.equal(calls(f, 'theater').length, 2); f.close();
});

test('old disconnected player cannot receive a quality change for the new room', () => {
  const f = fixture(FOLLOW); f.advance(1000);
  const oldRoot = f.root; oldRoot.remove();
  const newRoot = f.d.createElement('div'); newRoot.setAttribute('data-anchor-id', 'living-basic-player');
  f.d.querySelector('#PlayerLayout').append(newRoot);
  const next = f.createStore({ webId: '222222', roomId: '9002', playerRoot: oldRoot });
  f.w.history.pushState({}, '', '/follow/live/222222'); f.w.__STORE__ = next.api;
  const count = calls(f, 'quality').length; f.advance(1000); assert.equal(calls(f, 'quality').length, count);
  next.api.playerStore.playerInstance.root = newRoot; f.advance(1000); allOffAndHighest(next); f.close();
});

test('qualityLimit excludes its threshold and every higher tier', () => {
  const f = fixture(LIVE, { qualities: ['origin', 'fhd', 'uhd', 'hd', 'sd', 'ld'], qualityLimit: 'fhd' }); f.advance(1000);
  assert.equal(calls(f, 'quality').length, 1); assert.equal(calls(f, 'quality')[0].key, 'uhd');
  assert.equal(f.initial.quality.manulQuality, 'uhd'); f.close();
});

test('origin-mapped duplicate is removed before choosing the highest allowed tier', () => {
  const f = fixture(LIVE, { qualities: ['origin', 'fhd', 'uhd', 'hd', 'sd', 'ld'], qualityLimit: 'fhd', currentQuality: 'sd', realQualityMap: { origin: 'uhd' } }); f.advance(1000);
  assert.equal(calls(f, 'quality').length, 1); assert.equal(calls(f, 'quality')[0].key, 'hd'); f.close();
});

test('real quality mapping is respected when confirming the selected tier', () => {
  const f = fixture(LIVE, { realQualityMap: { origin: 'uhd' } }); f.advance(3000);
  assert.equal(f.initial.quality.manulQuality, 'origin'); assert.equal(f.initial.quality.realCurrentQuality, 'uhd');
  assert.equal(calls(f, 'quality').length, 1); f.close();
});

test('unknown qualityLimit never guesses an allowed quality', () => {
  const f = fixture(LIVE, { qualityLimit: 'unknown-tier' }); f.advance(35000);
  assert.equal(calls(f, 'quality').length, 0);
  assert.equal(f.initial.api.roomStore.danmakuSwitch, false); assert.equal(f.initial.api.giftStore.effectSwitchStatus, false); f.close();
});

test('failed quality callback is not retried indefinitely or treated as success', () => {
  const f = fixture(LIVE, { rejectQuality: true }); f.advance(35000);
  assert.equal(calls(f, 'quality').length, 1); assert.equal(f.initial.quality.manulQuality, 'hd');
  assert.equal(f.initial.api.roomStore.danmakuSwitch, false); assert.equal(f.initial.api.giftStore.effectSwitchStatus, false);
  assert.ok(f.events.warnings.length > 0); f.close();
});

test('quality timeout is cleared when entering the next room', () => {
  const f = fixture(FOLLOW, { qualityNeverCompletes: true }); f.advance(35000);
  assert.equal(calls(f, 'quality').length, 1);
  f.w.history.pushState({}, '', '/follow/live/222222');
  const next = f.createStore({ webId: '222222', roomId: '9002' }); f.w.__STORE__ = next.api; f.advance(1000);
  allOffAndHighest(next); assert.equal(calls(f, 'quality').length, 2); f.close();
});

test('late theater API leaves the other three settings independent', () => {
  const f = fixture(LIVE, { theaterMissing: true }); f.advance(2000); allOffAndHighest(f.initial);
  assert.equal(calls(f, 'theater').length, 0); assert.equal(f.initial.api.playerStore.isTheaterOn, false);
  f.initial.api.playerStore.setIsTheaterOn = f.initial.theaterSetter; f.advance(1000);
  assert.equal(f.initial.api.playerStore.isTheaterOn, true); assert.equal(calls(f, 'theater').length, 1);
  assert.equal(calls(f, 'danmaku').length, 1); assert.equal(calls(f, 'effects').length, 1); assert.equal(calls(f, 'quality').length, 1); f.close();
});

test('asynchronous theater state is read back without repeated requests', () => {
  const f = fixture(LIVE, { theaterDelay: 4000 }); f.advance(2000); allOffAndHighest(f.initial);
  assert.equal(f.initial.api.playerStore.isTheaterOn, false); assert.equal(calls(f, 'theater').length, 1);
  assert.equal(f.events.infos.length, 0, 'pending theater state must not be reported as completed');
  f.advance(4000); assert.equal(f.initial.api.playerStore.isTheaterOn, true);
  assert.equal(calls(f, 'theater').length, 1); assert.equal(f.events.warnings.length, 0); f.close();
});

test('unconfirmed theater state times out and a new room initializes again', () => {
  const f = fixture(FOLLOW, { theaterNeverCompletes: true }); f.advance(35000); allOffAndHighest(f.initial);
  assert.equal(calls(f, 'theater').length, 1); assert.equal(f.initial.api.playerStore.isTheaterOn, false);
  assert.equal(f.events.infos.length, 0); assert.ok(f.events.warnings.length > 0);
  f.w.history.pushState({}, '', '/follow/live/222222');
  const next = f.createStore({ webId: '222222', roomId: '9002' }); f.w.__STORE__ = next.api; f.advance(1000);
  assert.equal(next.api.playerStore.isTheaterOn, true); assert.equal(calls(f, 'theater').length, 2); f.close();
});

test('paid live fullscreen permission is respected without blocking other settings', () => {
  const f = fixture(LIVE, { paidLive: true, fullscreenAuth: 0 }); f.advance(35000); allOffAndHighest(f.initial);
  assert.equal(calls(f, 'theater').length, 0); assert.equal(calls(f, 'foldChatroom').length, 0);
  assert.equal(f.initial.api.playerStore.isTheaterOn, false);
  assert.ok(f.events.infos.every(args => !args.join(' ').includes('原生设置已完成'))); f.close();
  for (const config of [{ paidLive: true, fullscreenAuth: 1 }, { paidLive: false, fullscreenAuth: 0 }]) {
    const allowed = fixture(LIVE, config); allowed.advance(1000);
    assert.equal(allowed.initial.api.playerStore.isTheaterOn, true); assert.equal(calls(allowed, 'theater').length, 1); allowed.close();
  }
});

test('unknown paid live permission waits before entering theater mode', () => {
  const f = fixture(LIVE, { paidLive: true }); delete f.initial.api.paidLiveStore.paidLiveAuth.FullScreen;
  f.advance(2000); allOffAndHighest(f.initial); assert.equal(calls(f, 'theater').length, 0);
  f.initial.api.paidLiveStore.paidLiveAuth.FullScreen = 1; f.advance(1000);
  assert.equal(f.initial.api.playerStore.isTheaterOn, true); assert.equal(calls(f, 'theater').length, 1); f.close();
});

for (const mode of ['browserFullscreen', 'playerFullscreen', 'pictureInPicture']) test(`${mode} is preserved without forcing theater mode afterward`, () => {
  const f = fixture(LIVE, { [mode]: true }); f.advance(1000); allOffAndHighest(f.initial);
  assert.equal(calls(f, 'theater').length, 0); assert.equal(calls(f, 'foldChatroom').length, 0);
  f.d.fullscreenElement = null; f.d.pictureInPictureElement = null; f.initial.api.playerStore.isFullScreen = false;
  f.advance(35000); assert.equal(f.initial.api.playerStore.isTheaterOn, false);
  assert.equal(calls(f, 'theater').length, 0);
  assert.ok(f.events.infos.every(args => !args.join(' ').includes('原生设置已完成'))); f.close();
});

test('manual KeyY cancels pending automatic theater mode', () => {
  const f = fixture(LIVE, { theaterMissing: true }); f.advance(1000); f.userKey('KeyY');
  f.initial.api.playerStore.setIsTheaterOn = f.initial.theaterSetter; f.advance(35000);
  allOffAndHighest(f.initial); assert.equal(calls(f, 'theater').length, 0);
  assert.equal(f.initial.api.playerStore.isTheaterOn, false); f.close();
});

test('Escape outside theater mode does not cancel pending automatic entry', () => {
  const f = fixture(LIVE, { theaterMissing: true }); f.advance(1000); f.userKey('Escape');
  f.initial.api.playerStore.setIsTheaterOn = f.initial.theaterSetter; f.advance(1000);
  assert.equal(f.initial.api.playerStore.isTheaterOn, true); assert.equal(calls(f, 'theater').length, 1); f.close();
});

test('Escape leaving active theater mode prevents pending automatic reentry', () => {
  const f = fixture(LIVE, { theaterMissing: true }); f.advance(1000);
  f.initial.api.playerStore.isTheaterOn = true;
  f.userKey('Escape'); // Capture handler runs before the native Escape handler exits theater mode.
  f.initial.api.playerStore.isTheaterOn = false;
  f.initial.api.playerStore.setIsTheaterOn = f.initial.theaterSetter; f.advance(35000);
  allOffAndHighest(f.initial); assert.equal(calls(f, 'theater').length, 0);
  assert.equal(f.initial.api.playerStore.isTheaterOn, false); f.close();
});

test('typing Y or using modified Y does not cancel automatic theater mode', () => {
  for (const typing of [true, false]) {
    const f = fixture(LIVE, { theaterMissing: true }); f.advance(1000);
    const input = f.d.createElement('input'); f.d.body.append(input);
    f.userKey('KeyY', typing ? input : f.d.body, typing ? {} : { ctrlKey: true });
    f.initial.api.playerStore.setIsTheaterOn = f.initial.theaterSetter; f.advance(1000);
    assert.equal(f.initial.api.playerStore.isTheaterOn, true); assert.equal(calls(f, 'theater').length, 1); f.close();
  }
});

let failed = 0;
for (const [name, run] of tests) {
  try { run(); console.log(`PASS ${name}`); }
  catch (error) { failed++; console.error(`FAIL ${name}: ${error.message}`); }
}
console.log(`${tests.length - failed}/${tests.length} passed (controlled native API fixture; not live-site verification)`);
process.exitCode = failed ? 1 : 0;
