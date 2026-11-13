# Atoll privacy policy

Atoll collects no personal data and has no telemetry or ads. Everything it keeps stays on your computer:
settings and notes in `%APPDATA%\DynamicIsland`, chat logins and the error log in `%LOCALAPPDATA%\DynamicIsland`.

Atoll connects to the internet only for features you can see, and sends only what that feature needs:

| Feature | Service | What is sent |
|---|---|---|
| Weather | open-meteo.com | the city name or coordinates you set |
| Song lyrics | lrclib.net | track title and artist |
| "Translate" in the screenshot editor | translate.googleapis.com | the recognised text, only when you click |
| AI chat | the chat site you pick (Gemini, Claude, DeepSeek) | what you type there, under that site's own privacy policy |
| Calendar | the ICS link you add | nothing |
| Update check (GitHub version only) | api.github.com | nothing personal |

Screenshots, text recognition (OCR), QR codes, screen recordings and the clipboard are processed locally and are never uploaded.
Uninstalling Atoll removes the app; you can delete the folders above to remove your settings too.

Questions: open an issue at https://github.com/Fuwni/dynamic-island-windows/issues
