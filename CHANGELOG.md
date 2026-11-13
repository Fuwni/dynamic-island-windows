# Changelog

All notable changes to Atoll (formerly "Dynamic Island for Windows"). Русская версия каждого пункта — ниже английской.

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
