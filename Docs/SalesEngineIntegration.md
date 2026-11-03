# Sales Engine ↔ Unity – Integration

Stand: 2026-11-03 · Unity 6000.5.2f1 · Engine `tgregorczyk96/sales_engine`, `feature/review-stand-direct-sale@ba32a13` (Bewertungsaufsteller; davor `main@029e3e7`)

## Kurzfassung

| Was | Status |
|-----|--------|
| Engine-Domain (`SalesEngine`) in Unity | ✅ läuft (Play Mode, echte Engine-State-Werte) |
| Origin Scenario aus Seed (`SalesEngine.Content`) | ✅ läuft (`hair-salon:1406361028`, Easy) |
| Echter LLM-Kunde (`SalesEngine.AI`) in Unity | ✅ läuft über den SDK-freien `AnthropicMessagesClient`, sobald `SALESSIM_ANTHROPIC_API_KEY` gesetzt ist |
| Kunde ohne Key | Placeholder-Kunde **der Engine** (gescriptete Antworten, kein Intent); Grund steht im Log (`[SalesClient] Fallback reason`) |
| Spracheingabe (Push-to-Talk, ElevenLabs `scribe_v2_realtime`) | ✅ läuft über `SalesEngine.Voice(.ElevenLabs)`, sobald `SALESSIM_ELEVENLABS_API_KEY` gesetzt ist (siehe „Push-to-Talk“) |
| Sprachausgabe des Kunden (TTS), Hume, Animation | nicht integriert (TTS existiert in der Engine, ist in Unity aber noch nicht angebunden) |

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

## Änderungen am Engine-Repo (Branch `feature/unity-netstandard-targets`, Commits `c483f03`, `b29c8fa`, `698bdd4`, `029e3e7`)

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

## Character-Art (FLUX, Art-Pass 2026-10-06)

Die Figur auf dem Bildschirm ist der **Kunde** (Salon-Inhaberin aus dem Szenario `hair-salon`), nicht der Spieler: `CharacterView` zeigt die Reaktion des Kunden (`CustomerMood` → `CharacterVisualState`).

- **Assets:** `Assets/Game/Art/Generated/Characters/customer-salon-owner_<state>_v01.png` für alle sieben Zustände (`idle`, `speaking`, `thinking`, `positive`, `verypositive`, `negative`, `verynegative`). Der Zustandsteil ist `CharacterVisualState.ToString().ToLowerInvariant()`, so sucht ihn `GameLoopUiBuilder`. Zu jedem Bild gibt es eine JSON-Datei nach `Art/Generated/README.md` mit Prompt, Modell, Seed, Datum, Request-ID, Referenzbild und Nachbearbeitung.
- **Stil:** moderne, flache 2D-Vektor-Illustration mit weichem Cel-Shading, gleichmäßigen dunkelbraunen Outlines und natürlichen Proportionen. Halbrealistisch, nicht fotorealistisch und nicht comichaft.
- **Konsistenz:** Ein Grundbild (`idle`, FLUX.2 [max], T2I, Seed 72931, 1024×1344), aus zwei Kandidaten ausgewählt. Die sechs anderen Zustände sind **Bildbearbeitungen dieses Grundbilds**: dasselbe Modell, derselbe Seed, Referenz = Idle-Request. Jeder Prompt ändert nur Mimik und Gestik und hält Person, Haare, Ohrringe, Bluse, Schürze, Ausschnitt, Stil und Hintergrund fest. Speaking wurde einmal neu erzeugt, weil der erste Versuch überrascht statt sprechend wirkte.
- **Freistellung:** FLUX liefert einen flachen grauen Hintergrund (JPEG). Er wird automatisch entfernt: Flood-Fill vom Rand plus große eingeschlossene Lücken (z. B. zwischen Arm und Körper), 1 px weiche Kante und Entfernung des grauen Saums. Die Canvas-Größe bleibt gleich, deshalb liegen alle Zustände pixelgenau übereinander. Von Hand ist nichts übermalt.
- **Import:** Sprite (2D/UI), Sprite Mode **Single**, keine Mipmaps, Alpha Is Transparency, Bilinear, Standard-Kompression, Max Size 2048. Single ist nötig: Der 2D-Projektstandard „Multiple“ schneidet automatisch auf die Figur zu, sodass jeder Zustand einen anderen Ausschnitt hätte. `GameLoopUiBuilder.LatestSprite` erzwingt das jetzt.
- **Verdrahtung:** „Sales Sim/Build Game Loop UI“ (oder `-executeMethod SalesSim.Editor.GameLoopUiBuilder.Build`) trägt die jeweils höchste `_vNN`-Version pro Zustand in `CharacterView.sprites` ein. Code der Zustandslogik wurde nicht geändert.
- **Test:** `GameLoopTests.EveryCharacterState_ShowsItsOwnFullCanvasSprite` prüft, dass jeder Zustand sein eigenes, nicht beschnittenes Sprite zeigt.
- **Visuelle Prüfung:** Ein temporärer PlayMode-Lauf hat den Loop mit gescripteter Session in PNGs gerendert und wurde danach wieder gelöscht. Geprüft: Idle → Speaking → Thinking → Positive → VeryPositive → Negative → VeryNegative → Result Screen → Verkauf (Guthaben 0 → 700 €) → Nächster Run (Idle, Guthaben bleibt). Alle Zustände wurden korrekt angezeigt.
- **Bekannte Schwächen:** Bei Positive ist der Mund leicht offen, gewünscht war ein geschlossenes Lächeln. Der Hintergrundton schwankte zwischen den FLUX-Bildern minimal, durch die Freistellung ist das nicht sichtbar. Thinking/VeryPositive/VeryNegative ändern die Armhaltung, die Silhouette springt dort also bewusst. Der Szenen-Hintergrund kam im Art-Pass vom 2026-10-07 dazu (siehe unten).

## Szenen-Hintergrund (FLUX, Art-Pass 2026-10-07)

- **Asset:** `Assets/Game/Art/Generated/Backgrounds/hair-salon_modern_v01.png` (1920×1080) mit Sidecar-JSON. Leerer, moderner Friseursalon ohne Personen und ohne lesbare Texte/Logos. Links stehen zwei Stationen mit Spiegeln, Stühlen und Produkten hinter der Figur, rechts eine ruhige, helle Wand mit Fenster hinter Chat, Guidance und Debug.
- **Erzeugung:** FLUX.2 [max], T2I, Seed 83514, 1920×1088, Request `791f5cc7-49c2-476f-9ec3-f7bb3c61cc92`. Stil-Beschreibung wie bei der Figur. Ausgewählt aus 2 Kandidaten mit demselben Prompt; Seed 51207 wurde verworfen, weil rechts ein unruhiges Fenstergitter und ein volles Regal genau hinter der UI lagen. Nachbearbeitung: 4 px oben/unten gecroppt auf exakt 16:9, keine Skalierung, kein Übermalen.
- **Import/Verdrahtung:** `GameLoopUiBuilder` (`BackgroundSubject = "hair-salon"`) nimmt die höchste `_vNN`-Version. Sprite Single, keine Mipmaps, **High-Quality-Kompression** (die Standardkompression erzeugt Banding auf den weichen Wandverläufen). Das `Background`-Image wird zur Laufzeit abgedunkelt (Tint 0.55/0.55/0.6), die Datei selbst bleibt unverändert.
- **Lesbarkeit:** Nur wenn ein Hintergrund verdrahtet ist, setzt der Builder: DebugPanel-Deckkraft 0.3 → 0.6, dezente dunkle Flächen hinter `RunLabel`, `BalanceLabel` und `StateLabel` (Namen `…Backing`, nicht klickbar). Result- und Setup-Panel sind jetzt voll deckend: Das Projekt blendet im Linear-Farbraum, deshalb schien bei 0.98 der Chat sichtbar durch.
- **Figur:** Der Body reicht jetzt bis zur Oberkante der Eingabeleiste (Anker `0, -0.147, 1, 1` in der CharacterArea). Die Kopfhöhe bleibt gleich, die Hüft-Schnittkante liegt hinter der Eingabe statt frei im Raum. Die Figur ist dadurch etwa 29 % größer.
- **Visuelle Prüfung:** Wie beim Character-Pass hat ein temporärer PlayMode-Lauf mit gescripteter Session PNGs gerendert und wurde danach wieder gelöscht. Geprüft wurden Setup, Idle, Thinking, VeryPositive, VeryNegative, Guidance mit Hinweis + Mehr Hinweis + Beispiel und der Result Screen.
- **Bekannte Schwächen:** Die Outlines im Hintergrund sind etwas feiner als bei der Figur. Im Setup ist die `RunLabel`-Fläche leer sichtbar (gedimmt, weil noch kein Run läuft). Die Flaschen im Regal schimmern leicht durch das Debug-Panel. Bei anderen Seitenverhältnissen als 16:9 kann `preserveAspect` die Figur so verkleinern, dass zwischen Hüfte und Eingabe ein Spalt entsteht.

## Live-Guidance (Silent-Training)

- **Engine-API:** `SalesEngine.Training.TrainingCoach` (eine Instanz pro Session, wie in der Console). `Check(type)` + `RequestAsync(type)`; der Coach liest nur einen `GuidanceContext`-Snapshot und ändert die Session nie.
- **Boundary:** `ISalesGameSession.RequestGuidanceAsync(GuidanceLevel)` → `GuidanceResult(Level, Text, Focus)`. Fehler/Nichtverfügbarkeit als `GuidanceUnavailableException` mit `Reason` (`RequiresAi`, `GenerationFailed`, `ConversationOver`, `NotAllowedByDifficulty`, `Busy`, `NotConnected`).
- **Mapping:** `Hint` → `GuidanceType.Orientation` (Console `/hint`, `GuidanceRules`, ohne AI) · `HintMore` → `Specific` (`/hint more`, ohne AI) · `Example` → `FullExampleSentence` (`/example`, `LlmExampleSentenceGenerator` über denselben `AnthropicMessagesClient`, Schema `example_sentence`). Ohne API-Key gibt es keinen Generator → `RequiresAi`; Hinweis/Mehr Hinweis funktionieren weiter.
- **Spieler-Kontext für Beispiele (Engine-Änderung, 2026-10-07):** neuer Engine-Typ `SalesEngine.Training.TraineeProfile(Name, Company)`, optional: `new TrainingCoach(session, generator, trainee)` → `GuidanceContext.Trainee` → Prompt-Feld `salesperson`. Die Prompt-Regel „schreibe [Name]/[Firma]“ ist ersetzt durch „nutze `salesperson`; ist es `null`, stell dich ohne Namen/Firma vor; nie Platzhalter“. Zusätzlich lehnt `ExampleSentenceSchema.Parse` Sätze mit eckigen Klammern ab (→ normales „Beispiel konnte nicht erstellt werden“, nie ein roher Platzhalter beim Spieler). Console unverändert (kein Profil → Fallback). Unity: `SessionStartRequest.Player` (`PlayerProfile`), gesetzt im `SalesTestSceneBootstrap` (`Tomasz` / `tom-gre-it`, Inspector-Felder); unvollständige Profile → kein Profil.
- **Verfügbarkeit nach Difficulty:** `ISalesGameSession.GetGuidanceAvailability(level)` → `GuidanceAvailability` (`Available`, `NotAllowedByDifficulty`, `RequiresAi`, `ConversationOver`, `NotStarted`, `NotConnected`), im Adapter 1:1 aus `TrainingCoach.Check` (Engine-`GuidancePolicy`). Der Controller fragt sie bei jedem Input-Statuswechsel ab; nicht erlaubte Buttons sind schon vor dem Klick deaktiviert, mit Hinweis „nicht auf dieser Stufe“ bzw. „nur mit KI“, werden nie angefragt und nie gezählt. Stand Engine: Easy (Level 80) alle drei, Medium (50) Hinweis + Mehr Hinweis, Hard (10) nur Hinweis. Die Stufe kommt aus dem Run-Setup (siehe unten); Default ohne Angabe ist Easy.
- **UI:** `GuidancePanelView` unter dem Chat (Debug-Panel ist dafür nach rechts gerückt), Buttons „Hinweis“, „Mehr Hinweis“, „Beispiel“ direkt über der Eingabe. Eine Sektion pro Stufe, nie im Chat. Beispiel zeigt „Beispiel wird formuliert …“. Während eines Kunden-Zugs sind die Buttons gesperrt; während einer Guidance-Anfrage sind Senden/Beenden und weitere Guidance gesperrt, Tippen bleibt möglich (Entwurf bleibt erhalten, `PlayerInput.onFocusSelectAll = false`). Nach dem Senden eines Zugs wird die Guidance geleert (sie galt für diesen Zug).
- **Zählung:** `SalesSim.Game.GuidanceUsage` pro Run im `SalesConversationController`, nur erfolgreich angezeigte Guidance. Result Screen: „Guidance genutzt“ mit Hinweise / Mehr Hinweise / Beispiele, bei 0/0/0 zusätzlich „Run ohne Hilfe“. Kein Einfluss auf Lead-Wert oder Erlös.
- **Aufbau:** „Sales Sim/Build Guidance UI“ (`GuidanceUiBuilder`); die Result-Zeile baut `GameLoopUiBuilder`.
- **Tests:** EditMode `SalesEngineGameSessionTests` (Hint/HintMore ohne HTTP, gleicher Fokus, verschiedene Texte; Example über `example_sentence`-Request; Fehler ohne State-Änderung; ohne Key `RequiresAi`; nach Ende `ConversationOver`), `GuidanceUsageTests`, Spielerprofil im HTTP-Request, Fallback ohne Profil, Platzhalter-Antwort wird abgelehnt, Verfügbarkeit je Easy/Medium/Hard gegen `GuidancePolicy`; PlayMode `GuidanceTests` (13 Fälle, gescriptete Session) und `GuidanceDifficultyTests` (echte Engine je Difficulty, Platzhalter-Kunde, fester Beispielgenerator, kein API).

## Run-Setup, Seeds und Reruns (2026-10-07)

- **Engine (unverändert genutzt):** `ScenarioGenerator.Generate(seed)` ist deterministisch (gleicher Seed + Generator-Version + Content = gleicher Origin). Die Run-Difficulty (`DifficultyProfile.Easy/Medium/Hard`) ist **kein** Input des Generators: gleicher Seed = gleiche Welt auf jeder Stufe; die Stufe ändert nur Kundenverhalten und Guidance. Zufällige Seeds wie die Console bei `--random`: `TrainingPools` mit `TrainingPool.Standard`.
- **Boundary:** neuer Port `IScenarioSource` (`Difficulties`, `PickRandomSeed()`, `ScenarioIdFor(seed)`), Engine-Implementierung `EngineScenarioSource` (Adapter). `SessionStartRequest.Difficulty` (Preset-Name; leer = Default Easy). Der Adapter loggt pro Start `[SalesClient] Run: hair-salon:<seed> (generator v2, origin <business/perspective/offer>, customer <Name>), difficulty <Stufe>`.
- **Performance:** `TrainingPools.PickSeed` erzeugt bei jedem Aufruf 10.000 Origins (im Editor ~0,4–3 s). `EngineScenarioSource` baut die Kandidatenliste (`TrainingPools.CandidateSeeds`, dieselbe Auswahl) einmal und zieht daraus wie `PickSeed`; der Bootstrap wärmt sie beim Start im Hintergrund vor.
- **Run-Metadaten (Game):** `RunInfo` (RunId, Seed, Difficulty, ScenarioId, IsRerun) und `RunRegistry` (im Speicher, nicht persistiert): gespielte Seeds, letzter Run, letzter Score pro Seed.
- **Rerun = Training:** Ein Seed, der in dieser Spielsitzung schon gespielt wurde, ist ein Trainingslauf, egal auf welcher Stufe und auch wenn er manuell eingetippt wird (gleicher Seed = gleicher Origin). `LeadSale(isTraining: true)`: Bewertung wie immer, `CanSell = false`, `SellTo` wirft, Result zeigt „Trainingslauf – kein Payout“ und „Vorher: X · Jetzt: Y (±Δ)“. Economy-Formel unverändert.
- **Flow:** Start → Setup-Overlay (Stufe, Seed leer = zufällig / eintippen / „Zufällig“, „Neuer Run“, „Seed wiederholen“ = letzter Seed mit dessen Stufe) → Gespräch (Run-Zeile oben links) → Result (verkaufen / „Diesen Seed erneut spielen“ / „Neuer Run“ → Setup). Aufbau über „Sales Sim/Build Game Loop UI“, danach „Sales Sim/Build Guidance UI“.
- **Tests:** EditMode `RunTests` (Registry, Seed-Parsing, Training-Lead), `RunSetupEngineTests` (Engine-Presets, Scenario-Id, Zufallsseeds aus dem Standard-Pool und identisch zu `PickSeed`, gleicher Seed = gleicher Origin, gleicher Seed + Stufe = gleicher Start, Stufe ändert nicht den Origin, Guidance je Stufe, unbekannte Stufe); PlayMode `RunSetupTests` (12 Fälle, gescriptet) und `GuidanceDifficultyTests` (Start über das Setup, echte Engine).

## Spielstand und Kopieren (2026-10-07)

- **Save-Datei:** `{Application.persistentDataPath}/savegame.json` (Windows: `%USERPROFILE%\AppData\LocalLow\DefaultCompany\B2B Sales Simlator\`; Editor und Player teilen sie). Felder: `version`, `balance`, `runsStarted`, `playedSeeds`, `lastScores` (Seed → letzter Score), `hasLastRun`/`lastRun` (Seed, Difficulty, ScenarioId). Klassen: `SalesSim.Game.SaveData` / `IGameStateStore`, `SalesSim.Infrastructure.JsonFileGameStateStore` (JsonUtility).
- **Schreiben:** atomar über `savegame.json.tmp` → `File.Replace`, vorheriger Stand als `savegame.json.bak`. **Laden:** keine Datei = neues Spiel; unlesbar = Backup, sonst neues Spiel, die kaputte Datei bleibt als `savegame.corrupt-<Zeit>.json`, Warnung in der Console; unbekannte Felder werden ignoriert; neuere `version` = bekannte Felder laden, Original als `savegame.v<n>.json` sichern. Versionierung: neue Felder brauchen keinen Bump, Bedeutungsänderungen schon.
- **RunRegistry** bleibt die Laufzeitquelle: `SalesRunLoop.Configure(..., store)` lädt (`RunRegistry.Restore`, `new PlayerWallet(balance)`), gespeichert wird bei Run-Start (Seed gilt ab dann als gespielt, auch wenn das Spiel mitten im Run beendet wird), Run-Ende (Score) und Verkauf (`RunRegistry.WriteTo`).
- **Dev-Reset:** Editor-Menü „Sales Sim/Dev/Spielstand zurücksetzen“ und „…/Spielstand-Ordner öffnen“; nicht im Spiel sichtbar.
- **Copy – Ursache:** Guidance-Text war ein reines `TextMeshProUGUI` mit `raycastTarget = false` → nicht markierbar, Ctrl+C hatte nichts zu kopieren. Chat (read-only `TMP_InputField`): im gebauten Player mit echtem Ctrl+C (SendKeys) geprüft → kommt als KeyDown an und landet in der Windows-Zwischenablage, ist aber an Fokus + Maus-Markierung gebunden (jeder Klick/Fokuswechsel hebt sie auf), und TMP reagiert nur auf KeyDown, nicht auf IMGUI-„Copy“-Commands. Editor-Game-View ließ sich nicht automatisiert prüfen.
- **Copy – Lösung:** „Kopieren“-Button (`CopyTextButton`, Feedback „Kopiert“ für 1,5 s) an jeder Chat-Nachricht und jeder Guidance-Stufe (nur bei fertigem Text, nicht beim Laden/Fehler), schreibt über `TextClipboard.Current` (Standard: System-Zwischenablage, Tests: Fake). Guidance-Text ist jetzt dasselbe markierbare read-only `SelectableMessageText` wie der Chat. Nach dem Kopieren (und nach Guidance) bekommt das Eingabefeld den Fokus zurück, Cursor ans Ende des Entwurfs.
- **Tests:** EditMode `SaveGameTests` (14); PlayMode `CopyTests` (7, Fake-Clipboard; der echte Ctrl+C-Test wird übersprungen, wenn die Zwischenablage gesperrt ist), `SaveGameFlowTests` (3, temporäre Save-Datei).

## Push-to-Talk (Spracheingabe, 2026-11-02)

- **Vorhanden (Engine, wiederverwendet):** `SalesEngine.Voice` (neutrale Verträge `ISpeechToTextProvider` / `ITranscriptionSession`, netstandard2.1) und `SalesEngine.Voice.ElevenLabs` (`ElevenLabsSpeechToTextProvider`: Realtime-WebSocket, `scribe_v2_realtime`, manueller Commit, Abbruch = Dispose ohne Commit, `TranscriptionException` mit `Kind`, Key nur im Header, nie in Meldungen). Push-to-Talk-Ablauf und Mikrofon gab es nur in der Console (`SalesConsole/PushToTalk.cs`, `WindowsMicrophone`); Unity übernimmt denselben Ablauf, eine eigene STT-Anbindung gibt es nicht.
- **Engine-Änderung (Branch `feature/unity-netstandard-targets`):** `SalesEngine.Voice.ElevenLabs` baut zusätzlich `netstandard2.1` (wie AI/Content): `[GeneratedRegex]` und `HttpRequestError` unter `#if NET`; neue Polyfills in `Shared/NetStandardPolyfills.cs` (`Task(.<T>).WaitAsync(CancellationToken)`, nicht-generisches `TaskCompletionSource`, `ObjectDisposedException.ThrowIf`, `HttpContent.ReadAsByteArrayAsync(CancellationToken)`). Monos `ClientWebSocket` hat kein `HttpStatusCode`; `ClientRealtimeSocket.HandshakeStatus` liest den Status aus der Handshake-Exception (`WebException`-Response oder „status code '401' …“), damit ein abgelehnter Key nicht als Netzwerkfehler erscheint. Engine-Tests: 1.553 bestanden, 26 übersprungen, 0 Fehler (+7 `RealtimeSocketTests`).
- **Sync:** `Tools/Sync-SalesEngine.ps1` baut und kopiert jetzt auch `SalesEngine.Voice.dll` und `SalesEngine.Voice.ElevenLabs.dll` (explicitly referenced, nur `SalesSim.EngineAdapter` und die EditMode-Tests sehen sie).
- **Unity-Boundary:**
  - Application: `ISpeechInput` (`StartRecordingAsync` → `ISpeechRecording` mit `FinishAsync` / `Interrupted` / `Dispose` = verwerfen), `SpeechInputException` + `SpeechInputError`, `IMicrophone` (PCM16-Chunks, wie die Console), `UnavailableSpeechInput`.
  - EngineAdapter: `ElevenLabsSpeechInput`. Öffnet zuerst die Transkription (ein falscher Key zeigt sich vor der Aufnahme), streamt das Mikrofon mit 16 kHz mono hinein, dann Commit → Transkript; Verwerfen ohne Commit. Mappt `TranscriptionErrorKind` → `SpeechInputError`. `Create` liest `ElevenLabsSpeechToTextOptions.FromEnvironment` (Key, `SALESSIM_ELEVENLABS_STT_MODEL`, `SALESSIM_ELEVENLABS_STT_LANGUAGE`, Default `de`); ohne Key nicht verfügbar mit Grund. Log: `[SalesVoice] Speech input: ElevenLabs scribe_v2_realtime, language de, timeout 30s` (nie der Key).
  - Infrastructure: `UnityMicrophone` (`UnityEngine.Microphone`, Standardgerät, Loop-Clip, alle 100 ms gelesen, float → PCM16, ggf. resampelt). Kein Gerät → `NoMicrophone`; 2 s ohne Samples → `MicrophoneDenied` (z. B. Windows-Datenschutz); Aufnahme bricht ab → `MicrophoneFailed`. `SalesTestSceneBootstrap` verdrahtet es; Key nur aus Umgebung / git-ignorierter `.env`.
- **Bedienung (`SalesConversationController`):** Enter bei **leerem** Eingabefeld oder Button „Sprechen“ → Aufnahme; Enter / „Stopp“ → Transkription; Esc → verwerfen (auch während der Verarbeitung). Enter mit getipptem Entwurf startet keine Aufnahme (Texteingabe bleibt wie bisher; der Button geht trotzdem, der Entwurf kommt bei Abbruch/Fehler zurück). Teil-Transkripte erscheinen live im Eingabefeld; das finale Transkript steht im Feld und geht dann über **denselben** Sendepfad wie Text (`SendTurn`), also unverändert durch Engine, Indikatoren, Phasen, Thinking, Charakter und Fehlerbehandlung. Während Aufnahme/Verarbeitung sind Senden, Tippen und Guidance gesperrt; ein Toggle pro Frame (Taste + Klick zählen einmal); der Button ist nicht per Navigation auswählbar und wird nach dem Klick abgewählt, sonst würde das Input-System-„Submit“ auf Enter ihn ein zweites Mal drücken. Beenden, neuer Run oder Zerstörung verwerfen eine laufende Aufnahme; späte Transkripte werden über einen Versuchszähler verworfen.
- **UI (`SpeechInputView`, Builder „Sales Sim/Build Speech Input UI“ → `SpeechInputUiBuilder`):** Button zwischen Eingabe (jetzt 0.04–0.70) und „Senden“, Statuszeile darunter. Zustände: Bereit („Sprechen“, hell), Startet / Verarbeitung („…“, gelb, gesperrt), Aufnahme („Stopp“, rot, pulsierender Punkt, „Aufnahme läuft – Enter: senden · Esc: abbrechen“), Fehler (orange Statuszeile, erneut startbar), nicht verfügbar (Button aus, Grund in der Statuszeile). Fehlertexte auf Deutsch je `SpeechInputError`, immer mit „nichts gesendet“.
- **Nicht gemacht:** Sprachausgabe des Kunden (`ElevenLabsTextToSpeechProvider` wäre jetzt in Unity ladbar, ist aber nicht angebunden), keine Änderungen an Economy oder Balancing.
- **Tests:**
  - EditMode `SpeechInputTests`: 23 Fälle mit Fake-Provider und Fake-Mikrofon (Ablauf, 16 kHz / Sprache, Commit + Trim, leeres Transkript, Verwerfen ohne Commit + Abbruch laufender Sends, Fehler-Mapping, kein Mikrofon, Verbindungsabbruch, Abbruch beim Start, Teil-Transkripte, Konfiguration mit/ohne Key ohne Key-Log, PCM-Konvertierung).
  - Dazu 3 `[Explicit]`-Tests, am 2026-11-02 bestanden: ungültiger Key aus Mono → `Authentication`; TTS-Satz → Adapter → „Guten Tag. Ich möchte Ihnen unsere Webseite vorstellen.“; echtes Standardmikrofon liefert 2,1 s PCM.
  - PlayMode `PushToTalkTests`: 22 Fälle mit gescripteter Session + Spracheingabe (Enter/Button starten und stoppen, Esc ohne Zug, Esc während Verarbeitung, Transkript im Feld + als Zug + Kundenantwort, leeres Transkript, Startfehler je Art + erneuter Versuch, Transkriptionsfehler behält Entwurf, Verbindungsabbruch, Beenden während Aufnahme, neuer Run, doppelte Eingaben, Texteingabe unverändert, nicht verfügbar).
  - Gesamt: EditMode 116 bestanden / 3 übersprungen (die expliziten Live-Tests), PlayMode 73 bestanden / 1 übersprungen (`SalesEngineSmokeTest`, explizit).
  - Gesprochen ins echte Mikrofon und im Editor gespielt wurde noch nicht (keine Person in der Testumgebung).

## Kundenantworten bei Key ohne Workspace (Fix 2026-11-02)

- **Symptom:** Nach dem Tausch des abgelaufenen Anthropic-Keys kam auf jede Nachricht keine Kundenantwort; die Nachricht war nur als „nicht zugestellt“ markiert.
- **Ursache:** Der neue Key gilt für die ganze Organisation und ist keinem Workspace zugeordnet. Für solche Keys verlangt die API den Header `anthropic-workspace-id` (`wrkspc_…`); ohne ihn antwortet sie mit HTTP 400 (`invalid_request_error`: „This API key is not scoped to a workspace …“). `.env`, Key-Format, Modell (`claude-sonnet-5-5`) und Endpunkt waren in Ordnung, keine Umgebungsvariable hat die `.env` überschrieben. Unity zeigte den Grund nicht an; er stand nur in der Console.
- **Engine:** `LlmOptions.WorkspaceId` aus `SALESSIM_ANTHROPIC_WORKSPACE_ID` (optional, nur Anthropic, muss mit `wrkspc_` beginnen). `AnthropicMessagesClient` sendet ihn als `anthropic-workspace-id`; ohne Wert wird kein Header gesendet (Workspace-Keys laufen wie bisher). Die 400-Antwort „not scoped to a workspace“ wird in beiden Anthropic-Clients zu einer klaren Meldung mit der zu setzenden Variable. Console: Bei gesetzter Workspace-ID bekommt der SDK-Client einen eigenen `HttpClient` mit dem Header (das SDK hat keinen Request-Header-Parameter).
- **Unity-Boundary:** `ISalesGameSession.SendPlayerTurnAsync` wirft jetzt `SalesTurnFailedException` mit `SalesTurnFailure` (`Authentication`, `WorkspaceRequired`, `BadRequest`, `RateLimited`, `QuotaExceeded`, `Timeout`, `Network`, `ServiceUnavailable`, `InvalidAnswer`, `Unknown`) statt der Engine-Exception; der Adapter mappt `LlmProviderException`/`TimeoutException`. Zustand bleibt unverändert, der Zug kann erneut gesendet werden.
- **Anzeige:** Bei einem fehlgeschlagenen Zug erscheint im Chat eine Hinweis-Blase (`ConversationSender.Notice`, Label „Hinweis“, bernsteinfarben) mit Ursache und Abhilfe, z. B. „Trag die Workspace-ID … als SALESSIM_ANTHROPIC_WORKSPACE_ID in die .env ein …“. Die Spielernachricht bleibt „nicht zugestellt“, der Text kommt zurück ins Eingabefeld, Eingabe und Senden sind wieder frei. Erwartete Provider-Fehler werden als Warning geloggt, unerwartete weiter als Exception. Eine Abbruch-Exception, die nicht vom Zerstören der Szene kommt, blockiert die Eingabe nicht mehr dauerhaft.
- **Diagnose-Log beim Start (ohne Werte):** `[SalesClient] Settings: SALESSIM_ANTHROPIC_API_KEY from .env, SALESSIM_ANTHROPIC_WORKSPACE_ID from not set` (bzw. `environment (overrides .env)`), und `[SalesClient] Customer: Anthropic LLM (model …, timeout …, workspace wrkspc_… | from the key)`.
- **Tests:**
  - Engine: +9 (Header gesendet / nicht gesendet, 400 ohne Workspace → klare Meldung in beiden Clients, andere 400 unverändert, Workspace-ID lesen/validieren/OpenAI ignoriert); gesamt 1.564 bestanden, 26 übersprungen, 0 Fehler.
  - Unity EditMode: `SalesEngineGameSessionTests` +9 (Workspace-Header, `WorkspaceRequired`, 401/403 → `Authentication` ohne Key im Log, Timeout, Netzwerkfehler, ungültige Workspace-ID, Wiederholung nach Fehler), `LocalSettingsTests` (4: `.env` lesen, neuer Key beim nächsten Laden, Umgebungsvariable gewinnt und wird gemeldet, nicht gesetzt); gesamt 129 bestanden, 3 übersprungen (explizite Live-Tests).
  - Unity PlayMode: `CustomerReplyFailureTests` (9: Hinweis je Fehlerart, Eingabe frei, kein erfundener Kunde, Retry = normaler Zug mit Antwort, kein doppelter Zug, unerwarteter Fehler, Abbruch blockiert nicht); gesamt 82 bestanden, 1 übersprungen (`SalesEngineSmokeTest`, explizit).
  - Live (`SalesEngineSmokeTest`, angepasst an das Run-Setup, ohne Savegame): mit dem aktuellen Key und ohne Workspace-ID → HTTP 400, im Spiel erscheint der Workspace-Hinweis. Nach dem Eintragen der Workspace-ID: ein minimaler Live-Aufruf über denselben `AnthropicMessagesClient` mit Key + Workspace aus der `.env` → HTTP 200, Antwort gelesen (2026-11-02). Ein vollständiger Zug im Unity-Editor (Play) ist noch durch den Spieler zu bestätigen; der Batchmode-Lauf der Projektkopie scheiterte an Windows-Pfadlängen, nicht am Code.

## Bewertungsaufsteller vor Ort (2026-11-03)

- **Engine (Quelle aller Regeln):** siehe `sales_engine/docs/direct-sale.md`. Aufsteller = eigenes Produkt, 39,00 € einmalig; Kauf, Vor-Ort-Einrichtung und Zahlung (Überweisung, PayPal, bar, Karte/Stripe-Checkout; Status offen/bestätigt/fehlgeschlagen) sind getrennte Sachverhalte; Website optional danach. Neuer Pool `review-stand-visit` (nur Besuche, nur Aufsteller, Website als Folgeangebot).
- **Boundary:** `SalesSessionState.ProductSale` (`ProductSaleState`, 1:1 aus `ConversationSession.DirectSale`), `HasLeadOffer` (beim Aufsteller erst nach Wechsel zur Website), `Commitment` = Vereinbarung des Lead-Angebots (Website). `IScenarioSource.Kinds/Select` für die Wahl „Website & Co.“ / „Bewertungsaufsteller (Vor-Ort-Besuch)“.
- **Economy:** `ProductSettlement` bucht bei Run-Ende nur `BookedRevenue` (bestätigte Zahlung), offene/fehlgeschlagene Zahlungen werden nur angezeigt, Trainingsläufe buchen nichts. `LeadEvaluator`: ohne Lead-Angebot kein Lead. Aufsteller + Website-Lead = 39 € + Payout, nie doppelt.
- **Runs:** „schon gespielt“ gilt jetzt pro Szenario-Id (Seed + Art); alte Spielstände ohne `playedScenarios` behandeln ihre Seeds weiter in allen Arten als gespielt.
- **UI (Builder „Sales Sim/Build Game Loop UI“):** Auswahl der Art im Setup, Live-Statuszeile (`SaleStatusLabel`), Produktblock im Result Screen (verkauft, Einrichtung, Zahlungsmethode, Zahlungsstatus, weiterer Verlauf, fragwürdige Produktaussagen).
- **Tests:** Engine 1.649 bestanden / 26 übersprungen (+85). Unity EditMode 147/3 übersprungen (+18, `ReviewStandSaleTests`), PlayMode 91/1 übersprungen (+9, `ReviewStandFlowTests`). Zwei Live-Läufe gegen Anthropic bestanden (Ablehnung bei Zeitdruck; Kauf → Einrichtung → offene Überweisung → Website).

## Weitere offene Punkte

- `Engagement` vs. `Interest` (s. Mapping).
- `IsConversationOver`/`FinalTurnPending`: Unity zeigt die angekündigte letzte Runde noch nicht an.
- Opening-Evaluator ist nicht verdrahtet (beeinflusst keine der angezeigten Werte).
- Player-Builds: Content liegt in `StreamingAssets` (Desktop OK; Android/WebGL bräuchten anderes Laden).
- Erledigt 2026-11-02: Engine-Branch nach `main` gemergt und neu gesynct (`ENGINE_VERSION.txt` nennt `main@029e3e7`). DLLs, Content und `ENGINE_VERSION.txt` liegen im Unity-Repo, das Projekt baut ohne Engine-Checkout.

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
>
> Ist 2026-10-07 nach dem Background-Pass erneut passiert: Der letzte Batchmode-Lauf war ein PlayMode-`-runTests`. Der
> Test Runner stellt am Ende seine Backup-Szene wieder her, das ist die leere Startszene, und beim Beenden steht dann
> `sceneSetups: []` in der Datei (reproduzierbar). Ein reiner `-executeMethod`-Builder-Lauf hinterlässt `SalesTestScene`.
> Nach **jedem** Testlauf im Batchmode, bei geschlossenem Editor, diesen Inhalt wiederherstellen:
>
> ```
> sceneSetups:
> - path: Assets/Game/Scenes/SalesTestScene.unity
>   isLoaded: 1
>   isActive: 1
>   isSubScene: 0
> ```
>
> Batchmode-Testläufe können außerdem in `ProjectSettings.asset` das Define `SENTIS_ANALYTICS_ENABLED` entfernen
> (Paket-Nebeneffekt). Diese Änderung gehört nicht zur Arbeit und wird zurückgesetzt.
