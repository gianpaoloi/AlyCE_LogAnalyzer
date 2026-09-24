# AlyCE Log Analyzer

A Blazor Server app (UI built with the free **[Radzen.Blazor](https://blazor.radzen.com)** component
library, **material-dark** theme) for analyzing TeamSystem AlyCE JSON-lines log files (`all_*.log`).
Each line is one JSON object: `time, level, threadid, environment, username, company, cid, message, logger`.

## Run

```powershell
cd LogAnalyzer
dotnet run
```

Then open the URL printed in the console (e.g. `http://localhost:5134`).

## Loading logs

There are two ways to load a dataset (from the panel at the top of the Overview / Explorer / Triage
pages):

- **Folder** – type a local or UNC path and click **Load** / **Reload**. The default folder is set in
  `appsettings.json` → `LogAnalyzer:DefaultLogFolder`. The box is a `RadzenAutoComplete` that **suggests
  folders you loaded before** (see *Path history* below).
- **Drop zone** – click it to pick `.log` / `.zip` files, or drag them onto it. For a ZIP, every `.log` entry
  inside it (including files in sub-folders) is parsed. Useful when the logs aren't on a reachable
  folder/UNC share. Caps: 2 GB per ZIP, 500 MB per `.log`, 100 files per drop. One ZIP *or* one-or-more
  `.log` files per drop — mixing the two is rejected.

The **include DEBUG** checkbox applies to both. **Clear** drops the loaded dataset.

Loading runs in the background and reports progress in three places — a **spinner with the current phase**
where the Load button sits (or in the collapsed header), a determinate **progress bar** under the panel, and
the same spinner in place of the page body until the dataset is ready. The phase text names what the store is
actually doing, since file counters alone would look stalled:

| Phase | Shown as |
|---|---|
| Enumerating the folder / ZIP | *Looking for log files…* |
| Parsing | *Parsing… 7 / 31 files · 412,908 entries* |
| Post-parse sort + statistics | *Sorting and computing statistics…* |

The drop zone dims and stops accepting input while a load is in flight (drops were already ignored — now it
says so).

The panel is **collapsible on every page that shows it** — click the *Load files* header to fold it away and
give the grid / charts more room. While collapsed the header keeps a one-line summary (entries, files and
source path, or the load progress). The collapsed state is shared by all pages and persists across navigation
like the filters, so folding it once keeps it folded everywhere until a full page reload. The body stays in the
DOM while collapsed, so the drop zone and a half-typed folder path survive a collapse/expand round-trip.

**File Live Watch** has no load panel (it tails one file rather than loading a set), but its **Watch settings**
card collapses the same way — same `.collapse-header` / `.collapse-hidden` styling, its own
`SessionState.LiveSettingsCollapsed` flag, and a summary showing the watched file name plus the active text
filter. **Network Live Watch**'s *Listener settings* card is the same again
(`SessionState.NetworkSettingsCollapsed`, summary = the endpoint plus the text filter).

Both cards **collapse themselves on a successful start**, like the load panel does on a successful load: once
events are arriving, the path or the port is not what you are looking at. A start that fails does *not*
collapse — the card is where the path gets corrected or the port changed, and on the network page it also
carries the snippet the sender needs.

## Staying responsive on a long watch

A tail that runs for hours produces faster than a browser can render, and several things used to grow
without bound while it did. Four limits keep a long watch flat:

These apply to **both** live pages: the buffer, the caps and the ring live in `LiveEntryBuffer`, which
`Live.razor` and `Network.razor` share, and the network listener adds a cap of its own (a 200 ms publish
batch instead of one event per datagram).

- **One throttled refresh.** Neither live page queues a render per poll (plus one per status change).
  A single loop refreshes at most every 400 ms, only when something changed, and **awaits** each render, so
  it can never outrun the renderer and the render queue cannot pile up. User actions still render at once.
- **Capped read per poll.** `LogWatcher` decodes at most 4 MB per tick instead of the whole remainder;
  catching up from the start of a big file or after a rotation no longer allocates a huge string on the
  large-object heap. The rest follows on the next tick. The chunk is decoded with a `Decoder` kept across
  polls, which also fixes a multi-byte character split across two chunks decoding to garbage.
- **Capped intern pool.** `LogParser` interns repeated strings; the pool is now capped at 20 000 entries.
  `LogWatcher` keeps one parser for the life of the app, so an uncapped pool meant every distinct
  `username` / `cid` / `company` seen during a watch was retained for ever.
- **Capped filter values.** The Environment / Company header combos stop collecting new values at 500 each
  and the logger tree at 2 000 loggers — known values keep counting. Otherwise every refresh got slower as
  the dropdowns and the tree grew.

## Auto-scroll (File Live Watch)

New lines normally flow into the grid as they are read, which makes a row move while you are reading or
clicking it. Untick **auto-scroll** to hold the view still (`SessionState.LiveAutoScroll`, on by default):

- Switching it off takes a copy of the buffer and the grid renders that copy, so nothing on screen moves.
  The column filters, logger tree and download still work on it — they just operate on the frozen set.
- The tail keeps running underneath: lines are still parsed and buffered, and the status bar keeps counting
  (*lines read*, *buffered*, *last*). An **auto-scroll off** badge and a **Show N new lines** button appear
  there, so the pause is visible and can be released even with the settings card collapsed.
- **Clear** while paused empties the frozen view too, and new lines are counted as pending.
- The buffer is per-page, so leaving the page and coming back starts from an empty grid — auto-scroll turns
  itself back on there, since there is nothing left to hold still.

## Path history

Both path boxes — the **log folder** on the load panel and the **file** on File Live Watch — are autocompletes
that suggest paths already used on this machine. Click into an empty box to see the full list
(`OpenOnFocus`, `MinLength="0"`), or keep typing to filter it (case-insensitive *contains*).

- A path is recorded **only once it works** — after a load finishes without error, for File Live Watch once the
  watcher actually opened the file (`Watcher.IsWatching`), and immediately for anything picked in the file
  browser. Typos never reach the suggestions.
- Most recent first, de-duplicated case-insensitively (Windows paths), capped at 12 entries.
- Stored by `Services/PathHistory.cs` in the browser's **localStorage** under `alyce.pathHistory.*`, via the
  `pathHistoryLoad` / `pathHistorySave` helpers in `download.js`. `SessionState` was the wrong home for this:
  it only lives for the circuit, and history has to survive an app restart. localStorage also keeps it
  per user and behaves identically in the MAUI WebView.
- Every interop call is wrapped — pre-render, a dropped circuit, or storage blocked by policy just means no
  suggestions, never a failed load.

> Windows' own shell MRU list isn't reachable from inside a WebView, so this is the app's own history. A
> native folder picker would be possible in the MAUI host only (the server host would open the dialog on the
> wrong machine) — hence the in-app browser below, which behaves the same in both hosts.

## File browser (Browse…)

**File Live Watch** has a **Browse…** button that opens `Components/Shared/FileBrowserDialog.razor`, so the file to
tail never has to be typed. It browses the machine that *reads* the logs — the server for the web host, the
desktop for MAUI — which is the same machine `LogWatcher` tails from, so local and UNC paths both work.

- Drives → folders → files. Folders sort by name, files **newest first** (usually the log to tail). Size and
  Modified are shown; only `.log` is listed until *all files* is ticked.
- Click a folder to open it, a file to select it, then **Select** — **the watch starts straight away**, no
  second click on *Start watching*. (Switch files by stopping first: *Browse…* is disabled while watching.)
- The path box inside the dialog is only there
  to reach a share that cannot be browsed into (`\\server` isn't enumerable) — Enter or **Go** navigates.
- Every filesystem probe runs **off the UI thread under a 3 s timeout**. `File.Exists` / `Directory.Exists` /
  `DriveInfo.IsReady` block for ~30 s on a wrong or offline UNC path, which would otherwise freeze the app
  before the dialog even paints. The existence checks and the listing share one probe, so a bad path costs one
  wait, not three. An abandoned probe is left to finish by itself — a blocking share call can't be cancelled.
- A wrong, unreachable or empty path falls back to the **system drive** (`C:\`), with the reason shown in a
  warning; the drive list is only the last resort if even that fails. Press ↑ at a drive root to reach it.
- **Favorites** — *Add to favorites* pins the selected file, or the folder on screen if nothing is selected.
  Pinned entries appear as chips (last path segment, full path in the tooltip); click one to jump there, `×`
  to unpin. Stored by `PathHistory` under `alyce.pathHistory.favorites`, capped at 30, so they survive a
  restart like the rest of the path history.

Starting a watch is off the UI thread for the same reason: a wrong path used to freeze the page inside the
watcher's existence check. The button reads *Opening…* and is disabled while the path is being resolved.

## Network Live Watch (NLogViewer listener)

The second live page takes its entries off a **UDP socket** instead of a file, from NLog's
[`NLogViewer` target](https://github.com/NLog/NLog/wiki/NLogViewer-target) — for a process whose log file is
somewhere you cannot reach, is rotated away too quickly, or does not exist because the interesting run lasts
half a minute. The sender's half of the setup is one target and one rule:

```xml
<target name="viewer" xsi:type="NLogViewer" address="udp://127.0.0.1:9999"
        includeScopeProperties="true" />
<logger name="*" minlevel="Trace" writeTo="viewer" />
```

The page shows that snippet for the port currently entered, with a **Copy** button, because a listener that
nobody is sending to looks exactly like one that is working. Two things about the sender side, both verified
against NLog 6 rather than read off the wiki:

- **`includeScopeProperties="true"` is not optional in practice.** It defaults to false, and without it the
  scope properties never reach the wire — so Machine, Company, Username and Cid stay empty on every row.
  Event properties (`logger.Info("… {orderId}", id)`) are included without it.
- On **NLog 6** the target lives in the **`NLog.Targets.Network`** package (NLog 5 has it built in). It is
  auto-loaded once referenced, so no `<extensions>` entry is needed.

- **Binding.** `NetworkLogListener.StartAsync(port, allInterfaces)` binds `127.0.0.1` by default — enough for
  a sender on this machine, and it opens no port to the network. *all interfaces* binds `0.0.0.0` for events
  from other machines. A port already taken (a second copy of the app, or the real NLog viewer) is reported
  as such in the status bar and as a toast, rather than leaving a page that claims to be listening.
- **Wire format.** `Log4JXmlEventLayout` — a `<log4j:event>` XML fragment per event, with **no delimiter**
  between events and with the `log4j:` / `nlog:` prefixes never declared on it. `Log4JXmlParser` wraps each
  fragment in a root that declares both and reads it with DTDs prohibited and no resolver: the input comes
  off a socket, so entity expansion and external references must be impossible, not merely unlikely.
- **Framing.** One datagram usually carries one event, but neither direction can be relied on: the target
  splits a payload over `maxMessageSize` (65 000 bytes) across several sends, and several small events can
  share one. `Log4JEventSplitter` keeps one decode buffer **per sender** and cuts on `</log4j:event>`, so a
  split event is reassembled and a batched datagram is unpacked. A buffer that cannot become an event is
  dropped at once and counted (the *N datagrams ignored* badge); one that opens an event and never closes it
  is dropped at 4 MB.
- **Batching.** Events are published to the page every 200 ms rather than one at a time, because each batch
  costs the page a lock and a rebuild of the header-combo values. Stopping publishes what is still queued
  instead of dropping it.
- **UDP, deliberately.** Nothing blocks and nothing retries, so pointing a production process at a listener
  that is not running costs it nothing — and a receiver that cannot keep up loses datagrams rather than
  slowing the sender down. `tcp://` and `http://` addresses are *not* supported by this listener. There is no
  authentication or encryption on the wire either: it is a diagnostic channel, and *all interfaces* accepts
  log events from anything that can reach the port.

### How a log4j event becomes a `LogEntry`

A log4j event and an AlyCE log line do not carry the same fields, so the mapping is explicit — and the two
columns that hold something else are **renamed on this page** (`LogColumns.Network`) rather than mislabelled:

| Column | Comes from | Notes |
|---|---|---|
| Time | `timestamp` attribute | Epoch milliseconds (UTC), shown in local time. An ISO string is also accepted. |
| Level | `level` attribute | Upper-cased, so NLog's `Warn` and a file's `WARN` are one level in the filter. |
| **Machine** | `log4jmachinename` property | The Environment column, renamed: an event has no environment field. An explicit `environment` property wins when the sender sets one. |
| Company | `company` property | A scope property (`ScopeContext.PushProperty("company", …)`) or an event property. |
| Message | `log4j:message` | Real newlines are stored as `\CRLF` markers, as in the file format. |
| Logger | `logger` attribute | Feeds the logger tree unchanged. |
| Thread | `thread` attribute | |
| Username | `username` / `user` property | |
| **Cid / NDC** | `cid` / `correlationid` property | Falls back to `log4j:NDC`, the sender's own nested context. |
| **Application** | `log4japp` property | The Source file column, renamed. Falls back to the sender's `address:port`. |
| *stack trace* | `log4j:throwable` | Appended behind a `stackTrace:` marker, so the row gets its **stack** badge and the detail dialog its trace. `log4j:locationInfo` (with `includeCallSite` / `includeSourceInfo`) is prepended to it as an `at …` line. |

Everything else an event may carry — `nlog:eventSequenceNumber`, the assembly in `nlog:locationInfo`,
unrecognised properties — is read past and dropped.

## Pages

The sidebar groups them under two headings, because they answer two different questions — **File analysis**
(Overview, Explorer, Triage) works on a set of files already on disk, **Live watch** (File Live Watch,
Network Live Watch) on events arriving right now.

| Page | What it does |
|------|--------------|
| **Overview** | Load a folder / ZIP; totals (entries, files, environments, loggers, errors, warnings), time span, log volume per time bucket stacked by level, errors & warnings per bucket, and breakdown charts by level / environment / logger. |
| **Explorer** | Searchable, paginated grid, topped by a **log volume time series** of the filtered set that doubles as a filter (drag a time window, click a level in the legend). Per-column combo filters, resizable columns, a hidable logger tree, column picker, and download of the filtered set. Click a row for full detail incl. formatted stack trace. |
| **Triage** | Clusters similar ERROR/WARN messages into issue groups (guids/numbers/durations/quoted values masked), ordered by frequency, with first/last-seen, affected environments and a sample stack trace. |
| **File Live Watch** | Tails a single file on a local or remote **UNC** path (`\\server\share\...`) — picked with **Browse…** or typed — and shows new matching lines in real time, with the same column filters, tree, column picker, download and click-a-row detail. Its **Watch settings** card collapses like the load panel. |
| **Network Live Watch** | Listens on a **UDP port** for events pushed by NLog's `NLogViewer` target — no log file involved — and shows them in the same grid, with the same filters, tree, column picker, download and click-a-row detail. Its **Listener settings** card collapses the same way, and carries the target snippet to paste into the sender's `NLog.config`. See [above](#network-live-watch-nlogviewer-listener). |

## Explorer & Live features

The two live pages render **one component**, `Components/Shared/LiveGrid.razor` — same columns, same header
filters, same logger panel — and buffer through **one class**, `Services/LiveEntryBuffer.cs`, which is where
the caps in [Staying responsive on a long watch](#staying-responsive-on-a-long-watch) live. Each page keeps
only its own settings card, its own producer (`LogWatcher` / `NetworkLogListener`) and its own filter state.

- **Fixed columns** – Time, Level, Environment (**Machine** on Network Live Watch), **Company**, Message.
- **Per-column combo filters** – Level, Environment and Company each have a multi-select combo **in the column
  header** (populated with the distinct values from the data). Selections persist when the combo is reopened
  and drive the filtering directly. Time / Message keep the built-in simple filters.
- **Column picker** – an *Add columns…* dropdown adds any other JSON field as a column: Username, Thread
  (threadid), Cid, Logger, Source file.
- **Resizable columns** – drag any column border (`AllowColumnResize`).
- **Logger tree** – a hidable right-hand panel (**Loggers** toggle, hidden by default) shows a tree of logger
  namespaces (split on `.`, counts rolled up to ancestors) and is **fully expanded** when shown. Click a node
  to filter by that logger prefix.
- **Download** – a **Download** split-button exports the **currently filtered** rows as:
  - **CSV** (`.csv`, UTF-8 + BOM for Excel; message stack-trace `\CRLF` markers become real newlines), or
  - **Log lines** (`.log`, original JSON-lines format, so the subset can be re-loaded).
- **Row detail** – clicking any row opens the `LogDetail` dialog (draggable, resizable) with every field, the
  message rendered with real newlines, and *Copy message* / *Copy details* buttons. On **File Live Watch** the row
  is passed as a snapshot, so the tail keeps buffering behind the dialog without changing what you're reading.

## Log volume chart (Explorer)

Above the grid, `LogVolumeChart` draws a Grafana-style volume time series of the **currently filtered** rows —
so it narrows down with every search, level pick or logger-tree click.

- **Stacked bars per time bucket** – debug / info / warn / error bottom-to-top, using the same
  `ChartColors.Level` palette as the badges and the overview charts. Levels outside the known set (the parser stores
  `UNKNOWN` when the field is missing) land in an **other** series, so the bars always add up to the row count.
- **Automatic bucket size** – picked from a round-step ladder (1 s → 30 d) so the chart stays under ~180 bars
  whatever the time span; the chosen step is shown in the header (*"1h per bar"*). Empty buckets are kept, so
  the time axis stays linear and gaps are visible.
- **Scale follows the volume** – the y axis is *not* a fixed ceiling. A round tick step
  (1 / 2 / 2.5 / 5 × 10ⁿ, never below 1 entry) is chosen to split the busiest bucket into ~4 bands, and the
  axis top is the first multiple of that step at or above the peak — so a 40-entry peak gives 10/20/30/40 and
  a 930 K peak gives 250 K/500 K/750 K/1 M. The tallest bar always fills 70–100 % of the height.
- **Legend** – per-level totals for the filtered set; **click an entry to filter by that level** (see below).
- **Hover a bar** for its bucket start and per-level counts.
- **Collapsible** – click the *Log volume (930K)* header; the state persists across navigation.

### Filtering from the chart

- **Drag across the bars** to filter the grid to that time window; a plain **click** picks the single bucket
  under the cursor. Bars inside the window stay lit, the rest dim, and the window appears as a chip in the
  header (`08/03 14:00:00 → 16:59:59`) with an **✕** to clear it. **Reset** clears it too.
- The chart is deliberately fed the rows matching **every filter except the time window**, so selecting a
  window zooms the *grid* but leaves the whole timeline on screen — you can widen, move or drop the selection
  without first clearing it. Re-slicing on a drag reuses that already-filtered list instead of re-querying.
- **Click a legend entry** to toggle that level: it selects every raw level mapping to the series (so *warn*
  covers `WARN` and `WARNING`, *error* covers `ERROR` and `FATAL`) and lights up while active, driving the
  same `ExplorerLevels` filter as the Level column header. Clicking it again removes those levels.
- The window is stored as a single `TimeRange` in `SessionState.ExplorerRange` — one value, so a start without
  an end can't happen. It is applied *after* `LogStore.Query`, as a slice of the chart's own row list, rather
  than through `LogFilter.From` / `To`; that is what keeps the chart's timeline independent of the selection.

It is a plain HTML/CSS component (no SVG, no JS), so it reflows with the container. Bucketing is two passes
over the filtered set and only re-runs when the filtered list changes — on very large filtered sets (~1 M rows)
expect a short pause per filter change.

## Filters persist across navigation

Filter state (levels, environments, companies, text, logger-tree selection, the volume chart's time window,
chosen columns, panel toggle, the load-panel / volume-chart / watch-settings / listener-settings collapsed
states, plus the Live path / "from start" and the Network port / "all interfaces") is held in a **scoped
`SessionState`** service, which in Blazor Server lives for
the whole SignalR circuit — so filters survive moving between pages and return when you come back. They reset
only on a full page reload / reconnect. Explorer, File Live Watch and Network Live Watch each keep their own
independent filter state.

## Layout / navigation

- The app uses the launcher-style **dark navy / purple** theme (bg `#0f1021`, panels `#17182f`, brand
  `#8a63f4`, accent cyan `#21d4fd`). Radzen's `material-dark` theme is re-mapped to this palette in `app.css`.
- The left navigation can be **collapsed** with the ☰ button in the top bar; when collapsed the content area
  drops its width cap so the grids use the full window width.
- Nav items sit under two `.nav-group` headings (`<h2>`, so they are landmarks to a screen reader rather than
  just smaller text): *File analysis* and *Live watch*. The rail is **230px** wide and labels never wrap —
  *Network Live Watch* on a narrower rail broke onto three lines and pushed its icon onto a line of its own.

## Notes

- **DEBUG lines** are excluded by default on load (they are ~90% of volume). Tick *include DEBUG* to load
  everything — uses much more memory.
- Low-cardinality strings are interned during load to keep memory reasonable.
- Stack traces are stored inline in `message` with `\CRLF` markers; the UI renders them as real newlines.
- The live watcher polls the file (default 750 ms) rather than using `FileSystemWatcher`, so it works over
  network shares and while another process is writing.
- The network listener binds a UDP port on the machine running this host, which for the web build is the
  **server**, not the browser: a sender has to reach *that* machine, and `udp://127.0.0.1` only works for a
  process running on it. `NetworkLogListener` is scoped like `LogWatcher`, so a second circuit starting a
  listener on the same port is told the port is in use rather than quietly sharing the events.
- The whole app runs in `InteractiveServer` render mode (set once on `Routes`/`HeadOutlet` in `App.razor`).

## Layout

Models, services and all UI components live in the shared **`LogAnalyzer.Core`** project, which both this
server app and `LogAnalyzer.Maui` reference. This project only holds the web host shell (`Program.cs`,
`App.razor`, `Routes.razor`, the error page) and its own `wwwroot`.

```
../LogAnalyzer.Core/
  Models/     LogEntry, LogFilter, LogStats/TimeBucket/MessageGroup, LogColumns (optional columns),
              TimeRange (chart selection)
  Services/   LogParser, MessageNormalizer, LogStore (dataset + folder/ZIP loading), LogWatcher (live tail),
              NetworkLogListener (UDP NLogViewer listener), Log4JXmlParser (log4j event -> LogEntry),
              Log4JEventSplitter (per-sender framing), LiveEntryBuffer (ring + caps, shared by both
              live pages), LogExport (CSV / JSON-lines), SessionState (per-circuit UI state),
              PathHistory (recent paths in localStorage), ChartColors
  Components/
    Pages/    Home(Overview), Explorer, Triage, Live, Network, QuickStart, NotFound
    Shared/   LoadPanel (collapsible header), LoadProgress (spinner + phase), LogVolumeChart,
              LiveGrid (the grid both live pages render), LevelBadge, LoggerTree, LogDetail
    Layout/   MainLayout (collapsible sidebar), NavMenu
Components/ App.razor, Routes.razor, Pages/Error.razor (host shell only)
wwwroot/    app.css (dark theme + component styles),
            download.js (download, clipboard, drop-zone and path-history interop)
```

> `wwwroot/app.css` is duplicated in `LogAnalyzer.Maui/wwwroot/app.css` — component styles (e.g. the
> `.lv-*` volume-chart, `.load-busy-*` spinner and `.collapse-*` rules) must be added to **both** copies.
