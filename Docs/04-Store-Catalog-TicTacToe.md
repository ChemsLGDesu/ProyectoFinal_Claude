---
tags: [store, monetization, iap, tictactoe]
---

# Store Catalog — TicTacToe (IAP)

## Principio rector

Todo lo vendible es **cosmético**: pay-to-look, nunca pay-to-win (ver [[01-Directrices-Proyecto]] y [[02-GDD-TicTacToe#5. Monetización]]). Ninguna skin, marco, efecto o bundle puede afectar el tamaño de tablero disponible, la dificultad de la IA, el matchmaking o el resultado de una partida.

## Categorías de producto

### 1. Skins de fichas (X y O)
- Packs temáticos: Neón, Madera, Espacio, Animales, Retro arcade, Minimalista.
- Se venden como par (X+O a juego) o sueltas, para poder mezclar.

### 2. Skins de tablero / fondo
- Fondo del tablero y estilo de la grilla (neón, papel, madera, espacio).
- Fondo de pantalla de menú/juego (parallax simple, no debe distraer durante la partida).

### 3. Efectos visuales
- Efecto al colocar una ficha (partícula, trail).
- Animación de victoria (confetti, fuegos artificiales, etc.).
- Efecto en la línea ganadora (glow al completarse los 3/4/5 en línea).

### 4. Sonido
- Packs de sonido: colocar ficha, ganar, perder, tick del timer de turno.

### 5. Personalización de perfil
- Íconos de perfil (avatares).
- Marcos de perfil (bordes — ej. dorado/plateado, o ligados a una temporada de Ranked pasada).
- Color/estilo del nombre visible en leaderboard y en partidas online.
- Banners de perfil (fondo detrás del ícono).

### 6. Social / comunicación en partida
- Packs de "reacciones rápidas" (stickers tipo gg, buena jugada, revancha) para usar contra el rival en partidas online. Deliberadamente **no** es chat libre — evita el costo de moderación de texto y el riesgo de abuso/toxicidad.

### 7. Progreso / moneda
- Packs de moneda hard (varios tamaños, con mejor "tasa de cambio" en los packs más grandes).
- Quitar anuncios (pago único — ya definido en el GDD).
- Bundles: pack inicial (moneda + 1 skin + quitar ads con descuento) y bundles estacionales.

### 8. Estacional / eventos (post-lanzamiento, fuera de alcance del día 1)
- Skins de tiempo limitado ligadas a eventos o a la temporada de Ranked. Un pase de temporada cosmético queda anotado como candidato futuro, no como parte del catálogo inicial.

## Reglas de precio

| Tipo de ítem | Moneda | Notas |
|---|---|---|
| Skins de fichas, tablero, efectos, sonido, íconos, marcos, banners, reacciones | Moneda **soft** o **hard** | Nunca dinero real directo — siempre se compran con moneda del juego, para mantener un único punto de fricción de pago real (los packs de moneda) |
| Packs de moneda hard | Dinero real | Vía IAP de plataforma (Google Play / App Store) |
| Quitar anuncios | Dinero real | Pago único, vía IAP de plataforma |
| Bundles | Dinero real o mixto | Un bundle puede incluir moneda + cosméticos + quitar ads en un solo IAP |

## Esquema de datos (`STORE_CATALOG` en Remote Config)

```json
{
  "skus": [
    {
      "sku": "skin_xo_neon",
      "type": "symbol_skin",
      "displayNameKey": "store.skin_xo_neon.name",
      "price": { "currency": "soft", "amount": 500 },
      "iapProductId": null,
      "rarity": "common"
    },
    {
      "sku": "frame_profile_gold_ornate",
      "type": "profile_frame",
      "displayNameKey": "store.frame_gold_ornate.name",
      "price": { "currency": "hard", "amount": 300 },
      "iapProductId": null,
      "rarity": "rare"
    },
    {
      "sku": "iap_remove_ads",
      "type": "entitlement",
      "displayNameKey": "store.remove_ads.name",
      "price": { "currency": "real_money" },
      "iapProductId": "com.company.tictactoe.remove_ads",
      "rarity": null
    },
    {
      "sku": "iap_hard_currency_pack_m",
      "type": "currency_pack",
      "displayNameKey": "store.hard_pack_m.name",
      "price": { "currency": "real_money" },
      "iapProductId": "com.company.tictactoe.hardpack_m",
      "grants": { "hard_currency": 1200 }
    }
  ]
}
```

Igual que el resto de `STORE_CATALOG`, esta config viaja por Remote Config pero el cliente no la lee directo: la expone `GetGameConfig` en Cloud Code (ver [[03-Arquitectura-UGS-TicTacToe#Remote Config]]), así el precio/entitlement que ve el cliente es siempre el mismo que valida el servidor al canjear o comprar.

## Cloud Code: validación y entrega

Se suman dos funciones nuevas al módulo `TicTacToeModule` (ver [[03-Arquitectura-UGS-TicTacToe#Cloud Code]]):

| Función | Rol |
|---|---|
| `ValidatePurchase` | Ya definida en la arquitectura: valida el recibo de IAP (App Store/Google Play) contra la plataforma correspondiente y, si es válido, entrega lo que otorgue el SKU (moneda, entitlement de quitar ads, o un bundle completo) a `inventory`/`currency` en Cloud Save |
| `RedeemStoreItem` | Para ítems comprados con moneda soft/hard (no dinero real): descuenta el saldo **server-side** desde `currency` y entrega el ítem en `inventory`. Evita que el cliente manipule su propio saldo para "comprar gratis" |

Ambas escriben en la misma clave de Player Data (`currency` + `inventory`) que ya usa el resto del sistema de economía.

## Analytics

Se reutilizan los eventos ya definidos y se agregan dos:

- `iap_purchased` (+sku, price) — ya existente.
- `iap_failed` — ya existente.
- `store_item_redeemed` (+sku, currency_type) — nuevo, para compras con moneda soft/hard.
- `store_viewed` (+section) — nuevo, para medir qué categorías de la tienda se visitan más y priorizar contenido futuro con datos reales en vez de intuición.

## Conexiones

- [[01-Directrices-Proyecto]]
- [[02-GDD-TicTacToe]]
- [[03-Arquitectura-UGS-TicTacToe]]
- [[05-UI-Pantallas-TicTacToe]]

## Fuente

Derivado de las decisiones de monetización de `02-GDD-TicTacToe.md` (sección 5) y del catálogo de servicios de `03-Arquitectura-UGS-TicTacToe.md` (Remote Config y Cloud Code).
