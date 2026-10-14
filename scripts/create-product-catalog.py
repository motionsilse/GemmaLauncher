"""Generate the shipping catalog from model registrations and English product copy."""
import argparse
import copy
import json
from pathlib import Path
import sys
from typing import Any


def build_catalog(source: dict[str, Any], translations: dict[str, str]) -> dict[str, Any]:
    models = []
    seen = set()
    if not source.get("models"):
        raise ValueError("A catalog needs at least one model")
    for entry in sorted(source["models"], key=lambda item: item["order"]):
        model = copy.deepcopy(entry)
        model.pop("order")
        if model["id"] in seen:
            raise ValueError("Duplicate model ID: " + model["id"])
        seen.add(model["id"])
        prefix = model["localizationPrefix"]
        if not prefix:
            raise ValueError("A recommended model needs a localizationPrefix")
        for field in ("category", "headline", "description", "hardwareGuide", "memoryGuide"):
            model[field] = translations[f"{prefix}.{field}"]
        model["benefits"] = [translations[f"{prefix}.benefit1"], translations[f"{prefix}.benefit2"]]
        models.append(model)
    return {"schemaVersion": 1, "runtime": copy.deepcopy(source["runtime"]), "models": models}


def update_catalog(source: dict[str, Any], translations: dict[str, str], output: Path, *, check: bool = False) -> bool:
    rendered = json.dumps(build_catalog(source, translations), ensure_ascii=False, indent=2) + "\n"
    if check:
        return output.is_file() and output.read_text(encoding="utf-8") == rendered
    output.parent.mkdir(parents=True, exist_ok=True)
    temporary = output.with_suffix(".json.tmp")
    temporary.write_text(rendered, encoding="utf-8")
    temporary.replace(output)
    return True


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    try:
        source = json.loads((root / "config/launcher-models.json").read_text(encoding="utf-8"))
        translations = json.loads((root / "src/GemmaLauncher.Core/Locales/en.models.json").read_text(encoding="utf-8"))
        if not update_catalog(source, translations, root / "src/GemmaLauncher.App/Assets/catalog.json", check=args.check):
            print("The shipping catalog differs from its registrations or translations.", file=sys.stderr)
            return 1
    except (OSError, ValueError, KeyError) as error:
        print(f"Cannot generate the catalog: {error}", file=sys.stderr)
        return 1
    print("The shipping catalog matches its registrations and translations.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
