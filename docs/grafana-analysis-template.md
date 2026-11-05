# Grafana log analysis template

Use this template to analyze a log export downloaded from Grafana (`Logs-YYYY-MM-DD HH_MM_SS.json`)
with AlyCE Log Analyzer. The analyzer reads the export directly; no conversion step is needed.

## 1. Load and triage in the analyzer

1. Drag the `.json` export onto the drop zone (or pick it with the file picker), or enter its path in
   the path box and click **Load**. `.log`, `.zip` and `.json` files can be loaded together.
2. **Overview**: check the volume chart and level breakdown. Note the busiest hours.
3. **Triage**: sort by count. Each row is one error signature (digits masked), so this is the
   fastest way to see which problems dominate.
4. **Explorer**: filter to one signature, then click a row to read the full entry and stack trace.
   Entries from the export show the export's file name as their source.

## 2. Import directly from Grafana Cloud

Instead of downloading an export, the analyzer can query Loki itself. Click **Grafana** on the load panel.

1. **Paste the link.** Copy the address bar of the Loki logs page (the Explore link) and paste it into the
   top box, then click **Load**. The team, service_name, environment, datasource and time range are filled in
   from the link. If the link has exact dates, tick **Use the link's dates**. Level, field (such as `cid`) and
   line-text filters are added to the query as well; **Drop** removes them.
2. **Sign in with `grafana_session`.** The app needs your browser's Grafana session cookie, because SSO does not
   give an API token. Copy it as follows (Edge and Chrome; Firefox uses the **Storage** tab, then **Cookies**):
   1. Sign in to Grafana in the browser and keep the Grafana page open.
   2. Press **F12** (or right-click the page and choose **Inspect**).
   3. Open the **Application** tab (**Storage** in Firefox). If you don't see it, use the **»** arrow to find it.
   4. In the left tree expand **Cookies**, then click the Grafana address, for example
      `https://teamsystem.grafana.net`.
   5. In the list find the row named **`grafana_session`**. Click its **Value** cell, select all (Ctrl+A) and copy
      (Ctrl+C). Copy only the value, not the name.
   6. In the dialog, click the **grafana_session cookie** box (it selects everything) and paste (Ctrl+V).
3. **Import logs.** Press **Import logs**. Tick **Remember sign-in on this computer** to keep the cookie and the
   datasource for next time. **Forget sign-in** removes them.

Notes:
- The cookie is as sensitive as a password: anyone who has it can act as you in Grafana. Don't paste it into
  tickets, chats or screenshots, and use **Forget sign-in** on a shared computer.
- The cookie ends when your Grafana session ends. If the import says Grafana did not accept the cookie, sign
  in to Grafana again and copy the value again.
- If you can't find `grafana_session` in the list, note the cookie names you do see and ask for help; the name
  can differ on some Grafana setups.
- A service account token can be used instead of the cookie. It needs an admin to create it.

## 3. Format notes for Grafana exports

| Item | What to know |
|------|--------------|
| `line` | The original webapi record as a JSON string. The analyzer reads it directly. |
| `date` | UTC, ISO-8601 with `Z`. Use it when correlating with other systems. |
| `line.time` | Local time of the server, without offset. In winter it is UTC+1 (`11:04:34` local = `10:04:35Z`). |
| `fields` | Grafana labels: `cid`, `company`, `hostname`, `message`. Same values as `line`; not needed. |
| Order | The export is newest first. The analyzer sorts the entries by time. |
| Level | The export contains only what the Grafana query returned. Check the query: an all-ERROR export means INFO/WARN were filtered out. |
| Schema | Some records carry an extra `clientId` field. The analyzer ignores it. |
| Messages | Stack traces are in `message`, with real line breaks. Group by the first line. |

## 4. Analysis checklist

- [ ] **Scope**: the Grafana query, time range, environment(s), and company/cid filter used.
- [ ] **Volume**: total entries, distinct signatures, busiest hours (local time).
- [ ] **Top signatures**: the 5-10 most frequent messages, with first and last seen.
- [ ] **Exception types**: group by exception class (`InvalidOperationException`, `NotFoundException`...).
- [ ] **Who and where**: `company`, `username`, `logger`, `threadid`. Is one tenant or user dominant?
- [ ] **Bursts**: do peaks line up with deployments, batch jobs, or user activity?
- [ ] **Causality**: for each signature, is there a preceding `WARN`/`INFO` or a related `http_request`
      record in the same window? Check the `traceId` if present.
- [ ] **Noise vs. impact**: separate expected errors (e.g. "not found" on delete) from failures users see.

## 5. Report template

Copy this into a new note or ticket. Keep numbers and quotes exact.

```markdown
# Log analysis: <short title>

**Source:** Logs-YYYY-MM-DD HH_MM_SS.json (<n> entries; <environment>; <company/cid filter>)
**Grafana query / filter:** <query, levels, time range>
**Time span (local):** <first time> to <last time>
**Analyzed by / date:** <name>, <date>

## Summary
<2-3 sentences: what failed, for whom, how often, whether it is ongoing>

## Key numbers
| Metric | Value |
|--------|-------|
| Entries | |
| Distinct error signatures | |
| Busiest hour (local) | |
| Companies / users affected | |

## Findings (ranked by impact)
### 1. <signature, first line of message>
- **Count / first / last seen:** <n> / <time> / <time>
- **Affected:** company <x>, user <y>, logger <z>
- **Evidence:** `<time> <level> <message excerpt>`
- **Likely cause:** <statement>
- **Confidence:** high | medium | low, and why

## Open questions
- <what would confirm or rule out each cause>

## Next actions
- [ ] <owner> <action>
```

These are observations from the export only. Cause and impact still need confirmation from the source systems.
