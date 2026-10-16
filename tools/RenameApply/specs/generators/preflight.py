"""Reproduce Wave R in two isolated workspaces and lint both outputs.

Usage: python preflight.py --tree <mod root> --work-root <new external directory>
Optional: --inputs <preserved Scratchpad-WaveA bundle>.
The input archive and repository specs stay read-only. A report records input hashes,
output hashes and every generated difference from the shipped specification.
"""
import argparse
import hashlib
import json
import os
import shutil
import subprocess
import sys
from pathlib import Path


def hashes(root, files):
    return {str(p.relative_to(root)).replace("\\", "/"): hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(files)}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--tree", type=Path, required=True)
    parser.add_argument("--work-root", type=Path, required=True)
    parser.add_argument("--inputs", type=Path)
    args = parser.parse_args()
    tree, work = args.tree.resolve(), args.work_root.resolve()
    inputs = (args.inputs or tree / "DevData/Backups/Scratchpad-WaveA-20261015").resolve()
    if work == tree or tree in work.parents or work == inputs or inputs in work.parents:
        parser.error("--work-root must be outside the mod and immutable input bundle")
    if work.exists():
        parser.error("--work-root must not already exist; evidence is never overwritten")
    spec = tree / "tools/RenameApply/specs"
    scripts = Path(__file__).resolve().parent
    required = [inputs / "understand/defs.json", inputs / "understand/dlls/types.tsv"] + list((inputs / "spec").glob("*.json"))
    if not all(p.is_file() for p in required) or not (inputs / "spec/merged.json").is_file():
        parser.error("missing preserved Wave A inputs")
    work.mkdir(parents=True)
    output_hashes = []
    for run in ("first", "second"):
        destination = work / run
        shutil.copytree(spec, destination, ignore=shutil.ignore_patterns("__pycache__", "*.pyc"))
        env = dict(os.environ, MUGIRL_REPO=str(tree), MUGIRL_SPEC_INPUT=str(inputs), MUGIRL_SPEC_ROOT=str(destination), PYTHONUTF8="1", PYTHONDONTWRITEBYTECODE="1")
        # An inherited linter override must not accidentally validate a third workspace.
        env.pop("LINT_SPEC_ROOT", None)
        for script in ("gen_R.py", "lint_spec.py"):
            result = subprocess.run([sys.executable, str(scripts / script), str(tree)], env=env, text=True, encoding="utf-8", capture_output=True)
            (work / f"{run}-{script}.log").write_text(result.stdout + result.stderr, encoding="utf-8")
            if result.returncode:
                raise SystemExit(f"{run} {script} failed ({result.returncode}); see {work / (run + '-' + script + '.log')}")
        output_hashes.append(hashes(destination, list((destination / "R").glob("*.json")) + [destination / "generators/gen_R.log.json"]))
    if output_hashes[0] != output_hashes[1]:
        raise SystemExit("non-reproducible Wave R output; retained both workspaces")
    shipped = hashes(spec, list((spec / "R").glob("*.json")) + [spec / "generators/gen_R.log.json"])
    report = {"tree": str(tree), "inputs": str(inputs), "inputHashes": hashes(inputs, required), "outputHashes": output_hashes[0],
              "reproducible": True, "changedFromShipped": sorted(p for p, value in output_hashes[0].items() if shipped.get(p) != value)}
    (work / "preflight-report.json").write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Wave R: both runs lint clean and byte-identical; {len(report['changedFromShipped'])} generated files differ from shipped specs.")
    print(work / "preflight-report.json")


if __name__ == "__main__":
    main()
