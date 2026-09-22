---
tags: [dispositivos, encuadre, ui, safe-area, simulator, mediciones, tictactoe]
---

# Encuadre en dispositivos — hallazgos y trampas

Base de conocimiento de todo lo que se aprende **encuadrando y ajustando la UI contra pantallas de celular**, simuladas o reales: qué reporta cada herramienta, qué de eso miente, y qué regla queda para la próxima pantalla y para el próximo proyecto.

Es el análogo de [[08-Herramienta-Arte-Placeholder]] pero para el layout, y comparte su criterio de admisión:

> **Una precaución teórica no entra.** Solo se documenta lo que salió de una observación real, con el número que la condena. No "en móvil puede tapar el notch": el rect medido, el inset medido, y cuál de los dos gana.

**Diferencia con [[ERRORES_Conocidos]]:** allá va el error puntual con síntoma greppeable, para no re-diagnosticarlo. Acá va el conocimiento acumulado de encuadre: la geometría de cada dispositivo, qué herramienta la reporta bien, y las reglas de layout que salieron de haberse equivocado. Un mismo episodio puede dejar ficha en los dos lados; cuando pasa, se referencian.

## Cómo usar este archivo

1. **Antes de encuadrar una pantalla nueva**, leer la sección *Trampas de medición*. Todas producen números plausibles y ninguna tira un error.
2. **Antes de creerle a una medición**, correr el preámbulo de [[ERRORES_Conocidos]] (ERR-KB-004) y además los chequeos de T-03 y T-06 de acá. **Poner `runInBackground = true` antes de `Play`, nunca después** (T-07).
3. **Al cerrar una sesión de encuadre**, invocar a `docs-changelog` para que registre la iteración: qué se probó, en qué dispositivo, qué dio, y qué regla queda.

Cada entrada lleva: **qué se observó** · **la medición** · **la consecuencia** · **la regla que queda**. Si algo quedó sin resolver se marca **ABIERTO** y se dice explícitamente qué chequeo falta — una hipótesis nunca se escribe como conclusión.

---

## Geometría medida

Todo medido en play mode con el Device Simulator, panel en `ScaleWithScreenSize` con referencia 720×1400 y `match=0`, o sea **panel siempre de 720 unidades de ancho**.

| Dispositivo | Resolución | dpi | Inset arriba | Inset abajo | Panel resultante |
|---|---|---|---|---|---|
| Apple iPhone 11 | 828 × 1792 | 326 | 88 px | 68 px | 720 × 1558.2 |
| Apple iPhone 13 Pro Max | 1284 × 2778 | 458 | **141 px** | 102 px | 720 × 1557.8 |
| Apple iPad Pro 12.9" | 2048 × 2732 | 264 | **0** | 40 px | 720 × 960.5 |
| Samsung Galaxy J7 (2017) | 1080 × 1920 | 420 | 0 | 0 | 720 × 1280 |
| Xiaomi Redmi 6 Pro | 1080 × 2280 | 480 | 89 px | **0** | 720 × 1520 |
| Nvidia Shield Tablet | 1200 × 1920 | 320 | 0 | 0 | 720 × 1152 |

**Todo lo de arriba es portrait, y desde el 2026-08-09 eso es lo único que existe**: el juego quedó fijado a portrait (`defaultScreenOrientation: 0`). Antes estaba en `AutoRotation` con las cuatro orientaciones habilitadas, o sea que cualquier jugador podía girar el dispositivo y caer en un layout que nunca se midió — en landscape el panel del J7 sería 720×405 contra los 1152–1558 con los que se trabajó. Se cerró fijando la orientación, no midiendo landscape.

Cuatro cosas que se leen de esta tabla y no de la documentación de nadie:

- **El inset superior no escala con la resolución.** El 13 Pro Max tiene 141px de notch contra los 88 del 11: es 60% más, no 55% más como sugeriría la resolución. Y el Redmi, con la misma resolución de ancho que el J7 (1080), tiene 89px donde el otro tiene 0. Los insets se consultan, nunca se derivan.
- **Los cuatro bordes son independientes, y hay casos espejo de los dos lados.** El iPad tiene 0 arriba y 40 abajo; el Redmi 89 arriba y 0 abajo. Un layout que solo contemple "notch sí / notch no" se rompe en los dos. Es la razón por la que el controlador calcula los cuatro insets por separado en vez de asumir simetría.
- **La misma resolución no implica el mismo panel.** J7 y Redmi son los dos 1080 de ancho, pero 1920 contra 2280 de alto dan paneles de 1280 y 1520. El alto del panel es la única variable real cuando el ancho está fijo por `match=0`.
- **El caso que aprieta es la tablet, y el segundo es el teléfono 16:9.** El iPad deja 960 unidades de alto y el J7 1280, contra las 1558 de los teléfonos modernos. Un layout escrito contra 1400 de referencia está por encima de los dos: son los que hay que medir cuando algo tiene que entrar.

---

## Setup: el Device Simulator

`com.unity.device-simulator.devices` 1.0.1. La *vista* Simulator ya viene en el Editor; el paquete agrega el catálogo de dispositivos reales.

**Alimenta métricas de verdad al runtime**, no es una máscara visual: resolución, `dpi` y `safeArea` salen exactos contra el dispositivo real. Y **funciona con el build target en Android**, así que no hace falta cambiar de plataforma para chequear geometría de iOS — cambiar de plataforma cierra el play mode y dispara reimport.

**Es también la única vía para cambiar la resolución por código.** El sandbox de `Unity_RunCommand` rechaza `System.Reflection`, que es lo único que permite tocar `GameViewSizes` (ver ERR-KB-004). El Simulator esquiva la restricción entera.

**Trabajar con la pestaña Game cerrada.** Es lo que evita T-06 de raíz, y sale barato: `Ctrl+2` la devuelve.

### Truco: enumerar ventanas sin `System.Reflection`

Necesario para varias trampas de abajo, y bloqueado por la vía obvia. Esto pasa el sandbox:

```csharp
var windows = Resources.FindObjectsOfTypeAll<EditorWindow>();
foreach (var w in windows)
    result.Log("{0} pos={1}", w.GetType().FullName, w.position);
```

`GetType().FullName` no requiere importar `System.Reflection`, así que se puede identificar `UnityEditor.DeviceSimulation.SimulatorWindow` y `UnityEditor.GameView` por nombre.

---

## Trampas de medición

### T-01 — `Maximize On Play` deja al Simulator afuera, en silencio

**Observado (2026-08-09).** Con el Simulator abierto y mostrando el iPhone 11, entrar a play mode devolvía métricas del Game view común.

| | Fuera de play | En play con maximize |
|---|---|---|
| `Screen` | 828 × 1792 | **720 × 1400** |
| `Screen.dpi` | 326 | **96** |
| `Screen.safeArea` | `(0, 68, 828, 1636)` | **pantalla entera** |
| ventanas vivas | 10 | **2** |

**Causa:** `Maximize On Play` maximiza el **Game view**, no el Simulator, y Unity descarga el resto de las ventanas —incluida `SimulatorWindow`— llevándose el shim. Sin ningún error: la safe area pasa a ser la pantalla completa, que es exactamente lo que devuelve un dispositivo sin notch.

**Regla:** destildar `Maximize On Play` en la pestaña Game. Es un paso manual del usuario, no automatizable por MCP.

**Señal barata:** `Resources.FindObjectsOfTypeAll<EditorWindow>()` devolviendo **2 ventanas en vez de ~10** significa que hay una vista maximizada y que el Simulator no está en juego.

Es el caso particular de T-06, que es el general.

### T-02 — El play mode puede arrancar pausado

**Observado (2026-08-09).** La primera entrada a play mode por MCP quedó con `isPaused=true`. Dos llamadas seguidas leyeron `frameCount=1` y `time=0`: el player loop nunca arrancó, y todo lo medido en ese estado era el layout sin calcular.

Se confunde con la trampa 1 de ERR-KB-004 (el Editor de fondo no tickea), pero **no se arregla con `runInBackground`** — ahí ya estaba en `true` y el loop seguía clavado.

**Regla:** el preámbulo incluye `EditorApplication.isPaused = false`, además de leer `isPaused` para saber que hizo falta.

### T-03 — Lecturas mezcladas: mitad Simulator, mitad Game view

**Observado (2026-08-09).** Un frame devolvió `dpi=326` y la `safeArea` del iPhone 11 **junto a** `Screen=720×1400`, que es la resolución del Game view. Los tres valores son plausibles por separado.

**Chequeo de coherencia:**

```csharp
result.Log("coherent={0}", Mathf.Approximately(Screen.safeArea.width, Screen.width));
```

Si no coinciden, la lectura está a mitad de camino entre dos vistas: dejar pasar frames y volver a leer.

**Y no alcanza con no medir en ese frame: hay que no *actuar* en ese frame.** Ver L-04, donde este mismo caso se comió el arreglo de safe area durante toda una sesión.

### T-04 — La escala del panel con `ConstantPhysicalSize` · **cerrado por cambio de diseño**

**Observado (2026-08-09).** `MainPanelSettings` estaba en `ConstantPhysicalSize` con `referenceDpi = 96`. Con `Screen.dpi = 326` la fórmula pide escala 3.4; el panel medía 1:1 con los píxeles. Sondeado con clones en play mode:

| PanelSettings | `referenceDpi` | Panel resultante | Escala |
|---|---|---|---|
| el asset del proyecto | 96 | 828 × 1792 | **1** |
| clon | 163 | 414 × 896 | 2 = 326/163 ✓ |
| clon **idéntico al asset** | 96 | 243.83 × 527.71 | **3.3958 = 326/96** ✓ |

Los clones siguen la fórmula; el asset no. Un clon con los mismos valores da un resultado distinto del original, o sea que la diferencia está en la instancia y no en la configuración.

**El mecanismo nunca se estableció** y sigue sin establecerse. Parecía una escala cacheada en la instancia del asset que no se invalida al cambiar el DPI simulado, pero no se probó y no debe citarse como causa.

**Por qué está cerrado igual:** el proyecto pasó a `ScaleWithScreenSize` (L-02), donde la escala no depende del DPI y la ambigüedad desaparece. **Cerrado por dejar de depender de eso, no por haberlo entendido** — la distinción importa si otro proyecto necesita `ConstantPhysicalSize` de verdad, porque ahí esta trampa sigue viva y sin explicación.

### T-05 — Cambiar de dispositivo reinicia el play mode

**Observado (2026-08-09).** Al cambiar el dispositivo del Simulator, el play mode se reinicia: `frameCount` vuelve a ~39 y la app arranca de nuevo desde el splash.

Lo peligroso no es el reinicio sino cómo se manifestó: las lecturas siguientes devolvieron `frameCount` congelado en 84 con números perfectamente formados. Eran de **edit mode** — trampa 4 de ERR-KB-004 y el loop frenado, juntas. `EditorApplication.isPlaying` decía `False` mientras las medidas se leían normales.

**Regla:** después de cambiar de dispositivo hay que **esperar al splash de nuevo**, y `EditorApplication.isPlaying` va en la misma llamada que las medidas — no en una llamada aparte, porque el estado cambia entre una y otra.

### T-06 — Con el Game view abierto, el play mode se lo queda él

**Observado (2026-08-09).** Con `Maximize On Play` ya destildado y el Simulator mostrando un iPhone 13 Pro Max, entrar a play devolvía `Screen = 720×1400`: el Game view, que estaba docked al lado. Unity liga la vista de play a la **última que se tocó a mano**; `Focus()` por código no cuenta — se probó y no cambia nada.

**Lo grave: el chequeo de coherencia de T-03 no lo detecta.** `safeArea.width` y `Screen.width` valen los dos 720 y el frame es internamente consistente. Solo que del dispositivo equivocado.

**El único tell** es comparar `Screen.width/height` contra la resolución del dispositivo elegido en el dropdown. Conviene dejarlo escrito como constante en el comando de medición:

```csharp
// iPhone 13 Pro Max
result.Log("right device={0}", Screen.width == 1284 && Screen.height == 2778);
```

**Regla:** trabajar con la pestaña Game **cerrada**. Clickear el Simulator antes de cada play funciona pero hay que acordarse cada vez, y olvidarse no falla ruidosamente: devuelve una medición limpia de la pantalla equivocada.

### T-07 — Entrar a play mode puede dejar el bridge del MCP colgado, sin salida desde este lado

**Observado (2026-08-09).** Después de `Play`, **todas** las llamadas del MCP empezaron a devolver:

```text
Unity not detected (no fresh discovery files found)
```

No es que Unity se haya caído. Comprobado desde fuera, el Editor estaba vivo y respondiendo, y el relay corría con el `--project-path` correcto:

```text
Get-Process Unity        -> PID 40128, Responding=True, "TTTXO - Main - Android"
relay_win.exe --mcp --project-path D:/UnityProjects/TTTXO --name TTTXO
```

**La pista está en el archivo de descubrimiento, que existe pero está rancio:**

```text
now       : 15:40:38
heartbeat : 15:40:01   <- creado al entrar a play, nunca refrescado
```

**Causa:** el heartbeat de `~/.unity/mcp/connections/bridge-*.json` lo escribe el loop del Editor. Con `runInBackground = false` y la ventana sin foco, ese loop se congela al entrar a play mode — y se lleva el bridge con él, no solo el juego. Es la trampa 1 de ERR-KB-004 un nivel más arriba: ahí congelaba las *mediciones*, acá congela el *canal*.

**Por qué es un punto muerto y no solo una molestia.** El comando que pondría `runInBackground = true` es, por el preámbulo de siempre, el primero después de `Play`. Y ese es exactamente el que ya no puede llegar. No hay forma de salir desde el lado del agente: hay que pedirle al usuario que **le haga clic a la ventana de Unity**, lo que devuelve el foco, reanuda el loop y refresca el heartbeat.

**Por qué no había pasado antes.** Es una carrera, no una certeza: en todas las sesiones anteriores el primer comando entró antes de que el loop se frenara. Que haya funcionado veinte veces no significaba que estuviera bien ordenado.

**Y lo arma la disciplina correcta de otra ficha.** ERR-KB-004 pide revertir `runInBackground` a `false` al cerrar, para no ensuciar `ProjectSettings.asset`. Eso deja el flag en `false` para la sesión siguiente, que es la precondición de esta trampa. Las dos reglas son correctas por separado y se pisan en el orden.

**Prevención — invertir el orden:**

```csharp
// En edit mode el Editor sí tickea, así que este comando SIEMPRE llega.
PlayerSettings.runInBackground = true;   // ANTES de Play, nunca después
```

`Application.runInBackground` y `PlayerSettings.runInBackground` son la misma variable (ERR-KB-004), así que da igual cuál se toque; lo que importa es que sea **antes**. Y sigue valiendo revertirlo al cerrar la sesión.

**Diagnóstico rápido, en orden**, cuando el MCP diga "Unity not detected" más de una vez seguida:

1. `Get-Process Unity` — si no está, se cayó y esto no aplica.
2. Comparar `LastWriteTime` del `bridge-*.json` contra la hora actual. Rancio = loop congelado, no proceso muerto.
3. Si está rancio: pedir foco. Nada del lado del agente lo destraba.

### T-08 — `Unity_RunCommand` deja de funcionar en play mode · **ABIERTO**

**Observado (2026-08-09).** Todas las llamadas a `Unity_RunCommand` **dentro de play mode** empezaron a fallar así:

```text
UNEXPECTED_ERROR: Could not find file
"D:\UnityProjects\TTTXO\Library\AssistantRunCommand\Unity.AI.Assistant.RunCommand.Dynamic.<guid>.dll"
```

El GUID **cambia en cada intento**, o sea que cada llamada compila un assembly nuevo y después no lo encuentra. `Library/AssistantRunCommand/` está vacía.

**En edit mode funciona perfecto.** Y antes, en la misma sesión, había funcionado en play mode decenas de veces.

**Sobrevive a un reinicio completo del Editor**, así que no es un estado en memoria.

**Descartado:** la preferencia de compilación durante play. `EditorPrefs ScriptCompilationDuringPlay` vale **0** (`RecompileAndContinuePlaying`), o sea que compilar mientras corre el juego está permitido. Era la hipótesis obvia y es falsa.

**Sin diagnosticar.** No sé por qué pasa ni qué lo dispara, y está escrito así a propósito.

**Por qué importa acá más que en otro lado:** medir en play mode es *todo* el flujo de trabajo de este documento. Sin `RunCommand` en play, no hay `worldBound`, no hay `resolvedStyle`, no hay `Screen`. Lo que sí sigue andando es `Unity_ManageEditor` (Play/Stop/GetState) y `Unity_GetConsoleLogs`.

**Vía alternativa, la que ya conoce ERR-KB-005:** un script de Editor **de verdad**, compilado en `TTTXO.Game.Editor`, que loguee lo que haga falta al entrar a play, y leer la consola con `Unity_GetConsoleLogs`. Es más pesado que un `RunCommand` pero no depende del assembly dinámico.

**Y la lección que dejó el episodio, que vale más que la trampa:** el bloqueo de portrait se terminó cerrando **con una captura de pantalla**, no con un número. Si la app hubiera seguido la rotación, el layout se habría re-maquetado —título horizontal, navegación a lo ancho— y la captura mostraba la composición vertical dibujada rotada, que es imposible de producir con un re-maquetado. Estuve tratando "no lo medí con un `Log`" como "no está probado", y no es lo mismo: **cuando el instrumento de medición se rompe, el resultado renderizado puede ser evidencia más directa que la cifra, no menos.** Ese razonamiento solo aplica cuando la imagen discrimina entre las dos hipótesis; acá lo hacía.

### T-09 — Navegar antes de que termine la init de UGS **cuelga el juego**

**Observado (2026-08-09).** Un `router.Show(ScreenId.Profile)` lanzado en el **frame 2**, con el play mode recién arrancado, dejó la aplicación colgada:

```text
frameCount=2  time=0.02  unscaled=8.016     <- llamada 1
frameCount=2  time=0.02  unscaled=8.016     <- llamada 2, idénticos
frameCount=2  time=0.02  unscaled=8.016     <- llamada 3
```

Sin un solo error ni warning en consola. La pantalla destino quedó con `display: Flex` y rect `0×0`: nunca se maquetó. Reiniciar el play mode y **no** navegar dejó el juego llegar a Home normalmente (frame 7935), o sea que el disparador es la navegación temprana.

**Cómo se distingue de las otras dos congeladas**, que es lo único que importa en el momento:

| | `frameCount` | `Time.time` | `Time.unscaledTime` |
|---|---|---|---|
| T-02 (arrancó pausado) | clavado en 1 | 0 | **avanza** |
| Editor de fondo sin tickear (ERR-KB-004 trampa 1) | clavado | clavado | **avanza** |
| **T-09 (colgado)** | clavado | clavado | **clavado** |

`Time.unscaledTime` es el que discrimina: si tampoco avanza, no es que el loop no corra — es que **el hilo principal está bloqueado**.

**Es peor que la trampa de flujo que ya documentaba ERR-KB-004.** Allá, un `Show()` prematuro quedaba *pisado* segundos después por el `Show(Home)` del splash, sin error y sin daño. Acá cuelga el proceso.

**Un jugador no puede alcanzarlo** — el splash no ofrece navegación — pero este flujo de medición sí, y es exactamente lo que uno hace al empezar un barrido de pantallas.

**Regla:** navegar **solo desde una Home asentada**. El preámbulo es pollear hasta ver `screen-home` visible *y con rect distinto de cero*, y recién ahí empezar. Nunca encadenar `Play` con un `Show()` en la misma tanda.

---

## Hallazgos de layout

### L-01 — La safe area no estaba contemplada · **resuelto**

**Observado (2026-08-09).** Nada en `Assets/` leía `Screen.safeArea` (grep sin resultados). En el iPhone 13 Pro Max el resultado era visible: el nombre del jugador cortado por la cámara.

**Resuelto** con `Assets/Scripts/Game/UI/SafeAreaController.cs`, que convierte `Screen.safeArea` a unidades de panel y lo suma al padding **de cada pantalla**. Medido después del arreglo:

| Pantalla | Padding esperado (arriba/abajo) | Aplicado |
|---|---|---|
| Home (base 32) | 111.065 / 89.196 | 111.028 / 89.159 |
| Game (base 16) | 95.065 / 73.196 | 95.327 / 73.458 |

`.header-bar` pasó a arrancar en y=111.03 contra una zona insegura de `y < 79.07`.

**Dos decisiones de diseño que valen para cualquier proyecto de UI Toolkit:**

- **El inset va como padding de cada pantalla, no del root.** El padding vive dentro de la caja del elemento, así que el fondo de la pantalla sigue sangrando hasta el borde por debajo del notch y solo el contenido se corre. Con padding en el root los fondos se meten para adentro y quedan franjas negras arriba y abajo.
- **La base del padding se lee de propiedades custom del USS** (`--content-padding-x/y`), nunca de `resolvedStyle.padding`. Una vez que el controlador escribe padding inline, `resolvedStyle` devuelve *ese* valor: releerlo suma el inset sobre el inset en cada pasada. Cachear la primera lectura solo mueve el problema, porque puede caer antes de que resuelvan los estilos y **un padding de 0 es indistinguible de uno real**. La deuda que deja: las propiedades custom espejan el `padding` y hay que mantenerlas en sincronía a mano.

### L-02 — `ConstantPhysicalSize` hacía la UI más chica cuanto mejor la pantalla

**Observado (2026-08-09).** El usuario reportó que en algunos dispositivos los botones se veían muy chicos. Con el panel 1:1 contra los píxeles, cada `px` del USS era un píxel físico, así que **cuanta más resolución tenía el equipo, menor fracción de pantalla ocupaba todo**. El botón Jugar medía 144.67 unidades: 20% del ancho en un panel de 720, **11.3% en el de 1284** del 13 Pro Max.

**Arreglado** pasando a `ScaleWithScreenSize` con referencia **720×1400** y `match=0` (ancho). Con eso el panel mide **siempre 720 unidades de ancho** y todo ocupa la misma fracción de pantalla en cualquier dispositivo.

**Por qué 720×1400 y no otra:** es la resolución contra la que se escribió y midió todo el USS del proyecto. Elegirla como referencia hace que el cambio sea visualmente nulo a esa resolución y escalado puro en el resto — el mínimo riesgo de regresión posible.

**Por qué `match=0`:** el juego es portrait y el tablero se dimensiona por `min(ancho, alto)`. Fijar el ancho hace que todo el layout horizontal sea determinista y que la única variable sea el alto del panel, que se absorbe centrando. Medido: el tablero da **88.94%** del ancho en el iPhone y **88.87%** en el iPad.

**Regla portable:** un layout escrito en `px` contra un canvas fijo solo se sostiene si la escala del panel es ≈1. Elegir `scaleMode` es una decisión de layout, no de configuración.

### L-03 — Todo anclado arriba quedaba fuera del alcance del pulgar

**Observado (2026-08-09).** El usuario lo reportó sosteniendo el teléfono: "los dedos no llegan a nada". Las tarjetas de modo vivían en el tercio superior y el tablero pegado bajo la barra de turno.

**Arreglado** haciendo crecer y centrar los contenedores de contenido, dejando anclados el header y el botón de confirmar. Medido en el 13 Pro Max: las tarjetas pasaron de y≈96..640 a **y=521..1101** sobre un panel de 1557.8.

**El caso del ScrollView tiene su propia solución.** Centrar el contenido de un `ScrollView` a secas no sirve —el contenedor abraza a su contenido, así que no hay nada que centrar— y ponerle `flex-grow` deja el contenido largo recortado e inalcanzable arriba. Lo que funciona en los dos casos es `min-height: 100%` sobre el content container:

```css
.board-select-content .unity-scroll-view__content-container {
    min-height: 100%;
    justify-content: center;
}
```

Contenido corto: el contenedor queda a la altura del viewport y `justify-content` lo centra. Contenido largo: el contenedor crece más que el viewport, el centrado queda en no-op y el scroll funciona normal.

**Costo asumido, no gratis.** El tablero estaba anclado arriba *a propósito*, para que el aire se juntara abajo donde [[06-Wireframes-UI]] quiere la fila de reacciones. Centrar se lleva esa propiedad: esa fila va a tener que reclamar su propia banda bajo el tablero en vez de heredar lo que sobre.

### L-04 — Documentar una trampa no alcanza para no caer en ella

**Observado (2026-08-09).** El `SafeAreaController` se escribió **el mismo día** en que se documentó T-03, y cayó en T-03. Corrió en un frame donde `Screen` era del Game view y `safeArea` del Simulator, y el resultado fue:

```text
esperado   top=79.07   bottom=57.20
aplicado   top=0       bottom=102.06
```

`102.06` es `safeArea.y` **sin convertir a unidades** (factor 1 en vez de 0.5607, porque panel y `Screen` medían lo mismo en ese frame). El `top=0` salió de un `Screen.height` rancio que dio un inset **negativo**.

**Lo que convirtió un frame malo en un bug de sesión entera** no fue el frame malo: fue el early-out de "nada cambió", que guardó ese estado como aplicado y nunca recalculó.

**Tres reglas, y las tres son de código y no de disciplina:**

- **La guarda de coherencia va adentro del código que actúa**, no solo en el comando de medición. T-03 estaba escrito y no sirvió: leerlo no impide escribir el bug.
- **Un early-out de idempotencia solo se arma con un estado que se validó.** Si la pasada no pudo aplicarse a todo, no se marca como aplicada.
- **Hace falta un re-chequeo periódico**, no solo el evento de resize. Un frame malo tiene que ser autocorregible; si el único disparador es un evento que ya pasó, el error queda permanente.

### L-05 — Los tableros densos no llegan al objetivo táctil mínimo, y no es un problema de encuadre

**Observado (2026-08-09), Xiaomi Redmi 6 Pro.** Medida la celda del tablero 11×11 en play mode:

```text
celda = 53.33 unidades = 80 px = 26.67 dp   (dpi 480)
```

Contra los **48dp** que Android recomienda como objetivo táctil mínimo (44pt en iOS), es poco más de la mitad.

**Y no se arregla agrandando el tablero.** La pantalla tiene 360dp de ancho, así que once celdas dan **32.7dp aunque el tablero ocupara el 100%**. El techo es aritmético, no de layout. Corriendo la cuenta para los cuatro tamaños en un teléfono de 360dp, con el tablero al 88.89% del ancho que da el encuadre actual:

| Tablero | Celda | ¿48dp? |
|---|---|---|
| 3×3 | ~102 dp | sí |
| 6×6 | ~53 dp | sí, justo |
| 9×9 | ~35 dp | **no** |
| 11×11 | ~29 dp | **no** |

**Contrastado después contra una medición.** El 6×6 en el Galaxy J7 dio **57.5dp** por celda, contra los ~53 que predice la tabla. No es discrepancia: el J7 tiene 411dp de ancho (1080px a 420dpi), no 360. La cuenta y la medición concuerdan una vez que se usa el ancho real, que es la razón por la que la tabla dice de qué teléfono habla. Medidos hasta hoy: 11×11 a 26.67dp (Redmi) y 6×6 a 57.5dp (J7); las otras dos filas siguen siendo aritmética.

**En tablet los tableros diferidos sí entran, y por muy poco.** Medido en la Nvidia Shield Tablet (600dp de ancho), configurando un 11×11 a mano ya que Core lo sigue conociendo:

```text
celda 11x11 = 58.2 unidades = 48.5 dp   ->  pasa los 48dp
```

Coincide con la aritmética. Eso le da respaldo medido a la vuelta "solo por encima de cierto ancho de pantalla", **con un matiz que conviene no perder**: 48.5 contra 48 no es margen, es empate. Y la Shield es la tablet más apretada del catálogo — la Sony Xperia Z2, con la misma resolución a 240dpi, tiene 800dp y daría ~64.7dp. O sea que el umbral de ancho que habilitaría los tableros grandes cae en algún lugar **entre 411dp (J7, no llega) y 600dp (Shield, empata)**, y elegirlo pegado a 600 sería elegir el empate.

**Regla:** el tamaño de tablero jugable en teléfono está acotado por el objetivo táctil, no por la pantalla. `ancho_dp / n ≥ 48` da el máximo: en 360dp son 7 celdas. La cuenta se hace **antes** de ofrecer un tamaño de tablero, no después de encuadrarlo.

**Decidido el 2026-08-09: 9×9 y 11×11 no van al lanzamiento**, diferidos a post-launch. El corte se hizo a nivel config —`BOARD_CONFIGS` y `GameConfigService.ShippedBoardSizes`— y **no** en `BoardConfig`, así que las reglas, recompensas y params de IA siguen intactos y volver es reagregar dos entradas. Ver [[02-GDD-TicTacToe#3.1 Tableros]].

**Lo que este hallazgo no resuelve** es *para qué dispositivo* vuelven. En tablet los dos entran cómodos: la cuenta que los descarta es la de un teléfono de 360dp, no la de una pantalla grande. Si vuelven para todos, vuelve el objetivo de 29dp; si vuelven por encima de cierto ancho, hace falta una regla de disponibilidad por dispositivo que hoy no existe en `BOARD_CONFIGS`, que solo discrimina por modo.

### L-06 — El tablero 11×11 desborda su contenedor, y no se corrige solo

**Observado (2026-08-09), Nvidia Shield Tablet.** Configurando un 11×11 a mano (está recortado del catálogo, pero Core lo sigue conociendo):

```text
.board-container   639.60 ancho    (respeta max-width: 640)
.board-surface     691.20 ancho    x=14.40  ->  26 unidades a la izquierda del contenedor
celda 58.2 + márgenes 1.8+1.8 = paso 61.8   ->  x11 = 679.8 + inset de la superficie
```

Estable en el frame 258237, o sea que no es un estado transitorio del primer frame. El 6×6 en el mismo dispositivo dio 639.6 y entra bien.

**La celda aplicada, 58.2, es exactamente `640/11`**: se calculó sin restar ni los márgenes de celda ni el inset de la superficie.

**Mecanismo, en parte inferido y así hay que leerlo.** Lo medido es el desborde y el valor de la celda. La explicación que encaja con los números: `MeasureCellMargins` lee `cell.resolvedStyle.marginLeft`, que en la primera pasada devuelve `0` porque los estilos no resolvieron todavía — y `0` no dispara el fallback, que solo cubre `NaN`. Después no hay segunda pasada porque `ApplyCellSize` se re-dispara con el `GeometryChangedEvent` **del contenedor**, y el contenedor no cambia de tamaño: el que crece es la superficie.

**Es la cuarta forma de equivocarse con ese margen.** El comentario del propio método cuenta que adivinarlo falló tres veces seguidas (margen; margen+borde; margen+borde+padding). Esta no es adivinarlo mal, es **medirlo demasiado temprano**, que ninguna de las tres anteriores cubría. Un valor de 0 perfectamente formado es indistinguible de uno real, exactamente como el padding de L-04.

**Sin arreglar a propósito.** 9×9 y 11×11 están recortados del lanzamiento, así que ningún jugador llega: arreglar `ApplyCellSize` cambia el dimensionado de *todos* los tableros, con riesgo real, a cambio de cero beneficio hasta que vuelvan. **Queda como precondición de esa vuelta**, junto con la decisión de ancho de L-05 — y conviene revisarlo en teléfono además de en tablet, porque acá el desborde entra igual en pantalla (691.2 < 720) y en un panel más angosto podría no entrar.

### L-07 — Las doce pantallas barridas: ninguna desborda, y la razón es estructural

**Observado (2026-08-09), Nvidia Shield Tablet, panel 720×1152.** Hasta acá solo se habían medido Home, Mode select, Board select y Game. Barrido de las siete que faltaban:

| Pantalla | Elemento fijo más bajo | ScrollView (contenido / viewport) |
|---|---|---|
| Perfil | — | 717 / 962 — no necesita scrollear |
| Tienda | `store-content` | 342 / 971 |
| Ajustes | `settings-delete-account-button` | 355 / 962 |
| Leaderboard | `leaderboard-placement-note-label` | 690 / 925 |
| Historial | `match-history-list` | **4980 / 964 — scrollea** |
| Matchmaking | `matchmaking-cancel-button` | sin ScrollView |
| Resultado | `button-row` (sobran 439) | sin ScrollView |

**El historial es el único caso real**: 4980 unidades de lista en 964 de viewport, y scrollea bien. Los demás miden chrome.

**Trampa de lectura que este barrido dejó clara.** Casi todas dieron un "sobrante" de ~32 unidades hasta el borde inferior, y **ese número no dice nada**: el contenido de esas pantallas tiene `flex-grow`, así que el elemento más bajo queda siempre a la misma distancia del borde **sea cual sea el alto del panel**. Un sobrante constante parece una medición y es una tautología. Lo que decide el encaje es **viewport contra contenido**, no la distancia al borde.

**Por qué no hizo falta repetirlo en el iPad**, que es 192 unidades más corto: restando eso a cada viewport, Perfil entra por 53 y Leaderboard por 43, el resto holgado. Y aunque no entraran, **toda pantalla de contenido variable tiene un ScrollView** que absorbe la diferencia; las dos sin scroll —Matchmaking y Resultado— tienen contenido fijo y chico. La conclusión es estructural, no de este panel en particular, y por eso se extrapola.

**Tres salvedades, y la primera importa:**

- **La tienda está vacía** (342 de contenido): el catálogo real es un pendiente del proyecto. Lo medido ahí es el chrome, no una lista poblada. **Hay que volver cuando haya productos.**
- **Los diálogos** (confirmación y toast) están en `display: None` y no se pueden medir sin dispararlos. Fuera del barrido.
- **Todo en español.** Ver la sección de idiomas en *Abierto*.

### L-08 — Los diez idiomas medidos: ninguno desborda, uno entraba por 1.8 unidades

**Observado (2026-08-09), Nvidia Shield Tablet.** Toda la UI se había encuadrado en español, y el 2026-08-09 los elementos crecieron (título a 46px, botón con `min-width: 320`, etiquetas de navegación a 15px). Medido el ancho renderizado de las diez traducciones en cada contenedor apretado:

| Contenedor | Peor idioma | Ancho / espacio | |
|---|---|---|---|
| Nav "Ajustes" | de "Einstellungen" | 94.8 / 96.6 | **98%** |
| Chip de dificultad | tr "Uyarlanabilir" | 81.6 / 95 | 86% |
| Nav "Tienda" | vi "Cửa hàng" | 68.4 / 96.6 | 71% |
| Botón *Jugar* | de "Spielen" | 117 / 208.8 | 56% |
| Cabecera de pantalla | fr "Choisissez un mode" | 211.8 / 420 | 50% |
| Título de tarjeta | it "2 giocatori locale" | 183.6 / 380 | 48% |
| Confirmar | pl "Rozpocznij grę" | 142.2 / 372 | 38% |
| Tarjeta de tablero | id "sejajarkan 5" | 61.2 / 177.6 | 34% |

**Arreglado ensanchando el botón de navegación de 124 a 136**, lo que sube el espacio de 96.6 a 108.6 y deja el alemán en 87%. Tres botones a 136 más sus márgenes suman 444 de los 672 disponibles, así que la fila sigue holgada y no desborda en vertical (termina en 1120.2 de 1152).

**Por qué 98% cuenta como defecto y no como "entra".** 1.8 unidades no son margen: lo agota un cambio de texto, un ajuste de tamaño o una sustitución de fuente en un dispositivo que no tenga el glifo. Y `.secondary-nav-label` no lleva `text-overflow`, así que el modo de falla no es truncar sino desbordar a la vista.

**La técnica es lo más reutilizable de esta entrada.** No hace falta cambiar el idioma diez veces y esperar frames: `TextElement.MeasureTextSize` devuelve el ancho renderizado de cualquier cadena con la fuente y el tamaño de ese elemento. Los diez idiomas y los ocho contenedores salen en una sola llamada, sin tocar el estado del juego.

```csharp
Vector2 size = label.MeasureTextSize(candidate, 0f, VisualElement.MeasureMode.Undefined,
                                     0f, VisualElement.MeasureMode.Undefined);
bool overflows = size.x > room;
```

**Y contar caracteres no sirve como sustituto.** La estimación previa —caracteres por medio tamaño de fuente— daba "Einstellungen" en ~98 unidades, o sea desbordando; el ancho real es 94.8. Sobreestima porque las minúsculas estrechas (i, l, t, n) pesan mucho menos que el promedio, y "Einstellungen" son casi todas. Sirve para elegir qué medir, nunca para concluir.

**Lo que esto no cubre:** la cobertura de glifos. Que un texto mida bien no prueba que la fuente tenga sus caracteres; una sustitución silenciosa se ve igual de correcta en la medición. Eso es QA de `localization-manager`.

---

## Abierto

- **Ningún build en hardware real.** Todo lo de acá es Device Simulator. Sigue siendo el chequeo que ninguna medición por MCP sustituye, aunque ya no bloquee nada puntual.
- **T-04 sin explicación.** Cerrado para este proyecto por dejar de usar `ConstantPhysicalSize`, no por entenderlo.
- **Cobertura de dispositivos.** Seis medidos: tres Apple y tres Android, con y sin notch, dos tablets. Landscape ya no es un hueco: se cerró fijando el juego a portrait.
- **Con qué criterio vuelven 9×9 y 11×11** (L-05). El recorte al lanzamiento ya está hecho; falta si vuelven para todos o por encima de cierto ancho, y **ese umbral cae entre 411dp y 600dp** — pegado a 600 es empate técnico con el mínimo táctil. `BOARD_CONFIGS` hoy no sabe discriminar por dispositivo.
- **El desborde del 11×11** (L-06) es precondición de esa vuelta, y hay que revisarlo también en teléfono.
- **T-08 sin diagnosticar.** Se fue solo después de un reinicio del Editor y varios ciclos de play, sin que se tocara nada — o sea que tampoco se sabe qué lo cura. Queda la ruta del script de Editor real de ERR-KB-005 por si vuelve.
- **Cobertura de glifos, sin verificar** (L-08). Medir el ancho no prueba que la fuente tenga los caracteres: una sustitución silenciosa mide igual de bien. Es QA de `localization-manager`.
- **La tienda vacía** (L-07): hay que rebarrerla cuando el catálogo exista.
- **Los diálogos** nunca se midieron: hay que dispararlos para poder verlos.
- **Las propiedades custom del USS espejan el `padding` a mano.** Si alguien cambia uno y no el otro, el contenido se corre y nada avisa.
- **El aire bajo el tablero** ya no es lo que sobra: la fila de reacciones de [[06-Wireframes-UI]] necesita su propia banda.

## Conexiones

- [[ERRORES_Conocidos]] — ERR-KB-004 (preámbulo obligatorio de medición de UI) y ERR-KB-003 (ruteo del MCP). Este doc extiende ese preámbulo con T-01 a T-09, y T-07 corrige el **orden** que ERR-KB-004 sugiere: `runInBackground` va antes de `Play`, no en el primer comando después.
- [[06-Wireframes-UI]] — el layout objetivo de cada pantalla, contra el que se encuadra
- [[07-Estetica-UI]] — la paleta y el estilo; los colores no se tocan al encuadrar
- [[05-UI-Pantallas-TicTacToe]] — las 12 pantallas y su navegación
- [[08-Herramienta-Arte-Placeholder]] — mismo criterio de admisión, para el arte
- `Assets/Scripts/Game/UI/SafeAreaController.cs` — el inset y las tres reglas de L-04 (archivo de código, no nota)
- `Assets/UI/Settings/MainPanelSettings.asset` — `scaleMode` y la referencia 720×1400 de L-02 (archivo de asset, no nota)
- `Assets/UI/Styles/main.uss` — `--content-padding-x/y` y el patrón de ScrollView de L-03 (archivo de código, no nota)

## Fuente

**Setup · T-01 a T-04 · L-01 (diagnóstico) · L-02 (diagnóstico)**
- **Sesión 2026-08-09**, probando el Device Simulator por primera vez desde que se instaló (2026-08-08). Sin cambios de código: todo medido en play mode y con clones de `PanelSettings` que no tocan el asset.
- **Medido por MCP** con `Unity_RunCommand`, siguiendo el preámbulo de ERR-KB-004 y con la guarda de `Application.dataPath` en cada llamada por ERR-KB-003.
- **T-01 diagnosticado** enumerando ventanas dentro y fuera de play mode: 2 contra 10, con `SimulatorWindow` ausente en el primer caso.

**T-05 · T-06 · L-01 (arreglo) · L-02 (arreglo) · L-03 · L-04**
- **Misma sesión, segunda mitad**, ya aplicando arreglos de usabilidad pedidos por el usuario a partir de capturas del Simulator en seis dispositivos.
- **Verificado midiendo cada pantalla después de cada cambio**, en iPhone 13 Pro Max y en iPad Pro 12.9" — este último elegido a propósito por ser el que menos alto de panel deja (960 contra 1558).
- **L-04 se detectó porque el arreglo se midió en vez de darse por bueno.** Visualmente la captura era ambigua; los números no: `paddingTop=0` donde iban 111.

**Galaxy J7 · Redmi 6 Pro · L-05**
- **Misma sesión, pasada de validación cruzada** sobre los dos Android que habían aparecido en las capturas y no se habían medido. Sin cambios de código.
- **Las cuatro pantallas medidas en cada uno**: Home, Mode select, Board select y Game, buscando overflow. Ninguna desbordó.
- **El Redmi es el que cierra la simetría**: 89px arriba y 0 abajo, el espejo del iPad. Que los dos den bien es lo que respalda que el controlador trate los cuatro bordes por separado.
- **Corregido en el camino:** el Galaxy J7 (2017) es 1080×1920, no 720×1280 — ese es el J7 de 2015. El panel dio 720×1280 igual, por la escala 1.5.
- **L-05 salió de medir el 11×11 en el Redmi**, el tablero más denso en el dispositivo de mayor densidad de los Android. La tabla de las cuatro medidas es aritmética sobre los 360dp de ancho, no cuatro mediciones.

**L-08**
- **Sesión 2026-08-09, cierre.** Medido con `MeasureTextSize` en lugar de cambiar el idioma diez veces: los diez locales y los ocho contenedores salen en una llamada, sin tocar el estado del juego.
- **La estimación estática previa se registró como fallida a propósito**: predecía que el alemán desbordaba y no lo hacía. Contar caracteres sobreestima; sirve para priorizar, no para concluir.
- **El arreglo se verificó midiendo de nuevo** tras ensanchar el botón: 98% → 87%.

**T-09 · L-07**
- **Sesión 2026-08-09**, barrido de las siete pantallas que nunca se habían medido, en la Shield Tablet. Sin cambios de código.
- **T-09 apareció al empezar el barrido**, encadenando `Play` con un `Show()` — el atajo obvio, y el que cuelga. Se diagnosticó comparando `Time.unscaledTime` contra `Time.time`, que es lo que distingue "el loop no corre" de "el hilo está bloqueado".
- **L-07 se extrapola al iPad a propósito** en vez de medirse ahí: la razón por la que nada desborda es estructural (ScrollView en toda pantalla de contenido variable), no del alto de este panel.

**T-08 · la verificación del bloqueo de portrait**
- **Sesión 2026-08-09, cierre.** El bloqueo se verificó en tres frentes: el `.asset`, el runtime en play mode válido (los tres flags de autorotate apagados) y una captura con el dispositivo girado.
- **T-08 apareció intentando cerrar el tercero con un número.** La idea era leer `Input.deviceOrientation` (dispositivo físico) contra `Screen.orientation` (app) en una sola llamada, que se valida sola. Nunca se pudo ejecutar.
- **Descartada la hipótesis de la preferencia de compilación** leyendo `EditorPrefs`; quedó anotada como descartada para que nadie la vuelva a probar.
- **También se registró un error de criterio propio**: haber tratado la captura como evidencia débil cuando discriminaba perfectamente entre las dos hipótesis.

**Shield Tablet · L-06 · el bloqueo de portrait · la tablet en L-05**
- **Sesión 2026-08-09, cierre**, probando la única familia de dispositivos que faltaba. El catálogo tiene **solo dos tablets Android**, las dos 1200×1920: la Shield a 320dpi (600dp) y la Sony Xperia Z2 a 240dpi (800dp). Se eligió la Shield por ser la más apretada.
- **El bloqueo de portrait salió de mirar PlayerSettings, no de medir.** `defaultInterfaceOrientation` estaba en `AutoRotation` con las cuatro orientaciones habilitadas: landscape era alcanzable por cualquier jugador y nunca se había medido. Decisión del dueño: fijar portrait en vez de soportarlo.
- **También se descartó una alarma propia**: el archivo del dispositivo declara `navigationBarHeight: 96` y nada en `Screen`/`safeArea` lo descuenta, pero `startInFullscreen=True` y `fullScreenMode=FullScreenWindow` significan que esa banda no está ocupada de forma permanente. Se levantó y se cerró en la misma pasada, mirando la configuración en vez de suponer.

**T-07 · el contraste de L-05 · la re-medición del J7**
- **Sesión 2026-08-09, última pasada**, re-midiendo el Galaxy J7 después de agrandar todos los elementos y de recortar los tableros — o sea el panel más corto contra el layout más grande. Sin cambios de código.
- **T-07 se diagnosticó desde fuera de Unity**, que es la parte que vale: con el MCP mudo, la única evidencia disponible fue `Get-Process`, la línea de comandos del relay y el `LastWriteTime` del archivo de descubrimiento. La conclusión "proceso vivo, heartbeat rancio" no se puede sacar desde adentro.
- **El 6×6 del J7 es la primera medición que contrasta la tabla de L-05** en vez de agregarle una fila.

**Todo el documento**
- **Sin verificar en dispositivo real.** Ninguna afirmación sobre hardware físico está medida: cinco dispositivos simulados, cero builds.
