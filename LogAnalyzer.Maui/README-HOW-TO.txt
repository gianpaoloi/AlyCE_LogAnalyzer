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
✓ Browse logs by folder or upload as ZIP files
✓ Interactive filtering and search across all columns
✓ Real-time log monitoring and live tailing
✓ Export filtered logs to CSV or original format
✓ Dark theme interface for extended viewing
✓ Column management - add/remove/resize as needed


SYSTEM REQUIREMENTS
-------------------
• Windows 10 version 19041 or later (Windows 10 21H2 preferred or Windows 11)
• Minimum 4 GB RAM recommended
• .NET Runtime is included in this package - no additional installation needed


HOW TO USE
----------

1. LOADING LOGS:
   - Click 'Load Logs' and select a folder containing .log files
   - OR upload a ZIP file containing .log files
   - Logs will be parsed and indexed automatically

2. VIEWING LOGS:
   - Explorer tab: Browse all logs with advanced filtering
   - Dashboard: View summary statistics and charts
   - Live: Monitor a single log file in real-time
   - Triage: Manage and categorize log entries

3. FILTERING:
   - Use column headers to filter by level, logger, message content
   - Multiple filters combine automatically
   - Filters persist when switching tabs

4. EXPORTING:
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
