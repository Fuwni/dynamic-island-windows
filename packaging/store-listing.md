# Microsoft Store submission: Atoll Island

## Packages
Upload `dist\Atoll-<version>.msix` (built by `tools\make-msix.ps1`). The Store signs it.

## Restricted capabilities: justification (paste into "Submission options")
- **runFullTrust**: Atoll is a classic WPF desktop app. It shows a floating island window on top of other windows, reads the media session, system load and the clipboard, takes screenshots and records the screen at the user's request, and talks to the Claude Code CLI through local files.
- **unvirtualizedResources**: Atoll keeps its settings and notes in %APPDATA%\DynamicIsland and the Claude Code bridge files in %LOCALAPPDATA%\DynamicIsland, where the separately installed Claude Code CLI must be able to read them. Virtualised writes would hide these files from it.

## Properties
- Category: Utilities & tools
- Privacy policy URL: https://github.com/Fuwni/dynamic-island-windows/blob/main/PRIVACY.md

## Store listing (English)
**Description**
Atoll turns the top of your screen into a living island, inspired by the Dynamic Island. Music with lyrics, timers, downloads, calls and notifications appear in a small pill; hover or click to open a menu with screenshots and OCR, weather, notes, clipboard history, a calendar, an AI chat and your favourite apps. Animated characters live in the pill and react to what you do. Free, open source and translated into 7 languages.

**What's new**: apps right in the island, a control center (Wi‑Fi, Bluetooth, VPN, power), FPS and load in games, much lower CPU use.

**Features** (one per line): Music with live lyrics · Live activities: timers, downloads, Discord calls · Screenshots with OCR, QR codes and translation · Screen recording · Weather and a calendar · Notes and clipboard history · Control center and app launcher · FPS and CPU/GPU in games · Claude Code approvals on the island · 7 languages

## Store listing (Русский)
**Описание**
Atoll превращает верх экрана в живой остров в стиле Dynamic Island. Музыка с текстом песен, таймеры, загрузки, звонки и уведомления появляются в маленькой пилюле; наведите или нажмите — откроется меню со скриншотами и распознаванием текста, погодой, заметками, историей буфера, календарём, ИИ-чатом и любимыми приложениями. В пилюле живут анимированные персонажи и реагируют на ваши действия. Бесплатно, с открытым кодом, 7 языков.

**Что нового**: приложения прямо в острове, пункт управления (Wi‑Fi, Bluetooth, VPN, питание), FPS и нагрузка в играх, заметно меньше нагрузка на процессор.

## Screenshots
Use the demo renders (no personal data): `DynamicIsland.exe --snapshot <folder> --lang en` (and `--lang ru`).
