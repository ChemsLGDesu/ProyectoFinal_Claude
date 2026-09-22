---
tags: [ui, estetica, design-system, tictactoe]
---

# Lineamientos estéticos — UI de TicTacToe

Tema visual del juego: **oscuro plano con acentos neón**, priorizando legibilidad y velocidad de partida sobre el adorno (regla del GDD: la estética nunca sacrifica velocidad). Sin assets de arte en esta fase — todo es color, tipografía y layout definidos en `Assets/UI/Styles/main.uss` como variables USS (`:root`), que es la **fuente de verdad**: cualquier cambio de color se hace ahí, nunca hardcodeado en pantallas individuales.

## Paleta base (fondos y superficies)

| Variable USS | Hex | Uso |
|---|---|---|
| `--color-bg` | `#0B0F14` | Fondo global de la app (casi negro, tinte azul) |
| `--color-bg-panel` | `#121821` | Superficie de cards, chips, badges, celdas del tablero |
| `--color-bg-panel-alt` | `#182130` | Superficie seleccionada/hover (cards de tamaño, chips activos) |
| `--color-bg-elevated` | `#1C2733` | Elementos elevados: botones de ícono, toast, tags, celda ganadora |
| `--color-border` | `#263140` | Bordes por defecto de cards, chips y listas |

Escala de profundidad: `bg` → `bg-panel` → `bg-panel-alt` → `bg-elevated` (de más lejano a más cercano). El overlay de pausa/confirmación usa `rgba(5, 8, 12, 0.85)` como backdrop.

## Texto

| Variable USS | Hex | Uso |
|---|---|---|
| `--color-text` | `#E7EDF3` | Texto principal (blanco frío, no blanco puro) |
| `--color-text-muted` | `#8A97A6` | Texto secundario: descripciones, captions, labels de sección, estados vacíos |

## Acentos neón (identidad)

| Variable USS | Hex | Uso |
|---|---|---|
| `--color-accent` | `#35E6C6` | **Acento primario (cian neón)**: botón primario, bordes hover/selección, spinner, ficha **X**, avatar, logo |
| `--color-accent-dark` | `#1FA88F` | Hover/pressed del botón primario |
| `--color-accent-secondary` | `#7C5CFF` | **Acento secundario (violeta neón)**: ficha **O**, chips de dificultad |

Regla de identidad de fichas: **X = cian (`--color-accent`), O = violeta (`--color-accent-secondary`)** — se mantiene en tablero, barra de turno (dots) y cualquier UI futura (historial online, espectador, skins por defecto). Las skins compradas podrán reemplazar esto, pero el par cian/violeta es el "default" de la marca.

## Semánticos

| Variable USS | Hex | Uso |
|---|---|---|
| `--color-danger` | `#FF5D6C` | Acciones destructivas (Delete my data / Delete account), badge de notificación, derrota |
| `--color-danger-dark` | `#E14456` | Variante pressed de danger |
| `--color-warning` | `#FFB84D` | Tag "Soon" y avisos no bloqueantes |
| `--color-win` | `#3FD67A` | Resultado victoria (historial) |
| `--color-loss` | `#FF5D6C` | Resultado derrota (historial) — mismo hex que danger |
| `--color-draw` | `#8A97A6` | Resultado empate — mismo hex que text-muted |

## Monedas

| Variable USS | Hex | Uso |
|---|---|---|
| `--color-soft-currency` | `#F4C948` | Dot y valores de moneda soft (dorado) |
| `--color-hard-currency` | `#B58CFF` | Dot y valores de moneda hard (lila) |

Los badges de moneda (`.coin-badge`) van siempre en el header de Home y Tienda, con dot de color + valor en `--color-text`.

## Componentes y convenciones

- **Botón primario**: fondo `--color-accent`, texto `--color-bg` (oscuro sobre neón), radio 8px (12px en la variante grande de PLAY). Pressed: scale 0.97.
- **Botón secundario**: transparente con borde `--color-border`; hover → borde `--color-accent`.
- **Botón danger**: transparente con borde y texto `--color-danger`; hover → relleno `rgba(255, 93, 108, 0.12)`.
- **Cards / chips / list-items**: superficie `--color-bg-panel`, borde 2px `--color-border`, radio 10–12px (chips: pill 999px). Estado seleccionado: borde y texto en el acento que corresponda (cian en general, violeta para dificultad).
- **Interacción**: transiciones cortas (0.08–0.12s) solo en `border-color`, `background-color`, `scale` y `opacity`. Nada de animaciones largas que frenen el flujo.
- **Turn bar**: el lado inactivo baja a opacidad 0.55; el activo a 1.
- **Radios**: 4px celdas de tablero, 6–8px botones chicos, 10–12px cards, 999px (pill) chips/badges/avatares.
- **Tipografía**: fuente por defecto del tema de UI Toolkit por ahora; bold para títulos, valores y nombres; tamaños 11–13px captions, 14–16px cuerpo, 18–22px títulos de sección, 28–34px títulos de pantalla/resultado. Pendiente: font asset propio con cobertura latina extendida para los 10 idiomas (ver [[02-GDD-TicTacToe#8. Localización]]).

## Reglas

1. **Nunca hardcodear un hex en una pantalla**: todo color nuevo entra como variable en `:root` de `main.uss` y se documenta acá en la misma pasada.
2. **El neón es acento, no fondo**: los acentos cian/violeta se usan en bordes, textos cortos, dots y el botón primario — nunca como fondos grandes que fatiguen la vista en partidas largas.
3. **Contraste**: texto principal siempre `--color-text` sobre superficies `bg`/`bg-panel`; el texto sobre acento neón siempre es `--color-bg` (oscuro), nunca blanco.
4. Los wireframes ([[06-Wireframes-UI]]) definen estructura y jerarquía; esta nota define el color. Ante conflicto de estilo, gana esta nota.

## Conexiones

- [[06-Wireframes-UI]]
- [[05-UI-Pantallas-TicTacToe]]
- [[02-GDD-TicTacToe]]

## Fuente

Extraído de `Assets/UI/Styles/main.uss` (implementación del Milestone 1.5) y de las reglas de UX del GDD (`02-GDD-TicTacToe.md`, sección 7).
