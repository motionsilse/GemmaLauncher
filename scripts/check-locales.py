"""Check shipping translations for coverage, duplicate keys and .NET placeholders."""
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
LOCALES = ROOT / "src/GemmaLauncher.Core/Locales"
LANGUAGES = "ko en ja zh-cn zh-tw es pt fr de fil vi ru pl id ms tr th".split()
PLACEHOLDERS = re.compile(r"(?<!\{)\{(\d+)(?:[^{}]*)\}(?!\})")


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"Duplicate key: {key}")
        result[key] = value
    return result


def read(path):
    data = json.loads(path.read_text(encoding="utf-8-sig"), object_pairs_hook=unique_object)
    if not isinstance(data, dict):
        raise ValueError(f"{path.name}: expected a string dictionary")
    for key, value in data.items():
        if not isinstance(value, str) or not value.strip():
            raise ValueError(f"{path.name}: empty or non-string value for {key}")
    return data


def main():
    domains = ["engine", "ui", "models"]
    total_keys = 0
    all_keys = set()
    for domain in domains:
        source = read(LOCALES / f"en.{domain}.json")
        overlap = all_keys & source.keys()
        if overlap:
            raise ValueError(f"Keys appear in more than one domain: {sorted(overlap)}")
        all_keys.update(source)
        total_keys += len(source)
        for language in LANGUAGES:
            path = LOCALES / f"{language}.{domain}.json"
            translated = read(path)
            missing = source.keys() - translated.keys()
            extra = translated.keys() - source.keys()
            if missing or extra:
                raise ValueError(f"{path.name}: missing={sorted(missing)}, extra={sorted(extra)}")
            for key, value in source.items():
                if set(PLACEHOLDERS.findall(value)) != set(PLACEHOLDERS.findall(translated[key])):
                    raise ValueError(f"{path.name}: mismatched placeholders for {key}")
    print(f"Validated {len(LANGUAGES)} languages, {len(domains)} domains, {total_keys} keys per language.")


if __name__ == "__main__":
    main()
