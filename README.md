# EurekaAggro

Plugin para Dalamud (FFXIV) especializado en Eureka (**Anemos, Pagos, Pyros, Hydatos** y **Baldesion Arsenal**).

## Características principales

- **Taxonomía precisa de aggro**:
  - 🦻 **Sound (Oído)**: Cono 360°, 10.5m. Caminar (walk) es 100% seguro. Mobs como dragones durmientes (`slumbering dragon`, `voidragon`), piranus/ogrebons, artrópodos/cangrejos/arañas, sanguijuelas y topos.
  - 🌀 **Proximity (Proximidad)**: Cono 360°, 10.2m. Atacan siempre que entres en rango (slimes, plantas, cactuars, mandrágoras, gólems, constructos).
  - 🩸 **Blood (Sangre)**: Cono 360°, 25.0m. Detectan si el jugador tiene menos del 80% HP (espectros, zombis, cadáveres, skatenes).
  - ✨ **Magic (Magia)**: Cono 360°, 18.0m. Detectan si se castea un hechizo dentro del rango (sprites y elementales).
  - 👁️ **Sight (Vista)**: Cono frontal ~100°, 10.2m. Pasar por detrás o a los lados es seguro.
- **Filtro inteligente de nivel elemental**: Oculta mobs que ya no representen peligro según tu nivel elemental actual (configurable manual o automático).
- **Rendimiento de alto nivel**: Sin allocations de heap en el bucle de dibujado, seguro contra memory leaks.
