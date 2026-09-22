---
tags: [herramientas, arte, placeholder, editor, gemini, tictactoe]
---

# Herramienta de arte placeholder — diseño y decisiones

Ventana de Editor (`Tools → TTTXO → Placeholder Art Generator`) que genera todo el inventario de arte del juego llamando a la API de imágenes de Gemini. Vive en `Assets/Scripts/Game/Editor/` dentro del asmdef `TTTXO.Game.Editor`, así que **nunca entra en un build**.

Todo el arte que produce es **placeholder**: sirve para ver el juego vestido y validar layout, no es arte final.

## Archivos

| Archivo | Rol |
|---|---|
| `ArtPromptCatalog.cs` | Fuente de verdad: las 60 entradas, las reglas de dirección de arte compartidas y la composición del prompt final |
| `GeminiImageClient.cs` | La llamada HTTP y el decodificado de la respuesta |
| `ChromaKeyProcessor.cs` | Convierte el fondo croma en alfa real |
| `SubjectNormalizer.cs` | Impone el encuadre exacto: reescala el sujeto (o el agujero de un anillo) y lo centra |
| `BackdropFlattener.cs` | Divide la caída de luz radial de un board skin: las esquinas quedan tan brillantes como el centro |
| `TextureMinifier.cs` | Cuantiza la paleta por median cut |
| `IndexedPngEncoder.cs` | Escribe PNG indexado (1 byte por píxel) |
| `PlaceholderArtGeneratorWindow.cs` | La UI: estado por pieza, filtros, generación por lotes |

El catálogo está en C# y no en un ScriptableObject a propósito: se revisa y se diffea como el resto del código, y agregar una entrada no requiere tocar ningún asset.

## Restricción que define toda la arquitectura: el endpoint es JPEG-only

`response_format.mime_type` solo acepta `image/jpeg`. Pedir `image/png` devuelve **HTTP 400**:

```text
The value 'image/png' is not supported for 'response_format.mime_type'.
Supported values: 'image/jpeg'.
```

**No todo HTTP 400 es este.** El endpoint devuelve 400 también cuando el filtro de contenido bloquea la generación, con un cuerpo completamente distinto:

```text
Image generation blocked due to copyright/recitation. Please modify your input and retry.
```

Pasó regenerando `board_wood` con **Pro**: "dark stained walnut tabletop" es un tema de foto de stock clásico y el modelo lo bloqueó por recitación. El mismo prompt con **Flash** pasó sin problema. Después volvió a pasar con `board_paper` ("dark sugar paper"), esta vez **con Flash** — y el reintento del mismo prompt, sin cambiarle una coma, pasó. O sea que el filtro no es determinista: ante un bloqueo conviene reintentar una vez antes de tocar el prompt. Dos cosas útiles de ese caso: la respuesta aclara que **la request bloqueada no se cobra**, y el cuerpo completo solo está en la consola de Unity —el mensaje que devuelve el cliente dice apenas "HTTP 400: See Console"—, así que ante un 400 hay que ir a la consola antes de suponer que es el `mime_type`.

JPEG no tiene canal alfa, y buena parte del arte del juego **necesita** alfa. De esa distancia sale todo el pipeline:

- **Nunca se pide transparencia.** Pedirle a un modelo un fondo transparente en JPEG hace que **dibuje** un tablero de ajedrez, porque es la única forma que tiene de representarla.
- Las piezas que necesitan alfa se generan sobre un **fondo croma verde plano** y se recortan después.
- Las opacas conservan los bytes JPEG intactos.

## El recorrido del formato: entra JPEG, sale PNG

```
                    API (JPEG obligatorio)
                            |
            +---------------+---------------+
            |                               |
      entrada OPACA                  entrada con ALFA
            |                               |
   se guarda .jpg tal cual        decodificar -> RGBA32
   (bytes intactos, sin                     |
    reprocesar)                  chroma key: verde -> alfa
                                            |
                                    normalizar encuadre
                                            |
                                     cuantizar paleta
                                            |
                                    PNG indexado -> .png
```

Dos reglas que se desprenden y conviene no olvidar:

- **La extensión sigue a lo que realmente hay en disco.** Las opacas son `.jpg` porque son los bytes JPEG de la API sin tocar; escribirlos en un archivo `.png` sería mentir sobre el formato sin ganar nada. Las que llevan alfa son `.png` de verdad, reencodeadas.
- **La minificación va al final**, sobre los píxeles definitivos. Si corriera antes del recorte o del encuadre, cuantizaría colores que después se van a modificar o descartar.

## Chroma key

Color clave: **verde puro `#00FF00`**. Se eligió por distancia: el color de paleta más cercano es el cian `#35E6C6`, a ~206 de distancia RGB — muy por encima de cualquier umbral de recorte, así que el recorte no puede comerse el arte.

Tres decisiones no obvias, cada una nacida de un fallo real:

1. **La siembra es global, no desde los bordes.** Un relleno sembrado en los bordes no alcanza los **bolsones de fondo encerrados por el dibujo**: las celdas de un globo de alambre, el interior de un trazo de neón. Salían como agujeros verdes brillantes. Sembrar en cada coincidencia dura los alcanza. Es seguro *solo* porque el verde puro está lejísimos de la paleta — si el color clave cambia, hay que rehacer ese cálculo.
2. **Los píxeles recortados se re-alojan en una textura RGBA32 antes de codificar.** `ImageConversion.LoadImage` **adopta el formato del archivo de origen**, y un JPEG decodifica a `RGB24`, sin canal alfa. Escribir el alfa calculado de vuelta en esa textura lo descarta en silencio y produce un PNG opaco que parece correcto.
3. **Los píxeles de borde reciben alfa fraccional y se les resta el color clave** (de-spill), para que una silueta antialiaseada no quede con un aro verde.

**Los agujeros son fondo.** Un anillo o marco nunca debe pedir "centro transparente": hay que describir el hueco como relleno del mismo verde plano. El recorte lo levanta gracias a la siembra global.

## Las reglas se seleccionan según el tipo de entrada, no se acumulan

Mandarle todas las reglas a todas las entradas produjo arte inservible, porque **se contradicen por diseño**:

| Tipo de entrada | Reglas que recibe |
|---|---|
| Transparente, no `Effects` | Contraste de primer plano + encuadre + **borde duro** |
| Transparente, categoría `Effects` | Contraste de primer plano + encuadre + **caída suave** |
| Opaca | **Backdrop**: mantenerse oscura, bajo contraste interno, centro tranquilo |

- Un sujeto recortado tiene que ser **claro** para leerse sobre la UI oscura; una entrada opaca **es** esa superficie oscura y tiene que quedarse oscura. Aplicarle la regla de primer plano a los fondos produjo un tablero de madera naranja brillante y uno de papel casi blanco, sobre los cuales ninguna pieza se leía.
- Una silueta necesita **borde duro** para que el croma se separe limpio; un sprite de partícula **es** su caída suave. La regla anti-glow, aplicada a `Effects`, anulaba la categoría entera.

## Reglas de contenido aprendidas a los golpes

- **La regla de "sin texto" encabeza todos los prompts**, antes de la paleta. Con la regla al final y la paleta como lista pelada de hex, el banner de perfil volvió con `#1C2733` y `#8A97A6` **escritos como tipografía decorativa** sobre el arte — dos códigos que no estaban en el prompt de esa entrada, leídos directamente de la lista compartida. La paleta ahora aclara explícitamente que los hex son especificación de color y nunca contenido.
- **Piso de contraste**: todo sujeto se mide contra `--color-bg #0B0F14` y debe superar **3:1** (WCAG 1.4.11 para gráficos no textuales). Cuando un par de piezas debe diferenciarse por tono, se separan **por matiz**, nunca oscureciendo una mitad: pedir "nogal notablemente más oscuro" dio una X a 1.86:1, cinco veces más apagada que su propia O.
- **Encuadre**: sujeto centrado, ~70% del lado corto, márgenes parejos y silueta cuadrada en lienzo cuadrado. Sin esta regla los bounding boxes iban de 36% a 73% del cuadro, y un planeta con anillo salió 73×46, sin entrar en una celda cuadrada.
- **Los board skins son superficie, nunca grilla.** El juego está especificado en cuatro tamaños (3×3, 6×6, 9×9, 11×11; al lanzamiento `BOARD_CONFIGS` ofrece solo los dos primeros — ver [[02-GDD-TicTacToe#3.1 Tableros]]) y la grilla la dibuja la UI, donde cada celda es un `.board-cell` con su propio borde. **Los skins se siguen haciendo para los cuatro**: el recorte es de config y reversible, y un skin que asuma dos tamaños habría que rehacerlo cuando vuelvan. Una grilla horneada en la textura coincide como mucho con un tamaño y se duplica con la de la UI en todos.
- **Los materiales naturales tienen que renunciar explícitamente al permiso de acento.** `BackdropRules` autoriza "bright accents ... only as thin, sparse highlights", y el grano de madera se lee como una invitación a correr esos acentos a lo largo: `board_wood` volvió con dos vetas cian y dos violetas **cruzando el lienzo de lado a lado** —lo más brillante de la imagen, y encima líneas, que `BoardSurfaceRule` ya prohibía—. Decir "deep and muted rather than bright" no alcanzó: contra un permiso explícito hace falta una prohibición explícita. La entrada ahora declara que los únicos colores son los del material y que ninguna línea puede cruzar el lienzo. Efecto medido: píxeles de acento **0,1964% → 0,0000%**, y el píxel más brillante bajó de 8,9× la media a 1,5×.
  `board_paper` comparte la estructura y hace lo mismo en chico —motas cian y violeta de unos pocos píxeles— pero ahí se leen como fibras de color en el papel y quedan bien. La regla se aplica por entrada, no a la categoría.

## Lo que se pide y lo que se impone

Hay dos formas de conseguir una propiedad en el arte generado, y elegir mal cuesta cuota:

- **Pedirla en el prompt** sirve para todo lo que es criterio artístico: color, estilo, tema, composición.
- **Imponerla en post-proceso** sirve para lo que es medible y exacto. Un prompt de encuadre acerca pero nunca cierra: pedir "~70% del lienzo" dejó los seis avatares entre **60.1% y 78.1%** —30% de diferencia de diámetro, visible en la grilla del selector— y una pieza regenerada se pasó al otro lado del objetivo.

`ArtPromptEntry.SubjectCoverage` activa `SubjectNormalizer`, que mide el bounding box del sujeto, lo reescala al porcentaje exacto y lo centra. Es determinista, no gasta cuota y **se puede aplicar a un lote ya generado sin regenerarlo**.

Se activa en assets que se muestran **como conjunto**, donde un tamaño disparejo se lee como error: los avatares del selector, las dos mitades de un par de piezas, la fila de badges de tier. Se deja en `null` cuando la pieza va sola.

**Hay que normalizar por la dimensión que importa, no por la más obvia.** Un anillo o marco se normaliza por su **agujero** (`HoleCoverage`), no por su contorno: el agujero es la abertura donde entra el avatar. Los dos marcos de perfil salieron con diámetros exteriores casi idénticos pero agujeros de **58.6% y 54.3%** — normalizar el exterior los habría dejado desalineados justo donde la UI tiene que medir, obligando a una escala de avatar distinta por marco.

Detalle de implementación: el remuestreo se hace **premultiplicando el alfa**. Interpolar alfa recto arrastra el RGB de los píxeles transparentes hacia adentro de la silueta y deja un borde oscuro. La medición del agujero se hace por relleno por difusión desde el centro, no muestreando unos ejes, para que un marco ornamentado de borde interior irregular se mida bien.

**Valores vigentes**: avatares al 75% del lienzo; agujero de marco al 58%.

### El viñeteado: el caso más claro de "no se pide, se impone"

`BoardSurfaceRule` pedía "lit uniformly with no vignette, no central focal point" desde que existe la categoría. Los cuatro tableros volvieron igual con el centro entre **1,47× y 1,64×** más brillante que las esquinas. Un modelo al que le pedís una mesa te la dibuja iluminada, y la luz cae.

Se probó pedirlo más fuerte, reescribiendo la regla en positivo y largo ("las cuatro esquinas exactamente tan brillantes como el medio... un escaneo plano bajo luz pareja, no hay fuente de luz..."). **Empeoró**: el viñeteado *subió* en tres de los cuatro (neon 1,64× → 3,16×, wood 1,59× → 2,29×) y neon perdió casi toda su textura de circuito. Cuanto más discute el prompt sobre iluminación, más presupuesto se va ahí en vez de al material. La redacción volvió a la corta.

`BackdropFlattener` lo resuelve midiendo: arma el perfil de luminancia en 64 anillos radiales, lo suaviza y lo divide de vuelta, apuntando a la **media global** y no a la del centro —apuntar al centro aclararía la imagen entera, que es justo lo que `BackdropRules` no tolera—. Resultado sobre los cuatro: **1,47×–1,64× → 0,98×–1,02×**, sin anillos (desviación máxima por anillo ≤1,9%, del orden de la textura).

Tres decisiones de implementación:

- **Solo `BoardSkins`.** Un tablero se escala y recorta a cuatro tamaños, así que un medio brillante es un defecto. Un banner de perfil es una imagen compuesta donde la zona brillante *es* el sujeto: aplanarlo radialmente lo arruinaría.
- **Interpola entre anillos** en vez de saltar de uno a otro: un escalón en la curva de ganancia se ve como un anillo sobre una superficie tan plana. Por lo mismo el perfil se suaviza antes de usarse — un anillo que agarra un nudo oscuro se convertiría en una banda visible.
- **Corre antes del reencodeo de tamaño**, porque aplanar decodifica y vuelve a encodear; al revés tiraría a la basura la calidad elegida.

Y una ventaja que es el argumento central a favor del post-proceso: **se aplica a arte ya generado**. Cuando la regeneración de `board_neon` y `board_space` volvió peor que lo que había, se les aplicó el aplanado al arte existente y quedaron planos igual, sin gastar una sola llamada más. El costo es una generación extra de pérdida JPEG en esos dos (181 KB → 189 KB en space, que creció).

## Minificación

El arte llega por JPEG, así que incluso una ilustración plana viene cargada de miles de colores casi idénticos que son **ruido de compresión, no diseño**: un avatar traía 10.764 colores únicos para dibujar una docena reales. Ese ruido es lo que engorda el PNG — DEFLATE no encuentra repetición en un campo donde cada píxel difiere apenas de su vecino.

### Paso 1 — cuantización de paleta (`TextureMinifier`)

*Median cut* trabajando sobre el **histograma de colores únicos** (~10k) en lugar de sobre los 4.2M de píxeles; sin eso el algoritmo no sería viable a 2048×2048. Corta por el eje de mayor rango y parte en la **mediana de población**, no del listado: así un color que cubre media imagen no se lleva una caja que no necesita, y un destello de ojo o un acento raro sobrevive al recorte.

**Solo el RGB se cuantiza.** El alfa se copia tal cual: lleva el recorte del croma, y bandearlo mordería la silueta.

### Paso 2 — codificación indexada (`IndexedPngEncoder`)

Acá está el salto real. `ImageConversion.EncodeToPNG` **siempre escribe truecolor RGBA — cuatro bytes por píxel — y no acepta configuración**. Sobre arte plano eso es cuatro veces más datos de los que la imagen realmente contiene, y cuantizar solo ayuda en la medida en que DEFLATE logre exprimir esa redundancia después. Escribir directamente la forma con paleta la elimina de entrada.

La diferencia es medible y grande:

| | Bytes | vs original |
|---|---|---|
| Original de la API | 2.382.946 | — |
| Solo cuantizado (truecolor) | 898.377 | −62.3% |
| **Cuantizado + indexado** | **339.028** | **−85.8%** |

Detalles de la implementación, todos necesarios porque hubo que escribir el encoder a mano:

- PNG **tipo 3** (indexado): `IHDR` + `PLTE` + `tRNS` + `IDAT` + `IEND`, con CRC32 por chunk y un stream zlib (cabecera `78 9C`, deflate crudo de `DeflateStream`, y Adler-32 al cierre).
- **Filtro tipo 0 (None) en todas las filas.** Los filtros PNG hacen aritmética de bytes, que sobre índices de paleta no significa nada: restarle un índice a otro produce ruido, no un número más chico.
- El alfa viaja en `tRNS`, un byte por entrada de paleta, así que la paleta se arma sobre pares **(color, alfa)**. El tramo parcial se cuantiza a **16 niveles**, con 0 y 255 exactos — entre medio hay menos de una décima de por ciento de los píxeles, todos borde antialiaseado.
- Si los pares no entran en 256 entradas, se reduce el conteo de colores y se reintenta; **los colores se sacrifican antes que los niveles de alfa**, porque perder un color corre un tono plano mientras que perder un nivel de alfa astilla la silueta. Si aun así no entra, se cae a truecolor: un archivo más grande pero válido.

### Resultado y alcance

### Paso 3 — las opacas van por otro camino: reencodeo JPEG

La cuantización de paleta es la herramienta **equivocada** para un fondo. Un tablero o una nebulosa son degradados reales que una paleta bandea, y PNG sobre ese tipo de contenido pesa más que JPEG. Se reencodean como JPEG con `MinifyJpeg`.

**Calidad 90**, medida sobre el peor caso del catálogo — el banner de nebulosa, la pieza con el degradado más genuino: baja de 1,8 MB a 214 KB cambiando el píxel medio en 2,6 sobre 765 posibles, con solo el 0,3% de píxeles visiblemente distintos. A 1:1 sobre la zona más densa no se distingue del original. El arte llega de la API a una calidad mucho más alta de la que un fondo detrás de texto de UI necesita jamás.

Reencodear un JPEG es **pérdida generacional**, así que el paso se niega a actuar salvo que el ahorro sea real (umbral del 10%): correr el lote dos veces no puede degradarlo una segunda vez en silencio. Es el mismo principio que la regla de no re-minificar.

### Resultado

Medido sobre el catálogo completo: **44 piezas, 68,2 MB → 12,1 MB, ~82% de ahorro**, sin diferencia visible en ninguna.

| Ruta | Piezas | Ahorro |
|---|---|---|
| Cuantización + PNG indexado | 36 | 81,8% |
| Reencodeo JPEG | 8 | 83,3% |

### El tamaño de paleta se mide, no se supone

`ArtPromptCatalog.GetMinifyColours` y `GetJpegQuality` centralizan la decisión:

| Tipo de arte | Ajuste |
|---|---|
| Plano con alfa (piezas, iconos, avatares, efectos) | paleta **64** |
| Sombreado con alfa (badges de Ranked) | paleta **192** |
| Opaco (tableros, banners, fondos) | JPEG calidad **90** |

**Correcciones de reglas anteriores** (2026-08-08), las dos por el mismo motivo: se había supuesto en vez de medir.

- Los badges de Ranked estaban **excluidos por completo**, suponiendo que su sombreado metálico era un degradado real que una paleta chica bandearía. El sombreado resultó ser unas pocas bandas planas, y a 192 colores un badge es indistinguible de su fuente incluso ampliando la rampa del borde. Bajaron 82,2% (8,1 MB).
- Las entradas **opacas** figuraban como "no hay PNG que achicar", lo cual es cierto pero llevaba a la conclusión equivocada de dejarlas intactas. Sí se pueden achicar — reencodeando el JPEG. Bajaron 83,3% (12,0 MB).

Y un resultado contraintuitivo que conviene recordar: sobre esos badges, **192 colores dio a la vez el archivo más chico y la mejor fidelidad**, por debajo de 64, 128 y 256. Con más entradas la paleta cae más cerca de los colores reales, las tiradas de índices iguales se alargan y el DEFLATE comprime mejor — la paleta más ancha se paga sola. **Ante una categoría nueva, medir tres o cuatro tamaños antes de elegir**; la intuición de "menos colores, archivo más chico" es falsa acá.

## Marcado de arte defectuoso

`ArtPromptEntry.RegenerateReason` marca arte que **existe pero se sabe malo**, con la medición concreta que lo condena. La ventana muestra **NEEDS REGEN** en lugar de "already generated" —el riesgo real es que una sesión futura vea el archivo, lo dé por bueno y siga— más un botón que selecciona todo el conjunto marcado de una vez, para retomarlo cuando haya cuota disponible.

**Un flag se limpia en el mismo commit que reemplaza el arte.** Un flag huérfano hace que la ventana avise sobre piezas que ya están bien, que es exactamente el ruido que el mecanismo existe para evitar.

Y al marcar una pieza hay que **corregir también su prompt individual**: si el prompt sigue pidiendo lo que causó el defecto, regenerar reproduce el mismo resultado.

## Trampas de medición

Métricas que dieron falsos positivos y casi provocaron regeneraciones innecesarias:

- **Promediar matices es inválido en sujetos cálidos.** La X de fuego promedia 147° —verde— porque sus matices cruzan el 0°: 36.5% en 330–359° y 63% en 0–59°. Usar histograma por buckets.
- **El contraste contra `--color-bg` no aplica a los avatares.** Todos están construidos sobre un disco `#1C2733` intencional que arrastra el promedio; el astronauta mide 1.64:1 y está perfecto. Lo que importa es la mascota contra su propio disco, y además van dentro de un marco.
- **La heurística de "verde residual" marca falsos positivos en cian claro.** Para confirmar contaminación real conviene comparar el matiz de la caída suave contra el del núcleo: si se desplaza hacia el verde (120°) hay contaminación; si se desplaza hacia el azul, el de-spill funcionó.
- **La razón centro/esquina solo ve caídas radiales.** Una regeneración de `board_space` volvió con el centro oscuro y nubes violetas brillantes contra los bordes: midió **1,12×**, el "mejor" número de los cuatro, siendo la imagen *más* despareja del lote — simplemente su desnivel no era radial. Un número de viñeteado cerca de 1 no significa superficie pareja; hay que mirar también la media global y el máximo, y ante un cambio de composición, la imagen.
- **Un lote regenerado no es automáticamente mejor que el que reemplaza.** De cuatro tableros regenerados a la vez, dos volvieron peor: neon perdió sus trazos de circuito (media 0,075 → 0,049) y space se volvió inservible como fondo. Conviene auditar contra el arte anterior, no solo contra los umbrales, y quedarse con el mejor de los dos — que es posible justamente porque el post-proceso no necesita regenerar.

## Operativa

Cosas que cuestan tiempo si no se saben de antemano.

### El arte vive en Git LFS

`.gitattributes` trackea `*.png`, `*.jpg` y demás binarios en **Git LFS**. Consecuencias prácticas:

- `git show HEAD:ruta.png` devuelve un **puntero de ~132 bytes**, no la imagen. El tamaño real está en el campo `size` del puntero.
- El historial de git se mantiene liviano solo, así que el peso del arte **no es un problema de historial** — es cuota de almacenamiento y ancho de banda de LFS. Ese es el motivo real por el que la minificación importa.
- La caché local (`.git/lfs/objects/<aa>/<bb>/<oid>`) permite **recuperar el original íntegro de una pieza ya reemplazada**, que es como se rescató un avatar para reprocesarlo desde la fuente.

### No re-minificar arte ya minificado

Partir de una pieza ya cuantizada da peor resultado que partir del original: un avatar re-minificado quedó en 678 KB, contra 339 KB haciéndolo desde el original de LFS. La cuantización es con pérdida y **encadenarla degrada sin comprimir mejor**. Si hay que rehacer, se vuelve a la fuente.

Por eso el paso de minificado **se niega a escribir un resultado más grande que la entrada**: así una segunda pasada sobre el lote es inofensiva.

### El post-proceso es main-thread only

Todas las piezas del pipeline son públicas (`BuildFullPrompt`, `GenerateImageAsync`, `MinifyJpeg`, `GetOutputAssetPath`), así que se puede regenerar una entrada suelta por script sin abrir la ventana. Con una trampa: encadenar el post-proceso a la tarea con `ContinueWith` lo corre en un hilo del pool, y ahí `MinifyJpeg` —como todo lo que toca `Texture2D`— tira:

```text
SupportsTextureFormatNative can only be called from the main thread.
```

La excepción salta **después** de que la imagen ya se generó y se pagó, así que se pierden los bytes. El patrón seguro es que la continuación haga **solo IO de archivos** —guardar el JPEG crudo a disco— y que el minificado, la escritura del asset y el import ocurran en un comando posterior, sobre el hilo principal. Con el crudo persistido, un fallo del post-proceso no cuesta otra llamada.

### Cuando un cambio no mueve los números, mirar la consola de Unity antes de teorizar

Cuatro benchmarks seguidos dieron resultados idénticos y se estuvo a punto de concluir que el encoder devolvía `null`. La causa estaba en la consola: un **error de compilación** (`CompressionLevel` es ambiguo entre `System.IO.Compression` y `UnityEngine`). El assembly nunca se reconstruyó, así que las cuatro corridas midieron el código viejo.

Vale en general para esta herramienta: `Unity_RunCommand` compila su propio script contra el **último assembly válido**, así que un archivo del proyecto que no compila se traduce en resultados silenciosamente obsoletos, no en un error visible. Ante un resultado sospechosamente estable: `GetConsoleLogs` primero.

## Clave de API

`GEMINI_API_KEY` como variable de entorno, con `EditorPrefs` como alternativa por máquina. **Nunca se escribe en un archivo del repositorio.**

## Conexiones

- [[07-Estetica-UI]] — la paleta que los prompts referencian; `main.uss` sigue siendo la fuente de verdad del color
- [[06-Wireframes-UI]] — dónde va cada pieza de arte en pantalla
- [[04-Store-Catalog-TicTacToe]] — los cosméticos que este arte representa (skins de pieza, tableros, marcos, avatares)
- [[ERRORES_Conocidos]] — fichas de errores ya diagnosticados
- `Assets/Scripts/Game/Editor/ArtPromptCatalog.cs` — el catálogo y las reglas (archivo de código, no nota)

## Fuente

- **Restricción JPEG-only**: verificada contra la respuesta real del endpoint (HTTP 400).
- **Umbrales de contraste**: medidos con luminancia relativa WCAG contra `--color-bg #0B0F14`, en el Editor sobre los archivos generados.
- **Distancia del verde clave a la paleta**: calculada en espacio RGB contra las 12 variables de `:root`.
- **Cifras de minificación**: medidas sobre el lote real (31 archivos), con verificación de que cada PNG resultante vuelve a decodificar a 2048×2048 con su alfa intacto.
- **Cada regla de esta nota corresponde a un defecto observado en arte generado**, no a una precaución teórica. Si una regla no tiene un fallo real detrás, no pertenece a este documento — ver el rol de `docs-changelog` en `.claude/agents/`.
