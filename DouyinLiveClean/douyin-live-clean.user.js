// ==UserScript==
// @name         抖音直播自动关闭弹幕和礼物特效
// @namespace    local.douyin.live-clean
// @version      1.4.1
// @description  直接调用网页原生方法，关闭弹幕、屏蔽礼物特效、选择最高可用画质并进入网页全屏。
// @updateURL    https://raw.githubusercontent.com/primabrucexu/MiniTools/main/DouyinLiveClean/douyin-live-clean.user.js
// @downloadURL  https://raw.githubusercontent.com/primabrucexu/MiniTools/main/DouyinLiveClean/douyin-live-clean.user.js
// @match        https://live.douyin.com/*
// @match        https://www.douyin.com/*
// @run-at       document-idle
// @grant        none
// @noframes
// @license      GPL-3.0-only
// ==/UserScript==

// 旧版控件定位适配自 Frequenk《网页抖音体验增强》v5.8（GPL-3.0）。
// https://greasyfork.org/zh-CN/scripts/539942/code
// 本版依据抖音当前客户端的 RootStore、DanmaSetting、EffectSwitchPlugin
// 和 QualitySwitchPluginV2、TheaterPlugin 改用原生 JS 方法，不模拟鼠标或键盘事件。
// GPL-3.0 许可全文见随附 COPYING.txt。

(() => {
    'use strict';

    const ROOTS = '[data-anchor-id="living-basic-player"], [data-e2e="living-container"]';
    const QUALITY_ORDER = ['origin', 'fhd', 'uhd', 'hd', 'sd', 'ld'];
    const NAMES = { danmaku: '关闭弹幕', effects: '屏蔽礼物特效', quality: '最高可用画质', theater: '网页全屏' };
    let room = null;

    function roomKey() {
        const path = location.pathname.replace(/\/+$/, '');
        if (location.hostname === 'live.douyin.com' && path) return location.origin + path;
        if (location.hostname === 'www.douyin.com' && /^\/follow\/live(?:\/|$)/.test(path)) return location.origin + path;
        return '';
    }

    function visible(element) {
        if (!element?.isConnected) return false;
        const rect = element.getBoundingClientRect();
        const style = getComputedStyle(element);
        return rect.width > 0 && rect.height > 0 && style.display !== 'none' && style.visibility !== 'hidden';
    }

    function run(task, action) {
        if (task.done || task.failed || task.skipped) return;
        try {
            task.done = action(task) === true;
        } catch (error) {
            task.failed = true;
            console.warn('[抖音直播清爽] ' + NAMES[task.name] + '未完成：', error);
        }
    }

    function disableDanmaku(task, store, player) {
        const state = store?.roomStore;
        if (typeof state?.setDanmakuSwitch !== 'function' || typeof state.danmakuSwitch !== 'boolean') return false;
        if (!task.sent) {
            // 原生关闭回调也停止旧版 danmu 插件；新版 Canvas 组件订阅下方状态。
            player?.plugins?.danmu?.stop?.();
            task.sent = true;
            if (state.danmakuSwitch) state.setDanmakuSwitch(false);
        }
        return state.danmakuSwitch === false;
    }

    function disableEffects(task, store) {
        const state = store?.giftStore;
        if (typeof state?.setEffectSwitchStatus !== 'function' || typeof state.effectSwitchStatus !== 'boolean') return false;
        if (!task.sent) {
            task.sent = true;
            // 内部 false 表示特效关闭，界面的“屏蔽礼物特效”会显示为开启。
            if (state.effectSwitchStatus) state.setEffectSwitchStatus(false);
        }
        return state.effectSwitchStatus === false;
    }

    function highestQuality(task, player) {
        const stream = player?.getPlugin?.('liveStream');
        if (typeof stream?.getQualityInfo !== 'function' || typeof stream.handleChangeQuality !== 'function') return false;
        const info = stream.getQualityInfo();
        if (!Array.isArray(info?.qualities) || !info.qualities.length) return false;
        // 与原生菜单一致：去掉“原画”对应的重复档位，跳过要求登录的档位。
        const duplicate = info.qualities.includes('origin') && info.realQualityMap?.origin;
        const limit = stream._config?.qualityLimit;
        if (limit && !QUALITY_ORDER.includes(limit)) return false;
        const best = QUALITY_ORDER.find(key => info.qualities.includes(key)
            && (key !== duplicate || key === 'origin')
            && (!limit || QUALITY_ORDER.indexOf(key) > QUALITY_ORDER.indexOf(limit)));
        if (!best) return false;
        const current = info.manulQuality || info.realCurrentQuality;
        const expected = info.realQualityMap?.[best] || best;
        const selected = info.isAbrOpen === false && (current === best || current === expected)
            && (info.realCurrentQuality === best || info.realCurrentQuality === expected);
        if (!task.sent && selected) return true;
        if (!task.sent) {
            task.sent = true;
            stream.handleChangeQuality(best, result => {
                if (task.done || task.failed) return;
                task.confirmed = result?.success === true;
                if (result?.success === false) {
                    task.failed = true;
                    console.warn('[抖音直播清爽] 原生播放器报告画质切换失败。');
                }
            });
        }
        // 等原生切换回调和状态回读，不能仅凭调用方法就宣称切换成功。
        return task.confirmed === true && selected;
    }

    function enterWebFullscreen(task, store, player) {
        const state = store?.playerStore;
        const chat = store?.roomStore;
        const paid = store?.paidLiveStore;
        if (state?.isFullScreen || player?.fullscreen || document.fullscreenElement || document.pictureInPictureElement) {
            task.skipped = true;
            console.info('[抖音直播清爽] 保留当前全屏或画中画模式，跳过网页全屏。');
            return false;
        }
        if (state?.isTheaterOn === true) return true;
        if (typeof state?.setIsTheaterOn !== 'function' || typeof state.isTheaterOn !== 'boolean'
            || typeof chat?.setFoldChatroom !== 'function' || typeof paid?.isPaidLive !== 'boolean') return false;
        if (paid.isPaidLive) {
            if (typeof paid.paidLiveAuth?.FullScreen !== 'number') return false;
            if (paid.paidLiveAuth.FullScreen === 0) {
                task.skipped = true;
                console.info('[抖音直播清爽] 当前房间未开放全屏权限，跳过网页全屏。');
                return false;
            }
        }
        if (!task.sent) {
            task.sent = true;
            // 与原生“网页全屏”按钮一致，切换布局并展开聊天区。
            state.setIsTheaterOn(true);
            chat.setFoldChatroom(false);
        }
        return state.isTheaterOn === true;
    }

    function tick() {
        const key = roomKey();
        const root = key && [...document.querySelectorAll(ROOTS)].find(visible);
        if (!root) {
            room = null;
            return;
        }
        if (document.hidden) return;
        const store = window.__STORE__;
        const player = store?.playerStore?.playerInstance;
        const info = store?.roomStore?.roomInfo;
        const roomId = info?.roomId || info?.room?.id_str;
        if (!room || room.key !== key || room.root !== root || room.store !== store || room.player !== player || room.roomId !== roomId) {
            room = {
                key, root, store, player, roomId, ticks: 0, stopped: false,
                tasks: Object.fromEntries(Object.keys(NAMES).map(name => [name, { name, done: false, failed: false, sent: false }])),
            };
        }
        if (room.stopped) return;
        if (++room.ticks > 120) {
            room.stopped = true;
            const missing = Object.values(room.tasks).filter(task => !task.done && !task.skipped).map(task => NAMES[task.name]);
            console.warn('[抖音直播清爽] 等待原生接口或结果超时：' + missing.join('、'));
            return;
        }
        // SPA 导航可能先改 URL，后替换 store；不要把旧房间的成功记到新房间。
        const webRid = location.pathname.replace(/\/+$/, '').split('/').at(-1);
        if (!roomId || (info.web_rid && String(info.web_rid) !== webRid)) return;
        const activePlayer = player?.root?.isConnected && (root.contains(player.root) || player.root.contains(root)) ? player : null;
        run(room.tasks.danmaku, task => disableDanmaku(task, store, activePlayer));
        run(room.tasks.effects, task => disableEffects(task, store));
        run(room.tasks.quality, task => highestQuality(task, activePlayer));
        run(room.tasks.theater, task => enterWebFullscreen(task, store, activePlayer));
        if (Object.values(room.tasks).every(task => task.done || task.failed || task.skipped)) {
            room.stopped = true;
            if (Object.values(room.tasks).every(task => task.done)) console.info('[抖音直播清爽] 四项原生设置已完成。');
        }
    }

    // 初始化期间若用户主动操作某项，该项也立即让出控制；完成后不会反复改回。
    document.addEventListener('pointerdown', event => {
        if (!event.isTrusted || !room || room.key !== roomKey() || !(event.target instanceof Element) || !room.root.contains(event.target)) return;
        if (event.target.closest('.QualitySwitchNewPlugin, [data-e2e="quality"], [data-e2e="quality-selector"]')) room.tasks.quality.done = true;
        for (const [name, selector] of [['danmaku', '[data-e2e="danmaku-setting-icon"]'], ['effects', '[data-e2e="gift-setting"]']]) {
            if (room.root.querySelector(selector)?.parentElement.contains(event.target)) room.tasks[name].done = true;
        }
    }, true);
    document.addEventListener('keydown', event => {
        if (!event.isTrusted || room?.key !== roomKey() || event.target.closest?.('input, textarea, [contenteditable]')) return;
        if (event.code === 'KeyB') room.tasks.danmaku.done = true;
        const theaterKey = event.code === 'KeyY' || (event.code === 'Escape' && room.store?.playerStore?.isTheaterOn === true);
        if (!event.shiftKey && !event.metaKey && !event.ctrlKey && theaterKey) room.tasks.theater.skipped = true;
    }, true);

    // 四项各自等待接口就绪，同一轮发起；不再串行打开菜单或固定等待 1.5 秒。
    setInterval(tick, 250);
})();

