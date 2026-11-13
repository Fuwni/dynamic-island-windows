# Changelog

All notable changes to Atoll (formerly "Dynamic Island for Windows"). Русская версия каждого пункта — ниже английской.

## 1.6.0 — 2026-11-14

### Island
- **Island size** (Settings → Appearance): 80–130 %. The whole island, its shadow and window scale together.
- **Lock in place** (Settings → Behavior): the island can't be dragged or swiped away by accident.
- **Hide over chosen apps**: pick programs (a windowed game, a video-call app…) and the island hides while they are in front, like over a full-screen game.
- **Show in screenshots or not**: a switch to keep the island out of screenshots, recordings and screen sharing (on by default, as before) or let it show. With it shown, the frosted glass turns solid, otherwise the island would photograph itself.
- **Caps Lock and keyboard layout**: a short notice on the island when Caps Lock turns on or off and when the input language changes (each can be turned off).
- "Hide in games and fullscreen" and "Show FPS in games" now exclude each other: turning one on turns the other off.

### Tools
- **Notes**: pasting a long, multi-line text keeps all of it (before, only the first line was kept). Enter saves, Shift+Enter starts a new line; a click opens a long note in full; copy and delete buttons on each note.
- **Larger chat**: a button in the chat header gives sign-in pages more room, so captchas (Qwen, Grok…) are no longer cut off by the island's edge.
- **VPN tile**: without a Windows VPN profile it opens your VPN app instead (Happ, Hiddify, v2rayN, AmneziaVPN, Outline, WireGuard, NekoRay and others), found by its Start-menu shortcut.

### По-русски
- Размер острова (80–130 %), «Закрепить на месте», скрытие острова поверх выбранных программ, переключатель «Скрывать на скриншотах и демонстрации экрана».
- Уведомления о Caps Lock и смене раскладки.
- «Прятать в играх» и «FPS в играх» теперь взаимоисключающие.
- Заметки: длинный текст вставляется целиком, Shift+Enter — новая строка, кнопки «Скопировать» и «Удалить».
- Кнопка «Больше места» в чате — капча Qwen и других больше не обрезается.
- Плитка VPN открывает ваше VPN-приложение (Happ, Hiddify, v2rayN, Amnezia…), если VPN-профиля Windows нет.

## 1.5.1 — 2026-11-14

- Opening the island no longer shows the window behind it through the island for half a second. With frosted glass on, the island is see-through over a blurred picture of the screen. That picture was still small while the island opened, so a web page or chat text showed through sharply. Now the island stays solid until a fresh picture covers it; the same applies while dragging.
- "See-through bottom" now only applies over the frosted glass or your photo. Before, with glass off or with light graphics, the bottom of the island showed the windows behind it sharply.

### По-русски
- При открытии острова на полсекунды больше не просвечивает текст окна позади (страница, чат): остров непрозрачный, пока не готов свежий размытый снимок под стеклом. То же при перетаскивании.

## 1.5.0 — 2026-11-14

### Smoother
- Opening and closing run at a steady 60 fps: the window grows before the island moves (when the cursor reaches it) instead of mid-animation, the frosted glass never grabs the screen during an animation, and the sound level is read once per frame instead of many times.
- Dragging the island follows the mouse closely: one window move per frame instead of two, no lag.
- **Light graphics** for weak PCs (Settings → Behavior): no glass, glow or effects. Turns on by itself when the graphics card isn't fully accelerated, there are 4 threads or fewer, or animations keep coming late.

### Music
- A wave under the track that moves with the sound.
- **Repeat** for any player: players that allow it get it directly; for Spotify, Yandex Music and others Atoll repeats the track itself.
- **♥ Favourites**: like a track, see your list next to the lyrics, click to copy its name.
- **Yandex Music** app is recognised even when it doesn't report to Windows.

### Island
- **Several activities at once** (a Discord call, music, a timer, a download…): the mouse wheel over the pill switches between them; dots show how many there are.
- **Shortcut to open the island**: Ctrl+Alt+O by default (or Ctrl+Alt+A, Alt+Q, Ctrl+Shift+Space). Again, Esc or a click elsewhere folds it.
- **Themes**: 12 ready-made looks (colour, accent, glow), an online theme gallery (colours only, kept in this repository: `themes/themes.json`) and **your own photo** as the island's background.

### AI
- **Codex (OpenAI)**: permission requests and "done" from Codex CLI on the island, like Claude Code (Settings → AI agents → Connect; approve once with `/hooks` in Codex).
- The chat has **9 assistants**: ChatGPT, Claude, Gemini, DeepSeek, Grok, Perplexity, Copilot, Le Chat and Qwen.

### Safer
- Passwords copied from password managers (KeePass, Bitwarden, 1Password, Windows) never enter the clipboard history.
- The embedded chat can't download programs or scripts and doesn't offer to save passwords.

### Live waves
- Buttons work again with **Live waves** on: pressing squeezed the island towards its top, so a button low in the open island slid out from under the cursor and the click was lost. Only the folded pill squeezes now, and never under a button.
- A **wave strength** slider (Settings → Appearance).

### Also
- A button to the Atoll channel on Telegram (bottom of Settings).

### По-русски
- **Плавнее**: открытие и закрытие идут ровно в 60 кадров, перетаскивание острова больше не дёргается, для слабых ПК есть облегчённая графика (включается сама, если анимации тормозят).
- **Музыка**: волна под треком, повтор в любом плеере, ♥ избранное, поддержка приложения Яндекс Музыка.
- **Несколько активностей**: звонок в Discord, музыка, таймер — переключайте колесом мыши над пилюлей.
- **Ctrl+Alt+O** открывает остров. **Темы**: 12 готовых, галерея из интернета и своё фото.
- **Codex** на острове, **9 нейросетей** в чате.
- **Безопаснее**: пароли из менеджеров паролей не попадают в историю буфера, чат не скачивает программы.
- **Живые волны**: кнопки снова нажимаются, появился ползунок силы волн.
- Кнопка Telegram-канала в настройках.

## 1.4.1 — 2026-11-13

- **Much lighter**: the idle island now uses about a quarter of the CPU it did (measured: ~28 % of one core → ~7 %).
  - The window is only as tall as the folded pill, so Windows copies far fewer pixels on each frame.
  - The glow animates at 12 frames a second instead of 60, and pauses in games, full-screen apps and when the island is hidden.
  - The frosted glass captures only the area behind the island, at a quarter of the resolution, into one reused image, and only when the background changes.
  - The shadow is a cached layer: the character and the clock no longer force the whole island to be blurred again.
  - In games, GPU load is read with one query a second instead of hundreds.
  - A calm character redraws 15 times a second (8 when asleep) instead of 30.
  - The built-in chat browser closes after 10 minutes unused and frees its memory; your login stays.
- **Microsoft Store version** (coming soon): installs without the blue "Windows protected your PC" warning; it updates through the Store and starts with Windows through the Store's own startup setting.
- Privacy policy: [PRIVACY.md](PRIVACY.md).

### По-русски
- **Заметно легче**: в простое остров нагружает процессор примерно в 4 раза меньше (было ~28 % одного ядра, стало ~7 %). Свечение не работает поверх игр, стекло и тень больше не перерисовываются без нужды, чат освобождает память, если им не пользоваться.
- **Версия для Microsoft Store** (скоро): ставится без синего предупреждения Windows.

## 1.4.0 — 2026-11-13

- **Apps on Home**: a row of your favourite apps at the bottom of the open island. Press **+** and type a name to find any program, including Microsoft Store apps (Calculator, Spotify…). One click opens it; a dot shows what is running; right-click to open its folder or remove it.
- **Control center** (System page): Wi‑Fi, Bluetooth, airplane mode and VPN toggles, battery, and Lock / Sleep / Restart / Shut down (restart and shut down ask first).
- **Clean desktop**: one toggle hides the desktop icons and brings them back. The taskbar stays as it is.
- **FPS in games**: in game mode the pill shows the frame rate of the game in front, plus CPU and GPU load, and it now takes priority over other activities. FPS is measured like PresentMon (DirectX 9–12 and most Vulkan games). If Windows doesn't allow it yet, Settings has an **Allow** button (one confirmation, then sign out and back in). Games in *exclusive* full screen can't show any overlay; use borderless window.
- **Click outside to close**: clicking anywhere outside the open island folds it back.

### По-русски
- **Приложения на главной**: строка любимых программ внизу острова. Кнопка **+** и поиск по названию, включая приложения из Microsoft Store; запуск в один клик, точка у запущенных.
- **Пункт управления** (страница «Система»): Wi‑Fi, Bluetooth, режим «в самолёте», VPN, батарея, блокировка, сон, перезагрузка и выключение.
- **Чистый рабочий стол**: одной кнопкой прячет значки на рабочем столе; панель задач не трогается.
- **FPS в играх**: в игровом режиме пилюля показывает FPS и нагрузку CPU/GPU, и эта строка теперь важнее других событий.
- **Остров закрывается**, если кликнуть в любом месте вне его.

## 1.3.0 — 2026-11-12

- **Automatic updates**: Atoll checks GitHub shortly after start and every 6 hours. When a new version is out, the island tells you; one click downloads the installer (verified against GitHub's SHA-256 digest), updates in place and starts again. Settings → Behavior → Updates has "Check now" and an on/off switch. Portable copies open the release page instead.
- **New name: Atoll** (an atoll is a ring-shaped island). "Dynamic Island" is Apple's name; the app now has its own.
- **New minimal logo**: a white ring-shaped pill with one warm dot on a graphite tile — in the window, taskbar, tray, installer and Start menu.
- The installer is now `Atoll-…-setup.exe`. Installing over 1.2.x keeps all settings and replaces the old "Dynamic Island" shortcuts. Existing screenshot and recording folders keep their names; new users get `Pictures\Atoll` and `Videos\Atoll`.

### По-русски
- **Автообновление**: Atoll сам проверяет новые версии; при выходе обновления остров покажет уведомление, и в один клик скачает проверенный установщик, обновится и перезапустится. «Настройки → Поведение → Обновления».
- **Новое имя — Atoll** (атолл — кольцевой остров) и **новый минималистичный логотип**: белое кольцо-пилюля с тёплой точкой.
- Установщик теперь `Atoll-…-setup.exe`; обновление поверх 1.2.x сохраняет все настройки.

## 1.2.1 — 2026-11-12

- **Installer**: `DynamicIsland-1.2.1-setup.exe` installs per user (no admin rights) with a Start-menu entry, optional desktop icon and autostart, in 6 languages, and shows up in Windows "Installed apps".
- **Quick uninstall**: Settings → Behavior → *Uninstall Dynamic Island* (or Windows Settings → Apps). Removes the app, autostart and Claude Code hooks, and asks about deleting settings, notes and chat logins. Works for the portable zip too.
- **Language list** is a proper drop-down that opens inside the island; switching relaunches reliably.
- **Align to center** button (Settings, right-click menu, tray, command bar).

### По-русски
- **Установщик** без прав администратора: ярлык в «Пуске», автозапуск, запись в «Приложениях» Windows.
- **Быстрое удаление**: «Настройки → Поведение → Удалить Dynamic Island» — убирает программу, автозапуск и хуки, спрашивает про личные данные.
- Новый выпадающий список языков и кнопка «Выровнять по центру».

## 1.2.0 — 2026-11-12

### Languages
- The whole interface is translated into **English, Russian, Ukrainian, Spanish, German, Portuguese (Brazil) and Chinese (Simplified)**: pages, settings, notifications, tray menu, weather, timers, the screenshot editor.
- The language follows Windows by default; change it in **Settings → Behavior → Language** (the app restarts in a second).
- Dates and numbers use the chosen language; weather place names come in it too.
- Quick reminders and the command bar understand "tomorrow / in 20 min" in every language ("завтра", "tomorrow", "mañana", "morgen", "amanhã", "明天"…).
- "Translate" goes to your interface language by default.
- For contributors: strings live in `tools/i18n/strings*.tsv`; `python tools/i18n/extract.py && python tools/i18n/build.py` refreshes `Assets/lang/*.json` and lists anything untranslated. Adding a language = adding a column.

### Look
- **New app icon** (window, taskbar and tray), drawn in code so it is sharp at every size.

### For video makers
- `--brand <folder>` renders the icon at every size and an animated intro / outro.
- `--promo`, `--review` and `--live-demo` film the island on demo data for promo videos (see `tools/`).
- `--lang <code>` previews another language without changing your setting.

### По-русски
- Интерфейс переведён на **английский, русский, украинский, испанский, немецкий, португальский и китайский**. Язык выбирается автоматически по Windows или в «Настройки → Поведение → Язык».
- Даты, числа и быстрые напоминания работают на любом языке.
- Новая иконка приложения.
- Инструменты для съёмки промо-роликов и скриншотов.

## 1.1.0 — 2026-11-12

### Characters
- **15 characters** to choose from: Eyes, Cat, Ghost, Robot, Fish, Planet, Slime, Pulse, Bunny, Owl, Dino, Star, Cloud, **Claude** and **Claude Code**.
- Characters now live **in the open menu too** (in the header) and react to what you do: hop on button presses and page switches, cheer when you switch something on, type along while you type, greet you when the menu opens.
- **Easter egg:** click a character and it shows one of **7 emotions** (love, anger, surprise, sadness, dizziness, laughter, cool), each with its own eyes, motion and effects.
- **Claude** — the orange starburst: no face, never spins; the rounded rays stretch and pull back in a travelling wave and it swells in on mood changes, like the logo animation.
- **Claude Code** — the pixel mascot, drawn after the official sticker (wide body, arms halfway up, two pairs of legs, square eyes). It greets you with a wink and a wave; when you write in the Chat page it hops onto the chat window, turns sideways, sets down a laptop, opens it and types; when you stop, it closes the lid, puts it away and turns back.
- **Hand-drawn mode**: characters animate on twos (12 fps) with a trembling ink outline, like frame-by-frame animation.
- The Claude characters' **orange glow can be switched off**.

### Look and feel
- The island **reaches for the cursor with its whole body** (a smooth field, not a single bump).
- Opening uses a soft **blur reveal**, a split-second **colour separation** and a gentle **spring hop**.
- **Frosted glass** with adjustable tint, a see-through lower half, and an idle **shimmering glow** from the bottom edge (Aurora, Sunset, Ocean, Neon, Frost).
- **Docked mode**: the island grows out of the top edge of the screen.
- **Page dots** in the header with smooth, staggered icons on hover.
- Redesigned buttons (several distinct button kinds instead of one), a simpler settings page with a sidebar.

### New features
- **Command bar** (`Alt+Space`, or `Ctrl+Alt+Space` if another launcher owns it): apps, websites, web search, island actions.
- **Shelf** for files dropped on the island and **text tools**: fix text typed in the wrong keyboard layout, change case, transliterate, clean up.

### Fixes
- The weather city and chat logins (Claude / Gemini / DeepSeek) are **no longer lost after a reboot**: settings are saved atomically and the embedded browser shuts down cleanly so cookies reach the disk.
- Discord no longer shows "in a channel" when you aren't in one.
- Translation picks the direction automatically (Russian ↔ English) instead of translating Russian into Russian.
- The frosted glass no longer turns opaque a second after opening.

### Security
- The local **Claude Code bridge** now requires a per-user **secret token**, rejects requests from web browsers (any `Origin` / `Referer`), accepts only JSON up to 1 MB and limits how many permission questions can wait at once. Before, any web page could post fake notifications or permission prompts to it.
- **Links are opened only if they are http/https.** Text from QR codes, screenshots, the command bar or chat pages can no longer start programs or custom protocol handlers.
- The embedded chat **cannot navigate to non-web schemes**, sign-in pop-ups are matched by exact host (not by a substring of the URL), and only the chat sites can talk to the island.
- See [SECURITY.md](SECURITY.md).

### По-русски
- **15 персонажей**, включая **Claude** и **Claude Code**; они живут и в пилюле, и в шапке открытого меню и реагируют на нажатия, смену страниц, переключатели и набор текста.
- **Пасхалка:** клик по персонажу — одна из **7 эмоций** (влюблённость, злость, удивление, грусть, головокружение, смех, крутые очки).
- **Claude Code** нарисован по официальному стикеру; в чате открывает ноутбук и печатает. **Claude** — «искра» с лучами-волной.
- **Рисованный стиль** (покадрово, 12 кадров/с, дрожащий контур); оранжевую подсветку Claude можно выключить.
- Остров **тянется к курсору всем телом**, открывается с размытием, цветовым расслоением и пружинкой.
- **Матовое стекло** с оттенком, полупрозрачный низ, **переливающаяся подсветка** в покое, режим **«У края»**, точки страниц, новые кнопки, упрощённые настройки.
- **Командная строка**, **полка** файлов и **инструменты текста**.
- Исправлено: сброс города и входа в нейросети после перезагрузки, ложный «в канале» Discord, перевод русского на русский, пропадающее стекло.
- **Безопасность:** мост Claude Code с секретным ключом и защитой от веб-страниц, ссылки только http/https, чат не может запускать программы.

## 1.0.0

First public release: compact and expanded island, music with lyrics, weather, chat, timer, clipboard, notes, calendar, system page, screenshots with OCR / translation / QR, screen recording, file drop actions, Claude Code permission requests.
