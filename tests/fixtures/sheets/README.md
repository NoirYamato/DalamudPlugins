# Sheet fixtures

Snapshots of the two example TOP plans, captured 2026-11-11. Format, capture method and
verification: `docs/research/sheets-import.md` (section 2).

| key | spreadsheet | id |
|-----|-------------|----|
| `lpdu` | LPDU TOP Mitigation Sheet (13 tabs) | `1Dlnye-i7SZ0ZZ7TY7r8SKMu3nMj_PZI45zy2QqDzuCU` |
| `carry-the-clear` | Carry the Clear: TOP Healing Plan (3 tabs) | `1chKttd7POMf-aRLMAzQjNHDIgVzOKmLk9igSt-o2X6s` |

- `<key>/<tab>.json`: one tab from the Google Sheets API (`spreadsheets.get`, grid data with
  `effectiveFormat`), normalised by `normalize_sheets.py`. These are the reference fixtures.
- `<key>/export/<key>.xlsx`: `…/export?format=xlsx` of the whole spreadsheet (same moment).
- `<key>/export/csv/<tab>.csv`: `…/export?format=csv&gid=<gid>` per tab, byte-exact (CRLF, no final newline).

Regenerate the JSON: run the `get_spreadsheet` call given in the report, save the response, then
`python3 -I normalize_sheets.py <response.json> <key>/`.
