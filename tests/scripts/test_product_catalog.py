"""Regression checks for the release catalog generator."""

import copy
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location(
    "product_catalog", ROOT / "scripts/create-product-catalog.py"
)
assert SPEC is not None and SPEC.loader is not None
GENERATOR = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(GENERATOR)


class ProductCatalogTests(unittest.TestCase):
    def setUp(self) -> None:
        self.source = json.loads(
            (ROOT / "config/launcher-models.json").read_text(
                encoding="utf-8"
            )
        )
        self.translations = json.loads(
            (ROOT / "src/GemmaLauncher.Core/Locales/en.models.json").read_text(
                encoding="utf-8"
            )
        )

    def test_generated_catalog_matches_shipping_catalog(self) -> None:
        expected = json.loads(
            (ROOT / "src/GemmaLauncher.App/Assets/catalog.json").read_text(
                encoding="utf-8"
            )
        )
        self.assertEqual(
            expected, GENERATOR.build_catalog(self.source, self.translations)
        )

    def test_source_order_does_not_change_model_copy_or_output_order(self) -> None:
        expected = GENERATOR.build_catalog(self.source, self.translations)
        self.source["models"].reverse()
        self.assertEqual(
            expected, GENERATOR.build_catalog(self.source, self.translations)
        )

    def test_invalid_model_ids_are_rejected_before_writing(self) -> None:
        empty = copy.deepcopy(self.source)
        empty["models"] = []
        untranslated = copy.deepcopy(self.source)
        untranslated["models"][0]["localizationPrefix"] = ""
        duplicate = copy.deepcopy(self.source)
        duplicate["models"].append(copy.deepcopy(duplicate["models"][0]))
        for source, message in (
            (empty, "at least one model"),
            (untranslated, "localizationPrefix"),
            (duplicate, "Duplicate model ID"),
        ):
            with self.subTest(message=message), tempfile.TemporaryDirectory() as folder:
                output = Path(folder) / "catalog.json"
                output.write_text("existing catalog", encoding="utf-8")
                with self.assertRaisesRegex(ValueError, message):
                    GENERATOR.update_catalog(source, self.translations, output)
                self.assertEqual("existing catalog", output.read_text(encoding="utf-8"))

    def test_new_registered_models_do_not_need_generator_changes(self) -> None:
        additional = copy.deepcopy(self.source["models"][0])
        additional.update(id="additional-model", order=100)
        self.source["models"].append(additional)
        result = GENERATOR.build_catalog(self.source, self.translations)
        self.assertEqual("additional-model", result["models"][-1]["id"])

    def test_check_reports_drift_without_writing(self) -> None:
        with tempfile.TemporaryDirectory() as folder:
            output = Path(folder) / "catalog.json"
            self.assertFalse(
                GENERATOR.update_catalog(
                    self.source, self.translations, output, check=True
                )
            )
            self.assertFalse(output.exists())
            GENERATOR.update_catalog(self.source, self.translations, output)
            self.assertTrue(
                GENERATOR.update_catalog(
                    self.source, self.translations, output, check=True
                )
            )
            output.write_text("outdated catalog", encoding="utf-8")
            self.assertFalse(
                GENERATOR.update_catalog(
                    self.source, self.translations, output, check=True
                )
            )
            self.assertEqual("outdated catalog", output.read_text(encoding="utf-8"))


if __name__ == "__main__":
    unittest.main()
