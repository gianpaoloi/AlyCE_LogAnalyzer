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

There are two ways to load a dataset (from the panel at the top of the Overview / Explorer pages):

- **Folder** – type a local or UNC path and click **Load** / **Reload**. The default folder is set in
  `appsettings.json` → `LogAnalyzer:DefaultLogFolder`.
- **Upload ZIP** – click **Upload ZIP** and pick a `.zip`; every `.log` entry inside it (including files in
  sub-folders) is parsed. Useful when the logs aren't on a reachable folder/UNC share. Upload cap: 2 GB.

The **include DEBUG** checkbox applies to both. Loading runs in the background with a live progress bar.

## Pages

| Page | What it does |
|------|--------------|
| **Overview** | Load a folder / ZIP; totals (entries, files, environments, loggers, errors, warnings), time span, and breakdown charts by level / environment / logger. |
| **Dashboard** | Log volume per hour stacked by level (SVG), errors & warnings per hour, level and logger breakdowns. |
| **Explorer** | Searchable, paginated grid. Per-column combo filters, resizable columns, a hidable logger tree, column picker, and download of the filtered set. Click a row for full detail incl. formatted stack trace. |
| **Triage** | Clusters similar ERROR/WARN messages into issue groups (guids/numbers/durations/quoted values masked), ordered by frequency, with first/last-seen, affected environments and a sample stack trace. |
| **Live watch** | Tails a single file on a local or remote **UNC** path (`\\server\share\...`) and shows new matching lines in real time, with the same column filters, tree, column picker and download. |

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

## Filters persist across navigation

Filter state (levels, environments, companies, text, logger-tree selection, chosen columns, panel toggle, plus
the Live path / "from start") is held in a **scoped `SessionState`** service, which in Blazor Server lives for
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

```
Models/     LogEntry, LogFilter, LogStats/TimeBucket/MessageGroup, LogColumns (optional columns)
Services/   LogParser, MessageNormalizer, LogStore (dataset + folder/ZIP loading), LogWatcher (live tail),
            LogExport (CSV / JSON-lines), SessionState (per-circuit filter state), ChartColors
Components/
  Pages/    Home(Overview), Dashboard, Explorer, Triage, Live
  Shared/   LoadPanel, LevelBadge, BarChart, LoggerTree, LogDetail
  Layout/   MainLayout (collapsible sidebar), NavMenu
wwwroot/    app.css (dark theme + component styles), download.js (file download interop)
```
