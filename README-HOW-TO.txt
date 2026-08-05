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
✓ Collapsible 'Load files' panel to free up screen space


SYSTEM REQUIREMENTS
-------------------
• Windows 10 version 19041 or later (Windows 10 21H2 preferred or Windows 11)
• Minimum 4 GB RAM recommended
• .NET Runtime is included in this package - no additional installation needed


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
   - Triage: Manage and categorize log entries

3. FILTERING:
   - Use column headers to filter by level, logger, message content
   - Multiple filters combine automatically
   - Filters persist when switching tabs

4. LOG VOLUME CHART (Explorer):
   - The chart above the grid shows entries over time, stacked by level
   - It always reflects the filters currently applied
   - The bucket size adapts to the time range (shown as e.g. '1h per bar')
   - Hover a bar for the exact counts; click the header to collapse the chart

5. EXPORTING:
   - Use Download button to export filtered results as CSV or original .log files


TROUBLESHOOTING
---------------

Application won't start:
  • Ensure you're running Windows 10.0.19041 or later
  • Try extracting to a folder with a shorter path
  • If issues persist, try running as Administrator

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
