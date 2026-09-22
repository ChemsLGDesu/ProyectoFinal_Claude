---
tags: [tcgmaster, tttxo, estado-proyecto, milestone-5, ranked]
---

# MEMORIA Proyecto TicTacToe (UGS)

## Estado actual

> **Cómo leer este archivo.** Esta sección es la **única** fuente de verdad sobre el estado presente.
> Todo lo que viene abajo son entradas de bitácora fechadas: sus secciones de "Pendientes" son una
> foto del momento en que se escribieron, no una lista viva. Si una contradice a esta sección, gana
> esta. Todos los números de acá se verificaron contra el repo, el Editor o el Dashboard el
> 2026-08-08; no se copian de entradas anteriores, que es como se colaron tres datos falsos.

**Versión:** v0.6.0 (primera beta de Android). **Rama:** `main`, árbol limpio. Es la **única rama**, local y remota: `docs/project-design-guidelines` se borró de los dos lados el 2026-08-08 tras confirmar que estaba contenida entera en `main`.

**Existe un APK.** `Builds/Android/TTTXO-v0.6.0-b4-beta.apk`, 145.8 MB, `arm64-v8a` + `armeabi-v7a`, target SDK 35 — el primer build de Android del proyecto es del 2026-08-11; los `W.0.0.x` anteriores eran todos de Windows. `Builds/` está en `.gitignore`, así que el archivo no viaja en el repo. Se corta con `TTTXO → Build → Android Beta APK` (`Assets/Scripts/Game/Editor/BetaBuild.cs`). Identidad: `com.hellscythe25.tttxo`, company `Hellscythe25` — las tres cosas estaban sin configurar hasta ese día. Procedimiento completo en *Docs/10-Beta-Testers.md*.

**El `b4` está en manos del tester desde el 2026-08-12 y la sesión cerró esperando su respuesta.** Es el primer APK con las dos ABIs y con target SDK fijado; se entregó con tamaño exacto (152 912 041 bytes) y SHA-256 para que el tester descarte corrupción antes de instalar. Se le pidieron tres cosas: si instaló y si abre, modelo y versión de Android, y el texto exacto del error si falla. **La segunda es la que puede tumbar la hipótesis**: si el teléfono es de 64 bits y el `b4` igual funciona, la ABI no era la causa aunque el síntoma haya desaparecido.

**El `b2` no instalaba en teléfono; ni el `b3` ni el `b4` se probaron todavía en uno.** El primer APK salió solo con `arm64-v8a` contra un `minSdkVersion` de 25, que es la contradicción que mejor explica los tres síntomas (falla en físico, anda en MuMu, no anda en BlueStacks): en un dispositivo de 32 bits eso no falla al abrir, falla al instalar con `INSTALL_FAILED_NO_MATCHING_ABIS`. **Es la hipótesis que sobrevivió, no una causa confirmada.** Descartadas midiendo: firma (v2 válida), páginas de 16 KB (los 7 `.so` ya en `0x4000`), APK de split y archivo corrupto. Los cuatro logs que mandaron los testers eran **todos del emulador** y no contenían el fallo.

**Git: al día con el remoto.** El 2026-08-09 se pushearon los 4 commits del arnés de Quickmatch (`d000dff..9ae6c19`). Antes, el 2026-08-08, los 89 acumulados (`9118bca..0951f52`) — hasta ese momento `origin/main` estaba parado en el commit inicial del 2026-07-23. `main` ya tiene upstream, así que alcanza con `git push`. Repo **privado**: `https://github.com/Hellscythe25/TTTXO.git`. *(Las notas viejas de "sin credenciales de GitHub" eran falsas; el remote siempre existió.)*

**El repo usa Git LFS** — 131 objetos, ~123 MB. No estaba anotado en ninguna parte y aparece recién al pushear o clonar: en una máquina nueva hace falta `git lfs install` o los binarios bajan como punteros de texto.

**Repo privado, y hay una decisión abierta de hacerlo público.** La auditoría del 2026-08-12 no encontró **ningún secreto** en los 126 commits de historial: sin API keys, tokens, keystores ni claves privadas (la de Gemini vive en `EditorPrefs`). Lo que sí queda expuesto son los identificadores de UGS (`cloudProjectId`, `EnvironmentId`, en 8 lugares incluidos los tres scripts de `Tools/`), que no son secretos —viajan en cualquier APK— pero que **junto al repo** entregan el mapa de qué claves eran escribibles y herramientas ya apuntadas. Ese era el bloqueo, y es lo que ERR-KB-006 vino a cerrar. También queda público el email personal en los commits, que solo se saca reescribiendo el historial.

**Tests:** 240 EditMode, 0 fallan, medidos el 2026-08-12 vía `TestRunnerApi` desde el Editor (`TestResults.xml`: `total="240" passed="240"`). Los 3 nuevos fijan la separación de monederos de ERR-KB-006 en los dos sentidos. Los 6 últimos son `BoardSelectEligibilityTests` (2026-08-09). **El reparto por assembly no se volvió a verificar** desde que la cifra era 229 (75 `TTTXO.Core.Tests` + 154 `TTTXO.Game.Tests`); 6 de los de Game (`LocalAnalyticsSinkTests`) solo existen con `TTTXO_LOCAL_ANALYTICS` definido.

**Protocolo:** handshake `GameProtocol.Version` verificado contra el módulo de Cloud Code por `ContractMirrorTests`.

**Backend:** deployado al environment `development` el 2026-08-08 y verificado contra el Dashboard, no contra la ventana de Deployment. Ver *Manual del dueño → Deploy crítico*.

**Milestone en curso:** M5 Ranked, cierre. La validación en vivo del backend está hecha para los dos modos online; falta solo la pasada manual con dos clientes.

**Pendientes urgentes:**
1. ~~Validación end-to-end de Ranked~~ y ~~de Quickmatch~~ — **ambas hechas y en verde** contra el servidor real: Ranked el 2026-08-08 (`Tools/ranked-e2e.ps1`, encontró y cerró **tres bugs de producción**), Quickmatch el 2026-08-09 (`Tools/quickmatch-e2e.ps1`, pasó limpio — los tres bugs estaban en camino compartido y ya se habían arreglado). Lo que **sigue abierto** de validación: (a) la **pasada manual con dos clientes** — Wire push, UI de la serie, pantalla de matchmaking; (b) decay y rollover de temporada, que dependen del tiempo (frontera real el 28 de agosto).
2. **Encuadre en dispositivos:** 6 medidos simulados, **cero en hardware real**. Desde el 2026-08-11 ya no falta el APK — falta el teléfono. Instalarlo y llegar a Home es el chequeo más barato que queda sin hacer, y el que ninguna medición por MCP sustituye. iOS sigue sin ser viable en Windows sin Mac o Build Automation.
3. ~~Decisión de diseño abierta: ERR-KB-006~~ — **cerrada en código el 2026-08-12**, salida 1 (dos saldos separados). Los premios online van ahora a `authoritativeCurrency` en clase `Protected`; los cinco contadores anti-abuso a `Private`; `currency` queda como espejo local-first escribible, a propósito. **Falta la verificación contra el backend**: hay que deployar el módulo y volver a correr la sonda de `Tools\quickmatch-e2e.ps1`, que es lo único que prueba que la escritura se rechace. `GameProtocol.Version` 1 → 2, así que **el `b4` que tiene el tester es un cliente v1 y dejará de ver sus premios online en cuanto se deploye**.
4. **Registrar los 5 eventos de Ranked en el Event Manager.** Ya no bloquea beta —con `TTTXO_LOCAL_ANALYTICS` van a disco— pero **sí bloquea producción**: sin registrar, el pipeline los descarta.
5. **Deploy de Triggers** (`ugs deploy` para `Assets/Triggers/`). Necesita `ugs login` primero: CLI 1.9.0 devuelve 401.

---

## 2026-08-12 — Auditoría para repo público y cierre de ERR-KB-006

### Hecho

- **Auditoría completa de exposición** sobre 639 archivos y 126 commits, mirando historial y no solo HEAD. Sin secretos. Detalle en *Estado actual*.
- **ERR-KB-006 cerrado en código**, salida 1 de la ficha. Cinco claves reclasificadas, `ACCESS CLASS` greppeable en cada store, monedero del cliente separado en dos, `GameProtocol.Version` 1 → 2.
- Módulo de Cloud Code compila (0 errores) y la suite pasa 240/240.

### Decisiones que conviene no re-litigar

- **`currency` sigue escribible por el cliente, a propósito.** Para 1P y 2P local el servidor no puede verificar que la partida ocurrió, y ningún diseño lo cambia; bloquear el espejo de una economía que el servidor nunca ve no compra nada. Lo que se arregló es que los premios *online* dejaran de compartir esa clave.
- **La búsqueda tiene que ser por patrón de I/O, no por clave.** La tabla de la ficha listaba tres claves; grepear el módulo por `GetItemsAsync`/`SetItemAsync` encontró **cinco**. El defecto nunca estuvo en las claves.
- **El fix no está verificado.** Compilar y tener la suite verde no prueba nada sobre clases de acceso: ningún test de Editor puede. Lo único que lo prueba es la sonda de `quickmatch-e2e.ps1` contra el backend real, que es exactamente cómo se encontró.

### Hallazgos

- **`RankedRewardStore` no estaba en la ficha** y era la peor de las dos claves que faltaban: `rankedWin_*` es el anti-boosting de MMR, y resetearlo da Elo sin amortiguar contra el mismo rival indefinidamente.
- **Un test existente frenó una versión rota del fix.** `ApplyOnlineRewardCredit_DoesNotTouchLocalStateTimestamp` codificaba que ese método no debe estampar el reloj de estado local; la primera versión llamaba `PersistState()` y lo rompía. El invariante estaba escrito, por eso no pasó.
- **Los IDs de UGS no son secretos pero el repo los vuelve accionables.** El `cloudProjectId` sale de cualquier APK en segundos; lo que agrega el repo es el mapa de qué era escribible y tres scripts E2E con el ID ya como valor por defecto.

### Pendientes

1. **Deployar el módulo y correr la sonda.** Sin eso, ERR-KB-006 está cerrado en código y abierto en el servidor.
2. **Migración: no hay.** Los `rankedProfile` existentes se pierden al cambiar de clase y el MMR vuelve al inicial. Aceptado por ser datos de prueba, pero **el leaderboard ya tiene entradas** y no se limpian solas.
3. **Cortar un `b5`** antes de deployar, o el `b4` del tester queda como cliente v1 sin ver sus premios.
4. **Decidir sobre el email en el historial** antes de publicar.

## 2026-08-11 / 12 — Preparar el reparto a testers (primer APK de Android)

> Dos días, cuatro builds. El `b2` fue el primer APK del proyecto y no instalaba en ningún teléfono;
> el `b3` agregó la segunda ABI, el `b4` fijó el target SDK y es el que se entregó. Ni el `b3` ni el
> `b4` corrieron todavía en hardware: la sesión cierra esperando la respuesta del tester.

### Hecho

- **Identidad de app para Android, por primera vez.** `com.hellscythe25.tttxo`, company `Hellscythe25`, `bundleVersion` `0.6.0`. Antes de tocarlo: `companyName: DefaultCompany`, **ningún** `applicationIdentifier` para Android (solo el `com.DefaultCompany.2D-URP` de la plantilla) y `bundleVersion: 1.0` con el proyecto en su quinto milestone. Package name elegido con el dueño; es permanente si algún día hay Play.
- **`Assets/Scripts/Game/Editor/BetaBuild.cs`** — build de beta en un comando, con entrada de menú y de batch mode. Aplica la identidad, incrementa `versionCode`, fuerza APK sobre AAB y pone `BuildOptions.Development`.
- **`Docs/10-Beta-Testers.md`** — procedimiento de reparto, límites conocidos y mensaje listo para copiar.
- **APK generado y verificado en disco**: 104.8 MB.
- Changelogs de dev y de jugadores al día; `[Unreleased]` se cerró como `[0.6.0]`.

### Decisiones que conviene no re-litigar

- **La beta es Development Build a propósito, no por descuido.** `LocalAnalyticsBuildGuard` aborta cualquier release que todavía defina `TTTXO_LOCAL_ANALYTICS`, y la beta quiere ese define. Las dos reglas solo componen en un sentido: development + define, o release sin define. No hay tercera combinación válida.
- **La contraseña del keystore no pasa por el repo ni por el chat.** `BetaBuild` la lee de cuatro variables de entorno y, si falta alguna, cae al keystore de debug avisando qué cuesta esa caída. `ProjectSettings.asset` está versionado, así que guardarla ahí la publicaría.
- **La identidad se aplicó por script, no editando el `.asset`.** Unity tiene los Project Settings en memoria y los baja a disco recién al guardar el proyecto; una edición a mano con el Editor abierto se pierde. Ya estaba anotado en el *Manual del dueño* y esta sesión lo confirmó.

### Hallazgos

- **El APK pesa 104.8 MB para un tres en raya.** Parte es el Development Build. La otra parte es que **`com.unity.ai.inference` (Sentis) entra al player con sus shaders de runtime aunque ningún script del juego lo referencie** — llega como dependencia de `com.unity.ai.assistant`, que es la herramienta de Editor sobre la que corre el MCP. Sacarlo rompería el tooling, así que queda como decisión de `systems-programmer`. **Cuánto pesa cada mitad no se midió.**
- **`Android App Info` sin configurar en Localization Settings** (warning de la build). Afecta el nombre en el lanzador y el selector de idioma por app de Android 13+. **No** afecta el selector que tiene el juego adentro: se verificó leyendo la cadena de arranque en `LocalizationSetup` (`PlayerPrefLocaleSelector` → `SystemLocaleSelector` → default), ninguno de los cuales depende de esa metadata.
- **El "Unity not detected" del MCP no es exclusivo de play mode.** T-07 lo atribuye a un heartbeat rancio y T-08 a play mode; esta sesión lo vio dos veces **en edit mode**, las dos justo después de escribir un `.cs`, o sea durante el recompilado. Se destrabó reintentando. El archivo de descubrimiento tenía 10 segundos de antigüedad cuando falló, así que "rancio" no alcanza como explicación.

### Hallazgos del reporte de testers (misma fecha, más tarde)

- **`zipalign -c -P 16` no decide nada sobre 16 KB.** Devuelve `OK - compressed` para cada `.so`, que se lee como aprobación y no lo es: exime a las entradas comprimidas. Lo único que decide es `llvm-readelf --program-headers` sobre el ELF. Casi cierro la hipótesis de 16 KB por el lado equivocado, en los dos sentidos.
- **Un log de emulador no prueba nada sobre un teléfono.** Los cuatro archivos que mandaron los testers eran de MuMu y de VirtualBox; el `logcat_.log` muestra el juego corriendo 40 minutos sin drama. Sirvieron para confirmar que el juego funciona, no para diagnosticar el fallo — eso salió de inspeccionar el APK con `apksigner`, `aapt2` y `readelf`.
- **El "Unity not detected" del MCP se repitió dos veces más**, otra vez justo después de escribir un `.cs` y en edit mode. Esperar a que el `bridge-*.json` vuelva a tener menos de 6 segundos destraba sin reintentar a ciegas.

### Pendientes

1. **Esperar la respuesta del tester sobre el `b4`** — entregado el 2026-08-12. Es la única forma de convertir la hipótesis de ABI en diagnóstico. Sigue siendo el pendiente #2 del *Estado actual*. El `b4` mueve dos variables respecto del `b2` (ABI y target SDK), pero la atribución aguanta: **el `targetSdkVersion` no condiciona la instalación** — Android solo bloquea por target demasiado bajo (menor a 23, desde Android 14), nunca por bajar de 36 a 35.
2. ~~Fijar el Target SDK~~ — **hecho el 2026-08-12**: `AndroidTargetSdkVersion` de `0` (*Automatic*) a `35`, impuesto además por `BetaBuild.ApplyAndroidIdentity`. El `b4` es el primer APK que lo lleva (`targetSdkVersion: 35`, `compileSdkVersion: 35`, verificado con `aapt2`).
3. **El bloqueo a portrait sigue sin confirmar, y el manifiesto no lo puede confirmar.** Acá había escrito que bastaba con leer el manifiesto del build nuevo. **Era una prueba mal elegida**: `b3` (target 36) y `b4` (target 35) declaran los dos exactamente lo mismo — `screenOrientation=1` y `resizeableActivity=true`. La diferencia vive en cómo interpreta Android el `targetSdkVersion` en runtime, no en el archivo. Lo único que discrimina es girar el dispositivo en una pantalla de 600dp o más con Android 16.
2. **Crear el keystore de release** antes de la segunda tanda de testers — después ya cuesta una desinstalación por tester. Comando en `Docs/10-Beta-Testers.md`.
3. **Deploy de Triggers**, que ahora sí muerde: sin `SweepAbandonedMatches`, un tester que abandone una partida online deja al rival esperando.
4. El `applicationIdentifier` de Standalone sigue siendo `com.DefaultCompany.2D-URP`. No molesta a nadie hoy porque las builds de Windows son para el escritorio del dueño; queda anotado para que no sorprenda.

### Estado del Editor al cerrar

Fuera de play mode, `runInBackground` sin tocar (ERR-KB-004 limpio), target activo Android.

---

## 2026-08-08 — Construir juego completo M1–M5 (Ranked) con arquitectura UGS

### Hecho

Se implementó el juego TicTacToe multiplataforma en cinco milestones incrementales, cierre con M5 Ranked:

**M1 (v0.1.0)** — Lógica pura + Shell UI
- `TTTXO.Core` (sin UnityEngine): BoardState, MatchController, RulesEngine (k-in-a-line), AiPlayer (minimax + heurística Chebyshev), AdaptiveAiController, RewardCalculator
- 75 tests EditMode (RulesEngineTests, MatchControllerTests, AiPlayerTests, AdaptiveAiControllerTests, RewardCalculatorTests) — en su momento solo habían corrido en project standalone; desde el 2026-08-08 corren dentro del Editor
- Shell UI Toolkit (6 pantallas: Splash, Home, ModeSelect, BoardSelect, Game, Result), navegación con back stack
- GameManager (orquestación de match, alternancia first-player, soft wallet con persistencia PlayerPrefs)

**M1.5 + M2 (v0.2.0)** — UI según wireframes + Fundaciones UGS
- UI rediseñada según wireframes Claude Design (Docs/06-Wireframes-UI.md): ProfileScreen, MatchHistoryScreen, SettingsScreen (volume/language/delete-data), StoreScreen stub
- UGS Authentication: anonymous sign-in + player nickname "Name#1234", fallback offline
- Analytics wrapper (defensive, try/catch): match_started, match_finished, board_size_selected, soft_currency_earned
- Remote Config config-as-code (Assets/RemoteConfig/GameConfig.rc): BOARD_CONFIGS (tamaños + k-values), AI_DIFFICULTY_PARAMS
- Cloud Save: wallet, match history (50 entradas), adaptive AI levels (best-effort, sin error UI)
- Cloud Code module (`CloudCode~/TicTacToeModule/`): GetGameConfig(), CreateMatch(), PlayMove(), GetMatchState(), GetAiMove() — reutiliza TTTXO.Core sin duplicación via `<Compile Include>`
- Splash con inicialización UGS y timeout failover 60s

**M3 (v0.3.0)** — Localización (10 idiomas)
- Unity Localization 1.5.12 (en, es, fr, de, pt, it, id, vi, tr, pl)
- 85+ claves en UiStrings StringTable con fallback a inglés
- Language selector Settings (hot-reload sin restart)
- Font coverage tr/vi/pl flagged QA TODO

**M4 (v0.4.0)** — Online Quickmatch + Wire
- `com.unity.services.multiplayer` 2.2.3 (bundea Wire 1.4.4)
- Matchmaker config-as-code (Assets/Matchmaker/): QuickmatchQueue.mmq (skill disabled, backfill por board_size), MatchmakerEnvironmentConfig.mme, TTL 180s
- Cloud Code: CreateMatch() idempotent con verificación de ticket (best-effort, no bloquea), PlayMove() valida playerId + publica via Wire, GetMatchState() detección reactiva de abandono
- Client: MatchmakingService (CreateTicket → PollTicketStatus → CancelTicket), OnlineMatchService (Wire subscription + fallback polling 10s), MatchmakingScreenController (spinner + cancel + fallback "Play vs AI" a 45s)
- Analytics: matchmaking_wait_time, matchmaking_cancelled

**v0.4.1** — Server-side reward
- OnlineRewardCalculator (1.25× victoria, 1.0× draw, 0 abandono oponente)
- OnlineRewardStore (caps: 200/día online, 300/día global, 3 victorias/rival/24h)
- MatchStateDto.AwardedSoftCurrency (transmisión cliente-servidor)
- GameScreenController: muestra AwardedSoftCurrency server-sourced

**v0.4.2** — Ticket verification fix + Proactive abandoned sweep
- **Bug fix crítico:** CreateMatch recibía parámetros fuera de orden (ProjectId/EnvironmentId en lugar de ServiceToken/ticketId) → siempre 401. Ahora pasa parámetros correctos (bloqueante, no best-effort)
- MatchIndexStore: índice diario de partidas online activas en Cloud Save (particiones por fecha UTC)
- SweepAbandonedMatches(): Cloud Code function + Trigger config-as-code (Assets/Triggers/, cron cada 5 min) — scan 3 particiones, 200 matches/ejecución, idempotente
- MatchStateRecord.BothPlayersAbandoned: resolución como draw (ΔMMR=0, reward=0)

**M5 (v0.5.0)** — Ranked completo
- **Arquitectura Elo:** MMR separado por tablero (rankedProfile.mmr3x3, mmr6x6), K-factor variable (40 en las 5 de colocación, 32 hasta la 29, 24 established, 16 high), MMR floor 500, initial 1000
- **Serie 3×3 fairness:** 2-game series con swapped roles (game 1 X/O, game 2 O/X), MMR update único al final, RankedSeriesRecord en Cloud Save
- **Anti-boosting:** dilución por rival repetido en 24h (multiplier ×1.0/1.0/0.5/0.5/0.0 en victorias; pérdidas/draws siempre K completo)
- **Abandono Ranked:** full K loss/win solo abandoner, both abandon = no-contest (ΔMMR=0)
- **Temporadas:** 1 mes calendario, frontera el día 28 a las 00:00 UTC (antes 56 días; cambiado 2026-08-08), soft reset (MMR = 1000 + (prev-1000)×0.5, floor 500), decay (−25/día si MMR>1200, gracia 7 días, floor 1200)
- **Recompensas por tier:** Bronze 100 SC, Silver 200, Gold 400, Platinum 700, Diamond 1200 + frame cosmético, Top 100 +500 SC + banner
- **Leaderboard per-board per-season:** top N por MMR (ties por timestamp), visible post-placement (5 juegos desde 2026-08-08, antes 10), decay lazy
- **Matchmaker ranked queue:** Assets/Matchmaker/RankedQueue.mmq, skill-based MMR, relajación (0–9s ±100, 10–19s ±200, 20–29s ±350, 30–44s ±600, 45s+ open)
- **Remote Config:** RANKED_CONFIG con initialMmr, kFactor variables, tierNames/tierMinMmr/tierSoftCurrency, seasonStartUnixSeconds (1787875200 = 2026-08-28T00:00:00Z, ancla real), seasonDurationMonths (1)
- **Cloud Code 10 nuevos archivos:** RankedConfigReader, RankedEloCalculator, RankedSeasonCalculator, RankedTierCalculator, RankedRewardCalculator, RankedProfileStore, RankedRewardStore (anti-boosting ledger, distinto de moneda), RankedLeaderboardStore, RankedSeriesStore, RankedMatchSupport, RankedQueryFunctions (GetRankedProfile, GetRankedLeaderboard)
- **Client:** RankedQueryService (invoca endpoints, caché local), ModeSelect ahora lista "Ranked", BoardSelect filtrado 3x3/6x6, Result screen con card MMR delta (±N coloreado), Profile screen pestaña Ranked (MMR actual, tier badge, placement state, board switcher, season countdown), Leaderboard screen (wireframe 10) top 100+, indicador pending reward
- **Analytics 5 eventos nuevos** (deben registrarse en Dashboard antes de emisión): ranked_mmr_changed (board_size, mmr_before/after, delta, k_factor, result, reason), ranked_placement_completed, ranked_match_abandoned, ranked_queue_fallback_shown, ranked_season_reward_granted
- **Leaderboards config-as-code:** Assets/Leaderboards/ranked_3x3.lb, ranked_6x6.lb (UpdateType=Latest score, reset scheduled)

Archivos tocados clave: `CloudCode~/TicTacToeModule/` (10 archivos nuevos), `Assets/Scripts/Game/Services/RankedQueryService.cs`, `Assets/Scripts/Game/UI/` (ProfileScreen, LeaderboardScreen, ResultScreenController), `Assets/RemoteConfig/GameConfig.rc`, `Assets/Leaderboards/ranked_3x3.lb`, `Assets/Leaderboards/ranked_6x6.lb`, `Assets/Matchmaker/RankedQueue.mmq`, `Assets/Localization/UiStrings.asset` (20+ claves), `CHANGELOG-dev.md`

### Decisiones

| Decisión | Por qué | Impacto |
|----------|--------|--------|
| **TTTXO.Core sin UnityEngine** (noEngineReferences: true) | Permite reutilización en Cloud Code via `<Compile Include>` — mismas reglas, IA y lógica en servidor que en cliente. Anti-cheat máximo. Código puro testeable. | Architecura fuerte; Cloud Code compila sin duplicación; tests 100% verificables offline |
| **MMR en item Cloud Save separado (`rankedProfile`), NOT `profile`** | Cliente synca `profile` tras cada local 1P/2P y habría pisado MMR. Aislamiento: solo Cloud Code Ranked functions tocan `rankedProfile`. | Sin race conditions de sync, autoridad clara |
| **Serie 3×3 de 2 partidas** | 3×3 es juego resuelto; who goes first = ventaja sistemática. Serie con swapped roles neutraliza sesgo. | Ranked fair, no pay-to-win por orden |
| **Abandono bilateral = no-contest (ΔMMR=0)** | Empate normal = alguien gana rating por match que nadie jugó. No-contest detiene gaming. | Previene farming de rating vía mutual afk |
| **Relajación de MMR en queue (0–45s)** | Matching rápido > Balanced siempre. Inicio play immediato, relaj gradual si espera crece. | Onboarding rápido, evita queue infinita |
| **Recompensa Ranked < Quickmatch** (3×3 10/4/0 vs Quickmatch 12.5/5.0 base) | Ranked = estatus, no moneda. Si pagara más mata Quickmatch. | Preserva engagement Quickmatch |
| **Verificación ticket bloqueante (v0.4.2)** | 401 original NO era "sin permisos", era bug param order. Bloqueante = crash early si ticket invalido. | M5 anti-cheat: no match sin validación |
| **Particiones índice por fecha UTC** | Evita crecimiento unbounded. Sweep 3-day lookback prune old partitions independently. | Cloud Save sostenible |
| **Decay con gracia 7 días (−25/día si >1200)** | Protege top players hoarding sin remover inactivos permanente. Gracia = tolerancia de ausencias normales. | Rank dinámico, incentivo activity |

### Pendiente *(foto al cerrar M5 — varios ya se cerraron; el estado vivo está arriba)*

**Validación en vivo nunca ejecutada:**
1. ~~Los 75 tests EditMode nunca corrieron dentro del Editor~~ — cerrado el 2026-08-08.
2. **Quickmatch end-to-end:** sin probar desde que el fix de ticket pasó a bloqueante (v0.4.2). Si hay matiz en firma, cuelga match creation. **Sigue abierto.**
3. **Ranked end-to-end:** serie 3×3 encadenada (game 1 → auto-create game 2 → MMR actualiza), Leaderboard moviendo, decay aplicándose, temporada rollover. **Sigue abierto.**

**Acciones Dashboard/deploy:**
1. Registrar 5 eventos Analytics en Event Manager: ranked_mmr_changed, ranked_placement_completed, ranked_match_abandoned, ranked_queue_fallback_shown, ranked_season_reward_granted. **Sigue abierto** (ya no bloquea la beta, ver *Estado actual*).
2. ~~Verificar UpdateType en los `.lb` = "Latest score"~~ — **verificado 2026-08-08**: `Latest score` + `Highest to lowest` en ambos.
3. ~~**CRÍTICO:** seasonStartUnixSeconds debe coincidir con ResetConfig.Start~~ — **cerrado 2026-08-08**, y ahora lo verifica `SeasonConfigAlignmentTests`.
4. Deploy Triggers via UGS CLI: `ugs deploy` (Window → Deployment no soporta 1.7.2). **Sigue abierto**, necesita `ugs login`.
5. ~~Verificar el ID real de los leaderboards~~ — **verificado 2026-08-08: son ASCII, falsa alarma.** La ficha del Dashboard *renderiza* `ranked_3×3`, pero es solo la fuente: el texto real del DOM y la ruta de la URL dicen `ranked_3x3` y `ranked_6x6`, que es lo que usa `RankedLeaderboardStore`. Un screenshot no sirve para decidir esto — hay que leer el DOM o la URL.

**Deuda técnica mayor:**
- ~~`TTTXO.Game` sin tests~~ — cubierto el 2026-08-08. Lo que **sigue** sin cobertura son los 12 controladores de pantalla y los servicios con red de verdad (`PlayerDataService`, `MatchmakingService`, `OnlineMatchService`); esos necesitan un doble del SDK de UGS, que es otro orden de trabajo
- Sin push nativo FCM/APNs
- Tope global 300/día no unifica online+offline (offline audit-only)
- Ventanas de carrera Cloud Save sin transacciones (low probability, riesgo aceptado)
- Font asset cobertura tr/vi/pl sin verificación QA
- Vinculación Apple/Google sin UI (solo arquitectura server-side)
- 3 claves localización huérfanas (audit pendiente)
- `CHANGELOG-dev.md` arrastra el backfill de las decenas de commits entre v0.5.0 y hoy

**Sin empezar:**
- Tienda/IAP (solo stubs SKU)
- Audio (sliders persisten, sin sonido)

### Manual del dueño (Editor/Dashboard)

**Deploy crítico:**
1. ~~`Window → Deployment`~~ — **hecho 2026-08-08 21:07 (GMT-5) al environment `development`** (`3d36adea-d85f-42ad-80dc-6c2a2ed67e8b`, el mismo que usa el Editor según `ProjectSettings/Packages/com.unity.services.core/Settings.json`). Los 7 items en verde y **verificado contra el backend**, no solo contra la ventana: `RANKED_CONFIG` publicado con `placementMatches: 5`, `seasonStartUnixSeconds: 1787875200`, `seasonDurationMonths: 1` y sin `seasonDurationDays`; módulo `TicTacToeModule` con *Last uploaded* 2026-08-08 21:07; los dos leaderboards intactos (reset día 28, `Tiers: None`, *Last reset* y *Archived versions* sin cambios — el deploy de config **no** toca los puntajes).
2. UGS CLI: `ugs deploy` para Assets/Triggers/ (SweepAbandonedMatches.tr + SweepAbandonedMatchesSchedule.sched). **Pide `ugs login` primero**: el CLI está instalado (1.9.0) pero sin autenticar (401 al listar leaderboards).
3. Dashboard Event Manager: registrar 5 eventos Ranked (ver **Acciones Dashboard** arriba)
4. Dashboard Leaderboards config: ranked_3x3 y ranked_6x6 con UpdateType=Latest score, Tiers=None y reset día 28 desde 2026-08-28T00:00:00Z (hecho 2026-08-08; el deploy de los `.lb` reproduce lo mismo)
5. ~~Dashboard Remote Config: actualizar seasonStartUnixSeconds al launch real~~ — hecho: 1787875200 (2026-08-28T00:00:00Z) con `seasonDurationMonths: 1`

**Analytics local de beta:**
- `TTTXO_LOCAL_ANALYTICS` está **definido hoy en el target Android** (el activo). Con eso los eventos van a `Application.persistentDataPath/analytics/*.jsonl` y **no** a UGS.
- **El Editor compila con los defines del build target activo**, no con los de Standalone. Ponerlo solo en Standalone no hace nada y el sink parece roto — pasó al implementarlo.
- Para producción: sacarlo de `Player Settings → Scripting Define Symbols`. Si te olvidás, `LocalAnalyticsBuildGuard` aborta el build de release.
- El símbolo **sí viaja en el repo**: `ProjectSettings/ProjectSettings.asset` está versionado y el define quedó commiteado para `Android` y `Standalone`. (Una nota vieja de este archivo afirmaba que el asset estaba en `.gitignore` — era falsa; `git check-ignore` no devuelve nada para él.)
- **Cuidado con iOS**: `scriptingDefineSymbols` solo tiene entradas para `Android` y `Standalone`. Si alguien cambia el build target a iOS, el símbolo no está definido y los eventos vuelven a irse a UGS sin aviso.
- Unity **no escribe `ProjectSettings.asset` al instante**: bufferea los cambios de Project Settings y los baja a disco al guardar el proyecto. Si tocás defines por script y `git status` sale limpio, falta un `File → Save Project`.

**QA setup:**
- Correr 75 tests EditMode: `Window → General → Test Runner` (valida TTTXO.Core.Tests)
- Quickmatch: crear ticket, esperar match, jugar 1–2 games, verificar Wire push + abandonment detection
- Ranked: jugar serie 3×3 (game 1 complet → check game 2 auto-create), verificar MMR card delta en resultado, Leaderboard con self entry

**Handshake versión crítico:**
- Mismatch GameProtocol.Version entre cliente/servidor → warning (non-fatal) pero puede confundir QA
- Actualizar CloudCode~/TicTacToeModule/GameProtocol.cs en sync con Assets/Scripts/Game/Services/GameProtocol.cs

**Nota anti-cheat:**
- Toda regla de juego (validación movimientos, victoria, IA) en Cloud Code
- Toda recompensa server-side antes de issuance
- IAP validado ValidatePurchase server-side
- Leaderboard solo Cloud Code
- Nada de Remote Config se lee directo client (llega via GetGameConfig)

## 2026-08-08 (tarde) — Cosméticos en la UI, arte de tableros, ruteo del MCP

Sesión cerrada con el árbol limpio. **67 commits locales sin pushear** (sigue sin credenciales de GitHub). Los 8 de esta sesión, del más viejo al más nuevo:

| Commit | Qué |
|---|---|
| `bdad892` | retrato equipado en Home y en la barra de turno |
| `1527fbb` | títulos en el panel de equipamiento + ícono del rival (robot para la IA) |
| `bc58fe3` | orden de los modificadores de tamaño en USS |
| `eb40f0e` | link de tienda con string propio + 12 assets de localización regenerados |
| `b6d058d` | ERR-KB-003 |
| `83cc2c9` | `board_wood` regenerado sin vetas de neón |
| `b675667` | `BackdropFlattener` + los cuatro tableros aplanados |
| `6461632` | tablero dimensionado al espacio disponible |

### Decisiones que conviene no re-litigar

- **El viñeteado y el encuadre se imponen en post-proceso, no se piden en el prompt.** Reforzar la instrucción de iluminación en `BoardSurfaceRule` **empeoró** los resultados (viñeteado subió en 3 de 4, neon perdió su textura). Está revertido, con el experimento anotado en un comentario. Ver `Docs/08-Herramienta-Arte-Placeholder.md`.
- **`board_neon` y `board_space` conservan su arte anterior.** Se regeneraron y volvieron peor; se les aplicó solo el aplanado. Un lote regenerado no es automáticamente mejor que el que reemplaza.
- **`CosmeticSelection` es preferencia, no derecho.** Guarda en PlayerPrefs qué tiene equipado el jugador, no qué posee. Cuando exista la tienda, la propiedad la decide el servidor.
- **El rival humano usa una silueta neutra a propósito.** En 2P local el segundo jugador no tiene perfil y online el servidor no manda los cosméticos del oponente. `ApplyRivalAvatar` es el único lugar que cambia si algún día los manda.

### Pendientes

**Bloqueante de tooling — el MCP de Unity puede responder por otro proyecto.** Ficha completa en `ERRORES_Conocidos.md` (ERR-KB-003). Lo que hay que hacer al volver: cerrar la app de Claude Code **entera** (todas las conversaciones, no solo la sesión) y verificar que el `relay_win.exe --mcp` vivo lleve `--project-path` en su línea de comandos. Hasta entonces, todo comando que escriba algo abre con la guarda de `Application.dataPath`.

**UI**
1. ~~**Verificar el tablero anclado arriba** (`433af2a`)~~ — **hecho el 2026-08-08 (noche): los cuatro números dieron exactos.** Ver la sesión de esa fecha más abajo. Se apartó a propósito del wireframe, que pide "tablero central cuadrado" — decisión del dueño tras verlo centrado.
2. Arte sin usar: 6 reacciones (falta el sistema de reacciones in-game) y 9 iconos/packs de tienda (falta la pantalla de Store real).

**Estado del Editor al cerrar**
- TTTXO fuera de play mode, árbol limpio, `MainPanelSettings.asset` con `targetTexture=null`.
- **TCGMaster quedó en play mode** y no fue esta sesión — conviene mirarlo.
- Ojo con `ProjectSettings.asset`: `runInBackground` se activa al capturar pantallas con Unity en segundo plano y **no debe commitearse**. Ya se revirtió una vez.

**De antes, sin tocar esta sesión**: los 5 pendientes urgentes del *Estado actual* (tests EditMode en vivo, validación end-to-end, 5 eventos en el Dashboard, timestamps de temporada, deploy de Triggers), más tienda/IAP, audio y tests de `TTTXO.Game`.

## 2026-08-08 (noche) — Verificación del tablero anclado y cierre del ruteo del MCP

Sesión de verificación, **sin cambios de código**. Lo único que toca el repo son `Packages/manifest.json` + `packages-lock.json` (paquete nuevo) y `ProjectSettings.asset` (`scriptingDefineSymbols: Android`, efecto colateral del cambio de plataforma). Sin commitear al cerrar.

### El tablero anclado quedó verificado — los cuatro números dieron exactos

El pendiente #1 de la sesión anterior era confirmar `433af2a` en play mode, porque se había escrito con el Editor cerrado y los números eran aritmética. Medidos a 720×1400 (Android):

| Claim del commit | Medido |
|---|---|
| tablero 640px | 640×640 |
| celdas 205px | 205×205 |
| borde superior 40px bajo la barra de turno | 136 → 176 = 40 (16 margen + 24 padding) |
| ~568px de aire abajo | 568 |

**La aritmética era correcta.** Pero eso *no* probaba el cambio del controlador, y conviene entender por qué: a 720×1400 el limitante es el ancho (`availW=640` por el `max-width`, contra `availH=1208`), así que la resta del padding que agrega el commit no se ejecuta. La rama recién se ejercitó a **1280×400**, donde manda el alto:

- con el fix: alto disponible `232 − 24 = 208` → tablero 208×208, termina justo en el borde del contenedor;
- sin el fix habría medido 232 → **24px fuera del contenedor y 8px fuera de la pantalla**.

Los 40px bajo la barra de turno se mantuvieron en las tres resoluciones probadas (272×484, 720×1400, 1280×400), que es lo que se espera de un anclaje.

### Ruteo del MCP: causa confirmada y config corregida

ERR-KB-003 se reprodujo entero en vivo, y con eso se cayó una recomendación de la ficha original. Detalle completo en `ERRORES_Conocidos.md`; lo que importa acá:

- El disparador es **entrar** a play mode (antes se creía que era salir), y la ventana de riesgo es de **una sola llamada**.
- **No se recupera solo**: hubo que cerrar el Editor de TCGMaster.
- La config por proyecto estaba bien y **no se estaba usando**: los tres `relay_win.exe --mcp` vivos iban sin flags, o sea la entrada de scope user. La ficha vieja decía que esa entrada podía quedar "como fallback" — esa recomendación era el bug.
- **Fix aplicado**: `claude mcp remove unity-mcp -s user`, backup en `~/.claude.json.bak-mcp-routing`. Queda `unity-mcp` solo por proyecto.

**Pendiente y bloqueante**: cerrar la app de Claude Code **entera** y confirmar que el `relay_win.exe --mcp` vivo lleve `--project-path`. Hasta ese chequeo esto es config correcta, no fix verificado — la misma distinción que hubo que hacer con el tablero al empezar la sesión.

### Herramienta nueva

`com.unity.device-simulator.devices` 1.0.1 instalado. La *vista* Simulator ya venía en el Editor; el paquete agrega el catálogo de dispositivos reales. Sin usar todavía: sirve para el chequeo de safe area, que importa porque el aire de abajo del tablero es donde el wireframe 6 quiere la fila de reacciones.

### Trampas de medición por MCP — ficha nueva ERR-KB-004

Cuatro formas de obtener números falsos midiendo UI por MCP, todas encontradas hoy: el Editor de fondo no tickea (layout en NaN), las pantallas ocultas miden 0, `Screen.width` queda rancio, y **el UIDocument mide igual en edit mode** devolviendo números plausibles de la pantalla equivocada. Más una quinta de flujo: el splash pisa cualquier navegación por código, porque `Show(ScreenId.Home)` llega recién después del `await` de la init de UGS. El preámbulo obligatorio está en la ficha.

### Pendientes

- **Verificar el ruteo del MCP tras el reinicio completo** (arriba). Es lo primero al volver.
- **Sacar el Editor de play mode** si quedó adentro; hoy estaba en Play, Android, Game view 1280×400.
- `CHANGELOG-dev.md` está parado en v0.5.0 (2026-07-23) y arrastra decenas de commits sin registrar. Se agregó `[Unreleased]` solo con lo de esta sesión; el backfill sigue pendiente.
- Los de la sesión anterior que no se tocaron: arte sin usar (6 reacciones, 9 iconos de tienda), los 5 urgentes del *Estado actual*, tienda/IAP, audio y tests de `TTTXO.Game`.

## 2026-08-08 (noche, tras el reinicio) — Ruteo del MCP verificado y los tests EditMode corridos en vivo

Sesión corta, **sin cambios de código**. Cerró los dos pendientes que estaban primeros en la fila.

### El ruteo del MCP quedó verificado a nivel config

Los dos chequeos que ERR-KB-003 dejaba pendientes dieron bien tras cerrar la app entera de Claude Code:

| Chequeo | Antes | Ahora |
|---|---|---|
| `relay_win.exe --mcp` vivos | 3, todos sin flags | 1, con `--project-path D:/UnityProjects/TTTXO --name TTTXO` |
| `Application.dataPath` | saltaba a TCGMaster | `D:/UnityProjects/TTTXO/Assets` |

**Con el matiz de que esto no prueba el caso del bug.** Había un solo Editor abierto (los otros cuatro `Unity.exe` eran AssetImportWorkers de TTTXO, y un único descriptor en `~/.unity/mcp/connections/`), y con un Editor solo el ruteo es determinista por construcción. Lo que sí quedó cerrado es la parte que fallaba —que el proceso naciera sin `--project-path`— y que el flag discrimina ya estaba probado por separado. **El chequeo de contención sigue pendiente**: abrir el Editor de TCGMaster, entrar a play mode y pedir `dataPath` en la llamada siguiente.

### Los tests EditMode pasan dentro del Editor *(decía 76; **son 75**, corregido el 2026-08-08)*

Era el pendiente urgente #1 desde que existe el proyecto: los tests habían corrido en project standalone pero **nunca en el Editor real**. Resultado:

```
started count=76
finished status=Passed passed=76 failed=0 skipped=0 inconclusive=0 duration=3.5223811
```

Con la guarda de `dataPath` en el mismo comando, así que el resultado está atado al proyecto correcto. **Son 76, no 75** — la doc venía arrastrando el número viejo.

Correrlos por MCP tiene una trampa propia, ficha nueva **ERR-KB-005**: `TestRunnerApi.Execute()` es asíncrono y el domain reload del run se lleva los callbacks *y* el buffer de logs del bridge, así que el primer intento devolvió `executed successfully` con console vacía. La solución es `settings.runSynchronously = true`. De paso: el `CommandScript` del MCP **no admite clases anidadas** (el preprocesador las hoistea y rompe con `CS1527`).

### Corrección a ERR-KB-004: `runInBackground` sí persiste

La ficha afirmaba, como verificado, que `Application.runInBackground = true` era runtime-only. Es falso: el working tree apareció con `runInBackground: 0 → 1`. La verificación original no estaba equivocada, estaba **prematura** — Unity serializa `ProjectSettings.asset` más tarde (al guardar o cerrar), así que leer el archivo justo después siempre confirma lo que uno quiere oír. Segunda vez que este flag se cuela al working tree.

### `TTTXO.Game` dejó de estar sin tests — 97 nuevos, 173 en total

Era la deuda técnica que la propia doc marcaba como "la más crítica ante IAP real". Assembly nuevo `Assets/Scripts/Tests/Game/` (`TTTXO.Game.Tests`, EditMode), corriendo junto a los 76 de Core: **173 pasan, 0 fallan, 3.8s**.

Qué se cubrió, y por qué esas piezas:

| Archivo | Qué fija |
|---|---|
| `GameManagerTests` (38) | El cap diario de 300 SC, que `ApplyOnlineRewardCredit` **no** consuma ese cap ni estampe el timestamp local —los dos invariantes con razón de seguridad escrita en el código—, la alternancia de primer jugador y su clave de secuencia, el historial local (orden, tope 50, dificultad N/A en 2P) y el borrado de datos |
| `CosmeticSelectionTests` (14) | Que sea preferencia y no derecho: defaults, fallback ante id vacío, `Changed` que no se dispara si nada cambió, snapshot de nube que no blanquea slots ausentes, y que cada default exista en el catálogo |
| `CosmeticCatalogTests` (32) | Ids únicos por set y que un id desconocido —un save de un build más nuevo— siempre resuelva a algo pintable. Seis chequeos aplicados a los cinco sets |
| `GameAnalyticsCodeTests` (7) | Que todo valor de enum mapee a snake_case. Los mappers terminan en `_ => ToString()`, así que agregar una dificultad sin tocar `GameAnalytics` emitía PascalCase y el pipeline lo descartaba en silencio |
| `ContractMirrorTests` (2) | `GameProtocol.Version` y el mode code de Ranked contra el **fuente del módulo de Cloud Code**, leído como texto. El "bumpear los dos lados juntos" era una convención que no verificaba nadie |
| `RankedProfileCacheTests` (4) | Que un tablero sin dato devuelva el fallback del llamador y que los tamaños no se pisen |

**`PlayerPrefsSandbox` no es opcional.** Los tests EditMode corren contra los PlayerPrefs reales del Editor —no hay sandbox por test— así que sin capturar y restaurar, una corrida te pisaba el saldo local, los cosméticos equipados y los sliders de volumen. El helper lista las claves explícitamente porque PlayerPrefs no se puede enumerar; una clave nueva en el código de Game hay que agregarla ahí o queda desprotegida.

**Con un costo asumido**: el test del borrado de datos llama al `DeleteAllPlayerData` de producción, que hace `PlayerPrefs.DeleteAll()`. El sandbox devuelve las claves de TTTXO, pero las propias de Unity para el proyecto son daño colateral y se regeneran solas. Se prefirió eso a testear la ruta de privacidad contra un doble.

De yapa, la corrida confirmó en vivo la regla de que el wrapper de Analytics nunca rompe el juego: sin sesión UGS, los `SoftCurrencyEarned` degradaron a `Warning` y ningún test se cayó.

### El back stack, extraído y cubierto — 16 tests más, 189 en total

El hueco que la sección anterior dejaba declarado se cerró en el mismo tramo. `ScreenRouter` construye las 12 pantallas dentro de `Initialize()` contra un `UIDocument` vivo, así que el back stack —la lógica que de verdad importa ahí— no se podía tocar desde un test. Se extrajo a `Assets/Scripts/Game/UI/ScreenHistory.cs`, sin dependencias de UnityEngine.

**Es una extracción, no un rediseño.** Las cuatro reglas son las mismas y están en el mismo orden: avanzar apila la pantalla que se deja; navegar a la que ya se está viendo no apila; volver desapila en vez de apilar; y llegar a Home limpia todo. Lo único que se movió es dónde viven. Se respetó incluso el detalle de que el push estaba dentro del bloque que oculta la pantalla actual y el clear afuera: por eso `RecordNavigation` recibe un `ScreenId?` que es `null` cuando no hay pantalla que dejar.

Los tests valen por los escenarios, no por los primitivos: que Profile → Store → back no haga ping-pong (el bug que el comentario original describía y que un campo de un solo nivel produce), que una sub-pantalla vuelva a donde se la abrió y no a un padre hardcodeado, que diez ciclos de partida no acumulen historial, y que el Splash no quede alcanzable por un back —sería una pantalla sin salida—.

**La costura tiene un límite honesto**: los tests corren contra `ScreenHistory` más un navegador de mentira que replica la secuencia de llamadas del router. Si esa secuencia cambia y el doble no, los tests siguen verdes mientras la app se rompe. Está dicho en el propio archivo de tests.

### Los dos servicios de lectura, cubiertos — 22 tests más, 211 en total

**El fallback offline de `GameConfigService` no necesitaba costura.** `UgsInitializer.Status` arranca en `NotStarted`, así que en EditMode `InitializeAsync()` toma la rama del fallback sin tocar la red. Esa rama es la garantía de "el juego funciona 100% sin sesión" y es la que le toca a cualquiera que juegue en un avión; nunca la había verificado nadie. Cubre: los cuatro tableros con su `winLength`, que Ranked se ofrezca **solo** en 3x3 y 6x6, que los parámetros de IA salgan de `TTTXO.Core` y no de números copiados, y que Adaptive quede afuera a propósito.

**Test de espejo nuevo, y pasó**: el fallback se compara contra el `BOARD_CONFIGS` de `Assets/RemoteConfig/GameConfig.rc`. El código traía el comentario de que lo espejaba y nadie lo verificaba; si derivan, un jugador offline ve una lista de modos distinta a la de uno online y no hay señal en ningún lado.

**La costura fue mínima y no agrega estáticos mutables ni fakes de red.** Se partió `InitializeAsync()` en fetch y aplicación: `ApplyServerResponse(response)` es `internal` y se puede llamar con una respuesta armada a mano. Eso abre justo las formas que una corrida contra el servidor real nunca produce — respuesta nula, incompleta, con secciones opcionales ausentes, con versión distinta. Dos hallazgos que ahora quedan fijados: una respuesta rechazada **no** aplica a medias (tira antes de tocar nada), y una respuesta sin `MatchmakingConfig`/`RankedConfig` deja los defaults en vez de `null`, que es lo que evita un NRE en cada call site.

`RankedQueryService` es un wrapper fino a propósito —no atrapa excepciones porque cada llamador degrada distinto— así que hay poco que testear y el archivo de tests lo dice en vez de rellenar. Lo que sí vale: que `limit` se **omita** en vez de mandarse en cero o nulo cuando el llamador no opina, para que el servidor aplique su default; y el `BoardFor` de la respuesta, cuyo propio doc avisa que puede no encontrar nada.

`Assets/Scripts/Game/AssemblyInfo.cs` es nuevo: `InternalsVisibleTo("TTTXO.Game.Tests")`. Se prefirió eso a hacer públicos los seams, que invitaría a call sites que deberían pasar por `InitializeAsync`/`GetLeaderboardAsync`.

**Lo que sigue sin cobertura**: los 12 controladores de pantalla, y de los servicios lo que tiene red de verdad de por medio (`PlayerDataService`, `MatchmakingService`, `OnlineMatchService`) — esos sí necesitarían un doble del SDK, que es bastante más caro que estas dos extracciones.

### Cierre de sesión

Los nueve commits se mergearon a `main` con fast-forward (`a87bd60 → b5e016d`), sin commit de merge, y la rama `test/game-layer-coverage` se borró. **La suite se volvió a correr después del merge, no antes**: el `checkout main` devolvió los archivos a `a87bd60` y el fast-forward los trajo de vuelta, así que Unity reimportó dos veces y lo probado era el contenido de la rama, no el estado del Editor tras ese vaivén. Sobre `main`: 211 pasan.

`runInBackground` quedó revertido a `0` y el árbol limpio. **La forma correcta de revertirlo es por el Editor**, no con `git checkout`: son la misma variable que `PlayerSettings.runInBackground`, así que revertir el archivo con el Editor abierto deja el valor vivo en `True` y Unity lo reescribe. Está en ERR-KB-004 con el comando.

**Decisión del dueño anotada**: el push a GitHub no es prioridad. Los commits locales se acumulan a propósito, no por olvido. *(Corrección 2026-08-08: sí hay remote configurado y el initial check-in está pusheado — la parte de "sin credenciales" que se repetía en estas entradas era falsa. Conteo vivo en Estado actual.)*

### Pendientes

- **El chequeo de contención del MCP** — abrir el Editor de TCGMaster, entrar a play mode y pedir `dataPath` en la llamada siguiente. Es lo único que falta para dar ERR-KB-003 por cerrado del todo.
- **La fecha de temporada de Ranked** (ver *Estado actual* #4): los valores concuerdan, pero el placeholder ya venció.
- **Lo que sigue sin cobertura de tests**: los 12 controladores de pantalla, y los servicios con red de verdad (`PlayerDataService`, `MatchmakingService`, `OnlineMatchService`). Esos necesitan un doble del SDK de UGS, que es otro orden de trabajo que partir un método en dos — no es "lo mismo pero más".
- `CHANGELOG-dev.md` recibió esta sesión en `[Unreleased]`, pero **el backfill de las decenas de commits entre v0.5.0 y hoy sigue pendiente**.
- Sin tocar: validación end-to-end Quickmatch/Ranked, los 5 eventos en el Dashboard, deploy de Triggers, arte sin usar (6 reacciones, 9 iconos de tienda), 3 claves de localización huérfanas, cobertura de glifos tr/vi/pl, tienda/IAP y audio.

## 2026-08-08 (noche, tercera sesión) — Temporadas mensuales, analytics local de beta, y tres datos falsos que arrastraba esta doc

Seis commits, dos temas cerrados de punta a punta y una limpieza de esta memoria.

### Las temporadas Ranked pasaron a meses calendario

El dueño configuró en el Dashboard el reset de `ranked_3x3`/`ranked_6x6` para el **día 28 de cada mes**. El repo todavía decía 8 semanas, así que **el próximo `Window → Deployment` lo habría revertido en silencio**: config-as-code le gana al Dashboard. Ese es el riesgo real que disparó todo el trabajo.

- `RankedSeasonCalculator` reescrito con aritmética de calendario (`AddMonths`). **Ningún número fijo de días expresa "el 28 de cada mes"** — los meses tienen 28 a 31, y cualquier duración se desfasa del reset del leaderboard dentro de la primera temporada. Las fronteras se miden siempre desde el ancla y nunca apilando meses, para que el clampeo de `AddMonths` (31 ene → 28 feb) no se acumule en desfase permanente.
- `seasonDurationDays: 56` → `seasonDurationMonths: 1`; `seasonStartUnixSeconds` dejó de ser placeholder: `1787875200` = `2026-08-28T00:00:00Z`.
- Los dos `.lb` con cron `"0 0 28 * *"`. El cron de 5 campos **es válido** en leaderboard assets — el ejemplo oficial de Unity usa `"0 12 1 * *"`.
- **`placementMatches: 10 → 5`.** Era la razón por la que el diseño había descartado temporadas de 4 semanas: un jugador casual (~13 partidas al mes) apenas terminaba colocación. Dos efectos anotados donde viven: la ventana de K=40 se parte al medio (6.1) y el farmeo de Bronce con cuentas descartables cuesta la mitad (6.6).
- **Los leaderboards no llevan `TieringConfig`, a propósito.** El Dashboard tuvo brevemente bandas en 100/250/450/700 sobre una escala donde el score publicado *es* el MMR crudo, con `initialMmr` 1000 y `mmrFloor` 500: todos diamante desde la primera partida, y bronze/silver/gold inalcanzables. `RANKED_CONFIG.tierMinMmr` es la única tabla.
- **`SeasonConfigAlignmentTests`** convierte en test la sincronización que el README de Leaderboards declaraba obligatoria y no verificaba nadie. Compara ancla, día del mes, hora y cadencia entre las tres superficies, y rechaza un ancla posterior al 28 —donde el clampeo de `AddMonths` y un día-del-mes de cron dejan de describir las mismas fechas.

Deployado a `development` y **verificado contra el backend, no contra la ventana**. Eso importó: la columna *Deployment Status* dejó `GameConfig.rc` y el módulo en `Up to date` en vez de `Deployed`, que se lee como "no hice nada" — pero ambos habían subido, según el log del Editor y las marcas de tiempo del Dashboard. El redeploy de los `.lb` no tocó los puntajes: *Last reset* y *Archived versions* quedaron igual.

### Analytics local para la beta

Los eventos custom de UGS se facturan. Con `TTTXO_LOCAL_ANALYTICS` definido, `GameAnalytics` escribe JSON Lines a `Application.persistentDataPath/analytics/` y **no llama a `AnalyticsService`**.

- **Símbolo de compilación y no flag de runtime**, porque "desenchufado para producción" tiene que significar que el código de escritura a disco no está en el build. `LocalAnalyticsBuildGuard` aborta cualquier build de release que lo tenga.
- **La serialización queda siempre compilada y siempre testeada**; solo el I/O va detrás del símbolo. Un JSON mal escapado es otra falla invisible: la beta sigue jugando y el archivo sigue creciendo hasta que alguien intenta parsear un mes.
- Los ~15 métodos tipados no se tocaron: la lambda recibe un `AnalyticsEvent` con el mismo `Add(...)`.
- `DeleteAllPlayerData` borra el directorio — los eventos en disco son datos del jugador.
- Efecto colateral útil: el camino local no tiene el bail-out de "sesión UGS no Ready", así que captura offline y en el Editor, donde hoy se descartaba todo.

### Tres datos que esta doc afirmaba y eran falsos

Los tres se habían citado como hechos sin que nadie los verificara. Es el motivo del encabezado nuevo en *Estado actual*.

| Afirmación | Realidad |
|---|---|
| "`TTTXO.Core.Tests` son 76, no 75" (una nota previa insistía explícitamente) | Son **75**. El Test Runner es la fuente |
| "`ProjectSettings.asset` + `TTTXO.slnx` excluidos propositivamente (.gitignore)" | **Los dos están versionados.** `git check-ignore` no devuelve nada para ninguno |
| "sin credenciales de GitHub", "22 commits en rama `docs/project-design-guidelines` sin pushear" | Hay remote (`Hellscythe25/TTTXO`), el initial check-in está pusheado, y esa rama está **mergeada entera** en main |

### Trampas nuevas, para no repetirlas

- **El Editor compila con los defines del build target activo** (acá Android), no con los de Standalone. Poner el símbolo solo en Standalone no hace nada y el sink parece roto. La primera verificación de "compila con el define" no probó nada; lo que lo probó fue la **desaparición** del warning de "UGS session is not Ready", que vive en la rama `#else`.
- **Unity bufferea los Project Settings** y solo escribe el asset al guardar el proyecto. Tocar defines por script deja `git status` limpio y parece que no pasó nada.
- **Un screenshot no sirve para decidir si un identificador tiene `x` o `×`.** El Dashboard renderiza `ranked_3×3` con signo de multiplicación, y parecía un break duro contra el `ranked_3x3` ASCII de `RankedLeaderboardStore`. Es solo la fuente: hay que leer el DOM o la URL.
- **`scriptingDefineSymbols` solo tiene entradas para Android y Standalone.** Cambiar el target a iOS deja el símbolo sin definir y manda los eventos de vuelta a UGS sin aviso.

### Cierre: el repo quedó pusheado

El push dejó de ser "no prioridad" al final de la sesión. Los 89 commits acumulados subieron a `origin/main` y la rama `docs/project-design-guidelines` se borró de los dos lados. Tres cosas que aparecieron recién al hacerlo:

- **El repo usa Git LFS** (131 objetos, ~123 MB). No estaba documentado y solo se nota al pushear o clonar.
- **La rama remota hubo que borrarla por la API** (`gh api -X DELETE .../git/refs/heads/...`): `git push --delete` falló con `Could not resolve host: github.com` mientras `gh` funcionaba. Fue transitorio, pero la API es la vía que funcionó.
- Borrar la rama en el servidor **deja una referencia local rancia**: `git branch -a` la sigue mostrando hasta un `git remote prune origin`.

También: `git branch -d` se niega a borrar una rama que está adelante de **su propia remota**, aunque su contenido esté entero en `main` — la comprobación es contra el tracking ref, no contra "¿esto está en algún lado?". Verificar con `git merge-base --is-ancestor` y recién ahí usar `-D`.

### Pendientes

Los de *Estado actual*, sin novedad: validación end-to-end (con el 28 de agosto como fecha dura), los 5 eventos en el Dashboard, el deploy de Triggers con `ugs login`, y la decisión de diseño sobre el ingreso por premios.

## 2026-08-08 (cierre) — La validación end-to-end de Ranked, y los tres bugs que llevaba dos milestones escondiendo

El pendiente urgente desde que existe Ranked. Se hizo con un arnés nuevo, `Tools/ranked-e2e.ps1`, que
habla la API REST de UGS directamente: dos jugadores anónimos, tickets reales contra `ranked-queue` y
las funciones de Cloud Code de verdad. **No** cubre el cliente.

### Por qué guionado y no dos builds

Las reglas de Ranked viven enteras en Cloud Code. Un test por UI no puede afirmar que el MMR quedó en
un número exacto, y reproducir una partida a mano pide dos dispositivos. El arnés afirma valores
concretos y se corre en un comando.

Piezas que costó descubrir y conviene no volver a averiguar:

| Qué | Dónde |
|---|---|
| Auth anónima | `POST player-auth.services.api.unity.com/v1/authentication/anonymous`, headers `ProjectId` + `UnityEnvironment`, body `{}` |
| Cloud Code módulo | `POST cloud-code.services.api.unity.com/v1/projects/{id}/modules/{mod}/{fn}`, body `{"params":{...}}`, respuesta bajo `output` |
| Matchmaker | `POST matchmaker.../v2/tickets`, poll `GET /v2/tickets/status?id=`. **La respuesta del poll es plana** (`status`/`matchId` al tope): el `Value`/`Type` del SDK es un envoltorio de cliente, no el payload |
| `handoffId` | es el `matchId` del assignment |

### Tres bugs, el mismo patrón

Los tres eran afirmaciones razonadas, escritas en el código **como si estuvieran verificadas**, que
nadie había ejecutado nunca. Los dos primeros significaban que **ninguna partida online se podía
crear, ni Quickmatch ni Ranked**, desde el milestone 4.

1. **`Forbidden` en la verificación de ticket.** `GetTicketStatusAsync` iba con `context.ServiceToken`
   e `impersonatedUserId = null`. Es una API con alcance de jugador: el mismo GET con el bearer del
   jugador devuelve 200. Un Service Token no tiene identidad propia, tiene que nombrar al jugador por
   el que actúa. En el cable viaja como header `impersonated-user-id`. El comentario del código
   **pedía textualmente probarlo en vivo antes de confiar**; nadie lo hizo en dos milestones.
2. **El `oneOf` del SDK no parsea su propia respuesta.** Arreglada la auth, apareció
   `ApiException(Deserialization)`. `TicketStatusResponse` es un `oneOf` sobre cinco assignments y el
   payload normal deserializa contra **tres** de ellos —`Custom` y `Multiplay` solo difieren por
   campos opcionales—, así que la rama nunca resuelve. Defecto del generador; `0.0.26` es la última
   no-alpha. La salida: `HttpApiClient.ToApiResponse<T>` asigna `RawContent` **antes** de deserializar
   y adjunta el `ApiResponse` a la excepción, así que el cuerpo sobrevive y se parsea a mano.
3. **El gate de colocación del leaderboard no existía.** `design-doc` 6.6 lo exige y **dos comentarios
   afirmaban que funcionaba**; nada filtraba la escritura, y un jugador con 1/5 ya estaba rankeado. El
   gate va en la escritura (`SettleCoreAsync`, el único punto que publica un score); filtrar en la
   lectura costaría un perfil por entrada del top en cada consulta.

### Lo que quedó confirmado contra el servidor real

```
roles invertidos por partida     P1 alterna X y O en las diez partidas
un update de MMR por serie       no por partida; la colocación avanza una vez por serie
Elo con K=40                     primera serie exactamente 40 * (1 - 0.5) = +20 / -20
anti-boosting                    ganancias 20, 18, 8, 7, 0 sobre el mismo rival
derrotas nunca amortiguadas      caídas 20, 18, 16, 14, 13, K completo siempre
elegibilidad                     1/5 -> fuera de la tabla; 5/5 -> dentro, con ownEntry
temporada                        seasonEndUnixSeconds = 2026-09-28T00:00:00Z
```

La curva `[1.0, 1.0, 0.5, 0.5, 0.0]` de la sección 6.3 corriendo tal cual está escrita, **con la
quinta victoria pagando cero**.

### Trampas de método

- **Una carrera con el deploy hace parecer que el fix no funciona.** La primera corrida tras deployar
  el gate lo reportó inefectivo: el deploy terminó a las 22:45:55 con el script ya en vuelo, así que
  la serie 1 se liquidó contra el módulo viejo. Se persiguió un rato culpando al código. Lo zanjó
  **decompilar el binario que Unity realmente subió**. Dar unos segundos entre deploy y corrida.
- **Unity compila el módulo a `bin/Release/net9.0/linux-x64/`**, no al `bin/Release/net9.0/` que uno
  esperaría — ese sigue congelado en julio y mirarlo lleva a conclusiones falsas.
- **`ilspycmd`** (`dotnet tool install -g ilspycmd`) fue lo que resolvió los bugs 2 y 3: leer el IL del
  SDK en vez de razonar sobre su firma. Reflexión y docs prueban que algo compila; solo el IL o una
  llamada en vivo prueban qué hace.
- El arnés crea jugadores y entradas de leaderboard reales. Los de estas corridas quedaron en la tabla
  de `development`, algunos con 1/5 de antes del gate. Si molesta, resetear desde el Dashboard.

### Pendientes

Los de *Estado actual*. De validación queda la pasada manual con dos clientes, Quickmatch sin
ejercitar directamente, y decay/rollover que dependen del tiempo.

## 2026-08-09 — Quickmatch validado end-to-end, y la clave que el jugador puede escribir

Cerrado el punto (b) del pendiente de validación: Quickmatch nunca se había ejercitado directamente.
Arnés nuevo, `Tools/quickmatch-e2e.ps1`, misma técnica que el de Ranked — REST de UGS directo, dos
jugadores anónimos, tickets reales de `quickmatch-queue`, funciones de Cloud Code de verdad.

**Pasó todo en la primera corrida**, sin encontrar un solo bug de comportamiento. Vale anotar por qué,
porque contrasta fuerte con la sesión anterior: los tres bugs que encontró el arnés de Ranked estaban en
el camino compartido (`CreateOnlineMatchAsync` / `VerifyTicketResolvesToHandoffAsync`), así que arreglarlos
ya había arreglado Quickmatch. La entrada del 2026-08-08 lo daba por "muy probable, no verificado".
**Ahora está verificado**, y la sospecha era correcta.

### La forma del arnés

Cinco rondas puntuadas contra el mismo par: victoria, empate, victoria, victoria, victoria. La secuencia
está elegida para que la última **tenga que pagar cero** — la sección 4 del design-doc topea en 3 las
victorias pagadas por rival cada 24 h, y el empate no debe consumir un ordinal. Si el empate contara, la
cuarta victoria pagaría y la quinta no: el orden es el assert.

```
handoff              los dos tickets resuelven al mismo matchId de Matchmaker
join en dos fases    el 1º queda esperando, el 2º completa el roster, un solo matchId
idempotencia         un CreateMatch reintentado devuelve la misma partida
asignación X/O       playerId lexicográficamente menor, estable entre partidas
validación de jugada rechaza fuera de turno, casilla ocupada y a un tercero ajeno
tabla de premios     12 / 5 / 0  — floor(10 * 1,25) victoria, base empate, nada derrota
proyección           el DTO de cada jugador trae solo su propio premio
re-poll              volver a pedir GetMatchState nunca re-acredita
tope por rival       la 4ª victoria sobre el mismo rival paga 0
billetera            41 y 5 en Cloud Save, la suma exacta de los diez premios
```

Un detalle estructural que el arnés dejó a la vista y no es un bug pero conviene tener presente: en
Quickmatch **los roles no rotan**. X va al playerId lexicográficamente menor, así que contra el mismo
rival el mismo jugador abre siempre, para siempre. Ranked neutraliza la ventaja de primera jugada con la
serie de 2 y la compensación `F`; Quickmatch no compensa nada. Para un modo casual probablemente esté
bien — pero es una decisión que nadie tomó explícitamente, solo salió del orden lexicográfico.

### El hallazgo: el premio online cae en una clave que el jugador puede escribir

Ficha **ERR-KB-006**, abierta y sin fix. La última sonda del arnés hace lo que ningún test del Editor
puede hacer: pegarle a la API REST de Cloud Save con el token del jugador.

```text
--- probe: can a player overwrite their own wallet? ---
  ACCEPTED - player wrote balance=999999 directly with their own token
```

`OnlineRewardStore` acredita `currency` con `SetItemAsync` — Player Data en clase de acceso **`Default`**,
escribible por el propio jugador. Confirmado en vivo que se generaliza a las tres claves escritas con ese
patrón: `currency`, `onlineCurrencyLedger` y `rankedProfile`, las tres aceptan escritura directa.
`MatchStateStore` **no** está afectado — usa Custom Data, otra superficie.

El punto incómodo: el cliente **ya escribe `currency` a propósito**
(`PlayerDataService.SaveAfterMatchAsync`), porque la economía de 1P/local es local-first con PlayerPrefs
como fuente de verdad. El defecto no es que el cliente escriba; es que el premio online —calculado
server-side, con topes diarios y ledger anti-colusión— aterriza **en esa misma clave**. Todo lo que el
arnés acababa de validar es, mientras eso siga así, decorativo: un POST lo saltea entero.

`rankedProfile` es la más filosa de las tres: ese MMR es el que `CompleteOnlineMatchAsync` snapshotea
como entrada del Elo y el que termina en el leaderboard de un modo ya deployado.

**Se cortó a propósito antes de completar la cadena.** Está confirmado que las escrituras se aceptan;
**no** está probado que un `rankedProfile` forjado termine en una entrada del leaderboard. El defecto es
que la escritura se acepte, y terminar la cadena ensuciaba la tabla de `development` sin cambiar el
diagnóstico ni el arreglo.

No se arregló nada: la tensión es real —la economía offline necesita que el cliente escriba, la online
necesita que no pueda— y cómo se resuelve es decisión de diseño. La sonda quedó **imprimiendo, no
rompiendo**, para no dejar el arnés en rojo por una pregunta que no le toca contestar.

### Decisiones de método

- **El plumbing compartido se extrajo a `Tools/ugs-e2e-common.ps1`** en vez de copiarse. Auth anónima,
  el sobre de Cloud Code, tickets, polling y el driver de partida guionada son mode-agnósticos. La razón
  concreta: el manejo de error de Cloud Code lee el body a mano por una rareza de PowerShell 5.1 que costó
  una sesión encontrar, y una segunda copia se lleva el comentario que lo explica.
- **`[string]::CompareOrdinal`, no `Sort-Object`**, para verificar la asignación X/O: el servidor ordena
  con `StringComparer.Ordinal` y la comparación por defecto de PowerShell es culture-aware, que ordena
  distinto los mixed case. El assert habría sido decorativo justo en los pares donde importa.
- **Las sondas de validación de jugada corren en un par descartable** y después se re-matchea, porque la
  sonda de casilla ocupada consume una celda y todas las líneas guionadas asumen tablero vacío.
- **El mensaje final de `ranked-e2e.ps1` estaba mal desde `4c41a0b`**: decía "both players placed, ranked,
  and on the leaderboard" en cualquier corrida. Desde que los asserts toleran `-Series` corto, mentía
  exactamente en las corridas que ese cambio habilitó. Corregido.

### Pendientes

Los de *Estado actual*. De validación ya solo queda la **pasada manual con dos clientes** (Wire push, UI
de la serie, pantalla de matchmaking) y **decay/rollover**, que dependen del tiempo. Se suma la decisión
de diseño de ERR-KB-006, que **no bloquea la beta pero sí la tienda**: nada gastable depende hoy del
saldo, y eso deja de ser cierto en cuanto exista el catálogo.

## 2026-08-09 (tarde) — Encuadre en dispositivos, recorte de tableros y bloqueo de portrait

Primera sesión que usa el Device Simulator (el paquete se había instalado el 2026-08-08 sin estrenarse). Seis dispositivos medidos, **todos simulados y todos en portrait**. Tres arreglos de usabilidad, safe area implementada, recorte de tableros diferido, guarda server-side y portrait fijo. Todo el detalle técnico vive en `Docs/09-Encuadre-Dispositivos.md` (T-01 a T-09, L-01 a L-08); acá van solo estado, decisiones y pendientes.

### Hecho

**Device Simulator: seis dispositivos medidos**

Con el panel en `ScaleWithScreenSize` (ver abajo) el ancho del panel es siempre 720 unidades, así que **la única variable es el alto**:

| Dispositivo | Resolución | Insets arriba/abajo | Panel |
|---|---|---|---|
| iPhone 11 | 828 × 1792 | 88 / 68 px | 720 × 1558 |
| iPhone 13 Pro Max | 1284 × 2778 | 141 / 102 px | 720 × 1558 |
| iPad Pro 12.9" | 2048 × 2732 | 0 / 40 px | 720 × 960 |
| Galaxy J7 (2017) | 1080 × 1920 | 0 / 0 | 720 × 1280 |
| Redmi 6 Pro | 1080 × 2280 | 89 / 0 px | 720 × 1520 |
| Nvidia Shield Tablet | 1200 × 1920 | 0 / 0 | 720 × 1152 |

El **iPad es el caso que aprieta** (960 de alto) y el J7 el segundo (1280); los dos por debajo de las 1400 de referencia contra las que está escrito el USS. Y hay **casos espejo de safe area**: el iPad tiene 0 arriba y 40 abajo, el Redmi 89 arriba y 0 abajo — por eso el controlador calcula los cuatro bordes por separado.

**Tres arreglos de usabilidad pedidos por el dueño (commit `b1b3577`)**

- **Escala (ScaleWithScreenSize):** `MainPanelSettings` pasó de `ConstantPhysicalSize` a `ScaleWithScreenSize` con referencia 720×1400 y `match=0`. Efecto: el panel mide **siempre 720 unidades de ancho**, así que a mayor resolución la UI no se empequeñece (antes ocupaba 11.3% en 1284dp contra 20% en 720).
- **Centrado:** menús y tablero centrados en vertical, no anclados arriba. En board select con `min-height: 100%` en el container del ScrollView para que contenido corto centre y largo scroll.
- **Tamaños:** botón Jugar `min-width: 320`, título Home 46px, avatar header 64px, etiquetas nav 15px.

**Safe area implementada (`Assets/Scripts/Game/UI/SafeAreaController.cs`, nuevo)**

Ninguna pantalla leía `Screen.safeArea`. En iPhone 13 Pro Max la zona dinámica cortaba el nombre del jugador. Controlador nuevo que inset cada pantalla usando `--content-padding-x/y` (propiedades custom USS). Decisión: el inset va en cada pantalla para que los fondos desangra bajo el notch. **Deuda conocida:** las propiedades custom espejan el `padding` a mano y hay que mantenerlas en sincronía.

**Recorte de 9×9 y 11×11 — diferido a post-launch, decisión del dueño**

**Medida:** la celda del 11×11 dio **26.67dp en el Redmi 6 Pro**, contra los 48dp que Android recomienda como objetivo táctil mínimo. El techo es aritmético: en un teléfono de 360dp de ancho, once celdas dan 32.7dp **aunque el tablero ocupe el 100%**, así que no se arregla con layout. Por la misma cuenta el 9×9 tampoco llega y el 6×6 pasa raspando (medido: 57.5dp en el J7, que tiene 411dp de ancho).

El corte vive en **tres lugares que tienen que coincidir**: `BOARD_CONFIGS` de `Assets/RemoteConfig/GameConfig.rc`, `GameConfigService.ShippedBoardSizes` y el filtro de `BoardSelectScreenController`. **`TTTXO.Core.BoardConfig` sigue conociendo los cuatro tamaños**, a propósito: volver es config, no código.

Se rompió a medias en el primer intento: Board Select devolvía `BoardConfig.All` para 1P y 2P local, o sea que los tableros seguían apareciendo **justo donde se jugaban**, y los 231 tests quedaron verdes — porque el recorte estaba testeado en la config y en el fallback offline, y la pantalla ignoraba ambos. Detectado mirando las tarjetas renderizadas después de deployar, no por un test (commits `989ce0c`, `c4c1290`). El filtro se extrajo a un estático (`EligibleBoardSizes`) y `BoardSelectEligibilityTests` (6 casos) es la costura que faltaba.

**Guarda server-side (commit `f05afac`)**

`CreateMatch` valida ahora con `BoardAvailability.EnsureOfferedAsync()` **antes** de ramificar por modo. Validaba solo contra `BoardConfig.ForSize`, que es la tabla de reglas y conoce todos los tamaños, así que el recorte era client-side.

**El camino de Ranked era peor y es el hallazgo real:** no validaba el tamaño en absoluto. `mode="ranked"` con 11×11 caía en la rama con forma de 6×6 y habría creado una partida ranked en un tablero **sin leaderboard deployado** — solo existen `ranked_3x3.lb` y `ranked_6x6.lb`. Eso ya era un agujero antes del recorte.

**Falla a un piso compilado de `{3, 6}`**, al revés que `MatchmakingConfigReader`/`RankedConfigReader`, que fallan abiertos a propósito para que un hipo de Remote Config no impida resolver una partida. Para un control de seguridad esa postura está invertida. **Costo asumido:** es un segundo lugar que editar cuando los tableros vuelvan.

Verificado en vivo con `Tools/board-gate-e2e.ps1` (nuevo, commit `d741c39`): seis sondas contra `development`, todas pasan. La sonda que sostiene el argumento es pedir un **tamaño válido en un modo inexistente**: como la guarda cae al piso `{3, 6}` si no puede leer la config, una guarda que no leyera nada igual rechazaría 9×9 y 11×11 — solo el chequeo por modo contra una config realmente leída puede rechazar eso.

**Alcance que no cubre:** juego local 1P/2P nunca llama `CreateMatch` (corre client-side), así que cliente modificado puede jugar tablero recortado offline y cobrar por la clave `currency`. Cierre de esa brecha es milestone, no hotfix.

**Portrait fijo (commit `680e407`)**

`defaultInterfaceOrientation` estaba en `AutoRotation`. Se fijó a `Portrait` para no duplicar el encuadre de las 12 pantallas en landscape. Decisión del dueño: portrait único, sin portrait invertido.

**Barrido de 12 pantallas + 10 idiomas**

**Ninguna pantalla desborda**, y la razón es estructural: toda pantalla de contenido variable tiene ScrollView. El único caso con lista real es el historial (4980 unidades de contenido en 964 de viewport, scrollea bien); el resto mide chrome. **La tienda está vacía** —el catálogo es un pendiente— así que ahí se midió el marco, no una lista poblada, y hay que rebarrerla cuando exista.

**Ningún idioma desborda**, pero el alemán "Einstellungen" entraba por 1.8 unidades en la navegación inferior, y por eso el botón pasó de 124 a 136 (commit `7782c2a`): el espacio sube de 96.6 a 108.6 y el alemán baja de 98% a 87%. Medido con `TextElement.MeasureTextSize`, que devuelve el ancho renderizado de cualquier cadena sin cambiar el idioma del juego — los diez locales y los ocho contenedores salen en una llamada.

**La cobertura de glifos no se verificó**: medir el ancho no prueba que la fuente tenga los caracteres, porque una sustitución silenciosa mide igual de bien.

### Decisiones

| Decisión | Razón | Impacto |
|---|---|---|
| **ScaleWithScreenSize, no ConstantPhysicalSize** | Usabilidad: a mayor pantalla, la UI no se empequeñece. Referencia fija 720 ancho. | Consistencia visual independiente de resolución |
| **Inset per-pantalla, no global** | Fondos deben sangrar bajo notch. Safe area es problema de contenido, no de frame. | Más mantenimiento (propiedades USS custom), mejor UX |
| **Recorte a nivel config, no borrando Core** | Facilita vuelta posterior (solo config, sin compilación). Deuda minimizada. | Volver es trivial, pero duplica el lugar de edición |
| **Guarda server-side falla cerrada** | Seguridad: mejor pecar de restrictivo. Anti-cheat. | Segundo lugar a editar; costo de mantención asumido |
| **Portrait fijo** | No duplicar encuadre x landscape. Decisión dueño. | Tiempo de encuadre / cierre de Milestone 5 |

### Pendientes

1. **Ningún build en hardware real.** Seis dispositivos simulados, cero físicos. iOS no es viable en Windows sin Mac o Build Automation; Android es camino barato (target ya está en Android, precisa un prestado).
2. **Cobertura de glifos** (tr/vi/pl) sin verificación QA.
3. **Con qué criterio vuelven 9×9 y 11×11** — discriminador falta: `BOARD_CONFIGS` no conoce ancho de dispositivo. Techo mínimo 411dp (J7, no llega), techo máximo 600dp+ (Shield, alcanza 48.5dp). Entre 411 y 600 hay que decidir.
4. **L-06, desborde del 11×11** — precondición técnica de vuelta, revisar también en teléfono físico.
5. De antes: tienda/IAP, audio, pasada manual con dos clientes Ranked (Wire, UI serie, matchmaking), decay/rollover, eventos Analytics en Dashboard, deploy Triggers, ERR-KB-006 (decisión de diseño), `CHANGELOG-dev.md` backfill.

### Estado del Editor al cerrar

- Fuera de play mode, árbol limpio, `runInBackground` en `False`, orientación `Portrait`.
- Device Simulator en Nvidia Shield Tablet; **pestaña Game cerrada** a propósito (se cierra para trabajar dispositivos, ver `Docs/09-Encuadre-Dispositivos.md` T-06).
- **Remote Config y Cloud Code deployados por el dueño durante esta sesión** y verificados contra el backend, no solo contra el repo. Ese chequeo importa: los tests comparan el fallback offline contra el *archivo* `GameConfig.rc`, así que ninguno puede ver si lo deployado quedó atrasado.
- 11 commits pusheados a `main`, el último `7782c2a`.
- Tests EditMode: **237 pasan, 0 fallan**, de los cuales 6 son los nuevos `BoardSelectEligibilityTests`. (El reparto por assembly no se volvió a medir; la cifra previa registrada era 229.)

## Conexiones

[[01-Directrices-Proyecto.md]] — idioma, convenciones, git workflow
[[02-GDD-TicTacToe.md]] — modos, economía, temporadas Ranked
[[03-Arquitectura-UGS-TicTacToe.md]] — infraestructura Auth/Cloud Code/Matchmaker/Wire/Leaderboards
[[04-Store-Catalog-TicTacToe.md]] — tienda cosmética (aún sin implementar)
[[05-UI-Pantallas-TicTacToe.md]] — pantallas y flujo navegación
[[06-Wireframes-UI.md]] — wireframes todas 12 pantallas (incluyendo nueva Leaderboard wireframe 10)
[[07-Estetica-UI.md]] — paleta colores (X cian, O violeta) y estilo dark theme
[[CHANGELOG-dev.md]] — cambios detallados por versión (v0.1.0 a v0.5.0)
[[ERRORES_Conocidos.md]] — bugs diagnosticados y fixes (ticket verification 401, navegación Profile/Store bounce, tablas de localización sin regenerar, MCP respondiendo por otro proyecto, y ERR-KB-006 abierta: claves autoritativas de Cloud Save escribibles por el jugador)
[[08-Herramienta-Arte-Placeholder.md]] — generador de arte placeholder: reglas de prompt, post-proceso determinista y trampas de medición

## Fuente

Verificado contra CHANGELOG-dev.md v0.1.0–v0.5.0 (todas 2026-07-23), CLAUDE.md (milestones M1–M5 completados), CloudCode~/TicTacToeModule/README.md, Assets/RemoteConfig/GameConfig.rc, Assets/Leaderboards/*.lb, Assets/Matchmaker/*.mmq, Docs/06-Wireframes-UI.md, Docs/07-Estetica-UI.md.

Los números de *Estado actual* se remidieron el 2026-08-08 contra la fuente y no contra entradas previas de este archivo: `git rev-list`/`git branch --merged`/`git check-ignore` para el estado de git, el Test Runner del Editor para el conteo de tests, y el Dashboard de UGS (texto del DOM, no screenshots) para la config deployada. Tres afirmaciones que este archivo venía repitiendo resultaron falsas — están listadas en la entrada del 2026-08-08 (tercera sesión).
