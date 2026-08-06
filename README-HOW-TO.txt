AlyCE Log Analyzer - v1.0
=========================

QUICK START
-----------
Double-click 'LogAnalyzer.Maui' shortcut to launch the application.

Alternatively, navigate to the 'Application' folder and run 'LogAnalyzer.Maui.exe'


INSTALLATION
------------
1. Extract this entire folder to your desired location
2. The application is ready to use - no additional installation required
3. Create a desktop shortcut by right-clicking 'LogAnalyzer.Maui' and selecting 'Send to > Desktop (create shortcut)'


FOLDER STRUCTURE
----------------
- Application/          : Contains all application files and dependencies
- LogAnalyzer.Maui     : Shortcut to launch the application
- README.txt           : This file


FEATURES
--------
✓ Analyze and parse log files from your filesystem
✓ Browse logs by folder, or drag & drop .log / .zip files onto the app
✓ Interactive filtering and search across all columns
✓ Log volume chart over time, stacked by level and driven by your filters
✓ Real-time log monitoring and live tailing
✓ Export filtered logs to CSV or original format
✓ Dark theme interface for extended viewing
✓ Column management - add/remove/resize as needed
✓ Click any row for the full entry detail, with copy-to-clipboard
✓ Collapsible 'Load files' and 'Watch settings' panels to free up space


SYSTEM REQUIREMENTS
-------------------
• Windows 10 version 19041 or later (Windows 10 21H2 preferred or Windows 11)
• Minimum 4 GB RAM recommended
• .NET Runtime is included in this package - no additional installation needed
• Microsoft Edge WebView2 Runtime - the app's interface runs inside it
  - Already included in Windows 11 and on most updated Windows 10 machines
  - The installer (Setup.exe) installs it automatically when missing
  - With the ZIP package you may need it separately, free from Microsoft:
    https://developer.microsoft.com/microsoft-edge/webview2/
    (no administrator rights required)


HOW TO USE
----------

1. LOADING LOGS:
   - Type a folder path (local or \\server\share) and click 'Load'
   - OR drag .log files / a ZIP onto the drop zone, or click it to pick them
   - Logs will be parsed and indexed automatically
   - Tick 'include DEBUG' to load debug lines too (uses much more memory)
   - A spinner and a progress bar show the current phase while loading
     ('parsing 7 / 31 files', then 'sorting and computing statistics')
   - Click the 'Load files' header to collapse the panel once you are done

2. VIEWING LOGS:
   - Explorer tab: Browse all logs with advanced filtering
   - Dashboard: View summary statistics and charts
   - Live: Monitor a single log file in real-time
     (collapse the 'Watch settings' header to free up space)
   - Triage: Manage and categorize log entries
   - In Explorer and Live, click any row to open the full entry: all fields,
     the complete message with the stack trace, and buttons to copy it

3. FILTERING:
   - Use column headers to filter by level, logger, message content
   - Multiple filters combine automatically
   - Filters persist when switching tabs

4. LOG VOLUME CHART (Explorer):
   - The chart above the grid shows entries over time, stacked by level
   - It always reflects the filters currently applied
   - The bucket size adapts to the time range (shown as e.g. '1h per bar')
   - The vertical scale follows the actual volumes - it is not a fixed maximum
   - Drag across the bars to filter the grid to that time window
     (a single click picks one bar; the x on the chip clears the window)
   - Click a level in the legend to filter by that level
   - Hover a bar for the exact counts; click the header to collapse the chart

5. EXPORTING:
   - Use Download button to export filtered results as CSV or original .log files


TROUBLESHOOTING
---------------

Application won't start:
  • Ensure you're running Windows 10.0.19041 or later
  • Try extracting to a folder with a shorter path
  • If issues persist, try running as Administrator

'WebView2 Runtime required' message, or an error mentioning
"Couldn't find a compatible WebView2 Runtime":
  • The Microsoft Edge WebView2 Runtime is missing on this machine
  • Answer 'Yes' to the message to open the download page, or get it from
    https://developer.microsoft.com/microsoft-edge/webview2/
    -> section "Evergreen Standalone Installer" (x64)
  • It installs without administrator rights and needs no reboot
  • Re-running Setup.exe also installs it automatically
  • Corporate machines may block the download - ask IT for
    "Microsoft Edge WebView2 Runtime", it is a standard Microsoft component

Logs not loading:
  • Ensure .log files are in text format
  • Check file permissions - application needs read access
  • Try a smaller subset of files first

Slow performance:
  • Close other applications to free up memory
  • Reduce the number of columns displayed
  • Filter to a smaller date/time range


VERSION
-------
Version: 1.0
Build Date: 2026-07-16 15:58:28
Target Platform: Windows x64

For issues or feature requests, please contact the development team.
