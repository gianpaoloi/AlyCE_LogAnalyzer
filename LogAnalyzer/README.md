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

There are two ways to load a dataset (from the panel at the top of the Overview / Dashboard / Explorer /
Triage pages):

- **Folder** – type a local or UNC path and click **Load** / **Reload**. The default folder is set in
  `appsettings.json` → `LogAnalyzer:DefaultLogFolder`.
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

**Live watch** has no load panel (it tails one file rather than loading a set), but its **Watch settings**
card collapses the same way — same `.collapse-header` / `.collapse-hidden` styling, its own
`SessionState.LiveSettingsCollapsed` flag, and a summary showing the watched file name plus the active text
filter.

## Pages

| Page | What it does |
|------|--------------|
| **Overview** | Load a folder / ZIP; totals (entries, files, environments, loggers, errors, warnings), time span, and breakdown charts by level / environment / logger. |
| **Dashboard** | Log volume per hour stacked by level (SVG), errors & warnings per hour, level and logger breakdowns. |
| **Explorer** | Searchable, paginated grid, topped by a **log volume time series** of the filtered set that doubles as a filter (drag a time window, click a level in the legend). Per-column combo filters, resizable columns, a hidable logger tree, column picker, and download of the filtered set. Click a row for full detail incl. formatted stack trace. |
| **Triage** | Clusters similar ERROR/WARN messages into issue groups (guids/numbers/durations/quoted values masked), ordered by frequency, with first/last-seen, affected environments and a sample stack trace. |
| **Live watch** | Tails a single file on a local or remote **UNC** path (`\\server\share\...`) and shows new matching lines in real time, with the same column filters, tree, column picker, download and click-a-row detail. Its **Watch settings** card collapses like the load panel. |

## Explorer & Live features

- **Fixed columns** – Time, Level, Environment, **Company**, Message.
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
  message rendered with real newlines, and *Copy message* / *Copy details* buttons. On **Live watch** the row
  is passed as a snapshot, so the tail keeps buffering behind the dialog without changing what you're reading.

## Log volume chart (Explorer)

Above the grid, `LogVolumeChart` draws a Grafana-style volume time series of the **currently filtered** rows —
so it narrows down with every search, level pick or logger-tree click.

- **Stacked bars per time bucket** – debug / info / warn / error bottom-to-top, using the same
  `ChartColors.Level` palette as the badges and dashboard. Levels outside the known set (the parser stores
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
chosen columns, panel toggle, the load-panel / volume-chart / watch-settings collapsed states, plus the Live
path / "from start") is held in a **scoped
`SessionState`** service, which in Blazor Server lives for
the whole SignalR circuit — so filters survive moving between pages and return when you come back. They reset
only on a full page reload / reconnect. Explorer and Live keep their own independent filter state.

## Layout / navigation

- The app uses the launcher-style **dark navy / purple** theme (bg `#0f1021`, panels `#17182f`, brand
  `#8a63f4`, accent cyan `#21d4fd`). Radzen's `material-dark` theme is re-mapped to this palette in `app.css`.
- The left navigation can be **collapsed** with the ☰ button in the top bar; when collapsed the content area
  drops its width cap so the grids use the full window width.

## Notes

- **DEBUG lines** are excluded by default on load (they are ~90% of volume). Tick *include DEBUG* to load
  everything — uses much more memory.
- Low-cardinality strings are interned during load to keep memory reasonable.
- Stack traces are stored inline in `message` with `\CRLF` markers; the UI renders them as real newlines.
- The live watcher polls the file (default 750 ms) rather than using `FileSystemWatcher`, so it works over
  network shares and while another process is writing.
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
              LogExport (CSV / JSON-lines), SessionState (per-circuit UI state), ChartColors
  Components/
    Pages/    Home(Overview), Dashboard, Explorer, Triage, Live, QuickStart, NotFound
    Shared/   LoadPanel (collapsible header), LoadProgress (spinner + phase), LogVolumeChart,
              LevelBadge, LoggerTree, LogDetail
    Layout/   MainLayout (collapsible sidebar), NavMenu
Components/ App.razor, Routes.razor, Pages/Error.razor (host shell only)
wwwroot/    app.css (dark theme + component styles), download.js (download + drop-zone interop)
```

> `wwwroot/app.css` is duplicated in `LogAnalyzer.Maui/wwwroot/app.css` — component styles (e.g. the
> `.lv-*` volume-chart, `.load-busy-*` spinner and `.collapse-*` rules) must be added to **both** copies.
