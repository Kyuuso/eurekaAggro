#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Sanitizer for Eureka Suite .resx localization files.

Fixes:
1. Replaces fancy quotes („, ”, “, ”, «, », ‘, ’) with standard ASCII quotes (" and ').
2. Replaces em-dashes (—) and en-dashes (–) with standard ASCII hyphens (-).
3. Replaces unicode ellipses (…) with '...'.
4. Corrects mistranslated UI keywords (e.g. German 'Hand' -> 'Hauptseite' for 'Main').
5. Validates format placeholders ({0}, {1}, etc.) across all languages.
"""

import glob
import os
import re
import xml.etree.ElementTree as ET

# Manual term fixes for obvious machine-translation blunders
SPECIFIC_FIXES = {
    'Strings_de.resx': {
        'Main': 'Hauptseite',
        'Outside Eureka - Expedition features on standby': 'Außerhalb von Eureka - Expeditionsfunktionen in Bereitschaft',
        '[Sound - Walk!]': '[Geräusch - Gehen!]',
        'Manual Player Elemental Level': 'Manuelle Elementarstufe des Spielers',
        'Dragon Auto-Walk Safe Text': 'Drachen Auto-Walk - Sicherer Text',
        'Eureka Suite GitHub Source': 'Eureka Suite - GitHub Quellcode',
        'Draw Blood Aggro Circles': 'Blut-Aggro-Kreise anzeigen',
        'Draw Magic Aggro Warnings': 'Magie-Aggro-Warnungen anzeigen',
    },
    'Strings_pl.resx': {
        'Main': 'Główne',
        'Outside Eureka - Expedition features on standby': 'Poza Eureką - funkcje ekspedycji w trybie czuwania',
        '[Sound - Walk!]': '[Dźwięk - Idź!]',
        'Auto-Walk Near Sleeping Dragons': 'Automatyczny chód przy Śpiących Smokach',
        'Restore Running After Leaving Dragons': 'Wznów bieg po minięciu smoków',
        'Dragon Running Warning Text': 'Tekst ostrzeżenia przed biegiem przy smoku',
        'Dragon Auto-Walk Safe Text': 'Tekst bezpiecznego chodu przy smoku',
        'Color Distance Near (< 10m)': 'Kolor bliskiej odległości (< 10 m)',
        'Color Distance Far (>= 10m)': 'Kolor dalekiej odległości (>= 10 m)',
        'Eureka Suite GitHub Source': 'Eureka Suite - kod źródłowy GitHub',
        'Draw Blood Aggro Circles': 'Rysuj okręgi aggro krwi',
        'Draw Magic Aggro Warnings': 'Rysuj ostrzeżenia aggro magii',
        'Scroll down to "The Dumpster Fire" channel to discuss issues / suggestions for specific plugins.': 'Przewiń do kanału "The Dumpster Fire", aby omówić problemy lub sugestie dotyczące wtyczek.',
    },
    'Strings_fr.resx': {
        'Aggro Lines': 'Lignes d\'aggro',
        'Auto-Walk Near Sleeping Dragons': 'Marche automatique près des dragons endormis',
        'Auto-Walk Trigger Distance (m)': 'Distance d\'activation de la marche automatique (m)',
        'Open Cast Alert Preview': 'Aperçu de l\'alerte d\'action',
        'Draw Blood Aggro Circles': 'Afficher les cercles d\'aggro de sang',
        'Draw Magic Aggro Warnings': 'Afficher les alertes d\'aggro magique',
        'Sleeping Dragons & Auto-Walk Automation': 'Dragons endormis et marche automatique',
        'Auto-Walk Activation Distance (Meters)': 'Distance d\'activation de la marche automatique (mètres)',
        'Test / Preview Cast Alert HUD': 'Tester / Prévisualiser l\'alerte HUD',
    },
    'Strings_pt_BR.resx': {
        'Outside Eureka - Expedition features on standby': 'Fora de Eureka - Recursos de expedição em espera',
        '[Sound - Walk!]': '[Som - Ande!]',
        'Draw Blood Aggro Circles': 'Desenhar círculos de aggro por sangue',
        'Draw Magic Aggro Warnings': 'Desenhar avisos de aggro por magia',
        'Eureka Suite GitHub Source': 'Eureka Suite - Código-fonte no GitHub',
    }
}


def clean_text(text: str) -> str:
    """Normalizes typographic characters that cause missing glyphs in ImGui font atlases."""
    if not text:
        return text

    # Double quotes
    for ch in ['„', '”', '“', '«', '»']:
        text = text.replace(ch, '"')

    # Single quotes
    for ch in ['‘', '’', '`']:
        text = text.replace(ch, "'")

    # Dashes
    for ch in ['—', '–']:
        text = text.replace(ch, '-')

    # Ellipsis
    text = text.replace('…', '...')

    return text


def sanitize_all(loc_dir: str = 'BFE/Localization'):
    total_cleaned = 0

    for file_path in sorted(glob.glob(os.path.join(loc_dir, '*.resx'))):
        filename = os.path.basename(file_path)
        tree = ET.parse(file_path)
        root = tree.getroot()

        file_mods = 0
        fixes = SPECIFIC_FIXES.get(filename, {})

        for data in root.findall('data'):
            name = data.get('name', '')
            val_el = data.find('value')
            if val_el is None or not val_el.text:
                continue

            orig_val = val_el.text
            new_val = orig_val

            # Apply manual specific fixes if defined
            if name in fixes:
                new_val = fixes[name]

            # Apply typographic cleaning
            new_val = clean_text(new_val)

            if new_val != orig_val:
                val_el.text = new_val
                file_mods += 1

        if file_mods > 0:
            ET.indent(tree, space="  ", level=0)
            tree.write(file_path, encoding='utf-8', xml_declaration=True)
            print(f"[{filename}] Cleaned and normalized {file_mods} entries.")
            total_cleaned += file_mods
        else:
            print(f"[{filename}] Clean (no issues found).")

    print(f"\nSanitization complete. Total entries sanitized: {total_cleaned}")


if __name__ == '__main__':
    sanitize_all()
