# Word collection

The web app, native MAUI app and CLI use the same authenticated HTTP API and PostgreSQL collection. No SDK projects or embedded web views have been added. On Windows, MAUI renders native WinUI controls.

## Run locally

```sh
python3 scripts/dev.py prepare --target core
python3 scripts/dev.py run --target service --no-build
# In a second terminal:
python3 scripts/dev.py run --target web --no-build
# Copy this short-lived development token into Service connection in the UI:
python3 scripts/dev.py token
```

Open `http://localhost:5182`. The connection form loads the saved collection using REST GET calls. Add a word and its type, search by the beginning of a word, and select multiple type filters. Use **Edit** or **Delete** beside a word. Deletion asks which word you intend to remove.

After saving a word, the collection returns to its unfiltered first page. Use **Next** for further pages or search for a particular word. The web **Word types** dropdown closes when you click outside, press Escape, choose Close, or apply filters. Closing it keeps your selected checkboxes; **Clear filters** removes the search and type filters.

Choose **+ Add to sentence** to add a word to a sentence. The preview displays your selections; it is not a text input. **Save sentence** becomes available when connected and at least one word is selected, and is temporarily disabled during requests. Repeat a word as often as needed, reorder selections with the arrows, or remove a selection. **Save sentence** makes a REST POST and adds the result to the saved history. History shows newest entries first. A saved sentence contains a snapshot of its words; later edits and deletions leave that snapshot unchanged. Sentence text uses the database's current spelling when saved, with spaces between words; it does not automatically correct grammar or add punctuation.

There is one shared collection per deployment. Access is controlled with scopes, not per-user ownership. Production authentication uses the already-configured identity provider. The token input is a development/integration entry point, not a username/password login flow. Tokens remain in memory; reload or disconnect clears them.

The existing [build/debug guide](build-and-debug.md) covers individual projects, the combined debugger and native devices. Debug Android emulators reach the API through `http://10.0.2.2:5180/`. Release native clients require an HTTPS API URL.

## Upgrade an existing Docker database

New, empty databases run both SQL files automatically. Existing volumes need the additive migration **before starting the updated service**:

```sh
python3 scripts/migrate.py
# Only when using the Linux host-network fallback:
python3 scripts/migrate.py --host-network
```

The script targets Docker context `default`; use `--docker-context NAME` to select another. Rebuild and start the three-container stack with your existing Compose host override. Do not delete the database volume. `scripts/dev.py prepare` automatically applies the same migration to its separate debug database. Schema version 2 is required for readiness. Migrations run transactionally, serialize with an advisory lock, and may be repeated safely.

## HTTP contract

All routes require a bearer token. Development token scripts now include all five scopes: `status.read`, `words.read`, `words.write`, `sentences.read`, and `sentences.write`. Existing development tokens need to be regenerated. Production issuers must grant the appropriate scopes explicitly.

| Method and route | Scope | Result |
| --- | --- | --- |
| `GET /api/words?after=0&search=ev&types=Noun,Verb` | `words.read` | Up to 50 words, ordered by increasing ID |
| `GET /api/words/{id}` | `words.read` | One word |
| `POST /api/words` | `words.write` | Create a word; 201 and a Location header |
| `PUT /api/words/{id}` | `words.write` | Replace its spelling and type |
| `DELETE /api/words/{id}` | `words.write` | Delete a word; 204 |
| `GET /api/sentences?after=0` | `sentences.read` | Up to 10 sentences, newest first |
| `POST /api/sentences` | `sentences.write` | Save an ordered selection of word IDs |

Word creation/update body:

```json
{ "word": "evaluates", "type": "Verb" }
```

Word response (there are no database-only fields):

```json
{ "id": 1, "word": "evaluates", "type": "Verb" }
```

The nine types are `Noun`, `Verb`, `Adjective`, `Adverb`, `Pronoun`, `Preposition`, `Interjection`, `Conjunction`, and `Determiner`. Types use these exact names. A word contains letters, combining accents, apostrophes or hyphens, has at least one letter, and is at most 80 UTF-16 code units. Outer whitespace is removed and spelling is normalized to Unicode NFC. A case-insensitive duplicate with the same type returns 409; the same spelling with a different type is allowed. Updates currently use last-write-wins semantics.

List responses have this shape:

```json
{ "items": [{ "id": 1, "word": "evaluates", "type": "Verb" }], "nextAfter": null }
```

If `nextAfter` is not null, pass its value as `after` for the next page, retaining the same filters. Word cursors move forward in ID order; sentence cursors move backward to older history. Search matches a case-insensitive **prefix**, with literal wildcard characters. Empty type selection means all types. Paging avoids downloading the entire collection or running a full COUNT query on every request.

Sentence submission:

```json
{ "wordIds": [3, 1, 4, 3], "requestId": "9dc67e4c-2e91-475e-b9b5-e4f81785d28a" }
```

Choose 1–50 positive IDs. The request ID is a new UUID for each intended sentence; reuse it with exactly the same IDs when retrying an uncertain request. Concurrent retries return the same sentence ID. Reusing a request ID with different IDs returns 409. If a word is missing, no partial sentence is saved. A retry of an already-saved sentence succeeds even after its source words were deleted. The response contains `id`, `text`, and `createdAt` (UTC).

Malformed JSON returns 400 and oversized requests return 413. Validation returns friendly Problem Details with a support `requestId` matching `X-Request-ID`. Missing words return 404, duplicates/stale selections 409, unavailable storage 503, and unexpected failures a safe 500. Raw SQL, tokens and stack traces are never returned. The existing gRPC status endpoint remains available; these new collection operations are REST endpoints.

## CLI

```sh
Runninghill.Cli words add evaluates Verb
Runninghill.Cli words list --types Noun,Verb --search ev
Runninghill.Cli words get 1
Runninghill.Cli words update 1 evaluates Verb
Runninghill.Cli sentences add 3 1 4 3
Runninghill.Cli sentences list
Runninghill.Cli words delete 1 --yes
Runninghill.Cli --help
```

Set `RUNNINGHILL_SERVICE_URL` and `RUNNINGHILL_ACCESS_TOKEN` first, or use the existing Debug launch configuration. IDs above are examples; use IDs returned by your own database. Terminal output uses simple wrapped text rather than a wide table, strips control characters, and supports redirected output. Deletion requires the explicit `--yes` flag. The sentence command prints its request ID so a failed request can be retried using `--request-id UUID`.

## Design and accessibility

The supplied `AllElements.html` and `Treeview&MultiselectorDropdown.html` inform the pale gray canvas, white rounded panels, Segoe UI typography, blue actions, type tags, and expandable checkbox selection. Their examples were treated as design references. The supplied design checklist informed visible field labels, keyboard focus, 44-pixel touch targets, descriptive actions, deletion cancellation, empty/loading/error states, preserved drafts on failure, reduced-motion support, and layouts that stack on narrow screens. No external fonts, analytics, or prototype scripts are loaded.

The web design tokens live in `wwwroot/app.css`. Matching native styles live in `MainPage.xaml`; layout changes use available width, not device names. Windows uses WinUI through MAUI. Native controls retain platform-specific text rendering and focus behavior; exact pixel equality must be checked on Windows and is not guaranteed by a Linux build. Browser layouts have automated checks at 1440, 820, 390, and 320 pixels. Human usability research and screen-reader certification have not been performed.

## Verification

```sh
python3 scripts/build.py test -c Debug
python3 -m unittest discover -s tests/tooling -v
python3 tests/integration/collection.py --debug
python3 tests/cli/collection.py --debug --cli-directory artifacts/Release/cli/linux-x64
# For the debug browser UI (requires Playwright's Chromium, or RUNNINGHILL_CHROME_BINARY):
RUNNINGHILL_DEBUG=1 node tests/browser/collection.cjs
# UI regression checks use a fake API and never change the database (web server required):
RUNNINGHILL_DEBUG=1 node tests/browser/collection-regressions.cjs
# For the Release Docker stack:
python3 tests/integration/collection.py
node tests/browser/smoke.cjs
node tests/browser/collection.cjs
```

Integration checks use a unique word prefix and clean up only their own words. They leave saved test sentences in history to exercise immutable snapshots, so use an isolated test/development database. Browser screenshots are written under ignored `artifacts/ui/`. Release checks should also publish service/CLI with NativeAOT, web with WebAssembly AOT, and MAUI for each supported host/device.

The service uses normal globalization because invariant mode skips Unicode normalization. The Linux runtime image includes ICU; custom minimal images must supply it.

Npgsql array and TLS mappings are explicitly enabled on its slim builder, keeping the required features compatible with native compilation; see the [official builder documentation](https://www.npgsql.org/doc/api/Npgsql.NpgsqlSlimDataSourceBuilder.html). Queries are parameterized, pooled, cancellable and time-bounded. Unique/type/prefix indexes serve common collection operations. These safeguards are not a throughput benchmark: production capacity still depends on deployment resources and workload.
