#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
DeepL Automated Resx Localization Script for Eureka Suite.

Synchronizes all language `.resx` resource files against `Strings_en.resx`
using the DeepL API, preserving format tokens ({0}, {1}, etc.) and keeping
existing translations untouched.
"""

import argparse
import json
import os
import re
import ssl
import sys
import time
import urllib.parse
import urllib.request
import xml.etree.ElementTree as ET

LANG_MAP = {
    'Strings_de.resx': 'DE',
    'Strings_es.resx': 'ES',
    'Strings_fr.resx': 'FR',
    'Strings_hi.resx': 'HI',
    'Strings_id.resx': 'ID',
    'Strings_it.resx': 'IT',
    'Strings_ja.resx': 'JA',
    'Strings_ko.resx': 'KO',
    'Strings_pl.resx': 'PL',
    'Strings_pt_BR.resx': 'PT-BR',
    'Strings_ru.resx': 'RU',
    'Strings_tr.resx': 'TR',
    'Strings_vi.resx': 'VI',
    'Strings_zh_Hans.resx': 'ZH-HANS',
}

import html

PLACEHOLDER_REGEX = re.compile(r'\{(\d+(?::[^}]+)?)\}')


def protect_placeholders(text: str) -> str:
    """Escapes XML entities and wraps format placeholders like {0} in XML tags so DeepL ignores them."""
    escaped = html.escape(text)
    return PLACEHOLDER_REGEX.sub(r'<keep>{\1}</keep>', escaped)


def restore_placeholders(text: str) -> str:
    """Strips temporary protection XML tags and unescapes XML entities."""
    unwrapped = text.replace('<keep>', '').replace('</keep>', '')
    return html.unescape(unwrapped)


def call_deepl(api_key: str, texts: list[str], target_lang: str) -> list[str]:
    """Translates a batch of texts using the DeepL API."""
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

    max_retries = 3
    for attempt in range(max_retries):
        try:
            with urllib.request.urlopen(req, context=ctx, timeout=30) as resp:
                data = json.loads(resp.read().decode('utf-8'))
                raw_translations = [item['text'] for item in data.get('translations', [])]
                return [restore_placeholders(t) for t in raw_translations]
        except urllib.error.HTTPError as e:
            if e.code in (429, 503) and attempt < max_retries - 1:
                time.sleep(2 * (attempt + 1))
                continue
            error_body = e.read().decode('utf-8', errors='ignore')
            raise RuntimeError(f"DeepL API HTTP {e.code}: {error_body}")
        except Exception as e:
            if attempt < max_retries - 1:
                time.sleep(2)
                continue
            raise RuntimeError(f"DeepL request failed: {e}")

    return []


def load_resx(file_path: str) -> tuple[ET.ElementTree, dict[str, str]]:
    """Loads a .resx file, returning its XML tree and a dict of existing key-value pairs."""
    tree = ET.parse(file_path)
    root = tree.getroot()
    entries = {}
    for data in root.findall('data'):
        name = data.get('name')
        val_elem = data.find('value')
        val = val_elem.text if val_elem is not None and val_elem.text is not None else ''
        if name:
            entries[name] = val
    return tree, entries


def save_resx(tree: ET.ElementTree, file_path: str):
    """Saves a .resx file with standard UTF-8 XML formatting and 2-space indentation."""
    ET.indent(tree, space="  ", level=0)
    tree.write(file_path, encoding='utf-8', xml_declaration=True)


def main():
    parser = argparse.ArgumentParser(description="DeepL Automated Resx Translator")
    parser.add_argument('--api-key', default=os.environ.get('DEEPL_API_KEY'), help="DeepL API Key (or set DEEPL_API_KEY env var)")
    parser.add_argument('--loc-dir', default='BFE/Localization', help="Directory containing .resx files")
    parser.add_argument('--source', default='Strings_en.resx', help="Source English resx filename")
    parser.add_argument('--batch-size', type=int, default=40, help="Batch size for DeepL requests")
    args = parser.parse_args()

    if not args.api_key:
        print("ERROR: Missing DeepL API Key. Provide via --api-key or DEEPL_API_KEY environment variable.", file=sys.stderr)
        sys.exit(1)

    source_path = os.path.join(args.loc_dir, args.source)
    if not os.path.exists(source_path):
        print(f"ERROR: Source file not found: {source_path}", file=sys.stderr)
        sys.exit(1)

    _, en_entries = load_resx(source_path)
    print(f"Loaded source English dictionary: {len(en_entries)} strings from {source_path}")

    total_translated = 0

    for filename, target_code in LANG_MAP.items():
        target_path = os.path.join(args.loc_dir, filename)
        if not os.path.exists(target_path):
            print(f"Skipping {filename}: file does not exist")
            continue

        tree, target_entries = load_resx(target_path)
        root = tree.getroot()

        # Identify missing or empty keys
        missing_keys = []
        missing_texts = []
        for key, en_text in en_entries.items():
            if key not in target_entries or not target_entries[key].strip():
                missing_keys.append(key)
                missing_texts.append(en_text if en_text.strip() else key)

        if not missing_keys:
            print(f"[{target_code}] {filename}: Up to date ({len(target_entries)} keys).")
            continue

        print(f"[{target_code}] {filename}: Translating {len(missing_keys)} missing keys...")

        # Process in batches
        translated_values = []
        for i in range(0, len(missing_texts), args.batch_size):
            batch_texts = missing_texts[i:i + args.batch_size]
            results = call_deepl(args.api_key, batch_texts, target_code)
            translated_values.extend(results)
            print(f"  -> Batch {i // args.batch_size + 1}/{(len(missing_texts) + args.batch_size - 1) // args.batch_size} done ({len(results)} items)")
            time.sleep(0.2)  # courteous rate-pacing

        # Append to target XML tree
        for key, trans_val in zip(missing_keys, translated_values):
            data_elem = ET.SubElement(root, 'data')
            data_elem.set('name', key)
            data_elem.set('xml:space', 'preserve')
            val_elem = ET.SubElement(data_elem, 'value')
            val_elem.text = trans_val

        save_resx(tree, target_path)
        total_translated += len(missing_keys)
        print(f"[{target_code}] {filename}: Saved with {len(missing_keys)} new translations.")

    print(f"\nCompleted! Total translated strings across all languages: {total_translated}")


if __name__ == '__main__':
    main()
