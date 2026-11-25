// VK shim for the island's own WebView2.
// VK never calls navigator.mediaSession, so Windows (and the island) can't see what it plays.
// We catch the AudioPlayer instance by hooking its prototype and report metadata/controls to mediaSession.
// It also hides the feed ads and mutes the ad inserts the service splices into the queue.
(function () {
    'use strict';
    if (window.__VKMS_WEBVIEW__) return;
    window.__VKMS_WEBVIEW__ = true;

    var TAG = '[vk-island]';
    var player = null;
    var lastKey = '';
    var handlersSet = false;
    var inUpdate = false;
    var hooked = false;
    var needTimeline = true;   // send setPositionState on a new track / after a seek
    var lastSentPos = -1;      // the last position sent to the system
    var posSource = '';
    // Manual position correction in seconds (when the sound lags behind VK's report): put your copy of
    // vk.js into %LOCALAPPDATA%\DynamicIsland\music-extras and set the number here, e.g. 12.
    var POS_BIAS = 0;
    var posTicks = 0;
    var stuckTicks = 0, stuckPos = -1;
    var chosenSrc = '';        // the position source picked for the current track

    var lastAudio = null;

    (function hookAudioConstructor() {
        try {
            var Orig = window.Audio;
            if (!Orig || Orig.__vkiAudio) return;
            var Hooked = function () {
                var el = new (Function.prototype.bind.apply(Orig, [null].concat(Array.prototype.slice.call(arguments))))();
                try { lastAudio = el; } catch (e) {}
                return el;
            };
            Hooked.__vkiAudio = true;
            Hooked.prototype = Orig.prototype;
            window.Audio = Hooked;
        } catch (e) {}
    })();

    (function hookMediaElement() {
        try {
            var play = window.HTMLMediaElement && HTMLMediaElement.prototype.play;
            if (play && !play.__vki) {
                var W = function () {
                    try { if (this && this.tagName === 'AUDIO') lastAudio = this; } catch (e) {}
                    return play.apply(this, arguments);
                };
                W.__vki = true;
                HTMLMediaElement.prototype.play = W;
            }
            var create = document.createElement;
            document.createElement = function (tag) {
                var el = create.apply(document, arguments);
                try { if (String(tag).toLowerCase() === 'audio') lastAudio = el; } catch (e) {}
                return el;
            };
        } catch (e) {}
    })();

    // A live element only: VK also keeps empty ones in memory (paused, readyState 0, no src).
    function usable(el) {
        return !!el && !isNaN(el.currentTime) && el.readyState > 0 && el.currentTime > 0 && !el.paused;
    }

    var elCache = { el: null, at: 0 };

    function audioEl() {
        try {
            var now = Date.now();
            if (elCache.el && now - elCache.at < 900 && usable(elCache.el)) return elCache.el;
            if (usable(lastAudio)) { elCache.el = lastAudio; elCache.at = now; return lastAudio; }
            var all = document.querySelectorAll('audio');
            for (var i = 0; i < all.length; i++) {
                if (usable(all[i])) { elCache.el = all[i]; elCache.at = now; return all[i]; }
            }
        } catch (e) {}
        return null;
    }

    function readAudioTime() {
        var el = audioEl();
        return el ? (Number(el.currentTime) || 0) : -1;
    }

    function readListened() {
        try {
            if (typeof player.getListenedTime === 'function') {
                var l = Number(player.getListenedTime()) || 0;
                if (l > 0) return Math.max(0, l - POS_BIAS);   // seconds
            }
        } catch (e) {}
        return -1;
    }

    function readProgTime() {
        try {
            if (typeof player.getCurrentProgressTime === 'function') {
                var t2 = Number(player.getCurrentProgressTime()) || 0;
                if (t2 > 1000) return t2 / 1000;               // milliseconds
            }
        } catch (e) {}
        return -1;
    }

    // The position source is picked ONCE per track and kept while it lives: switching between the
    // <audio> element and VK's own counter is what produced jumps and a frozen clock.
    function positionNow() {
        var v;
        if (chosenSrc === 'audio') { v = readAudioTime(); if (v >= 0) { posSource = 'audio'; return v; } chosenSrc = ''; }
        if (chosenSrc === 'progTime') { v = readProgTime(); if (v >= 0) { posSource = 'progTime/1000'; return v; } chosenSrc = ''; }
        if (chosenSrc === 'listened') { v = readListened(); if (v >= 0) { posSource = 'listened'; return v; } chosenSrc = ''; }

        v = readAudioTime(); if (v >= 0) { chosenSrc = 'audio'; posSource = 'audio'; return v; }
        // progTime knows about seeking, listened doesn't (it keeps counting and lies after a seek)
        v = readProgTime(); if (v >= 0) { chosenSrc = 'progTime'; posSource = 'progTime/1000'; return v; }
        v = readListened(); if (v >= 0) { chosenSrc = 'listened'; posSource = 'listened'; return v; }
        posSource = 'none';
        return -1;
    }

    function durationNow() {
        var el = audioEl();
        if (el && isFinite(el.duration) && el.duration > 0) return el.duration;
        try { if (typeof player.getCurrentDuration === 'function') return Number(player.getCurrentDuration()) || 0; } catch (e) {}
        return 0;
    }
    var logged = {};

    function log(msg, obj) {
        if (logged[msg]) return;
        logged[msg] = true;
        try { if (obj !== undefined) console.log(TAG, msg, obj); else console.log(TAG, msg); } catch (e) {}
    }

    function html(s) {
        if (!s) return '';
        var ta = document.createElement('textarea');
        ta.innerHTML = String(s);
        return ta.value;
    }

    // ---------------- catching the player instance ----------------
    function wrap(proto, name, refresh) {
        var orig = proto[name];
        if (typeof orig !== 'function' || orig.__vki) return 0;
        var wrapper = function () {
            player = this;
            if (refresh) { try { update(); } catch (e) {} }
            return orig.apply(this, arguments);
        };
        wrapper.__vki = true;
        proto[name] = wrapper;
        return 1;
    }

    function grab(proto) {
        if (hooked) return 0;
        // actions: refresh the metadata after them
        ['play', 'playAudio', 'playAudioItem', 'playList', 'playPlaylist', 'toggleAudio',
         'toggleCurrentAudioRow', 'resume', 'pause', 'playNext', 'playPrev', 'seek', 'seekToTime',
         'setCurrentAudio'].forEach(function (n) { wrap(proto, n, true); });
        // getters: only catch `this`, otherwise update() recurses into itself
        ['getCurrentAudio', 'getCurrentAudioData', 'isPlaying', 'updateCurrentPlaying']
            .forEach(function (n) { wrap(proto, n, false); });
        hooked = true;
        log('hooked VK AudioPlayer');
        return 1;
    }

    function findPlayer() {
        if (window.AudioPlayer && window.AudioPlayer.prototype) { grab(window.AudioPlayer.prototype); return true; }
        if (typeof window.getAudioPlayer === 'function') {
            try { player = window.getAudioPlayer(); } catch (e) {}
            if (player) { log('legacy getAudioPlayer'); return true; }
        }
        return false;
    }

    // ---------------- the track's metadata ----------------
    function names(list) {
        return list.map(function (x) { return x && (x.name || x.title) || x; })
                   .filter(function (x) { return typeof x === 'string' && x; });
    }

    function coverSources(cover) {
        var out = [];
        function add(x) {
            if (!x) return;
            if (typeof x === 'string') {
                x.split(',').forEach(function (s) { s = s.trim(); if (s) out.push(s); });
            } else if (Array.isArray(x)) {
                x.forEach(add);
            } else if (typeof x === 'object') {
                add(x.url || x.src || x.photo_300 || x.photo_600 || x.photo_1200 || x.coverUrl || '');
            }
        }
        add(cover);
        return out;
    }

    // The cover is taken from the page as is: VK signs its URL (sign=) for the exact size, so the
    // size parameter can't be swapped for a bigger one — the image answers 403.
    function domCover() {
        try {
            var sels = [
                'img[alt="Обложка аудиозаписи"]',
                'img[alt*="Обложка"]',
                'img[alt*="Audio cover"]',
                '[data-testid*="AudioPlayer"] img',
                '[class*="AudioPlayer"] img',
                '.audio_cover img',
                '.top_audio_player_cover img'
            ];
            for (var i = 0; i < sels.length; i++) {
                var img = document.querySelector(sels[i]);
                var src = img && (img.currentSrc || img.src);
                if (src && /^https?:/i.test(src)) return String(src);
            }
            // last try: any audio cover image (VK serves those with type=audio)
            var all = document.querySelectorAll('img');
            for (var j = 0; j < all.length; j++) {
                var s2 = all[j].currentSrc || all[j].src;
                if (s2 && /vkuserphoto|userapi|sun9-/i.test(s2) && /type=audio/i.test(s2)) return String(s2);
            }
        } catch (e) {}
        return '';
    }

    function track() {
        if (!player) return null;
        var a = null;
        try {
            if (typeof player.getCurrentAudioData === 'function') a = player.getCurrentAudioData();
            if (!a && typeof player.getCurrentAudio === 'function') a = player.getCurrentAudio();
        } catch (e) { return null; }
        if (!a) return null;

        var title, artist, cover, duration;
        if (Array.isArray(a)) {
            var U = window.AudioUtils || {};
            var idx = function (k, d) { var v = U[k]; return typeof v === 'number' ? v : d; };
            title = a[idx('AUDIO_ITEM_INDEX_TITLE', 3)];
            artist = a[idx('AUDIO_ITEM_INDEX_PERFORMER', 4)];
            duration = Number(a[idx('AUDIO_ITEM_INDEX_DURATION', 5)]) || 0;
            cover = a[idx('AUDIO_ITEM_INDEX_COVER_URL', 14)];
        } else {
            title = a.title || a.name;
            var au = a.author || {};
            artist = au.raw
                || (Array.isArray(au.main) ? names(au.main).join(', ') : '')
                || (Array.isArray(au.feat) ? names(au.feat).join(', ') : '')
                || a.performer || a.artist || a.subtitle || '';
            duration = Number(a.duration) || 0;
            cover = a.artwork || a.coverUrl || a.cover_url || a.thumb || a.cover;
        }

        title = html(title);
        artist = html(artist);
        if (!title && !artist) return null;

        var covers = [];
        coverSources(cover).forEach(function (src) {
            covers.push({ src: src, sizes: '300x300', type: 'image/jpeg' });
        });
        if (covers.length === 0) {
            var dom = domCover();
            if (dom) covers.push({ src: dom, sizes: '300x300', type: 'image/jpeg' });
        }
        var album = '';
        if (!Array.isArray(a)) album = (a.album && (a.album.title || a.album.name)) || '';
        return { title: title || 'Unknown', artist: artist || 'VK', cover: covers, duration: duration, album: album, raw: a };
    }

    function playing() {
        if (!player) return false;
        try {
            if (typeof player.isPlaying === 'function') return !!player.isPlaying();
            if (typeof player.isPaused === 'function') return !player.isPaused();
        } catch (e) {}
        return false;
    }

    function setHandlers(p) {
        if (handlersSet || !navigator.mediaSession) return;
        handlersSet = true;
        var ms = navigator.mediaSession;
        var h = {
            play: function () { try { p.play(); } catch (e) {} },
            pause: function () { try { p.pause(); } catch (e) {} },
            previoustrack: function () { try { p.playPrev(); } catch (e) {} },
            nexttrack: function () { try { p.playNext(); } catch (e) {} }
        };
        Object.keys(h).forEach(function (k) { try { ms.setActionHandler(k, h[k]); } catch (e) {} });
        try {
            ms.setActionHandler('seekto', function (d) {
                if (!d || typeof d.seekTime !== 'number') return;
                try { p.seekToTime(d.seekTime); } catch (e) {}
                // after a seek the old sources lie: drop the choice and report the target position at once
                chosenSrc = '';
                needTimeline = true;
                lastSentPos = -1;
                stuckTicks = 0;
                try {
                    var dd = durationNow();
                    if (dd > 0) {
                        navigator.mediaSession.setPositionState({
                            duration: dd, playbackRate: 1,
                            position: Math.min(Math.max(d.seekTime, 0), dd)
                        });
                        report('seek -> ' + d.seekTime.toFixed(1));
                    }
                } catch (e) {}
            });
        } catch (e) {}
        log('mediaSession handlers set');
    }

    // ---------------- ad inserts ----------------
    // VK splices an ad into the queue as a track of its own. DTO hints: ads / type promo|ad / adStart.
    // When one starts: mute, jump to the next track, then bring the volume back.
    var adSkipped = {};
    var volumeBeforeAd = -1;

    function looksLikeAd(a, t) {
        // by metadata: VK labels inserts «Реклама»
        if (t) {
            var text = String(t.title || '') + ' ' + String(t.artist || '');
            if (/реклам|advert/i.test(text)) return true;   // empty fields are NOT a hint: VK sends them between tracks
        }
        if (!a) return false;
        if (Array.isArray(a)) {
            var U = window.AudioUtils || {};
            var adsIdx = typeof U.AUDIO_ITEM_INDEX_ADS === 'number' ? U.AUDIO_ITEM_INDEX_ADS : -1;
            if (adsIdx >= 0 && a[adsIdx]) return true;
            return false;
        }
        if (a.ads) return true;
        if (a.adStart || a.adstart || a.adSource) return true;
        if (a.isPromoted || a.promoted) return true;
        var type = String(a.type || '').toLowerCase();
        return type.indexOf('ad') >= 0 || type.indexOf('promo') >= 0;
    }

    var adLastAt = 0, adStreak = 0, adBlockedUntil = 0, adLastAtStreakReset = 0;

    function skipAd(info) {
        var now = Date.now();
        if (now < adBlockedUntil) return;
        if (now - adLastAt < 4000) return;                       // too often: leave it alone
        adLastAt = now;
        if (now - adLastAtStreakReset > 60000) { adStreak = 0; adLastAtStreakReset = now; }
        if (++adStreak > 3) { adBlockedUntil = now + 60000; report('ad skipping paused for a minute'); return; }
        try { if (volumeBeforeAd < 0) volumeBeforeAd = Number(player.getVolume ? player.getVolume() : 1); player.setVolume && player.setVolume(0); } catch (e) {}
        report('AD: ' + info + ' -> next');
        setTimeout(function () {
            try { player.playNext(); } catch (e) {}
            setTimeout(function () {
                try { if (volumeBeforeAd >= 0 && player.setVolume) player.setVolume(volumeBeforeAd); } catch (e) {}
                volumeBeforeAd = -1;
            }, 700);
        }, 500);
    }

    // The same by markup: during an insert VK shows «Реклама» in the player bar.
    // Catch it, mute, jump to the next track and put the evidence into the log.
    var adDom = false;
    var adSeeks = 0;

    function adMarker() {
        try {
            var bars = document.querySelectorAll('[class*="AudioPlayer"], [data-testid*="AudioPlayer"], [class*="TopAudioPlayer"], [class*="audio_player"]');
            for (var i = 0; i < bars.length; i++) {
                var el = bars[i];
                if (el.offsetParent === null) continue;
                var txt = (el.innerText || '').slice(0, 400);
                if (/реклам|advert/i.test(txt)) return { text: txt };   // «Реклама»
            }
        } catch (e) {}
        return null;
    }

    // An insert plays as a separate file from ad hosts (r.mradx.net in the log) while the metadata
    // still belongs to the track — so it is caught by the host of the audio source.
    var AD_SRC = /mradx\.net|ad\.mail\.ru|ads\.vk\.com|advert|doubleclick/i;
    var VK_AUDIO_HOST = /vkuseraudio|vkuserphoto|userapi|vk\.com|vk\.ru|blob:/i;

    function adBySrc() {
        var el = audioEl();
        var src = el ? String(el.src || '') : '';
        if (!src || el.readyState === 0) return '';
        if (AD_SRC.test(src)) return src;
        // sound not coming from VK's own audio hosts is an insert (catches any new ad domain too)
        if (!VK_AUDIO_HOST.test(src)) return src;
        return '';
    }

    function adWatch() {
        var bySrc = adBySrc();
        var m = bySrc ? null : adMarker();
        var isAd = !!bySrc || !!m;

        if (isAd) {
            if (!adDom) {
                adDom = true;
                adSeeks = 0;
                try { volumeBeforeAd = Number(player && player.getVolume ? player.getVolume() : 1); } catch (e) { volumeBeforeAd = 1; }
                report('AD: ' + (bySrc ? 'src ' + bySrc.slice(0, 90) : 'DOM ' +
                       String((m && m.text) || '').replace(/\s+/g, ' ').slice(0, 140)));
            }
            // muted for the whole insert, not on a timer: VK brings the sound back early otherwise
            try { if (player && player.setVolume) player.setVolume(0); } catch (e) {}
            var el = audioEl();
            if (el) {
                try { el.muted = true; } catch (e) {}
                // wind the insert to its end: VK then moves on to the next track by itself
                if (adSeeks < 3) {
                    try {
                        if (isFinite(el.duration) && el.duration > 0 && el.currentTime < el.duration - 0.3) {
                            el.currentTime = el.duration - 0.2;
                            adSeeks++;
                        }
                    } catch (e) {}
                }
            }
            return;
        }

        if (adDom) {
            adDom = false;
            var el2 = audioEl();
            if (el2) { try { el2.muted = false; } catch (e) {} }
            try { if (player && player.setVolume) player.setVolume(volumeBeforeAd >= 0 ? volumeBeforeAd : 1); } catch (e) {}
            volumeBeforeAd = -1;
            report('insert over, sound restored');
        }
    }

    // a report for the island: lands in %LOCALAPPDATA%\DynamicIsland\log.txt as «vk shim: ...»
    function report(text) {
        try { if (window.chrome && chrome.webview) chrome.webview.postMessage(text); } catch (e) {}
    }

    function update() {
        if (inUpdate) return;
        inUpdate = true;
        try { updateCore(); } catch (e) { } finally { inUpdate = false; }
    }

    function updateCore() {
        if (!navigator.mediaSession) return;
        setHandlers(player);

        var t = track();
        if (!t) {
            if (navigator.mediaSession.playbackState !== 'none') navigator.mediaSession.playbackState = 'none';
            return;
        }

        var key = t.title + '|' + t.artist + '|' + (t.cover[0] ? t.cover[0].src : '');
        if (key !== lastKey) {
            lastKey = key;
            needTimeline = true;
            lastSentPos = -1;
            chosenSrc = '';        // a new track: pick the position source again
            stuckTicks = 0; stuckPos = -1;

            var rawTrack = t.raw;
            if (looksLikeAd(rawTrack, t) && !adSkipped[key]) {
                adSkipped[key] = 1;
                skipAd(t.title + ' — ' + t.artist + ' | type:' +
                       ((rawTrack && rawTrack.type) || '?') + ' | ads:' + JSON.stringify((rawTrack && rawTrack.ads) || null) +
                       ' | dto: ' + JSON.stringify(rawTrack).slice(0, 300));
                return;
            }
            try {
                navigator.mediaSession.metadata = new MediaMetadata({
                    title: t.title, artist: t.artist, album: t.album || '', artwork: t.cover
                });
                log('metadata: ' + t.title + ' — ' + t.artist +
                    ' | artwork:' + t.cover.length + (t.cover[0] ? ' -> ' + t.cover[0].src : '') +
                    ' | dur:' + t.duration + ' | rawArtwork:' + JSON.stringify(t.raw && t.raw.artwork || null));
                report('track: ' + t.title + ' — ' + t.artist +
                       ' | art: ' + (t.cover[0] ? t.cover[0].src : 'none') +
                       ' | dur: ' + t.duration +
                       ' | playing: ' + playing() +
                       ' | type: ' + ((t.raw && t.raw.type) || '?') +
                       ' | ads: ' + JSON.stringify((t.raw && t.raw.ads) || null) +
                       ' | dtoArtwork: ' + JSON.stringify(t.raw && t.raw.artwork || null));
            } catch (e) {}
        }

        navigator.mediaSession.playbackState = playing() ? 'playing' : 'paused';

        if (player && !window.__VKI) {
            window.__VKI = {
                get player() { return player; },
                audio: function () { return track(); },
                raw: function () {
                    try {
                        return typeof player.getCurrentAudioData === 'function'
                            ? player.getCurrentAudioData() : player.getCurrentAudio();
                    } catch (e) { return String(e); }
                },
                ad: function () { return adMarker(); },
                adLine: function () {
                    var t = document.body ? String(document.body.innerText || '') : '';
                    var m = t.match(/.{0,80}реклам.{0,160}/i);   // «Реклама»
                    return m ? m[0].replace(/\s+/g, ' ') : '';
                }
            };
            log('console diagnostics: __VKI.raw() / __VKI.adLine()');
        }

        // Position: <audio>.currentTime is the honest source, VK's own counter otherwise.
        // Reported every second, but with a real value (zeros froze the clock).
        var pos = positionNow();
        var dur = t.duration || durationNow();
        if (pos >= 0 && lastSentPos >= 0 && pos < lastSentPos - 3) {   // a jump back means a seek
            chosenSrc = '';
            stuckTicks = 0;
            pos = positionNow();
        }
        if (pos >= 0 && dur > 0) {
            try {
                navigator.mediaSession.setPositionState({
                    duration: dur, playbackRate: 1, position: Math.min(Math.max(pos, 0), dur)
                });
                needTimeline = false;
                lastSentPos = pos;
            } catch (e) {}
        }
        // every 30 seconds a full slice of what playback actually lives on
        if (playing() && pos === stuckPos) { if (++stuckTicks === 15) report('POSITION STUCK at ' + pos.toFixed(1) + ' | src ' + posSource); }
        else { stuckTicks = 0; stuckPos = pos; }

        if (++posTicks % 30 === 0) {
            var el = audioEl();
            var bits = ['pos ' + pos.toFixed(1) + '/' + dur, 'src ' + posSource, 'playing ' + playing()];
            try {
                if (typeof player.getCurrentProgressTime === 'function') bits.push('progTime ' + JSON.stringify(player.getCurrentProgressTime()));
                if (typeof player.getListenedTime === 'function') bits.push('listened ' + player.getListenedTime());
                if (typeof player.getVolume === 'function') bits.push('vol ' + player.getVolume());
            } catch (e) {}
            if (el) {
                bits.push('el t=' + (el.currentTime || 0).toFixed(1) + ' paused=' + el.paused + ' muted=' + el.muted +
                          ' rs=' + el.readyState + ' src=' + String(el.src || '').slice(0, 40));
                try { bits.push('drift ' + ((Number(player.getListenedTime() || 0)) - el.currentTime).toFixed(1)); } catch (e) {}
            }
            else bits.push('el none');
            report('state: ' + bits.join(' | '));
            posTicks = 0;
        }
    }

    // ---------------- ads ----------------
    // the same the VK Styles extension does with its CSS: hide promo posts and banners.
    var AD_SELECTORS = [
        '[data-ad]', '[data-testid="ads_banner"]', '.ads_promoted', '.wall_post_ads', '._post_ads',
        '.promoted_post', '[class*="Promoted"]', '[class*="promoted"]', '.ad_banner', '.ads_banner',
        '.left_ads', '.ads_ad_rows', '.audio_ads', '.music_promo_block', '#ads_ads'
    ];

    function scrubAds() {
        try {
            for (var i = 0; i < AD_SELECTORS.length; i++) {
                var found = document.querySelectorAll(AD_SELECTORS[i]);
                for (var j = 0; j < found.length; j++) {
                    if (found[j].style.display !== 'none') found[j].style.display = 'none';
                }
            }
        } catch (e) {}
    }

    // ---------------- start ----------------
    var boot = setInterval(function () {
        findPlayer();
        if (hooked) { clearInterval(boot); update(); }
    }, 500);

    setInterval(update, 1000);
    setInterval(scrubAds, 1500);
    setInterval(adWatch, 500);
    document.addEventListener('visibilitychange', update);
    log('vk shim for WebView2 started');
    report('started, waiting for AudioPlayer');
})();
