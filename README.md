# EurekaAggro

Dalamud plugin for FFXIV tailored specifically for Eureka (**Anemos, Pagos, Pyros, Hydatos**, and the **Baldesion Arsenal**).

## Key Features

- **Precise Aggro Taxonomy**:
  - 🦻 **Sound**: 360° detection, 10.5m radius. Walking is 100% safe. Covers sleeping dragons (`slumbering dragon`, `voidragon`), piranus/ogrebons, amphibians, arthropods (crabs, spiders), blind burrowers (worms, leeches, moles), and shelled mollusks.
  - 🌀 **Proximity**: 360° detection, 10.2m radius. Attacks regardless of walking or running (slimes, carnivorous plants, cactuars, mandragoras, golems, magitek constructs).
  - 🩸 **Blood**: 360° detection, 25.0m radius. Detects players with HP < 80% (ashkin, wraiths, specters, corpses, skeletons, zombies, dullahans).
  - ✨ **Magic**: 360° detection, 18.0m radius. Detects spellcasting within range (sprites and elementals).
  - 👁️ **Sight**: Frontal cone (~100°), 10.2m range. Passing behind or to the sides is safe (beasts, birds, ungulates, reptiles).
- **Smart Elemental Level Filtering**: Automatically or manually hides non-threatening monsters based on your current elemental level.
- **Zero-Allocation Rendering**: High-performance overlay rendering with zero heap allocations during the render loop.
