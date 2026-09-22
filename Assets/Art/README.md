# Arte placeholder — TTTXO

Esta carpeta contiene arte **placeholder generado por IA** (Gemini), pensado para desbloquear trabajo de tienda y de pulido visual mientras no hay arte final. **No es arte de producción**: sirve para tener algo visible y con la dirección de arte correcta (paleta oscura/neón del proyecto) mientras se define o se encarga el arte definitivo.

La herramienta que genera este arte es un tool de Editor (`TTTXO/Art/Generate Placeholder Art`), implementada en:

- `Assets/Scripts/Game/Editor/ArtPromptCatalog.cs` — catálogo de prompts (fuente de verdad).
- `Assets/Scripts/Game/Editor/GeminiImageClient.cs` — cliente HTTP hacia la API de Gemini.
- `Assets/Scripts/Game/Editor/PlaceholderArtGeneratorWindow.cs` — ventana de Editor.

Es una herramienta **solo de Editor**: no compila ni se incluye en ningún build (asmdef `TTTXO.Game.Editor`, `includePlatforms: Editor`).

## 1. Obtener y configurar la API key

1. Conseguí una API key de Gemini con acceso a generación de imágenes (Google AI Studio / Google Cloud).
2. Abrí la ventana: menú `TTTXO → Art → Generate Placeholder Art`.
3. Pegá la key en el campo "Gemini API Key" (queda enmascarada por defecto; hay un toggle "Show" para verla mientras la escribís) y tocá **Save Key**.
4. La key se guarda en `EditorPrefs` bajo la clave `TTTXO.ArtGen.GeminiApiKey` — **nunca se escribe en un archivo del proyecto ni se commitea**. Es local a tu máquina/usuario de Unity.
5. Para sacarla (por ejemplo antes de compartir la máquina), usá el botón **Clear Key**.

Si no hay key configurada, la ventana lo avisa con un warning y el botón de generar queda deshabilitado — no se hace ningún llamado a la API sin key.

## 2. Cómo correr la herramienta

1. `TTTXO → Art → Generate Placeholder Art`.
2. Elegí el **modelo**: `gemini-3.1-flash-image` (default, generalista), `gemini-3-pro-image` (calidad premium, más lento/caro) o `gemini-3.1-flash-lite-image` (rápido/barato).
3. Marcá las entradas que querés generar (checkbox por entrada, o "Select All" / "Select None"). Cada entrada muestra su categoría, aspect ratio, si lleva fondo transparente u opaco, y un preview si ya existe un archivo generado.
4. El campo de texto debajo de cada entrada es el **prompt editable**: podés ajustarlo para esa corrida sin tocar el código ni recompilar. El catálogo en C# (`ArtPromptCatalog.cs`) es la fuente de verdad versionada; este campo es solo para iterar rápido.
5. Tocá **Generate Selected (N)**. Si alguna de las entradas seleccionadas ya tiene un archivo generado, la herramienta te pregunta una sola vez por todo el lote: **Overwrite All**, **Cancel** o **Skip Existing** — nunca sobrescribe en silencio.
6. La generación corre en lote, de forma asíncrona: el Editor no se congela, hay una barra de progreso y un botón **Cancel**. Si una entrada falla (cuota agotada, error de red, respuesta inesperada), se marca en rojo con el mensaje de error y **el resto del lote sigue** — un fallo no aborta el lote completo. El detalle completo (incluyendo el cuerpo crudo de la respuesta HTTP cuando aplica) queda siempre en la consola de Unity.

## 3. Qué genera cada categoría

| Categoría | Carpeta | Contenido |
|---|---|---|
| Brand / Base UI | `Generated/brand-ui/` | Marca gráfica (X/O, sin texto — ver nota abajo), íconos de moneda soft/hard, íconos de los 3 modos, fondo de menú |
| Piece Skins | `Generated/piece-skins/` | 6 pares de fichas X/O: Neon (default), Wood, Space, Animals, Retro arcade, Minimalist |
| Board Skins | `Generated/board-skins/` | 4 fondos/texturas de tablero: neon, paper, wood, space |
| Visual Effects | `Generated/effects/` | Partícula al colocar ficha, confetti de victoria, glow de línea ganadora |
| Profile | `Generated/profile/` | 6 avatares, 2 marcos de perfil, 2 banners |
| Reactions | `Generated/reactions/` | 6 stickers de reacción rápida (gg, buena jugada, revancha, aplauso, pensando, saludo) — representados con iconografía, nunca con letras |
| Ranked | `Generated/ranked/` | 5 insignias de tier (bronze/silver/gold/platinum/diamond) + banner de Top 100 |
| Store | `Generated/store/` | 7 íconos de categoría de tienda + 3 imágenes de packs de moneda |

**Nota sobre el "wordmark"**: el brief original pedía un wordmark del juego, pero un logotipo con letras viola la regla de "sin texto en las imágenes" (todo texto del juego viene de Unity Localization en 10 idiomas y no se puede hornear en un PNG). La entrada `brand_mark_icon` cubre en su lugar la marca gráfica del juego (el emblema X/O), sin ninguna letra.

## 4. Reglas que sigue el catálogo

- **Ningún prompt pide texto en la imagen** — se recuerda explícitamente en cada llamado (ver `ArtPromptCatalog.BuildFullPrompt`).
- **Paleta exacta del proyecto** (`Docs/07-Estetica-UI.md`) inyectada en todos los prompts.
- **Identidad X/O**: en el set Neon (default), X es cian `#35E6C6` y O es violeta `#7C5CFF`. En las demás skins el material puede variar, pero X y O siempre quedan inequívocamente distinguibles entre sí.
- Fondo transparente en íconos/fichas/stickers/avatares/marcos/insignias; fondo propio en fondos de menú, tableros, banners y arte de packs de moneda.

## 5. Importación automática

Cada imagen generada se guarda como `Assets/Art/Generated/<categoria>/<id>.png` y se importa automáticamente como `Sprite` (single), sin mip maps, sin compresión agresiva (`Uncompressed`), con el alpha respetado según corresponda. No hace falta tocar el Texture Importer a mano.

## 6. Costo

**Cada generación consume cuota de pago de la API de Gemini.** Un lote grande (por ejemplo, "Select All" con el catálogo completo, ~58 entradas) puede representar un costo no trivial, más aún con el modelo `gemini-3-pro-image` (premium). Generá de a categorías o de a pocas entradas mientras iterás un prompt, y guardá el "Select All" para una corrida final ya validada.

## 7. `Assets/Art/Generated/` — ¿se versiona en git?

**Se versiona.** Son placeholders livianos (PNG comprimidos, generados una sola vez por entrada salvo iteración deliberada) que el resto del equipo necesita ver en su Editor sin tener que correr la herramienta ni tener su propia API key — lo mismo que ya pasa con cualquier otro sprite del proyecto. La alternativa de ignorarlos en git rompería el proyecto para cualquiera que lo clone (referencias rotas en UXML/USS/prefabs hasta que corra la herramienta manualmente), y obligaría a cada colaborador a pagar su propia cuota solo para tener el mismo placeholder que ya generó otra persona. El día que se reemplace un placeholder por arte final, el PNG final ocupa el mismo lugar y el commit lo deja en el historial como cualquier otro asset.
