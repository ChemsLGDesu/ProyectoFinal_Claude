---
tags: [design-doc, game-design, milestone-1, milestone-4, milestone-5, ranked, tictactoe]
---

# Especificación de Diseño — Milestone 1: Juego Local (TicTacToe)

Documento de diseño vivo del Milestone 1. Fuente de verdad para `gameplay-programmer`: todo valor numérico que necesite el juego local está aquí; si algo no está, se pregunta, no se inventa.

> **Nota de alcance (v3).** El documento creció más allá del Milestone 1 sin cambiar de título: las **secciones 1–3 y 5** siguen siendo el Milestone 1 (juego local), la **sección 4** cubre además el **Milestone 4** (online Quickmatch) y la **sección 6** cubre el **Milestone 5** (Ranked) completo. Cada extensión está marcada en su lugar y registrada en el historial de versiones al final.

## Alcance del Milestone 1

- **Incluye**: un jugador vs IA (Fácil / Medio / Difícil / Adaptativo) y 2 jugadores locales en el mismo dispositivo. Sin UGS: toda la lógica corre en el cliente, pero **espejando exactamente las estructuras que luego vivirán en Remote Config** (`BOARD_CONFIGS`, `AI_DIFFICULTY_PARAMS`) y en Cloud Code (`GetAiMove`), para que la migración del Milestone 2+ sea mover datos, no rediseñar (ver [[03-Arquitectura-UGS-TicTacToe#Remote Config]]).
- **Excluye**: online, matchmaking, leaderboards, IAP, anuncios, localización completa, persistencia en Cloud Save. La moneda soft se acumula localmente (persistencia local simple) y migrará a Cloud Save después.
- La config de tableros e IA debe vivir en **un único asset/archivo de datos local** (no hardcodeada dispersa en el código), con la misma forma que tendrán las keys de Remote Config.

---

## 1. Configuración de tableros (espejo de `BOARD_CONFIGS`)

### Objetivo de diseño

Partidas rápidas y legibles en 3x3 (el modo estrella, < 60 s según el GDD), y tableros grandes que cambien la textura estratégica (amenazas múltiples, control de espacio) sin degenerar en partidas eternas ni en victorias automáticas del primer jugador.

### Reglas concretas

| Tamaño | k (símbolos a alinear) | Modos en Milestone 1 | Modos a futuro (referencia GDD) |
|---|---|---|---|
| 3x3 | 3 | 1 jugador (IA), 2 jugadores locales | Todos, incluido Ranked |
| 6x6 | 4 | 1 jugador (IA), 2 jugadores locales | 1P, local, online (incl. Ranked) |
| 9x9 | **5** (configurable 4–5) | 1 jugador (IA), 2 jugadores locales | 1P, local — **diferido a post-launch** |
| 11x11 | 5 | 1 jugador (IA), 2 jugadores locales | 1P, local — **diferido a post-launch** |

**9x9 y 11x11 no se ofrecen al lanzamiento (2026-08-09).** Todo lo que esta sección y las siguientes especifican para ellos —k, recompensas, params de IA, IA adaptativa por tamaño— sigue vigente e implementado; lo único que cambia es que no aparecen en el selector. El corte vive en `BOARD_CONFIGS` y en `GameConfigService.ShippedBoardSizes`, no en `BoardConfig`, justo para que volver sea config y no código. Motivo en [[02-GDD-TicTacToe#3.1 Tableros]], medición en [[09-Encuadre-Dispositivos]] (L-05).

**Decisión 9x9 → k = 5 como valor inicial.** El GDD deja 9x9 en "4-5 configurable". Se arranca con 5 por dos razones: (a) en un tablero de 81 celdas, k = 4 da una ventaja enorme al primer jugador — con tanto espacio libre es demasiado fácil fabricar una doble amenaza abierta de 3 que el rival no puede bloquear a la vez, y la partida se siente decidida en 6-8 turnos; (b) k = 5 unifica la regla con 11x11, lo que reduce la carga cognitiva del jugador (en tableros "grandes", siempre 5). El campo queda **configurable** en la estructura de datos (rango válido 4–5) para poder A/B testearlo vía Remote Config en Milestone 2+ sin nueva build.

**Quién empieza (regla de alternancia por sesión):**

- **X siempre mueve primero** (convención clásica; simplifica UI y comunicación).
- **Partida 1 de la sesión**: en 1 jugador, el humano es X (empieza el humano). En 2 jugadores locales, el Jugador 1 es X.
- **Revancha / partida siguiente contra el mismo rival en la misma sesión**: los símbolos se intercambian (quien fue O pasa a ser X y empieza). La alternancia es estricta: partida 1 → humano/J1 empieza, partida 2 → IA/J2 empieza, partida 3 → humano/J1, etc.
- Al cambiar de tamaño de tablero, de rival o de dificultad, la secuencia se reinicia (se vuelve al estado "partida 1").
- Contexto: en k-en-línea el primer jugador tiene ventaja teórica en todos los tamaños; sin alternancia, un jugador de la sesión acumula ventaja estructural partida tras partida.

### Interacciones con sistemas existentes

- La estructura local debe ser **1:1 con la futura key `BOARD_CONFIGS`** de Remote Config: por tamaño → `k`, modos disponibles. Así el Milestone 2 solo cambia el origen del dato (Remote Config vía `GetGameConfig`), no su forma (ver [[03-Arquitectura-UGS-TicTacToe#Remote Config]]).
- El selector de tamaño de la pantalla principal emite (a futuro) `board_size_selected`; en Milestone 1 el evento no se envía, pero el punto de instrumentación debe quedar identificado en el flujo.

### Riesgos

- Que 9x9 con k = 5 resulte demasiado "empatoso" o largo (ver riesgos de balance, sección 5). Por eso el valor es configurable.
- 11x11 puede superar los 3-4 minutos por partida contra IA Difícil; aceptable en Milestone 1 (es un modo de nicho según el GDD), pero se vigila con `match_finished.duration` cuando llegue Analytics.

---

## 2. Reglas de partida local

### Objetivo de diseño

Reglas deterministas, sin ambigüedad, idénticas a las que luego validará Cloud Code server-side (`PlayMove`), para que el port a servidor autoritativo sea literal.

### Reglas concretas

- **Victoria**: el primer jugador que alinea exactamente `k` símbolos propios **contiguos** en línea horizontal, vertical o diagonal (ambas diagonales) gana. La detección se evalúa inmediatamente después de cada movimiento, solo alrededor de la celda recién jugada (no hace falta reescanear todo el tablero).
- Alinear más de `k` (ej. 6 seguidos en un tablero con k = 5) **también cuenta como victoria** (regla "overline permitida"). Se elige la variante permisiva porque es la intuitiva para el público casual; la variante estricta de gomoku (overline no gana) confundiría.
- **Empate**: tablero completamente lleno sin que ningún jugador haya formado línea. En Milestone 1 **no** hay detección anticipada de empate (declarar empate cuando ya nadie puede ganar aunque queden celdas): queda explícitamente fuera de alcance como mejora post-M1, porque su costo de implementación y test en tableros grandes no se justifica todavía.
- **Movimiento válido**: solo en celda vacía, solo en el turno propio. No existe deshacer (undo) en Milestone 1.
- **Timer de turno: NO hay timer en Milestone 1.** Decisión y justificación: el GDD lo condiciona a "si aplica al modo"; en local/1P no hay rival remoto que sufra la espera, no hay abandono que resolver, y un timer agrega estados (expiración, ¿pierde turno o pierde partida?) y UI que no aportan a los pilares de "partida rápida" — una partida 3x3 ya dura < 60 s sin él. **Valores de referencia para cuando llegue online (no implementar ahora)**: 30 s por turno en 3x3 y 6x6, 45 s en 9x9 y 11x11, con expiración = derrota por abandono. Se documentan aquí solo para que la estructura de config de partida reserve el campo (`turnTimerSeconds`, valor 0 = sin timer, que es el valor de todo el Milestone 1).

### Interacciones con sistemas existentes

- La lógica de victoria/empate debe implementarse como módulo puro (sin dependencias de Unity UI) porque es exactamente lo que se moverá a Cloud Code `PlayMove` en Milestone 2+ (ver [[01-Directrices-Proyecto#Seguridad / anti-cheat]]: "el cliente nunca decide quién ganó" — en M1 sí decide, pero con el mismo código que luego será servidor).
- El resultado de partida alimenta: alternancia de inicio (sección 1), historial local para IA Adaptativa (sección 3) y recompensa de moneda soft (sección 4).

### Riesgos

- Divergencia futura entre reglas cliente (M1) y servidor (M2): se mitiga con la exigencia de módulo puro compartible.

---

## 3. IA por dificultad (espejo de `AI_DIFFICULTY_PARAMS`)

### Objetivo de diseño

Responder a la queja #1 de la competencia ("la IA es fácil de vencer", ver [[02-GDD-TicTacToe#3.2 Un jugador (vs IA)]]): **Difícil debe ser genuinamente difícil** (en 3x3, imbatible), **Fácil debe dejar ganar sin regalar descaradamente**, y **Adaptativo** debe mantener al jugador en una franja de victoria del 40–60 %. En Milestone 1 la IA corre local, pero con exactamente esta spec, que después se replica en Cloud Code (`GetAiMove`) sin cambios de diseño.

### Arquitectura de decisión común (todas las dificultades, todos los tamaños)

Cada turno de IA se resuelve en cascada; los parámetros por dificultad controlan cada paso:

1. **Ganar ya**: si existe un movimiento que completa `k` en línea → lo juega con probabilidad `pWin`; si el chequeo probabilístico falla, sigue al paso siguiente (es decir, "no lo vio").
2. **Bloquear derrota inmediata**: si el rival puede completar `k` en su próximo turno → bloquea con probabilidad `pBlock` (si hay varias amenazas, bloquea la primera detectada; una doble amenaza rival es derrota asumida).
3. **Jugada posicional**: busca la mejor jugada según el motor del tamaño de tablero (abajo). Con probabilidad `blunderChance` juega en su lugar la **segunda mejor** jugada encontrada (error sutil, no una jugada absurda).

**Generación de candidatas (obligatoria en 6x6+, opcional en 3x3)**: solo se consideran celdas vacías a **distancia Chebyshev ≤ 2** de alguna celda ocupada. Primer movimiento de la partida en tablero vacío: celda central (en 6x6, cualquiera de las 4 centrales al azar). Tope de candidatas evaluadas por nodo de búsqueda: **12** (las 12 mejor puntuadas por la heurística estática).

### Motor por tamaño de tablero

- **3x3**: minimax completo con poda alfa-beta hasta el final del árbol (9 plies máximo; trivial en cómputo). El paso 3 usa el resultado del minimax como "mejor jugada".
- **6x6, 9x9, 11x11**: minimax con poda alfa-beta a **profundidad limitada** (`searchDepth`, por dificultad) sobre las candidatas, con evaluación heurística en hojas. **Presupuesto de tiempo: 500 ms por movimiento** con iterative deepening — si se agota, se devuelve la mejor jugada de la última profundidad completada. Esto garantiza que la IA nunca congela el dispositivo y que la misma spec es viable en Cloud Code (que también tiene límites de tiempo de ejecución).

**Heurística estática (tableros 6x6+)**: se evalúan todas las ventanas de longitud `k` (horizontales, verticales, diagonales) que contienen la posición. Puntaje por ventana según cuántos símbolos propios contiene sin símbolos rivales (y viceversa, con signo negativo):

| Contenido de la ventana (solo símbolos propios + vacías) | Puntos |
|---|---|
| k−1 propios, 1 vacía (amenaza de victoria inmediata) | 10 000 |
| k−2 propios, resto vacías, **abierta por ambos extremos** | 1 500 |
| k−2 propios, resto vacías, abierta por un extremo | 400 |
| k−3 propios, resto vacías | 50 |
| 1 propio, resto vacías | 5 |

Puntaje de posición = suma de ventanas propias − **1,2 ×** suma de ventanas rivales (defender pesa un poco más que atacar: contra humanos casuales, perder por descuido se siente peor que ganar por brillantez).

### Parámetros por dificultad

| Parámetro | Fácil | Medio | Difícil |
|---|---|---|---|
| `pWin` (ve su victoria inmediata) | 0,70 | 1,00 | 1,00 |
| `pBlock` (bloquea derrota inmediata) | 0,50 | 0,90 | 1,00 |
| `blunderChance` (juega la 2ª mejor) | — (no busca) | 0,25 | **0,00 en 3x3**; 0,05 en 6x6+ |
| `searchDepth` en 3x3 | 0 (sin búsqueda) | completo | completo |
| `searchDepth` en 6x6+ | 0 (sin búsqueda) | 2 | 4 |
| Jugada posicional sin búsqueda | aleatoria uniforme entre candidatas | — | — |

- **Fácil**: sin búsqueda; solo los pasos 1-2 con sus probabilidades y luego azar entre candidatas. Resultado esperado: el jugador promedio gana ~75–85 % de las veces, pero la IA a veces gana o empata porque sí ve la mitad de las amenazas — no es un muñeco.
- **Medio**: juega "bien pero distraído": nunca deja pasar su propia victoria, bloquea casi siempre, y 1 de cada 4 jugadas posicionales es la segunda mejor. Objetivo: jugador promedio gana ~40–55 %.
- **Difícil**: en 3x3 es **matemáticamente imbatible** (lo máximo alcanzable contra ella es el empate) — este es el diferenciador de marketing frente a XO Game y debe comunicarse así en la UI. En 6x6+ un 5 % de error sutil evita que sea desmoralizante, manteniendo un win rate esperado del jugador < 15 %.

### Adaptativo

Nivel efectivo continuo `L` en escala **0–100** (0 = más fácil que Fácil, 100 = Difícil). Los parámetros se interpolan linealmente con `t = L / 100`:

- `pWin = 0,70 + 0,30 × t`
- `pBlock = 0,50 + 0,50 × t`
- `blunderChance = 0,40 × (1 − t)` (aplica solo cuando hay búsqueda, es decir L ≥ 35)
- `searchDepth` (6x6+): L < 35 → 0 (sin búsqueda, como Fácil); 35 ≤ L < 70 → 2; L ≥ 70 → 4. En 3x3: L < 35 → sin búsqueda; L ≥ 35 → minimax completo (el error lo aporta `blunderChance`).

**Regla de ajuste (historial reciente):**

- **Ventana**: últimas **10 partidas terminadas** contra Adaptativo **en el mismo tamaño de tablero** (el nivel se guarda por tamaño: jugar bien en 3x3 no debe endurecer la IA de 11x11).
- **Valor inicial**: L = 50 (equivalente a Medio). Mínimo de datos: con menos de **3** partidas en la ventana no se ajusta nada (se juega con el L actual).
- **Score del jugador en la ventana**: victoria = 1, empate = 0,5, derrota = 0. Win rate = promedio.
- **Ajuste tras cada partida terminada**:
	- win rate > 0,60 → `L += 10`
	- win rate < 0,40 → `L −= 10`
	- entre 0,40 y 0,60 → sin cambio (banda objetivo).
- **Regla de racha (reactividad)**: 3 victorias consecutivas del jugador → `L += 15` adicional inmediato; 3 derrotas consecutivas → `L −= 15`. La racha se resetea al aplicarse.
- `L` se recorta siempre al rango [0, 100]. El ajuste se aplica **entre partidas, nunca a mitad de una** (una IA que se vuelve tonta al ir ganando se percibe como tramposa).
- **Persistencia en M1**: local en el dispositivo (junto al historial de partidas). En Milestone 2+ migra a `history` de Cloud Save y el ajuste lo ejecuta Cloud Code con esta misma regla.

### Interacciones con sistemas existentes

- La estructura de parámetros debe ser 1:1 con la futura key `AI_DIFFICULTY_PARAMS` de Remote Config; el motor de IA, un módulo puro portable a Cloud Code `GetAiMove` (ver [[03-Arquitectura-UGS-TicTacToe#Cloud Code]]).
- El Adaptativo consume el historial local de resultados (el mismo que luego será `history` en Cloud Save).
- La dificultad seleccionada afecta la recompensa de moneda soft (sección 4).

### Riesgos

- Que Medio quede demasiado cerca de Fácil o de Difícil (el salto de `searchDepth` 2 → 4 en tableros grandes es grande). Vigilar con win rate por dificultad.
- Presupuesto de 500 ms insuficiente para profundidad 4 en 11x11 en móviles de gama baja: el iterative deepening lo degrada con gracia (juega con la profundidad que alcanzó), pero hay que medirlo en hardware real.
- Adaptativo oscilando de forma perceptible ("me dejó ganar dos y ahora me aplasta"): la banda muerta 0,40–0,60 y el ajuste solo entre partidas existen para amortiguarlo.

---

## 4. Recompensa de moneda soft por partida

### Objetivo de diseño

Dar un goteo de progresión que haga significativa cada partida (la economía completa —tienda, cosméticos, moneda hard— llega en milestones posteriores, ver [[02-GDD-TicTacToe#4. Progresión y economía]]). Valores deliberadamente **bajos y fáciles de subir después**: inflar una economía es fácil, desinflarla enfurece a los jugadores.

### Reglas concretas

**Base por resultado y tamaño (modo 1 jugador vs IA):**

| Tamaño | Victoria | Empate | Derrota |
|---|---|---|---|
| 3x3 | 10 | 5 | 2 |
| 6x6 | 20 | 10 | 4 |
| 9x9 | 30 | 15 | 6 |
| 11x11 | 40 | 20 | 8 |

**Multiplicador por dificultad de IA** (se aplica sobre la base y se redondea hacia abajo):

| Dificultad | Multiplicador |
|---|---|
| Fácil | ×0,5 |
| Medio | ×1,0 |
| Difícil | ×1,5 |
| Adaptativo | ×1,0 (fijo, independiente de L — si pagara más con L alto, incentivaría manipular al adaptativo) |

**2 jugadores locales**: cada jugador (ganador y perdedor) recibe un **pago plano de 2 monedas por partida terminada**, empate incluido, con **tope de 20 monedas diarias** provenientes de este modo. Razón: en el mismo dispositivo una sola persona puede jugar ambos lados; cualquier pago por victoria es autofarmeable. El pago plano premia jugar acompañado sin abrir la canilla.

**Topes anti-farming (modo 1 jugador incluido):**

- Tope diario global de moneda soft ganada jugando: **300 monedas** (reinicio a medianoche local del dispositivo en M1).
- Una partida abandonada/reiniciada antes de terminar paga **0**.

### Modo online Quickmatch (Milestone 4)

#### Objetivo de diseño

Que jugar contra un humano se sienta **más valioso que farmear a la IA** (rival más desafiante, mejor para retención), sin abrir una canilla que el rival humano permita explotar por colusión (dos cuentas que se dejan ganar). Ranked (Milestone 5) todavía no entra: aquí solo se resuelve **Quickmatch casual**, en cualquier tamaño de tablero.

#### Reglas concretas (reemplazan el valor provisional ×1,0)

**Base por resultado y tamaño (online Quickmatch):** se parte de la misma **base por tamaño** de la tabla 1P, con multiplicadores propios del modo online por resultado (redondeo hacia abajo):

| Tamaño | Victoria (base_win **×1,25**) | Empate (base_draw **×1,0**) | Derrota |
|---|---|---|---|
| 3x3 | 12 | 5 | **0** |
| 6x6 | 25 | 10 | **0** |
| 9x9 | 37 | 15 | **0** |
| 11x11 | 50 | 20 | **0** |

*(9x9 y 11x11 existen en Quickmatch porque admite cualquier tamaño; en Ranked no — ver [[02-GDD-TicTacToe#3.4 Multijugador online]].)*

**1. Multiplicador de victoria = ×1,25 sobre la base por tamaño (no ×1,0).** Decisión: se **corrige** el provisional. Un rival humano es, en promedio, más exigente que Medio (×1,0) y su valor de retención es mayor, así que la victoria online debe pagar una prima sobre el mismo tamaño en 1P vs Medio; pero se mantiene **por debajo de Difícil (×1,5)** a propósito, porque un multiplicador alto sobre un modo con rival humano es exactamente lo que hace rentable la colusión. ×1,25 es el punto medio: premia el desafío real sin financiar el farmeo. La prima se aplica **solo a la victoria**; el empate paga la base tal cual (×1,0), porque un empate no requiere "ganarle" a nadie y no es un vector de colusión rentable.

**2. Derrota online = 0 (a diferencia de 1P, que paga 2–8).** Justificación: (a) el matchmaking de Quickmatch empareja por habilidad apuntando a un win rate mediano ≈ 50 %, así que el jugador promedio ya cobra en la mitad de sus partidas — el ingreso no se seca por no pagar derrotas; (b) pagar la derrota restaura la propiedad "ambas cuentas cobran siempre" que es la que vuelve rentable a un anillo de colusión (con derrota = 0, un par que se turna las victorias gana solo `base_win` por partida, no `base_win + base_loss`). El costo es que un jugador nuevo en una mala racha cobra 0; se acepta por el punto (a) y se vigila (ver riesgos).

#### Mitigación de farming / colusión (efecto de segundo orden crítico — hay rival humano)

Tres topes acumulativos, **todos server-side** (ver responsabilidad abajo):

- **Tope diario online = 200 monedas/día** (sub-tope dedicado al modo online, por debajo del tope global de 300). Deja margen para que el 1P siga aportando y acota el techo que un anillo puede extraer por online aunque burle los otros límites. A 12/victoria en 3x3, son ~16 victorias online/día topadas; holgado para juego legítimo (mediana esperada 5–15 partidas online/día).
- **Tope por rival repetido = 3 victorias pagadas por rival cada 24 h.** A partir de la 4ª victoria **contra el mismo `playerId` rival** en la ventana de 24 h, la victoria paga **0**. El matchmaking con un pool amplio rara vez reempareja a dos desconocidos ≥ 3 veces/día; un par en colusión, en cambio, choca contra el muro tras 3 partidas → máximo 3×12 = 36 monedas/día desde ese cómplice. Sostener el farmeo exigiría muchas cuentas cómplices distintas, cada una topada, y todas bajo el sub-tope de 200 y el global de 300: el farmeo queda económicamente inútil.
- **Victoria por abandono del rival = 0.** Si la partida termina porque el rival abandona / expira su timer de turno (ver valores de timer online en sección 2: 30 s en 3x3/6x6, 45 s en 9x9/11x11), el jugador acreditado con la victoria cobra **0**, no la victoria completa. "El rival se rinde" es el vector de colusión más barato (una cuenta simplemente sale y alimenta a la otra); pagarlo 0 lo cierra. Un jugador legítimo pierde de vez en cuando la recompensa por un rival que abandona de bronca: aceptable y monitoreado.

El **tope global de 300/día** sigue aplicando por encima de todo lo anterior; las ganancias online cuentan contra él.

#### Responsabilidad server-side (Cloud Code — NO cliente)

A diferencia del M1 (donde el cliente calcula y persiste la moneda como conveniencia local), en online **hay incentivo real de trampa**, así que todo lo siguiente lo debe implementar `systems-programmer` en Cloud Code al resolver el `PlayMove` terminal (el resultado de la partida ya es autoritativo en el servidor), entregando a `currency` de Cloud Save — el cliente nunca calcula ni escribe la moneda online (ver [[01-Directrices-Proyecto#Seguridad / anti-cheat]]):

1. Cálculo de la recompensa (base × 1,25 victoria / ×1,0 empate / 0 derrota).
2. Detección de **abandono vs victoria genuina** (solo el servidor la conoce) → victoria por abandono paga 0.
3. **Tope diario online de 200** y **tope global de 300**, con reinicio a **medianoche UTC de servidor** (no la hora local del dispositivo, que es falsificable).
4. **Ledger por rival** con clave `(playerId, rivalId, día)` para el tope de 3 victorias pagadas/rival/24 h.

#### Lineamiento para Ranked (Milestone 5 — solo directriz, no especificar ahora)

Ranked **no debe heredar tal cual** la curva plana por resultado de Quickmatch. Lineamiento a desarrollar en el pase de diseño de M5: la economía de Ranked debería apoyarse en **posición de ladder / tier de MMR y recompensas de fin de temporada**, no en un goteo por-partida de moneda soft, para no duplicar el incentivo de grind y mantener el prestigio de Ranked mayormente cosmético (marcos/temporada, ver [[04-Store-Catalog-TicTacToe]]). Recomendación de arranque: moneda soft por-partida en Ranked **≤** Quickmatch (o 0 por-partida, con el grueso entregado como premio de colocación al cierre de temporada). Ranked es 3x3/6x6 únicamente, así que no hay escalado de tableros grandes que balancear. Esto **no bloquea M5**: el cálculo de Quickmatch es reutilizable como piso.

### Interacciones con sistemas existentes

- El saldo se persiste localmente en M1 y migrará al `currency` de Cloud Save; los pagos, a futuro, los otorgará Cloud Code al resolver `PlayMove` final, nunca el cliente (ver [[01-Directrices-Proyecto#Seguridad / anti-cheat]]).
- Estos valores son la referencia de entrada para el diseño de precios de [[04-Store-Catalog-TicTacToe]]: un cosmético básico debería costar el equivalente a ~2-3 días de juego activo (~500–800 monedas) — a validar cuando se diseñe la tienda.

### Riesgos

- Derrota pagada (2–8 monedas) + IA Fácil = farming de derrotas rápidas: mitigado por el multiplicador ×0,5 y el tope diario, pero vigilar la tasa de ingreso real.
- Si los precios de tienda se diseñan después sin respetar esta tasa de ingreso, la economía nace rota en una de las dos puntas.
- **Colusión online (dos cuentas que se dejan ganar)**: mitigada por derrota = 0, tope de 3 victorias/rival/24 h, sub-tope online de 200 y global de 300, y victoria-por-abandono = 0 — pero todo esto solo vale si es **server-side**; si se dejara en el cliente, es trivialmente burlable. Señal de alarma: partidas online < 15 s en masa, pares de `playerId` que juegan repetidamente entre sí, o cuentas que tocan el sub-tope de 200 vía online exclusivamente. Se mide reutilizando `soft_currency_earned` con `source` = `online_quickmatch` (nuevo valor de la fuente) cruzado con `match_finished` (`duration`, `board_size`).
- **Derrota online = 0 percibida como castigo por jugadores nuevos en mala racha**: aceptable mientras el matchmaking sostenga un win rate mediano ≈ 50 %. Señal de alarma: win rate online del segmento nuevo < 40 % sostenido, o caída de retención D1/D7 de quienes pierden sus primeras 3 partidas online. Si se dispara, la palanca es introducir un consuelo plano pequeño (p. ej. 2 monedas planas por derrota online contestada), no escalar la recompensa de victoria.

---

## 5. Riesgos de balance a vigilar y métricas futuras

En Milestone 1 no hay Analytics; esta tabla define **qué se medirá desde que exista** (eventos base ya definidos en [[02-GDD-TicTacToe#9. Métricas de éxito (Analytics)]]; los marcados como *nuevo* requieren registrarse en el Event Manager antes de emitirse, según [[01-Directrices-Proyecto]]).

| Riesgo | Señal de alarma | Métrica / evento que lo mide |
|---|---|---|
| Ventaja del primer jugador excesiva (sobre todo 6x6 con k=4 y 9x9) | Win rate de quien empieza > 60 % en 2P local u online | `match_finished` + parámetro **nuevo** `first_player_won` (bool) |
| 9x9 con k=5 demasiado largo o empatoso | Duración media > 240 s, o tasa de empate > 35 %, o abandono intra-partida alto | `match_finished` (`duration`, `turns`, `reason`, `board_size`) |
| Medio mal calibrado (indistinguible de Fácil o de Difícil) | Win rate del jugador vs Medio fuera de 40–55 % | `match_finished` + parámetro **nuevo** `ai_difficulty` |
| Difícil desmoralizante en tableros grandes | Win rate < 5 % vs Difícil en 6x6+ con caída de retención de ese segmento | `match_finished` (+`ai_difficulty`) cruzado con retención |
| Adaptativo oscilante o clavado en extremos | Distribución de L concentrada en 0 o 100; cambios de L > 25 puntos por sesión | Evento **nuevo** `ai_level_changed` (+`old_level`, `new_level`, `board_size`) |
| Farming de moneda soft (2P local o derrotas rápidas vs Fácil) | Jugadores llegando al tope diario de 300 sistemáticamente, o partidas < 15 s en masa | `match_finished` (`duration`) + evento **nuevo** `soft_currency_earned` (+`amount`, `source`) |
| Tableros grandes sin uso (¿vale la pena mantener 11x11?) | < 5 % de partidas en 9x9/11x11 tras el primer mes | `board_size_selected`, `match_started` (`board_size`) |

Los tres eventos/parámetros nuevos (`first_player_won`, `ai_difficulty` en `match_finished`, `ai_level_changed`, `soft_currency_earned`) quedan propuestos aquí como extensión del set del GDD; deben registrarse en el Event Manager del Dashboard antes de emitirse, y sus nombres finales los fija `gameplay-programmer` en inglés/snake_case conforme a [[01-Directrices-Proyecto]].

---

---

## 6. Milestone 5 — Ranked

> **Alcance de esta sección.** Especifica el modo **Ranked** completo (MMR, matchmaking, economía, abandono, temporadas, leaderboard). **Extiende** —no reemplaza— las secciones 1, 2, 4 y 5 de este documento en los puntos marcados como *"extiende sección N"*. Ranked es **3x3 y 6x6 únicamente** al lanzamiento ([[02-GDD-TicTacToe#3.4 Multijugador online]]); 9x9 y 11x11 quedan fuera y no se contemplan acá.
>
> **Estado real asumido**: Quickmatch online ya existe y es jugable (Matchmaker sin restricción de habilidad, partidas server-authoritative en Cloud Code, abandono resuelto reactiva y proactivamente). Ranked no existe: hoy la UI lo muestra con tag "Soon".

### 6.0 Objetivo de diseño del modo

Ranked es el modo donde el jugador **compite por estatus**, no por moneda. Quickmatch es el modo de ingreso económico; Ranked es el modo de reputación. Todo lo que sigue está subordinado a esa separación: si Ranked pagara más moneda que Quickmatch, Quickmatch dejaría de existir y perderíamos el modo de entrada de bajo compromiso que sostiene la retención casual.

Tres requisitos de experiencia:

1. **El número tiene que moverse.** Un ladder donde el MMR se congela no es un ladder. Esto es un problema real y no teórico en 3x3 (juego resuelto), y condiciona el diseño entero — ver 6.1.
2. **Entrar tiene que ser rápido.** Con la concurrencia de lanzamiento, esperar más de ~45 s mata el modo antes de que exista una población que lo sostenga.
3. **Perder tiene que doler, pero de forma predecible.** El jugador debe poder anticipar exactamente qué le pasa al MMR antes de tocar "Abandonar".

---

### 6.1 Sistema de MMR (Elo)

#### Objetivo de diseño

Ordenar a la población por habilidad real con un número que el jugador entienda, que converja rápido para el jugador nuevo y sea estable arriba, y que **siga moviéndose pese a que 3x3 sea un juego resuelto**.

#### Decisión estructural 1 — MMR **separado por tamaño de tablero**

**Hay dos MMR independientes: `mmr3x3` y `mmr6x6`.** No hay MMR único compartido.

Justificación (tres razones independientes, cualquiera alcanzaría):

- **Son juegos distintos, no dificultades distintas del mismo juego.** 3x3 con k=3 es un problema cerrado de 9 celdas; 6x6 con k=4 es un juego de control de espacio y amenazas dobles cuya profundidad no está agotada por ningún jugador humano. La habilidad no transfiere: un jugador puede ser perfecto en 3x3 y mediocre en 6x6, y viceversa.
- **Las distribuciones estadísticas son incompatibles.** 3x3 producirá una tasa de empate altísima y una varianza baja; 6x6 producirá resultados decisivos y varianza alta. Un MMR único mezclaría dos distribuciones con formas distintas y el número resultante no significaría nada en ninguno de los dos tableros — y, peor, el matchmaking de 6x6 emparejaría usando información obtenida en 3x3.
- **La UI ya asume la separación**: el wireframe 10 (Leaderboard) muestra filtro 3×3 / 6×6 ([[05-UI-Pantallas-TicTacToe#10. Leaderboard (Ranked)]]). Un solo MMR haría que ese filtro solo cambiara el orden de las mismas puntuaciones, lo que es directamente engañoso.

Costo aceptado: la población de ranked se parte en dos, lo que empeora la concurrencia por cola. Se compensa con la curva de relajación de 6.2, que está calibrada precisamente para esa población partida.

#### Decisión estructural 2 — la **unidad puntuada** en 3x3 es una **serie de 2 partidas**, no una partida

Este es el punto crítico de todo el milestone. **3x3 es un juego resuelto: con juego perfecto de ambos lados el resultado es siempre empate.** Consecuencias si Ranked 3x3 puntuara partidas sueltas con Elo estándar:

- Dos jugadores competentes empatan prácticamente el 100 % de las veces. Empate en Elo entre iguales = ΔMMR 0.
- El ladder se congela por encima del umbral de competencia (que en 3x3 se alcanza en horas, no en meses). El top del leaderboard queda determinado por quién tuvo suerte con rivales malos en sus primeras partidas y luego se congeló ahí.
- El jugador ve "+0 MMR" partida tras partida y abandona el modo.

**Regla adoptada — "serie" (par) de 2 partidas:**

- Una entrada a Ranked 3x3 crea una **serie de exactamente 2 partidas** contra el mismo rival. En la partida 1 empieza uno; en la partida 2, el otro. **Cada jugador empieza exactamente una vez.**
- Resultado de la serie: gana quien gane más partidas. `2-0` o `1-0 con un empate` → victoria de serie. `1-1`, `0-0 con dos empates` → **empate de serie**.
- **El Elo se actualiza una sola vez, sobre el resultado de la serie** (S = 1 / 0,5 / 0). Las partidas individuales no mueven MMR.
- La recompensa de moneda soft también se paga una sola vez por serie (ver 6.3).
- No hay "mejor de 3": con 2 partidas cada jugador empieza exactamente una vez, que es lo que neutraliza estructuralmente la ventaja del primer jugador. Un formato de 3 partidas le daría 2 de 3 salidas al mismo jugador — asimetría gratuita.

Por qué esto arregla el problema: la única forma de empatar la serie es ser perfecto **en los dos lados**. Un solo desliz como segundo jugador (que es donde el juego perfecto exige atención real) decide la serie. El ladder vuelve a moverse por habilidad y no por ruido.

Honestidad de diseño — **lo que esto NO arregla**: dos jugadores realmente perfectos siguen empatando la serie y sus MMR siguen sin moverse. Eso es una propiedad matemática de 3x3 y ningún sistema de rating puede inventar información que el juego no produce. Se acepta explícitamente y se compensa fuera del Elo, con el desempate del leaderboard y el decay por inactividad (6.6), que impiden que el top quede ocupado para siempre por quien llegó primero.

**6x6 se juega como partida única**, no como serie. Razón: una partida de 6x6 dura varios minutos; una serie duplicaría eso hasta un compromiso incompatible con el pilar de partidas rápidas. La ventaja del primer jugador en 6x6 se compensa numéricamente con el factor `F` de abajo, no estructuralmente.

#### Fórmula

**Elo estándar**, con dos modificaciones explícitas (compensación de primer movimiento y amortiguación por rival repetido). Se elige Elo y no Glicko/TrueSkill porque: (a) es trivial de implementar y auditar server-side, sin estado adicional más allá de un entero por jugador; (b) es de suma cero, lo que hace imposible generar MMR de la nada; (c) el público entiende el número. Glicko-2 aportaría un intervalo de confianza que acá se aproxima suficientemente bien con el K variable por colocación.

Para una unidad puntuada entre el jugador A y el jugador B:

1. **Ratings efectivos** (solo para calcular la expectativa, nunca para el update):
   - `Rp_A = R_A + F` si A hizo el primer movimiento, si no `Rp_A = R_A`. Ídem para B. En 3x3 `F = 0` siempre (la serie ya lo neutraliza).
2. **Expectativa**: `E_A = 1 / (1 + 10^((Rp_B − Rp_A) / 400))`, `E_B = 1 − E_A`.
3. **Score**: `S = 1` victoria, `S = 0,5` empate, `S = 0` derrota (de la **unidad puntuada**: serie en 3x3, partida en 6x6).
4. **Delta bruto**: `Δ_raw = K × (S − E)`, con `K` de la tabla de abajo (K se evalúa por jugador, puede diferir entre los dos).
5. **Amortiguación por rival repetido** (anti-boosting, ver 6.3): si `Δ_raw > 0`, `Δ = Δ_raw × D`; si `Δ_raw ≤ 0`, `Δ = Δ_raw` (**las pérdidas nunca se amortiguan**).
6. **Redondeo**: entero, medio hacia afuera del cero. **Si el resultado fue decisivo (S = 1 o S = 0), `D > 0` y el redondeo dio 0, se fuerza ±1** — una victoria nunca puede valer 0 MMR.
7. **Piso**: `MMR_final = max(500, MMR_previo + Δ)`.

`F` (compensación del primer movimiento):

| Tablero | `F` inicial | Nota |
|---|---|---|
| 3x3 | **0** | La serie de 2 con salida alternada ya lo neutraliza estructuralmente |
| 6x6 | **50** | Equivale a esperar un score medio del primer jugador de ≈ 0,571 |

**Recalibración de `F` (mensual, vía Remote Config, sin build):** `F = −400 × log10(1/s − 1)`, donde `s` es el score medio observado del primer jugador (victoria 1 / empate 0,5) en partidas Ranked del tablero durante las últimas 4 semanas. Resultado recortado a `[0, 150]` y redondeado a múltiplo de 5. El valor inicial de 50 es una estimación, no una medición: se corrige con los primeros datos de `match_finished.first_player_won` filtrados por Ranked.

#### Valores concretos

| Parámetro | Valor | Justificación |
|---|---|---|
| **MMR inicial** (jugador nuevo, por tablero) | **1000** | Ancla redonda y legible para público casual. Es también el `MMR_target` del soft reset de temporada (6.5), así que toda la matemática de reset gira alrededor del mismo número. |
| **Piso de MMR** (hard floor) | **500** | Sí hay piso: el MMR **no** puede bajar indefinidamente. 500 puntos por debajo del arranque es margen de sobra para el peor jugador real, y acotar el rango por abajo es lo que hace que las curvas de relajación de 6.2 (±100…±600) signifiquen algo. Sin piso, un jugador en espiral se aleja de toda la población y deja de ser emparejable. |
| **Techo de MMR** | **ninguno** | No hace falta: Elo de suma cero con la población acotada por abajo no diverge por arriba. |
| **Granularidad** | entero | Nunca se muestra ni se almacena con decimales. |

**K-factor: variable.** Se evalúa por jugador y por tablero (porque el contador de partidas es por tablero):

| Condición (partidas Ranked completadas en ese tablero, en la temporada actual) | MMR actual | **K** |
|---|---|---|
| 0–4 (**colocación**) | cualquiera | **40** |
| 5–29 | cualquiera | **32** |
| ≥ 30 | < 1600 | **24** |
| ≥ 30 | ≥ 1600 | **16** |

- **Por qué variable y no fijo**: un K fijo obliga a elegir entre convergencia rápida (jugador nuevo mal ubicado durante 40 partidas = frustración) y estabilidad arriba (el top del ladder oscilando ±40 por partida = el leaderboard es ruido). El K variable resuelve las dos puntas sin más estado que un contador de partidas.
- **El contador de colocación se resetea cada temporada** (6.5). Eso es lo que permite que el soft reset converja rápido: un jugador realmente de 2000 vuelve a su nivel en ~10-15 partidas y no re-grindea desde cero. Con `placementMatches = 5` la ventana de K = 40 es la mitad de la original, así que la convergencia post-reset se apoya más en el tramo de K = 32 y tarda algo más; el número vive en `RANKED_CONFIG` justamente para poder recalibrarlo si la métrica de calidad de colocación (`ranked_placement_completed`) sale mal.
- Las unidades resueltas como *"ambos abandonaron"* (6.4) **no incrementan el contador** — no son información sobre habilidad.
- **Nota de segundo orden aceptada**: con K asimétrico (nuevo 40 vs veterano 16) el intercambio deja de ser exactamente de suma cero e inyecta MMR neto en la población → deriva inflacionaria lenta. Se acepta a propósito (es el precio de la convergencia rápida) y **el soft reset de temporada es, además de una decisión de producto, el mecanismo que compacta esa deriva**. Se vigila con la métrica de MMR mediano poblacional (6.7, riesgo 4).

#### Cómo puntúa un empate — decisión explícita

- **3x3**: el empate **de partida** no puntúa nada, porque la partida no es la unidad puntuada. El **empate de serie** puntúa `S = 0,5` con Elo estándar. Entre iguales, ΔMMR = 0; contra un rival más fuerte, el empate de serie **sube** MMR, que es exactamente el comportamiento correcto y la principal vía de ascenso legítima de un jugador sólido de 3x3.
- **6x6**: empate de partida = `S = 0,5`, Elo estándar, con `F` aplicado (empatar como segundo jugador vale más que empatar como primero, que es lo justo).
- **No se usa** ninguna variante de "el empate es derrota para quien empieza" ni scores asimétricos tipo 0,45/0,55: hacen que el símbolo asignado —que el jugador no elige— determine parte del rating. Eso es ruido presentado como habilidad. La serie de 2 resuelve lo mismo sin mentirle al jugador.

#### Persistencia (contrato para `systems-programmer`)

En `profile` de Cloud Save (Player Data), un sub-objeto `ranked` indexado por tamaño de tablero:

```
ranked: {
  "3": { mmr, placementsPlayed, lastRankedMatchAtUnixSeconds, decayAppliedThroughUnixSeconds, seasonId },
  "6": { ... }
}
```

Escrito **exclusivamente desde Cloud Code**, nunca desde el cliente ([[01-Directrices-Proyecto#Seguridad / anti-cheat]]). Es el mismo `profile` que el ticket de Matchmaker usa como atributo (ver [[03-Arquitectura-UGS-TicTacToe#Cloud Save]]).

Todos los números de esta sección viven en una nueva key de Remote Config `RANKED_CONFIG` (`initialMmr`, `mmrFloor`, `kFactorTiers[]`, `firstMoveElo` por tablero, `placementMatches`, `repeatRivalDamping[]`, y los de 6.2/6.5/6.6), expuesta al cliente vía `GetGameConfig` como el resto — para poder recalibrar `F`, la curva de relajación y los umbrales de tier sin build nueva.

#### Interacciones con sistemas existentes

- **Extiende sección 2 (Reglas de partida local)**: Ranked es el primer modo que **exige timer de turno server-side**. Valores: **20 s por turno en 3x3 Ranked** (más corto que los 30 s de referencia de la sección 2, porque una serie son 2 partidas y el peor caso de 2 × 9 turnos × 30 s serían 9 min) y **30 s en 6x6 Ranked**. Expiración de turno = derrota por abandono (6.4). El resto de las reglas de partida (victoria, empate, overline permitida, movimiento válido) es idéntico y no cambia.
- **Extiende sección 1**: no cambia ningún `k` ni ninguna config de tablero. Sí agrega la noción de *unidad puntuada* (serie en 3x3, partida en 6x6), que es un concepto de Ranked, no de `BOARD_CONFIGS`.
- Reutiliza el `MatchState` y el `PlayMove` autoritativos ya existentes. La serie de 3x3 requiere una envoltura nueva sobre ellos (ver confirmaciones pendientes al final).

#### Riesgos de balance a vigilar

- Que la serie de 2 partidas de 3x3 siga produciendo demasiados empates en el cuartil superior y el ladder se congele igual. Palanca si pasa: **no** tocar el scoring de empate; en su lugar endurecer el decay por inactividad (6.6) y, si hace falta, subir el K del tramo ≥ 1600 de 16 a 24 para que el ruido residual al menos rote el top.
- Que `F = 50` en 6x6 esté mal estimado y el primer jugador quede sobre o sub-compensado. Se corrige con la fórmula de recalibración, no a ojo.
- Deriva inflacionaria por K asimétrico (ver nota arriba).

---

### 6.2 Matchmaking Ranked (resolución del riesgo #2 de la arquitectura)

#### Objetivo de diseño

Que el jugador entre a una partida **competitiva** en menos de 45 s con la concurrencia real de lanzamiento, y que nunca quede atrapado en una pantalla de espera sin salida. El fracaso de este sistema no se ve como "matchmaking lento": se ve como "Ranked está vacío" y el modo muere en la primera semana.

#### Reglas concretas — **corrige** el ejemplo de [[03-Arquitectura-UGS-TicTacToe#Resolución detallada de riesgos]] #2

La arquitectura proponía como *ejemplo* "±50 MMR cada 5 s hasta ±300 a los 30 s". **Se corrige** por esta curva:

| Tiempo en cola | Diferencia máxima de MMR permitida |
|---|---|
| 0 – 9 s | **±100** |
| 10 – 19 s | **±200** |
| 20 – 29 s | **±350** |
| 30 – 44 s | **±600** |
| 45 s en adelante | **sin restricción de MMR** (solo se exige mismo tamaño de tablero) |

Por qué se corrige, punto por punto:

- **±50 inicial es inútil con la concurrencia de lanzamiento.** Durante las primeras semanas casi toda la población estará amontonada cerca del MMR inicial de 1000 y partida en dos colas (3x3 / 6x6). ±100 abarca prácticamente la misma gente que ±50 en términos de calidad percibida, con aproximadamente el doble de probabilidad de encontrar rival en el primer intervalo. No hay costo de experiencia real: una diferencia de 100 MMR es un rival ligeramente mejor o peor, no una paliza.
- **6 escalones cada 5 s es churn de reglas sin beneficio perceptible.** 4 escalones (0/10/20/30 s) es suficiente resolución para tunear y mucho más barato de expresar y auditar en config-as-code.
- **±300 como techo a los 30 s es demasiado estrecho para el lanzamiento.** Con piso 500 y una dispersión realista de 700–1500 tras las primeras semanas, un jugador de 1500 tiene casi nadie dentro de ±300. ±600 a los 30 s es la diferencia entre "Ranked funciona con 40 jugadores concurrentes" y "Ranked no encuentra partida".
- **La fase sin restricción es deliberada y segura desde el rating**: Elo ya cobra correctamente un emparejamiento desbalanceado (el favorito gana ~0 puntos por ganar y pierde mucho por perder). El problema de un match muy desbalanceado es de *experiencia*, no de *rating* — por eso la fase sin restricción solo se alcanza **después** de que el jugador vio el techo de espera y eligió seguir esperando (abajo).

**Techo de espera: 45 s** — se **ratifica** el valor de la arquitectura (y coincide con `MATCHMAKING_CONFIG.WaitCeilingSeconds = 45`, ya implementado para Quickmatch).

Qué pasa a los 45 s: **no se cancela nada**. Se muestra una tarjeta de decisión con tres opciones explícitas, y el ticket **sigue corriendo** por defecto:

| Opción | Efecto |
|---|---|
| **Seguir esperando** (por defecto, el ticket nunca se cancela solo) | El ticket continúa hasta el TTL, ya sin restricción de MMR |
| **Pasar a Quickmatch** | Cancela el ticket de Ranked y crea uno de Quickmatch con el mismo tamaño de tablero. Sin costo ni penalización |
| **Jugar contra la IA (Difícil)** | Cancela el ticket. Partida 1P normal, **no puntúa MMR**, paga la recompensa 1P de la sección 4 |

**TTL del ticket: 180 s** (el mismo ya configurado, `MATCHMAKING_CONFIG.TicketTtlSeconds`). Al expirar: se emite `matchmaking_cancelled` con `reason = "timeout"` y se vuelve a mostrar la misma tarjeta, ahora sin la opción "seguir esperando" (hay que recrear el ticket). Nunca se deja al jugador mirando una animación infinita.

**Colocación**: los primeros 5 partidos de la temporada usan **la misma curva**. Se evaluó abrir un rango inicial más ancho para jugadores en colocación (cuyo MMR no significa nada todavía), pero requiere ancho de búsqueda por ticket o pools filtrados, y con K = 40 la convergencia ya es rápida aunque los primeros emparejamientos sean imperfectos. **No se implementa**; queda anotado como palanca si la métrica de calidad de colocación sale mal.

**Evitar re-emparejar al rival inmediatamente anterior**: deseable (reduce repetición y boosting), **no bloqueante**. Si el Matchmaker no puede expresar la regla, no se implementa: la amortiguación por rival repetido de 6.3 ya neutraliza el incentivo económico y de rating.

#### Interacciones con sistemas existentes

- Cola/pool nuevos y separados de Quickmatch (`ranked-queue` / `ranked-pool`), como ya prevé [[03-Arquitectura-UGS-TicTacToe#Matchmaker]]. Reglas del pool: `SameBoardSize` (Equality sobre `Players.CustomData.board_size`, idéntica a la de `QuickmatchQueue.mmq`) **+** una regla de diferencia sobre `Players.CustomData.mmr` con las relajaciones temporales de la tabla.
- El ticket publica **el MMR del tablero al que se está encolando** (`mmr3x3` o `mmr6x6` según el `board_size` del propio ticket), leído por el cliente del `profile` que ya sirve `GetGameConfig`/`PlayerDataService`. Un solo campo `mmr` en el ticket, no dos.
- El handoff Matchmaker → `CreateMatch` idempotente por `ticketId` no cambia (riesgo #1 ya resuelto). En 3x3, `CreateMatch` crea **la serie**, no una partida suelta.
- Aumenta el volumen de tickets (segunda cola) → toca directamente el riesgo #5 de la arquitectura (cuota/costo de Matchmaker). Debe re-estimarse antes de habilitar Ranked en producción.

#### Riesgos de balance a vigilar

- Que la fase sin restricción produzca emparejamientos de +800 MMR de diferencia con frecuencia, y el jugador débil abandone el modo tras una paliza. Señal: mediana de `mmr_gap` > 250 o win rate del favorito > 85 %.
- Que la curva sea *demasiado* generosa y Ranked se sienta igual de aleatorio que Quickmatch (perdiendo su razón de ser). Señal: mediana de `mmr_gap` alta con p95 de espera bajo → hay que **endurecer** la curva, no relajarla más.
- Que la tarjeta de los 45 s canibalice Ranked hacia Quickmatch. Señal: tasa de elección `switch_quickmatch` > 40 % de las tarjetas mostradas.

---

### 6.3 Recompensa de moneda soft en Ranked — **extiende sección 4 (v2 → v3)**

#### Objetivo de diseño

Que Ranked **no compita con Quickmatch como fuente de ingreso**. Ranked ya paga en estatus (posición, tier, cosméticos irrepetibles): si además pagara más moneda, Quickmatch se vaciaría y perderíamos el modo online de bajo compromiso — que es el que retiene al jugador casual y el que alimenta de población a Ranked. El grueso del pago de Ranked se entrega **al cierre de temporada**, no por partida.

Esto **desarrolla y ratifica** el "Lineamiento para Ranked" de la sección 4 v2 ("moneda soft por-partida en Ranked ≤ Quickmatch, con el grueso entregado como premio de colocación al cierre de temporada"). No lo contradice.

#### Reglas concretas

Ranked **no hereda** la curva de Quickmatch (base ×1,25 / ×1,0 / 0). Tiene tabla propia, expresada en valores absolutos para que no haya que componer multiplicadores sobre multiplicadores:

| Tablero | Unidad puntuada | Victoria | Empate | Derrota | Victoria por abandono del rival |
|---|---|---|---|---|---|
| **3x3** | serie de 2 partidas | **10** | **4** | **0** | **0** |
| **6x6** | partida única | **15** | **5** | **0** | **0** |

Comparación explícita con Quickmatch (sección 4 v2): 3x3 → 12/5, 6x6 → 25/10. **Ranked paga menos por unidad puntuada y bastante menos por minuto** (la serie de 3x3 dura ~2 partidas y paga 10, contra 12 por partida suelta en Quickmatch). Ese ordenamiento es el objetivo, no un efecto colateral.

- **No hay escalado por tier ni por MMR en el pago por partida.** Se evaluó y se **descarta**: pagar más por partida a los jugadores de MMR alto (a) concentra la economía en quienes menos la necesitan, (b) crea un incentivo directo a inflar MMR por vías no legítimas (boosting) que hoy solo tiene recompensa de estatus, y (c) convierte una caída de MMR en una caída de ingresos, que es la forma más rápida de que perder se sienta punitivo por partida doble. **La progresión por MMR se paga una sola vez, al cierre de temporada** (6.5), donde el boosting es mucho más caro de sostener durante 8 semanas y mucho más fácil de detectar.
- **Derrota = 0** y **victoria por abandono = 0**: idéntico a Quickmatch, por las mismas razones ya documentadas en la sección 4 (cerrar el vector de colusión "una cuenta se rinde y alimenta a la otra").
- Ranked **no existe en 9x9/11x11**, así que no hay escalado de tableros grandes que balancear.

#### Topes anti-farming — **los mismos de Quickmatch, sin fork**

Las ganancias de Ranked son ganancias online y cuentan contra **exactamente los mismos contadores** ya implementados en `OnlineRewardStore`/`OnlineRewardCalculator`:

- **Tope diario online: 200 monedas/día**, compartido entre Quickmatch y Ranked (no se suma un sub-tope aparte).
- **Tope global: 300 monedas/día**, compartido con todo lo demás.
- **Tope por rival repetido: 3 victorias pagadas por rival cada 24 h**, compartido entre modos (una victoria de Ranked y una de Quickmatch contra el mismo rival cuentan al mismo contador). Se mantiene en 3 y **no** se endurece a 2 para Ranked: en una cola Ranked de baja concurrencia, dos jugadores legítimos de MMR parecido **van a** encontrarse repetidamente, y bajar el tope castigaría el juego legítimo sin frenar el boosting real — que no es de moneda, sino de MMR (ver abajo).

#### Anti-boosting de MMR (nuevo, específico de Ranked)

El tope de moneda **no protege el rating**: un anillo de cuentas puede regalar victorias para inflar MMR sin que le importe cobrar 0. La contramedida vive en el propio Elo:

**Amortiguación por rival repetido (`D` del paso 5 de la fórmula en 6.1)**, contada por `(playerId, rivalId, boardSize)` en ventana de 24 h UTC, **solo sobre victorias**:

| Victoria Ranked nº contra el mismo rival en 24 h | `D` (multiplicador de la **ganancia** de MMR) |
|---|---|
| 1ª – 2ª | **1,00** |
| 3ª – 4ª | **0,50** |
| 5ª en adelante | **0,00** |

- **Las derrotas y los empates se aplican siempre a `D = 1,00`**, sin excepción. La asimetría es deliberada: si el tope amortiguara también las pérdidas, un anillo podría usarlo como escudo. Como está, el rating perdido nunca se puede esquivar y el rating ganado se agota, así que farmear al mismo cómplice tiene rendimiento decreciente hasta cero mientras el riesgo de perder puntos sigue intacto.
- Reutiliza la **forma de ledger ya existente** (`OnlineRewardStore.IncrementRivalWinCountAsync`, clave `(playerId, rivalId, día UTC)`), con un discriminador extra de modo y tablero — es un contador distinto del económico, no el mismo (uno cuenta victorias *pagadas*, este cuenta victorias *puntuadas*).

#### Recompensa de temporada — exenta de topes

El premio de cierre de temporada (6.5) es un **otorgamiento**, no una ganancia por partida: **no cuenta contra el tope de 200 online ni contra el de 300 global** (si contara, un jugador de Diamante cobraría su premio en fracciones a lo largo de 4 días, lo que es absurdo). Se registra igualmente con `soft_currency_earned` con `source = "ranked_season_reward"` para que aparezca en el balance económico.

#### Interacciones con sistemas existentes

- El punto de extensión marcado en `OnlineRewardCalculator` se resuelve así: **una función de recompensa Ranked propia** con la tabla de arriba (no un multiplicador sobre `ComputeRawReward`), reutilizando sin cambios el conjunto de topes (`OnlineDailyCap`, `GlobalDailyCap`, `MaxPaidWinsPerRivalPerDay`) y el ledger de `OnlineRewardStore`. La tabla es lo bastante chica (4 números) como para que expresarla directamente sea más claro y auditable que derivarla de la base 1P.
- Alimenta la referencia de precios de [[04-Store-Catalog-TicTacToe]]: con Ranked activo, un jugador competitivo medio suma ~60-80 soft/día por Ranked más el premio de temporada. No cambia la recomendación de sección 4 de ~500–800 soft para un cosmético básico.

#### Riesgos de balance a vigilar

- **Ranked deja de jugarse por pagar poco.** Señal: partidas Ranked < 10 % de las partidas online totales tras 4 semanas. Palanca correcta: subir el **premio de temporada**, no el pago por partida (subir el pago por partida es lo que canibaliza Quickmatch).
- **Ranked canibaliza Quickmatch igual**, porque el estatus resulta más motivador que la moneda. Señal: partidas Ranked > 60 % de las online. No es necesariamente malo; sí obliga a revisar si el tope de 200 online/día está estrangulando el ingreso de la población que solo juega Ranked.
- **Boosting de MMR entre cuentas.** Señal: pares `(playerId, rivalId)` con > 4 encuentros Ranked en 24 h de forma sostenida, o cuentas con > 70 % de sus victorias Ranked concentradas en ≤ 2 rivales.

---

### 6.4 Abandono y desconexión en Ranked

#### Objetivo de diseño

Una regla **única, simétrica y anunciada de antemano**. El jugador debe poder leer en la pantalla de confirmación exactamente qué le va a pasar al MMR, y esa consecuencia debe ser la misma siempre.

#### Premisa asumida (dada por la arquitectura y por el estado real)

**No podemos distinguir de forma fiable un corte de red involuntario de un rage-quit server-side.** Cualquier heurística que lo intente (patrón de latencia, si iba perdiendo, hora del día) es (a) inexacta y (b) *trivialmente explotable*: en cuanto el jugador aprende que "modo avión no penaliza", el modo avión pasa a ser la forma estándar de no perder MMR. Por lo tanto, se **cierra ahora** la nota abierta de [[03-Arquitectura-UGS-TicTacToe#Resolución detallada de riesgos]] #4 ("sin penalización de ranking para el jugador inactivo si el corte de red parece involuntario — a definir con datos reales"): **no se intenta distinguir. Todo abandono se trata igual.**

#### Reglas concretas

| Caso | MMR de quien abandona | MMR de quien se queda | Moneda | Contador de colocación |
|---|---|---|---|---|
| **Abandono de uno** (inactividad > `AbandonTimeoutMinutes` = 3, o expiración de timer de turno, o "Abandonar partida" desde el menú de pausa) | **Derrota completa**: `S = 0`, K completo, `F` aplicado normalmente | **Victoria completa**: `S = 1`, K completo, `D` de rival repetido aplicado normalmente | Ganador: **0** (ya vigente). Abandonador: 0 | **Cuenta** para ambos |
| **Ambos abandonan** (barrido proactivo, hoy resuelve empate con recompensa 0) | **ΔMMR = 0** | **ΔMMR = 0** | 0 para ambos (ya vigente) | **No cuenta** para ninguno |
| **Reconexión antes de que se resuelva** (< 3 min de inactividad y sin timer de turno expirado) | — | — | — | La partida sigue normalmente; no pasó nada |

Justificaciones:

- **Abandono = derrota completa, sin excepciones.** Es la penalización más dura que es *justa*: el rival efectivamente ganó la partida. Se comunica explícitamente en la confirmación de abandono ("Salir de una partida Ranked cuenta como derrota") y en la pantalla de entrada al modo. Un jugador que pierde MMR por un túnel del subte tiene una experiencia mala, pero es una experiencia mala **honesta y poco frecuente**; la alternativa (abandono barato) produce un modo Ranked donde nadie termina las partidas que va perdiendo, que es una experiencia mala **permanente y para todos**.
- **"Ambos abandonan" = sin cambio de MMR (no-contest), no empate de Elo.** Esto es una **corrección importante** respecto de tratarlo como un empate normal: un empate de Elo entre jugadores de distinto rating **sí mueve el rating** (el favorito pierde puntos). Aplicar eso a una partida que nadie jugó significaría que un jugador que perdió la conexión *gana* MMR porque su rival, mejor rankeado, también la perdió. Es rating generado por infraestructura, no por juego. `ΔMMR = 0` para ambos es el semántico correcto ("no hubo partida") y además es la implementación más barata: el barrido proactivo no escribe rating en absoluto. La partida sigue apareciendo en el historial como empate con recompensa 0 (comportamiento actual, no se toca), simplemente no toca `ranked`.
  - Vector de colusión teórico: dos jugadores que se ponen de acuerdo en irse los dos para "anular" una partida. No hay nada que farmear (0 moneda, 0 rating) y les cuesta tiempo real; se acepta.
- **Abandono a mitad de una serie de 3x3: se pierde la serie entera, sin importar el marcador parcial.** Si el jugador que va 1-0 arriba abandona, **pierde la serie**. Sin esta regla, "voy ganando 1-0, me voy y me lo quedo" sería el exploit obvio del formato.
- **Expiración de timer de turno = abandono**, no una jugada perdida. Es coherente con la sección 2 ("expiración = derrota por abandono") y evita el estado ambiguo de "pierde el turno pero sigue jugando", que en un juego de colocación pura equivale igual a perder.

#### Sin penalización adicional al lanzamiento — y cuándo agregarla

**No hay leaver penalty más allá de la derrota de MMR** (ni sustracción extra de MMR, ni bloqueo de cola) en el lanzamiento de Ranked. Razón: sobre público móvil con redes poco fiables, apilar un castigo encima de una derrota completa de Elo produce exactamente el tipo de reseña que este proyecto está tratando de evitar. La derrota de Elo ya es la penalización máxima justa.

**Lineamiento para después (no implementar ahora)**: si `ranked_abandon_rate` supera el **8 %** de las unidades Ranked durante 2 semanas seguidas, se agrega un **cooldown de cola escalonado**, nunca sustracción extra de MMR:

| Abandonos en las últimas 24 h | Bloqueo de cola Ranked |
|---|---|
| 1º – 2º | ninguno |
| 3º | 5 min |
| 4º | 15 min |
| 5º o más | 60 min |

#### Interacciones con sistemas existentes

- Reutiliza sin cambios los dos caminos ya implementados: reactivo (`GetMatchState` → `AbandonedWinnerPlayerId`) y proactivo (`SweepAbandonedMatches` → `BothPlayersAbandoned`). Lo único nuevo es **qué hace cada camino con el MMR**, más el timer de turno server-side (que hoy no existe).
- El camino proactivo, por diseño, **no toca MMR**, así que el barrido no gana costo ni riesgo de escritura.

#### Riesgos de balance a vigilar

- Tasa de abandono alta en 6x6 (partidas largas → más abandono que en 3x3). Señal: `ranked_abandon_rate` de 6x6 al doble que el de 3x3.
- Abandono concentrado en el segundo juego de la serie de 3x3 (el jugador que perdió el primero se va). Señal: `ranked_match_abandoned` con `game_index = 2` > 60 % de los abandonos de 3x3. Si pasa, la palanca es de comunicación (dejar clarísimo que se pierde la serie entera), no de reglas.
- Percepción de injusticia por cortes reales de red. Señal: reseñas + caída de retención D7 en el segmento con ≥ 1 abandono Ranked.

---

### 6.5 Temporadas

#### Objetivo de diseño

Dar a Ranked un **ciclo con principio y final**: un motivo recurrente para volver, un premio que se cobra, y una cancha que se vuelve a nivelar cada tanto para que llegar tarde no sea llegar sin chance.

#### Decisión: **sí, hay temporadas desde el día 1 de Ranked**

El ladder **no** es permanente. Cuatro razones, cualquiera de las dos primeras alcanzaría:

1. **Ya es una dependencia de monetización.** [[04-Store-Catalog-TicTacToe#5. Personalización de perfil]] vende "marcos de perfil ligados a una temporada de Ranked pasada". Sin temporadas, ese ítem no significa nada. El concepto de temporada no es opcional: ya está vendido.
2. **Un ladder permanente es veneno para la retención de los que llegan después.** La cohorte de lanzamiento acumula MMR durante meses; el jugador que instala en el mes 4 ve un top-100 inalcanzable y no entra al modo.
3. Compacta periódicamente la deriva inflacionaria del K asimétrico (6.1).
4. Da un beat de re-enganche recurrente con **costo de contenido casi nulo** (un ícono + un marco por temporada).

#### Reglas concretas

- **Duración: 1 mes calendario, frontera el día 28 a las 00:00 UTC** (decisión del dueño, 2026-08-08; reemplaza el "8 semanas (56 días)" original). Son meses de calendario, no un número fijo de días: ningún valor en días reproduce "el 28 de cada mes" sin desfasarse. Implementado en `RankedSeasonCalculator` con `AddMonths` sobre `RANKED_CONFIG.seasonStartUnixSeconds` + `seasonDurationMonths`, y espejado en el `ResetConfig` de los dos leaderboards (`0 0 28 * *`).
  - **Consecuencia 1 del cambio de 56 días a ~30 — resuelta: `placementMatches` bajó de 10 a 5** (decisión del dueño, 2026-08-08). Era la razón por la que el diseño original había descartado explícitamente las temporadas de 4 semanas: un jugador casual (3 partidas Ranked por semana ≈ 13 al mes) apenas terminaba las 10 partidas de colocación antes del cierre y casi no le quedaba temporada para subir. Con 5 le queda la mayor parte del mes para escalar. Los efectos colaterales están anotados donde corresponde: ventana de K = 40 a la mitad (6.1), y el ataque de cuentas descartables por el premio de Bronce a mitad de costo (tabla de recompensas más abajo).
  - **Consecuencia 2 — abierta, económica.** El techo de premios por temporada (3400 soft) no cambió, pero ahora se cobra ~12 veces al año en vez de ~6, así que el ingreso anual por premios de temporada se duplica y el equivalente diario pasa de ~61 a ~112 contra un tope diario de 300.
- **La temporada 1 arranca el 2026-08-28T00:00:00Z** (`seasonStartUnixSeconds` = 1787875200). Antes de esa fecha `CurrentSeasonId` clampea a 1, así que lo jugado en la ventana previa cuenta como temporada 1 aunque el leaderboard se limpie al llegar al ancla.
- **Soft reset de MMR** al rollover, por tablero de forma independiente:

  ```
  MMR_nuevo = redondear( 1000 + (MMR_viejo − 1000) × 0,5 )
  MMR_nuevo = max(500, MMR_nuevo)
  ```

  Ejemplos: 2000 → 1500 · 1600 → 1300 · 1400 → 1200 · 1000 → 1000 · 800 → 900 · 600 → 800.

  Por qué 0,5 y por qué hacia 1000: comprimir a la mitad hacia el ancla inicial re-mezcla el top (los primeros puestos vuelven a estar en disputa) sin obligar al jugador fuerte a re-grindear desde cero, y **sube** a los jugadores del fondo, que es donde más falta hace la sensación de reinicio. Es el reset estándar de la industria y no hay razón para inventar otro.

- **El contador de colocación vuelve a 0** por tablero: las primeras **5** unidades Ranked de la temporada usan **K = 40** (eran 10 con temporadas de 8 semanas; se bajó al pasar a temporadas mensuales para que un jugador casual complete colocación y todavía le quede temporada para subir). Sigue siendo lo que hace que el soft reset converja rápido (un jugador realmente de 2000 vuelve a ~2000 en 10-15 unidades, ahora con más peso del tramo K = 32).
- **No se resetea nada más**: inventario, moneda, estadísticas de por vida, historial y niveles de la IA Adaptativa quedan intactos. Solo MMR, contador de colocación y leaderboard.

#### Recompensas de cierre de temporada

Se otorgan **al cierre**, basadas en el **MMR final** de cada tablero (no en el pico alcanzado). Por qué el final y no el pico: premiar el pico hace óptimo **dejar de jugar** apenas se toca un tier, y lo que queremos es actividad hasta el último día. El riesgo simétrico —sentarse sobre el rating las últimas semanas— lo cierra el decay por inactividad de 6.6, que impide congelar un MMR sin jugar.

Se otorgan **por tablero, de forma independiente**: un jugador que juega 3x3 y 6x6 puede cobrar los dos.

| Tier | MMR final | Moneda soft | Cosmético |
|---|---|---|---|
| **Bronce** | < 900 | 100 | — |
| **Plata** | 900 – 1099 | 200 | — |
| **Oro** | 1100 – 1299 | 400 | Ícono de perfil de la temporada |
| **Platino** | 1300 – 1499 | 700 | Ícono + **marco de perfil de la temporada** |
| **Diamante** | ≥ 1500 | 1200 | Ícono + marco + variante animada del marco |
| **Top 100 global** (adicional al tier) | — | +500 | Banner de perfil "Top 100 — Temporada N" |

- **Requisito de elegibilidad: haber completado las 5 partidas de colocación de ese tablero en la temporada.** Sin colocación completa, no hay recompensa de ningún tier. Sigue cerrando la creación de cuentas descartables para cobrar Bronce, pero **al bajar de 10 a 5 el ataque cuesta la mitad**: una cuenta nueva cobra los 200 soft de Bronce en los dos tableros con 10 partidas online reales en vez de 20, y las temporadas mensuales le dan 12 ventanas al año en vez de 6. Sigue siendo menos de un día del tope de 300/día y exige jugar contra gente real, así que se acepta; si `ranked_season_reward_granted` muestra un pico de cuentas que cobran Bronce y no vuelven, la palanca es subir el mínimo solo para el tier Bronce, no toda la colocación.
- **Techo económico de la temporada**: el máximo teórico para un jugador que hace Diamante y Top 100 en los dos tableros es `1200 + 1200 + 500 + 500 = 3400` soft por temporada. Con las temporadas mensuales del día 28 eso es ~112/día equivalente frente a un tope diario de 300 (con las 8 semanas originales eran ~61/día). Sigue sin ser una inundación, pero el ingreso anual por premios de temporada se duplicó al pasar de ~6 a ~12 temporadas por año — ver la nota de "Duración" arriba, es la consecuencia del cambio que queda pendiente de decisión. Un jugador Oro típico en un solo tablero cobra 400, ~1 día de juego activo.
- **Los cosméticos de temporada se vuelven inobtenibles al cerrar la temporada.** Eso es literalmente lo único que los hace valiosos.

#### Corrección obligatoria a [[04-Store-Catalog-TicTacToe]] (para `docs-changelog`)

El catálogo de tienda hoy incluye un SKU de ejemplo `frame_profile_gold_season1` **vendido por 300 de moneda hard**. Eso **contradice** el diseño de Ranked: vender el marco de temporada destruye el único premio intrínseco del modo competitivo, y es además el borde exacto de "pay-to-look" que roza el estatus comprado.

**Regla que se fija acá**: los marcos/íconos/banners **ganados en Ranked** son un conjunto de SKUs **cerrado y jamás vendible**, separado del conjunto vendible. La tienda **puede** vender marcos con temática estacional, pero **no** los que denotan tier o posición de ladder, y deben ser visualmente distinguibles de un vistazo. El SKU de ejemplo del catálogo debe renombrarse (p. ej. `frame_profile_gold_ornate`) para que no sugiera vínculo con la temporada 1 de Ranked.

#### Interacciones con sistemas existentes

- Requiere un `seasonId` global (config, no calculado por el cliente) accesible desde Cloud Code y expuesto por `GetGameConfig` para que la UI muestre "Temporada 2 — quedan 12 días".
- El rollover (reset + otorgamiento de premios) es un trabajo **por jugador y perezoso**, no un barrido global: la primera vez que el jugador toca `profile` con un `seasonId` viejo, Cloud Code aplica el soft reset, otorga el premio pendiente y actualiza el `seasonId`. Evita depender de un cron que recorra toda la base. El cálculo de Top 100 sí necesita una foto del leaderboard al momento del cierre (ver confirmaciones pendientes).

#### Riesgos de balance a vigilar

- **Soft reset percibido como robo** ("me sacaron 500 puntos"). Mitigación de comunicación: la pantalla de nueva temporada debe mostrar el MMR anterior, el nuevo y el premio cobrado en la misma vista. Señal: caída de partidas Ranked en la primera semana de temporada nueva > 30 % respecto de la última semana de la anterior.
- **Inflación económica por premios de temporada** si los tiers quedan demasiado accesibles. Señal: inyección mediana por jugador > 1500 soft/temporada. Palanca: mover los umbrales de MMR de los tiers (vía `RANKED_CONFIG`), no los montos.
- **Inflación de tiers por deriva de MMR**: si el MMR mediano poblacional sube temporada tras temporada, "Oro" pasa a significar cada vez menos. Los umbrales son absolutos a propósito, para que la deriva sea *visible* en la distribución de tiers y se corrija recalibrando en vez de esconderse.

---

### 6.6 Leaderboard

#### Objetivo de diseño

Que el ladder sea **legible** (el número del leaderboard es el mismo que el jugador ve en la pantalla de resultado) y **vivo** (que el top rote, aunque 3x3 sea un juego resuelto).

#### Reglas concretas

- **Puntaje publicado: el MMR crudo, entero.** Nada derivado, nada compuesto. Si la pantalla de resultado dice "+18 MMR → 1218", el leaderboard dice 1218. Cualquier transformación rompe esa correspondencia y confunde sin ganar nada.
- **Leaderboards separados por tamaño de tablero, y por temporada**: cuatro tablas vivas como máximo en cualquier momento — `ranked_3x3_s{N}`, `ranked_6x6_s{N}` (activas) y las dos de la temporada anterior en **solo lectura durante los primeros 7 días** de la temporada nueva, para que el jugador pueda ver su posición final y su premio. Después se archivan. El filtro 3×3 / 6×6 del wireframe 10 conmuta entre las dos tablas activas.
- **Elegibilidad**: un jugador aparece en el leaderboard **solo tras completar sus 5 partidas de colocación** de ese tablero en la temporada actual. Antes, la pantalla de Perfil muestra "Colocación: 3 / 5" en lugar de una posición. Evita que el #1 del primer día sea alguien con 1 partida jugada. Los dos números viajan en la respuesta del servidor (`PlacementsPlayed`/`PlacementMatchesRequired`), así que el requisito se recalibra desde Remote Config sin build nueva.
- **Desempate con MMR igual: gana quien llegó primero a ese MMR.** Esto importa especialmente en 3x3, donde el top va a empatar en puntaje por la naturaleza del juego. Si el servicio no permite configurar la política de desempate, se acepta su default y **no** se simula con un puntaje compuesto (ver confirmaciones pendientes).

#### Política de jugadores inactivos: **decay, no ocultamiento**

- **Umbral de protección**: el decay solo aplica a jugadores con **MMR > 1200**. Por debajo no hay nada que proteger y el decay solo se sentiría como castigo por no jugar.
- **Gracia**: **7 días consecutivos** sin completar una unidad Ranked en ese tablero.
- **Tasa**: **−25 MMR por cada día adicional** de inactividad a partir del día 8.
- **Piso del decay: 1200.** El decay nunca baja de ahí (y obviamente nunca del piso duro de 500).
- **Aplicación perezosa**, sin cron: se calcula al leer/escribir el `profile` del jugador, acumulando los días pendientes desde `decayAppliedThroughUnixSeconds`. Mismo criterio "sin cron por jugador" que ya usa el resto del módulo.
- El decay **no cuenta como partida** ni afecta el contador de colocación, y se emite como `ranked_mmr_changed` con `reason = "inactivity_decay"` para poder separarlo del rating ganado jugando.
- **Aviso**: a partir del día 5 de inactividad, la pantalla de Perfil muestra "Tu MMR empieza a decaer en 2 días".

Por qué decay y no ocultar al inactivo:

- Ocultar deja un leaderboard fantasma: el #1 que dejó de jugar sigue ocupando el puesto, invisible; el #2 nunca es #1.
- El decay **libera activamente el top** y crea un gancho de retorno ("estás perdiendo puntos").
- Cierra el exploit de 6.5: llegar a Diamante y dejar de jugar para proteger el premio de fin de temporada deja de funcionar.

#### Interacciones con sistemas existentes

- La escritura del leaderboard es **exclusivamente desde Cloud Code** al resolver la unidad terminal (o al aplicar decay / soft reset), nunca desde el cliente — ya es la regla de [[03-Arquitectura-UGS-TicTacToe#Leaderboards]] y de [[01-Directrices-Proyecto]].
- El nombre mostrado sale del player name de Authentication con sufijo `#1234`, ya resuelto por `SetPlayerName`.

#### Riesgos de balance a vigilar

- **Decay percibido como castigo** por el jugador que se toma dos semanas. Señal: caída de retención D14/D30 en el segmento con decay aplicado. Palancas por orden: subir la gracia de 7 a 10 días, luego bajar la tasa de 25 a 15. **No** eliminar el decay (vuelve el leaderboard fantasma).
- **Top de 3x3 congelado en el mismo puntaje** por la naturaleza resuelta del juego, con el desempate por antigüedad como único diferenciador. Señal: > 20 jugadores empatados en el puntaje máximo. Es esperable; si se vuelve absurdo, la palanca es el decay (que rota el top), no inflar el Elo.

---

### 6.7 Métricas de Analytics de Ranked — **extiende sección 5**

Todos los eventos y parámetros marcados como **nuevo** deben **registrarse en el Event Manager del Dashboard antes de emitirse**, o el pipeline los descarta ([[01-Directrices-Proyecto]]). Nombres en inglés `snake_case`.

**Extensiones a eventos existentes:**

| Evento | Cambio |
|---|---|
| `match_finished` | El overload online hoy **no emite `mode`**. Debe emitirlo, con valores `online_quickmatch` \| `ranked`. Parámetro **nuevo** en ese overload: `mode` |
| `matchmaking_wait_time` | Parámetros **nuevos**: `mode` (`quickmatch` \| `ranked`) y `mmr_gap` (diferencia absoluta de MMR del match resultante; 0 si no hubo match). Sin `mmr_gap` la curva de relajación de 6.2 no se puede tunear con datos |
| `soft_currency_earned` | Valores **nuevos** de `source`: `ranked_match`, `ranked_season_reward` |

**Eventos nuevos:**

| Evento | Parámetros | Para qué |
|---|---|---|
| `ranked_mmr_changed` | `board_size`, `mmr_before`, `mmr_after`, `delta`, `opponent_mmr`, `k_factor`, `result` (`win`/`draw`/`loss`/`no_contest`), `reason` (`match`/`season_reset`/`inactivity_decay`), `season` | Evento troncal del sistema: mide movimiento del ladder, deriva poblacional, calidad de emparejamiento y peso real del decay |
| `ranked_placement_completed` | `board_size`, `final_mmr`, `wins`, `losses`, `draws`, `season` | Calidad de la colocación: si el MMR post-colocación no predice el rendimiento posterior, el K de colocación está mal |
| `ranked_match_abandoned` | `board_size`, `role` (`abandoner`/`stayer`/`both`), `turn_index`, `game_index` (1 ó 2 en la serie de 3x3), `elapsed_seconds` | Tasa de abandono y dónde ocurre; dispara la decisión de agregar cooldown (6.4) |
| `ranked_queue_fallback_shown` | `board_size`, `waited_seconds`, `choice` (`keep_waiting`/`switch_quickmatch`/`play_ai`/`dismiss`) | Valida si el techo de 45 s y la curva de relajación están bien puestos |
| `ranked_season_reward_granted` | `season`, `board_size`, `tier`, `soft_currency`, `top100` (bool) | Inyección económica real por temporada y distribución de tiers |

**Tabla de riesgos → señal → métrica (Ranked):**

| Riesgo | Señal de alarma | Métrica |
|---|---|---|
| Ladder de 3x3 congelado pese a la serie de 2 | Tasa de empate **de serie** > 40 % en el cuartil superior de MMR, con \|Δ\| medio < 3 en ese segmento | `ranked_mmr_changed` (`delta`, `result`) segmentado por cuartil de MMR |
| Espera inaceptable con baja concurrencia | p95 de `matchmaking_wait_time.seconds` (mode=ranked) > 45 s, o tarjeta de fallback mostrada en > 25 % de las búsquedas | `matchmaking_wait_time` (+`mode`), `ranked_queue_fallback_shown` |
| Emparejamientos desbalanceados tras la relajación | Mediana de `mmr_gap` > 250, o win rate del favorito > 85 % | `matchmaking_wait_time.mmr_gap`, `ranked_mmr_changed.opponent_mmr` |
| Deriva inflacionaria de MMR por K asimétrico | MMR mediano poblacional se aleja > ±75 de 1000 dentro de una misma temporada | Agregado de `ranked_mmr_changed.mmr_after` |
| `F` mal calibrado en 6x6 | Score medio del primer jugador fuera de [0,47 ; 0,53] tras aplicar `F` | `match_finished.first_player_won` filtrado por `mode = ranked`, `board_size = 6` |
| Canibalización entre modos online | Partidas Ranked < 10 % o > 60 % de las partidas online totales | `match_started` (+`mode`) |
| Abandono/rage-quit epidémico | `ranked_match_abandoned` con rol `abandoner` > 8 % de las unidades Ranked, 2 semanas seguidas | `ranked_match_abandoned` |
| Boosting entre cuentas | Pares `(playerId, rivalId)` con > 4 encuentros Ranked/24 h sostenido; cuentas con > 70 % de sus victorias contra ≤ 2 rivales | Ledger server-side + `ranked_mmr_changed.opponent_mmr` |
| Decay percibido como castigo | Caída de retención D14/D30 en el segmento con `reason = inactivity_decay` | `ranked_mmr_changed` cruzado con retención |
| Premios de temporada inflando la economía | Inyección mediana > 1500 soft/jugador/temporada | `ranked_season_reward_granted`, `soft_currency_earned` (source=`ranked_season_reward`) |
| Soft reset ahuyentando jugadores | Partidas Ranked en la semana 1 de temporada nueva < 70 % de la última semana de la anterior | `match_started` (+`mode`) por semana de temporada |

---

### 6.8 Dependencias técnicas a confirmar con `systems-programmer`

Este diseño asume capacidades que **no verifiqué** y que pueden obligar a revisar decisiones. Ninguna es opinión: son preguntas de factibilidad.

1. **Serie de 2 partidas en 3x3** (6.1): ¿cómo se modela sobre el `MatchState` actual —una envoltura `SetState` que referencia hasta 2 `matchId`, o un array `games[]` dentro de un mismo registro—, y cómo interactúa con el índice de partidas activas y el barrido proactivo? **Es la dependencia más cara del milestone.** Si resulta inviable en el alcance de M5, avisar antes de implementar: el fallback de diseño (partida única en 3x3 con score de empate asimétrico por símbolo) es peor y necesita ratificación mía explícita, no se aplica por defecto.
2. **Timer de turno server-side** (20 s en 3x3 Ranked, 30 s en 6x6): hoy no existe; solo hay `AbandonTimeoutMinutes = 3`. Ranked lo requiere. ¿Se enforcea con el mismo mecanismo de `lastActivityAt` o hace falta uno nuevo?
3. **Matchmaker — relajaciones temporales**: ¿el schema `.mmq` soporta una regla de tipo diferencia sobre `Players.CustomData.mmr` con relajaciones por tiempo (4 escalones a 0/10/20/30 s), y se puede **desactivar la regla por completo** a los 45 s para la fase sin restricción? Si solo admite relajar hasta un valor finito, usar ±5000 como equivalente práctico.
4. **Matchmaker — evitar el rival anterior**: ¿es expresable comparar `Players.CustomData.last_rival_id` contra el `playerId` del otro ticket? Si no, se descarta (no bloquea).
5. **Leaderboards — temporadas**: ¿se pueden crear IDs de leaderboard por temporada vía config-as-code, o hay reset nativo? ¿Y se puede consultar el top 100 desde Cloud Code al cierre para calcular el premio de Top 100?
6. **Leaderboards — desempate**: ¿la política de desempate ante puntajes iguales es configurable (primero en llegar gana)? Si no lo es, se acepta el default del servicio.
7. **Ledger por rival con discriminador de modo/tablero** (6.3): ¿la clave `rivalWin_{rivalId}_{day}` de `OnlineRewardStore` puede ganar un discriminador (`rankedWin_{rivalId}_{boardSize}_{day}`) sin migración de datos existentes?
8. **Decay perezoso al leer `profile`** (6.6): ¿es aceptable la amplificación de escrituras si `profile` se lee con frecuencia, o conviene aplicarlo solo en escritura y al abrir el leaderboard?
9. **Rollover de temporada perezoso por jugador** (6.5): confirmar que se puede ejecutar reset + otorgamiento de premio de forma idempotente sin cron global.
10. **Cuota/costo de Matchmaker** con una segunda cola (riesgo #5 de la arquitectura): re-estimar antes de habilitar Ranked en producción.

---

## Historial de versiones

- **v1 (2026-07-23)**: versión inicial. No reemplaza ningún documento anterior; desarrolla en detalle las secciones 3.1, 3.2 y 3.3 del [[02-GDD-TicTacToe]] para el Milestone 1.
- **v2 (2026-07-23)**: extiende la **sección 4 (Recompensa de moneda soft)** para cubrir el **modo online Quickmatch (Milestone 4)**, ausente en v1. No modifica las reglas de 1P vs IA ni de 2P local (siguen intactas); agrega la subsección "Modo online Quickmatch" con su tabla base×resultado (victoria ×1,25 / empate ×1,0 / derrota 0), los topes anti-colusión server-side (200/día online, 3 victorias/rival/24 h, victoria-por-abandono = 0), el lineamiento para Ranked (M5) y dos riesgos nuevos. **Corrige** el valor provisional del programador (base×1,0 estilo 1P vs Medio, con derrota pagada): la victoria pasa a ×1,25 y la derrota online a 0.
- **v3 (2026-07-23)**: agrega la **sección 6 completa — Milestone 5: Ranked** (MMR/Elo, matchmaking, economía, abandono, temporadas, leaderboard, métricas). **No modifica ni invalida ninguna regla de las secciones 1–5**; las extiende en tres puntos, explícitamente marcados en el cuerpo:
	- **Extiende sección 2 (Reglas de partida)**: Ranked introduce **timer de turno server-side** —20 s en 3x3 Ranked, 30 s en 6x6 Ranked—, primer modo que lo exige. Los valores de referencia de la sección 2 (30/45 s) siguen vigentes para Quickmatch y no se tocan. Expiración = derrota por abandono.
	- **Extiende sección 4 (v2 → v3)**: la subsección 6.3 desarrolla y **ratifica** el "Lineamiento para Ranked" que v2 dejó abierto, con tabla propia de recompensa Ranked (3x3 serie: 10/4/0 · 6x6 partida: 15/5/0), reutilizando **sin fork** los topes anti-farming ya implementados de Quickmatch (200 online/día, 300 global/día, 3 victorias pagadas/rival/24 h, todos compartidos entre modos). Las tablas de Quickmatch y de 1P **no cambian**.
	- **Extiende sección 5 (métricas)**: la subsección 6.7 agrega 5 eventos nuevos (`ranked_mmr_changed`, `ranked_placement_completed`, `ranked_match_abandoned`, `ranked_queue_fallback_shown`, `ranked_season_reward_granted`) y 3 extensiones de eventos existentes (`mode` en el overload online de `match_finished`, `mode` + `mmr_gap` en `matchmaking_wait_time`, valores nuevos de `source` en `soft_currency_earned`).

	Además, **corrige/cierra decisiones abiertas en otros documentos** (para registro de `docs-changelog`):
	- **Corrige** el ejemplo de curva de relajación de [[03-Arquitectura-UGS-TicTacToe]] "Resolución detallada de riesgos" #2 (±50 MMR cada 5 s hasta ±300 a los 30 s) por ±100 / ±200 / ±350 / ±600 en escalones de 0/10/20/30 s, con fase sin restricción a los 45 s. **Ratifica** el techo de espera de 45 s de ese mismo documento.
	- **Cierra** la nota abierta de [[03-Arquitectura-UGS-TicTacToe]] riesgo #4 ("sin penalización de ranking para el jugador inactivo si el corte de red parece involuntario — a definir con datos reales"): se decide **no distinguir** corte de red de rage-quit; todo abandono es derrota completa de MMR. "Ambos abandonan" pasa a ser **no-contest** (ΔMMR = 0 para ambos), no un empate de Elo.
	- **Corrige** [[04-Store-Catalog-TicTacToe]]: el SKU de ejemplo `frame_profile_gold_season1`, vendido por moneda hard, contradice el premio de temporada de Ranked. Los cosméticos que denotan tier o posición de ladder **no son vendibles nunca**; el SKU de ejemplo debe renombrarse (p. ej. `frame_profile_gold_ornate`).

## Conexiones

- [[01-Directrices-Proyecto]]
- [[02-GDD-TicTacToe]]
- [[03-Arquitectura-UGS-TicTacToe]]
- [[04-Store-Catalog-TicTacToe]]
- [[analisis-competencia-tic-tac-toe]]

## Fuente

Derivado de [[02-GDD-TicTacToe]] (modos, tableros, pilares, IA adaptativa) y de [[03-Arquitectura-UGS-TicTacToe]] (estructura de las keys `BOARD_CONFIGS` y `AI_DIFFICULTY_PARAMS` de Remote Config, contrato de `GetAiMove`/`PlayMove` en Cloud Code), siguiendo las convenciones de proceso de [[01-Directrices-Proyecto]].
