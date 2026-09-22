---
tags: [gdd, game-design, tictactoe]
---

# Documento de Diseño de Juego — TicTacToe (UGS)

## 1. Concepto

Tres en raya (Tic Tac Toe) multiplataforma (Android / iOS / PC vía Google Play Games), con tableros de múltiples tamaños, IA adaptativa real, multijugador local y online con ranking, y una postura de privacidad y monetización pensada para resolver el problema #1 detectado en toda la competencia grande: exceso y agresividad de la publicidad (ver [[analisis-competencia-tic-tac-toe]]).

## 2. Pilares de diseño

- **Partidas rápidas y fluidas.**
- **IA desafiante y configurable**, no trivial de vencer.
- **Privacidad como argumento de venta**, no como letra chica.
- **Monetización justa**: anuncios opcionales, IAP nunca forzado.

## 3. Modos de juego

### 3.1 Tableros

| Tamaño | Símbolos a alinear | Disponible en |
|---|---|---|
| 3x3 | 3 | Todos los modos, incluido Ranked |
| 6x6 | 4 | Un jugador, local, online (incluido Ranked) |
| 9x9 | 4-5 (configurable) | Un jugador, local — **no al lanzamiento** |
| 11x11 | 5 | Un jugador, local — **no al lanzamiento** |

**9x9 y 11x11 quedan fuera del lanzamiento (decidido el 2026-08-09), diferidos a post-launch.** No es una cancelación: las reglas, la economía y los parámetros de IA de los dos siguen especificados acá y siguen implementados. El motivo es táctil y medido, no de balance: en un teléfono de 360dp de ancho la celda de 11x11 mide ~29dp y la de 9x9 ~35dp, contra los 48dp que Android recomienda como objetivo mínimo. **El techo es aritmético** — 11 celdas en 360dp dan 32.7dp aunque el tablero ocupara el 100% del ancho —, así que no se arregla con layout; el máximo jugable en un teléfono de ese ancho son 7 celdas. Medición y cuentas en [[09-Encuadre-Dispositivos]] (L-05).

Volver a habilitarlos es reagregar sus entradas en `BOARD_CONFIGS` y en `GameConfigService.ShippedBoardSizes`, sin tocar código de juego. Lo que hay que resolver antes es **para qué dispositivo**: en tablet las dos entran cómodas, así que la pregunta abierta es si vuelven para todos, solo por encima de cierto ancho de pantalla, o con celdas desplazables.

El tablero 15x15 (visto en vapps2015) no se replica al inicio por riesgo de partidas demasiado largas y abandono; queda como candidato post-lanzamiento si hay demanda real medida por Analytics. **La misma cuenta táctil lo descarta con más razón**: 15 celdas en 360dp son 24dp por celda.

### 3.2 Un jugador (vs IA)

- Niveles: Fácil / Medio / Difícil / Adaptativo.
- El modo **Adaptativo** ajusta estilo y margen de error según el historial reciente del jugador. La IA se resuelve en Cloud Code (ver [[03-Arquitectura-UGS-TicTacToe#Cloud Code]]), no en el cliente, para que no sea trivial de inspeccionar ni moddear.
- Este es el diferenciador directo frente a XO Game (Onetap Global), cuya IA es descrita en reseñas como "fácil de vencer".

### 3.3 Multijugador local

- 2 jugadores en el mismo dispositivo, cualquier tamaño de tablero.

### 3.4 Multijugador online

- **Quickmatch** (casual, cualquier tamaño de tablero) y **Ranked** (con ladder/leaderboard).
- Ranked limitado a 3x3 y 6x6 al lanzamiento, para mantener partidas cortas y un matchmaking más simple.
- Servidor autoritativo: cada movimiento se valida en Cloud Code antes de reflejarse (ver arquitectura).

## 4. Progresión y economía

- Moneda soft (se gana jugando) + moneda hard (se compra).
- Cosméticos: temas visuales/skins de tablero y fichas (neón y otras variantes). Puramente estéticos — pay-to-look, nunca pay-to-win.
- Historial de partidas y estadísticas (win rate, racha actual).

## 5. Monetización

- **Anuncios**: recompensados y opcionales (ej. moneda extra por ver uno) + intersticiales con frecuencia limitada, con el tope configurable vía Remote Config sin necesidad de nueva build.
- **IAP**: quitar anuncios (pago único), packs de moneda hard, skins/temas individuales o en bundle.
- Regla dura, sin excepciones: ningún anuncio bloquea la pantalla sin un botón de cierre visible; nunca se redirige a otra tienda disfrazado de anuncio del juego.

## 6. Privacidad (diferenciador competitivo)

- Sign-in anónimo por defecto (ver [[03-Arquitectura-UGS-TicTacToe#Authentication]]).
- No se comparte información con terceros más allá de lo estrictamente necesario para operar ads/IAP, declarado explícitamente en la ficha de Play Store — siguiendo el ejemplo de HDuo Fun Games, el único competidor analizado que no comparte datos con terceros.
- Borrado de cuenta y datos accesible desde el menú de ajustes, sin fricción.

## 7. UX/UI

- Estética neón/glow como opción visual, priorizando siempre la velocidad de partida sobre el adorno.
- Una partida de 3x3 debe poder jugarse en menos de 60 segundos, incluyendo cualquier transición de anuncio.
- Selector de tamaño de tablero y modo en la pantalla principal: nunca más de 2 taps para empezar a jugar.

## 8. Localización

Cobertura inicial: **10 idiomas**, todos de alfabeto latino, elegidos para evitar la complejidad de otro tipo de layout (RTL) o de fuentes con otro alfabeto (CJK, devanagari, cirílico, etc.), priorizando mercados grandes de juegos casuales/móviles. Textos gestionados vía Unity Localization, con las tablas que necesitan tuning frecuente (ofertas de tienda, mensajes promocionales) sincronizadas también con Remote Config para poder ajustarlas sin nueva build:

1. Inglés (en)
2. Español (es)
3. Francés (fr)
4. Alemán (de)
5. Portugués (pt-BR / pt-PT)
6. Italiano (it)
7. Indonesio (id)
8. Vietnamita (vi)
9. Turco (tr)
10. Polaco (pl)

Al ser todos de alfabeto latino y layout LTR, no hay riesgo de RTL ni de fuentes con glifos no latinos: alcanza con un font asset de TextMeshPro con cobertura de caracteres extendidos latinos (tildes, diéresis, cedillas, caracteres propios del turco/vietnamita/polaco), sin necesidad de configuración especial de layout. Queda simplificado el riesgo técnico que antes existía por árabe/urdu (ver [[03-Arquitectura-UGS-TicTacToe#Localización — implicancias técnicas]]).

## 9. Métricas de éxito (Analytics)

Eventos base (heredados del patrón de TCGMaster) + eventos propios del funnel de monetización de este proyecto:

`match_started`, `match_finished` (+won, board_size, turns, duration, reason), `matchmaking_wait_time`, `ad_watched` (+placement, ad_type), `ad_skipped`, `iap_purchased` (+sku, price), `iap_failed`, `board_size_selected`.

## 10. Resumen de posicionamiento frente a la competencia

| Oportunidad detectada | Cómo se resuelve acá |
|---|---|
| Exceso de anuncios agresivos (queja #1 en las 3 apps grandes) | Frecuencia limitada + siempre cerrable + recompensados opcionales |
| IA fácil de vencer (XO Game) | IA Adaptativa real, resuelta server-side |
| Minijuegos ajenos que generan confusión (Arclite) | No se agregan minijuegos no relacionados |
| Solo 1 de 4 competidores no comparte datos con terceros | Postura de privacidad estricta desde el día 1 |
| Ninguno resolvió el problema de ads pese a responder reseñas | Cambios reales de producto, no solo respuesta pública |

## Conexiones

- [[01-Directrices-Proyecto]]
- [[03-Arquitectura-UGS-TicTacToe]]
- [[04-Store-Catalog-TicTacToe]]
- [[05-UI-Pantallas-TicTacToe]]
- [[analisis-competencia-tic-tac-toe]]

## Fuente

Elaborado a partir de `analisis-competencia-tic-tac-toe.md` y de los patrones de diseño de `Docs/CCG_POC_Design_And_Architecture.md` (proyecto TCGMaster).
