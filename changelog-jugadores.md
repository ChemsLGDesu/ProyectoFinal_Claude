# Changelog — Parches y Novedades

¡Gracias por jugar **TicTacToe XO**! Aquí encontrarás un resumen de cada actualización.

---

## v0.6.0 — 11 de agosto de 2026 · *Beta cerrada en Android*

Primera versión que sale del escritorio: esta es la build que se reparte a los testers.

### Novedades

**El juego ya se juega en el teléfono**

TicTacToe XO llega a Android. Funciona en cualquier teléfono con Android 7.1 o superior.

**La pantalla ya no gira**

El juego queda fijo en vertical. Antes podía girarse a horizontal, donde nunca se había comprobado
que la interfaz entrara bien.

**Tableros disponibles: 3×3 y 6×6**

Los tableros de 9×9 y 11×11 no aparecen por ahora. En la pantalla de un teléfono sus casillas quedan
más chicas de lo que se puede tocar con precisión, así que vuelven cuando tengan una solución propia
para pantallas pequeñas. Sus reglas y recompensas siguen intactas.

**Ajustes de interfaz**

Los botones de la barra inferior se ensancharon para que los idiomas con palabras largas —como el
alemán— no queden pegados al borde.

### Todavía en camino

- La tienda existe como pantalla, pero aún no hay nada que comprar.
- El juego no tiene sonido todavía; los controles de volumen ya guardan su valor.
- La sesión es anónima: al desinstalar el juego se pierde el progreso. La vinculación de cuenta llega
  más adelante.

---

## v0.5.0 — 23 de julio de 2026

### Novedades

**¡Modo Ranked! Competí y sube de rango**

Ahora tenés un modo competitivo oficial. Acá está lo que necesitás saber:

**Cómo jugar Ranked**
1. En Modo Juego, elegí "Ranked" (antes decía "Próximamente")
2. Elegí tablero: 3x3 o 6x6 (los tableros grandes vienen después)
3. Buscamos un rival de tu nivel — el emparejamiento mira tu puntuación actual (MMR) y busca a alguien similar
4. En 3x3: jugás 2 partidas (vos empezás una, tu rival empieza la otra) — así es justo porque el primer movimiento importa mucho
5. En 6x6: jugás una partida normal

**Tu Rango y Puntuación**

Después de cada partida ganás o perdés puntos de rango (MMR). Hay 5 tierras:
- Bronce: 0-899 puntos
- Plata: 900-1099 puntos
- Oro: 1100-1299 puntos
- Platino: 1300-1499 puntos
- Diamante: 1500+ puntos

Tu puntuación empieza en 1000. Después de 5 partidas clasificatorias, ya aparecés en la tabla de posiciones.

**Tabla de Clasificación (Leaderboard)**

Nueva pantalla en el menú: mirá dónde estás vos en el ranking global. Podés ver los mejores jugadores, y tu propia posición si estás en el top. Cada tablero (3x3 y 6x6) tiene su propia tabla.

**Temporadas**

Las temporadas duran un mes: cierran **el día 28 de cada mes**, a las 00:00 UTC. Al cierre de temporada, si ganaste bastantes partidas, recibís una recompensa especial según tu tier final:
- Bronce: 100 monedas
- Plata: 200 monedas
- Oro: 400 monedas
- Platino: 700 monedas
- Diamante: 1200 monedas + marco cosmético animado
- Top 100 global: +500 monedas + banner exclusivo

Después, la tabla se reinicia para la próxima temporada.

**Abandono en Ranked**

Si alguno se va durante la partida, cuenta como derrota completa (perdés puntos). Si ambos abandonan, la partida se anula (no ganás ni perdés puntos, es un empate).

**Monedas vs Rango**

Las recompensas en Ranked son más bajas que en Quickmatch porque lo importante acá es subir de rango, no ganar monedas. Por eso:
- **3x3 serie:** 10 monedas por victoria, 4 por empate
- **6x6 partida:** 15 monedas por victoria, 5 por empate

En Quickmatch seguís ganando más.

### Cambios de Balance

**Recompensas Ranked (Deliberadamente Bajas)**
- Ranked está pensado para competir por rango, no por monedas
- Por eso las monedas en Ranked son menores que en Quickmatch
- Así la economía no se rompe y Quickmatch sigue siendo atractivo

**Matchmaking Más Inteligente**
- El sistema busca rivales de tu nivel (mismo MMR aprox.)
- Si esperas mucho, empieza a ampliar la búsqueda
- 0-9 segundos: rivales con ±100 puntos
- 10-19 segundos: ±200 puntos
- 20-29 segundos: ±350 puntos
- 30-44 segundos: ±600 puntos
- 45+ segundos: sin restricción (cualquiera)

### Correcciones

- Arreglado: las partidas online ahora reportan correctamente si fueron Ranked o Quickmatch en nuestros análisis (antes decía todas "Quickmatch")

---

## v0.4.2 — 23 de julio de 2026

### Correcciones

- **Partidas online abandonadas:** Si los dos jugadores se van y dejan la partida colgada, ahora se cierra automáticamente como empate después de un tiempo. Antes se quedaba esperando indefinidamente. Nadie gana ni pierde en eso — es lo correcto.

---

## v0.4.1 — 23 de julio de 2026

### Novedades

**Recompensas Online Mejoradas**
- Las victorias online ahora te dan un poco más de monedas que antes
- Tabla de recompensas online:
  - **3×3:** 12 monedas por victoria (antes 10)
  - **6×6:** 25 monedas por victoria (antes 20)
  - **9×9:** 37 monedas por victoria (antes 30)
  - **11×11:** 50 monedas por victoria (antes 40)

### Cambios de Balance

**Recompensas Online Ajustadas**
- **Victoria:** Recibirás un 25% más de monedas que antes
- **Empate:** Recibirás monedas iguales al valor base (sin bonus)
- **Derrota:** No recibirás monedas (antes había un piso mínimo; se removió)
- **Por abandono del rival:** No recibirás monedas (anti-abuso)

**Límites Diarios Implementados**
- Máximo 200 monedas online por día (límite anti-abuso)
- Máximo 3 victorias pagadas contra el mismo rival cada 24 horas (después no sumas)
- Estos límites se controlan desde el servidor, no desde tu dispositivo

### Correcciones

- Arreglado: las recompensas online ahora se entregan correctamente desde el servidor
- Arreglado: se removió el piso mínimo de monedas en las derrotas

---

## v0.4.0 — 23 de julio de 2026

### Novedades

**🌐 ¡Partidas Online! (Quickmatch)**
- Jugá contra rivales reales en tiempo real
- **Cómo jugar:**
  1. Ve a Modo Juego → Quickmatch
  2. Elige tu tamaño de tablero favorito (3×3, 6×6, 9×9, 11×11)
  3. Buscamos un rival mientras ves un spinner
  4. ¡Juega! Tu rival ve tus movimientos al instante
- Si la búsqueda tarda más de 45 segundos, tenés la opción de jugar contra la IA mientras esperas
- Ganas las mismas recompensas que contra la IA

**📊 Modo Ranked (Próximamente)**
- En el menú ves "Ranked — Próximamente"
- Cuando esté listo, competirás por puntos y clasificación global

**🔄 Navegación Mejorada**
- Botones "Atrás" ahora funcionan correctamente entre pantallas
- Ya no te quedas atrapado entre Perfil y Tienda

### Cambios de Balance

- Las recompensas online mantienen la misma proporción que el juego local
- Sin cambios de dificultad de IA

### Correcciones

- Arreglado: navegación que rebotaba entre Perfil y Tienda
- Arreglado: algunos controles de pantalla que no respondían correctamente
- Arreglado: tableros vacíos en ciertos modos

---

## v0.3.0 — 23 de julio de 2026

### Novedades

**🌍 Ahora en 10 Idiomas**
- Juega en tu idioma: Español, Inglés, Francés, Alemán, Portugués, Italiano, Indonesio, Vietnamita, Turco y Polaco
- Selector de idioma en Ajustes — cambia el idioma en cualquier momento, ¡sin reiniciar el juego!
- Todos los textos de las pantallas están traducidos y listos

### Cambios de Balance

- Ninguno (los cambios de v0.3.0 son solo de idioma)

### Correcciones

- (Sin correcciones en esta versión)

---

## v0.2.0 — 23 de julio de 2026

### Novedades

**📊 Nuevas Pantallas**
- **Mi Perfil** — Ve tu nombre de jugador único (formato "Nombre#1234")
- **Historial de Partidas** — Revisa tus últimas 50 partidas: cuándo jugaste, contra quién, y si ganaste
- **Ajustes** — Controla el volumen de sonido y música (aunque aún no hay audio en el juego), selector de idioma listo para próximas versiones
- **Tienda** — Próximamente... ¡cosmética y pases de batalla en camino!

**☁️ Tu Progreso Guardado en la Nube**
- Cuando tu dispositivo se conecta a internet, tu cartera de monedas y tu historial se sincronizan automáticamente
- Si no hay conexión, juegas normalmente — todo se guarda localmente y se sincroniza cuando vuelves a conectar
- **Nota:** El servidor aún no está completamente activado, pero la infraestructura está lista. Habla con los desarrolladores si quieres activar esta característica.

**🆔 Identidad de Jugador**
- Cada jugador recibe un nombre único (formato "Nombre#1234") que aparece en tu perfil
- Tu nombre se guarda localmente y está listo para sincronizar cuando se active el servidor

**🎮 Flujo "Eliminar mis datos"**
- Opción segura en Ajustes para borrar tu historial de partidas y monedas locales (confirmación necesaria)
- Si en el futuro nos conectamos a un servidor, habrá una opción adicional "Eliminar cuenta y datos"

### Cambios de Balance

- Ninguno (v0.2.0 es solo interfaz y preparativos para la nube)

### Correcciones

- Diseño visual completamente renovado según las últimas especificaciones — pantallas más limpias y más fáciles de navegar

---

## v0.1.0 — 23 de julio de 2026

### Novedades

**🎮 Juego Local Completamente Funcional**
- Juega contra la inteligencia artificial en tu dispositivo (sin conexión necesaria)
- Elige entre 4 tamaños de tablero:
  - **Pequeño (3×3)** — Clásico y desafiante
  - **Medio (6×6)** — Más estrategia
  - **Grande (9×9)** — Batallas épicas
  - **Gigante (11×11)** — ¡El máximo de complejidad!

**🤖 Cuatro Dificultades de IA**
- **Fácil** — La IA comete errores, perfecta para aprender
- **Normal** — Rival equilibrado que pone a prueba tu estrategia
- **Difícil** — Un oponente muy competitivo (¡prácticamente imbatible en 3×3!)
- **Adaptativa** — La IA aprende de tus victorias y derrotas, ajustándose a tu nivel en tiempo real

**👥 Modo 2 Jugadores Local**
- Juega contra un amigo en el mismo dispositivo
- Alternancia automática de turnos
- Perfecto para competencias cara a cara

**💰 Sistema de Monedas**
- Gana monedas blandas por cada victoria
- Más monedas = tableros más grandes = más recompensa
- Bonus si ganas contra dificultades más altas
- Límites diarios para mantener el juego justo

**🎨 Interfaz Limpia y Moderna**
- Tema oscuro con acentos neón
- Navegación intuitiva entre pantallas
- Feedback visual claro en cada movimiento

### Cambios de Balance

- Empates solo ocurren cuando el tablero se llena completamente
- Se permite hacer líneas de más de k símbolos (overline permitido)
- No hay límite de tiempo por turno en esta versión
- Las dificultades varían su estrategia según el tamaño del tablero

### Correcciones

- (Primera versión pública, sin correcciones previas)

---

**¿Encontraste un bug?** Repórtalo en el proyecto. ¡Gracias por tu feedback!
