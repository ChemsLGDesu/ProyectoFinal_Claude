# Leaderboards config-as-code (Ranked, Milestone 5)

`ranked_3x3.lb` / `ranked_6x6.lb` - config-as-code for [`Docs/design-doc.md` sección 6.6](../../Docs/design-doc.md#66-leaderboard)
(Leaderboard de Ranked). Deploy junto con el resto vía `Window → Deployment` (el paquete
`com.unity.services.deployment` instalado ya lista Leaderboards entre los servicios soportados - ver
`CloudCode~/TicTacToeModule/README.md` "Cómo deployar").

**Extensión `.lb` sin verificar contra el Editor real** (este task no abre Unity): la doc oficial
(`https://docs.unity.com/en-us/leaderboards/leaderboards-assets.md`) confirma el JSON schema
(`https://ugs-config-schemas.unity3d.com/v1/leaderboards.schema.json`, usado como `$schema` en ambos
archivos) y el flujo `Create → Services → Leaderboard Configuration` en el Editor, pero no publica la
extensión exacta del asset generado. `.lb` es la convención más probable (mismo patrón 2-3 letras que
`.rc`/`.mmq`/`.ccmr`) - **verificar en el Editor** creando un asset nuevo vía ese menú y comparando la
extensión antes de asumir que `Window → Deployment` va a detectar estos dos archivos tal cual están; si
difiere, renombrar sin tocar el contenido.

## Por qué DOS leaderboards, no uno por temporada

`ranked_3x3`/`ranked_6x6` son los ÚNICOS dos IDs de leaderboard que este proyecto necesita para Ranked,
para siempre - **no** se crea un `ranked_3x3_s{N}` nuevo cada temporada. El propio servicio de
Leaderboards tiene **reset nativo con archivado** (`ResetConfig`): cada vez que el leaderboard resetea
(por `Schedule`), la versión actual se archiva bajo un `versionId` propio y queda consultable para
siempre vía `GetLeaderboardVersionScoresAsync`/`GetLeaderboardVersionsAsync` (ver
`CloudCode~/TicTacToeModule/RankedLeaderboardStore.cs`) - confirmado por reflexión contra el ensamblado
0.0.26 instalado y `https://docs.unity.com/en-us/leaderboards/concepts/{resets,archives}.md`. Esto
resuelve directamente la pregunta 5 de `Docs/design-doc.md` sección 6.8.

## `UpdateType: keepLatest` (no `keepBest`)

**Crítico**: el MMR tiene que poder BAJAR (una derrota, un tick de decay, un soft reset de temporada) y
que el leaderboard lo refleje. `keepBest` (el default más intuitivo) conservaría el máximo histórico del
jugador para siempre, contradiciendo `Docs/design-doc.md` sección 6.6: "El puntaje publicado: el MMR
crudo, entero... Cualquier transformación rompe esa correspondencia". No cambiar esto sin releer esa
sección.

## Sincronización obligatoria con `RANKED_CONFIG`

`ResetConfig.Start`/`Schedule` de estos dos archivos y `RANKED_CONFIG.seasonStartUnixSeconds`/
`seasonDurationMonths` (`Assets/RemoteConfig/GameConfig.rc`) representan **el mismo límite de temporada**,
pero viven en dos superficies de config-as-code independientes (Leaderboards vs Remote Config) que UGS
no sincroniza automáticamente entre sí. Quien deploye:

1. `seasonStartUnixSeconds` (Remote Config, unix seconds) y `ResetConfig.Start` (estos dos archivos,
   RFC-3339) apuntan al **mismo instante**: hoy `1787875200` = `2026-08-28T00:00:00Z`, el ancla de la
   temporada 1.
2. `Schedule: "0 0 28 * *"` (00:00 UTC del día 28 de cada mes) debe coincidir con
   `seasonDurationMonths: 1` y con el día del ancla. Si `game-designer` cambia la duración de temporada,
   ambos valores cambian juntos — y ojo: un cron `*/N` en el campo de mes significa "cada N-ésimo mes del
   año calendario", **no** "cada N meses contados desde el ancla", así que para duraciones > 1 mes hay
   que rederivar el cron a mano.
3. **El ancla no puede caer después del 28.** `RankedSeasonCalculator` usa `AddMonths`, que *clampea*
   (31 de enero → 28 de febrero); un cron con día 31 simplemente **no dispara** en los meses que no lo
   tienen. Del 29 en adelante las dos superficies dejan de describir las mismas fechas.
4. Si algún día se necesita re-alinear manualmente (p. ej. tras un reset manual accidental desde el
   Dashboard), usar **reset manual con archivado** desde el Dashboard (`Leaderboard → Reset Leaderboard`,
   tildando la opción de archivo) para no perder la temporada en curso, y verificar
   `RankedSeasonCalculator.CurrentSeasonId` siga devolviendo el número de temporada esperado con el
   `seasonStartUnixSeconds` vigente.

Esto **ya no es solo convención**: `Assets/Scripts/Tests/Game/SeasonConfigAlignmentTests.cs` compara los
tres archivos como texto y falla si el ancla, el día del mes, la hora o la cadencia se separan.

Ver `CloudCode~/TicTacToeModule/RankedSeasonCalculator.cs` para el cálculo exacto de temporada que Cloud
Code usa, y `RankedProfileStore.TouchAsync` para el rollover perezoso por jugador que depende de que
ambas superficies estén alineadas.

## Sin `TieringConfig`: los tiers son de Cloud Code

Estos dos archivos **no** definen bandas de tier a propósito, y el Dashboard tiene `Tiers = None`. La
tabla de tiers que manda es `RANKED_CONFIG.tierNames`/`tierMinMmr`/`tierSoftCurrency`, que
`RankedTierCalculator` usa para el premio de fin de temporada y para lo que muestra Perfil; el cliente
dibuja el badge desde ahí. Un `TieringConfig` en el leaderboard sería una **segunda** tabla sobre la
misma escala de MMR, y dos tablas se separan: el jugador vería un tier en la tabla y otro en su perfil.
Si alguna vez se agrega, tiene que espejar `tierMinMmr` exactamente — el test
`NoLeaderboardCarriesItsOwnTierBands` está para forzar esa conversación antes de que pase.

## Desempate en puntajes iguales

No verificado contra un valor configurable - el schema de `.lb` no expone ningún campo de tie-break
(solo `TieringConfig`, que son bandas de tier, no desempate de rank). Se acepta el default del servicio
(`Docs/design-doc.md` sección 6.6: "Si el servicio no permite configurar la política de desempate, se
acepta su default y no se simula"). No usado en este módulo para ninguna decisión de negocio.
