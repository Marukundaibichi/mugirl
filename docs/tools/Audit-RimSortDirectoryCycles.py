"""只读检查 RimSort 模组目录图，报告可达的 Junction/符号链接环路。"""

import argparse
from collections import Counter, deque
from datetime import datetime
import json
import os
from pathlib import Path
import stat
import time
import xml.etree.ElementTree as ET


def canonical(path):
    return os.path.normcase(os.path.realpath(path))


def metadata(path):
    result = {"name": Path(path).name, "package_id": None, "about_xml": None}
    try:
        about = next((p for p in Path(path).iterdir() if p.name.lower() == "about" and p.is_dir()), None)
        xml = next((p for p in about.iterdir() if p.name.lower() == "about.xml" and p.is_file()), None) if about else None
        if xml:
            root = ET.parse(xml).getroot()
            values = {e.tag.lower(): (e.text or "").strip() for e in root}
            result.update(name=values.get("name") or result["name"], package_id=values.get("packageid"), about_xml=str(xml))
    except (OSError, ET.ParseError) as exc:
        result["metadata_error"] = str(exc)
    return result


def strongly_connected(graph, reverse):
    # 迭代 DFS，避免 Python 递归深度限制；每个物理目录只枚举一次。
    seen, order = set(), []
    for root in graph:
        if root in seen:
            continue
        seen.add(root)
        stack = [(root, iter(graph[root]))]
        while stack:
            node, children = stack[-1]
            child = next(children, None)
            if child is None:
                order.append(node)
                stack.pop()
            elif child not in seen:
                seen.add(child)
                stack.append((child, iter(graph[child])))
    seen, cycles = set(), []
    for root in reversed(order):
        if root in seen:
            continue
        component, pending = [], [root]
        seen.add(root)
        while pending:
            node = pending.pop()
            component.append(node)
            for parent in reverse.get(node, ()):
                if parent not in seen:
                    seen.add(parent)
                    pending.append(parent)
        if len(component) > 1 or root in graph[root]:
            cycles.append(component)
    return cycles


def scan(sources, max_directories=500000, max_seconds=180):
    started = time.perf_counter()
    roots, errors = [], []
    for source, kind in sources:
        try:
            for entry in sorted(Path(source).iterdir()):
                if entry.is_dir():
                    roots.append({"path": str(entry), "physical_path": canonical(entry), "source": kind, **metadata(entry)})
        except OSError as exc:
            errors.append({"path": str(source), "error": str(exc)})

    graph, reverse, links = {}, {}, []
    queued = {r["physical_path"] for r in roots}
    pending = sorted(queued)
    file_count = 0
    while pending:
        if len(graph) >= max_directories or time.perf_counter() - started > max_seconds:
            raise RuntimeError("扫描超过安全上限；没有生成可误认为完整结果的报告。")
        current = pending.pop()
        children = []
        graph[current] = children
        try:
            with os.scandir(current) as entries:
                for entry in entries:
                    try:
                        info = entry.stat(follow_symlinks=False)
                        attributes = getattr(info, "st_file_attributes", 0)
                        reparse = bool(attributes & 0x400) or stat.S_ISLNK(info.st_mode)
                        directory = bool(attributes & 0x10) or entry.is_dir(follow_symlinks=True)
                        if not directory:
                            file_count += 1
                            continue
                        child = canonical(entry.path) if reparse else os.path.normcase(entry.path)
                        if reparse:
                            links.append({"path": entry.path, "parent": current, "target": child, "attributes": hex(attributes), "reparse_tag": getattr(info, "st_reparse_tag", None)})
                        children.append(child)
                        reverse.setdefault(child, []).append(current)
                        if child not in queued:
                            queued.add(child)
                            pending.append(child)
                    except OSError as exc:
                        errors.append({"path": entry.path, "error": str(exc)})
        except OSError as exc:
            errors.append({"path": current, "error": str(exc)})
        if len(graph) % 20000 == 0:
            print(f"已枚举 {len(graph)} 个物理目录，{file_count} 个文件。", flush=True)

    components = strongly_connected(graph, reverse)
    reachable_cycles = {}
    cycles = []
    for index, component in enumerate(components, 1):
        members = set(component)
        cycles.append({"id": index, "directories": component, "cycle_links": [link for link in links if link["parent"] in members and link["target"] in members]})
        pending = deque(component)
        seen = set(component)
        while pending:
            node = pending.popleft()
            reachable_cycles.setdefault(node, []).append(index)
            for parent in reverse.get(node, ()):
                if parent not in seen:
                    seen.add(parent)
                    pending.append(parent)
    for root in roots:
        root["reachable_cycle_ids"] = reachable_cycles.get(root["physical_path"], [])
        root["contained_directory_links"] = sum(link["path"].startswith(root["physical_path"] + os.sep) for link in links)
    return {
        "generated_at": datetime.now().astimezone().isoformat(),
        "sources": [{"path": str(p), "kind": k} for p, k in sources],
        "summary": {"seconds": round(time.perf_counter() - started, 3), "candidate_directories": len(roots), "sources": dict(Counter(r["source"] for r in roots)), "about_xml_present": sum(bool(r["about_xml"]) for r in roots), "physical_directories": len(graph), "files": file_count, "directory_links": len(links), "cycle_components": len(cycles), "affected_candidates": sum(bool(r["reachable_cycle_ids"]) for r in roots), "scan_errors": len(errors)},
        "affected_mods": [r for r in roots if r["reachable_cycle_ids"]],
        "mods_with_directory_links": [r for r in roots if r["contained_directory_links"]],
        "cycles": cycles, "directory_links": links, "scan_errors": errors,
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--settings", default=str(Path.home() / "AppData/Local/RimSort/settings.json"))
    parser.add_argument("--source", action="append", default=[], help="额外的模组容器目录；不提供 --settings 时可用于独立检查")
    parser.add_argument("--output", required=True)
    args = parser.parse_args()
    sources = []
    if args.settings:
        settings = json.loads(Path(args.settings).read_text(encoding="utf-8-sig"))
        for instance in settings.get("instances", {}).values():
            for field, kind in (("local_folder", "local"), ("workshop_folder", "workshop"), ("game_folder", "data")):
                value = instance.get(field)
                if value:
                    sources.append((Path(value) / "Data" if kind == "data" else Path(value), kind))
    sources.extend((Path(p), "extra") for p in args.source)
    unique = {}
    for source, kind in sources:
        unique.setdefault(canonical(source), (source, kind))
    if not unique:
        parser.error("至少需要一个扫描来源")
    report = scan(list(unique.values()))
    output = Path(args.output)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({"summary": report["summary"], "affected_mods": report["affected_mods"], "cycle_links": [link for c in report["cycles"] for link in c["cycle_links"]], "scan_errors": report["scan_errors"][:10]}, ensure_ascii=False, indent=2))
    return 2 if report["scan_errors"] else 1 if report["affected_mods"] else 0


if __name__ == "__main__":
    raise SystemExit(main())
