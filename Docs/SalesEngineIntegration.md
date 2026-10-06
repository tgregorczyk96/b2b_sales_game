# Sales Engine ↔ Unity – Integration

Stand: 2026-10-06 · Unity 6000.5.2f1 · Engine `tgregorczyk96/sales_engine`, Branch `feature/unity-netstandard-targets` (lokal, nicht committet, auf Basis `main@6bffd33`)

## Kurzfassung

| Was | Status |
|-----|--------|
| Engine-Domain (`SalesEngine`) in Unity | ✅ läuft (Play Mode, echte Engine-State-Werte) |
| Origin Scenario aus Seed (`SalesEngine.Content`) | ✅ läuft (`hair-salon:1406361028`, Easy) |
| Echter LLM-Kunde (`SalesEngine.AI`) in Unity | ❌ **offen** – Anthropic-SDK / System.Text.Json inkompatibel mit Unity (s. unten) |
| Aktueller Kunde in Unity | Placeholder-Kunde **der Engine** (gescriptete Antworten, kein Intent, keine Stage-Wechsel) |
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
- csproj: `TargetFrameworks net10.0;netstandard2.1`; ns2.1 mit `LangVersion 14`, **System.Text.Json 8.0.5** (= Unitys eingebaute Version); **Anthropic-SDK nur für net10.0**, `Anthropic/**` im ns2.1-Build ausgeschlossen.
- Engine-Tests: vor und nach den Änderungen 1.510 bestanden, 26 übersprungen, 0 Fehler.

## Offener Punkt: echte LLM-Integration in Unity

**Problem.** Aktiver Provider ist Anthropic. `SalesEngine.AI` nutzt dafür das offizielle Anthropic-SDK 12.53.0. Dessen `netstandard2.0`-Build verlangt **System.Text.Json ≥ 10.0.6** (plus `Microsoft.Extensions.AI.Abstractions 10.5`, `System.Net.ServerSentEvents 10`). Unity 6000.5 referenziert automatisch seine eigene **System.Text.Json 8.0** (`Editor/Data/BCLExtensions`). Eine zweite, gleichnamige STJ 10 in `Assets/Plugins` kollidiert damit; mit STJ 8 ist das SDK nicht lauffähig. Deshalb ist der Anthropic-Client im Unity-Build ausgeschlossen. Der vorhandene OpenAI-Client wäre technisch lauffähig, OpenAI wird aber bewusst nicht verwendet.

Verifiziert: Mit LLM-Verdrahtung lief die Engine in Unity bis zum HTTP-Aufruf (Session, Origin, State OK). Der LLM-Aufruf selbst wurde nicht erfolgreich durchgeführt.

**Kleinste vorgeschlagene Engine-Änderung (noch nicht umgesetzt):** ein SDK-freier `AnthropicMessagesClient : IStructuredLlmClient` in `SalesEngine.AI` (`HttpClient` + System.Text.Json, `POST /v1/messages` mit `output_config.format = json_schema`, gleiche Fehlerabbildung wie `AnthropicStructuredLlmClient`), nur im `netstandard2.1`-Build oder für beide Targets. Geschätzt ~150 Zeilen + Tests analog `AnthropicStructuredLlmClientTests`. Prompts, Schemas und Simulator bleiben unverändert in der Engine. Danach im Unity-Adapter eine Factory `CreateWithAnthropic(contentDirectory, getSetting)` und Key-Bereitstellung (Env/.env, git-ignoriert).

Alternativen (schlechter): Anthropic-SDK-Version mit STJ-8-Kompatibilität suchen (an SDK-Releases gebunden); Engine out-of-process als lokaler Dienst (neue Architektur).

## Weitere offene Punkte

- `Engagement` vs. `Interest` (s. Mapping).
- `IsConversationOver`/`FinalTurnPending`: Unity zeigt die angekündigte letzte Runde noch nicht an.
- Opening-Evaluator ist nicht verdrahtet (beeinflusst keine der angezeigten Werte).
- Player-Builds: Content liegt in `StreamingAssets` (Desktop OK; Android/WebGL bräuchten anderes Laden).
- DLLs/Content sollten committed werden (Unity-Projekt baut dann ohne Engine-Checkout); Engine-Branch vorher reviewen/committen und neu syncen, damit `ENGINE_VERSION.txt` einen sauberen Commit nennt.

## Smoke-Test

`Assets/Game/Tests/PlayMode/SalesEngineSmokeTest.cs` – lädt `SalesTestScene`, sendet einen Satz über die UI, prüft Antwort und State. Lauf (Batchmode, PlayMode) am 2026-10-06: **Passed**.

```
[Smoke] Session started. Customer: "Guten Tag?" | Trust: 40 | Openness: 50 | Engagement: 25 | Patience: 70 | Stage: Opening | Intent: -
[Smoke] Player: "Guten Tag, mein Name ist Max Berger von Webwerk. ..."
[Smoke] Customer: "Ja, worum geht es denn?"
[Smoke] Trust: 43 | Openness: 50 | Engagement: 29 | Patience: 66 | Stage: Opening | Intent: -
```

Kommandozeile (Editor muss geschlossen sein):
`Unity.exe -batchmode -nographics -projectPath "<projekt>" -runTests -testPlatform PlayMode -testResults results.xml -logFile log.txt`

> **Achtung:** Ein Batchmode-Testlauf startet in einer leeren, unbenannten Szene und speichert diese beim Beenden als
> zuletzt geöffnete Szene (`Library/LastSceneManagerSetup.txt` → `sceneSetups: []` bzw. `path:` leer). Der Editor
> öffnet danach „Untitled“, und Play zeigt **nichts**. Nach einem Batchmode-Lauf im Editor `Assets/Game/Scenes/SalesTestScene.unity`
> öffnen (oder die Datei wieder auf diese Szene setzen). `SalesTestScene` ist Index 0 der Build Settings, ein gebautes
> Spiel startet daher immer mit ihr.
