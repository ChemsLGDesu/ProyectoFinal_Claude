---
tags: [tttxo, beta, testers, build, android, distribucion]
---

# Reparto de builds a testers

Cómo se corta y se reparte una beta de TTTXO, qué lleva adentro y qué **no** está verificado
todavía. La primera build de Android del proyecto es del 2026-08-11; todo lo anterior fueron builds
de Windows para el escritorio del dueño.

## Estado de esta beta

| Dato | Valor |
|---|---|
| Versión | **0.6.0** (`bundleVersion`), versionCode **4** |
| Archivo | `Builds/Android/TTTXO-v0.6.0-b4-beta.apk` |
| Tamaño | 145.8 MB |
| Plataforma | Android, `arm64-v8a` + `armeabi-v7a`, IL2CPP |
| Android mínimo | 7.1 (API 25) |
| Target SDK | 35, fijado (el `b3` llevaba 36 por *Automatic*) |
| Package | `com.hellscythe25.tttxo` |
| Firma | **keystore de debug de Unity** (ver *Firma* abajo) |
| Tipo | Development Build — **obligatorio**, no es un descuido |
| Backend | environment `development` de UGS |
| Analytics | archivo local en el dispositivo, **no** va a UGS |

**La build es Development Build a propósito.** `TTTXO_LOCAL_ANALYTICS` está definido para Android, y
`LocalAnalyticsBuildGuard` aborta cualquier build de release que todavía lo tenga. Esa combinación es
la correcta para una beta —los eventos se escriben a un archivo en vez de gastar eventos facturables
de Unity Analytics— pero significa que el APK arrastra símbolos de depuración y pesa bastante más que
uno de producción. No hay que "arreglar" el flag: sacarlo es parte de preparar producción, no de
preparar una beta.

## Lo que nadie verificó todavía

Esto es lo que hay que decir en voz alta antes de mandarle el archivo a alguien:

- **Este APK nunca corrió en un teléfono.** Ni en este ni en ningún otro: el proyecto tiene cero
  builds en hardware físico. Los seis dispositivos medidos en [[09-Encuadre-Dispositivos]] son todos
  Device Simulator. Que compile no prueba que arranque.
- **La pasada manual con dos clientes sigue abierta** — Wire push, UI de la serie de Ranked y
  pantalla de matchmaking nunca se probaron con dos jugadores reales al mismo tiempo.
- **Los Triggers no están deployados** (`Assets/Triggers/`, necesita `ugs login`). Sin ellos,
  `SweepAbandonedMatches` no corre: una partida online abandonada queda colgada en vez de resolverse
  sola. Si un tester cierra el juego a mitad de una partida online, es esperable que el rival se
  quede esperando.

Conviene que el primer teléfono sea uno propio o prestado, no el de un tester, y que la primera
sesión sea simplemente abrir el juego y llegar a Home.

## El `b2` no instalaba en teléfono, y lo que se descartó para llegar acá

El primer APK (`b2`) no abrió en ningún dispositivo físico. Corría en MuMu y **no** en BlueStacks.
Los cuatro logs que mandaron los testers eran todos del emulador, así que no contenían el fallo; el
diagnóstico salió de inspeccionar el APK, no de leerlos.

**Lo que se descartó midiendo**, para que nadie lo vuelva a probar:

| Hipótesis | Cómo se descartó |
|---|---|
| APK sin firmar | `apksigner verify`: firma v2 válida, `CN=Android Debug` |
| Páginas de 16 KB (Android 15+) | `llvm-readelf --program-headers`: los 7 `.so` en `LOAD align: 0x4000` |
| APK de split, no instalable suelto | Sin `isSplitRequired` ni `requiredSplitTypes` en el manifiesto |
| Archivo corrupto | `aapt2` y `apksigner` lo parsean entero |

**`zipalign -c -P 16` no sirve para decidir lo de 16 KB.** Devolvía `OK - compressed` para las siete
librerías, que parece una aprobación y no lo es: exime a las entradas comprimidas del chequeo. Lo que
decide es la alineación de los segmentos LOAD dentro del ELF, y eso solo lo dice `readelf`.

**La causa que quedó en pie: el APK traía solo `arm64-v8a` contra un `minSdkVersion` de 25.** Las dos
cosas se contradicen — API 25 es Android 7.1 (2016), una época con muchos teléfonos de 32 bits, y en
uno de esos un APK solo de 64 bits **no falla al abrir, falla al instalar**, con
`INSTALL_FAILED_NO_MATCHING_ABIS`, que le llega al usuario como un escueto *"Aplicación no
instalada"*. Es también lo único que explica que MuMu 12 sí y BlueStacks no: MuMu traduce ARM, varias
versiones de BlueStacks no.

Desde el `b3` el APK lleva las dos ABIs. Pasó de 104.8 MB a 145.8 MB, no al doble: IL2CPP compila dos
veces pero las librerías de 32 bits comprimen mejor.

**Esto todavía no está confirmado en hardware.** Es la hipótesis que sobrevivió a las que se pudieron
refutar, no una causa verificada — para eso hace falta que el build actual instale en el mismo
teléfono donde falló el `b2`.

El `b4` cambia **dos** cosas respecto del `b2` (la segunda ABI y el target SDK), lo cual normalmente
arruinaría la atribución. Acá no la arruina, y conviene saber por qué: **el `targetSdkVersion` no
condiciona la instalación**. Android solo bloquea por target cuando es *demasiado bajo* (menor a 23,
desde Android 14), nunca por bajar de 36 a 35. Así que si el `b4` instala donde el `b2` no instalaba,
la ABI sigue siendo la única de las dos que puede explicarlo.

## Cómo se entrega

**Link de descarga (Drive, WeTransfer), no adjunto de mensajería.** El APK pasa los 145 MB, y una
transferencia por WhatsApp o Telegram puede recomprimirlo, renombrarlo o cortarlo. "Archivo corrupto"
ya fue una de las hipótesis que hubo que descartar a mano cuando el `b2` no instalaba; publicar el
tamaño evita repetir ese trabajo.

**Con cada entrega van el tamaño exacto en bytes y el SHA-256**, y el tester verifica el tamaño antes
de instalar. Es un chequeo de tres segundos que descarta una causa entera.

```powershell
(Get-Item .\TTTXO-v0.6.0-b4-beta.apk).Length
Get-FileHash .\TTTXO-v0.6.0-b4-beta.apk -Algorithm SHA256
```

| Build | Bytes | SHA-256 |
|---|---|---|
| `b4` | 152 912 041 | `ED595FCB4D4F698BE0D3A2C11A4E13D58A6F2245956792407BF458C9FD5F012F` |

## Instalación (para el tester)

1. Descargar el `.apk` al teléfono.
2. Abrirlo desde el gestor de archivos. Android va a pedir permiso para **instalar desde esta
   fuente**: hay que concedérselo a la app desde la que se abre el archivo (el navegador o el gestor
   de archivos), no al APK.
3. Es probable que Play Protect muestre un aviso de "app no reconocida". Es esperable: el APK no
   está firmado con una clave publicada ni pasó por Play. Se instala con *Instalar de todos modos*.
4. Requiere Android 7.1 o superior. Desde el `b3` incluye binarios de 32 y de 64 bits, así que también
   entra en teléfonos viejos o de gama baja.

## Qué se puede probar

**Modos**
- **1 jugador vs IA** — la dificultad se adapta a cómo viene jugando el jugador.
- **2 jugadores local** — dos personas en el mismo teléfono.
- **Quickmatch online** — partida casual contra otro jugador real.
- **Ranked** — competitivo con MMR, tiers y tabla de posiciones. En 3×3 es una serie de dos partidas
  (cada jugador empieza una), en 6×6 es una sola.

**Tableros: 3×3 y 6×6.** 9×9 y 11×11 están implementados pero no se ofrecen: en un teléfono de 360dp
una celda de 11×11 mide ~27dp contra los 48dp que Android pide como objetivo táctil mínimo, y eso no
se arregla con layout (ver L-05 en [[09-Encuadre-Dispositivos]]).

**Idiomas: 10** (en, es, fr, de, pt, it, id, vi, tr, pl). Se cambian desde Ajustes. Al primer arranque
el juego toma el idioma del sistema si está entre esos diez, y si no, inglés.

**Pantallas**: Splash, Home, Modo de juego, Selección de tablero, Partida, Resultado, Perfil,
Historial, Tabla de posiciones, Matchmaking, Tienda y Ajustes.

## Qué falta y no es un bug

Si un tester reporta alguna de estas, ya está sabido:

- **La tienda está vacía.** La pantalla existe, el catálogo no. Nada es comprable todavía.
- **No hay audio.** Los sliders de volumen guardan su valor pero no hay ningún sonido que controlar.
- **Los cosméticos no se compran.** Se pueden equipar los que ya vienen; la propiedad la va a decidir
  el servidor cuando exista la tienda.
- **El nombre de la app no está traducido** en el lanzador de Android, y el juego no aparece en el
  selector de idioma por app de Android 13+. Falta configurar *Android App Info* en Localization
  Settings; no afecta el selector de idioma que sí tiene el juego adentro.
- **Sin vinculación de cuenta con UI.** La arquitectura server-side está, la pantalla no. La sesión es
  anónima: **si el tester desinstala, pierde su progreso.**

## Firma: por qué importa y qué hacer

El APK está firmado con el keystore de debug que genera Unity sola. Instala y se juega, pero **dos
APK firmados con claves distintas no se actualizan entre sí**: el día que se pase a una clave real,
cada tester va a tener que desinstalar, y ahí se lleva puesto su monedero y su historial locales.

Para evitarlo desde la segunda tanda, conviene crear una clave de una vez. El comando lo tiene que
correr el dueño, no el agente — la contraseña no debe pasar por el chat ni quedar en el repo:

```bash
keytool -genkeypair -v -keystore Builds/tttxo-release.keystore -alias tttxo -keyalg RSA -keysize 2048 -validity 10000
```

`Builds/` está en `.gitignore`, así que el archivo no se va a commitear. **Conviene guardar una copia
fuera de la máquina**: si se pierde esa clave, no hay forma de publicar una actualización de la misma
app en Play, nunca más.

Después, `BetaBuild` la usa sola si encuentra estas cuatro variables de entorno; si falta alguna,
avisa por consola y cae al keystore de debug:

```bash
setx TTTXO_KEYSTORE_PATH "D:\UnityProjects\TTTXO\Builds\tttxo-release.keystore"
setx TTTXO_KEYSTORE_PASS "..."
setx TTTXO_KEY_ALIAS "tttxo"
setx TTTXO_KEY_PASS "..."
```

## Cómo cortar la próxima build

Con el Editor abierto: **`TTTXO → Build → Android Beta APK`**. Sale en `Builds/Android/` con el
nombre versionado y abre la carpeta al terminar.

El script ([`BetaBuild.cs`](../Assets/Scripts/Game/Editor/BetaBuild.cs)) se encarga de lo que se
olvida a mano: aplica la identidad de la app, **incrementa el `versionCode`** —Android se niega a
instalar un APK con un código menor al instalado, y dos builds con el mismo código se pisan sin
avisar— fuerza APK en vez de AAB, y pone `BuildOptions.Development`, que es lo que hace que la build
sea legal con el define de analytics puesto.

Desde línea de comandos, con el Editor **cerrado** (el proyecto queda tomado si está abierto):

```bash
Unity.exe -quit -batchmode -projectPath D:\UnityProjects\TTTXO -executeMethod TTTXO.Game.Editor.BetaBuild.BuildAndroidBetaFromCommandLine
```

## Recuperar los datos de la beta

Con `TTTXO_LOCAL_ANALYTICS`, cada sesión escribe un `.jsonl` (una línea JSON por evento) en:

```
/storage/emulated/0/Android/data/com.hellscythe25.tttxo/files/analytics/events-<fecha>-<hora>.jsonl
```

**Ojo con Android 11+**: `Android/data/` no es navegable desde el gestor de archivos de la mayoría de
los teléfonos. Para sacarlo hace falta `adb`:

```bash
adb shell "run-as com.hellscythe25.tttxo ls files/analytics"
```

Si el tester borra sus datos desde Ajustes, ese directorio se borra también — es dato del jugador
como cualquier otro, y dejarlo ahí haría falsa la promesa de esa pantalla.

## Mensaje para copiar y pegar

> Te comparto la beta de **TicTacToe XO** (v0.6.0, Android).
>
> **Antes de instalar:** confirma que el archivo pesa exactamente los bytes que van en este mensaje.
> Si no coincide, se cortó en la descarga y no tiene sentido probarlo.
>
> **Instalación:** descarga el `.apk`, ábrelo desde el gestor de archivos y concede el permiso para
> instalar desde esa fuente. Si Play Protect avisa que no reconoce la app, es normal — todavía no
> pasó por Google Play.
>
> **Requisitos:** Android 7.1 o superior.
>
> **Qué probar:** jugar contra la IA, contra alguien en el mismo teléfono, y las partidas online
> (Quickmatch y Ranked). También cambiar el idioma desde Ajustes — hay 10.
>
> **Ya sabemos que falta:** la tienda está vacía, no hay sonido, y como la sesión es anónima, al
> desinstalar se pierde el progreso.
>
> **Lo que más me sirve:** que todo entre bien en la pantalla —que no se corte ni se superponga
> nada— y cualquier cosa que se sienta lenta o se cuelgue. Indica también qué teléfono es y qué
> versión de Android.

## Pendientes

- **Esperando la respuesta del tester sobre el `b4`.** Entregado el 2026-08-12. Hasta que conteste, la
  causa del fallo de instalación es una hipótesis sobreviviente, no un diagnóstico cerrado. El `b3`
  quedó obsoleto sin haberse probado nunca en hardware.

  Las tres cosas que se pidieron, y por qué cada una:

  1. **Si instaló y si abre** — separa "no instala" de "instala y no arranca", que son dos bugs
     distintos y hasta ahora estuvieron confundidos en un solo reporte.
  2. **Modelo y versión de Android** — es la que puede tumbar la hipótesis: si el teléfono resulta ser
     de 64 bits y el `b4` igual funciona, entonces lo arregló otra cosa y la ABI no era la causa,
     aunque el síntoma haya desaparecido.
  3. **Texto exacto del error, y en qué momento aparece** — `INSTALL_FAILED_NO_MATCHING_ABIS` y un
     bloqueo de Play Protect se ven casi iguales en pantalla y no se arreglan igual.
- ~~Fijar el Target SDK~~ — **hecho**: `AndroidTargetSdkVersion` pasó de `0` (*Automatic*) a `35`, y
  `BetaBuild.ApplyAndroidIdentity` lo impone en cada build. *Automatic* resolvía al SDK más alto
  instalado en la máquina que construye, o sea que la misma build salía con target distinto en otra
  máquina, cambiando el comportamiento sin que cambiara una línea de código ni apareciera nada en el
  diff. **El `b3` que está repartiéndose todavía lleva 36**: se construyó antes del cambio, y no se
  rebuildeó a propósito para no mover dos variables sobre el experimento de ABI que sigue abierto.
- **El bloqueo a portrait sigue sin confirmar, y el manifiesto no puede confirmarlo.** Esta nota decía
  antes que bastaba con leerlo en el build nuevo. Se leyó, y **los dos manifiestos son idénticos en lo
  que importa**: `b3` (target 36) y `b4` (target 35) declaran los dos `screenOrientation=1` (portrait)
  y `resizeableActivity=true`. La diferencia no vive en el manifiesto sino en **cómo interpreta
  Android el `targetSdkVersion` en runtime**: con 36, Android 16 ignora la restricción de orientación
  en pantallas de 600dp o más; con 35 debería respetarla. Un chequeo estático no discrimina entre esas
  dos hipótesis. Lo único que decide es **girar el dispositivo** en una pantalla de 600dp o más
  corriendo Android 16 — tablet, plegable o Device Simulator.
- **Crear el keystore de release** antes de la segunda tanda (ver *Firma*).

## Conexiones

- [[09-Encuadre-Dispositivos]] — la geometría por dispositivo y las trampas del Device Simulator; el
  hueco que esta beta viene a cerrar es justamente que todo eso está simulado
- [[02-GDD-TicTacToe]] — modos, economía y localización que el tester va a ver
- [[05-UI-Pantallas-TicTacToe]] — las 12 pantallas y su navegación
- [[MEMORIA_Proyecto]] — estado vivo del proyecto y pendientes que bloquean producción
- [[ERRORES_Conocidos]] — ERR-KB-006 (el saldo escribible por el cliente) no bloquea la beta pero sí
  la tienda
- `Assets/Scripts/Game/Editor/BetaBuild.cs` — el script que corta la build
- `Assets/Scripts/Game/Editor/LocalAnalyticsBuildGuard.cs` — el que impide mandar a producción con el
  define de beta puesto

## Fuente

**Toda la configuración de Android · la primera build**
- **Sesión 2026-08-11.** La identidad de la app se aplicó por script con el Editor abierto, no
  editando `ProjectSettings.asset`: Unity tiene los Project Settings en memoria y los baja a disco
  recién al guardar el proyecto, así que una edición a mano se pierde.
- **Lo que estaba mal antes de tocarlo**, verificado en el `.asset`: `companyName: DefaultCompany`,
  **ningún** `applicationIdentifier` para Android (solo el `com.DefaultCompany.2D-URP` de la
  plantilla), y `bundleVersion: 1.0` con el proyecto en su quinto milestone. Ninguna de las tres
  rompe la build — producen un APK que se instala con la identidad equivocada.
- **Los 104.8 MB están medidos**, no estimados. Parte es el Development Build; parte es que
  `com.unity.ai.inference` (Sentis) entra al APK con sus shaders de runtime aunque **ningún** script
  del juego lo use — llega como dependencia de `com.unity.ai.assistant`, que es la herramienta de
  Editor que hace andar el MCP. Cuánto pesa cada mitad no se midió.
- **El warning de *Android App Info* apareció en esta build.** Se verificó que **no** afecta el
  selector de idioma del juego leyendo la cadena de arranque de locales en `LocalizationSetup`:
  `PlayerPrefLocaleSelector` → `SystemLocaleSelector` → default. Lo que sí queda sin resolver es el
  nombre en el lanzador y el selector por app de Android 13+.
- **El `versionCode` de esta build es 2, no 1**, porque el contador arrancaba en 1 y el script
  incrementa antes de construir. No es un error: el 1 nunca se repartió.
