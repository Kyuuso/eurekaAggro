#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Script to apply high-quality, authentic Castilian Spanish (Spain - es-ES) translations
for Eureka Suite with complete FFXIV / MMO terminology coverage.
"""

import os
import re
import xml.etree.ElementTree as ET

# Complete 100% dictionary for all keys in Strings_en.resx
TRANSLATIONS_ES = {
    # Styling, colors & theme
    "Color": "Color",
    "Language": "Idioma",
    "Teal": "Turquesa",
    "Blue": "Azul",
    "Pink": "Rosa",
    "Custom RGB": "RGB personalizado",
    "Compact mode": "Modo compacto",
    "Loading UI fonts...": "Cargando fuentes de la interfaz...",
    "UI fonts failed to load. See the plugin log.": "Error al cargar las fuentes de la interfaz. Consulta el registro del plugin.",
    "Settings": "Ajustes",
    "Ko-fi": "Ko-fi",
    "Discord": "Discord",
    "Scroll down to \"The Dumpster Fire\" channel to discuss issues / suggestions for specific plugins.": "Desplázate hacia abajo hasta el canal \"The Dumpster Fire\" para comentar dudas o sugerencias sobre plugins concretos.",
    "Loaded": "Cargado",
    "About": "Acerca de",
    "Area Selection": "Selección de zona",
    "AutoRetainer Settings": "Ajustes de AutoRetainer",
    "BFE Settings": "Ajustes de BFE",
    "Bunnies": "Conejos",
    "Bunny Fate Engine": "Motor de FATEs de conejos",
    "Click to Copy Repo URL": "Haz clic para copiar la URL del repositorio",
    "Copy Icon Guide Link": "Copiar enlace de la guía de iconos",
    "Currently Not Supported!": "¡Actualmente no compatible!",
    "Debug Stats": "Estadísticas de depuración",
    "Dependencies": "Dependencias",
    "Dismounting": "Bajando de la montura",
    "Enable Logout After Completion": "Cerrar sesión al finalizar",
    "Enable Multi Support": "Activar soporte multipersonaje",
    "Enable Repair": "Activar reparaciones",
    "Enable Retainer Support": "Activar soporte para retainers",
    "Enable Submarine Support": "Activar soporte para submarinos",
    "Enables retainer support for this character.": "Activa el soporte para retainers en este personaje.",
    "Entering Pyros": "Entrando a Pyros",
    "General Settings": "Ajustes generales",
    "Get Repo Url": "Obtener URL del repositorio",
    "Gil Earned": "Guiles obtenidos",
    "Go To Personal/FC House After Completion": "Ir a la casa personal o de la FC al finalizar",
    "Going to Eureka": "Yendo a Eureka",
    "Going to FC": "Yendo a la casa de la FC",
    "Going to Personal": "Yendo a la casa personal",
    "Going to Repair Mender": "Yendo al reparador",
    "Gold Coffers": "Cofres de oro",
    "Silver Coffers": "Cofres de plata",
    "Bronze Coffers": "Cofres de bronce",
    "Hold Ctrl to enable the button.": "Mantén pulsado Ctrl para habilitar el botón.",
    "Hours": "Horas",
    "Hydatos Stats": "Estadísticas de Hydatos",
    "Idle": "En espera",
    "Idle. Select an area and press Start to begin.": "En espera. Selecciona una zona y pulsa Iniciar para comenzar.",
    "If crafters are leveled, uses dark matter for self repair.": "Si las clases de artesanía tienen nivel suficiente, usa materia oscura para autorrepararse.",
    "In Bunny Fate": "En la FATE de conejos",
    "Infinite": "Infinito",
    "Installed, not loaded": "Instalado, no cargado",
    "Leaving Duty": "Saliendo de la misión",
    "Lifetime": "Total histórico",
    "Load all required plugins to start Bunnies automation.": "Carga todos los plugins requeridos para iniciar la automatización de conejos.",
    "Logging Out": "Cerrando sesión",
    "Minutes": "Minutos",
    "Missing": "Faltante",
    "Mounting Up": "Subiendo a la montura",
    "Moving to {0}": "Desplazándose a {0}",
    "Not available": "No disponible",
    "OG Author": "Autor original",
    "Open settings window": "Abrir ventana de ajustes",
    "Pagos Stats": "Estadísticas de Pagos",
    "Press to reset your stats.": "Pulsa para restablecer tus estadísticas.",
    "Pyros Stats": "Estadísticas de Pyros",
    "RESET STATS": "RESTABLECER ESTADÍSTICAS",
    "Refresh": "Actualizar",
    "Repair Value": "Durabilidad mínima para reparar",
    "Resending Retainers": "Reenviando retainers",
    "Run for X time": "Ejecutar durante un tiempo determinado",
    "Running": "En ejecución",
    "Select to start bunnies": "Selecciona para iniciar la ruta de conejos",
    "Self Repair": "Autorreparación",
    "Self Repairing": "Autorreparando",
    "Session": "Sesión",
    "Start Bunnies": "Iniciar conejos",
    "Start Pyros": "Iniciar Pyros",
    "State: {0}": "Estado: {0}",
    "Stats": "Estadísticas",
    "Stop": "Detener",
    "Task": "Tarea",
    "Teleport To Personal": "Teletransporte a la casa personal",
    "Teleport to Free Company": "Teletransporte a la compañía libre",
    "Threshold to repair gear": "Umbral de durabilidad para reparar equipo",
    "Time elapsed": "Tiempo transcurrido",
    "Total Stats": "Estadísticas totales",
    "Using Aethernet": "Usando el Aethernet",
    "Using Retainer": "Gestionando retainer",
    "WIP": "En desarrollo",
    "Waiting Navmesh": "Esperando a Navmesh",
    "Waiting at Fate": "Esperando en la FATE",
    "Will logout after looping is complete.": "Cerrará sesión cuando finalice el bucle.",
    "Will teleport to Personal or FC house after looping is complete.": "Se teletransportará a la casa personal o de la FC cuando finalice el bucle.",
    "{0} (legacy alias)": "{0} (alias heredado)",
    "AutoRetainer is currently not installed or enabled. Click to copy Repo.": "AutoRetainer no está instalado o activado. Haz clic para copiar el repositorio.",
    "Once gear conditions are met, will automatically repair at the vendor after next Bunny Fate.": "Una vez cumplidas las condiciones del equipo, reparará automáticamente en el comerciante tras la siguiente FATE de conejos.",
    "Set to Run Infinitely or set a timer up to 24 hours": "Configurar para ejecutar indefinidamente o fijar un temporizador de hasta 24 horas",
    "Recovered Bunnies automation for Eureka bunny fate routing, coffer hunting, and run stats.": "Automatización de conejos recuperada para rutas de FATEs en Eureka, búsqueda de cofres y estadísticas de sesión.",
    "The following plugins are required to be installed and enabled: ": "Es necesario tener instalados y activados los siguientes plugins: ",
    "Moving to Kugane Npc": "Yendo hacia el PNJ de Kugane",
    "Grabbing Coffer": "Abriendo cofre",
    "Eldthurs Mount": "Montura Eldthurs",
    "Pyros Hairstyles": "Peinado de Pyros",
    "Copycat Bulb": "Bulbo imitador",
    "Petrel Mount": "Montura Petrel",
    "Repo URL Copied": "URL del repositorio copiada",
    "Going to Personal House": "Yendo a la casa personal",
    "Required plugins unavailable for Bunnies:\n{0}": "Plugins requeridos no disponibles para conejos:\n{0}",
    "Start {0}": "Iniciar {0}",
    "BFE Debug": "Depuración de BFE",
    "Normal Raid": "Incursión normal",
    "Window appearance": "Apariencia de la ventana",
    "Compact visible on main window": "Modo compacto visible en la ventana principal",
    "Language visible on main window": "Selector de idioma visible en la ventana principal",
    "Transparency": "Transparencia",
    "Opacity (%)": "Opacidad (%)",
    "Auto-fade when unfocused": "Atenuar automáticamente al perder el foco",
    "Unfocused opacity (%)": "Opacidad sin foco (%)",
    "Unfocused delay (seconds)": "Retardo sin foco (segundos)",
    "Copy / ZIP Dalamud log": "Copiar / Comprimir registro de Dalamud (ZIP)",
    "Export capped log anyway": "Exportar registro limitado de todos modos",
    "Exporting log...": "Exportando registro...",
    "Log ZIP ready. Share it manually; remove old exports when you no longer need them.": "Archivo ZIP del registro listo. Compártelo manualmente y elimina exportaciones antiguas cuando ya no las necesites.",
    "Log export failed.": "Error al exportar el registro.",
    "Open Export Folder": "Abrir carpeta de exportación",
    "This log has reached Dalamud's 100 MiB limit. Logging may have stopped, so recent activity may be missing and this log may not help troubleshoot your current issue.": "Este registro ha alcanzado el límite de 100 MiB de Dalamud. Es posible que el registro se haya detenido, por lo que la actividad reciente podría faltar y este archivo no serviría para diagnosticar tu problema actual.",
    "Open XA Slave log tools": "Abrir herramientas de registro de XA Slave",
    "Opens XA Slave's Utility > XA Mods panel, which contains Dalamud Log Cleaner.": "Abre el panel Utilidades > XA Mods de XA Slave, que contiene el limpiador de registros de Dalamud.",
    "XA Slave log tools could not be opened.": "No se pudieron abrir las herramientas de registro de XA Slave.",
    "Aggro Lines": "Líneas de aggro",
    "Tracker": "Rastreador",
    "About & Credits": "Acerca de y créditos",
    "Main": "Principal",
    "Statistics": "Estadísticas",
    "Configuration": "Configuración",
    "Language:": "Idioma:",
    "Select plugin interface language": "Seleccionar idioma de la interfaz del plugin",
    "Location: {0}": "Ubicación: {0}",
    "Outside Eureka - Expedition features on standby": "Fuera de Eureka - Funciones de expedición en espera",
    "Auto-synced": "Sincronización automática",
    "Manual": "Manual",
    "Night": "Noche",
    "Day": "Día",
    "Elemental Lv. {0}": "Nv. elemental {0}",
    "Radar Controls & Filters": "Controles y filtros del radar",
    "Master Radar Enable": "Activar radar principal",
    "Only In Eureka Expeditions": "Solo en expediciones de Eureka",
    "Auto-Detect Elemental Level": "Autodetectar nivel elemental",
    "Manual Elemental Level": "Nivel elemental manual",
    "Safe Zone Distance (m)": "Distancia de zona segura (m)",
    "Safety Margin (m)": "Margen de seguridad (m)",
    "Monsters Nearby ({0})": "Monstruos cercanos ({0})",
    "Search Monsters...": "Buscar monstruos...",
    "Filter Safe Monsters": "Filtrar monstruos seguros",
    "Safe Level Difference": "Diferencia de nivel segura",
    "Show Vision Cones": "Mostrar conos de visión",
    "Show Sound Circles": "Mostrar círculos de oído",
    "Show Blood Circles": "Mostrar círculos de sangre",
    "Show Magic Warnings": "Mostrar avisos de magia",
    "Show Proximity Circles": "Mostrar círculos de proximidad",
    "Show Distance Lines": "Mostrar líneas de distancia",
    "Show Mob Labels": "Mostrar nombres de monstruos",
    "Fill Shapes (Solid)": "Rellenar formas (sólido)",
    "Fill Opacity": "Opacidad del relleno",
    "Aggro Cone Arc (Degrees)": "Ángulo del cono de aggro (grados)",
    "Aggro Types": "Tipos de aggro",
    "Sight": "Vista",
    "Sound": "Oído",
    "Blood": "Sangre",
    "Magic": "Magia",
    "Proximity": "Proximidad",
    "True Sight": "Visión verdadera",
    "Safe": "Seguro",
    "Aggro Risk": "Riesgo de aggro",
    "In Range": "En alcance",
    "Name": "Nombre",
    "Level": "Nivel",
    "Aggro Type": "Tipo de aggro",
    "Range": "Alcance",
    "Distance": "Distancia",
    "Status": "Estado",
    "Color Configuration": "Configuración de colores",
    "Color Vision (Sight)": "Color de visión (vista)",
    "Color Sound (Dragons / Hearing)": "Color de sonido (dragones / oído)",
    "Color Blood (Undead / Ashkin)": "Color de sangre (no muertos / ashkin)",
    "Color Magic (Sprites / Elementals)": "Color de magia (espíritus / elementales)",
    "Color Distance Near (< 10m)": "Color distancia cercana (< 10 m)",
    "Color Distance Far (>= 10m)": "Color distancia lejana (>= 10 m)",
    "Reset Colors to Default": "Restablecer colores predeterminados",
    "Always Show Sleeping Dragons": "Mostrar siempre dragones dormidos",
    "Always Show Undead (Blood)": "Mostrar siempre no muertos (sangre)",
    "Always Show Sprites (Magic)": "Mostrar siempre espíritus (magia)",
    "Auto-Walk Near Sleeping Dragons": "Caminar automático cerca de dragones dormidos",
    "Auto-Walk Trigger Distance (m)": "Distancia de activación de caminar automático (m)",
    "Restore Running After Leaving Dragons": "Reanudar correr tras alejarse de dragones",
    "Enable Sound / Beep Alerts": "Activar alertas de sonido / pitidos",
    "Audio Alert Volume": "Volumen de alertas de audio",
    "Open Cast Alert Preview": "Abrir vista previa de alerta de casteo",
    "Lock Cast Alert Position": "Bloquear posición de alerta de casteo",
    "Reset Cast Alert Position": "Restablecer posición de alerta de casteo",
    "[!] ENEMY ACTION ALERT": "[!] ALERTA DE ACCIÓN ENEMIGA",
    "[!] ENEMY ACTION ALERT (Drag to move)": "[!] ALERTA DE ACCIÓN ENEMIGA (Arrastrar para mover)",
    "STUN REQUIRED!": "¡SE REQUIERE ATURDIMIENTO!",
    "BREAK LINE OF SIGHT (HIDE)!": "¡ROMPER LÍNEA DE VISIÓN (ESCONDERSE)!",
    "INTERRUPT / SILENCE AVAILABLE!": "¡INTERRUPCIÓN / SILENCIO DISPONIBLE!",
    "{0} is casting: ": "{0} está casteando: ",
    "[OK] AUTO-WALK ENGAGED (SAFE)": "[OK] CAMINAR AUTOMÁTICO ACTIVADO (SEGURO)",
    "[WARN] RUNNING NEAR DRAGON! WALK NOW (KEYPAD /)": "[AVISO] ¡CORRIENDO CERCA DE DRAGÓN! CAMINA AHORA (TECLADO /)",
    "[SAFE] SLEEPING DRAGON": "[SEGURO] DRAGÓN DORMIDO",
    "[WARN] SOUND AGGRO! WALK TO AVOID (KEYPAD /)": "[AVISO] ¡AGGRO POR OÍDO! CAMINA PARA EVITAR (TECLADO /)",
    "[ALERT] HP < 80%: BLOOD AGGRO ACTIVE (HEAL TO SAFE)": "[ALERTA] PS < 80%: AGGRO POR SANGRE ACTIVO (CÚRATE PARA ESTAR A SALVO)",
    "[ALERT] CASTING DETECTED! SPRITE WILL AGGRO!": "[ALERTA] ¡CASTEO DETECTADO! ¡EL ESPÍRITU HARÁ AGGRO!",
    "[MAGIC] Sprite: DO NOT CAST SPELLS": "[MAGIA] Espíritu: NO CASTEAR HECHIZOS",
    "[Sound - Walk!]": "[Oído - ¡Camina!]",
    "[Blood / Undead]": "[Sangre / No muertos]",
    "[Magic / Sprite]": "[Magia / Espíritu]",
    "[Proximity]": "[Proximidad]",
    "[Sight]": "[Vista]",
    "Tracker:": "Rastreador:",
    "Create a new Eureka Tracker on ffxiv-eureka.com": "Crear un nuevo rastreador de Eureka en ffxiv-eureka.com",
    "6-char Code": "Código de 6 letras",
    "Enter the 6-character code of an existing tracker": "Introduce el código de 6 caracteres de un rastreador existente",
    "Password (optional)": "Contraseña (opcional)",
    "Optional password. Required to modify pop timers if the tracker is protected.": "Contraseña opcional. Requerida para modificar temporizadores si el rastreador está protegido.",
    "Copy password to clipboard": "Copiar contraseña al portapapeles",
    "Connect": "Conectar",
    "Disconnect": "Desconectar",
    "Connecting...": "Conectando...",
    "Auto-connect to Eureka Tracker when entering an expedition": "Conectar automáticamente al rastreador de Eureka al entrar en una expedición",
    "Sync Instance ID": "Sincronizar ID de instancia",
    "Instance ID:": "ID de instancia:",
    "Auto-detected": "Detectado automáticamente",
    "Manual Entry": "Entrada manual",
    "Not in Eureka": "No estás en Eureka",
    "Set": "Establecer",
    "Not Connected to a Tracker": "No conectado a ningún rastreador",
    "Enter a 6-character tracker code above or click (+) to create a new one.": "Introduce arriba un código de rastreador de 6 caracteres o pulsa (+) para crear uno nuevo.",
    "Pop Time": "Hora de aparición",
    "Respawn": "Reaparición",
    "Alive": "Vivo",
    "Dead": "Muerto",
    "Spawn Conditions": "Condiciones de aparición",
    "Weather Forecast": "Pronóstico del tiempo",
    "Show All": "Mostrar todos",
    "Hide Dead": "Ocultar muertos",
    "Copy Tracker Link": "Copiar enlace del rastreador",
    "Copy Flag": "Copiar posición (<flag>)",
    "Reset Timer": "Reiniciar temporizador",
    "Set Pop Time": "Fijar hora de aparición",
    "Minutes Ago": "Minutos atrás",
    "Hours Ago": "Horas atrás",
    "Apply": "Aplicar",
    "Cancel": "Cancelar",
    "Logograms & Actions Database": "Base de datos de logogramas y acciones",
    "Search Actions...": "Buscar acciones...",
    "Recipe": "Receta",
    "Role": "Rol",
    "Description": "Descripción",
    "Mnemonic": "Mnemónico",
    "Notorious Monsters Database": "Base de datos de Notorious Monsters (NMs)",
    "Zone": "Zona",
    "Anemos": "Anemos",
    "Pagos": "Pagos",
    "Pyros": "Pyros",
    "Hydatos": "Hydatos",
    "Aggro Radar Status:": "Estado del radar de aggro:",
    "Dragon Auto-Walk:": "Paso automático para dragones:",
    "Cast Alerts:": "Alertas de casteo:",
    "Live Nearby Threats (Radar Detection Range)": "Amenazas cercanas en vivo (alcance del radar)",
    "Eureka Monsters Reference Database": "Base de datos de referencia de monstruos de Eureka",
    "Enemy Actions & Tactical Counters Database": "Base de datos de acciones enemigas y contraataques",
    "Active": "Activo",
    "Disabled": "Desactivado",
    "Enabled": "Activado",
    "Player character not logged in or unavailable.": "El personaje del jugador no ha iniciado sesión o no está disponible.",
    "○ Expedition radar is on standby while outside Eureka.": "[o] El radar de expedición está en espera fuera de Eureka.",
    "Live threat detection and danger cones will automatically activate once you enter Anemos, Pagos, Pyros, or Hydatos.": "La detección de amenazas en vivo y los conos de peligro se activarán automáticamente en cuanto entres a Anemos, Pagos, Pyros o Hydatos.",
    "Threat / Name": "Amenaza / Nombre",
    "Lv": "Nv",
    "Danger": "Peligro",
    "Tactical Action": "Acción táctica",
    "Source Mob": "Monstruo de origen",
    "Counter Measure": "Contramedida",
    "Cast Time": "Tiempo de casteo",
    "Action Name": "Nombre de la acción",
    "All Zones": "Todas las zonas",
    "General Detection & Safety": "Detección general y seguridad",
    "Master Enable": "Activar radar principal",
    "Only Activate In Eureka": "Solo activar en Eureka",
    "Detection Range (Meters)": "Alcance de detección (metros)",
    "Safety Margin (Meters)": "Margen de seguridad (metros)",
    "Auto-Detect Player Elemental Level": "Autodetectar nivel elemental del jugador",
    "Manual Player Elemental Level": "Nivel elemental manual del jugador",
    "Filter Safe Monsters (Safe Level Difference)": "Filtrar monstruos seguros (diferencia de nivel)",
    "Visual Shapes & Drawing Elements": "Formas visuales y elementos de dibujo",
    "Draw Vision Cones": "Dibujar conos de visión",
    "Draw Sound Aggro Circles": "Dibujar círculos de aggro por oído",
    "Draw Blood Aggro Circles": "Dibujar círculos de aggro por sangre",
    "Draw Magic Aggro Warnings": "Dibujar avisos de aggro por magia",
    "Draw Proximity Circles": "Dibujar círculos de proximidad",
    "Draw Distance Guide Lines (< 12m)": "Dibujar líneas de guía de distancia (< 12 m)",
    "Draw Floating Monster Labels": "Dibujar etiquetas flotantes de monstruos",
    "Solid Shape Fills": "Relleno sólido de formas",
    "Vision Cone Arc Angle (Degrees)": "Ángulo de apertura del cono de visión (grados)",
    "Sleeping Dragons & Auto-Walk Automation": "Dragones dormidos y caminar automático",
    "Auto-Walk Near Sleeping Dragons (Prevents Waking / Aggro)": "Caminar automático cerca de dragones dormidos (evita despertar / aggro)",
    "Auto-Walk Activation Distance (Meters)": "Distancia de activación de caminar automático (metros)",
    "Restore Running Upon Leaving Danger Radius": "Reanudar correr al salir del radio de peligro",
    "Tactical Cast Alerts HUD": "HUD de alertas tácticas de casteo",
    "Display On-Screen Floating Enemy Action Alert": "Mostrar alerta flotante en pantalla de acción enemiga",
    "Lock Window Position": "Bloquear posición de la ventana",
    "Test / Preview Cast Alert HUD": "Probar / vista previa del HUD de alertas",
    "Reset Window Position": "Restablecer posición de la ventana",
    "Audio Sound Alerts": "Alertas de audio",
    "Play Beep Warning On Aggro Threat": "Reproducir aviso sonoro ante amenaza de aggro",
    "Color Palette & Customization": "Paleta de colores y personalización",
    "Dragon Sound Circle": "Círculo de oído de dragón",
    "Dragon Running Warning Text": "Texto de aviso al correr cerca de dragones",
    "Dragon Auto-Walk Safe Text": "Texto de caminar automático seguro",
    "Near Distance Line (< 10m)": "Línea de distancia cercana (< 10 m)",
    "Far Distance Line (>= 10m)": "Línea de distancia lejana (>= 10 m)",
    "Support McVaxius on Ko-fi": "Apoyar a McVaxius en Ko-fi",
    "Join Aethertek Discord": "Unirse al Discord de Aethertek",
    "Eureka Suite GitHub Source": "Código fuente de Eureka Suite en GitHub",
    "Community & External Links:": "Comunidad y enlaces externos:"
}

PLACEHOLDER_REGEX = re.compile(r'\{(\d+(?::[^}]+)?)\}')

def clean_ascii(text: str) -> str:
    """Ensure standard ASCII quotation marks, apostrophes, and dashes to prevent ImGui font glyph issues."""
    for ch in ['„', '”', '“', '«', '»']:
        text = text.replace(ch, '"')
    for ch in ['‘', '’', '`']:
        text = text.replace(ch, "'")
    for ch in ['—', '–']:
        text = text.replace(ch, '-')
    text = text.replace('…', '...')
    return text

def main():
    source_path = 'BFE/Localization/Strings_en.resx'
    target_path = 'BFE/Localization/Strings_es.resx'

    tree = ET.parse(source_path)
    root = tree.getroot()

    keys = []
    en_vals = {}
    for d in root.findall('data'):
        name = d.get('name')
        val_el = d.find('value')
        val = val_el.text if val_el is not None and val_el.text else name
        keys.append(name)
        en_vals[name] = val

    print(f"Loaded {len(keys)} keys from {source_path}")

    # Check for missing keys
    missing_keys = [k for k in keys if k not in TRANSLATIONS_ES]
    if missing_keys:
        print(f"ERROR: {len(missing_keys)} keys missing from Spanish dictionary:")
        for mk in missing_keys:
            print(f"  - {mk}")
        raise ValueError("Missing keys in Spanish dictionary!")

    # Check placeholder consistency
    placeholder_errors = 0
    for k in keys:
        en_txt = en_vals[k]
        es_txt = TRANSLATIONS_ES[k]
        en_holes = set(PLACEHOLDER_REGEX.findall(en_txt))
        es_holes = set(PLACEHOLDER_REGEX.findall(es_txt))
        if en_holes != es_holes:
            print(f"WARNING: Placeholder mismatch for '{k}': EN={en_holes} vs ES={es_holes}")
            placeholder_errors += 1

    if placeholder_errors > 0:
        raise ValueError(f"{placeholder_errors} placeholder mismatches found!")

    # Prepare target XML root
    target_tree = ET.parse(source_path)
    target_root = target_tree.getroot()

    for d in target_root.findall('data'):
        target_root.remove(d)

    for k in keys:
        trans = clean_ascii(TRANSLATIONS_ES[k])
        elem = ET.SubElement(target_root, 'data')
        elem.set('name', k)
        elem.set('xml:space', 'preserve')
        val_elem = ET.SubElement(elem, 'value')
        val_elem.text = trans

    ET.indent(target_tree, space="  ", level=0)
    target_tree.write(target_path, encoding='utf-8', xml_declaration=True)
    print(f"Successfully wrote {len(keys)} Castilian Spanish (es-ES) entries into {target_path}!")

if __name__ == '__main__':
    main()
