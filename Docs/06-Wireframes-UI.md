---
tags: [ui, wireframes, tictactoe]
source: https://claude.ai/design/p/ed18524f-5c74-40b3-bc10-4cc18229d540?file=TicTacToe+Wireframes.dc.html
---

# Wireframes de UI — TicTacToe (12 pantallas)

Wireframes lo-fi (phone portrait) hechos en Claude Design. Son la fuente de verdad del **layout y jerarquía** de cada pantalla; el estilo visual final sigue siendo el tema oscuro/neón del proyecto (los wireframes son estructura, no arte). Notación: los elementos van de arriba hacia abajo tal como aparecen en el frame.

## 1. Splash / Bootstrap
- Centrado vertical: logo (150x150) → spinner → texto "Loading…".
- Sin UI interactiva ni texto técnico de error. En fallo de init: pantalla de error genérica con botón Retry.
- Hook: init UGS → sign-in anónimo → primer `GetGameConfig`.

## 2. Home
- Header: nombre de jugador (`Player #1234`) a la izquierda; saldos soft (dot dorado) y hard (dot violeta) a la derecha — **siempre visibles**.
- Centro: wordmark/título + botón grande primario "▶ PLAY".
- Fila inferior: 3 botones secundarios — Profile (con badge de notificación), Store, Settings.

## 3. Selección de modo
- Header con botón back "‹" + título "Choose a mode".
- 3 tarjetas verticales grandes: Single player (👤, "Play vs AI · pick difficulty"), Local 2P (👥, "same device"), Online (🌐, "Quickmatch or Ranked").

## 4. Tamaño de tablero + dificultad
- Header back + título del modo elegido.
- Label "Board size" → grid 2x2 de tarjetas: 3×3/align 3, 6×6/align 4, 9×9/align 4–5, 11×11/align 5. Seleccionada = invertida (fondo oscuro). Tamaños no disponibles para el modo: **ocultos, no griseados**.
- Label "Difficulty" (solo 1P) → chips horizontales: Easy / Medium / Hard / Adaptive (seleccionado = chip invertido).
- Botón primario "Start game" anclado abajo.

## 5. Buscando partida (matchmaking — pantalla para M2+)
- Spinner + tiempo transcurrido grande (mm:ss) + "Searching for a rival…".
- Card "Taking too long?" con 2 botones: Play Quickmatch / Play vs AI (aparece pasado el techo de espera).
- Botón "Cancel" siempre visible abajo (cancela el ticket → vuelve a selección de modo).

## 6. Partida
- Header: estado de conexión Wire como chip fantasma (◉ Subscribed — solo online), botón pausa ⏸ a la derecha.
- Barra de turno: "You · X ●" | chip timer (⏱ 0:08, si el modo usa timer) | "Rival · O".
- Tablero central cuadrado (grid según tamaño), celdas de línea ganadora resaltadas (glow).
- Fila inferior (solo online): chips de reacciones rápidas (😀, gg, 👍, Nice!).
- Pausa = overlay, no pantalla: Resume · Abandon · sonido rápido.

## 7. Resultado
- Título grande centrado ("You win! 🎉" / Lose / Draw).
- Card de recompensa: "Reward" + dot dorado "+ N".
- Card Ranked (solo ranked): "MMR 1240 ▲ +18".
- Slot de intersticial opcional (nunca bloquea continuar).
- Fila: botón primario "Rematch" + botón "Home".

## 8. Perfil
- Header back + "Profile".
- Banner equipado (full width) con avatar circular superpuesto + nombre con sufijo `#1234` + link "✎ edit look · go to Store ›".
- Card de stats en fila: win rate % / racha 🔥 / MMR.
- Botones lista: "Match history ›", "Leaderboard (Ranked) ›".
- Abajo del todo: "Delete my data" en rojo (con confirmación explícita).

## 9. Historial de partidas
- Chips de filtro: All / Single / Local / Online / Ranked.
- Lista de cards: resultado coloreado (Win verde, Loss rojo, Draw gris) + tamaño, modo + duración, y "hace cuánto" a la derecha.

## 10. Leaderboard (Ranked)
- Chips de filtro de tablero: 3×3 / 6×6 (únicos de Ranked).
- Top N en cards + separador "⋮" + fila propia destacada (invertida) con posición y MMR.

## 11. Tienda
- Header: título + saldos (hard con botón "+").
- Tabs de categoría: Pieces / Boards / FX / Sound (y Profile, Reactions, Currency según catálogo).
- Grid 2 columnas de cards: preview + chip de precio (dot soft/hard) o estado (Equipped invertido / Owned fantasma).
- Botón punteado "Not enough? Buy currency ›".

## 12. Ajustes
- Card Language: selector con dropdown (10 idiomas).
- Card Sound/Music: 2 sliders.
- Card push "It's your turn": toggle On/Off.
- Cuenta: Link Apple / Link Google (fila) + Log out.
- Abajo: "Delete account & data" en rojo (misma confirmación que Perfil).

## Alcance por milestone
- **Ya jugable (M1)**: 1, 2, 3, 4, 6, 7 — rehacer layout según wireframe.
- **Nuevas como locales/stub en M1.5**: 8 (stats locales), 9 (historial local), 12 (idioma solo English hasta M3; links de cuenta ocultos hasta M2).
- **Requieren UGS (M2+)**: 5 (Matchmaker), 10 (Leaderboards), online real en 3 y 6; 11 (Tienda) requiere catálogo/IAP — stub navegable mientras tanto.

## Conexiones

- [[05-UI-Pantallas-TicTacToe]]
- [[02-GDD-TicTacToe]]
- [[design-doc]]

## Fuente

Wireframes creados en Claude Design (proyecto "Game screens wireframe", link en el front-matter), derivados de `05-UI-Pantallas-TicTacToe.md`.
