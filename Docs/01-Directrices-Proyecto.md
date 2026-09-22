---
tags: [directrices, proceso, tictactoe]
---

# Directrices del Proyecto — TicTacToe (UGS)

## Idioma

- **Documentación** (GDD, arquitectura, notas de diseño): español.
- **Código** — nombres de clases, métodos, variables, comentarios, mensajes de commit, nombres de branches, logs, nombres de eventos de Analytics: **inglés, sin excepción**. Ningún identificador ni comentario en español debe llegar a un archivo `.cs`.
- **Contenido del juego** (textos de UI, diálogos, tienda): gestionado vía localización (ver [[02-GDD-TicTacToe#8. Localización]]), no hardcodeado en ningún idioma.

## Convenciones de código (C#)

- PascalCase para clases y métodos públicos, camelCase para variables locales y parámetros.
- Funciones de Cloud Code marcadas `[CloudCodeFunction]`, recibiendo `context`/`gameApiClient` (y `pushClient` si notifican por [[03-Arquitectura-UGS-TicTacToe#Wire (push en tiempo real)|Wire]]) inyectados vía `ICloudCodeSetup`, replicando el patrón `ModuleSetup` de TCGMaster.
- Eventos de Analytics en `snake_case`, en inglés (`match_started`, `ad_watched`, `iap_purchased`, etc.). Un evento sin su definición correspondiente en el Event Manager del Dashboard se descarta en el pipeline — no emitir eventos "ad hoc" sin registrarlos primero.
- El wrapper de Analytics nunca debe poder romper el juego: toda emisión de evento va en try/catch y degrada a warning si falla.

## Estructura de la documentación (estilo Obsidian)

Cada nota de diseño/arquitectura sigue el mismo formato que las notas UGS_* existentes:

1. Front-matter con `tags`.
2. Cuerpo con encabezados `##`.
3. Sección `## Conexiones` al final, con `[[wikilinks]]` a notas relacionadas.
4. Sección `## Fuente` citando el documento maestro del que se deriva.

## Control de versiones y deploy

- Deploy de Cloud Code y Remote Config vía `Window → Deployment` en el Editor, igual que TCGMaster.
- Reutilizar el patrón de handshake de versión (`GameProtocol.Version`) para evitar mezclar cliente y servidor incompatibles — más crítico todavía acá porque hay IAP con dinero real desde el día 1, no solo desde una fase de beta.

## Principios de diseño (derivados del análisis de competencia)

Ver [[analisis-competencia-tic-tac-toe]] para el detalle completo. Resumen de las reglas no negociables que salen de ahí:

1. **Anti-anuncio-abusivo**: frecuencia limitada y configurable sin redeploy, siempre con recompensa opcional, nunca un anuncio sin botón de cierre visible, nunca redirigir a otra tienda disfrazado de anuncio.
2. **IA con dificultad real y diferenciada**, no cosmética — es el diferenciador directo frente a la queja recurrente de "la IA es fácil de vencer" en la competencia.
3. **Privacidad estricta**: minimizar recolección de datos, no compartir con terceros salvo lo estrictamente necesario, borrado de datos accesible desde ajustes.
4. **UI simple y rápida por encima de la estética**: el estilo visual (neón u otro) es un plus, nunca debe sacrificar velocidad de partida.

## Seguridad / anti-cheat

- Toda regla de juego —incluida la validación de movimientos y la detección de victoria— se resuelve en Cloud Code, servidor autoritativo. El cliente nunca decide quién ganó.
- Toda compra (IAP) se valida server-side vía Cloud Code antes de otorgar cualquier moneda o cosmético.

## Conexiones

- [[02-GDD-TicTacToe]]
- [[03-Arquitectura-UGS-TicTacToe]]
- [[analisis-competencia-tic-tac-toe]]

## Fuente

Basado en los patrones de proceso de `Docs/CCG_POC_Design_And_Architecture.md` (proyecto TCGMaster) y en las conclusiones de `analisis-competencia-tic-tac-toe.md`.
