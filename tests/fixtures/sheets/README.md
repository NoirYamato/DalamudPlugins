# Sheet fixtures

Snapshots of the two example TOP plans, captured 2026-11-11, and of the LPDU TEA plan, captured 2026-11-14 the
same way. Format, capture method and verification: `docs/research/sheets-import.md` (section 2).

| key | spreadsheet | id |
|-----|-------------|----|
| `lpdu` | LPDU TOP Mitigation Sheet (13 tabs) | `1Dlnye-i7SZ0ZZ7TY7r8SKMu3nMj_PZI45zy2QqDzuCU` |
| `carry-the-clear` | Carry the Clear: TOP Healing Plan (3 tabs) | `1chKttd7POMf-aRLMAzQjNHDIgVzOKmLk9igSt-o2X6s` |
| `lpdu-tea` | [LPDU] TEA Heal/Mit (2 tabs) | `1D2AriiqGwEMWV-u_wGPRdeDCOKVO2xYQ_ms70RrJlwM` |

- `<key>/<tab>.json`: one tab from the Google Sheets API (`spreadsheets.get`, grid data with
  `effectiveFormat`), normalised by `normalize_sheets.py`. These are the reference fixtures.
- `<key>/export/<key>.xlsx`: `…/export?format=xlsx` of the whole spreadsheet (same moment).
- `<key>/export/csv/<tab>.csv`: `…/export?format=csv&gid=<gid>` per tab, byte-exact (CRLF, no final newline).

Regenerate the JSON: run the `get_spreadsheet` call given in the report, save the response (either the raw
`spreadsheets.get` JSON or the MCP tool-result file that wraps it as `{"content": …}`), then
`python3 -I normalize_sheets.py <response.json> <key>/`. Re-running this on 2026-11-11 reproduced all 16 TOP files
byte for byte, and on 2026-11-14 both TEA files (the response holds the whole grid of both tabs).

The xlsx files are not byte-stable: every download reorders shared strings and style records (same content),
so compare parsed cells, never file hashes. The CSV exports were byte-identical across downloads.
