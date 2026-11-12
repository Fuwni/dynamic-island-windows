# Security

## Reporting a problem

Please **don't open a public issue** for a security problem. Use GitHub's
**[private vulnerability report](https://github.com/Fuwni/dynamic-island-windows/security/advisories/new)**
(Security tab → *Report a vulnerability*). Describe what you found and how to reproduce it; you'll get an answer as soon as possible.

Пожалуйста, не публикуйте уязвимости в открытых issue — используйте приватный отчёт по ссылке выше.

## How the app protects you

| Area | Protection |
|---|---|
| **Claude Code bridge** (`http://localhost:47811`) | Listens on the loopback interface only. Every call must carry a random 256-bit secret stored in `%LOCALAPPDATA%\DynamicIsland\bridge.token` (readable only by your Windows account). Requests with an `Origin` or `Referer` header (web pages), non-JSON bodies or bodies over 1 MB are rejected, and at most 4 permission questions can wait at once. If the island isn't running, Claude Code falls back to asking in the terminal. |
| **Opening links** | Only `http://` and `https://` addresses are ever handed to the system. Text from QR codes, OCR, the command bar and web pages can't launch programs, files or custom protocol handlers. |
| **Embedded chat** (WebView2) | Navigation to non-web schemes is blocked, DevTools and host objects are off, sign-in pop-ups are allowed only for the exact chat / sign-in hosts, and only those sites can send the island its "you are typing" signal (which never includes what you type). |
| **Your data** | No telemetry. Settings and notes stay in `%APPDATA%\DynamicIsland`, chat logins in `%LOCALAPPDATA%\DynamicIsland\WebView2`. Clipboard history is kept in memory only. Nothing personal is included in releases or in the repository. |
| **Claude Code settings** | The island only adds and removes its own hook entries in `~/.claude/settings.json` and keeps a backup (`settings.json.before-dynamic-island`). |

## Supported versions

Only the latest release gets security fixes.
