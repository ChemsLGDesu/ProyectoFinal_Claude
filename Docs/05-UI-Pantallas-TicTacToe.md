---
tags: [ui, ux, design, tictactoe]
---

# UI — Pantallas de TicTacToe

## Nota técnica de estructura

Estas "pantallas" no son necesariamente Unity Scenes separadas: para evitar recargas lentas y no perder la sesión de UGS (Auth, ticket de Matchmaker activo, conexión de Wire), se recomienda **una sola escena persistente** con paneles de UI mostrados/ocultados (o cargados de forma additiva), salvo la pantalla de Splash/Bootstrap que sí puede ser una escena propia mínima. Esta nota aplica a toda la lista de abajo.

## Orientación: portrait fijo

**El juego es portrait y no rota** (`defaultScreenOrientation: 0`, decidido el 2026-08-09). Ninguna pantalla necesita un layout horizontal, y ninguna debe asumir que existe.

Hasta esa fecha el proyecto estaba en `AutoRotation` con las cuatro orientaciones habilitadas — o sea que cualquier jugador podía girar el dispositivo y caer en un layout que nunca se diseñó ni se midió: el panel del Galaxy J7 en horizontal sería 720×405, contra los 1152–1558 con los que está encuadrado todo. Se cerró **fijando la orientación**, no soportando landscape; soportarlo habría significado un segundo encuadre para las 12 pantallas.

Consecuencia práctica al encuadrar: el ancho del panel es siempre 720 unidades y **la única variable es el alto**, que va de 960 (tablet 4:3) a 1558 (teléfono 19.5:9). Ver [[09-Encuadre-Dispositivos]].

## Mapa de navegación (alto nivel)

```
Splash/Bootstrap
      │
      ▼
   Home ──────────────┬──────────────┬──────────────┐
      │                │              │              │
      ▼                ▼              ▼              ▼
Selección de modo   Perfil          Tienda         Ajustes
      │
      ├─▶ Un jugador ─▶ Selección tablero + dificultad ─▶ Partida
      ├─▶ Local (2P)  ─▶ Selección tablero               ─▶ Partida
      └─▶ Online      ─▶ Quickmatch/Ranked ─▶ Buscando partida ─▶ Partida
                                                                     │
                                                                     ▼
                                                            Resultado de partida
                                                             │            │
                                                             ▼            ▼
                                                          Revancha      Home
```

Leaderboard (Ranked) e Historial de partidas se acceden desde Perfil, no son destinos de primer nivel.

## 1. Splash / Bootstrap

**Objetivo**: inicializar UGS antes de mostrar cualquier UI interactiva.

**Qué pasa acá**: init de Unity Services, sign-in anónimo (Authentication), primera llamada a `GetGameConfig` (Remote Config vía Cloud Code) para tener `BOARD_CONFIGS`, `AI_DIFFICULTY_PARAMS`, `AD_FREQUENCY_CAPS` y `STORE_CATALOG` disponibles antes de navegar.

**Elementos**: logo, indicador de carga simple (sin texto de error técnico visible al usuario).

**Navegación**: única salida → Home. Si falla la inicialización, pantalla de error genérica con botón "Reintentar" (sin detalle técnico expuesto al usuario).

## 2. Home (menú principal)

**Objetivo**: punto de entrada, no más de 2 taps para empezar a jugar (regla del GDD).

**Elementos**:
- Botón principal "Jugar" → Selección de modo.
- Accesos secundarios: Perfil, Tienda, Ajustes.
- Indicador de moneda soft/hard visible en el header (siempre visible, también dentro de Tienda).
- Badge de notificación si hay una partida online pendiente de respuesta (push de Wire recibido con la app en foreground).

**Analytics**: `store_viewed` no aplica acá; sí un evento de sesión estándar (login) ya cubierto por `sdkStart`.

## 3. Selección de modo

**Objetivo**: elegir entre Un jugador / Local (2P) / Online.

**Elementos**: 3 tarjetas grandes (una por modo), con ícono y descripción corta.

**Navegación**:
- Un jugador → Selección de tablero + dificultad.
- Local → Selección de tablero (sin dificultad).
- Online → Selección Quickmatch/Ranked → Buscando partida.

## 4. Selección de tablero (y dificultad si aplica)

**Objetivo**: elegir tamaño de tablero según lo habilitado por `BOARD_CONFIGS` para el modo elegido. **Al lanzamiento son 3x3 y 6x6**: 9x9 y 11x11 están diferidos a post-launch por objetivo táctil (ver [[02-GDD-TicTacToe#3.1 Tableros]] y [[09-Encuadre-Dispositivos]] L-05), y Ranked ya se limitaba a 3x3/6x6 de antes. La pantalla no hardcodea ninguna lista: dibuja una tarjeta por entrada de `BOARD_CONFIGS`, así que cuando los tableros vuelvan aparecen solos.

**Elementos**:
- Grid de tamaños disponibles (los no disponibles para el modo actual no se muestran, no se muestran grisados para no generar confusión).
- Si es Un jugador: selector adicional de dificultad (Fácil/Medio/Difícil/Adaptativo).

**Conexiones de sistema**: la lista de tamaños viene de `GetGameConfig` (Remote Config), no hardcodeada en el cliente.

**Analytics**: `board_size_selected` (+board_size, mode).

**Navegación**: confirmar → Partida (un jugador/local) o Quickmatch/Ranked → Buscando partida (online).

## 5. Buscando partida (Matchmaking)

**Objetivo**: mostrar progreso de búsqueda mientras el ticket de Matchmaker está activo (ver [[03-Arquitectura-UGS-TicTacToe#Matchmaker]]).

**Elementos**:
- Animación de "buscando rival" con tiempo transcurrido.
- Botón "Cancelar" siempre visible y funcional (dispara cancelación explícita del ticket).
- A partir del techo de espera configurado (ver riesgo #2 de matchmaking), ofrecer opción de pasar a Quickmatch o jugar vs IA en su lugar.

**Conexiones de sistema**: creación/polling/cancelación de ticket de Matchmaker; al resolverse, llamada a `CreateMatch`.

**Analytics**: `matchmaking_wait_time`, `matchmaking_cancelled` (+reason).

**Navegación**: match encontrado → Partida. Cancelar → Selección de modo.

## 6. Partida (tablero de juego)

**Objetivo**: pantalla central del juego — jugar el tres en raya.

**Elementos**:
- Tablero (tamaño según selección), con skins de fichas/tablero aplicadas desde `inventory` del jugador.
- Indicador de turno (propio/rival) y, en online, estado de conexión de Wire (Conectando/Suscrito/Sin conexión).
- Timer de turno (si aplica al modo).
- En online: acceso rápido a "reacciones" (stickers, ver [[04-Store-Catalog-TicTacToe#6. Social / comunicación en partida]]).
- Menú de pausa (overlay, no pantalla nueva): Reanudar, Abandonar partida, Ajustes rápidos (sonido).

**Conexiones de sistema**: `PlayMove`/`GetMatchState` (Cloud Code, servidor autoritativo); en un jugador, `GetAiMove`. Push de Wire dispara refresco de `GetMatchState`.

**Analytics**: `match_started`, eventos intermedios ya cubiertos por Cloud Code/Analytics del lado servidor donde aplique.

**Navegación**: fin de partida → Resultado de partida. Abandonar → confirmación → Home (en online, cuenta como abandono, ver riesgo #4 de partidas abandonadas).

## 7. Resultado de partida

**Objetivo**: mostrar el desenlace y las recompensas, e invitar a la siguiente acción sin fricción.

**Elementos**:
- Estado (Ganaste/Perdiste/Empate), recompensa de moneda soft ganada.
- En Ranked: cambio de MMR/posición si aplica.
- Botones: "Revancha" (mismo modo/tablero, solo si el rival sigue conectado, aplica a local/online), "Volver a Home".
- Punto natural para mostrar un intersticial opcional, siempre respetando `AD_FREQUENCY_CAPS` y nunca bloqueando el botón de continuar.

**Analytics**: `match_finished` (+won, board_size, turns, duration, reason).

## 8. Perfil

**Objetivo**: identidad del jugador y acceso a estadísticas/ranking.

**Elementos**:
- Ícono, marco y banner equipados (editable desde acá, con acceso directo a Tienda para cambiar).
- Nombre (con sufijo `#1234` de Authentication).
- Estadísticas: win rate, racha, historial reciente (acceso a Historial de partidas completo).
- Rango/MMR actual y acceso a Leaderboard (Ranked).
- Acceso a "Borrar mis datos" (ver [[02-GDD-TicTacToe#6. Privacidad (diferenciador competitivo)]]), con confirmación explícita.

**Navegación**: sub-pantallas → Historial de partidas, Leaderboard.

## 9. Historial de partidas

**Objetivo**: lista de partidas jugadas recientes, con resultado y modo.

**Elementos**: lista simple, filtrable por modo (un jugador/local/online/ranked).

## 10. Leaderboard (Ranked)

**Objetivo**: mostrar el ladder competitivo.

**Elementos**: posición propia destacada + top N global; filtro opcional por tamaño de tablero (3x3/6x6, únicos habilitados en Ranked).

## 11. Tienda

**Objetivo**: navegar y comprar el catálogo de [[04-Store-Catalog-TicTacToe]].

**Elementos**:
- Tabs por categoría: Fichas, Tableros, Efectos, Sonido, Perfil (íconos/marcos/banners), Reacciones, Moneda/Bundles.
- Cada ítem muestra precio (soft/hard/dinero real según corresponda) y estado (comprado/equipado/disponible).
- Header con saldo de moneda visible (igual que en Home), y botón directo a "comprar moneda" si el saldo no alcanza para un ítem.

**Conexiones de sistema**: catálogo vía `GetGameConfig` (`STORE_CATALOG`); compra vía `ValidatePurchase` (dinero real) o `RedeemStoreItem` (moneda soft/hard).

**Analytics**: `store_viewed` (+section), `iap_purchased`, `iap_failed`, `store_item_redeemed`.

**Navegación**: acceso desde Home y desde Perfil (para equipar cosméticos ya comprados).

## 12. Ajustes

**Objetivo**: configuración general, no relacionada a una partida puntual.

**Elementos**:
- Selector de idioma (los 10 idiomas de [[02-GDD-TicTacToe#8. Localización]]).
- Volumen de sonido/música.
- Gestión de cuenta: vincular con Apple/Google (ver [[03-Arquitectura-UGS-TicTacToe#Authentication]]), cerrar sesión.
- Notificaciones push (activar/desactivar "es tu turno").
- Borrar cuenta y datos (también accesible, con la misma confirmación, desde Perfil).

## Conexiones

- [[01-Directrices-Proyecto]]
- [[02-GDD-TicTacToe]]
- [[03-Arquitectura-UGS-TicTacToe]]
- [[04-Store-Catalog-TicTacToe]]

## Fuente

Derivado de los modos de juego y reglas de UX del GDD (`02-GDD-TicTacToe.md`), de los servicios descritos en `03-Arquitectura-UGS-TicTacToe.md` y del catálogo de `04-Store-Catalog-TicTacToe.md`.
