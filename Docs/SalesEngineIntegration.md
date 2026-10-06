# Sales Engine ↔ Unity – Integration

Stand: 2026-10-06 · Unity 6000.5.2f1 · Engine `tgregorczyk96/sales_engine`, Branch `feature/unity-netstandard-targets` (lokal, nicht committet, auf Basis `main@6bffd33`)

## Kurzfassung

| Was | Status |
|-----|--------|
| Engine-Domain (`SalesEngine`) in Unity | ✅ läuft (Play Mode, echte Engine-State-Werte) |
| Origin Scenario aus Seed (`SalesEngine.Content`) | ✅ läuft (`hair-salon:1406361028`, Easy) |
| Echter LLM-Kunde (`SalesEngine.AI`) in Unity | ✅ läuft über den SDK-freien `AnthropicMessagesClient`, sobald `SALESSIM_ANTHROPIC_API_KEY` gesetzt ist |
| Kunde ohne Key | Placeholder-Kunde **der Engine** (gescriptete Antworten, kein Intent); Grund steht im Log (`[SalesClient] Fallback reason`) |
| Voice, STT, TTS, Hume, Animation, Savegames, Szenario-Auswahl | bewusst nicht integriert |

## Verwendeter Integrationsweg

**Vorgebaute `netstandard2.1`-DLLs aus dem separaten Engine-Repo + dünner Adapter hinter `ISalesGameSession`.**

```
sales_engine (eigenes Repo, kein UnityEngine)            B2B Sales Simlator (Unity)
───────────────────────────────────────────             ─────────────────────────────────────────────
src/SalesEngine          (netstandard2.1)  ─┐
src/SalesEngine.Content  (net10.0;ns2.1)   ─┼─ Tools/Sync-SalesEngine.ps1 ─►  Assets/Plugins/SalesEngine/*.dll
src/SalesEngine.AI       (net10.0;ns2.1)   ─┘                                Assets/Plugins/SalesEngine/ENGINE_VERSION.txt
content/**/*.json                          ──────────────────────────────►  Assets/StreamingAssets/SalesEngine/content/

Presentation ──► ISalesGameSession (Application, noEngineReferences)
                        ▲
Infrastructure (Composition Root) ──► SalesSim.EngineAdapter.SalesEngineGameSession (noEngineReferences)
                                              └──► SalesEngine.dll, SalesEngine.Content.dll (explicitly referenced)
```

- Die DLLs sind im PluginImporter als *explicitly referenced* markiert: nur `SalesSim.EngineAdapter` sieht Engine-Typen. Presentation und Application kennen die Engine nicht.
- `SalesSim.EngineAdapter` hat `noEngineReferences: true` → der Adapter kann technisch kein `UnityEngine` verwenden.
- Unity spricht weiterhin nur `StartSessionAsync`, `SendPlayerTurnAsync`, `CurrentState`.
- Szenario-Id ist für Unity opak; der Adapter interpretiert sie als `<generation pool>:<seed>` und regeneriert den Origin genau wie die Console (`ContentRepository.LoadGenerator(pool).Generate(seed)`).
- `ENGINE_VERSION.txt` hält Repo, Branch und Commit fest, aus dem die DLLs gebaut wurden.

**Engine aktualisieren:** im Unity-Projekt `pwsh Tools/Sync-SalesEngine.ps1 [-EnginePath <pfad>]`. Das Skript baut `-f netstandard2.1`, kopiert DLLs/PDBs und Content und **bricht ab**, wenn eine Engine-Assembly etwas referenziert, das Unity nicht selbst mitbringt.

### Mapping Engine → Unity

| Unity (`SalesSessionState`) | Engine |
|---|---|
| `CustomerMessage` | Start: letzte Zeile von `ConversationSession.Turns` (Begrüßung) · Zug: `TurnResult.CustomerReply` |
| `Indicators.Trust / Openness / Patience` | `CustomerState.Trust / Openness / Patience` (0–100) |
| `Indicators.Engagement` | **`CustomerState.Interest`** – die Engine hat kein numerisches Engagement (nur die Kategorie `CustomerReaction.Engagement`). Offener Punkt: Unity-Bezeichnung angleichen. |
| `Stage` | `ConversationStage.ToString()` |
| `Intent` | `TurnResult.CustomerReaction?.Intent.ToString()` – leer ohne LLM-Kunde |
| `IsConversationOver` | `TurnResult.End != null` bzw. `session.IsEnded` |

## Analyse der Engine (main@6bffd33)

| Projekt | Target | Packages | Hängt ab von | Für Text-Run nötig | Unity-tauglich (vorher) |
|---|---|---|---|---|---|
| SalesEngine | netstandard2.1, C# 9 | – | – | ja (Domain, `ISalesEngine`) | ✅ |
| SalesEngine.Content | net10.0, C# 14 | (System.Text.Json aus BCL) | SalesEngine | ja (Origin aus Seed, Content-JSON) | ❌ |
| SalesEngine.AI | net10.0, C# 14 | Anthropic 12.53.0 (→ STJ 10, M.E.AI.Abstractions, …) | SalesEngine | ja (LLM-Kunde, Interpreter) | ❌ |
| SalesEngine.Voice | netstandard2.1 | – | – | nein | ✅ |
| SalesEngine.Voice.ElevenLabs | net10.0 | – | Voice | nein | – |
| SalesConsole | net10.0 Exe | – | alle | nein (enthält OriginStore/History/.env – CLI-intern) | – |

- Öffentliche API vorhanden: `ISalesEngine` (`StartScenario`, `ProcessTurnAsync`, `EndScenario`), Kunde/Interpreter als Ports (`ICustomerSimulator`, `IConversationInterpreter`).
- async/await: Engine nutzt `Task` und `CancellationToken`, kein Threading-Zwang; Unity-Controller awaited auf dem Main Thread (UnitySynchronizationContext). Keine Unity-API im Adapter → keine Thread-Probleme.
- JSON: System.Text.Json. Unity 6000.5 bringt über `BCLExtensions` **STJ 8.0** mit und referenziert es automatisch.
- HTTP: `HttpClient` (Mono) – funktioniert in Editor/Standalone; WebGL wäre nicht unterstützt.
- Unity-Runtime: Mono, API-Level .NET Standard 2.1, C# 9 für Unity-eigene Skripte (vorgebaute DLLs dürfen intern neuere C#-Features nutzen).

## Vergleich der Integrationsmöglichkeiten

| Option | Trennung | Updatebarkeit | Testbarkeit | Build-Stabilität | Duplizierung | Bewertung |
|---|---|---|---|---|---|---|
| Direkte Source-Integration (Code nach `Assets`) | schlecht (Engine-Code im Unity-Repo) | schlecht (manuelles Kopieren) | Engine-Tests laufen nicht in Unity | Content/AI kompilieren nicht (C# 14, net10) | hoch | ❌ |
| **Lokale DLL-Referenz (gewählt)** | gut (separates Repo, explicit references) | gut (ein Skript, Versionsdatei) | Engine-Tests bleiben im Engine-Repo; Unity Smoke-Test | gut, solange keine fremden Abhängigkeiten (Skript prüft) | keine | ✅ |
| Unity Package (UPM, z. B. Git-URL mit vorgebauten DLLs) | sehr gut | sehr gut (Versionen/Tags) | wie DLL | gut | keine | 👍 späterer Ausbau der DLL-Variante, wenn Engine Releases taggt |
| Shared-Projekt / Git-Submodule mit Sources | mittel | mittel (Submodule-Pflege) | mittel | schlecht (gleiches C#-/TFM-Problem wie Source) | gering | ❌ |
| Dünner Adapter um vorhandene Engine-Assemblies | – | – | – | – | – | ist **Teil** der gewählten Lösung (`SalesEngineGameSession`) |

## Änderungen am Engine-Repo (Branch `feature/unity-netstandard-targets`, uncommitted)

Kleinstmögliche Änderung: `SalesEngine.Content` und `SalesEngine.AI` bauen **zusätzlich** für `netstandard2.1`; `net10.0` (Console, Tests) bleibt funktional unverändert.

- `src/Shared/NetStandardPolyfills.cs` (neu, nur im ns2.1-Build): `ArgumentNullException.ThrowIfNull`, `ArgumentOutOfRangeException.ThrowIfNegative(OrZero)`, `Enum.GetNames<T>/IsDefined<T>`, `HttpContent.ReadAsStringAsync(CancellationToken)`, `CallerArgumentExpression`, `IsExternalInit`.
- `ContentRepository`, `ProviderErrorText`: `[GeneratedRegex]` unter `#if NET`, sonst statischer `Regex`.
- `OpenAiResponsesClient`: `HttpRequestError` unter `#if NET`.
- `ScenarioGenerator.FindOrigins`: Parameter `IReadOnlySet<int>?` → `IEnumerable<int>?` (quellkompatibel).
- csproj: `TargetFrameworks net10.0;netstandard2.1`; ns2.1 mit `LangVersion 14`, **System.Text.Json 8.0.5** (= Unitys eingebaute Version); **Anthropic-SDK nur für net10.0**; im ns2.1-Build ist nur `Anthropic/AnthropicStructuredLlmClient.cs` (SDK) ausgeschlossen.
- `Anthropic/AnthropicMessagesClient.cs` (neu): SDK-freier `IStructuredLlmClient` für beide Targets, gleiche Anfrage und Fehlerabbildung wie der SDK-Client (per Test verglichen), optionaler Trace-Callback ohne Key/Inhalte.
- Engine-Tests: vorher 1.510 bestanden; jetzt 1.539 bestanden (+29 `AnthropicMessagesClientTests`), 26 übersprungen, 0 Fehler.

## LLM-Kunde in Unity (Anthropic)

**Warum ein eigener Client:** Das offizielle Anthropic-SDK verlangt System.Text.Json ≥ 10; Unity 6000.5 referenziert automatisch seine eigene STJ 8.0 (`Editor/Data/BCLExtensions`), eine zweite STJ in `Assets/Plugins` würde kollidieren. Deshalb gibt es in `SalesEngine.AI` den SDK-freien `AnthropicMessagesClient` (`HttpClient` + STJ, `POST https://api.anthropic.com/v1/messages`, `output_config.format = json_schema`). Er implementiert dieselbe Schnittstelle `IStructuredLlmClient` wie der SDK-Client; Prompts, Schemas, Simulator und Interpreter sind unverändert. Keine Unity-Abhängigkeit.

**Auswahl (`SalesEngineGameSession.Create`):**
- `SALESSIM_ANTHROPIC_API_KEY` gesetzt (Umgebung zuerst, dann `.env` im Projekt-Root, git-ignoriert) → Engine mit `LlmCustomerSimulator` + `LlmConversationInterpreter` über `AnthropicMessagesClient`. `SALESSIM_ANTHROPIC_MODEL` / `SALESSIM_AI_TIMEOUT_SECONDS` optional (Engine-Defaults).
- kein Key → Placeholder-Kunde der Engine, Grund im Log.
- Setup-Fehler (z. B. ungültiger Timeout) → `NotConnectedSalesGameSession` + Error in der Console.

**Fehlerverhalten (Engine-Design):** Schlägt der LLM-Aufruf fehl (HTTP-Fehler, Timeout, unlesbare oder schemafremde Antwort), gibt es **keinen** Fallback-Text: Die Engine wirft `CustomerSimulationFailedException`, der Zug wird nicht gezählt, der Zustand bleibt unverändert, der Spieler kann ihn erneut senden. Ein Fehler des Interpreters bricht den Zug nicht ab (nur keine Stage-Auswertung).

**Diagnose-Logs** (Unity Console, ohne Key, Prompts oder Modellausgaben):
```
[SalesClient] Customer: Anthropic LLM (model claude-sonnet-5-5, timeout 30s)
[SalesClient] Request: POST https://api.anthropic.com/v1/messages (customer_simulation_response)
[SalesClient] Status: 200
[SalesClient] Response received: yes
[SalesClient] Deserialization successful: yes
[SalesClient] Request: POST https://api.anthropic.com/v1/messages (conversation_interpretation)
...
[SalesClient] Fallback used: no
[SalesClient] Fallback reason: -
```
Die Zeilen sind als temporäre Diagnose gedacht; sie hängen an einem optionalen Log-Callback und lassen sich im Bootstrap abschalten.

**Tests:**
- Engine: `tests/SalesEngine.AI.Tests/AnthropicMessagesClientTests.cs` (29 Fälle: Anfrageformat inkl. Vergleich mit SDK-Client, Key nur im Header, Erfolg, Refusal/max_tokens/kein Text/unlesbar, HTTP 401/403/402/404/429/5xx/529, Netzwerk, Abbruch/Timeout, Trace).
- Unity EditMode: `Assets/Game/Tests/EditMode/SalesEngineGameSessionTests.cs` (7 Fälle mit gescriptetem HTTP, keine Netzkosten): erfolgreiche Antwort → kein Fallback und Antwort im State; echter Anthropic-Client statt Placeholder; HTTP-Fehler / unlesbare Antwort / schemafremde Antwort → Zug schlägt fehl, State unverändert, kein Fallback-Text; ohne Key → Placeholder mit Grund; Logs ohne Key.
- Unity PlayMode (explizit, echte API): `SalesEngineSmokeTest` (zwei Turns, Verlauf, Thinking, Debug-State).

## Chat-Verlauf in SalesTestScene (reine Presentation)

- `ConversationHistoryView` (ScrollRect → Viewport mit RectMask2D → Content mit VerticalLayoutGroup + ContentSizeFitter), Nachrichten aus dem Prefab `Assets/Game/Prefabs/ConversationMessage.prefab` (`ConversationMessageView`), `ThinkingIndicatorView` als Kunden-Bubble mit `.` → `..` → `...`. Keine Engine-Abhängigkeit; der Verlauf wird nicht gespeichert.
- Ablauf im `SalesConversationController`: Spieler-Nachricht sofort anzeigen → Thinking an, Input gesperrt → `SendPlayerTurnAsync` → Kunden-Nachricht + Debug-Panel. Bei Fehler: Spieler-Nachricht bleibt („nicht zugestellt“), keine erfundene Kundenantwort, Text zurück ins Eingabefeld; Thinking aus und Input frei in `finally`.
- Kopierbarer Text: `SelectableMessageText` = read-only `TMP_InputField` ohne Eingabefeld-Optik (Markieren mit der Maus, `Ctrl+C` über TMPs eigenen Copy-Pfad; Tippen/Löschen/Einfügen blockiert `readOnly`; Rich Text aus). Einzige Ergänzung: Mausrad wird an den ScrollRect weitergereicht, weil ein mehrzeiliges TMP-Feld es sonst verschluckt.
- Auto-Scroll: folgt neuen Nachrichten nur, wenn man unten (≤ 60 px) ist; bleibt am Boden, bis die TMP-Höhe stabil ist; wer hochgescrollt hat, behält die Position.
- Aufbau reproduzierbar per Editor-Menü „Sales Sim/Build Conversation UI“ (`Assets/Game/Scripts/Editor/ConversationUiBuilder.cs`).
- Tests: `Assets/Game/Tests/PlayMode/ConversationHistoryTests.cs` (7 Fälle mit gescripteter Session, ohne API).

## Weitere offene Punkte

- `Engagement` vs. `Interest` (s. Mapping).
- `IsConversationOver`/`FinalTurnPending`: Unity zeigt die angekündigte letzte Runde noch nicht an.
- Opening-Evaluator ist nicht verdrahtet (beeinflusst keine der angezeigten Werte).
- Player-Builds: Content liegt in `StreamingAssets` (Desktop OK; Android/WebGL bräuchten anderes Laden).
- DLLs/Content sollten committed werden (Unity-Projekt baut dann ohne Engine-Checkout); Engine-Branch vorher reviewen/committen und neu syncen, damit `ENGINE_VERSION.txt` einen sauberen Commit nennt.

## Smoke-Test

`Assets/Game/Tests/PlayMode/SalesEngineSmokeTest.cs` – lädt `SalesTestScene`, sendet einen Satz über die UI, prüft Antwort und State. `[Explicit]`, weil er mit gesetztem Key die echte Anthropic-API aufruft. Lauf mit echter API (Batchmode, PlayMode) am 2026-10-06: **Passed** (7,5 s).

```
[Smoke] Session started. Customer: "Guten Tag?" | Trust: 40 | Openness: 50 | Engagement: 25 | Patience: 70 | Stage: Opening | Intent: -
[Smoke] Player: "Guten Tag, mein Name ist Max Berger von Webwerk. ..."
[Smoke] Customer: "Ach so, na ja, zwei Minuten habe ich gerade zwischen zwei Terminen. Online ist bei uns tatsächlich nicht so toll, worum geht's genau?"
[Smoke] Trust: 40 | Openness: 50 | Engagement: 32 | Patience: 70 | Stage: Opening | Intent: AskQuestion
```

Kommandozeile (Editor muss geschlossen sein):
`Unity.exe -batchmode -nographics -projectPath "<projekt>" -runTests -testPlatform PlayMode -testFilter SalesSim.Tests.PlayMode.SalesEngineSmokeTest -testResults results.xml -logFile log.txt`
(EditMode-Tests: `-testPlatform EditMode -testFilter SalesSim.Tests.EditMode`)

> **Achtung:** Ein Batchmode-Testlauf startet in einer leeren, unbenannten Szene und speichert diese beim Beenden als
> zuletzt geöffnete Szene (`Library/LastSceneManagerSetup.txt` → `sceneSetups: []` bzw. `path:` leer). Der Editor
> öffnet danach „Untitled“, und Play zeigt **nichts**. Nach einem Batchmode-Lauf im Editor `Assets/Game/Scenes/SalesTestScene.unity`
> öffnen (oder die Datei wieder auf diese Szene setzen). `SalesTestScene` ist Index 0 der Build Settings, ein gebautes
> Spiel startet daher immer mit ihr.
