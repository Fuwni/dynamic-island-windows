# Changelog

All notable changes to Atoll (formerly "Dynamic Island for Windows"). Русская версия каждого пункта — ниже английской.

## 2.2.0 — 2026-11-26

- **The island never goes past the screen's edge.** Pushed against a side (or low on the screen), it used to open partly off the screen. Now it opens inward and goes back to its place when it folds; the place you chose never moves. Dragged, it gives way softly at the edge instead of sliding off.
- **On a side.** Settings → Appearance → "On a side", or the island's menu → Position → Left / Right: a narrow column along the edge, its content the right way up (the character, the cover, the time). It opens outward from the edge and stays on the screen; let go on the other half of the screen, it moves to that side.
- **In the taskbar.** "On the taskbar" puts the pill right in the taskbar, at the height of its buttons, first in its free part (left of centred buttons). Opening, it rises above the bar so Start and the buttons stay free. It follows a taskbar that hides itself, works with a taskbar on top or on a side, stays above the bar after you click it, and fades away while Start, search or the notification centre is open over it.
- **The wheel goes through the tabs** of the open island, one per notch; lists, sliders and text boxes keep their own scrolling.
- **The island comes out of its own edge** when it shows, hides or moves to another screen: up out of the taskbar, right from the left side, left from the right one.
- The island's menu: "Position" opens its choices (the menu could not show a submenu before).

- **Островок больше не уходит за край экрана.** Прижатый к краю (или стоящий низко), он раскрывался наполовину за экраном. Теперь он раскрывается внутрь и, свернувшись, возвращается на своё место, а выбранное вами место не сдвигается. При перетаскивании он мягко упирается в край, а не уезжает за него.
- **Сбоку.** Настройки → Внешний вид → «Сбоку» или меню островка → Положение → Слева / Справа: узкий столбик вдоль края, всё внутри стоит ровно (персонаж, обложка, время). Раскрывается наружу от края и не выходит за экран. Если отпустить его на другой половине экрана, он переходит на ту сторону.
- **В панели задач.** «У панели задач» ставит пилюлю прямо в панель, на высоте её значков, сначала в свободную часть (слева от значков по центру). Раскрываясь, островок поднимается над панелью, «Пуск» и значки остаются свободными. Понимает панель, которая сама скрывается, и панель сверху или сбоку, не прячется за панель после клика по ней и исчезает, пока поверх открыты «Пуск», поиск или центр уведомлений.
- **Колесо мыши листает вкладки** открытого островка, по одной за щелчок; списки, ползунки и поля ввода прокручиваются как раньше.
- **Островок выезжает из своего края**, когда появляется, прячется или переходит на другой экран: вверх из панели задач, вправо с левого края, влево с правого.
- Меню островка: «Положение» открывает свои варианты (раньше меню не умело показывать подменю).

## 2.1.1 — 2026-11-24

- **Less load in full-screen games.** In 2.1.0 the glass kept taking pictures of the screen 4–5 times a second over a game, and the island kept redrawing with the music and re-raising itself; this could show up as small freezes. With a full-screen game in front the island now takes no pictures (it turns solid), stops its waves, equaliser and pull to the cursor, re-raises itself only when the game has really covered it, and the FPS pill changes once a second and doesn't resize for every digit. Measured on a test window: pictures 4.6 → 0 a second, island redraws 84 → 0 a second, music playing. Everything comes back when you leave the game. Games in an ordinary window are not recognised.

- **Меньше нагрузки в полноэкранных играх.** В 2.1.0 стекло и поверх игры снимало экран 4–5 раз в секунду, а остров перерисовывался под музыку и постоянно поднимал себя наверх; это могло давать микрофризы. Теперь, когда впереди полноэкранная игра, остров не делает снимков (становится сплошным), не рисует волны, эквалайзер и притяжение к курсору, поднимает себя, только если игра его действительно закрыла, а пилюля с FPS меняется раз в секунду и не меняет ширину из-за каждой цифры. Замер на тестовом окне с музыкой: снимки 4,6 → 0 в секунду, перерисовки 84 → 0 в секунду. После выхода из игры всё возвращается. Игры в обычном окне не распознаются.

## 2.1.0 — 2026-11-24

- **Smooth glass.** The glass's picture of what's behind the island used to change about once a second (and every six seconds folded), and dropped out while you dragged the island. Now it is taken on a thread of its own up to 20 times a second while the island is open, 4–5 times folded, and under twice a second when nothing behind moves; a new picture fades in over the old one instead of jumping. Measured (liquid glass, Ryzen 7 2700): open over a moving picture 2–3 % CPU, folded about 1 %, the UI thread spends 0.2 ms per picture instead of 1.7 ms.
- **Liquid glass bends on the graphics card**: the lens at the rim was worked out pixel by pixel on the processor for every picture; now a shader does it, with the same look.
- **The glass stays on while you drag the island**: the picture stays where it was taken and slides back as the island moves, so what's behind stands still. Measured while dragging: glass on in 98 % of frames at a normal pace (it was off), at a very fast flick the glass fades to the solid colour for a moment.
- **Two new glass themes** in Settings → Appearance: "See-through" — what is behind the island shows as it is under a tint, with no pictures taken at all — and "Matte" — strongly frosted glass, the tint thick enough for the text over this backdrop. Each has its own "Glass density".
- **Music right in the island**: the Music tab has a row of sources — YouTube Music, YouTube, Spotify, SoundCloud, Deezer, Apple Music, internet radio and your own files — with one search and a Playlists button. A service opens as its own site inside the island, with your own sign-in, under a one-line player; folded, it plays on. Radio stations can be kept with ♥, folders of your files are playlists. Nothing is loaded until you open it; after five minutes without music the player closes and frees its memory.
- **Liquid glass is glass now, not a dark panel**: a clear tint (near-black 0.32 in the dark theme, white 0.14 in the light one), a soft blur, richer colours, a 1 px ring with a brighter line along the top, and a clear band at the rim that bends what is behind. Tiles, chips, buttons and notification cards are panes of the same glass over one picture. The tint follows the backdrop: over a bright page the dark glass gets only as dense as the text needs. "Glass density" in Settings → Appearance goes from clear to nearly opaque; other themes keep their own tint.
- **Yandex Music**: the app has its own name on the island and its own volume; lyrics of an artist written in Cyrillic are found under the Latin spelling too.

### По-русски
- Плавное стекло. Картинка того, что за островом, раньше обновлялась примерно раз в секунду (а в свёрнутом — раз в шесть секунд) и пропадала, пока остров перетаскивают. Теперь она снимается в отдельном потоке до 20 раз в секунду у раскрытого острова, 4–5 раз у свёрнутого и реже двух раз, когда за островом ничего не движется; новая картинка плавно проступает поверх старой, а не меняется скачком. Замер (жидкое стекло, Ryzen 7 2700): раскрытый остров над движущейся картинкой — 2–3 % процессора, свёрнутый — около 1 %, поток интерфейса тратит на картинку 0,2 мс вместо 1,7 мс.
- Жидкое стекло преломляет на видеокарте: линза по краю считалась на процессоре по пикселю для каждой картинки, теперь это делает шейдер, вид тот же.
- Стекло не пропадает, пока остров перетаскивают: картинка остаётся там, где её сняли, и сдвигается обратно вслед за островом, так что фон стоит на месте. Замер при перетаскивании: стекло видно в 98 % кадров при обычной скорости (раньше выключалось); при очень резком броске стекло на мгновение плавно переходит в сплошной цвет.
- Две новые стеклянные темы в Настройки → Внешний вид: «Полупрозрачная» — то, что за островом, видно как есть под тоном, без единого снимка экрана, — и «Матовая» — сильно размытое стекло, тон ровно такой плотный, чтобы читался текст. У каждой своя «Плотность стекла».
- Музыка прямо в островке: во вкладке «Музыка» — ряд источников (YouTube Music, YouTube, Spotify, SoundCloud, Deezer, Apple Music, интернет-радио и свои файлы), общий поиск и кнопка «Плейлисты». Сервис открывается своим сайтом внутри островка, с вашим собственным входом, под плеером в одну строку; свёрнутый остров продолжает играть. Станции сохраняются сердечком, папки ваших файлов — плейлисты. Пока вы ничего не открыли, ничего не загружается; через пять минут без музыки плеер закрывается и освобождает память.
- Жидкое стекло теперь стекло, а не тёмная панель: прозрачный тон (почти чёрный 0,32 в тёмной теме, белый 0,14 в светлой), мягкое размытие, цвета насыщеннее, кольцо в 1 px с более яркой линией сверху и прозрачная полоса у края, которая преломляет фон. Плитки, чипы, кнопки и карточки уведомлений — такое же стекло поверх одной картинки. Тон следит за фоном: над светлой страницей тёмное стекло становится плотнее ровно настолько, чтобы текст читался. «Плотность стекла» в Настройках → Внешний вид — от прозрачного до почти сплошного; у других тем тон свой, прежний.
- Яндекс Музыка: у приложения своё название на острове и своя громкость; слова исполнителя, записанного кириллицей, находятся и под латинским написанием.

## 2.0.1 — 2026-11-23

- **Fixed: memory kept growing past 1 GB.** Every second the island asked Windows for the sound devices anew (for the wave and for "is Discord in a call") and never let them go; each one kept a piece of memory Windows couldn't take back. Measured: in one minute 2.0.0 left 302 of them behind, 2.0.1 keeps one.
- **Fixed: karaoke in Chinese, Japanese and Korean lit up in random places**, and in any language the part before a dash lit up on its own. Now one edge of light runs across the whole line in every script, Chinese and Japanese commas and full stops are pauses, and a line sung twice in a row starts its light over instead of standing lit.
- **Fixed: closing notifications shook and folded the island.** Close cards one after another with × or a swipe: the island keeps its size while the pointer is on the cards and shrinks once when it leaves. New: "Clear all" next to "Show all".
- **Notices no longer fold the open island**: a message, Claude Code, "widget removed — undo", a liked track, repeat — while the island is open they come as a little island of their own beside it; a click does what the notice does.
- **Fixed: page dots drawn over the tab icons** after changing the tabs in Settings → Layout, and the highlight under the tabs sliding a quarter of a tab off the chosen one.
- **Fixed: the glass thinned at the tip of a drip** when the edge waves with the music. New: "Waves from the music" (Settings → Layout) turns the music's waves off and keeps the ones from touches.
- **Claude Code: "Connected" now means events arrive.** The status says whether the hooks are waiting for their first event (restart open sessions), working (with the time of the last event) or the port is busy; "Check" runs the installed hook the way Claude Code does and says what happened. The same for Codex. Problems go to `%USERPROFILE%\.atoll\hook.log`.
- **No more hidden PowerShell** — antivirus programs complained about it: Claude Code's and Codex's hooks now run Atoll itself (about 0.3 s per event instead of 1.6 s), and the two steps that need administrator rights (FPS access, the equaliser) start Atoll through the usual Windows prompt.
- **The command bar's shortcut can be changed or turned off**: Settings → Behavior — Alt+Space (as before), Ctrl+Alt+Space, Ctrl+Shift+Space or off.
- **Screenshots**: File Explorer no longer opens after saving unless you turn it on; "After a screenshot" can save and copy it without any window, only a short notice. In a full-screen game a screenshot never opens a window, so the game keeps its focus.
- **Liked tracks open where they were playing**: a click in the liked list opens the track in Spotify, YouTube Music, Yandex Music, Apple Music, SoundCloud, VK or Deezer — wherever it was playing (as a search: Windows doesn't tell which track page it was).
- **Fixed: the island got in the way of games.** Throwing the mouse to the top of the screen in a game landed on the island: it swelled, opened, and a click took the game's focus away. Now the island makes way for the cursor (Settings → Behavior → "Make way for the cursor", on by default): a cursor flying across it over another window finds it see-through, and clicks go to the window under it; rest the cursor on it or push it to the top edge and it comes back. In full-screen games it stays out of the way the whole time the cursor is over it.
- **Its own place for each program** (Settings → Behavior, off by default): move the island while a program is in front, and it remembers that place — whenever that program is in front again, the island floats there, and back to its usual place for the others. "Align to center" in the island's menu gives that program back the usual place.
- **Smaller things**: sliders save their value once you let go instead of on every step; the Gemini key is stored encrypted for your Windows user and never written to the log; a start menu folder Windows doesn't let anyone read no longer stopped the search for programs; the privacy policy lists every service, in Russian too.

### По-русски
- Исправлено: память со временем вырастала больше 1 ГБ. Каждую секунду остров заново спрашивал у Windows звуковые устройства (для волны и для «идёт ли звонок в Discord») и не отпускал их; каждое держало кусочек памяти, который Windows не могла забрать. Замер: за минуту 2.0.0 оставлял 302 таких объекта, 2.0.1 держит один.
- Исправлено: караоке на китайском, японском и корейском заполнялось с произвольных мест, а на любом языке часть перед тире загоралась отдельно. Теперь один край света бежит по всей строке на любой письменности, китайские и японские запятые и точки — паузы, а строка, спетая два раза подряд, начинает свет заново, а не стоит залитой.
- Исправлено: при закрытии уведомлений остров дёргался и сворачивался. Закрывайте карточки подряд крестиком или свайпом — пока курсор на карточках, остров не меняет размер и уменьшается один раз, когда курсор ушёл. Новое: «Очистить все» рядом с «Показать все».
- Уведомления больше не сворачивают раскрытый остров: сообщение, Claude Code, «виджет убран — вернуть», избранное, повтор — пока остров раскрыт, они появляются отдельным маленьким островком сбоку; нажатие делает то же, что уведомление.
- Исправлено: точки страниц рисовались поверх иконок вкладок после изменения вкладок в Настройки → Раскладка, а подсветка под вкладками съезжала с выбранной на четверть вкладки.
- Исправлено: стекло становилось тоньше на кончике «капли», когда край качается в такт музыке. Новое: «Волны от музыки» (Настройки → Раскладка) — можно выключить волны от музыки и оставить волны от касаний.
- Claude Code: «Подключено» теперь значит, что события приходят. Статус говорит, ждут ли хуки первого события (перезапустите открытые сессии), работают ли (со временем последнего события) или занят порт; «Проверить» запускает установленный хук так же, как Claude Code, и сообщает, что получилось. То же для Codex. Ошибки пишутся в `%USERPROFILE%\.atoll\hook.log`.
- Больше никакого скрытого PowerShell, на который жаловались антивирусы: хуки Claude Code и Codex теперь запускают сам Atoll (около 0,3 с на событие вместо 1,6 с), а два шага с правами администратора (доступ к FPS и эквалайзер) запускают Atoll через обычный запрос Windows.
- Сочетание командной строки можно сменить или выключить: Настройки → Поведение — Alt+Space (как было), Ctrl+Alt+Space, Ctrl+Shift+Space или «Выкл».
- Скриншоты: Проводник после сохранения больше не открывается, если его не включить; «После снимка» можно сохранять и копировать без окна, только с коротким уведомлением. В полноэкранной игре снимок никогда не открывает окно — игра не теряет фокус.
- Избранное открывается там, где играло: нажатие на песню в списке открывает её в Spotify, YouTube Music, Яндекс Музыке, Apple Music, SoundCloud, VK или Deezer — там, где она звучала (как поиск: Windows не сообщает, какая это была страница трека).
- Исправлено: остров мешал играм. Мышь, брошенная в игре к верху экрана, попадала на остров: он раздувался, раскрывался, а щелчок забирал у игры фокус. Теперь остров уступает курсору (Настройки → Поведение → «Уступать курсору», включено): курсор, пролетающий по нему над другим окном, находит его прозрачным, и щелчки уходят окну под ним; задержите курсор или упритесь в верхний край — и остров вернётся. В играх на весь экран он не мешает всё время, пока курсор над ним.
- Своё место для каждой программы (Настройки → Поведение, по умолчанию выключено): передвиньте остров, пока программа впереди, — он запомнит это место и, когда она снова впереди, сам уедет туда, а для остальных вернётся на обычное место. «Выровнять по центру» в меню острова возвращает этой программе обычное место.
- Мелочи: ползунки сохраняют значение, когда их отпустили, а не на каждом шаге; ключ Gemini хранится зашифрованным для вашего пользователя Windows и никогда не попадает в журнал; папка меню «Пуск», которую Windows никому не даёт читать, больше не обрывает поиск программ; политика конфиденциальности перечисляет все сервисы, теперь и по-русски.

## 2.0.0 — 2026-11-20

- **Desktop widgets — more than 30 of them**: music (with a live visualizer, synced lyrics and an equalizer through Equalizer APO), weather, clocks, world time, calendar and month, photos, notes you type right on the glass, to-do, reminders, countdown, timer, stopwatch, Do not disturb, calculator, volume, apps, clipboard, quick actions, system (2-minute graphs, the graphics card's temperature, a warning only on real overheating), processes, CPU, GPU, memory, disk, network, battery, water, breathing and the moon. Each comes in S, M or L, snaps to a grid and opens up when clicked; recolour it, pick its accent and how much glass it has — the glass blurs the wallpaper under it and bends it a little at the rim, live wallpapers included. Save a set and bring it back in one click. The "Widgets" tab of the island lists them all. Full-screen games and videos pause them, and a widget under windows does no work.
- **Settings numbers are scrubbers**: island size, corner roundness, idle transparency, wave strength, the AI assistant's volume, and the widgets' glass (refraction, blur, a widget's glass) are now one bar each with the name on the left and the value on the right — grab it anywhere and drag, or use the arrow keys; a thin capsule at the fill's edge wakes up while you hover or drag. Music and volume keep the jelly sliders. After SmoothUI's Scrubber, same timings.
- **Claude Code's plan on the Home page**: when Claude Code keeps a task list, the open island shows it live — a light streak runs under the step it is on, a finished step's box turns green and draws its check, the row dims, and "3/5" counts what is done. A finished plan goes away after 10 minutes. Atoll adds one more Claude Code hook for this (PostToolUse, only for the task list tools, run in the background so Claude never waits). After SmoothUI's AI task list, same timings.
- **Notifications stay on the Home page**: messages, Claude Code, timers, alarms, downloads, calendar and updates the island has shown (or could not show while it was hidden) gather in a stack of cards on the open island's Home page. Folded, two more cards peek out under the newest; hover or "Show all" fans them out. Swipe a card sideways to dismiss it, or click it to open what the notification opened. Kept in memory only, up to 12. After Kokonut UI's Notification stack (MIT), same timing and springs.
- **Menus grow out of a pill**: every right-click menu (the island, the tray, widgets) springs open from a small pill at the pointer — width first, then height — and its items rise in one after another. New in Settings > Layout > "Shape and look": "Menu button ☰ in the island header" — the same island menu from a button whose lines turn into a cross while it is open. After designbookmark's Dynamic menu, same springs.
- **Copy buttons say it worked**: Copy in the Claude window and the text tools, the reports on the Check page and in Settings, and the notes' copy icon — the icon turns into a check, the word rolls up to "Copied" and the button springs to fit, then rolls back; no separate notice any more. After designbookmark's Copy button, same springs.
- **Dropping a file on the island comes alive**: the circle wakes up when a file comes near, gulps it on the drop, and making a ZIP or converting pictures now shows real progress on a ring around a percentage ("12 MB of 40 MB"), ending green with a check while the two lines roll over to the result. The work runs in the background (before, the island froze while it zipped) and the island stays open until it is done. After designbookmark's File drop, same springs.
- **A liquid "+" for apps**: the "+" in Home → Apps pours out bubbles — Find, File, Folder, Downloads — fused to it like liquid while close; a bubble can be pulled out on a rubber band; picking one pulls them back in and the "+" turns into a pill saying what was added. After designbookmark's Goo menu, same springs.
- **The song's name has weight**: in the Music tab the letters near the mouse grow bold, and while the music plays a swell of bold rolls through the name by itself; on pause it rests. After designbookmark's Weight wave, same springs.
- **A rubber pill under the tabs**: the island's tabs and the Settings menu have one highlight that travels to the chosen tab and stretches like rubber on the way (its leading edge is quick, the trailing one lazy), the chosen icon pops, and the pill can be dragged along the tabs. After designbookmark's Rubber dock, same springs.
- **Jelly sliders**: every slider (the song's seek bar in the Music tab, music and app volume, the music widget, the settings) now has a jelly thumb: it follows the mouse on a spring and stretches with its speed, a drop with the value rises out of it while you drag, leaning the way you pull, and past either end it meets rubber and springs back. On the seek bar the drop shows the time, and the song jumps when you let go. The music volume has a scale under it, the marks you passed light up. After designbookmark's Jelly slider, same springs.
- **The island morphs like Morph island**: width and height are separate springs (380/24 and 300/22, after designbookmark's Morph island), so every change lands sideways first and then drops into place; the width gives a little while the height moves fast, the folded pill dips to 0.96 when pressed and gets a small push on every change, and the content swaps with a soft blur. "Fast" keeps the feel, only quicker; with "springy" off or Windows' reduced animations it glides without overshoot.
- **The cover is a record**: in the Music tab and on the music widget (M and L) the cover turns like a vinyl record while the music plays and stops where it is on pause; the ring around it is how far the song is, the middle of the record plays and pauses, and thin bars beside the title pulse with the music. After SmoothUI's "Music toggle" (MIT); with Windows' "Animation effects" off it stays still.
- **FPS only in the games you choose**: Settings → Behavior, under "FPS and load in games" — turn on "Only in the programs I choose" and add your games: from the list of running programs, or any .exe or a whole folder on disk with "Program…" / "Folder…" (any game, even one without a shortcut; a folder covers everything started from inside it). The same buttons are under "Hide the island in front of programs". Other full-screen programs then hide the island instead of showing the FPS pill.
- **No FPS pill over browsers and File Explorer**: Chrome, Edge, Firefox, Opera, Brave, Vivaldi, Yandex Browser and Explorer in full screen never count as games; with "FPS and load in games" on, the island simply hides over them.

### По-русски
- Виджеты на рабочем столе — больше 30: музыка (с живой визуализацией, синхронным текстом и эквалайзером через Equalizer APO), погода, часы, мировое время, календарь и месяц, фото, заметка, которую пишешь прямо на стекле, список дел, напоминания, обратный отсчёт, таймер, секундомер, «Не беспокоить», калькулятор, громкость, приложения, буфер обмена, быстрые действия, система (графики за 2 минуты, температура видеокарты, предупреждение только при настоящем перегреве), процессы, процессор, видеокарта, память, диск, сеть, батарея, вода, дыхание и луна. У каждого размер S, M или L, он встаёт по сетке и раскрывается по нажатию; можно перекрасить, выбрать акцент и насыщенность стекла — стекло размывает обои под собой и чуть преломляет их по краю, живые обои тоже. Удачный набор сохраняется и возвращается одним нажатием. Все виджеты — во вкладке острова «Виджеты». В играх и видео на весь экран они на паузе, а виджет под окнами ничего не считает.
- Числа в настройках — скрабберы: размер острова, скругление, прозрачность в покое, сила волн, громкость ИИ-ассистента и стекло виджетов (преломление, размытие, насыщенность у виджета) стали полосой с названием слева и значением справа — тяните за любое место или жмите стрелки; тонкая палочка у края заливки оживает при наведении и перетаскивании. Музыка и громкость остаются «желейными». По мотивам Scrubber от SmoothUI, те же тайминги.
- План Claude Code на «Главной»: когда Claude Code ведёт список задач, раскрытый остров показывает его вживую — под текущим шагом бежит светлая полоска, у готового шага рамка зеленеет и рисуется галочка, строка бледнеет, а «3/5» считает сделанное. Законченный план уходит через 10 минут. Для этого Atoll добавляет Claude Code ещё один хук (PostToolUse, только для инструментов списка задач, в фоне — Claude его не ждёт). По мотивам AI task list от SmoothUI, те же тайминги.
- Уведомления остаются на «Главной»: сообщения, Claude Code, таймеры, будильники, загрузки, календарь и обновления, которые показал остров (или не смог показать, пока был спрятан), складываются стопкой карточек на «Главной» раскрытого острова. Сложенная стопка показывает края ещё двух карточек под последней; наведите мышь или нажмите «Показать все» — раскроется. Карточку можно смахнуть в сторону или нажать — откроется то же, что открывало уведомление. Хранятся только в памяти, до 12 штук. По мотивам Notification stack от Kokonut UI (MIT), те же тайминги и пружины.
- Меню вырастают из «таблетки»: любое меню правой кнопки (остров, трей, виджеты) пружиной раскрывается из маленькой таблетки у курсора — сначала в ширину, потом в высоту, — а пункты появляются один за другим. Новое в Настройках > Раскладка > «Форма и вид»: «Кнопка меню ☰ в заголовке острова» — то же меню острова по кнопке, её полоски превращаются в крестик, пока меню открыто. По мотивам Dynamic menu от designbookmark, те же пружины.
- Кнопки копирования сами говорят, что сработало: «Копировать» в окне Claude и в инструментах текста, отчёты на странице «Проверка» и в Настройках, значок копирования у заметок — значок становится галочкой, слово перекатывается в «Скопировано», кнопка пружиной подстраивает ширину и потом возвращается; отдельное уведомление больше не всплывает. По мотивам Copy button от designbookmark, те же пружины.
- Бросок файла на остров ожил: кружок «просыпается», когда файл рядом, «глотает» его при броске, а сжатие в ZIP и конвертация картинок показывают настоящий прогресс кольцом с процентами («12 МБ из 40 МБ») и заканчиваются зелёной галочкой, пока строки перекатываются на результат. Работа идёт в фоне (раньше остров замирал на время сжатия), остров не сворачивается, пока она не закончится. По мотивам File drop от designbookmark, те же пружины.
- Жидкий «+» для приложений: «+» в «Приложениях» на Главной выпускает капли — «Найти», «Файл», «Папка», «Загрузки», — слитые с ним, как жидкость, пока рядом; каплю можно оттянуть на резинке; выбранная втягивает остальные обратно, а «+» перетекает в плашку с тем, что добавлено. По мотивам Goo menu от designbookmark, те же пружины.
- У названия песни есть «вес»: во вкладке «Музыка» буквы рядом с мышью становятся жирными, а пока играет музыка, по названию сама катится волна жирности; на паузе — затихает. По мотивам Weight wave от designbookmark, те же пружины.
- Резиновая подсветка вкладок: у вкладок острова и меню Настроек одна подсветка, которая переезжает к выбранной вкладке и по пути тянется как резина (передний край быстрый, задний ленивый), значок выбранной подпрыгивает, а саму подсветку можно протащить по вкладкам. По мотивам Rubber dock от designbookmark, те же пружины.
- Желейные ползунки: у всех ползунков (перемотка песни во вкладке «Музыка», громкость музыки и программ, виджет музыки, настройки) бегунок теперь как желе — тянется за мышью на пружине и вытягивается от скорости, при перетаскивании из него «выдувается» капля со значением и наклоняется туда, откуда тянут, а за краями встречает резину и отскакивает. На перемотке в капле — время, а песня перескакивает, когда отпускаете. Под громкостью музыки — шкала, пройденные риски подсвечиваются. По мотивам Jelly slider от designbookmark, те же пружины.
- Остров двигается как Morph island: ширина и высота — отдельные пружины (380/24 и 300/22), поэтому любое изменение сначала доезжает вбок, а потом опускается на место; пока высота быстро меняется, ширина чуть отдаёт, свёрнутая пилюля при нажатии сжимается до 0,96 и слегка вздрагивает при смене состояния, а содержимое сменяется с мягким размытием. «Быстрая» сохраняет характер, только быстрее; без «пружинистости» или с уменьшенной анимацией Windows — без перелёта.
- Обложка — пластинка: во вкладке «Музыка» и на виджете музыки (M и L) обложка крутится как винил, пока играет музыка, и замирает на паузе; кольцо вокруг — сколько песни прошло, середина пластинки — пауза и воспроизведение, а тонкие полоски у названия пульсируют в такт. По мотивам «Music toggle» от SmoothUI (MIT); при выключенных «Эффектах анимации» Windows всё неподвижно.
- FPS только в выбранных играх: Настройки → Поведение, под «FPS и нагрузка в играх» — включите «Только в выбранных программах» и добавьте свои игры: из списка запущенных программ или любой .exe либо целую папку с диска кнопками «Программа…» / «Папка…» (любая игра, даже без ярлыка; папка — всё, что запускается из неё). Те же кнопки есть у «Прятать остров поверх программ». Остальные полноэкранные программы тогда прячут остров, а не показывают пилюлю FPS.
- Нет пилюли FPS поверх браузеров и Проводника: Chrome, Edge, Firefox, Opera, Brave, Vivaldi, Яндекс Браузер и Проводник во весь экран никогда не считаются играми; при включённых «FPS и нагрузка в играх» остров над ними просто прячется.

## 1.8.1 — 2026-11-19

- **Fixed: heavy CPU load with the glass while the island stays visible to screen recording** (it does so on its own whenever NVIDIA's recorder runs). Every refresh of the glass copied the whole desktop — all monitors — on the interface thread; now it takes only the area behind the island, on a thread of its own. On several large monitors the glass also switched itself off quietly; it no longer does.
- **Fixed: on some PCs Atoll did not open unless light graphics were on.** The screen picture for the glass (Windows' Magnification API) can crash inside Windows itself, where no error handler reaches. Atoll now notices when a run died there and stops using it (the glass is solid while the island is visible to recording; Settings → Check → Try again). And if a start does not finish for any reason, the next one turns light graphics on by itself and says so.
- **Fixed: one broken feature closed the whole island.** An error now goes to the log with where it happened, and the island keeps working; if a feature fails to start, the rest still starts.
- **Less work on the interface thread**: screen brightness, the progress of other programs and messenger notifications are read in the background; the karaoke line only redraws while it moves; a sound-device object no longer piles up every few seconds.
- **Safe mode**: start Atoll with `--safe` to switch off what 1.8.0 added (volume and brightness, program progress, karaoke, the voice assistant, glass while visible to recording, your own island look) for that run. Your settings stay as they are.
- **The report says where the time goes**: the reports from Settings (smoothness, and Check → Copy report) now end with the five things the island spent most time on and how much CPU it used.
- **Fixed: the Voice button in the Chat tab was hidden under the Claude Code character.** The voice controls now sit on the left, next to the chat choice.
- **Fixed: the System tab named a graphics card that was no longer in the PC** (after swapping cards it kept showing the old one). The name now comes from the cards Windows sees right now.
- **Any program or folder in the lists**: Home → Apps (+) can now add any file or folder from disk, not only what the Start menu knows; "Hide the island in front of programs" (Settings → Behavior) takes any .exe, even one that is not running, or a whole folder — everything started from it (a game's folder, a portable program).
- **Microsoft Store version**: Claude Code and Codex reach the island again (their hook and its key now live in `%USERPROFILE%\.atoll`, a folder the Store version shares with other programs). Hooks set up earlier are moved there on their own.

### По-русски
- Исправлено: сильная нагрузка на процессор со стеклом, когда остров виден записи экрана (так бывает сам собой при запущенной записи NVIDIA). Каждое обновление стекла копировало весь рабочий стол со всех мониторов в потоке интерфейса; теперь берётся только область за островом, в отдельном потоке. На нескольких больших мониторах стекло больше не выключается само.
- Исправлено: на некоторых ПК Atoll не открывался без облегчённой графики. Снимок экрана для стекла (Magnification API) может падать внутри самой Windows, где ошибку не поймать. Теперь Atoll замечает, что прошлый запуск упал там, и больше его не использует (стекло сплошное, пока остров виден записи; Настройки → Проверка → «Попробовать снова»). А если запуск не дошёл до конца по любой причине, следующий сам включает облегчённую графику и сообщает об этом.
- Исправлено: сбой одной функции закрывал весь остров. Теперь ошибка пишется в журнал, а остров работает дальше; если функция не запустилась, остальные всё равно запускаются.
- Меньше работы в потоке интерфейса: яркость, прогресс программ и уведомления мессенджеров читаются в фоне; строка караоке перерисовывается, только пока движется; аудиоустройство теперь освобождается, а не копится.
- Безопасный режим: запуск с --safe выключает на этот раз новое из 1.8.0 (громкость и яркость, прогресс программ, караоке, голосовой ассистент, стекло при записи экрана, свой вид острова). Настройки не меняются.
- Отчёт показывает, куда уходит время: отчёты из настроек (плавность и «Проверка» → «Скопировать отчёт») теперь заканчиваются пятью самыми затратными делами острова и долей процессора.
- Исправлено: кнопку «Голос» на вкладке «Чат» закрывал персонаж Claude Code. Кнопки голоса теперь слева, рядом с выбором чата.
- Исправлено: вкладка «Система» показывала видеокарту, которой уже нет в ПК (после замены карты оставалось старое название). Теперь название берётся из карт, которые Windows видит сейчас.
- Любая программа или папка в списках: «Приложения» на Главной (+) теперь добавляют любой файл или папку с диска, а не только то, что есть в меню «Пуск»; «Прятать остров поверх программ» (Настройки → Поведение) принимает любой .exe, даже не запущенный, или целую папку — всё, что из неё запускается (папка с игрой, портативная программа).
- Версия из Microsoft Store: Claude Code и Codex снова достучатся до острова (хук и его ключ теперь лежат в %USERPROFILE%\.atoll). Ранее настроенные хуки переносятся туда сами.

## 1.8.0 — 2026-11-16

- **Resize the island by hand**: right-click the island → Resize… (or Settings → Layout). Pull the folded pill's right edge to make it wider, its bottom edge to make it bigger; Reset puts it back.
- **Your layout**: Settings → Layout — drag the tabs and the Home buttons into your own order, click one to hide it or bring it back; choose what the idle pill shows in the middle (time, time and date, date) and on the right (weather, battery, nothing).
- **App volume**: the System tab now has a slider and a mute button for every program that makes sound.
- **Welcome steps**: on the very first start, three short steps — pick a look, choose what the island shows, see how to use it. People who already use Atoll never get them.
- **Check page**: Settings → Check says in plain words what works, what doesn't and why (messages, music, weather, FPS, glass, start with Windows, updates, calendar), with a button that fixes it where a fix exists, and "Copy report" for asking for help.
- **Messages without the notification center**: where Windows keeps no notifications (the center is switched off, or access was refused), the island still says "new message" — from the unread counter in the messenger's window title. Without the text; the text needs the notification center.
- **Volume and brightness**: while either changes, a small level shows beside the island (brightness on laptops). Switch: Settings → Behavior.
- **Long jobs of other programs**: copying in Explorer, unpacking in 7-Zip or WinRAR, encoders and other programs that show a percentage in their window title appear in the pill with a ring, and "Done" when they finish.
- **Join a call from the calendar**: an event with a Zoom, Meet, Teams, Telemost… link shows it 10 minutes before and at the start — one click on the notice opens the call.
- **Glass while visible to recording**: frosted and liquid glass now also work when the island is not hidden from screenshots and screen recording (including while NVIDIA's recorder runs).
- **Your own theme**: Settings → Appearance → Your own theme — island colour, accent, scene; saved next to the built-in ones. A theme travels as one line of text: right-click yours to copy its code, paste a code to add one.
- **Follow the mouse to another monitor** (off by default): Settings → Behavior. Not yet tried on mixed-scaling setups.
- **What's new**: after an update, a notice opens the list of changes of that version, with the Telegram channel one click away. Also a button next to "Updates".
- **Voice assistant (Gemini Live)**: a live voice conversation from the Chat tab ("Voice" button; the voice is picked from the menu beside it). It can open websites, YouTube, Google, Steam, Roblox, Spotify, Telegram and Discord by voice. Needs your own Gemini API key: Settings → AI agents (or the GEMINI_API_KEY variable); its loudness is set there too.
- **Karaoke, redone**: the line being sung is shown whole and the island springs to its width the moment it changes (before, the line was cut to fit the island). Light runs across the words as they are sung, with the album's colour on its leading edge; a new line springs up from below while the old one lifts off.
- **More to make your own** (Settings → Layout): corner rounding from a pill to nearly square, see-through when idle (the folded island fades while the cursor is away and comes back as it comes near), the shadow on or off, a choice of font, a 12-hour clock and seconds, the delay before the island opens on hover, and the timer / reminder sound on or off.
- **22 new characters**: puppy, fox, panda, bear, frog, penguin, chick, piggy, koala, mouse, bee, octopus, mushroom, cactus, alien, bat, moon, sun, flame, droplet, heart and pumpkin — each watches the cursor, dances to the music and falls asleep like the first ones. And **"No character"**: the pill is just the clock.
- **A Discord call can be hidden**: Settings → Behavior → "Show a Discord call on the island". Off: it shows neither beside the island nor inside the pill.
- **A softer pull towards the cursor**: the point of the island nearest to the cursor reaches out first, and the rest of the island follows it a moment later, content and edges included. Before, the top and the far side stayed nailed in place, and with the liquid glass theme the rim stayed a plain pill while the body moved.
- **Fixed: FPS and load in games never appeared.** Since 1.6 the "FPS and load in games" switch turned off "hide in full screen", and the game was then never noticed. Now either switch notices a game.
- **Fixed: the Discord call island blinked** while the main island opened on a wide page (Settings). It now stays, or hides once when there is no room and comes back when the island folds.
- **Fixed: no more "fps: Value does not fall within the expected range" in the log** every time a game lost the front; if the FPS reading really stops, the helper's numbers are used.
- **Less to freeze on**: the Downloads folder and the Discord call check are read in the background, never on the interface thread. If the interface still stops answering for a few seconds, the log now says what it was doing.
- **Safer FPS helper**: the administrator helper refuses to write through a link placed in its folder.
- **Security hardening**: the updater refuses an installer that has no published checksum (before, it skipped the check); steps that run as administrator start Windows' own PowerShell by its full path; uninstalling leaves folders in place if their path could be misread by the command line.

### По-русски
- Голосовой ассистент (Gemini Live): живой голосовой диалог с вкладки «Чат» (кнопка «Голос»; голос выбирается в меню рядом). По голосу открывает сайты, YouTube, Google, Steam, Roblox, Spotify, Telegram и Discord. Нужен свой API-ключ Gemini: Настройки → ИИ-агенты (или переменная GEMINI_API_KEY); там же громкость.
- Караоке заново: строка показывается целиком, а остров пружиной подстраивается под её ширину в момент смены (раньше строка обрезалась под остров). По словам бежит свет в такт пению, с цветом обложки на переднем крае; новая строка выпрыгивает снизу, старая уходит вверх.
- Больше настроек под себя (Настройки → Раскладка): скругление углов от пилюли до почти квадрата, прозрачность в покое (свёрнутый остров бледнеет, пока курсор далеко, и возвращается, когда он рядом), тень вкл/выкл, выбор шрифта, 12-часовой формат и секунды, задержка раскрытия при наведении, звук таймера и напоминаний вкл/выкл.
- 22 новых персонажа: пёсик, лиса, панда, мишка, лягушка, пингвин, цыплёнок, хрюша, коала, мышка, пчёлка, осьминог, грибочек, кактус, пришелец, летучая мышь, луна, солнышко, огонёк, капелька, сердечко и тыква — каждый следит за курсором, танцует под музыку и засыпает, как первые. И «Без персонажа»: в пилюле остаются только часы.
- Звонок Discord можно скрыть: Настройки → Поведение → «Показывать звонок Discord на острове». Если выключить, звонок не показывается ни рядом с островом, ни в пилюле.
- Мягче тянется к курсору: ближайшая к курсору точка острова тянется первой, а остальной остров идёт за ней чуть позже — вместе с содержимым и краями. Раньше верх и дальний бок стояли на месте, а в теме «Жидкое стекло» кромка оставалась ровной пилюлей, пока тело двигалось.
- Исправлено: FPS и нагрузка в играх не показывались. С версии 1.6 переключатель «FPS и нагрузка в играх» выключал «прятать в полноэкранном режиме», и игра после этого не замечалась. Теперь игру замечает любой из двух переключателей.
- Исправлено: островок звонка Discord мигал, пока основной остров раскрывался на широкой странице (Настройки). Теперь он остаётся на месте либо один раз прячется, если места нет, и возвращается, когда остров сворачивается.
- Исправлено: в журнале больше нет «fps: Value does not fall within the expected range» при каждом выходе из игры; если счётчик FPS действительно остановился, берутся числа помощника.
- Меньше поводов зависнуть: папка «Загрузки» и проверка звонка Discord читаются в фоне, а не в потоке интерфейса. Если интерфейс всё же перестанет отвечать на несколько секунд, в журнале будет записано, чем он был занят.
- Безопаснее помощник FPS: помощник с правами администратора отказывается писать через ссылку, подложенную в его папку.
- Безопасность: обновление отказывается запускать установщик без опубликованной контрольной суммы (раньше проверка пропускалась); шаги с правами администратора запускают системный PowerShell по полному пути; удаление не трогает папки, если их путь может быть неверно прочитан командной строкой.
- Размер острова руками: правый клик по острову → «Изменить размер…» (или Настройки → Раскладка). Тяните свёрнутый остров за правый край — шире, за нижний — крупнее; «Сбросить» возвращает как было.
- Своя раскладка: Настройки → Раскладка — перетащите вкладки и кнопки Главной в свой порядок, нажмите, чтобы скрыть или вернуть; выберите, что пилюля показывает в центре (время, время и дата, дата) и справа (погода, батарея, ничего).
- Громкость программ: на вкладке «Система» — ползунок и кнопка «без звука» для каждой программы, которая издаёт звук.
- Первый запуск: три коротких шага — выбрать вид, что показывать на острове, как им пользоваться. Тем, кто уже пользуется Atoll, они не показываются.
- Страница «Проверка»: Настройки → Проверка простыми словами говорит, что работает, что нет и почему (сообщения, музыка, погода, FPS, стекло, автозапуск, обновления, календарь), с кнопкой «исправить» там, где это возможно, и «Скопировать отчёт», если нужна помощь.
- Сообщения без центра уведомлений: если Windows не хранит уведомления (центр отключён или нет доступа), остров всё равно скажет «новое сообщение» — по счётчику непрочитанных в заголовке окна мессенджера. Без текста: для текста нужен центр уведомлений.
- Громкость и яркость: пока они меняются, рядом с островом показывается уровень (яркость — на ноутбуках). Переключатель: Настройки → Поведение.
- Долгие задачи других программ: копирование в Проводнике, распаковка в 7-Zip и WinRAR, кодировщики и другие программы, которые пишут проценты в заголовке окна, видны в пилюле с кольцом, а по завершении — «Готово».
- Звонок из календаря: событие со ссылкой Zoom, Meet, Teams, Телемост… показывает её за 10 минут и в начале — один клик по уведомлению открывает звонок.
- Стекло при записи экрана: матовое и жидкое стекло теперь работают и тогда, когда остров не скрыт от скриншотов и записи (в том числе пока работает запись NVIDIA).
- Своя тема: Настройки → Внешний вид → Своя тема — цвет острова, акцент, сцена; сохраняется рядом со встроенными. Тема передаётся одной строкой текста: правый клик по своей теме — скопировать код, вставьте код — добавится тема.
- Переход на монитор, где курсор (по умолчанию выключено): Настройки → Поведение. На экранах с разным масштабом пока не проверено.
- Что нового: после обновления уведомление открывает список изменений этой версии, рядом кнопка Telegram-канала. Кнопка есть и возле «Обновлений».

## 1.7.1 — 2026-11-15

- **Liquid glass theme** (Settings → Appearance → Themes, first in the list): the island is clear frosted glass over what's really behind it, and near its edge the glass works like a lens — the picture under it curls over the rounded rim, with a thin colour fringe (red bends a little more than blue) and a light rim on top. Glass tiles inside. Choosing the theme turns frosted glass on.
- **No grey flash when the island opens** with frosted glass: the backdrop for the open size is taken before the island moves (when the cursor reaches it), so the glass stays see-through from the first frame instead of turning solid for a moment.

### По-русски
- Тема «Жидкое стекло»: прозрачное матовое стекло над тем, что за островом, у краёв подворачивает картинку как линза, с тонкой радужной каймой.
- При открытии острова со стеклом больше нет серой вспышки.

## 1.7.0 — 2026-11-14

### Seasons
- **Four animated season themes** (Settings → Appearance → Themes): spring flowers along the bottom and drifting petals; a summer beach with waves, a low sun and an umbrella the wind blows away now and then; autumn leaves falling and settling, warm light and a steaming cup; a cosy winter window with snow outside and a flickering candle. Kept to the edges so the text stays readable, quieter in the folded pill, about 0.3 % CPU, a still picture with light graphics.

### Smart island
- **Opens on what matters now**: a running timer, a Discord call, something you just copied, the music — or the tab you usually open at this time of day (it learns this on its own; the counts stay on this PC; *Forget habits* resets them).
- **Keeps out of the way while you type**: it doesn't pop open under a resting cursor, and messenger notices wait for a pause.
- **Low battery** (laptop, 20 % or less, not charging): light graphics turn on by themselves until you plug in.

### Island
- **Discord call as its own little island** to the right of the main one: who's talking, the call's length, a microphone button; a click brings Discord forward. The main island keeps the music or the time. Can be switched back in Settings → Behavior.
- **Dragging follows the cursor directly**: the island sits where the cursor is each frame instead of trailing it on a spring (about 3× less lag; the throw after letting go stays soft).

### По-русски
- Четыре анимированные темы-сезона: весна, лето, осень, зима.
- Умный остров: открывается на важном сейчас или на привычной вкладке, не мешает, пока печатаете, бережёт батарею.
- Звонок Discord — отдельным островком справа.
- Перетаскивание без отставания от курсора.

## 1.6.1 — 2026-11-14

- **FPS in games without signing out**: one "Yes" in the Windows prompt and FPS shows at once. Until the next sign-in a small helper (running as administrator, ending with Atoll) measures it; after that Atoll reads it by itself.
- **Weather without a VPN**: when Open-Meteo can't be reached (some providers in Russia), the weather comes from MET Norway; towns Open-Meteo doesn't know are found in OpenStreetMap.
- **Caps Lock and layout** notices are a small pill next to the island (under it when open) — the open island no longer folds.
- **NVIDIA recording**: while NVIDIA's recorder (ShadowPlay / NVIDIA app overlay) runs, the island stops hiding from screen capture, so it no longer says "Atoll prevents desktop recording". The glass turns solid meanwhile. A choice made in Settings still wins.
- **Messenger notifications**: Settings explain why messages may not appear — most often the Windows notification center is turned off by a "tweak" tool, so Windows keeps no notifications at all; or Telegram uses its own windows instead of system notifications.
- **Smoothness**: Windows 11 no longer runs the island in "efficiency mode" (slower CPU and timers for background apps, mostly on laptops). New *Settings → Behavior → Copy smoothness report* with the real frame times, graphics cards, screens and power, to send if the island stutters.

### По-русски
- FPS в играх без выхода из Windows: одно «Да» — и FPS сразу в пилюле.
- Погода без VPN: запасной сервис MET Norway и поиск городов через OpenStreetMap.
- Caps Lock и раскладка — плашкой рядом с островом, открытый остров не сворачивается.
- Запись NVIDIA больше не пишет «Atoll препятствует записи рабочего стола».
- Настройки подсказывают, почему не приходят уведомления (часто центр уведомлений отключён «твикером»).
- Atoll не попадает под энергосбережение Windows 11; кнопка «Скопировать отчёт о плавности».

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
