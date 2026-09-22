# TicTacToeModule (Cloud Code)

Módulo único de Cloud Code (C#, .NET 9) para TicTacToe, según
[`Docs/03-Arquitectura-UGS-TicTacToe.md`](../../Docs/03-Arquitectura-UGS-TicTacToe.md#Cloud Code).
El sufijo `~` en `CloudCode~/` hace que Unity ignore esta carpeta (no la importa como assets).

## Estructura

```
CloudCode~/TicTacToeModule/
  TicTacToeModule.sln          # abrir esto en el IDE (Rider/VS)
  TicTacToeModule.csproj       # net9.0, incluye Assets/Scripts/Core/*.cs por <Compile Include>
  ModuleConfig.cs              # ICloudCodeSetup - registra IGameApiClient (IPushClient no necesita registro, ver comentario)
  GameProtocol.cs              # gemelo de Assets/Scripts/Game/Services/GameProtocol.cs
  Dtos.cs                      # contratos de GetGameConfig y de MatchState (+ Players/abandono, +reward ledger, +active-match index)
  GetGameConfigFunctions.cs    # [CloudCodeFunction] GetGameConfig
  MatchFunctions.cs            # [CloudCodeFunction] CreateMatch / PlayMove / GetMatchState / GetAiMove / SweepAbandonedMatches
  MatchStateStore.cs           # persistencia en Cloud Save Custom Data + replay de movimientos
  MatchIndexStore.cs           # indice diario de partidas online activas (Cloud Save Custom Data) para el barrido proactivo
  MatchmakingConfigReader.cs   # lee MATCHMAKING_CONFIG de Remote Config (abandon timeout, wait ceiling)
  OnlineRewardCalculator.cs    # calculo puro de la recompensa online (base x1.25/x1.0/0 + constantes de topes)
  OnlineRewardStore.cs         # I/O en Cloud Save Player Data: currency, ledger diario, ledger por rival

  # --- Milestone 5 - Ranked (design-doc.md sección 6) ---
  RankedConfigReader.cs        # lee RANKED_CONFIG de Remote Config (mismo patron que MatchmakingConfigReader)
  RankedEloCalculator.cs       # calculo puro de Elo (F, K variable, amortiguacion por rival, piso, redondeo forzado)
  RankedSeasonCalculator.cs    # calculo puro de temporada (seasonId/boundaries) a partir de RANKED_CONFIG
  RankedTierCalculator.cs      # calculo puro de tier/premio de cierre de temporada
  RankedRewardCalculator.cs    # tabla propia de moneda Ranked (10/4/0 3x3, 15/5/0 6x6)
  RankedProfileStore.cs        # I/O en Cloud Save Player Data (key "rankedProfile") + decay/rollover perezosos
  RankedRewardStore.cs         # I/O en Cloud Save Player Data: ledger de victorias puntuadas por rival/tablero (anti-boosting de MMR, distinto del ledger de moneda)
  RankedLeaderboardStore.cs    # wrapper de IGameApiClient.Leaderboards (submit, top N, version archivada)
  RankedSeriesStore.cs         # I/O en Cloud Save Custom Data (key = seriesId) de la serie 3x3 + creacion de game1/game2
  RankedMatchSupport.cs        # orquestacion Ranked completa, invocada desde MatchFunctions (CreateMatch/PlayMove/GetMatchState/SweepAbandonedMatches)
  RankedQueryFunctions.cs      # [CloudCodeFunction] GetRankedProfile / GetRankedLeaderboard - lecturas puras, no liquidan nada
```

Config-as-code del Matchmaker (Milestone 4) vive en `Assets/Matchmaker/` (no en esta carpeta, porque
Deployment la descubre por extensión igual que `Assets/RemoteConfig/GameConfig.rc`):

```
Assets/Matchmaker/
  QuickmatchQueue.mmq               # cola "quickmatch-queue" -> pool "quickmatch-pool"
  MatchmakerEnvironmentConfig.mme   # defaultQueueName = quickmatch-queue
```

Config-as-code del barrido proactivo de abandono (ver "Barrido proactivo de partidas abandonadas"
abajo) vive en `Assets/Triggers/` - **no** deployable todavía desde `Window → Deployment` (el paquete
`com.unity.services.deployment` instalado, 1.7.2, no lista Triggers/Scheduler entre los servicios
soportados a la fecha de este README - ver esa sección para el camino de deploy alternativo):

```
Assets/Triggers/
  SweepAbandonedMatches.tr               # Trigger: EventType del Scheduler -> ActionUrn del modulo
  SweepAbandonedMatchesSchedule.sched    # Scheduler: cron cada 5 min -> dispara el EventType de arriba
```

`TTTXO.Core` **no se duplica**: el `.csproj` lo incluye directamente vía
`<Compile Include="../../Assets/Scripts/Core/*.cs" />`, así que las reglas de juego y la IA son
literalmente el mismo código que corre en el cliente en Milestone 1. No copiar esos archivos acá.

## Funciones

| Función | Qué hace |
|---|---|
| `GetGameConfig` | Lee `BOARD_CONFIGS`, `AI_DIFFICULTY_PARAMS` y `MATCHMAKING_CONFIG` de Remote Config y los devuelve junto a `GameProtocol.Version` |
| `CreateMatch` | Crea un `MatchState` nuevo. 1P/local: directo, sin ticket (igual que Milestone 2). Online (`mode="online_quickmatch"`): idempotente por `ticketId` (ver "Milestone 4" abajo) |
| `PlayMove` | Valida y aplica un movimiento (server-authoritative). Online: valida turno por `context.PlayerId`, notifica al rival por Wire y, si el movimiento termina la partida, calcula y acredita la recompensa de moneda online (ver "Recompensa de moneda online" abajo) |
| `GetMatchState` | Devuelve el estado actual de una partida. Online: resuelve abandono reactivamente (ver abajo, acreditando 0 al ganador por abandono) y actualiza `lastActivityAt` del caller |
| `GetAiMove` | Calcula el movimiento de la IA (no lo aplica - el caller llama `PlayMove` después). Sin cambios en Milestone 4 |
| `SweepAbandonedMatches` | Barrido **proactivo** de partidas online abandonadas por ambos jugadores (ver "Barrido proactivo de partidas abandonadas" abajo). No la llama el cliente - la dispara un Scheduler/Trigger programado (o se puede invocar a mano para pruebas) |
| `GetRankedProfile` | Milestone 5. Devuelve el estado Ranked **autoritativo del caller** (MMR/colocación/tier por tablero 3x3 y 6x6, más contexto de temporada), aplicando el mismo decay/rollover perezoso que ya usa la liquidación (`RankedProfileStore.TouchAsync`) - nunca un valor pre-decay. Ver "Ranked (Milestone 5)" -> "Lecturas: GetRankedProfile / GetRankedLeaderboard" abajo |
| `GetRankedLeaderboard` | Milestone 5. Top N (default 25, tope duro 50) del leaderboard **vivo** de un tablero Ranked (3x3 o 6x6) más la fila propia del caller (posición + MMR), o `hasOwnEntry = false` si todavía no completó colocación. Solo lectura - el leaderboard se sigue escribiendo exclusivamente desde la liquidación de una unidad Ranked |

`ValidatePurchase` y `RedeemStoreItem` **no están implementadas** todavía - son del milestone de
tienda/IAP (ver [`Docs/04-Store-Catalog-TicTacToe.md`](../../Docs/04-Store-Catalog-TicTacToe.md)).

Ranked (Milestone 5, `Docs/design-doc.md` sección 6) está implementado **reutilizando las mismas 4
funciones de match** (`CreateMatch`/`PlayMove`/`GetMatchState`/`SweepAbandonedMatches`) para todo lo
que liquida una unidad Ranked - solo ramas `Mode == "ranked"` dentro de ellas - más las **dos
funciones de lectura nuevas** de la tabla de arriba (`GetRankedProfile`/`GetRankedLeaderboard`,
`RankedQueryFunctions.cs`), que no liquidan nada, solo leen. Ver "Ranked (Milestone 5)"
más abajo.

## Milestone 4 - Quickmatch online

### Flujo CreateMatch (handoff Matchmaker -> Cloud Code, riesgo #1)

Cada jugador tiene su **propio** ticket de Matchmaker (`quickmatch-pool` tiene
`maxPlayersPerTicket = 1`), pero ambos reciben el **mismo** `MatchIdAssignment.MatchId` una vez
emparejados (`matchHosting.type = "MatchId"`, ver `Assets/Matchmaker/QuickmatchQueue.mmq`) - ese
`MatchId` compartido (acá "`handoffId`") es la clave de unión/idempotencia, no el ticket de cada uno.

1. El cliente crea su ticket (`MatchmakerService.Instance.CreateTicketAsync`) contra `quickmatch-queue`
   con `CustomData = { board_size }`.
2. Hace polling de `GetTicketAsync` hasta ver un `MatchIdAssignment` con `Status == Found` -> ese es
   el `handoffId`.
3. Llama `CreateMatch(boardSize, mode: "online_quickmatch", ticketId: <su propio ticket>, handoffId)`.
4. El módulo:
   - Llama al Matchmaker Admin API (`gameApiClient.MatchmakerTickets.GetTicketStatusAsync`) con el
     `ticketId` del caller y verifica que resolvió (`Status == Found`) **y** que su `MatchId` coincide
     con el `handoffId` reclamado - el caller solo puede afirmar su propia identidad
     (`context.PlayerId`, ya autenticada), nunca la del rival.
   - Idempotencia: busca un puntero `handoff-{handoffId}` en Cloud Save Custom Data.
     - **No existe** (primer caller): crea un `MatchState` de **1 jugador** ("waiting for opponent",
       `MatchStateDto.IsWaitingForOpponent = true`, sin símbolo asignado todavía) y publica el puntero.
     - **Existe con 1 jugador** y es **otro** `context.PlayerId` (segundo caller = el rival): completa
       el roster a 2 jugadores y recién ahí asigna X/O.
     - **Existe con 1 jugador** y es el **mismo** caller (reintento): no-op idempotente, devuelve el
       estado "waiting" tal cual.
     - **Existe con 2 jugadores** (ya completo): no-op idempotente, devuelve el `MatchState` ya armado.
   - `PlayMove` rechaza movimientos mientras `Players.Count == 1` ("Match is waiting for the opponent
     to join").

### Símbolo X/O (asignación determinista)

Recién cuando el segundo jugador completa el roster: los 2 `playerId` se ordenan con
`StringComparer.Ordinal`, el primero es `X` (que siempre mueve primero), el segundo `O`. No puede
calcularse antes de conocer ambos IDs - ver "waiting for opponent" arriba.

### PlayMove online

- Rechaza al caller si no es uno de los 2 jugadores del match, o si no es su turno
  (`InvalidReason = "Not this player's turn"` - la validación vive en el módulo, no en `TTTXO.Core`,
  porque Core no tiene noción de "qué cliente de red" está moviendo).
- Al aplicar el movimiento con éxito, notifica al rival vía `IPushClient.SendPlayerMessageAsync`
  (`messageType = "match_updated"`, payload `{type, matchId}` serializado a JSON string - el rival
  vuelve a pedir `GetMatchState`, nunca confía en el payload). Un fallo del push nunca revierte el
  movimiento ya persistido - el polling de respaldo del cliente cubre un push perdido.

### Abandono (riesgo #4, resolución reactiva)

`GetMatchState`, si el match es online y sigue `InProgress`: si el rival no tuvo actividad
(`Players[].LastActivityAtUnixSeconds`) por más de `MATCHMAKING_CONFIG.AbandonTimeoutMinutes`
(Remote Config, default 3), resuelve el match como victoria por abandono para el caller
(`AbandonedWinnerPlayerId`), lo persiste y lo refleja en el DTO (`Status = "x_won"/"o_won"`,
`EndedByAbandonment = true`). Sin cron: se resuelve la próxima vez que alguien llama `GetMatchState`.

### Recompensa de moneda online (Milestone 4, design-doc.md sección 4 "Modo online Quickmatch")

Todo el calculo y el otorgamiento corren en `PlayMove` (movimiento terminal) / `GetMatchState`
(resolucion de abandono) - el cliente nunca calcula ni escribe moneda online
(Docs/01-Directrices-Proyecto.md#Seguridad / anti-cheat: "hay incentivo real de trampa").

- **Base**: se deriva de `TTTXO.Core.RewardCalculator` llamandolo con dificultad Medio (`x1,0`,
  identidad) en vez de duplicar la tabla base-por-tamano - ver `OnlineRewardCalculator.ComputeRawReward`.
  Victoria = `floor(base_win x 1,25)`, empate = `base_draw x 1,0`, derrota = `0`, victoria por
  abandono = `0` (ver `MatchFunctions.ResolveAbandonmentIfNeededAsync`).
- **Topes anti-farming**, todos server-side (`OnlineRewardCalculator`/`OnlineRewardStore`):
  - Tope por rival: 3 victorias pagadas por `(playerId, rivalId, dia UTC)`, la 4a+ paga 0. Item de
    Cloud Save Player Data con clave `rivalWin_{rivalId}_{yyyy-MM-dd}` (el dia va en la clave misma,
    no hace falta reset explicito).
  - Tope diario online (200) y tope global (300), en el item `onlineCurrencyLedger` (Player Data),
    reset a medianoche **UTC de servidor** (no la hora local del dispositivo). Ver la limitacion
    documentada en `OnlineCurrencyLedgerRecord` sobre por que el tope global solo ve, por ahora, las
    ganancias online (el 1P/local offline sigue siendo un contador 300/dia separado y local en
    `GameManager`, sin visibilidad server-side hasta que ese flujo tambien migre a Cloud Code).
  - La moneda otorgada se escribe en el item `currency` (Player Data) con el mismo shape que ya usa
    el cliente (`Assets/Scripts/Game/Services/PlayerDataService.cs`'s `CurrencySaveData`), asi que el
    proximo `PlayerDataService.LoadIntoSessionAsync` del cliente la recoge sin cambios adicionales.
- **Persistencia del resultado**: el monto otorgado a cada jugador queda en
  `MatchStateRecord.AwardedSoftCurrencyByPlayerId` (por playerId), y `MatchStateStore.ToDto` lo
  expone en `MatchStateDto.AwardedSoftCurrency` solo para el caller de esa llamada - se calcula una
  unica vez (la jugada que termina la partida no puede repetirse: un reintento de `PlayMove` sobre
  una partida ya terminada falla la validacion de movimiento antes de llegar al calculo de
  recompensa) y las llamadas posteriores a `GetMatchState` solo leen el valor ya persistido.
- **Ranked (Milestone 5)**: no implementado. `OnlineRewardCalculator` deja un punto de extension
  comentado (reutilizar la lectura de base Medio, pero con su propio multiplicador por MMR/tier y su
  propio set de topes) en vez de heredar la curva plana de Quickmatch, siguiendo el lineamiento de
  design-doc.md seccion 4.

### Limitaciones conocidas (a cerrar antes de Ranked, Milestone 5)

- **Ventana de carrera en el "waiting for opponent"**: el check-then-write del puntero
  `handoff-{handoffId}` (`TryGetMatchIdForHandoffAsync` + `SaveHandoffPointerAsync`/`SaveAsync`) no es
  atómico - Cloud Save Custom Data no ofrece transacciones (mismo límite ya documentado para otros
  flujos de este proyecto). Si ambos jugadores llaman `CreateMatch` en el mismo instante, en teoría
  los dos pueden ver "no existe puntero todavía" y crear cada uno su propio `MatchState` de 1 jugador,
  pisándose el puntero (el último `SaveHandoffPointerAsync` gana). En la práctica es una ventana muy
  corta (ambos clientes ya están haciendo polling concurrente del mismo hallazgo), pero antes de
  Ranked conviene medirlo con datos reales y, si hace falta, mover el puntero a una escritura
  condicional si el SDK de Cloud Save lo soporta para cuando se agregue.
- Sin push nativo (FCM/APNs) para "es tu turno" en background - gap ya identificado en la arquitectura.
- **Índice de partidas activas sin transacciones** (mismo límite que el puntero de handoff, ver
  "Barrido proactivo de partidas abandonadas" abajo): `MatchIndexStore.AddMatchAsync`/`RemoveMatchAsync`
  hacen su propio read-modify-write no atómico sobre el documento del día. Es un riesgo aceptado y
  estrictamente aditivo (ver esa sección) - no bloquea el cierre de este ítem.
- **Deploy de Triggers/Scheduler no soportado por el Deployment window instalado** (paquete
  `com.unity.services.deployment` 1.7.2 - ver "Barrido proactivo de partidas abandonadas" y "Cómo
  deployar" abajo): los archivos `.tr`/`.sched` de `Assets/Triggers/` están listos pero hay que
  deployarlos por CLI o Dashboard manual, no desde `Window → Deployment`, hasta que ese paquete agregue
  soporte.

Cerrados en esta tarea (ver detalle en las secciones siguientes): la verificación de ticket de
Matchmaker ahora es bloqueante (antes: "modo best-effort"), y existe resolución **proactiva** de
abandono además de la reactiva (antes: "sin resolución proactiva").

## Verificación de ticket de Matchmaker

`TryVerifyTicketResolvesToHandoffAsync` (best-effort, degradaba a warning en 401) pasó a ser
**`VerifyTicketResolvesToHandoffAsync`**: lanza excepción - y por lo tanto rechaza `CreateMatch` - si el
ticket del caller no resolvió, resolvió a un match distinto del `handoffId` reclamado, o la llamada al
Matchmaker Client API falla por cualquier motivo (auth, transporte, etc.).

### La causa real del 401 no era falta de permisos - era un bug de parámetros

La versión anterior llamaba:

```csharp
gameApiClient.MatchmakerTickets.GetTicketStatusAsync(
    context, context.ProjectId, context.EnvironmentId, ticketId, CancellationToken.None);
```

Reflejando el ensamblado instalado (`Com.Unity.Services.CloudCode.Apis` 0.0.26,
`Unity.Services.Matchmaker.Api.IMatchmakerTicketsApi`) con `System.Reflection.MetadataLoadContext`
(proyecto aislado fuera del repo, target `net9.0`, sin resolver dependencias en runtime), la firma real
es:

```csharp
Task<ApiResponse<TicketStatusResponse>> GetTicketStatusAsync(
    IExecutionContext executionContext, string accessToken, string id,
    string impersonatedUserId = null, CancellationToken cancellationToken = default);
```

Es decir: **sí existe** un parámetro de access token explícito (segunda posición) - no hace falta una
sobrecarga de 6 argumentos ni pasar `projectId`/`environmentId` (no son parámetros de este método en
absoluto). La llamada anterior pasaba `context.ProjectId` (un GUID, no un token) como `accessToken` y
`context.EnvironmentId` como `id` (el ticket a buscar) - nunca estaba buscando `ticketId` en absoluto,
así que el 401 era consecuencia de un valor de auth basura, no de un límite real del SDK o de falta de
rol en el Dashboard.

### El fix: `context.ServiceToken` **+ `impersonatedUserId`**, no Service Account

La llamada corregida:

```csharp
gameApiClient.MatchmakerTickets.GetTicketStatusAsync(
    context, context.ServiceToken, ticketId, context.PlayerId, CancellationToken.None);
```

> **El cuarto argumento no es opcional en la práctica.** Con `null` ahí, este endpoint devuelve
> **`Forbidden`** — lo encontró la primera corrida en vivo (2026-08-08, `development`), y como la
> verificación es bloqueante, **ninguna partida online se podía crear, ni Quickmatch ni Ranked**.
> `GetTicketStatus` es una API con alcance de jugador: el mismo GET con el bearer del **jugador**
> devuelve 200 con el assignment, y así se aisló la causa. Un Service Token no tiene identidad de
> jugador propia, así que tiene que nombrar a aquel por el que actúa: `context.PlayerId`, que es
> justo el dueño del ticket cuya propiedad se quiere verificar. En el cable viaja como header
> `impersonated-user-id`.

### Y por qué no se usa `.Data`: el `oneOf` del SDK no resuelve

Arreglada la auth, apareció un segundo fallo apilado detrás: `ApiException(Deserialization)` en cada
llamada. `Unity.Services.Matchmaker.Model.TicketStatusResponse` es un **`oneOf` generado** sobre cinco
tipos de assignment, y el payload normal

```json
{"assignmentType":"MatchIdAssignment","message":null,"status":"Found","matchId":"..."}
```

deserializa **con éxito contra tres de los cinco** — `CustomAssignment` y `MultiplayAssignment` solo
se distinguen por campos opcionales (`customData`, `ip`, `port`). Un `oneOf` que matchea más de una
rama no puede resolver, así que **el SDK no parsea la respuesta normal de su propio endpoint**. Es un
defecto del generador, no algo que el llamador pueda cambiar, y `0.0.26` es la última versión no-alpha
publicada.

La salida está en cómo falla `HttpApiClient.ToApiResponse<T>` (decompilado):

```csharp
string text = await response.Content.ReadAsStringAsync();
ApiResponse<T> apiResponse = new ApiResponse<T> { ..., RawContent = text };   // cuerpo guardado
try   { apiResponse.Data = JsonConvert.DeserializeObject<T>(text); }
catch { throw new ApiException(..., $"Deserialization of type '{typeof(T)}' failed.", apiResponse); }
```

`RawContent` se setea **antes** de deserializar y la excepción **se lleva el `ApiResponse` entero**,
así que el cuerpo sobrevive. `VerifyTicketResolvesToHandoffAsync` atrapa
`ApiException` con `Type == Deserialization`, saca `ex.Response.RawContent` y lee `status`/`matchId`
con `JsonDocument`. Los tres rechazos de seguridad son los mismos de antes; solo cambió de dónde
salen los dos valores.

usa exactamente el mismo patrón que `MatchStateStore`/`OnlineRewardStore` ya usan para toda otra
llamada cross-player de Cloud Save en este módulo. Esto está respaldado por la documentación oficial de
Unity ("Service and access token support",
<https://docs.unity.com/en-us/cloud-code/scripts/how-to-guides/token-support>), cuya tabla de "Service
Token Support" lista **Matchmaker explícitamente bajo "UGS Client APIs"** (soportado), a diferencia de
"UGS Admin APIs" (que la misma tabla marca "Not supported - use Service Account authentication"). Esto
coincide con que `IMatchmakerTicketsApi` vive en el namespace `Unity.Services.Matchmaker.Api` (Client
API), no en un namespace `.Admin.` - a diferencia de, por ejemplo,
`Unity.Services.CloudSave.Admin.Api.ICloudSaveDataApi`, que si es la Admin API (y también acepta
`context.ServiceToken` con éxito hoy, según el propio código de este módulo, porque el token de
servicio SÍ cubre acceso cross-player para los servicios listados en esa tabla, Cloud Save incluido).

**No hace falta crear una service account ni asignar roles de "Matchmaker Admin" en el Dashboard** para
este endpoint en particular - la corrección es puramente de parámetros. El único requisito de Dashboard
es el que ya existía: Matchmaker habilitado en el proyecto (ya lo está, la creación de tickets desde el
cliente ya depende de eso).

### Cómo se verificó, y la lección de método

La versión anterior de esta sección decía que todo lo de arriba estaba respaldado por la firma
reflejada del ensamblado y por la documentación de Unity, **pero no por una llamada real**, y pedía
probarlo en `development` antes de confiar en ello. Nadie lo hizo durante dos milestones. Cuando por
fin se corrió, el endpoint devolvió `Forbidden` y quedó claro que el razonamiento era correcto en el
credencial y **incompleto en el alcance**.

La lección, para que no se repita: **reflexión y documentación prueban que una firma compila; solo una
llamada en vivo prueba que el servicio la acepta.** Un comentario que dice "verificar esto antes de
confiar" no es una verificación — es deuda, y esta costó dos milestones de Quickmatch y Ranked rotos
sin que nadie lo supiera.

El arnés que lo encontró vive en `Tools/ranked-e2e.ps1`: autentica dos jugadores anónimos, los
empareja por `ranked-queue` y llama a las funciones de Cloud Code contra el entorno real. Correrlo es
la forma barata de no volver a acumular esta clase de deuda.

Si en algún momento vuelve a fallar con un error de auth (y no con "resolved to a different match",
que sería un bug de lógica), revisar si el proyecto tiene alguna restricción de Access Control
(Dashboard → Access Control) que limite el token de servicio del módulo — y ajustarla ahí, **sin**
reintroducir el `try/catch` de degradación antes de entender por qué falló.

## Barrido proactivo de partidas abandonadas

Antes de esta tarea, una partida abandonada por **ambos** jugadores nunca se resolvía: la resolución de
abandono en `GetMatchState` es reactiva (solo corre cuando alguien llama), así que si nadie vuelve a
abrir la partida, queda `in_progress` para siempre. Esto ahora se complementa con una resolución
**proactiva**: `SweepAbandonedMatches`, disparada por un Scheduler/Trigger programado.

### Índice de partidas activas (`MatchIndexStore.cs`)

Cloud Save Custom Data no tiene una llamada de "listar todos los documentos" accesible simplemente
desde este módulo. Lo que sí existe, verificado por reflexión contra el ensamblado 0.0.26, es una API de
**query sobre índices** (`Unity.Services.CloudSave.Admin.Api.ICloudSaveDataApi.QueryDefaultCustomDataAsync`
+ `CreateDefaultCustomIndexAsync`/`CreatePrivateCustomIndexAsync`, con filtros vía `FieldFilter`/
`QueryIndexBody`) - técnicamente disponible desde `gameApiClient.CloudSaveData` (mismo tipo concreto
usado hoy para Custom Data). Se descartó para esta tarea: requeriría provisionar el índice primero (un
paso de setup adicional, no verificable sin deploy) y la semántica exacta de filtros combinados sobre
campos anidados (`Players[].LastActivityAtUnixSeconds`) no se pudo probar en vivo. Queda documentado
como un camino de evolución futura si el índice manual no escala.

En su lugar, el módulo mantiene su propio índice liviano: un documento de Cloud Save Custom Data **por
día UTC** (clave `active-matches-{yyyy-MM-dd}`), con la lista de `matchId` de toda partida online que
completó su roster de 2 jugadores y todavía no se observó terminal.

- **Alta**: en `MatchFunctions.CompleteOnlineMatchAsync` (cuando el segundo jugador completa el roster
  - una partida "waiting for opponent" de 1 jugador nunca se indexa, porque "ambos abandonaron" no
  tiene sentido todavía sin un rival real).
- **Baja**: en `PlayMove` (la jugada que termina la partida), en `GetMatchState` (resolución reactiva
  de abandono) y en el propio `SweepAbandonedMatches` (resolución proactiva o poda de una entrada ya
  resuelta) - los tres puntos donde una partida puede volverse terminal.
- **Partición por día**: acota el tamaño de cada documento a, aproximadamente, un día de partidas
  online concurrentes, y usa siempre el día calculado desde `MatchStateRecord.CreatedAtUnixSeconds` (no
  "ahora") tanto al agregar como al quitar, para que ambas operaciones apunten siempre a la misma
  partición.
- **Poda**: cada corrida de `SweepAbandonedMatches` relee la lista completa de un día, descarta del
  índice cualquier `matchId` que ya esté terminal por otra vía (jugada real, resolución reactiva, una
  corrida anterior del barrido, o un registro faltante/corrupto) y escribe de vuelta solo lo que sigue
  activo - así el índice nunca acumula entradas muertas indefinidamente sin necesitar un job de limpieza
  aparte.
- **Límite conocido**: mismo problema de falta de transacciones en Cloud Save Custom Data ya documentado
  para el puntero de handoff - un alta/baja concurrente sobre el documento del mismo día puede perder una
  entrada. Es un riesgo aceptado y **estrictamente aditivo**: una partida que se cae del índice solo
  pierde el barrido proactivo, nunca la resolución reactiva ya existente en `GetMatchState` - el peor
  caso es idéntico al comportamiento previo a este índice.

### `SweepAbandonedMatches` (`[CloudCodeFunction]`)

Recorre hasta 3 particiones diarias (`IndexLookbackDays` en `MatchFunctions.cs`), y para cada `matchId`
indexado (hasta un límite por corrida, ver abajo): recarga el `MatchStateRecord` real (nunca confía en
el índice solo), y si sigue `InProgress`, con roster de 2 jugadores y sin abandono ya resuelto, compara
el `LastActivityAtUnixSeconds` de **ambos** jugadores contra `MATCHMAKING_CONFIG.AbandonTimeoutMinutes`
(mismo Remote Config que usa la resolución reactiva). Si **ambos** exceden el umbral, resuelve la
partida y la saca del índice; si no, la deja indexada para la próxima corrida.

**Decisión de diseño - "ambos abandonaron" no tiene ganador natural**: a diferencia del camino reactivo
(donde quien llama es, por construcción, un jugador activo, y gana razonablemente), un barrido solo
dispara porque **ninguno** de los dos jugadores fue visto recientemente. Declarar ganador a cualquiera
de los dos sería arbitrario (¿por qué X y no O?) y no cambia la recompensa (ambos cobran 0 de cualquier
forma, ver "Derrota/abandono online = 0" en `OnlineRewardCalculator`), así que esta resolución es un
**empate/sin ganador** (`MatchStateRecord.BothPlayersAbandoned`, expuesto como `Status = "draw"`,
`EndedByAbandonment = true`), a diferencia del camino reactivo (`AbandonedWinnerPlayerId`, que sí elige
un ganador). Esto importa sobre todo para Ranked (Milestone 5, no implementado): un empate es el input
de MMR correcto para "nadie estaba jugando realmente", mientras que acreditarle una victoria fantasma a
cualquiera de los dos lados distorsionaría el ladder.

**Idempotencia**: cada `matchId` se revalida contra su propio estado justo antes de resolverlo, nunca
confiando en que el índice esté al día - una partida ya cerrada por el camino reactivo (o por otra
corrida del barrido) entre haber sido indexada y ser barrida simplemente se poda del índice sin volver a
resolverse. En el sentido inverso, `GetMatchState`/`ResolveAbandonmentIfNeededAsync` ahora también
comprueba `!record.BothPlayersAbandoned` antes de intentar una resolución reactiva, para no pisar un
empate ya resuelto por el barrido con una victoria unilateral.

**Límite por corrida y frecuencia recomendada**:

- `DefaultMaxMatchesPerSweep = 200` (partidas evaluadas por corrida si el trigger no pasa
  `maxMatches` explícito), `MaxMatchesPerSweepHardCap = 500` (techo duro aunque se pida más). Lo que no
  entra en una corrida queda indexado para la siguiente - nunca bloquea ni se descarta.
- **Frecuencia recomendada: cada 5 minutos** (`*/5 * * * *` en `Assets/Triggers/SweepAbandonedMatchesSchedule.sched`),
  razonable frente a `AbandonTimeoutMinutes` por defecto = 3 minutos (detecta un abandono a los pocos
  minutos de vencer el umbral, no instantáneamente, pero sin sobrecargar Cloud Code con ejecuciones
  constantes). Si el volumen de partidas concurrentes creciera mucho para Ranked, subir el límite por
  corrida antes que bajar la frecuencia.

### Disparo programado (Scheduler + Trigger)

Confirmado con la documentación oficial de Unity (Cloud Code Triggers/Scheduler,
<https://docs.unity.com/en-us/cloud-code/triggers/getting-started> y páginas relacionadas) y los
schemas oficiales (`https://ugs-config-schemas.unity3d.com/v1/triggers.schema.json` y
`.../schedules.schema.json`, obtenidos directamente): **sí existe** disparo programado config-as-code
para Cloud Code, vía dos archivos:

- `Assets/Triggers/SweepAbandonedMatchesSchedule.sched` (extensión confirmada, no solo el schema JSON):
  define un evento recurrente `sweep-abandoned-matches-tick` con cron `*/5 * * * *`.
- `Assets/Triggers/SweepAbandonedMatches.tr`: escucha el `EventType` que ese schedule produce
  (`com.unity.services.scheduler.{EventName}.v{PayloadVersion}` -
  `com.unity.services.scheduler.sweep-abandoned-matches-tick.v1`) y lo conecta a
  `ActionUrn = "urn:ugs:cloud-code:TicTacToeModule/SweepAbandonedMatches"`.

**Bloqueo real: el Deployment window instalado no los soporta todavía.** El manual oficial del paquete
`com.unity.services.deployment` (1.7.2, el instalado en este proyecto - ver `Packages/manifest.json`)
lista explícitamente los servicios soportados: *"Cloud Code, Economy, Leaderboards, Multiplayer
Services, Server Hosting, Matchmaker, Services tooling, Game Overrides, Access Control, Remote
Config"* - **sin Triggers ni Scheduler**. `Window → Deployment` no va a listar estos dos archivos.

**Camino alternativo confirmado: UGS CLI.** La documentación de `ugs deploy`
(<https://services.docs.unity.com/guides/ugs-cli/1.4.0/general/base-commands/deploy/>) sí lista
"Triggers and Scheduler" entre los servicios soportados por el comando general `ugs deploy`. Pasos para
el usuario:

1. Instalar la [UGS CLI](https://docs.unity.com/ugs/en-us/manual/overview/manual/ugs-cli-introduction)
   si no la tiene (`dotnet tool install -g com.unity.services.cli` o el instalador standalone, según la
   documentación oficial vigente).
2. Autenticarse (`ugs login`) y seleccionar el proyecto/ambiente (`ugs config set project-id
   6edc8de2-6599-4f88-bbe1-e9228d6b19e8`, `ugs config set environment-name <development|production>`).
3. Desde la raíz del repo: `ugs deploy Assets/Triggers --services scheduler,triggers` (o `ugs deploy
   Assets/Triggers` a secas, dejando que el CLI detecte el tipo de cada archivo por extensión).
4. Verificar en el Dashboard (proyecto `tttxo` → Cloud Code → Triggers / Scheduler, si esa vista ya
   existe en el plan contratado) o con `ugs scheduler list` / `ugs triggers list` que quedaron
   publicados.
5. **Nota de fragilidad reportada por otros usuarios**: hay un hilo del foro oficial
   (<https://discussions.unity.com/t/error-when-trying-to-deploy-scheduler/1704301>) documentando un bug
   temporal donde un deploy de Scheduler "tenía éxito" pero el trigger no corría y el Dashboard no lo
   mostraba (resuelto server-side por Unity, no requería acción del usuario) - si el sweep no corre
   después de deployar, confirmar con `ugs scheduler list`/`ugs triggers list` antes de asumir que la
   config-as-code está mal escrita.

Si el CLI tampoco estuviera disponible/permitido en el flujo de este equipo, la función sigue siendo
invocable manualmente (desde el Dashboard, `Cloud Code → TicTacToeModule → SweepAbandonedMatches → Run`,
o vía el SDK del cliente como cualquier otro `[CloudCodeFunction]`) - sirve como base funcional aunque
el disparo automático quede pendiente.

## Cómo deployar

> **Requisito**: la solución debe contener un publish profile para el proyecto principal
> (`Properties/PublishProfiles/FolderProfile.pubxml`, ya incluido). Sin ese archivo, el
> Deployment window falla con "Failed to retrieve main project — Could not find a Publish
> Profile". Verificable con: `dotnet publish TicTacToeModule.csproj -p:PublishProfile=FolderProfile`.

1. Verificar que el proyecto compila: `dotnet build TicTacToeModule.sln` desde esta carpeta.
2. En el Editor de Unity, abrir `Assets/CloudCode/TicTacToeModule.ccmr` (ya apunta a
   `TicTacToeModule.sln` con una ruta relativa) - no hace falta crearlo de nuevo.
3. Instalar el paquete **Multiplayer Services** (`com.unity.services.multiplayer`) desde
   `Window → Package Manager → Unity Registry` si el Deployment window todavía no lista
   `Assets/Matchmaker/QuickmatchQueue.mmq`/`MatchmakerEnvironmentConfig.mme` - es el paquete que le
   enseña al Deployment window a reconocer recursos de Matchmaker (ver Packages/manifest.json).
4. `Window → Deployment` (o `Services → Deployment` en Unity 2022+): debería listar
   `TicTacToeModule.ccmr`, `Assets/RemoteConfig/GameConfig.rc`,
   `Assets/Matchmaker/QuickmatchQueue.mmq`, `Assets/Matchmaker/MatchmakerEnvironmentConfig.mme`,
   `Assets/Matchmaker/RankedQueue.mmq` (Milestone 5) y, si el paquete instalado soporta Leaderboards
   config-as-code, `Assets/Leaderboards/ranked_3x3.lb`/`ranked_6x6.lb` (Milestone 5 - ver
   `Assets/Leaderboards/README.md` sobre la extensión `.lb` no verificada contra el Editor real).
5. Tildar todo y `Deploy Selected`.
6. En el [Dashboard de UGS](https://cloud.unity.com/) del proyecto (`tttxo`, cloudProjectId
   `6edc8de2-6599-4f88-bbe1-e9228d6b19e8`):
   - Habilitar los servicios Authentication, Cloud Code, Cloud Save, Remote Config, Analytics,
     **Matchmaker**, **Leaderboards** (Milestone 5) (Deployment no requiere habilitación aparte, es una
     herramienta del Editor).
   - Confirmar en **Matchmaker → Queues** que `quickmatch-queue`/`quickmatch-pool` y
     `ranked-queue`/`ranked-pool` (Milestone 5) quedaron publicados con `matchHosting.type = MatchId`,
     `backfillEnabled = false` y `timeoutSeconds = 180` - si el deploy de algún `.mmq`/`.mme` falla o el
     Deployment window no lo detecta (paquete/versión no soportada aún), configurarlo **a mano**
     siguiendo "Fallback manual del Matchmaker" más abajo (aplica igual a `ranked-queue`, con la regla
     `Difference` sobre `mmr` y las relajaciones descritas en "Ranked (Milestone 5)" arriba).
   - **IMPORTANTE antes de deployar `RANKED_CONFIG`**: `seasonStartUnixSeconds` en
     `Assets/RemoteConfig/GameConfig.rc` (`1787875200` = `2026-08-28T00:00:00Z`) y el `ResetConfig.Start`
     de `Assets/Leaderboards/ranked_3x3.lb`/`ranked_6x6.lb` tienen que seguir apuntando al mismo
     instante, con el cron `0 0 28 * *` en el mismo día del mes - ver `Assets/Leaderboards/README.md`
     "Sincronización obligatoria" (lo verifica `SeasonConfigAlignmentTests`). Si no llega por el deploy
     del `.rc`, agregar `MATCHMAKING_CONFIG` y `RANKED_CONFIG` a mano en Remote Config con el contenido
     de `GameConfig.rc`.
   - Confirmar en **Leaderboards** (Milestone 5) que `ranked_3x3`/`ranked_6x6` quedaron publicados con
     `UpdateType = Latest score` (**no** "Best score" - ver `Assets/Leaderboards/README.md`, es crítico
     para que el MMR pueda bajar), `SortOrder = High to low`, y el reset programado con `Archive`
     habilitado. Si el Deployment window no soporta `.lb` todavía, crearlos a mano con esos mismos
     valores.
   - Registrar en el **Event Manager** de Analytics los eventos/parámetros exactos que emite
     `Assets/Scripts/Game/Services/GameAnalytics.cs` (ver el reporte final de la tarea que agregó
     este módulo, o `Docs/design-doc.md` secciones 5/6.7) **antes** de que un build los emita, o el
     pipeline los descarta. Milestone 4 agrega `matchmaking_wait_time` (+seconds, board_size, matched)
     y `matchmaking_cancelled` (+reason). Milestone 5 agrega `ranked_mmr_changed`,
     `ranked_placement_completed`, `ranked_match_abandoned`, `ranked_queue_fallback_shown`,
     `ranked_season_reward_granted`, más los parámetros nuevos `mode`/`mmr_gap` en
     `matchmaking_wait_time`, `mode` en el overload online de `match_finished`, y los valores nuevos de
     `source` (`ranked_match`, `ranked_season_reward`) en `soft_currency_earned`.
   - **No hace falta ninguna configuración nueva de Dashboard para la verificación de ticket de
     Matchmaker** (ver "Verificación de ticket de Matchmaker" arriba) - `context.ServiceToken` ya
     cubre el Matchmaker Client API. Lo mismo aplica a Leaderboards (ver "Ranked (Milestone 5)" arriba,
     "Leaderboards" - confirmado por la doc oficial de tokens de servicio). Sí conviene validar todo en
     development antes de confiar en modo hard en producción.
   - Deployar `Assets/Triggers/SweepAbandonedMatches.tr` y `SweepAbandonedMatchesSchedule.sched` por
     **UGS CLI** (`Window → Deployment` no los soporta en el paquete instalado) - ver "Barrido
     proactivo de partidas abandonadas" arriba para los pasos exactos y el fallback manual. Sin cambios
     por Ranked (el barrido ya es agnóstico de modo).

### Fallback manual del Matchmaker (si el Deployment window no soporta `.mmq`/`.mme` en tu versión)

Si `Window → Deployment` no lista los archivos de `Assets/Matchmaker/` (paquete Multiplayer Services
no disponible, o versión del Deployment package sin soporte de Matchmaker todavía), configurar a mano
en el [Dashboard de UGS](https://cloud.unity.com/) → proyecto `tttxo` → **Matchmaker**:

1. **Queues → Create queue**: nombre `quickmatch-queue`, `maxPlayersPerTicket = 1`.
2. Dentro de la queue, **Add pool**: nombre `quickmatch-pool`.
   - **Match hosting**: `Match ID` (serverless/P2P - no Multiplay, no servidor dedicado).
   - **Backfill**: deshabilitado.
   - **Ticket timeout**: `180` segundos (riesgo #3 - TTL de ticket).
   - **Team definition**: 1 equipo (nombre libre, ej. "Side"), `teamCount min/max = 2`,
     `playerCount min/max = 1` (2 equipos de 1 jugador = partida 1v1).
   - **Match rules**: una regla `Equality` sobre `Players.CustomData.board_size` (sin regla de
     habilidad/MMR - Quickmatch acepta cualquier tamaño de tablero, solo exige que ambos tickets
     pidan el mismo tamaño).
3. **Environment config**: `defaultQueueName = quickmatch-queue`.
4. Guardar y publicar en el environment correspondiente (`production`/`development` según corresponda).

El contenido exacto que debería terminar viendo el Dashboard es el de
`Assets/Matchmaker/QuickmatchQueue.mmq` y `MatchmakerEnvironmentConfig.mme` - úsalos como referencia
campo por campo si el editor visual del Dashboard difiere en nombres.

## Ranked (Milestone 5)

`Docs/design-doc.md` sección 6 completa. Reutiliza `CreateMatch`/`PlayMove`/`GetMatchState`/
`SweepAbandonedMatches` sin funciones nuevas - toda la lógica Ranked-específica vive en
`RankedMatchSupport.cs` y los stores `Ranked*.cs`, invocados desde ramas `Mode == "ranked"` dentro de
esas 4 funciones (ver `MatchFunctions.cs`).

### Serie de 2 partidas (Ranked 3x3) - el modelo elegido

Respuesta a `Docs/design-doc.md` sección 6.8 pregunta 1: **registro de serie que envuelve hasta 2
matchIds** (`RankedSeriesRecord`, Cloud Save Custom Data, key = `seriesId`), NO un array `games[]`
embebido. Cada partida de la serie sigue siendo un `MatchStateRecord` **completamente ordinario**,
creado/guardado/reproducido por el mismo `MatchStateStore`/`MatchIndexStore` que ya usa Quickmatch - la
serie solo agrega 3 campos nuevos a `MatchStateRecord` (`RankedSeriesId`, `RankedSeriesGameIndex`,
`RankedMmrBeforeByPlayerId`) para que una partida "sepa" a qué serie pertenece.

Por qué el wrapper y no `games[]` embebido: `MatchStateStore.Replay`/`PlayMove` son la garantía
anti-cheat central de todo el módulo ("el cliente nunca decide quién ganó" -
`Docs/01-Directrices-Proyecto.md`) precisamente porque son funciones **puras** sobre una lista plana de
movimientos. Embeber 2 partidas dentro de un mismo registro habría obligado a reescribir esas dos
funciones para operar sobre un sub-objeto seleccionado dinámicamente - mucho más invasivo y con más
superficie para una regresión sutil en la validación de movimientos, contra el principio de este archivo
de "priorizar soluciones simples y consistentes con la arquitectura existente".

Flujo:

1. `CreateMatch(boardSize: 3, mode: "ranked", ticketId, handoffId)`: primer caller crea un
   `RankedSeriesRecord` de 1 jugador ("waiting", **sin** `MatchStateRecord` todavía - no hay nada que
   indexar/barrer aún) y publica el mismo puntero de handoff que ya usan Quickmatch/Ranked 6x6
   (`MatchStateStore.SaveHandoffPointerAsync`, reutilizado tal cual - es agnóstico de qué string
   apunta). Segundo caller completa el roster, **snapshotea el MMR de ambos** (colapsando antes
   cualquier decay/rollover pendiente - ver "Decay y rollover perezosos" abajo) y crea game 1
   (`RankedSeriesStore.CompleteRosterAndCreateGame1Async`), indexado igual que cualquier partida online
   (`MatchIndexStore.AddMatchAsync`).
2. **Contrato de cliente importante**: mientras la serie espera al segundo jugador, `MatchStateDto.MatchId`
   viene **vacío** (no existe partida aún) - a diferencia de Quickmatch/Ranked 6x6, donde el "waiting"
   SÍ tiene un `matchId` real para hacer `GetMatchState`-polling. Un cliente Ranked 3x3 en espera debe
   **volver a invocar `CreateMatch`** (ya idempotente) hasta que `IsWaitingForOpponent` sea `false`, no
   `GetMatchState`. Esto queda pendiente de implementar en el cliente (ver reporte final de esta tarea).
3. Game 1 termina (por jugada real): `PlayMove` detecta `RankedSeriesGameIndex == 1` y crea game 2
   automáticamente (`RankedSeriesStore.CreateGame2Async`) - **sin** nuevo ticket de Matchmaker, ambos
   jugadores ya son conocidos. Símbolos **invertidos** respecto de game 1 (quien fue O ahora es X y
   empieza - `Docs/design-doc.md` sección 6.1). El DTO devuelto al que movió incluye `RankedNextMatchId`;
   el rival recibe el push Wire de siempre sobre game 1 y, al volver a pedir `GetMatchState(game1)`, ve
   `RankedNextMatchId` también (vía `RankedMatchSupport.EnrichDtoWithSeriesInfoAsync`) - **el cliente
   debe navegar a esa partida**, no queda implementado en la UI en esta tarea.
4. Game 2 termina: `PlayMove` calcula el resultado de la serie a partir de los resultados de **ambas**
   partidas (`RankedMatchSupport.SeriesScoreFor`) y liquida Elo/moneda/leaderboard una sola vez (ver
   "Liquidación" abajo).
5. **Abandono en cualquier punto de la serie = se pierde la serie entera**, sin importar el marcador
   parcial (`Docs/design-doc.md` sección 6.4) - tanto la resolución reactiva
   (`ResolveAbandonmentIfNeededAsync`) como la proactiva (`SweepAbandonedMatches`) cierran la serie
   completa vía `RankedMatchSupport.HandleAbandonmentAsync`/`HandleBothAbandonedAsync`, nunca solo la
   partida individual.

### Impacto sobre el índice y el barrido proactivo

**Ninguno estructural.** `MatchIndexStore` sigue indexando `matchId`s individuales exactamente igual que
hoy - cada partida de una serie (game 1, game 2) se indexa/desindexa con las mismas
`AddMatchAsync`/`RemoveMatchAsync` de siempre, sin saber que una serie existe. `SweepAbandonedMatches`
solo gana **una llamada adicional** (`RankedMatchSupport.HandleBothAbandonedAsync`) justo después de
marcar `BothPlayersAbandoned` en una partida Ranked, para además cerrar la serie dueña (si es 3x3) como
no-contest. El barrido en sí - lectura de particiones por día, límite por corrida, poda - no cambia en
absoluto.

### Timer de turno server-side (pregunta 2)

**No hacía falta un mecanismo nuevo** - se extendió el mismo camino reactivo que ya resuelve el timeout
de 3 minutos (`ResolveAbandonmentIfNeededAsync`, invocado desde `GetMatchState`/`PlayMove`). Se agregó
`MatchStateRecord.TurnStartedAtUnixSeconds` (se resetea en cada jugada válida y al completar el roster);
si `now - TurnStartedAtUnixSeconds` excede `RANKED_CONFIG.turnTimeoutSecondsBoard{3,6}` (20s/30s), se
resuelve como abandono del jugador **a quien le tocaba mover** (no necesariamente el caller ni "el
rival" - se deriva de `match.CurrentPlayer`), con la misma liquidación completa de MMR que cualquier
abandono. **Límite aceptado, documentado**: como el resto de este módulo, esto es reactivo, no un cron -
si NINGÚN cliente vuelve a llamar `GetMatchState`/`PlayMove` tras vencer el timer, el turno vencido queda
sin resolver hasta que el barrido proactivo de 3 minutos lo alcance (o alguien vuelva). En la práctica,
mientras el rival esté esperando (polling normal o push Wire), la resolución es casi inmediata.

### Matchmaker - relajaciones de MMR (pregunta 3)

**Sí, el schema `.mmq` lo soporta**, verificado contra
`https://ugs-config-schemas.unity3d.com/v1/matchmaker/matchmaker-queue.schema.json` (no solo contra
documentación): una `Rule` de `type: "Difference"` sobre `Players.CustomData.mmr`, con `relaxations[]` de
tipo `"ReferenceControl.Replace"` (reemplaza el valor de referencia - la diferencia máxima permitida - a
los `atSeconds` indicados) y `"RuleControl.Disable"` (desactiva la regla por completo a partir de
`atSeconds`). `Assets/Matchmaker/RankedQueue.mmq` implementa exactamente la curva de
`Docs/design-doc.md` sección 6.2: ±100 inicial, reemplazado por ±200/±350/±600 a los 10/20/30s, regla
deshabilitada del todo a los 45s (fase "sin restricción de MMR", NO un ±5000 aproximado - el schema
permite deshabilitar la regla entera, así que no hace falta el equivalente práctico que la pregunta
contemplaba como plan B). Cada relajación especifica `ageType` (`Youngest`/`Oldest`/`Average` - qué
antigüedad del/de los ticket(s) del grupo dispara la relajación); se usó `"Oldest"` para que la curva se
mida por el ticket que más tiempo lleva esperando dentro del grupo evaluado, no por el más reciente -
**válido pero no verificado contra un match real** (este task no deploya), confirmar en development que
el comportamiento observado coincide con la intención antes de confiar en él para el lanzamiento.

### Matchmaker - evitar el rival anterior (pregunta 4)

**No es expresable** con el schema actual: toda `Rule.reference` es un valor literal (string/number/array
fijo), no una referencia dinámica al atributo de OTRO ticket del grupo - no hay forma de decir "el
`last_rival_id` de este ticket debe ser distinto del `playerId` del otro ticket candidato". Se **descarta**
sin bloquear nada, tal como preveía el propio design-doc (la amortiguación por rival repetido de la
sección 6.3 ya neutraliza el incentivo). No implementado.

### Leaderboards (preguntas 5 y 6)

Ver `Assets/Leaderboards/README.md` para el detalle completo (dos leaderboards fijos con reset nativo,
`UpdateType: keepLatest`, sincronización obligatoria con `RANKED_CONFIG`). Resumen:

- **Top N desde Cloud Code**: sí, `IGameApiClient.Leaderboards.GetLeaderboardScoresAsync`/
  `GetLeaderboardVersionScoresAsync` (para el top de una temporada ya cerrada/archivada), con
  `context.ServiceToken` - confirmado soportado por
  `https://docs.unity.com/en-us/cloud-code/scripts/how-to-guides/token-support.md` ("Service token
  support" lista Leaderboards explícitamente bajo "UGS Client APIs"). Ver `RankedLeaderboardStore.cs`.
- **IDs por temporada**: NO hacen falta - reset nativo con archivado (`ResetConfig`), ver
  `Assets/Leaderboards/README.md`.
- **Desempate**: no configurable en el schema `.lb` (solo hay `TieringConfig`, que son bandas de tier, no
  política de desempate de rank). Se acepta el default del servicio, tal como preveía el design-doc.

### Ledger por rival con discriminador (pregunta 7)

**Sí, trivial, sin migración**: `RankedRewardStore.cs` usa una clave completamente nueva
(`rankedWin_{rivalId}_{boardSize}_{day}`), sin tocar ni leer nunca las claves `rivalWin_{rivalId}_{day}`
que `OnlineRewardStore` ya usa para el tope de moneda (que Ranked sigue compartiendo sin fork, per
`Docs/design-doc.md` sección 6.3). Son dos contadores distintos con dos formatos de clave distintos -
nunca hubo dato viejo que migrar porque la clave nueva no existía antes.

### Decay y rollover perezosos (preguntas 8 y 9)

- **Decay (pregunta 8)**: se aplica en `RankedProfileStore.TouchAsync`, invocado en las escrituras
  naturales (liquidación de una unidad Ranked, roster de una serie completándose) - **deliberadamente
  NO en cada lectura ordinaria de `profile`** (el `profile` client-writable ni siquiera es el mismo Cloud
  Save item, ver "Corrección de arquitectura" abajo). Ahora también corre en `GetRankedProfile`
  (`RankedQueryFunctions.cs`, ver "Ranked (Milestone 5)" -> "Lecturas" arriba), invocada al abrir Perfil
  y antes de encolar en Ranked - exactamente el "o mejor aplicarlo solo en escritura y al abrir el
  leaderboard/perfil" que esta pregunta contemplaba como alternativa, cerrado.
- **Rollover (pregunta 9)**: sí, idempotente sin cron global - `TouchAsync` compara
  `record.SeasonId` contra `RankedSeasonCalculator.CurrentSeasonId(...)` y, si divergen, aplica el soft
  reset + calcula el premio de tier pendiente (elegible solo si completó colocación) en la MISMA
  escritura, de una sola vez. El cálculo de Top 100 no depende de que el rollover del jugador sea
  puntual: lee la versión **archivada** más reciente del leaderboard (que ya quedó fija en el momento en
  que el propio leaderboard reseteó, según su `ResetConfig`), así que da el mismo resultado sin importar
  cuánto tiempo después vuelva el jugador. **Requisito real, no resuelto por la plataforma**: `RANKED_CONFIG`
  (Remote Config) y `ResetConfig` de los `.lb` (Leaderboards) son dos superficies de deploy
  independientes que deben fijarse a la MISMA fecha/duración a mano - ver
  `Assets/Leaderboards/README.md` "Sincronización obligatoria".

### Cuota/costo de Matchmaker con una segunda cola (pregunta 10)

Según `https://docs.unity.com/en-us/matchmaker/matchmaker-rate-limits.md`: el límite de config-as-code es
**10 colas** y **10 pools por cola** - una segunda cola (`ranked-queue`, van 2 de 10) no se acerca a ese
límite, ni las 25 relajaciones por regla (usamos 4). Los rate limits de request (1 ticket/seg por
jugador autenticado) tampoco cambian por tener una cola más. Lo que este documento **no** cubre es el
costo/cuota **comercial** (MAU o tickets facturados según el plan de UGS contratado) - eso es una
pregunta de facturación sobre el plan real del proyecto, no de config-as-code, y sigue siendo el riesgo
#5 de la arquitectura sin resolver: **estimar volumen esperado antes de habilitar Ranked en producción**,
tal como ya recomendaba `Docs/03-Arquitectura-UGS-TicTacToe.md`.

### Corrección de arquitectura no pedida explícitamente

El MMR Ranked **NO vive en el mismo item de Cloud Save `profile`** que `Docs/design-doc.md` sección 6.1
sugiere literalmente ("En `profile` de Cloud Save... un sub-objeto `ranked`"), sino en un item nuevo,
`rankedProfile` - ver `RankedProfileSaveData` en `Dtos.cs`. Motivo: `profile` YA es escrito directamente
por el cliente (`Assets/Scripts/Game/Services/PlayerDataService.cs`'s `ProfileSaveData`/
`SaveAfterMatchAsync`, con un shape que no tiene ningún campo `ranked`) al final de **cada partida 1P o
local** - de haber usado la misma clave, el próximo `SaveAsync` del cliente después de jugar 1P habría
sobreescrito silenciosamente el MMR que Cloud Code acababa de escribir. `rankedProfile` nunca lo toca el
cliente (solo Cloud Code) - cierra el mismo tipo de problema que
`Docs/01-Directrices-Proyecto.md#Seguridad / anti-cheat` prohíbe en general ("el cliente nunca decide"),
aunque en este caso hubiera sido un bug de sobreescritura accidental, no un intento de trampa. Reportado
para que `docs-changelog` lo registre como corrección técnica.

### Liquidación (Elo + moneda + leaderboard, una sola vez por unidad)

`RankedMatchSupport.SettleCoreAsync` es el único punto que liquida una unidad Ranked (serie 3x3 o partida
6x6), llamado desde 3 caminos - jugada real que termina la unidad, abandono reactivo, o el "no contest"
del barrido proactivo (que NO llama a `SettleCoreAsync`, ver `HandleBothAbandonedAsync` - "ΔMMR = 0" es
literalmente no tocar el Elo, no un Elo que da 0). Por jugador: K variable por colocación/MMR, `F` (solo
6x6), amortiguación por rival repetido (`RankedRewardStore`, ledger separado del de moneda), piso 500,
redondeo medio-hacia-afuera con el forzado ±1 en resultados decisivos - todo en `RankedEloCalculator.cs`,
pura y testeable. La recompensa de moneda reutiliza **sin fork** los topes de `OnlineRewardStore`/
`MatchFunctions.ApplyDailyCapsAndCreditAsync` ya existentes; el premio de temporada (`GrantPendingSeasonRewardIfAnyAsync`)
los **evita a propósito** (`Docs/design-doc.md` sección 6.3: "exenta de topes").

### Lecturas: `GetRankedProfile` / `GetRankedLeaderboard`

Cierran los dos bloqueos de cliente que quedaban abiertos (ver "Lo que esta tarea NO implementó" más
abajo, ahora reducida): no había forma de leer MMR/tier/colocación ni el leaderboard sin liquidar una
unidad Ranked jugando. Ambas funciones son **lecturas puras** en `RankedQueryFunctions.cs` - no
liquidan MMR, no escriben el leaderboard, no tocan `RankedSettled`/series - solo reutilizan los mismos
stores/calculadoras que la liquidación real ya usa, para que el número mostrado nunca pueda
desincronizarse del que produciría una partida real:

- **`GetRankedProfile`**: por cada tablero Ranked (3x3, 6x6) llama `RankedProfileStore.TouchAsync` con
  `context.PlayerId` (nunca otro jugador) - el mismo punto de entrada que ya colapsa decay perezoso
  (design-doc.md sección 6.6) y rollover de temporada perezoso (sección 6.5) al liquidar una unidad, así
  que el valor devuelto es el real, no uno pre-decay. Si el touch destapa un premio de temporada
  pendiente (el jugador vuelve a abrir Perfil después de que cerró la temporada, sin haber jugado
  todavía), se otorga ahí mismo vía `RankedMatchSupport.GrantPendingSeasonRewardIfAnyAsync` - de lo
  contrario `TouchAsync` ya habría avanzado `SeasonId` y esa moneda se habría perdido en silencio.
  Barata por diseño: 2 lecturas de Cloud Save Player Data (una por tablero), escritura solo en el tick
  raro donde había decay/rollover pendiente - pensada para llamarse en cada apertura de Perfil y antes
  de encolar en Ranked (ver `MatchmakingService` abajo).
- **`GetRankedLeaderboard(boardSize, limit?)`**: `RankedLeaderboardStore.GetTopAsync` (top N, default 25,
  tope duro 50) + `GetOwnEntryAsync` (fila propia) en la misma llamada. Si el caller no completó
  colocación en ese tablero, `GetOwnEntryAsync` ya devuelve `null` (404 del Client API) y la respuesta
  queda con `hasOwnEntry = false` - el top se devuelve igual, nunca se fabrica una fila propia.

### Cliente: decisión sobre `RankedProfileCache`

Se **mantiene**, pero cambia de rol: antes era la única fuente del `mmr` publicado en el ticket de
Ranked (`MatchmakingService.StartSearchAsync`); ahora es **solo el fallback explícito** para cuando
`GetRankedProfile` falla (offline, Cloud Code inalcanzable) - el camino normal siempre intenta la
lectura autoritativa primero y refresca el cache con lo que el servidor devolvió. Se descartó
eliminarlo: el proyecto ya tiene la convención de "degradar con gracia, nunca romper el juego" en
cualquier lectura de Cloud Code (`GameConfigService`, `UiText.Localize`) y bloquear el encolado de
Ranked por completo ante un hiccup de red sería más disruptivo que usar el último valor conocido -
sobre todo porque el propio Elo del servidor ya es tolerante a un MMR de ticket levemente desactualizado
(el matchmaking por habilidad, no el rating en sí). Ver `RankedProfileCache.cs` y
`MatchmakingService.ResolveRankedMmrForTicketAsync` para el detalle.

### Lo que esta tarea NO implementó (ver reporte final de la tarea para el detalle completo)

- Emisión real de los eventos de Analytics nuevos desde el cliente (los métodos tipados ya existen en
  `GameAnalytics.cs`, pero no hay call sites todavía - no hay pantallas que los disparen).
- `ranked_placement_completed`, que requiere que el cliente cuente wins/losses/draws de colocación
  localmente (Cloud Code no lo trackea explícitamente hoy, solo `PlacementsPlayed`).
- Un aviso de decay próximo en Perfil (design-doc.md sección 6.6: "a partir del día 5... Tu MMR empieza a
  decaer en 2 días") - `GetRankedProfile` no devuelve hoy `lastRankedMatchAtUnixSeconds`/
  `decayAppliedThroughUnixSeconds`, solo lo que el Perfil pedía en esta tarea (MMR/tier/colocación). Es
  una extensión aditiva de `RankedBoardProfileDto` si se necesita después, no un bloqueo.

## Nota sobre `gameApiClient.RemoteConfigSettings`

La llamada usada en `GetGameConfigFunctions.cs` (`AssignSettingsGetAsync`) sigue el patrón
documentado en los foros/discusiones de Unity para leer Remote Config desde un módulo C#, pero la
referencia del SDK de Cloud Code en C# no está completamente publicada al momento de escribir esto.
Verificar la firma exacta (y el parseo de `settingsResult.Data.Configs.Settings`) contra el
IntelliSense del paquete `Com.Unity.Services.CloudCode.Apis` instalado antes de dar por buena la
función, y ajustar `GetGameConfigFunctions.ParseSetting` si el shape difiere.
