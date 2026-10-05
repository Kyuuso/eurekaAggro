# EurekaAggro

**EurekaAggro** is a Dalamud tactical radar and safety assistant plugin for Final Fantasy XIV, engineered specifically for the Forbidden Land, Eureka expeditions (**Anemos, Pagos, Pyros, Hydatos**, and the **Baldesion Arsenal**).

---

## Key Features

### 1. Accurate Aggro Taxonomy & 3D Overlays
- 🦻 **Sound Aggro (10.5m radius)**: Running triggers monsters; **walking is 100% safe**. Covers lethal Sleeping Dragons (*Sleeping Dragon*, *Slumbering Dragon*, *Voidragon*), crabs, bats, worms, salamanders, and blind cave creatures.
- 🌀 **Proximity Aggro (10.2m radius)**: 360-degree detection regardless of walking or running (slimes, carnivorous plants, cactuars, mandragoras, golems, magitek constructs).
- 🩸 **Blood Aggro (25.0m radius)**: Detects low HP players (< 80%) across large distances (ashkin, wraiths, specters, corpses, skeletons, zombies, dullahans).
- ✨ **Magic Aggro (18.0m radius)**: Detects spellcasting within range (sprites and elementals). Displays *"DO NOT CAST SPELLS"* warnings.
- 👁️ **Sight Aggro (10.2m range, ~100° cone)**: Frontal directional field of view. Passing behind or to the sides is safe (beasts, birds, reptiles).

### 2. Auto-Walk Safety Trigger for Sleeping Dragons
- **Automatic Walk Mode**: Automatically engages Walk mode when entering a configurable proximity range (default: 12.0m) near lethal Sleeping Dragons.
- **Prevents Running Wipes**: Eliminates accidental aggro while exploring or navigating narrow passages.
- **Auto-Restore**: Automatically restores Run mode once you are at a safe distance.
- **HUD Indicator**: Displays an unmistakable on-screen badge `✔ EUREKA AUTO-WALK ENGAGED (Dragon - Dist)` when active.
- **Full Logging**: Outputs events to the Dalamud console (`/xllog`) and has an optional toggle for in-game chat notifications.

### 3. Enemy Action Alerts (HUD Cast Monitor)
- **Real-Time Tactical HUD Alert**: A floating alert window displays whenever a nearby or targeted enemy begins casting a dangerous ability.
- **Interrupt / Silence Prompts**: Highlights interruptible spells (`⚡ INTERRUPT / SILENCE AVAILABLE`) so tanks and ranged DPS can cancel them before completion.
- **Stun & Line of Sight (LOS) Reminders**: Warns you to stun enemies or break line of sight behind terrain to avoid lethal gaze attacks and room-wide debuffs.
- **Cast Progress Bar**: Live progress bar showing cast completion percentage and remaining time.
- **Lumina Action Resolution**: Reads the game's actual action database to show the real ability name (*Dread Gaze*, *Grim Halo*, *Bad Breath*, etc.).

### 4. Real-Time Mutation & Adaptation Tracker
- Checks active weather and Eorzea time against Eureka mutation rules.
- Adds indicators above eligible monsters (e.g. `🧬 CAN MUTATE NOW: Fog / Night`) to maximize Mutation Box farming efficiency.

### 5. In-Game Database Browser
- `/eurekaaggro` or `/ea` opens a multi-tab configuration interface:
  - **General & Visual Settings**: Adjust detection ranges, vertical cliff tolerance, safety margins, color pickers, and test Walk mode.
  - **Eureka Monsters**: Fully responsive, searchable table with every Eureka mob, elemental level, danger tier, and aggro type.
  - **Enemy Actions & Counters**: Searchable table of dangerous enemy casts, interruptibility, and counter strategies.

### 6. Full Color Customization
- Integrated RGBA color pickers for every visual element:
  - Sound circles (Sleeping Dragons)
  - Vision cones (Sight aggro)
  - Proximity circles
  - Blood aggro circles (Undead)
  - Magic aggro circles (Sprites)
  - Distance guide lines (< 10m red, >= 10m green)
  - Mutation tags and floating mob labels
  - One-click **"Reset All Colors to Default"** button.

### 7. Zero-Allocation Architecture
- High-performance overlay rendering with zero heap allocations during the render loop, ensuring smooth 60+ FPS gameplay with no garbage collection stutter.

---

## Commands

| Command | Description |
| :--- | :--- |
| `/eurekaaggro` | Opens or closes the main configuration window. |
| `/ea` | Short alias to open or close the main configuration window. |
| `/xllog` | Dalamud log console (view real-time auto-walk and cast monitor logs). |

---

## Installation via Dalamud Custom Repository

1. Open `/xlplugins` in Final Fantasy XIV.
2. Click on **Settings** -> **Custom Plugin Repositories**.
3. Paste the following URL and click **Save**:
   ```
   https://raw.githubusercontent.com/Kyuuso/dalamud-plugins/main/pluginmaster.json
   ```
4. Search for **Eureka Aggro** in the plugin list and click **Install**.

