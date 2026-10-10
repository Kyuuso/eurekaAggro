#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Retranslate Eureka Suite Spanish localization specifically using DeepL ES-ES (Spanish of Spain / Castellano)
combined with an exhaustive FFXIV & gaming terminology glossary.
"""

import argparse
import html
import json
import os
import re
import ssl
import sys
import time
import urllib.parse
import urllib.request
import xml.etree.ElementTree as ET

PLACEHOLDER_REGEX = re.compile(r'\{(\d+(?::[^}]+)?)\}')

# Exhaustive Castilian Spanish (Spain) MMO / FFXIV terms glossary
SPANISH_GLOSSARY = {
    # Navigation & Core Tabs
    "Aggro Lines": "Líneas de aggro",
    "Bunny Fate Engine": "Motor de FATEs de conejos",
    "Tracker": "Rastreador",
    "About & Credits": "Acerca de y créditos",
    "Main": "Principal",
    "Statistics": "Estadísticas",
    "Configuration": "Configuración",
    "Settings": "Ajustes",
    "Language": "Idioma",
    "Language:": "Idioma:",
    "Select plugin interface language": "Seleccionar idioma de la interfaz del plugin",
    "General Settings": "Ajustes generales",
    "Window appearance": "Apariencia de la ventana",
    "Dependencies": "Dependencias",
    "About": "Acerca de",
    "Area Selection": "Selección de zona",
    "AutoRetainer Settings": "Ajustes de AutoRetainer",
    "BFE Settings": "Ajustes de BFE",
    "BFE Debug": "Depuración de BFE",
    "Debug Stats": "Estadísticas de depuración",
    "Total Stats": "Estadísticas totales",
    "Stats": "Estadísticas",
    "Session": "Sesión",
    "Lifetime": "Total histórico",
    "WIP": "En desarrollo",

    # Status & Headers
    "Auto-synced": "Sincronización automática",
    "Manual": "Manual",
    "Night": "Noche",
    "Day": "Día",
    "Location: {0}": "Ubicación: {0}",
    "Elemental Lv. {0}": "Nv. elemental {0}",
    "State: {0}": "Estado: {0}",
    "Outside Eureka - Expedition features on standby": "Fuera de Eureka - Funciones de expedición en espera",
    "Aggro Radar Status:": "Estado del radar de aggro:",
    "Dragon Auto-Walk:": "Paso automático para dragones:",
    "Cast Alerts:": "Alertas de casteo:",
    "Live Nearby Threats (Radar Detection Range)": "Amenazas cercanas en vivo (alcance del radar)",
    "Eureka Monsters Reference Database": "Base de datos de monstruos de Eureka",
    "Enemy Actions & Tactical Counters Database": "Base de datos de acciones enemigas y contraataques",
    "Active": "Activo",
    "Disabled": "Desactivado",
    "Enabled": "Activado",
    "Idle": "En espera",
    "Running": "En ejecución",
    "Stop": "Detener",
    "Task": "Tarea",
    "Infinite": "Infinito",
    "Missing": "Faltante",
    "Not available": "No disponible",
    "Loaded": "Cargado",
    "Installed, not loaded": "Instalado, no cargado",
    "Ready": "Listo",
    "Trigger": "Invocación",
    "Name": "Nombre",
    "Level": "Nivel",
    "Aggro Type": "Tipo de aggro",
    "Range": "Alcance",
    "Distance": "Distancia",
    "Status": "Estado",
    "Threat / Name": "Amenaza / Nombre",
    "Lv": "Nv",
    "Danger": "Peligro",
    "Tactical Action": "Acción táctica",
    "Action Name": "Nombre de la acción",
    "Source Mob": "Monstruo de origen",
    "Counter Measure": "Contramedida",
    "Cast Time": "Tiempo de casteo",
    "All Zones": "Todas las zonas",
    "Minutes": "Minutos",
    "Hours": "Horas",
    "Time elapsed": "Tiempo transcurrido",
    "OG Author": "Autor original",

    # Brands & Platforms
    "Ko-fi": "Ko-fi",
    "Discord": "Discord",
    "Scroll down to \"The Dumpster Fire\" channel to discuss issues / suggestions for specific plugins.": "Desplázate hacia abajo hasta el canal \"The Dumpster Fire\" para comentar problemas o sugerencias sobre plugins concretos.",
    "Support McVaxius on Ko-fi": "Apoyar a McVaxius en Ko-fi",
    "Join Aethertek Discord": "Unirse al Discord de Aethertek",
    "Eureka Suite GitHub Source": "Código fuente de Eureka Suite en GitHub",
    "Community & External Links:": "Comunidad y enlaces externos:",

    # FFXIV & Eureka Mechanics
    "Sight": "Vista",
    "Sound": "Oído",
    "Blood": "Sangre",
    "Magic": "Magia",
    "Proximity": "Proximidad",
    "True Sight": "Visión verdadera",
    "Safe": "Seguro",
    "Aggro Risk": "Riesgo de aggro",
    "In Range": "En alcance",
    "Dismounting": "Bajando de la montura",
    "Mounting Up": "Subiendo a la montura",
    "Leaving Duty": "Saliendo de la misión",
    "Entering Pyros": "Entrando en Pyros",
    "Going to Eureka": "Yendo a Eureka",
    "Going to FC": "Yendo a la casa de la compañía libre",
    "Going to Personal": "Yendo a la casa personal",
    "Going to Personal House": "Yendo a la casa personal",
    "Going to Repair Mender": "Yendo al reparador",
    "Moving to {0}": "Desplazándose a {0}",
    "Moving to Kugane Npc": "Yendo hacia el PNJ de Kugane",
    "Using Aethernet": "Usando el Aethernet",
    "Using Retainer": "Gestionando retainer",
    "Resending Retainers": "Reenviando retainers",
    "Waiting Navmesh": "Esperando a Navmesh",
    "Waiting at Fate": "Esperando en la FATE",
    "In Bunny Fate": "En la FATE de conejos",
    "Start Bunnies": "Iniciar conejos",
    "Start Pyros": "Iniciar Pyros",
    "Select to start bunnies": "Selecciona para iniciar la ruta de conejos",
    "Grabbing Coffer": "Abriendo cofre",
    "Gil Earned": "Guiles obtenidos",
    "Gold Coffers": "Cofres de oro",
    "Silver Coffers": "Cofres de plata",
    "Bronze Coffers": "Cofres de bronce",
    "Eldthurs Mount": "Montura Eldthurs",
    "Pyros Hairstyles": "Peinado de Pyros",
    "Copycat Bulb": "Bulbo imitador",
    "Petrel Mount": "Montura Petrel",
    "Bunnies": "Conejos",
    "Teleport To Personal": "Teletransporte a la casa personal",
    "Teleport to Free Company": "Teletransporte a la compañía libre",
    "Go To Personal/FC House After Completion": "Ir a la casa personal o de la compañía libre al finalizar",
    "Will logout after looping is complete.": "Cerrará sesión cuando finalice el bucle.",
    "Will teleport to Personal or FC house after looping is complete.": "Se teletransportará a la casa personal o de la compañía libre cuando finalice el bucle.",
    "Enable Logout After Completion": "Cerrar sesión al finalizar",
    "Enable Multi Support": "Activar soporte para múltiples personajes",
    "Enable Repair": "Activar reparaciones",
    "Enable Retainer Support": "Activar soporte para retainers",
    "Enable Submarine Support": "Activar soporte para submarinos",
    "Enables retainer support for this character.": "Activa el soporte para retainers en este personaje.",
    "If crafters are leveled, uses dark matter for self repair.": "Si las clases de artesanía tienen nivel suficiente, usa materia oscura para autorrepararse.",
    "Self Repair": "Autorreparación",
    "Self Repairing": "Autorreparando",
    "Repair Value": "Valor de durabilidad para reparar",
    "Threshold to repair gear": "Umbral de durabilidad para reparar equipo",
    "Run for X time": "Ejecutar durante un tiempo determinado",
    "Anemos": "Anemos",
    "Pagos": "Pagos",
    "Pyros": "Pyros",
    "Hydatos": "Hydatos",
    "Anemos Stats": "Estadísticas de Anemos",
    "Pagos Stats": "Estadísticas de Pagos",
    "Pyros Stats": "Estadísticas de Pyros",
    "Hydatos Stats": "Estadísticas de Hydatos",
    "Normal Raid": "Incursión normal",

    # HUD & Alerts
    "STUN REQUIRED!": "¡SE REQUIERE ATURDIMIENTO!",
    "BREAK LINE OF SIGHT (HIDE)!": "¡ROMPER LÍNEA DE VISIÓN (ESCONDERSE)!",
    "INTERRUPT / SILENCE AVAILABLE!": "¡INTERRUPCIÓN / SILENCIO DISPONIBLE!",
    "[!] ENEMY ACTION ALERT": "[!] ALERTA DE ACCIÓN ENEMIGA",
    "[!] ENEMY ACTION ALERT (Drag to move)": "[!] ALERTA DE ACCIÓN ENEMIGA (Arrastrar para mover)",
    "[OK] AUTO-WALK ENGAGED (SAFE)": "[OK] CAMINAR AUTOMÁTICO ACTIVADO (SEGURO)",
    "[WARN] RUNNING NEAR DRAGON! WALK NOW (KEYPAD /)": "[AVISO] ¡CORRIENDO CERCA DE DRAGÓN! CAMINA AHORA (TECLADO /)",
    "[SAFE] SLEEPING DRAGON": "[SEGURO] DRAGÓN DORMIDO",
    "[WARN] SOUND AGGRO! WALK TO AVOID (KEYPAD /)": "[AVISO] ¡AGGRO POR OÍDO! CAMINA PARA EVITAR (TECLADO /)",
    "[ALERT] HP < 80%: BLOOD AGGRO ACTIVE (HEAL TO SAFE)": "[ALERTA] PS < 80%: AGGRO POR SANGRE ACTIVO (CÚRATE PARA ESTAR A SALVO)",
    "[ALERT] CASTING DETECTED! SPRITE WILL AGGRO!": "[ALERTA] ¡CASTEO DETECTADO! ¡EL ESPÍRITU HARÁ AGGRO!",
    "[MAGIC] Sprite: DO NOT CAST SPELLS": "[MAGIA] Espíritu: NO CASTEAR HECHIZOS",
    "[Sound - Walk!]": "[Oído - ¡Camina!]",
    "[Blood / Undead]": "[Sangre / No muerto]",
    "[Magic / Sprite]": "[Magia / Espíritu]",
    "[Proximity]": "[Proximidad]",
    "[Sight]": "[Vista]",
    "{0} is casting: ": "{0} está casteando: ",

    # Tracker
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
    "Auto-connect to Eureka Tracker when entering an expedition": "Autoconectar al rastreador de Eureka al entrar a una expedición",
    "Sync Instance ID": "Sincronizar ID de instancia",
    "Instance ID:": "ID de instancia:",
    "Auto-detected": "Detectado automáticamente",
    "Manual Entry": "Entrada manual",
    "Not in Eureka": "No estás en Eureka",
    "Set": "Establecer",
    "Refresh": "Actualizar",
    "Not Connected to a Tracker": "No conectado a ningún rastreador",
    "Enter a 6-character tracker code above or click (+) to create a new one.": "Introduce un código de rastreador de 6 caracteres arriba o pulsa (+) para crear uno nuevo.",
    "Pop Time": "Hora de aparición",
    "Respawn": "Reaparición",
    "Alive": "Vivo",
    "Dead": "Muerto",
    "Spawn Conditions": "Condiciones de aparición",
    "Weather Forecast": "Pronóstico del tiempo",
    "Show All": "Mostrar todos",
    "Hide Dead": "Ocultar muertos",
    "Copy Tracker Link": "Copiar enlace del rastreador",
    "Copy Flag": "Copiar bandera",
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
    "Notorious Monsters Database": "Base de datos de monstruos notorios",
    "Zone": "Zona",

    # Aggro Radar Configuration
    "General Detection & Safety": "Detección general y seguridad",
    "Master Enable": "Activar radar principal",
    "Only Activate In Eureka": "Solo activar dentro de Eureka",
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
    "Fill Opacity": "Opacidad del relleno",
    "Vision Cone Arc Angle (Degrees)": "Ángulo de apertura del cono de visión (grados)",
    "Sleeping Dragons & Auto-Walk Automation": "Dragones dormidos y caminar automático",
    "Auto-Walk Near Sleeping Dragons (Prevents Waking / Aggro)": "Caminar automático cerca de dragones dormidos (evita despertar / aggro)",
    "Auto-Walk Activation Distance (Meters)": "Distancia de activación del caminar automático (metros)",
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
    "Dragon Running Warning Text": "Texto de aviso al correr cerca de dragón",
    "Dragon Auto-Walk Safe Text": "Texto de caminar automático seguro",
    "Near Distance Line (< 10m)": "Línea de distancia cercana (< 10 m)",
    "Far Distance Line (>= 10m)": "Línea de distancia lejana (>= 10 m)",
    "Reset Colors to Default": "Restablecer colores por defecto",
    "Color Vision (Sight)": "Color de visión (vista)",
    "Color Sound (Dragons / Hearing)": "Color de sonido (dragones / oído)",
    "Color Blood (Undead / Ashkin)": "Color de sangre (no muertos / ashkin)",
    "Color Magic (Sprites / Elementals)": "Color de magia (espíritus / elementales)",
    "Always Show Sleeping Dragons": "Mostrar siempre dragones dormidos",
    "Always Show Undead (Blood)": "Mostrar siempre no muertos (sangre)",
    "Always Show Sprites (Magic)": "Mostrar siempre espíritus (magia)",
    "Open Cast Alert Preview": "Abrir vista previa de alerta de casteo",
    "Lock Cast Alert Position": "Bloquear posición de alerta de casteo",
    "Reset Cast Alert Position": "Restablecer posición de alerta de casteo",
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
    "Show Mob Labels": "Mostrar etiquetas de monstruos",
    "Fill Shapes (Solid)": "Rellenar formas (sólido)",
    "Aggro Cone Arc (Degrees)": "Arco del cono de aggro (grados)",
    "Aggro Types": "Tipos de aggro",
    "Color Configuration": "Configuración de colores",
    "Restore Running After Leaving Dragons": "Reanudar correr tras alejarse de dragones",
    "Enable Sound / Beep Alerts": "Activar alertas sonoras / pitidos",
    "Audio Alert Volume": "Volumen de alertas de audio",
    "Auto-Walk Trigger Distance (m)": "Distancia de activación del caminar automático (m)",
}


def protect_placeholders(text: str) -> str:
    escaped = html.escape(text)
    return PLACEHOLDER_REGEX.sub(r'<keep>{\1}</keep>', escaped)


def restore_placeholders(text: str) -> str:
    unwrapped = text.replace('<keep>', '').replace('</keep>', '')
    return html.unescape(unwrapped)


def clean_text(text: str) -> str:
    for ch in ['„', '”', '“', '«', '»']:
        text = text.replace(ch, '"')
    for ch in ['‘', '’', '`']:
        text = text.replace(ch, "'")
    for ch in ['—', '–']:
        text = text.replace(ch, '-')
    text = text.replace('…', '...')
    return text


def call_deepl(api_key: str, texts: list[str], target_lang: str = 'ES-ES') -> list[str]:
    if not texts:
        return []
    is_free = api_key.endswith(':fx')
    url = 'https://api-free.deepl.com/v2/translate' if is_free else 'https://api.deepl.com/v2/translate'

    protected = [protect_placeholders(t) for t in texts]
    payload = [('target_lang', target_lang), ('tag_handling', 'xml'), ('ignore_tags', 'keep')]
    for t in protected:
        payload.append(('text', t))

    encoded_data = urllib.parse.urlencode(payload).encode('utf-8')
    headers = {
        'Authorization': f'DeepL-Auth-Key {api_key}',
        'Content-Type': 'application/x-www-form-urlencoded',
        'User-Agent': 'EurekaSuite-Localizer/1.0',
    }

    ctx = ssl._create_unverified_context()
    req = urllib.request.Request(url, data=encoded_data, headers=headers)

    with urllib.request.urlopen(req, context=ctx, timeout=30) as resp:
        data = json.loads(resp.read().decode('utf-8'))
        raw = [item['text'] for item in data.get('translations', [])]
        return [clean_text(restore_placeholders(t)) for t in raw]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--api-key', required=True)
    parser.add_argument('--source', default='BFE/Localization/Strings_en.resx')
    parser.add_argument('--target', default='BFE/Localization/Strings_es.resx')
    parser.add_argument('--batch-size', type=int, default=35)
    args = parser.parse_args()

    tree = ET.parse(args.source)
    root = tree.getroot()

    keys = []
    en_texts = []
    for data in root.findall('data'):
        name = data.get('name')
        val_el = data.find('value')
        val = val_el.text if val_el is not None and val_el.text else name
        keys.append(name)
        en_texts.append(val)

    print(f"Translating {len(keys)} entries to ES-ES (Spanish - Spain)...")

    to_translate_indices = []
    to_translate_texts = []
    translations = {}

    for idx, (k, txt) in enumerate(zip(keys, en_texts)):
        if k in SPANISH_GLOSSARY:
            translations[k] = SPANISH_GLOSSARY[k]
        elif txt in SPANISH_GLOSSARY:
            translations[k] = SPANISH_GLOSSARY[txt]
        else:
            to_translate_indices.append(idx)
            to_translate_texts.append(txt)

    print(f"Glossary matches: {len(translations)} entries. Querying DeepL for remaining {len(to_translate_texts)} entries...")

    translated_results = []
    for i in range(0, len(to_translate_texts), args.batch_size):
        batch = to_translate_texts[i:i + args.batch_size]
        res = call_deepl(args.api_key, batch, 'ES-ES')
        translated_results.extend(res)
        print(f"  -> Batch {i // args.batch_size + 1}/{(len(to_translate_texts) + args.batch_size - 1) // args.batch_size} done ({len(res)} items)")
        time.sleep(0.2)

    for idx, trans_val in zip(to_translate_indices, translated_results):
        k = keys[idx]
        translations[k] = clean_text(trans_val)

    target_tree = ET.parse(args.source)
    target_root = target_tree.getroot()

    for d in target_root.findall('data'):
        target_root.remove(d)

    for k in keys:
        trans = translations.get(k, k)
        elem = ET.SubElement(target_root, 'data')
        elem.set('name', k)
        elem.set('xml:space', 'preserve')
        val_elem = ET.SubElement(elem, 'value')
        val_elem.text = trans

    ET.indent(target_tree, space="  ", level=0)
    target_tree.write(args.target, encoding='utf-8', xml_declaration=True)
    print(f"Successfully retranslated {len(keys)} entries into {args.target} using Castilian Spanish (ES-ES)!")


if __name__ == '__main__':
    main()
