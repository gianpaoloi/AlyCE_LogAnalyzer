# AlyCE Log Analyzer — Quick Info

Read, filter and tail structured application logs on Windows. This page is also shipped in the portable ZIP as `README.txt`.

## Install

Every release is published on the [Releases page](https://github.com/gianpaoloi/AlyCE_LogAnalyzer/releases) of the repository. Choose one of the three ways below.

### 1. Setup (recommended)

1. Download `AlyCE-LogAnalyzer-Setup-{version}.exe` from the latest release.
2. Run it. No administrator rights are needed.
3. Start it from the **AlyCE Log Analyzer** Start-menu entry. Tick **Create a desktop icon** during setup if you want one.

The setup:

- installs per user, into `%LOCALAPPDATA%\TeamSystem\AlyCE Log Analyzer`;
- installs the Microsoft Edge WebView2 Runtime when it is missing;
- adds a right-click **Open with AlyCE Log Analyzer** entry for `.log` files;
- lets the app update itself later (see the Updates section).

To uninstall, use the **Uninstall AlyCE Log Analyzer** Start-menu entry.

### 2. winget (not available yet)

```
winget install TeamSystem.AlyCELogAnalyzer
```

This command works only after the package is accepted into the winget community repository. Until then, use the Setup download above.

### 3. Portable version (ZIP)

Use the portable version when you cannot or do not want to install: a locked-down machine, a network share, or a USB stick. It needs no administrator rights and writes nothing to the registry.

1. Download `AlyCE-LogAnalyzer-{version}-win-x64.zip` from the [Releases page](https://github.com/gianpaoloi/AlyCE_LogAnalyzer/releases) and extract it anywhere: a local folder, a network share or a USB stick.
2. Double-click `Launch.bat`, or run `Application\LogAnalyzer.Maui.exe` directly.

The ZIP extracts to this folder:

```
AlyCE-LogAnalyzer-{version}-win-x64/
├── Application/       the app and its runtime
├── Launch.bat         starts it from wherever you extracted it
└── README.txt         this page
```

Nothing is installed and nothing is written to the registry. The WebView2 Runtime must already be present (see the System requirements section). The ZIP does not update itself: download the new ZIP when a release is published.

### Silent install (for IT)

The Setup package accepts the standard Inno Setup switches:

```
AlyCE-LogAnalyzer-Setup-{version}.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-
```

Use `/SILENT` instead of `/VERYSILENT` to show a progress window.

## Updates

The desktop app checks for a newer release when it starts. When one exists, an **Update to v…** line appears above the version in the sidebar.

- **Installed with Setup:** click the line, read the release notes, and press **Download and install**. The app downloads the Setup package, checks its SHA256 against the release, installs it silently, and restarts on the new version.
- **Portable ZIP:** the line only links to the release page. Download the new ZIP and extract it again.

## System requirements

- Windows 10 version 17763 (1809) or later, or Windows 11. x64.
- No .NET runtime is needed; the package is self-contained.
- Minimum 4 GB RAM recommended.
- **Microsoft Edge WebView2 Runtime**: the app's interface runs inside it. It is included in Windows 11 and most updated Windows 10 machines. Setup installs it when it is missing. The ZIP needs it already present; get it from [Microsoft's WebView2 page](https://developer.microsoft.com/microsoft-edge/webview2/) (no administrator rights needed).

## Quick start

1. Start the app from the Start menu, the desktop icon, or `Launch.bat`.
2. Load logs in one of the ways below. The file-based ways use the **Load files** panel.
3. Read the result on **Overview**, then use **Explorer** for every line and **Triage** for grouped errors and warnings.

### Ways to load logs

- **Folder or file path:** type a folder path (local or `\\server\share`) and press **Load**. Click the empty path box to pick a folder you used before.
- **Drag and drop, or pick:** drag `.log` files or a ZIP onto the drop zone, or click it to choose them.
- **Grafana `.json` export:** in Grafana, use **Download logs** on the Logs page to save a `.json` file. Drag it onto the drop zone, or pick it the same way. It is read like a `.log` file, and `.log`, `.zip` and `.json` files can be loaded together.
- **Paste from the clipboard:** copy lines from another program, then press **Paste** in the panel, or press Ctrl+V anywhere on the page. The pasted lines load as one log.
- **Open with:** with the Setup install, right-click a `.log` file in Explorer and choose **Open with AlyCE Log Analyzer**.
- **Grafana Cloud:** import the logs from a Loki page, with its filters and time range. The Grafana Cloud import section below explains the steps.
- **A file that is still growing:** **File Live Watch** follows it as new lines are written. The pages section below describes it.
- **Events sent over the network:** **Network Live Watch** receives NLog events with no file at all. The pages section below shows how to set it up.

## The pages

The sidebar groups the pages in two. **File analysis** works on log files already on disk. **Live watch** works on events arriving now.

### Overview

Summary statistics, log volume over time, and breakdown charts for the loaded logs.

### Explorer

Every log line with advanced filtering. Search the message text, filter by level, environment, company or time from the column headers, and add optional columns from **Add columns…**. Click any row for the full entry, with the complete message and the stack trace.

### Triage

The same errors and warnings, grouped by message signature. Volatile parts of a message (ids, GUIDs, paths, durations) are normalised before grouping. Click a row to see the occurrences behind it.

### File Live Watch

Follow a log file while it grows, on a local path or a UNC share. Tick **from start** to read the existing contents first. Untick **auto-scroll** to read a line while the tail keeps running; **Show N new lines** catches you up.

### Network Live Watch

Receives log events pushed over the network by NLog's `NLogViewer` target, with no log file involved.

1. Add this to the sending application's `NLog.config`, then restart that application:

   ```xml
   <target name="viewer" xsi:type="NLogViewer"
           address="udp://127.0.0.1:9999"
           includeScopeProperties="true" />
   <logger name="*" minlevel="Trace" writeTo="viewer" />
   ```

   `includeScopeProperties` fills the Machine, Company, Username and Cid columns. It is off by default. On NLog 6 the target comes from the `NLog.Targets.Network` package; NLog 5 has it built in.
2. On the page, set the same port and press **Start listening**. The page shows the snippet for that port with a **Copy** button.
3. By default the page listens on this machine only (127.0.0.1). Tick **all interfaces** to also accept events from other machines.

If nothing appears, check that the port matches, that the application was restarted, and the **N datagrams ignored** badge: it means something is arriving that is not an NLogViewer event.

## Working with the logs

### Loading

- Type a folder path and press **Load**, or click the empty path box to pick a folder you used before. The last 12 paths are remembered between sessions.
- Tick **include DEBUG** to load debug lines too. This uses much more memory.
- Progress shows the current phase, for example *parsing 7 / 31 files*, then *sorting and computing statistics*.
- Click the **Load files** header to collapse the panel once you are done.

### Filtering

- Use the column headers to filter by level, logger or message content.
- Multiple filters combine.
- Filters stay in place when you switch pages.

### Log volume chart

- The chart above the Explorer grid shows entries over time, stacked by level, and always reflects the filters in use.
- The bucket size adapts to the time range, shown as for example *1h per bar*.
- Drag across the bars to filter the grid to that time window. A single click picks one bar. The **x** on the chip clears the window.
- Click a level in the legend to filter by it. Hover a bar for exact counts.

### Exporting

Use **Download** to export the filtered results as CSV or as the original `.log` lines.

## Grafana Cloud import

Overview, Explorer and Triage can load logs straight from Grafana Cloud Loki.

1. Open the Loki logs page in your browser and copy its address.
2. In the import dialog, paste the address into the link box, or press **Paste link** to read it from the clipboard. **Open link** opens the pasted link in a new tab, or the Grafana Explore page when the box is empty. Press **Load** to fill the team, service, environment, filters and time range from the link.
3. Choose a sign-in: the `grafana_session` cookie of your browser session, or a service account token. Click **?** beside the box for where to find either.
4. Press **Import logs**. Lines appear while they download, and **Cancel** keeps the lines loaded so far.

To keep the sign-in for next time, tick **Remember sign-in on this computer**. The token or cookie is stored encrypted: for this Windows account in the desktop app, or with the server's keys in the web app. **Forget sign-in** removes it. Treat the cookie or token like a password, and use **Forget sign-in** on a shared computer.

## AI assistant (optional)

The AI assistant is off until you add a provider. Log text is sent over the internet to that provider only when you ask for it, never in the background.

### Set up a provider

1. Click **AI** in the top bar.
2. Press **Add provider**, choose the type, and fill in its fields:
   - **Claude (Anthropic)** or **ChatGPT (OpenAI)**: an API key and a model.
   - **Other gateway or local model**: an address and a model, plus an API key if the server needs one.
3. Press **Test connection**. A green check appears when the provider answers. Press **Save**.
4. Press **Use for explanations** on the provider you want active. You can save several and switch between them.

The API key is stored encrypted on this computer. A key is never sent to an address over plain `http`, unless that address is this computer.

Each saved provider shows the tokens used through it. **Reset usage** clears its totals.

### Compatible providers

- **Claude (Anthropic):** the Claude API. The address is fixed. Use an API key from the Claude Console.
- **ChatGPT (OpenAI):** the OpenAI API. The address is fixed. Use an API key from the OpenAI platform.
- **Google Gemini (native API):** Gemini's own `generateContent` API. Use it for Google's endpoint, or for a gateway that serves Gemini natively, such as Databricks AI Gateway. Enter the address up to the gateway root, for example `https://<workspace>.azuredatabricks.net/ai-gateway/gemini`; the app adds `/v1beta/models/<model>:generateContent`. Enter the model name as the endpoint lists it, and the token or key the gateway requires (for Databricks, a personal access token). Google's own address is filled in by default. For a key from [Google AI Studio](https://aistudio.google.com/), keep that address, enter a model name such as `gemini-2.5-flash`, and paste the key; the app sends it in the `x-goog-api-key` header that Google requires.
- **OpenAI Responses API:** OpenAI's `/v1/responses` API, which newer OpenAI models use, and gateways that serve it, such as Databricks AI Gateway. Enter the address up to the gateway root, for example `https://<workspace>.azuredatabricks.net/ai-gateway/mlflow`; the app adds `/v1/responses`. Enter the model name as the endpoint lists it, and the token or key the gateway requires. OpenAI's own address is filled in by default.
- **Other gateway or local model:** any server that offers the OpenAI chat-completions API (`/v1/chat/completions`). Examples are a LiteLLM gateway, which can route to Gemini, Claude and other models; Ollama; LM Studio; vLLM; and the llama.cpp server. Enter the server's address, with or without `/v1`, the model name exactly as the server lists it, and an API key only if the server requires one.

Not supported directly: Azure OpenAI and other formats that are neither OpenAI's nor Gemini's. Reach them through a gateway that offers an OpenAI-compatible or Gemini-native endpoint. Only the Claude and ChatGPT types have fixed addresses; a key is never sent over plain `http` unless the address is this computer, so a remote gateway needs `https`.

### Where AI helps

- **Log detail** (Explorer, Live, Network): **Explain** describes one message and its likely cause. In Explorer, **Find related logs** lists the entries with the same CID, or the same thread within two minutes in the same environment. These related lines are included in the explanation.
- **Triage**: **Analyse clusters with AI** ranks the groups on screen into *Fix soon*, *Look at later* and *Probably noise*. Each cluster also has **Explain** and **Draft issue with AI**. The draft opens on GitHub in your browser, where you still press Submit. The app never posts it.
- **Overview**: **Summarise with AI** describes the loaded file. For a file of 50,000 entries or more, the app asks you to confirm first.
- **Explorer**: type a question in plain words, for example *errors from payments yesterday*, and **Ask** sets the filters. With a time range selected on the chart, **Why did it change?** compares that range with the same length of time before it, and **Summarise this window** describes it. A large window asks for confirmation first.
- **Live**: **Explain new errors with AI** queues each new error for your approval. Nothing is sent until you press **Send** on it.

### Review before sending

The **Review text before sending** switch sits next to the AI buttons. It is off by default.

- **Off:** the request is sent as soon as you press the button. Its text is not shown, but values such as e-mail addresses, IP addresses, passwords and tokens are masked before it leaves the computer.
- **On:** the exact text is shown and you can edit it. Send sends what is in the box.

Every answer shows the tokens its request used, when the provider reports them.

## Troubleshooting

### The application does not start

- Make sure you are running Windows 10 version 17763 or later.
- Try extracting the ZIP to a folder with a shorter path.
- If it still fails, try running it as Administrator.

### "WebView2 Runtime required", or "Couldn't find a compatible WebView2 Runtime"

- The Microsoft Edge WebView2 Runtime is missing.
- Answer **Yes** to the message to open the download page, or get it from [Microsoft's WebView2 page](https://developer.microsoft.com/microsoft-edge/webview2/), under *Evergreen Standalone Installer* (x64).
- It installs without administrator rights and needs no restart. Re-running the Setup package also installs it.
- Corporate machines may block the download. Ask IT for *Microsoft Edge WebView2 Runtime*, a standard Microsoft component.

### Logs do not load

- Make sure the `.log` files are plain text.
- Check that the application can read the files.
- Try a smaller set of files first.

### Performance is slow

- Close other applications to free memory.
- Hide columns you do not need.
- Filter to a smaller time range.

### Network Live Watch port in use

Only one copy of the app can listen on a UDP port at a time. If the port is busy, choose another port or close the other copy.

## About

The version you are running is shown at the bottom of the navigation sidebar. Click it to copy the exact version and commit. Include it in any bug report, or using the "Feedback" link directly from the app.

**Target platform:** Windows x64

For issues or feature requests, contact the author:

**Author:** Gianpaolo Iannetta
**Email:** g.iannetta[at]teamsystem.com

**LinkedIn:** [https://www.linkedin.com/in/gianpaoloiannetta/](https://www.linkedin.com/in/gianpaoloiannetta/)

Thanks for using it ;)
