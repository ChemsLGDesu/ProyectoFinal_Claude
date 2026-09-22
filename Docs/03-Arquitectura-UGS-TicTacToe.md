---
tags: [ugs, architecture, hub, tictactoe]
---

# Arquitectura Técnica — UGS para TicTacToe

## Visión general

El cliente Unity nunca decide quién ganó ni valida movimientos: todo pasa por Cloud Code, igual que en TCGMaster ([[Visión General UGS]]). Se reutiliza el mismo patrón de backend autoritativo, adaptado a un juego más simple pero con capacidades que TCGMaster no tuvo desde su alfa: **matchmaking real vía UGS Matchmaker** y **IAP con validación server-side**. A diferencia de TCGMaster (que resolvía el emparejamiento con una cola propia sobre Cloud Save), acá se usa el **servicio de Matchmaker de UGS**, evitando reinventar esa lógica.

```
Cliente Unity ──(tickets)──▶ UGS Matchmaker (pools/reglas Quickmatch y Ranked)
     │                              │ match encontrado
     │                              ▼
     └──(Cloud Code RPC)──▶ TicTacToeModule (C#, Cloud Code)
     ▲                                      │
     │ Wire push + FCM/APNs (móvil)         │ Remote Config (config de tableros, IA, ofertas)
     │                                      ▼
     └────────────────────────────  Remote Config
                                            │
                                      Cloud Save (estado de partida, perfil, economía)
                                            │
                                      Leaderboards (ranked ladder)
```

| Servicio UGS | Rol en TicTacToe |
|---|---|
| Authentication | Identidad del jugador + vinculación de cuenta desde el día 1 |
| Remote Config | Fuente de verdad de config de tableros, IA, ads y catálogo de tienda |
| Matchmaker | Empareja jugadores para Quickmatch y Ranked (pools y reglas propias) |
| Cloud Code | Dueño de toda la lógica de juego, IA y validación de IAP |
| Cloud Save | Persiste partidas, perfil y economía |
| Wire | Notifica al rival que algo cambió (push) |
| Leaderboards | Ladder de Ranked |
| Analytics y Diagnostics | Eventos custom + crash reporting |

## Authentication

- Sign-in anónimo desde el día 1 (`playerId`), igual que TCGMaster.
- A diferencia de TCGMaster —que empezó solo con sesión anónima local y recién en beta agregó vinculación de cuenta, tras detectar que "teléfono nuevo = progreso y compras perdidas = reembolsos"—, acá la **vinculación de cuenta se implementa desde el lanzamiento** (Unity Player Accounts + Sign in with Apple/Google). Al haber IAP real desde el día 1, no tiene sentido repetir ese mismo problema tarde.
- `SetPlayerName` espeja el nombre elegido al player name de Authentication (sufijo `#1234`) para que el leaderboard muestre nombres reales — mismo patrón que TCGMaster.

## Remote Config

Config que necesita poder ajustarse sin build nueva:

| Key | Tipo | Contenido |
|---|---|---|
| `BOARD_CONFIGS` | JSON | Tamaños de tablero disponibles por modo, símbolos a alinear por tamaño |
| `AI_DIFFICULTY_PARAMS` | JSON | Parámetros por nivel de IA, incluido el modo Adaptativo |
| `AD_FREQUENCY_CAPS` | JSON | Frecuencia máxima de intersticiales, cooldowns por sesión |
| `STORE_CATALOG` | JSON | SKUs de IAP, precios de referencia, ofertas activas |
| `LOCALIZATION_OVERRIDES` | JSON | Textos de tienda/ofertas que necesitan tuning por idioma sin nueva build |

Igual que en TCGMaster: el cliente **no** lee Remote Config directo. Cloud Code expone un único endpoint (`GetGameConfig`) para que la config sea siempre consistente con lo que el propio módulo usa para resolver partidas. La lectura directa vía SDK queda documentada como camino de escalado si el catálogo llegara a usarse también para tuning fuera de partida (segmentación, A/B de tienda).

Deploy vía config-as-code (`Assets/RemoteConfig/GameConfig.rc`), desde `Window → Deployment` en el Editor.

## Matchmaker

Se usa el **servicio de Matchmaking de UGS** (no una cola propia) para Quickmatch y Ranked:

- **Pools separados**: `quickmatch-pool` (sin restricción de habilidad, cualquier tamaño de tablero) y `ranked-pool` (solo 3x3 y 6x6, con reglas de matching por habilidad).
- **Tickets**: el cliente crea un ticket de matchmaking vía el SDK de Matchmaker al entrar a la cola, con atributos relevantes (modo, tamaño de tablero y, en Ranked, el MMR/Elo del jugador leído de `profile` en Cloud Save).
- **Reglas de Ranked**: matching por rango de habilidad (Elo/MMR) con **backfill/relajación progresiva** del rango si no aparece rival en un tiempo configurable — evita esperas eternas en horarios de baja concurrencia.
- **Handoff a Cloud Code**: cuando el Matchmaker resuelve un match, se dispara un backend hook (o el cliente hace polling del estado del ticket) que invoca `CreateMatch` en Cloud Code para generar el `MatchState` inicial. El Matchmaker solo empareja — la partida en sí sigue viviendo enteramente en Cloud Code/Cloud Save, igual que el resto del diseño.
- Configuración (pools, reglas, regiones) vía config-as-code, igual que Remote Config y Cloud Code.

## Cloud Code

Módulo único `TicTacToeModule` (C#, .NET 9), funciones marcadas `[CloudCodeFunction]`:

| Función | Rol |
|---|---|
| `CreateMatch` | Se dispara al resolverse un ticket de Matchmaker (o directo, en modo un jugador/local); crea el `MatchState` según tamaño de tablero y modo |
| `PlayMove` | Valida y aplica un movimiento, detecta victoria/empate (servidor autoritativo) |
| `GetMatchState` | Recupera el estado actual de una partida |
| `GetAiMove` | Resuelve el movimiento de la IA server-side, para que no sea inspeccionable ni moddeable desde el cliente |
| `ValidatePurchase` | Verifica el recibo de IAP contra la store correspondiente antes de otorgar moneda o cosméticos |

Cada función recibe `context`/`gameApiClient` (y `pushClient` cuando notifica por Wire) inyectados vía `ICloudCodeSetup`, mismo patrón `ModuleSetup` que TCGMaster. Deploy desde el Editor: `Window → Deployment`, tildando el `.ccmr` correspondiente.

## Cloud Save

Mismo patrón de `SetCustomItemAsync` / `GetCustomItemsAsync` que TCGMaster:

- **Estado de partida** (Custom Data, clave = `matchId`): `MatchState` completo (tablero, turno, tamaño, modo, símbolos a alinear).
- **Datos por jugador** (Player Data, uno por jugador): `profile` (incluye MMR/Elo, usado como atributo del ticket de Matchmaker), `currency`, `inventory` (skins/temas comprados), `history` (estadísticas, win rate, racha).

La cola de matchmaking ya no vive en Cloud Save: la resuelve el servicio de Matchmaker de UGS (ver sección anterior), así que el límite de falta de transacciones de Cloud Save que afectaba a TCGMaster no aplica acá para este flujo.

### Límite conocido

Una partida online abandonada por ambos jugadores no se resuelve sola porque no hay cron server-side por partida (mismo gap que TCGMaster). Acá el impacto es mayor porque afecta directamente el ranking — se evalúa como riesgo prioritario a resolver antes del lanzamiento del modo Ranked (ver riesgos abajo).

## Wire (push en tiempo real)

- Igual que TCGMaster: `IPushClient.SendPlayerMessageAsync` se invoca al final de cada `PlayMove`, con un payload liviano `{type, matchId}`.
- El cliente que recibe el push **vuelve a pedir `GetMatchState`** — no confía en el contenido del push como fuente de verdad, solo lo usa como señal de "algo cambió, refrescá".
- Wire solo vive con la app abierta: para notificar "es tu turno" en background en móvil se necesita un segundo canal, push nativas (FCM/APNs) — mismo gap que TCGMaster identificó para su propio beta móvil.

## Leaderboards

- Tabla **Ranked** para el modo competitivo (3x3 y 6x6 al lanzamiento).
- El puntaje se actualiza desde Cloud Code al resolver el `PlayMove` que termina una partida Ranked — nunca se actualiza desde el cliente.

## Analytics y Diagnostics

Mismo wrapper defensivo que TCGMaster (`Assets/Scripts/Analytics/GameAnalytics.cs`): todo en try/catch, degrada a warning si falla, y cada evento necesita también su definición en el Event Manager del Dashboard o el pipeline lo descarta.

- **Eventos base**: `match_started`, `match_finished` (+won, board_size, turns, duration, reason), `matchmaking_wait_time`.
- **Eventos propios de este proyecto** (funnel de monetización): `ad_watched` (+placement, ad_type), `ad_skipped`, `iap_purchased` (+sku, price), `iap_failed`, `board_size_selected`.
- **Cloud Diagnostics**: `CrashReportHandler.SetUserMetadata` adjunta `playerId` a cada reporte para poder correlacionar un crash con el jugador/partida, igual que TCGMaster.

### Sink local para beta (`TTTXO_LOCAL_ANALYTICS`)

Los eventos custom de UGS se facturan, y durante la beta no hace falta pagarlos. Con el símbolo de
compilación `TTTXO_LOCAL_ANALYTICS` definido, `GameAnalytics.SafeRecord` escribe cada evento a un
archivo **JSON Lines** en `Application.persistentDataPath/analytics/` y **no llama a
`AnalyticsService` en absoluto** — cero eventos facturados.

- **Es un símbolo de compilación, no un flag de runtime**, a propósito: "desenchufado para
  producción" tiene que significar que el código de escritura a disco *no está en el build*, no que
  una variable esté en `false`.
- **`LocalAnalyticsBuildGuard`** (`IPreprocessBuildWithReport`) **aborta cualquier build de release**
  que todavía tenga el símbolo. Sin eso el modo de falla es invisible: el build sale bien, el juego
  anda, y la ausencia de telemetría se descubre cuando alguien va a buscar los datos.
- **La serialización queda siempre compilada y siempre testeada** (`AnalyticsEvent.ToJsonLine`,
  `AnalyticsEventTests`). Solo el I/O va detrás del símbolo. Un JSON mal escapado no se nota: la beta
  sigue jugando y el archivo sigue creciendo, y el problema aparece al intentar parsear un mes de
  datos.
- **JSON Lines y no un array JSON** para que un append nunca tenga que reescribir ni cerrar el
  archivo: un crash a mitad de beta cuesta como mucho la última línea, no la sesión.
- **`GameManager.DeleteAllPlayerData` borra el directorio.** Los eventos en disco son datos del
  jugador; dejarlos ahí haría falsa la promesa de la pantalla de Ajustes.
- Efecto colateral útil: en el camino local **no está el bail-out de "sesión UGS no Ready"**, así que
  la beta registra eventos offline y en el Editor, donde hoy se descartan todos.

**Ojo con el build target activo**: el Editor compila con los defines del target activo (hoy
Android), no con los de Standalone. Ponerlo solo en Standalone no tiene ningún efecto y el sink
parece no funcionar.

## Localización — implicancias técnicas

- Unity Localization Package como motor de tablas de strings, con las claves de textos "vivos" (ofertas, promociones) espejadas también en `LOCALIZATION_OVERRIDES` de Remote Config para poder ajustarlas sin nueva build.
- Los 10 idiomas objetivo (ver [[02-GDD-TicTacToe#8. Localización]]) son todos de alfabeto latino y layout LTR (inglés, español, francés, alemán, portugués, italiano, indonesio, vietnamita, turco, polaco) — sin necesidad de soporte RTL ni de fuentes con alfabetos no latinos.
- Único requisito de fuente: un font asset de TextMeshPro con cobertura de caracteres latinos extendidos (tildes, diéresis, cedillas, y los diacríticos propios del turco, vietnamita y polaco). Esto es notablemente más simple que soportar RTL o CJK, y reduce el riesgo técnico de la localización a un ítem de QA de fuentes, no de layout.

## Seguridad y anti-cheat

- Todo el estado de partida y la detección de victoria se resuelve server-side, nunca en el cliente.
- La IA corre en Cloud Code, no en el cliente.
- Todo IAP se valida server-side (`ValidatePurchase`) antes de otorgar cualquier recurso — no negociable, dado que hay dinero real desde el día 1 (a diferencia de TCGMaster, que arrancó su alfa sin este requisito).

## Riesgos técnicos conocidos

| Riesgo | Detalle | Mitigación propuesta |
|---|---|---|
| Handoff Matchmaker → Cloud Code | El emparejamiento y la creación de la partida son dos pasos separados (Matchmaker resuelve el ticket, luego se llama `CreateMatch`); una falla o demora entre ambos deja al jugador con match "encontrado" pero sin `MatchState` | Definir timeout de handoff + reintento de `CreateMatch`; el cliente debe poder reintentar el polling del ticket sin crear tickets duplicados |
| Rango de habilidad en Ranked con poca concurrencia | Si hay pocos jugadores online, el matching por Elo puede demorar o emparejar rangos muy distintos tras la relajación progresiva | Ajustar la curva de backfill/relajación vía config-as-code y monitorear `matchmaking_wait_time` desde el lanzamiento |
| Tickets huérfanos | Un jugador cierra la app con un ticket activo en cola | Configurar TTL de ticket y cancelación explícita al salir de la pantalla de búsqueda |
| Partidas online abandonadas | Sin cron server-side, quedan sin resolver hasta que alguien la toque | Impacto mayor que en TCGMaster por afectar el ranking; evaluar timeout server-side o resolución al reconectar antes de lanzar Ranked |
| Costo/cuota del servicio de Matchmaker | Servicio con límites de uso y costo asociado por ticket/región, distinto del resto de UGS ya usado en TCGMaster | Validar límites del plan de UGS contratado antes de escalar Ranked a más tamaños de tablero |
| Ads/IAP mal configurados | Puede reproducir la queja #1 de toda la competencia | `AD_FREQUENCY_CAPS` vía Remote Config, QA específico de frecuencia de anuncios antes de cada release |

## Resolución detallada de riesgos

### 1. Handoff Matchmaker → Cloud Code
- `CreateMatch` se implementa **idempotente**: recibe el `ticketId` como clave de idempotencia. Si se llama dos veces para el mismo ticket (por reintento del cliente o porque ambos jugadores disparan la creación en paralelo), la segunda llamada devuelve el `MatchState` ya existente en vez de crear uno nuevo.
- El cliente hace polling del estado del ticket (`GetTicket`); al ver `status = Matched`, llama `CreateMatch` con reintento automático (backoff corto) si la llamada falla.
- Salvaguarda adicional: si un jugador nunca llega a llamar `CreateMatch` (cerró la app justo en ese instante), el otro jugador sí lo hace al hacer su propio polling — cualquiera de los dos puede disparar la creación gracias a la idempotencia, así que no depende de un único cliente.

### 2. Rango de habilidad en Ranked con poca concurrencia
- Definir la curva de relajación como config-as-code (ej. ±50 MMR cada 5s hasta un tope de ±300 MMR a los 30s), ajustable sin redeploy.
- Poner un techo de espera razonable (ej. 45s): si no hay match, ofrecer al jugador la opción explícita de pasar a Quickmatch o jugar vs IA en su lugar, en vez de dejarlo esperando indefinidamente.
- Monitorear `matchmaking_wait_time` desde el día 1 del lanzamiento de Ranked (ya está en el set de eventos de Analytics) y alertar si el p95 supera el umbral definido, para reaccionar ajustando la curva antes de que se note en reseñas.

### 3. Tickets huérfanos
- TTL de ticket configurado en el propio Matchmaker (expira solo si nadie lo cancela).
- Cancelación explícita del ticket en el `OnDisable`/`OnDestroy` de la pantalla de búsqueda de partida, y también al detectar que la app pasa a background (`OnApplicationPause`).
- Evento de Analytics `matchmaking_cancelled` (+reason: user_cancel, app_background, timeout) para poder medir cuánto abandono hay en la cola y ajustar UX si el número es alto.

### 4. Partidas online abandonadas
- Cada `PlayMove` y cada `GetMatchState` actualiza un `lastActivityAt` en el `MatchState`.
- Resolución **reactiva** (sin depender de un cron): cuando cualquiera de los dos jugadores vuelve a abrir la partida o hace polling, si el rival no tuvo actividad por más de N minutos, `GetMatchState` resuelve la partida ahí mismo (victoria por abandono para quien sigue activo, sin penalización de ranking para el jugador inactivo si el corte de red parece involuntario — a definir con datos reales).
- Si el plan de UGS contratado incluye Scheduler/Cloud Code Triggers programados, se puede sumar además una resolución **proactiva** (barrido periódico) para no depender de que alguien vuelva a abrir la app — deseable antes de escalar Ranked, no bloqueante para el lanzamiento inicial.

### 5. Costo/cuota del servicio de Matchmaker
- Antes de habilitar Ranked en producción: estimar volumen esperado de tickets/mes (based on proyección de DAU) y compararlo contra los límites del plan de UGS contratado.
- Configurar alertas de uso en el dashboard de UGS para detectar acercamiento a la cuota con margen de reacción.
- Definir un fallback de degradación controlada (mensaje "Ranked no disponible temporalmente", no un crash) si se llega a superar la cuota en algún pico.

### 6. Ads/IAP mal configurados
- Checklist de QA obligatorio antes de cada release: verificar que ningún intersticial aparece antes de la partida N configurada en `AD_FREQUENCY_CAPS`, que todo anuncio tiene botón de cierre visible, y que ninguna oferta redirige fuera del juego sin que el usuario lo haya pedido.
- Los eventos `ad_watched`/`ad_skipped` ya definidos permiten armar un dashboard de frecuencia real de anuncios por sesión y cruzarlo con `iap_failed`/desinstalaciones si UGS Analytics lo permite, para detectar el problema antes de que aparezca en reseñas (a diferencia de la competencia, que solo reaccionó después).

### Riesgos ya resueltos por decisiones previas
- **Cloud Save sin transacciones para la cola de matchmaking**: dejó de aplicar al mover el matchmaking al servicio real de UGS Matchmaker.
- **RTL / fuentes con alfabeto no latino**: dejó de aplicar al acotar la localización a los 10 idiomas de alfabeto latino (ver [[02-GDD-TicTacToe#8. Localización]]).

## Conexiones

- [[01-Directrices-Proyecto]]
- [[02-GDD-TicTacToe]]
- [[04-Store-Catalog-TicTacToe]]
- [[05-UI-Pantallas-TicTacToe]]
- Referencias de patrón (proyecto TCGMaster): [[UGS Authentication]], [[UGS Remote Config]], [[UGS Cloud Code]], [[UGS Cloud Save]], [[UGS Wire]], [[UGS Analytics y Diagnostics]], [[Visión General UGS]]

## Fuente

Adaptado del patrón arquitectónico de `Docs/CCG_POC_Design_And_Architecture.md` (proyecto TCGMaster, ver notas `UGS_*.md`) y de las decisiones de producto en `analisis-competencia-tic-tac-toe.md`.
