---
tags: [errores, arquitectura, ui, ugs, leaderboard, localizacion, tooling, mcp, seguridad, economia]
---

# Errores Conocidos — TTTXO

## Cómo usar este archivo

Cada vez que aparezca un error en la consola o un comportamiento inesperado, **antes de diagnosticar de nuevo**:

1. Copia el mensaje de error exacto (la línea primera del warning/exception).
2. Haz `grep` en este archivo buscando fragmentos del mensaje (ej: "Object reference not set" o "GetRankedLeaderboard failed").
3. Si hay coincidencia → aplica el fix documentado y actualiza la fecha y recurrencias.
4. Si no hay ficha → procede con el diagnóstico; al cerrar, invoca al `error-historian` para documentar.

**Propósito**: evitar que un error ya diagnosticado se re-diagnostique desde cero en otra sesión. Una diagnosis completa toma horas; documentarla toma minutos.

**Qué NO va acá**: el conocimiento acumulado de encuadre de UI en pantallas de celular —geometría por dispositivo, safe area, escalado de panel, trampas del Device Simulator— vive en [[09-Encuadre-Dispositivos]], que lo mantiene `docs-changelog`. Este archivo guarda el error puntual con síntoma greppeable; aquel, la base de conocimiento de layout. Un mismo episodio puede dejar ficha en los dos: cuando pasa, se referencian en vez de duplicarse.

---

## ERR-KB-001 — NullReferenceException en LeaderboardScreenController constructor

**Estado:** resuelto-puntual · **Recurrencias:** 1 · **Última vez:** 2026-08-06

**Síntoma** (mensaje exacto, para grep):
```text
LeaderboardScreenController: GetRankedLeaderboard failed - Object reference not set to an instance of an object
```

Stack trace relacionado:
```
TTTXO.Game.UI.Screens.LeaderboardScreenController/<LoadAsync>d__17:MoveNext () (at Assets/Scripts/Game/UI/Screens/LeaderboardScreenController.cs:133)
TTTXO.Game.UI.Screens.LeaderboardScreenController:RefreshList () (at LeaderboardScreenController.cs:121)
TTTXO.Game.UI.Screens.LeaderboardScreenController:SelectBoardSize (int) (at LeaderboardScreenController.cs:109)
TTTXO.Game.UI.Screens.LeaderboardScreenController:BuildFilterChips () (at LeaderboardScreenController.cs:97)
TTTXO.Game.UI.Screens.LeaderboardScreenController:.ctor (...) (at LeaderboardScreenController.cs:51)
TTTXO.Game.UI.ScreenRouter:Initialize () (at ScreenRouter.cs:103)
TTTXO.Game.Bootstrap.AppBootstrap:Start () (at AppBootstrap.cs:19)
```

**Causa raíz:** 

El constructor de `LeaderboardScreenController` iniciaba una cadena de trabajo: `ctor → BuildFilterChips() → SelectBoardSize() → RefreshList() → LoadAsync()`, que culminaba en `RankedQueryService.GetLeaderboardAsync()` (llamada a Cloud Code). Esto ocurría durante `ScreenRouter.Initialize()`, invocado desde `AppBootstrap.Start()`, **antes de que completara `UgsInitializer.InitializeAsync()`** (que se ejecuta dentro de `SplashScreenController.RunStartupSequenceAsync()`).

La arquitectura de arranque construye todas las 12 pantallas de una y recién después navega a la Splash. Sin sesión UGS activa, el SDK devuelve una `NullReferenceException` cruda. El trabajo en constructores es particularmente peligroso porque no está protegido por la guarda de `UgsInitializer.Status` que sí existe en `OnShow()`.

**Fix:** 

En `Assets/Scripts/Game/UI/Screens/LeaderboardScreenController.cs` (líneas 51–133):

1. Separar responsabilidades en `SelectBoardSize(int)`:
   - Crear `SetSelectedBoardSize(int)`: actualiza solo el estado de selección y visualización de chips (usado por `BuildFilterChips()` en constructor, sin trabajo de red).
   - Mantener `SelectBoardSize(int)`: selecciona + recarga datos (usado por el tap del usuario en un chip después de que la pantalla esté activa).

2. Agregar guarda defensiva en `RefreshList()`: antes de llamar `RankedQueryService.GetLeaderboardAsync()`, verificar `if (UgsInitializer.Status != UgsInitStatus.Ready)`, mostrar `UiText.Leaderboard.UnavailableMessage` y retornar. Nunca dejar escapar una excepción del SDK.

3. Mover la primera carga de datos a `OnShow()` (ya correctamente implementado en el método existente).

**Prevención:**

- **Regla de constructor**: todo controlador de pantalla en `OnShow()` es seguro; en el constructor, **solo binding de elementos (Q<T>), wiring de callbacks e inicialización de estado visual**. Nunca:
  - Llamadas de red (async/await, Task, `_ = SomethingAsync()`).
  - Acceso a `RankedQueryService`, `OnlineMatchService`, `GameConfigService`, `PlayerDataService`, `MatchmakingService`, `CloudCodeService`, `AuthenticationService`, `PlayerNameService`.
  - Cualquier operación que dependa de sesión UGS.

- **Cómo detectarlo**: revisar el constructor y toda la cadena de llamadas que invoca (búsqueda recursiva); si llega a métodos `async` o servicios de UGS, es problemático.

- **Degradación defensiva obligatoria**: toda operación que dependa de UGS que no esté en `OnShow()` debe envolver su resultado con guarda de estado (`if (UgsInitializer.Status != UgsInitStatus.Ready)`) y never re-lanzar excepciones del SDK — mostrar un mensaje visible al usuario o silenciarlas con try/catch.

- **Auditoría realizada**: se revisaron los 12 controladores de pantalla más `ConfirmDialogController`, `ToastController` y `ScreenRouter`. Este fue el **único caso**; el resto ya tenía el patrón correcto (trabajo en `OnShow`, constructor limpio). La próxima sesión que toque este código no necesita volver a auditar constructores de pantalla.

## ERR-KB-002 — "No translation found" con la clave correctamente definida

**Estado:** resuelto-sistémico · **Recurrencias:** 1 · **Última vez:** 2026-08-08

**Síntoma** (mensaje exacto, para grep):
```text
No translation found for 'Profile/RankedPlacementFormat'
```

Aparece **renderizado dentro de la UI del juego**, en el lugar donde debería ir el texto, no en la consola. El patrón es `No translation found for '<Tabla>/<Clave>'` y puede tocar cualquier clave.

**Causa raíz:**

La clave **sí estaba definida** en `LocalizationSetup.Translations` (`Assets/Scripts/Game/Editor/LocalizationSetup.cs`), con sus 10 traducciones completas. Nada faltaba en el código fuente.

Lo que faltaba eran los **assets generados**. `LocalizationSetup` es tooling de Editor de una sola pasada: lee ese diccionario y escribe los `StringTable` en `Assets/Localization/Tables/`. En runtime, Unity Localization lee **los assets, nunca el diccionario**. Agregar una entrada al diccionario no cambia nada hasta que alguien vuelve a correr la generación.

O sea que el diccionario y las tablas son dos fuentes que se desincronizan en silencio, y la única señal de que pasó es el texto roto en pantalla.

**Fix:**

Correr `TTTXO → Setup → Create Localization Assets` desde el menú del Editor. Regenera las 10 tablas desde el diccionario, agregando las claves nuevas.

**Prevención:**

- **Regla**: agregar una entrada a `LocalizationSetup.Translations` es la **mitad** del trabajo. La otra mitad es correr el menú de generación y commitear los `.asset` modificados de `Assets/Localization/Tables/`. Un commit que agrega una clave y no toca ningún `.asset` está incompleto.
- **Cómo detectarlo antes de que llegue al juego**: si un commit modifica `LocalizationSetup.cs` sin modificar nada bajo `Assets/Localization/Tables/`, es sospechoso.
- **Por qué no se rompe la compilación**: `UiText.Localize(clave, fallback)` recibe un texto por defecto, así que el código compila y corre igual. La falla solo se ve mirando la pantalla — ningún test ni la consola la reportan.
- **Alcance real**: esto aplica a cualquier clave, no solo a la de Ranked. Al ver una, conviene correr la generación una vez y revisar las demás pantallas, en vez de arreglar clave por clave.

## ERR-KB-003 — el MCP de Unity responde por otro proyecto

**Estado:** resuelto-verificado (a nivel config) · **Recurrencias:** 2 · **Última vez:** 2026-08-08 (noche)

**Síntoma** (mensaje exacto, para grep):
```text
ExecuteMenuItem failed because there is no menu named 'TTTXO/Setup/Create Localization Assets'
```

Otras formas del mismo síntoma, todas engañosas porque suenan a bugs propios:

```text
LocalizationSetup type not found
Unity not detected (no fresh discovery files found)
```

Y la señal más confusa: la consola devuelve logs de archivos que no existen en este repo (ej. `Assets\Scripts\SharedRules\Logic\BoosterRules.cs`, que es de TCGMaster), o advertencias de Plastic sobre otra organización.

**Ojo con el segundo síntoma: tiene dos causas y una es inocente.** `Unity not detected (no fresh discovery files found)` también aparece cuando el Editor está reimportando —cambio de plataforma, recompilación larga— porque el descriptor de `~/.unity/mcp/connections/` desaparece mientras dura el import y vuelve al terminar. Antes de dar por roto el ruteo, mirar el título de la ventana del Editor:

```powershell
Get-Process Unity | Select-Object Id, MainWindowTitle
```

Si dice `Importing…` o `busy for Ns`, no hay nada que arreglar: hay que esperar.

**Confirmación en una línea** — antes de teorizar nada, correr esto por `Unity_RunCommand`:

```csharp
result.Log(Application.dataPath);
```

Si no imprime `D:/UnityProjects/TTTXO/Assets`, el problema es el ruteo, no el código.

**Causa raíz:**

Esto no es el paquete de comunidad "MCP for Unity" ni ninguno de los forks (IvanMurzak, CoderGamester, CoplayDev). Es el relay oficial de Unity: `~/.unity/relay/relay_win.exe` (`unity-ai-relay`), con el puente del lado Editor dentro de `com.unity.ai.assistant`. Usa **named pipes, no TCP** — así que nada de puertos, `netsh portproxy`, reglas de firewall ni el enredo de WSL2 aplica acá. Tampoco aplica el conflicto de `System.Collections.Immutable` de Unity 6.3: ese es justamente contra el paquete que sí usamos.

Cada Editor con MCP activo publica un descriptor en `~/.unity/mcp/connections/`, con el nombre `bridge-{sha1(dataPath)[0..8]}-{pid}.json`, y abre su propio pipe `\\.\pipe\unity-mcp-{hash}-{pid}`. O sea que **varios Editores en paralelo es el caso soportado**, no una limitación: cada uno tiene su canal.

El problema es del lado del cliente. El relay acepta `--project-path <ruta>` ("Connect to Unity instance with this project path") y `--instance-id <pid>`. Registrado **sin** esos flags, engancha un bridge cualquiera de los vivos. Mientras haya un solo Editor abierto no se nota; al abrir el segundo, la elección es efectivamente una lotería — y peor, **puede cambiar en mitad de una sesión**: cuando el bridge de TTTXO se reinició (salida de play mode + domain reload), el relay reconectó y se quedó con el de TCGMaster, sin avisar.

**Reproducción completa (2026-08-08, noche).** La primera vez el salto se infirió; esta vez se vio entero, y agrega tres datos que la ficha original no tenía:

1. **El disparador es entrar a play mode, no salir.** Secuencia exacta: `Unity_ManageEditor Play` → responde `Entered play mode` (llegó al Editor correcto) → la **llamada inmediatamente siguiente** ya contestó `dataPath = D:/UnityProjects/TCGMaster/Assets`. La ventana de riesgo es de una sola llamada: cualquier domain reload sirve, y entrar a play mode es uno.
2. **No se recupera solo.** Dos pings seguidos siguieron dando el proyecto equivocado (`SampleScene` en vez de `Main`). Esperar no sirve; hubo que **cerrar el Editor de TCGMaster** para que el relay volviera a TTTXO. Con un solo Editor vivo el ruteo es determinista por construcción.
3. **La config estaba bien y no se usaba.** Los dos proyectos tenían su registro local correcto con `--project-path`, y aun así los **tres** `relay_win.exe --mcp` vivos estaban lanzados sin ningún flag: los de la entrada de scope user. O sea que tener el registro por proyecto no alcanza mientras exista la entrada global — que es exactamente lo que la ficha original recomendaba conservar "como fallback". Esa recomendación era el bug.

**Fix:**

Registrar el servidor en scope local, fijado a este proyecto:

```
claude mcp add unity-mcp -- "C:/Users/User/.unity/relay/relay_win.exe" --mcp --project-path "D:/UnityProjects/TTTXO" --name TTTXO
```

Scope local (`~/.claude.json`, bajo la clave de este proyecto) y no un `.mcp.json` versionado a propósito: la ruta del relay es de la máquina, así que un archivo en el repo rompería en otra.

**Y —esto es lo que faltaba— borrar la entrada de scope user**, que es la que estaba ganando:

```
claude mcp remove unity-mcp -s user
```

Hecho el 2026-08-08 (noche), con backup en `~/.claude.json.bak-mcp-routing`. Queda `claude-design` como única entrada global; `unity-mcp` existe solo por proyecto. **Costo aceptado**: cualquier otro proyecto Unity se queda sin MCP hasta que se le registre el suyo con su `--project-path`. Es deliberado — un comodín global que engancha cualquier Editor es peor que no tener servidor, porque falla escribiendo en el proyecto equivocado en vez de fallar avisando.

Que el flag funciona está verificado hablándole al relay directo, sin Claude Code de por medio:

| argumentos | responde |
|---|---|
| `--mcp --project-path D:/UnityProjects/TTTXO` | TTTXO ✅ |
| `--mcp --project-path D:/UnityProjects/TCGMaster` | TCGMaster ✅ |
| `--mcp` (sin fijar) | el que toque |

Las barras normales sirven igual que las invertidas, aunque el descriptor guarde la ruta con invertidas.

**El paso que falta, y que no es obvio:** agregar la config **no alcanza**, y reiniciar la conversación tampoco. El servidor MCP **está compartido entre las sesiones de Claude Code**, no es uno por sesión: lo hostea un `claude.exe` cualquiera y respawnea con los argumentos capturados cuando *esa* sesión arrancó. Se verifica así:

```powershell
Get-CimInstance Win32_Process -Filter "Name='relay_win.exe'" |
  Select-Object ProcessId, ParentProcessId, CreationDate, CommandLine | Format-List
```

Si el `--mcp` que aparece no lleva `--project-path`, la config nueva no se está usando por más que `claude mcp get` diga *Connected* — eso reporta el archivo de config, no el proceso vivo. Hay que **cerrar la app de Claude Code entera** (todas las ventanas, todas las conversaciones), no solo la sesión, y volver a abrir.

Mientras tanto, matar los procesos `relay_win.exe --mcp` (los que **no** llevan `--relay`; esos son de los Editores y no se tocan) fuerza un respawn y suele reengancharlo bien, pero sigue siendo la lotería: puede volver a saltar.

**Prevención:**

- **Guarda en cada comando destructivo o de escritura.** Mientras el ruteo no sea determinista, todo `Unity_RunCommand` que escriba algo abre con:
  ```csharp
  if (!Application.dataPath.Replace('\\', '/').EndsWith("TTTXO/Assets", StringComparison.OrdinalIgnoreCase))
  { result.LogWarning("WRONG PROJECT: " + Application.dataPath); return; }
  ```
  Aborta antes de tocar nada en vez de escribir en el proyecto equivocado.
- **Re-verificar después de cada domain reload.** El salto original pasó al salir de play mode; el de la segunda vez, al entrar. Entrar/salir de play mode, recompilar y cambiar de plataforma son todos momentos de riesgo, y la ventana es de **una sola llamada**: el comando que dispara el reload todavía llega bien, el siguiente ya puede estar en el otro proyecto.
- **`Play` y `Stop` cuentan como comandos de escritura.** La guarda de `dataPath` se pensó para `Unity_RunCommand`, pero `Unity_ManageEditor` con `Play`/`Stop` también actúa sobre el Editor: mal ruteado, mete en play mode el proyecto de otra sesión. En la reproducción de esta noche todas las llamadas mal ruteadas fueron de lectura y por eso no hubo daño — fue suerte, no diseño.
- **Regla**: un Editor por proyecto es normal; lo que no puede quedar sin fijar es el cliente. Cada proyecto con su `unity-mcp` de scope local apuntando a su ruta, y **ninguna entrada de scope user**. Dejarla "como fallback para proyectos sin fijar" fue justamente lo que hizo recaer el error: la entrada global es la que termina viva. Un proyecto sin registro propio tiene que quedarse sin MCP, no heredar un comodín.
- **Mientras haya dos Editores abiertos, el ruteo no es confiable ni con la config correcta.** Si el fix de fondo todavía no está verificado, cerrar el otro Editor es el atajo que funciona seguro.
- **Una sesión de Claude Code por proyecto.** Ojo que, por lo del servidor compartido, esto ayuda al aislamiento de contexto pero **no** garantiza por sí solo el ruteo.
- **No confiar en el log del relay**: imprime `Project path: (not specified)` aunque el flag sí se esté respetando. La única verificación válida es preguntarle el `Application.dataPath` al Editor.
- **Ante cualquier resultado raro del MCP** (un menú que "no existe", un tipo que "no se encuentra", logs de archivos ajenos), el primer paso es confirmar la instancia, no leer código. En esta sesión se perdió tiempo persiguiendo un menú que sí existía.

## ERR-KB-004 — medir la UI por MCP devuelve números falsos pero plausibles

**Estado:** resuelto-sistémico · **Recurrencias:** 1 · **Última vez:** 2026-08-08 (noche)

**Síntoma** — no hay mensaje de error: los comandos dicen `executed successfully` y devuelven medidas. El problema es que las medidas mienten. Cuatro formas, en orden de peligrosidad:

```text
CELL [NaN]x[NaN]            → layout nunca calculado
CONTAINER ... [0]x[0]       → el elemento existe pero está oculto
Screen = [640]x[480]        → valor rancio; el panel real era 272x484
(números correctos, pero de edit mode y no de play mode)
```

La última es la grave: **son números reales, bien formados y de la pantalla equivocada**. No hay forma de detectarla mirando los valores.

**Causa raíz:** cuatro cosas distintas, todas del entorno y ninguna del layout.

1. **El Editor en segundo plano no tickea.** Sin foco y con `runInBackground` apagado, el player loop se congela: `Time.frameCount` no avanza y el layout de UI Toolkit nunca se recalcula. Todo lo que se muestre por código después de eso queda en 0/NaN. La señal es `Time.time` clavado entre dos llamadas.
2. **Las pantallas ocultas miden 0.** Un `display: none` deja el elemento vivo y consultable, con tamaño 0 y posiciones viejas. Consultar sin verificar visibilidad da ceros que parecen un bug de layout.
3. **`Screen.width`/`Screen.height` quedan rancios** cuando el Game view no renderiza. Reportaron 640×480 durante toda una sesión congelada; el panel real era 272×484. La medida buena es `doc.rootVisualElement.parent.layout`.
4. **El UIDocument renderiza igual en edit mode.** Si el play mode se cerró —y se cierra solo al cambiar de plataforma— las consultas siguen funcionando y devuelven el layout de edit mode. `Application.isPlaying` puede decir `True` mientras `EditorApplication.isPlaying` dice `False`.

**Fix / procedimiento.** Toda medición de UI por MCP arranca con este preámbulo, en este orden:

```csharp
// 1. ¿Es play mode de verdad? (edit mode mide igual y miente)
result.Log("isPlaying={0}", EditorApplication.isPlaying);
// 2. Que el juego corra con el Editor de fondo. OJO: SÍ persiste (ver abajo).
Application.runInBackground = true;
// 3. Tamaño del panel, nunca Screen.width
result.Log("panel={0}", doc.rootVisualElement.parent.layout);
// 4. ¿La pantalla que voy a medir está visible?
result.Log("display={0}", screen.resolvedStyle.display);
```

Y para navegar por código: **esperar a que el splash termine.** `SplashScreenController.RunStartupSequenceAsync()` hace `await` de la init de UGS (red) y recién ahí llama `Show(ScreenId.Home)`. Un `Show(ScreenId.Game)` lanzado antes se ve pisado segundos después, sin error, y lo que queda es una pantalla oculta midiendo 0. Hay que pollear hasta ver `screen-home` visible.

**Corrección (2026-08-08, noche — la ficha decía lo contrario).** `Application.runInBackground = true` **sí persiste** a `ProjectSettings.asset`. La verificación original —mirar el archivo justo después de asignarlo y ver `runInBackground: 0`— era **prematura**, no falsa: la asignación cambia el valor en memoria y Unity recién serializa `ProjectSettings.asset` más tarde (al guardar o al cerrar el Editor). La sesión siguiente abrió el repo y encontró el diff:

```diff
-  runInBackground: 0
+  runInBackground: 1
```

**El mecanismo, confirmado el 2026-08-08 al revertirlo:** `Application.runInBackground` y `PlayerSettings.runInBackground` son **la misma variable**. Leídas juntas antes y después de tocar una, se mueven las dos:

```text
before: PlayerSettings.runInBackground=True   Application.runInBackground=True
after:  PlayerSettings.runInBackground=False  Application.runInBackground=False
```

O sea que no es un flag de runtime que se filtra al proyecto: asignarlo en runtime **es** editar ProjectSettings. Lo único que llega tarde es la serialización a disco. Eso explica la observación en vez de solo registrarla.

**Y determina cómo se revierte.** `git checkout -- ProjectSettings/ProjectSettings.asset` es la forma equivocada con el Editor abierto: devuelve el archivo pero deja el valor vivo en `True`, y Unity lo reescribe en el próximo guardado. La forma que funciona es por el Editor, que arregla memoria y disco a la vez:

```csharp
PlayerSettings.runInBackground = false;
AssetDatabase.SaveAssets();
```

O sea que el preámbulo de medición **ensucia el working tree**, con retardo y sin avisar. Consecuencias prácticas:

- **Chequear `git diff -- ProjectSettings/ProjectSettings.asset` antes de cualquier commit** después de una sesión de medición. Es la segunda vez que este flag se cuela.
- **No verificar "no persiste" leyendo el archivo enseguida.** Para settings del Editor, el archivo en disco va atrasado respecto de la memoria; la lectura inmediata siempre va a confirmar lo que uno quiere oír.
- Durante la sesión el flag en `1` es **útil** (es justamente la trampa 1), así que conviene revertirlo al cerrar, no al medir.

**Prevención:**

- **La resolución del Game view no se puede cambiar por código.** El sandbox de `Unity_RunCommand` rechaza `System.Reflection` (`Script uses one or more unauthorized namespaces`), que es la única vía para tocar `GameViewSizes`. Hay que pedirla a mano o usar el Device Simulator (`com.unity.device-simulator.devices`, instalado 2026-08-08). **Y ojo con el orden de este preámbulo**: `runInBackground` hay que ponerlo en `true` **antes** de entrar a play mode, no en el primer comando después. Con el flag en `false` y el Editor sin foco, entrar a play congela el loop que refresca el heartbeat del bridge del MCP, y el comando que arreglaría el flag es justamente el que ya no llega — punto muerto que solo se destraba dándole foco a Unity a mano. Ficha completa en [[09-Encuadre-Dispositivos]] (T-07).

**Y si `Unity_RunCommand` empieza a fallar solo dentro de play mode** con `Could not find file ...AssistantRunCommand\...Dynamic.<guid>.dll`, es T-08 de [[09-Encuadre-Dispositivos]]: sin diagnosticar, sobrevive al reinicio del Editor, y **no** es la preferencia de compilación durante play (ya descartada). La vía alternativa es la de ERR-KB-005: script de Editor real en `TTTXO.Game.Editor` más `Unity_GetConsoleLogs`.

**El Simulator trae sus propias trampas**, nueve encontradas el 2026-08-09: `Maximize On Play` lo saca de juego en silencio, el play mode puede arrancar pausado, una lectura puede salir mitad Simulator mitad Game view, cambiar de dispositivo reinicia el play mode, y con la pestaña Game abierta al lado el play mode se lo queda ella — este último **no lo detecta el chequeo de coherencia**, porque los valores son consistentes entre sí y solo están mal en conjunto. Están en [[09-Encuadre-Dispositivos]] (T-01 a T-09) con sus señales de detección; este preámbulo se extiende con las de allá antes de medir contra un dispositivo.
- **Una medición sola no verifica un layout.** Los números del anclaje del tablero dieron exactos a 720×1400 y no probaban nada de la rama corregida: a esa resolución manda el ancho. Recién a 1280×400 —donde manda el alto— se ejercitó el código que el commit arreglaba. Elegir la resolución que ejercita la rama, no la que está a mano.
- **Dos lecturas seguidas para detectar congelamiento.** Si `Time.frameCount` no cambió entre dos llamadas, nada de lo que se mida vale.

## ERR-KB-005 — correr los tests por MCP "funciona" y no devuelve resultados

**Estado:** resuelto-sistémico · **Recurrencias:** 1 · **Última vez:** 2026-08-08 (noche)

**Síntoma** — no hay error. `Unity_RunCommand` contesta `Command executed successfully`, el `TestRunnerApi.Execute()` se lanza, y después:

```text
Unity_GetConsoleLogs → {"logs": [], "totalCount": 0}
```

Console vacía, cero resultados, y ningún archivo de resultados en `Library/`. Parece que los tests no corrieron; en realidad corrieron y el reporte se perdió.

**Causa raíz:** dos cosas encadenadas.

1. **`TestRunnerApi.Execute()` es asíncrono** y devuelve el control enseguida. El `ExecutionResult` del comando ya se cerró cuando llegan los callbacks, así que `result.Log()` desde `RunFinished` no va a ningún lado.
2. **El run de EditMode dispara un domain reload**, que descarga el assembly dinámico del `CommandScript` — con él se van los callbacks registrados — y de paso **limpia el buffer de logs del bridge**. Por eso `GetConsoleLogs` devuelve `totalCount: 0` en vez de logs viejos: no es que no se logueó, es que el buffer se reinició.

O sea que `Debug.Log` desde los callbacks tampoco sirve: el reload se lleva las dos vías a la vez.

**Fix:** correr el run **sincrónico**, que completa dentro de `Execute()` y deja los resultados disponibles para `result.Log()`:

```csharp
var settings = new ExecutionSettings(new Filter { testMode = TestMode.EditMode });
settings.runSynchronously = true;          // ← esto es todo
var api = ScriptableObject.CreateInstance<TestRunnerApi>();
api.RegisterCallbacks(this);
api.Execute(settings);
api.UnregisterCallbacks(this);
result.Log("SUMMARY passed={0} failed={1}", _passed, _failed);   // ya están
```

Con eso los 76 tests EditMode corrieron en 3.5s y el reporte volvió entero en la respuesta del comando.

**Prevención:**

- **El `CommandScript` no puede tener clases anidadas.** El preprocesador del MCP las hoistea fuera de la clase y la compilación revienta con `CS1527: Elements defined in a namespace cannot be explicitly declared as private`. La forma que funciona es una sola clase plana: `internal class CommandScript : IRunCommand, ICallbacks`, registrándose a sí misma con `RegisterCallbacks(this)`.
- **Nada asíncrono sobrevive a un `Unity_RunCommand`.** Vale para tests, para imports y para cualquier API con callbacks: o corre sincrónico, o el resultado hay que persistirlo a disco desde un script del Editor **de verdad** (compilado en `TTTXO.Game.Editor`), no desde el assembly dinámico.
- **Console vacía después de una operación pesada ≠ no pasó nada.** `totalCount: 0` es señal de buffer reiniciado por reload, no de silencio. Para saber si el Editor terminó, preguntar `EditorApplication.isCompiling` / `isUpdating`.
- **Correr los tests deja el `dataPath` chequeado gratis**: conviene meter la guarda de ERR-KB-003 en el mismo comando y loguear el `dataPath` junto al summary, así el resultado queda atado al proyecto donde corrió.

## ERR-KB-006 — el premio online se acredita en una clave que el jugador puede escribir

**Estado:** **cerrado en código el 2026-08-12** — *sin verificar contra el backend todavía*: falta deployar el módulo y volver a correr la sonda de `Tools\quickmatch-e2e.ps1`, que es lo único que prueba que la escritura ahora se rechace · **Recurrencias:** 1 · **Última vez:** 2026-08-09

**Síntoma** — no hay error, no hay excepción, no hay log. Todo el camino de recompensa online funciona
exactamente como está especificado: `Tools\quickmatch-e2e.ps1` valida contra el backend real la tabla de
premios (12 por victoria, 5 por empate, 0 por derrota), el tope de 3 victorias pagadas por rival cada 24 h,
y que el saldo final en Cloud Save sea la suma exacta. Los 5 asserts de economía pasan.

Lo que no se ve por ahí es esto, la sonda del final del mismo script:

```text
--- probe: can a player overwrite their own wallet? ---
  ACCEPTED - player wrote balance=999999 directly with their own token
```

**Causa raíz:**

`OnlineRewardStore` acredita la clave `currency` con `gameApiClient.CloudSaveData.SetItemAsync(...)` —
Player Data en **clase de acceso `Default`**, que es escribible por el propio jugador contra la API REST
de Cloud Save, con su token, sin SDK y sin que el módulo se entere.

Y no es un descuido aislado: el cliente **ya escribe esa misma clave a propósito** en
`Assets/Scripts/Game/Services/PlayerDataService.cs` (`SaveAfterMatchAsync`), porque la economía offline
de 1P/local es local-first con PlayerPrefs como fuente de verdad y Cloud Save como espejo. El defecto es
que el premio online —que sí se calcula server-side, con topes, con anti-colusión— **aterriza en la misma
clave que el cliente tiene permiso de pisar**. Mientras eso siga así, toda esa maquinaria es decorativa:
alcanza un POST para saltearla entera.

Verificado que se generaliza a las otras claves autoritativas escritas con el mismo patrón
`GetItemsAsync`/`SetItemAsync`:

| Clave | Escrita por el módulo en | Escritura directa del jugador |
|---|---|---|
| `currency` | `OnlineRewardStore` | **aceptada** |
| `onlineCurrencyLedger` | `OnlineRewardStore` (contadores de tope diario) | **aceptada** |
| `rankedProfile` | `RankedProfileStore` (MMR, placements, temporada) | **aceptada** |

**Esta tabla estaba incompleta.** Al aplicar el fix el 2026-08-12 se grepeó el módulo entero por el
patrón en vez de confiar en la lista, y aparecieron **dos claves más**, las dos por-rival y por-día,
las dos escribibles:

| Clave | Escrita por | Qué protege |
|---|---|---|
| `rivalWin_{rivalId}_{día}` | `OnlineRewardStore` | tope de 3 victorias pagadas por rival por día |
| `rankedWin_{rivalId}_{tablero}_{día}` | `RankedRewardStore` | **anti-boosting de MMR** (amortiguación por rival repetido) |

La segunda es la que más duele: resetearla da Elo sin amortiguar contra el mismo rival, indefinidamente.
`RankedRewardStore` no figuraba en el diagnóstico original porque la búsqueda había sido por clave, no
por patrón de I/O — y el defecto nunca estuvo en las claves, estuvo en el patrón.

`rankedProfile` es la más delicada de las tres, porque el MMR que guarda es el que
`CompleteOnlineMatchAsync` snapshotea como entrada del cálculo de Elo, y el que termina publicándose en
el leaderboard de Ranked — un modo ya deployado.

El estado de partida **no** está afectado: `MatchStateStore` usa `GetCustomItemsAsync`/`SetCustomItemAsync`
(Custom Data por `matchId`), que es otra superficie.

**Fix (2026-08-12): la primera de las tres salidas — dos claves separadas.** Decisión del dueño, tomada
al preparar el repo para hacerse público, porque publicar convertía un defecto conocido en un defecto
conocido *por todos*: el `cloudProjectId` viaja en cualquier APK, pero el repo agrega el mapa de qué
claves son escribibles y tres scripts E2E ya apuntados al proyecto.

| Clave | Antes | Ahora | Por qué esa clase |
|---|---|---|---|
| `authoritativeCurrency` *(nueva)* | — | **Protected** | el jugador ve su saldo, no lo escribe; es donde aterrizan los premios online |
| `currency` | Default | **Default, sin cambios** | sigue siendo el espejo local-first de 1P/2P, que el cliente posee |
| `onlineCurrencyLedger` | Default | **Private** | el jugador no tiene por qué leer sus propios contadores de tope |
| `rivalWin_*` | Default | **Private** | ídem, anti-farming |
| `rankedProfile` | Default | **Protected** | se muestra en perfil y resultado; solo Cloud Code lo escribe |
| `rankedWin_*` | Default | **Private** | anti-boosting: leerlo ya diría cuándo parar |

**Lo que el fix NO arregla, a propósito.** Para 1P y 2P local el servidor **no puede verificar que la
partida ocurrió**, y ningún diseño lo cambia. Por eso `currency` sigue escribible: bloquear el espejo de
una economía que el servidor nunca ve no compra nada. Lo que sí cambia es que los premios *online* —los
que sí se calculan server-side, con topes y anti-colusión— dejaron de aterrizar en esa clave. La
maquinaria dejó de ser decorativa; antes alcanzaba un POST para saltearla entera.

**Migración: no hay.** Las claves cambian de clase de acceso, así que lo que había en `Default` no se lee
desde `Protected`/`Private`. En la práctica, en el environment `development`: los `rankedProfile`
existentes **se pierden y el MMR vuelve al inicial**, y los contadores de tope arrancan de cero una vez.
Se aceptó porque son datos de prueba, pero **el leaderboard de Ranked ya tiene entradas** y esas no se
borran solas: van a quedar puntajes sin perfil detrás hasta el próximo reset de temporada.

**`GameProtocol.Version` 1 → 2.** Las formas de los RPC no cambiaron, pero qué clave acredita un RPC es
parte del contrato desde el punto de vista del cliente: un cliente v1 lee solo `currency`, así que contra
un servidor v2 juega perfecto y **deja de ver sus ganancias online sin ningún error**. El APK `b4` que ya
está en manos de un tester es exactamente ese cliente v1.

**Lo que esta verificación NO prueba.** Se confirmó que las tres escrituras son **aceptadas**. No se llevó
el caso hasta el final: falta probar que un `rankedProfile` forjado (MMR alto + `PlacementsPlayed` al borde
de completar colocación) efectivamente termine en una entrada del leaderboard tras una serie. Se cortó ahí
a propósito — el defecto está en que la escritura se acepte, y completarlo ensuciaba el leaderboard de
`development` sin cambiar ni el diagnóstico ni el arreglo.

**Prevención:**

- **La clase de acceso de Cloud Save es parte del modelo de seguridad, no un detalle de I/O.** Escribir un
  store nuevo copiando `GetItemsAsync`/`SetItemAsync` de otro store deja la clave en `Default` sin pedirlo
  y sin avisar. Todo lo que el servidor trate como autoritativo va en `Protected` o en custom data privada.
- **Comentario `ACCESS CLASS` greppeable en cada store**, declarando la clase elegida y por qué. Hoy no hay
  ninguno en el módulo: por eso las tres claves quedaron iguales sin que nadie lo decidiera.
- **La pregunta que hay que hacerse ante cada clave nueva** no es "¿el cliente necesita leer esto?" sino
  "¿qué pasa si el cliente lo escribe?". Para `currency` la respuesta era conocida y aceptable en M2
  (economía offline); dejó de serlo en M4, cuando la misma clave empezó a recibir premios calculados
  server-side, y nadie volvió a hacerse la pregunta.
- **Un harness end-to-end es donde esto aparece.** Ningún test de Editor lo hubiera encontrado: el módulo
  se comporta perfecto: hay que golpear la API REST con el token del jugador, que es justo lo que un
  cliente modificado haría.

## Conexiones

- [[03-Arquitectura-UGS-TicTacToe]] — ciclo de inicialización de UGS y sincronización de sesión
- [[05-UI-Pantallas-TicTacToe]] — ciclo de vida `OnShow()` / `OnHide()` de controladores de pantalla
- [[02-GDD-TicTacToe]] — roster de 10 idiomas y la regla de que ningún texto se hardcodea
- [[09-Encuadre-Dispositivos]] — base de conocimiento de encuadre en celulares; extiende el preámbulo de medición de ERR-KB-004 con las trampas del Device Simulator
- `Assets/Scripts/Game/UI/ScreenRouter.cs` — orden de construcción de pantallas vs. punto de navegación a Splash (archivo de código, no nota)
- `Assets/Scripts/Game/Editor/LocalizationSetup.cs` — el diccionario y el generador de tablas (archivo de código, no nota)
- `CloudCode~/TicTacToeModule/OnlineRewardStore.cs` y `RankedProfileStore.cs` — las tres claves de ERR-KB-006 y el patrón `GetItemsAsync`/`SetItemAsync` que las dejó en clase `Default` (archivos de código, no nota)
- `Assets/Scripts/Game/Services/PlayerDataService.cs` — el espejo local-first que escribe `currency` desde el cliente (archivo de código, no nota)

## Fuente

**ERR-KB-001**
- **Diagnosticado por:** `systems-programmer` (seguimiento de stack trace en ciclo de bootstrap) y `ui-programmer` (validación de patrón de ciclo de vida en arquitectura de pantallas).
- **Arreglado en:** sesión 2026-08-06, rama `fix/leaderboard-controller-null-reference`; mergeado a `main` el 2026-08-08.
- **Verificado:** ejecución en Editor sin que aparezca el warning; UI de Leaderboard accesible desde Splash post-auth.

**ERR-KB-002**
- **Detectado por:** el usuario, en una captura del Profile corriendo — el texto roto era visible en pantalla.
- **Arreglado en:** sesión 2026-08-08, corriendo `TTTXO → Setup → Create Localization Assets`.
- **Verificado:** tablas regeneradas y commiteadas junto al fix; la clave dejó de faltar.

**ERR-KB-003**
- **Detectado por:** dos sesiones en paralelo (TTTXO y TCGMaster) que llegaron al mismo diagnóstico desde los dos lados: cada una veía su MCP respondiendo por el proyecto de la otra.
- **Diagnosticado leyendo** `ServerDiscovery.cs` y `MCPConstants.cs` del paquete `com.unity.ai.assistant`, más `relay_win.exe --help` para los flags. Verificado con `Application.dataPath`.
- **Arreglado en dos pasos:** sesión 2026-08-08 (tarde), registrando `unity-mcp` en scope local fijado por proyecto, en TTTXO y en TCGMaster — insuficiente. Sesión 2026-08-08 (noche), borrando además la entrada de scope user, que era la que efectivamente se instanciaba.
- **Reproducido de punta a punta** en la sesión de la noche: entrar a play mode por MCP y ver la llamada siguiente contestar por TCGMaster, con los tres `relay_win.exe --mcp` vivos lanzados sin `--project-path` pese a que ambos proyectos tenían su registro local correcto.
- **Verificado el 2026-08-08 (noche), tras cerrar la app entera de Claude Code.** Los dos chequeos pendientes dieron bien:
  - queda **un solo** `relay_win.exe --mcp` vivo (antes eran tres) y su línea de comandos es `C:/Users/User/.unity/relay/relay_win.exe --mcp --project-path D:/UnityProjects/TTTXO --name TTTXO`. El otro `relay_win.exe` que aparece lleva `--relay` y es del Editor: ese no se toca;
  - `Application.dataPath` = `D:/UnityProjects/TTTXO/Assets`, `productName` = `TTTXO`.
- **Lo que esta verificación NO prueba.** Al correrla había **un solo Editor abierto** (los otros cuatro `Unity.exe` eran AssetImportWorkers de TTTXO, y había un único descriptor en `~/.unity/mcp/connections/`). Con un Editor solo el ruteo es determinista por construcción, así que el escenario de contención —que es el del bug— no se ejercitó. Lo que sí queda cerrado es la parte que fallaba: que el proceso vivo **naciera sin** `--project-path`. Combinado con la tabla de arriba, que ya probó por separado que el flag discrimina, la evidencia es fuerte pero no completa. **El chequeo que falta es con el Editor de TCGMaster abierto**, entrando a play mode y pidiendo `dataPath` en la llamada siguiente.

**ERR-KB-004**
- **Detectado por:** la verificación del anclaje del tablero (`433af2a`) en la sesión 2026-08-08 (noche), donde las cuatro trampas aparecieron una tras otra antes de conseguir una medición válida.
- **Diagnosticado** comparando `Time.frameCount` entre llamadas, el `layout` del panel contra `Screen.width`, y `EditorApplication.isPlaying` contra `Application.isPlaying`.
- **Verificado:** con el preámbulo aplicado, las mediciones a 272×484, 720×1400 y 1280×400 dieron los valores esperados y reproducibles.
- **Corregido después:** la afirmación de que `runInBackground` no persiste era una verificación prematura. Se cayó sola al abrir el repo en la sesión siguiente y encontrar el flag en `1` en el working tree. El mecanismo se confirmó al revertirlo, leyendo `PlayerSettings.runInBackground` y `Application.runInBackground` en la misma llamada antes y después: son la misma variable.

**ERR-KB-005**
- **Detectado por:** el primer intento de correr los 75 tests EditMode dentro del Editor (pendiente urgente #1), que devolvió `executed successfully` y console vacía.
- **Diagnosticado** descartando en orden: que el Editor estuviera ocupado (`isCompiling`/`isUpdating` en `False`), que faltara el assembly (`Library/ScriptAssemblies/TTTXO.Core.Tests.dll` presente) y que hubiera un archivo de resultados en `Library/` (no hay ninguno).
- **Verificado:** con `runSynchronously = true`, 76 tests corrieron y devolvieron `passed=76 failed=0 skipped=0 duration=3.52`, con `dataPath` confirmado como TTTXO en la misma llamada.

**ERR-KB-006**
- **Detectado por:** la sonda final de `Tools\quickmatch-e2e.ps1` en la sesión 2026-08-09, escrita a propósito para responder empíricamente una pregunta que había quedado en inferencia al leer `OnlineRewardStore`: si `currency` estaba en clase `Default`, todo lo que el resto del script acababa de validar era decorativo.
- **Confirmado en vivo** contra `development`, con jugadores anónimos descartables: `POST .../players/{id}/items` con el token del propio jugador devolvió 2xx para `currency` (balance leído de vuelta en 999999), `onlineCurrencyLedger` y `rankedProfile`.
- **Sin arreglar a propósito.** La sonda quedó como observación que imprime, no como assert que rompe: que `currency` sea escribible por el cliente es intencional para la economía offline, así que si la clave se parte en dos o se mueve entera a `Protected` es una decisión de diseño, no algo que deba forzar el harness.
- **Pendiente de cerrar:** la cadena completa `rankedProfile` forjado → entrada en el leaderboard (ver "Lo que esta verificación NO prueba").
