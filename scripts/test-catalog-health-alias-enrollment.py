"""Synthetic XML controls only; no native test execution or migration acceptance."""
import copy
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
import uuid
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parent.parent
SPEC = importlib.util.spec_from_file_location("catalog_health_native", ROOT / "scripts/verify-country-health-diagnostics.py")
NATIVE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(NATIVE)
MANIFEST = json.loads((ROOT / "scripts/country-health-diagnostic-expected.json").read_text())
NS = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"


def tag(name):
    return "{" + NS + "}" + name


def fixture(root, scope):
    identities = []
    if scope != "aliases":
        for identity, count in MANIFEST["methods"].items():
            identities.extend([identity] * count)
    if scope != "diagnostics":
        for identity, count in MANIFEST["healthAliasMethods"].items():
            identities.extend([identity] * count)
    if scope == "full":
        # These filler names are synthetic counter controls, never a claim about the historical full roster.
        identities.extend("Synthetic.Baseline.Case" + str(index)
                          for index in range(MANIFEST["fullForecast"] - len(identities)))
    document = ET.Element(tag("TestRun"))
    definitions = ET.SubElement(document, tag("TestDefinitions"))
    results = ET.SubElement(document, tag("Results"))
    for index, identity in enumerate(identities):
        class_name, method = identity.rsplit(".", 1)
        test_id, execution_id = str(uuid.uuid4()), str(uuid.uuid4())
        display = identity + "(" + str(index) + ")"
        unit = ET.SubElement(definitions, tag("UnitTest"), id=test_id, name=display)
        ET.SubElement(unit, tag("Execution"), id=execution_id)
        ET.SubElement(unit, tag("TestMethod"), className=class_name, name=method)
        ET.SubElement(results, tag("UnitTestResult"), testId=test_id, executionId=execution_id,
                      testName=display, outcome="Passed")
    summary = ET.SubElement(document, tag("ResultSummary"), outcome="Completed")
    counters = {key: str(len(identities)) for key in ("total", "executed", "passed")}
    counters.update({key: "0" for key in ("failed", "error", "timeout", "aborted", "inconclusive",
        "passedButRunAborted", "notRunnable", "notExecuted", "disconnected", "warning", "completed", "inProgress", "pending")})
    ET.SubElement(summary, tag("Counters"), **counters)
    coverage = ET.Element("coverage")
    packages = ET.SubElement(coverage, "packages")
    for assembly in ("Api", "Application", "Data", "Domain"):
        package = ET.SubElement(packages, "package", name="Legacy.Maliev.CatalogService." + assembly)
        ET.SubElement(package, "line", number="1", hits="1")
    for name in ("raw-a", "raw-b"):
        folder = root / name
        folder.mkdir()
        ET.ElementTree(coverage).write(folder / "coverage.cobertura.xml", encoding="utf-8")
    return document


class HealthAliasEnrollmentControls(unittest.TestCase):
    def verify(self, root, document, scope, manifest=MANIFEST):
        ET.ElementTree(document).write(root / "synthetic.trx", encoding="utf-8")
        return NATIVE.verify_native(root, scope == "full", manifest, aliases=scope == "aliases")

    def test_exact_alias_diagnostic_and_full_scopes_pass(self):
        for scope, count in (("aliases", 5), ("diagnostics", 20), ("full", 4756)):
            with self.subTest(scope=scope), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                proof = self.verify(root, fixture(root, scope), scope)
                self.assertEqual(count, proof["actualPassed"])
                self.assertEqual(0 if scope == "aliases" else 10, proof["countryHealthDiagnosticActualPassed"])
                self.assertEqual(5 if scope == "aliases" else 20 if scope == "diagnostics" else 25,
                                 sum(proof["methods"].values()))

    def test_alias_gate_rejects_missing_failed_skipped_error_and_identity_drift(self):
        for mutation in ("missing", "failed", "skipped", "passing-error", "duplicate", "wrong-class",
                         "wrong-method", "wrong-name", "wrong-counter", "manifest-method"):
            with self.subTest(mutation=mutation), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                document = fixture(root, "aliases")
                manifest = copy.deepcopy(MANIFEST)
                results = document.find(tag("Results"))
                definitions = document.find(tag("TestDefinitions"))
                if mutation == "missing":
                    results.remove(results[-1])
                elif mutation in ("failed", "skipped"):
                    results[-1].set("outcome", "Failed" if mutation == "failed" else "NotExecuted")
                elif mutation == "passing-error":
                    ET.SubElement(ET.SubElement(results[-1], tag("Output")), tag("ErrorInfo"))
                elif mutation == "duplicate":
                    results[-1].set("executionId", results[0].get("executionId"))
                elif mutation in ("wrong-class", "wrong-method"):
                    definitions[-1].find(tag("TestMethod")).set(
                        "className" if mutation == "wrong-class" else "name", "Unreviewed")
                elif mutation == "wrong-name":
                    results[-1].set("testName", "Unreviewed")
                elif mutation == "wrong-counter":
                    document.find(tag("ResultSummary")).find(tag("Counters")).set("warning", "1")
                else:
                    key = next(iter(manifest["healthAliasMethods"]))
                    manifest["healthAliasMethods"][key + "Unreviewed"] = manifest["healthAliasMethods"].pop(key)
                with self.assertRaises(SystemExit):
                    self.verify(root, document, "aliases", manifest)

    def test_full_gate_keeps_both_original_diagnostics_and_new_alias_methods(self):
        for mutation in ("original-method", "alias-method", "missing-alias-execution",
                         "missing-alias-roster", "lowered-full-count"):
            with self.subTest(mutation=mutation), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                document = fixture(root, "full")
                manifest = copy.deepcopy(MANIFEST)
                definitions = document.find(tag("TestDefinitions"))
                if mutation == "missing-alias-execution":
                    results = document.find(tag("Results"))
                    results.remove(results[20])
                elif mutation == "missing-alias-roster":
                    manifest.pop("healthAliasMethods")
                elif mutation == "lowered-full-count":
                    manifest["fullForecast"] -= 1
                    results = document.find(tag("Results"))
                    results.remove(results[-1])
                    definitions.remove(definitions[-1])
                    counters = document.find(tag("ResultSummary")).find(tag("Counters"))
                    for name in ("total", "executed", "passed"):
                        counters.set(name, str(manifest["fullForecast"]))
                else:
                    index = 0 if mutation == "original-method" else 20
                    unit = definitions[index]
                    method = unit.find(tag("TestMethod"))
                    display = unit.get("name").replace(method.get("className"), "Synthetic.Unreviewed", 1)
                    method.set("className", "Synthetic.Unreviewed")
                    unit.set("name", display)
                    document.find(tag("Results"))[index].set("testName", display)
                with self.assertRaises(SystemExit):
                    self.verify(root, document, "full", manifest)


if __name__ == "__main__":
    unittest.main()
