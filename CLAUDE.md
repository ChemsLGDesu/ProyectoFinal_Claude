# TTTXO — TicTacToe (UGS)

Tres en raya multiplataforma (Android / iOS / PC) con backend autoritativo en Unity Gaming Services. Documentación de diseño completa en `Docs/`:

- [Docs/01-Directrices-Proyecto.md](Docs/01-Directrices-Proyecto.md) — directrices de proceso, idioma, convenciones y seguridad.
- [Docs/02-GDD-TicTacToe.md](Docs/02-GDD-TicTacToe.md) — GDD: modos, economía, monetización, privacidad, localización, métricas.
- [Docs/03-Arquitectura-UGS-TicTacToe.md](Docs/03-Arquitectura-UGS-TicTacToe.md) — arquitectura UGS (Auth, Remote Config, Matchmaker, Cloud Code, Cloud Save, Wire, Leaderboards, Analytics) y riesgos técnicos.
- [Docs/04-Store-Catalog-TicTacToe.md](Docs/04-Store-Catalog-TicTacToe.md) — catálogo de tienda / IAP.
- [Docs/05-UI-Pantallas-TicTacToe.md](Docs/05-UI-Pantallas-TicTacToe.md) — pantallas y navegación de UI.
- [Docs/08-Herramienta-Arte-Placeholder.md](Docs/08-Herramienta-Arte-Placeholder.md) — generador de arte placeholder: restricciones del endpoint, chroma key, reglas de prompt y trampas de medición. **Consultalo antes de tocar el catálogo de arte o sus prompts** — cada regla ahí viene de un defecto real.
- [Docs/09-Encuadre-Dispositivos.md](Docs/09-Encuadre-Dispositivos.md) — encuadre de UI en pantallas de celular: geometría medida por dispositivo, safe area, escalado de panel y trampas del Device Simulator. **Consultalo antes de medir o ajustar layout contra cualquier dispositivo** — cada entrada viene de una medición real, y varias documentan números plausibles pero falsos.

## Reglas no negociables (resumen operativo)

### Idioma
- **Documentación** (GDD, arquitectura, notas): español.
- **Código**: clases, métodos, variables, comentarios, commits, branches, logs y eventos de Analytics en **inglés, sin excepción**. Nada en español en archivos `.cs`.
- **Textos del juego** (UI, tienda): vía Unity Localization (10 idiomas de alfabeto latino), nunca hardcodeados.

### Convenciones C#
- PascalCase para clases y métodos públicos; camelCase para variables locales y parámetros.
- Cloud Code: módulo único `TicTacToeModule` (C#, .NET 9); funciones `[CloudCodeFunction]` reciben `context`/`gameApiClient` (y `pushClient` si usan Wire) inyectados vía `ICloudCodeSetup` (patrón `ModuleSetup` de TCGMaster).
- Eventos de Analytics en `snake_case` inglés (`match_started`, `ad_watched`, …). No emitir eventos sin registrarlos antes en el Event Manager del Dashboard.
- El wrapper de Analytics nunca puede romper el juego: toda emisión en try/catch, degrada a warning.

### Seguridad / anti-cheat (servidor autoritativo)
- Toda regla de juego —validación de movimientos, detección de victoria, IA— se resuelve en **Cloud Code**. El cliente nunca decide quién ganó.
- Toda compra (IAP) se valida server-side (`ValidatePurchase`) antes de otorgar moneda o cosméticos. Compras con moneda del juego pasan por `RedeemStoreItem` (saldo descontado server-side).
- El cliente no lee Remote Config directo: la config llega por `GetGameConfig` en Cloud Code.
- Leaderboard Ranked se actualiza solo desde Cloud Code, nunca desde el cliente.

### Monetización / anuncios
- Anuncios con frecuencia limitada configurable vía Remote Config (`AD_FREQUENCY_CAPS`), siempre con botón de cierre visible, recompensados opcionales. Nunca redirigir a otra tienda disfrazado de anuncio.
- Todo lo vendible es cosmético: pay-to-look, nunca pay-to-win.

### Privacidad
- Sign-in anónimo por defecto; vinculación de cuenta desde el día 1.
- Minimizar recolección de datos; borrado de cuenta y datos accesible desde Ajustes.

### UX / UI
- UI simple y rápida por encima de la estética: partida 3x3 en < 60s; nunca más de 2 taps para empezar a jugar.
- Una sola escena persistente con paneles (salvo Splash/Bootstrap).
- **Toda la UI se construye con Unity UI Toolkit** (UXML + USS + controladores C#), no con uGUI/TextMeshPro. Cada pantalla de `Docs/05-UI-Pantallas-TicTacToe.md` es un panel UXML mostrado/ocultado por un router de navegación. Los textos localizables usan Unity Localization con bindings de UI Toolkit.
- Layout según `Docs/06-Wireframes-UI.md`; colores y estilo según `Docs/07-Estetica-UI.md` — la paleta vive como variables USS en `:root` de `Assets/UI/Styles/main.uss` y ningún color se hardcodea fuera de ahí. X = cian `--color-accent`, O = violeta `--color-accent-secondary`.

### Estructura técnica (cliente)
- Unity 6000.3.20f1, 2D URP. C# del cliente en `Assets/Scripts/` con asmdefs:
  - `TTTXO.Core` — lógica pura de juego (tablero, reglas, detección de victoria, IA local): **sin dependencias de UnityEngine** más allá de lo indispensable, para poder reutilizarla después en el módulo de Cloud Code.
  - `TTTXO.Game` — capa Unity: bootstrap, managers, controladores de UI Toolkit. Depende de `TTTXO.Core`.
  - `TTTXO.Core.Tests` (EditMode) — tests de la lógica pura.
  - `TTTXO.Game.Tests` (EditMode) — tests de la capa Unity: cap diario de moneda, alternancia de primer jugador, historial local, cosméticos y espejo de constantes cliente/Cloud Code. Usan `PlayerPrefsSandbox` porque corren contra los PlayerPrefs reales del Editor.
  - `TTTXO.Game.Editor` — scripts de editor (setup de escena, iconos, generación de assets de localización).
- El módulo de Cloud Code vive en `CloudCode~/TicTacToeModule/` (el `~` hace que Unity ignore la carpeta) y **reutiliza el código fuente de `TTTXO.Core`** vía `<Compile Include>` — no se duplica.
- Estado de milestones: **M1** local (1P vs IA + 2P) · **M1.5** UI según wireframes · **M2** fundaciones UGS · **M3** localización (10 idiomas) · **M4** online Quickmatch + Wire · **M5** Ranked (Elo, serie 3x3, temporadas, Leaderboards) — todos implementados y deployados. Pendientes: tienda/IAP y audio.

### Deploy
- Cloud Code y Remote Config vía `Window → Deployment` en el Editor (config-as-code).
- Handshake de versión (`GameProtocol.Version`) entre cliente y servidor — crítico porque hay IAP real desde el día 1.

## Skills

- **`ugs`** — convenciones y procedimientos de Unity Gaming Services (orden de arranque, reglas de arquitectura, convenciones propias). **Cargala ante cualquier trabajo de UGS**: Auth, Cloud Code, Cloud Save, Remote Config, Matchmaker, Leaderboards, Economy, Wire — aunque el usuario no nombre "UGS". No es documentación de API: para firmas concretas, consultá la doc oficial.

**Prerequisito externo**: el pack `claude-kit` es compartido entre proyectos y **no está versionado acá** (`.claude/skills/` está en `.gitignore`). El enlace es **`.claude/skills/ugs`**, un *junction* de Windows al pack — no `.claude/skills/` entero. La diferencia importa: Claude Code busca `skills/<nombre>/SKILL.md`, así que enlazar la carpeta padre deja el `SKILL.md` un nivel arriba y la skill no aparece, con el mismo síntoma que se estaba tratando de arreglar.

Si en otra máquina no aparece, cloná `claude-kit` en `D:/UnityProjects/claude-kit` y recreá el enlace:

```
mklink /J "D:\UnityProjects\TTTXO\.claude\skills\ugs" "D:\UnityProjects\claude-kit\packs\ugs\skills\ugs"
```

Junction (`/J`) y no symlink de directorio (`/D`) a propósito: el junction lo crea cualquier usuario, el symlink pide admin o modo desarrollador.

## Agentes del proyecto

El trabajo se organiza con subagentes especializados. Delegar la tarea al agente que corresponda en vez de hacerlo todo inline:

| Agente | Rol | Cuándo invocarlo |
|---|---|---|
| `game-designer` | Diseña mecánicas, progresión y balance; mantiene el design-doc. Especifica, no implementa. | **Proactivamente** antes de implementar cualquier sistema nuevo; decisiones de diseño o balance |
| `gameplay-programmer` | Implementa mecánicas, input y reglas de juego a partir de specs; tests de gameplay. | Código de gameplay (no UI) |
| `systems-programmer` | Arquitectura, integración de motor y servicios, rendimiento, build, Cloud Code. | Decisiones estructurales, backend, cambios que cruzan varios sistemas |
| `ui-programmer` | **Dueño de toda la UI**: UXML/USS, pantallas y paneles, adaptación táctil/móvil, accesibilidad, theming. | **Cualquier** interfaz del juego, grande o chica |
| `backend-security` | Proyección de vistas (qué sale del servidor), anti-cheat, anti-farming, validación de compras, permisos, privacidad. **Tiene veto.** | **Proactivamente** ante cambios en DTOs de vista, recompensas, economía, IAP o datos personales |
| `localization-manager` | Roster de idiomas, cobertura de claves, QA de glifos/fuentes, largo de textos, formatos regionales. | Agregar idiomas, auditar cobertura, preparar batches de traducción |
| `level-designer` | Layout de niveles, ritmo y curva de dificultad usando mecánicas existentes. | Crear o ajustar niveles/encuentros |
| `narrative-writer` | Diálogos, lore, textos in-game; mantiene `lore-bible.md`. | Contenido narrativo o copy del juego |
| `qa-tester` | Ejecuta pruebas, busca bugs y casos límite; reporta por severidad, no repara. | **Proactivamente** después de cualquier cambio de código o contenido |
| `error-historian` | Mantiene `Docs/ERRORES_Conocidos.md`: síntoma, causa raíz, fix y prevención de cada error. | Al **cerrar** un debugging (error confirmado, no hipótesis). Ante un error nuevo, **consultar su archivo primero** |
| `memory-keeper` | Mantiene `Docs/MEMORIA_Proyecto.md`: estado de cada sesión, decisiones y pendientes. | Al cerrar una sesión o hito, o ante una decisión que una sesión futura necesite conocer |
| `docs-changelog` | Mantiene `CHANGELOG-dev.md`, `changelog-jugadores.md`, notas Obsidian, `Docs/08-Herramienta-Arte-Placeholder.md` y `Docs/09-Encuadre-Dispositivos.md`. | Después de cualquier cambio significativo, al preparar una versión, o al **cerrar una sesión de encuadre de UI en dispositivos** (aunque solo se haya medido) |
| `git-ops` | Commits atómicos (Conventional Commits), ramas, tags, PRs. **Nunca push a `main` ni PR sin confirmación explícita del usuario.** | Preparar commits, ramas, versiones o pull requests |

Flujo típico: `game-designer` especifica → `systems-programmer`/`gameplay-programmer`/`ui-programmer` implementan → `backend-security` audita (si tocó servidor/economía) → `qa-tester` verifica → `docs-changelog` registra → `git-ops` versiona.

Límites entre agentes (respetarlos también al trabajar sin subagentes):
- **Toda la UI es de `ui-programmer`** — `gameplay-programmer` hace lógica de juego, `systems-programmer` arquitectura y servicios; ninguno de los dos toca UXML/USS.
- Balance y valores numéricos los decide diseño, no programación.
- QA reporta, no repara. `error-historian` documenta solo errores ya diagnosticados y confirmados.
- Cambios de arquitectura o sistemas compartidos se coordinan con `systems-programmer`; los que tocan lo que el servidor expone o la economía pasan por `backend-security`, que puede vetarlos.
- Todo cambio relevante termina registrado por `docs-changelog`.

## Estructura de documentación (estilo Obsidian)
Cada nota de diseño lleva: front-matter con `tags`, cuerpo con `##`, sección `## Conexiones` con `[[wikilinks]]` y sección `## Fuente` al final.
