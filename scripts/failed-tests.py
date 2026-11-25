#!/usr/bin/env python3
"""Print each failed test in the .trx files under a folder, with the start of its message.

CI logs are read from their tail, and a long functional run pushes the failures out of it; this step, run last, puts them back at the
end. Usage: scripts/failed-tests.py [folder] [lines per failure]."""
import pathlib
import sys
import xml.etree.ElementTree as ET

NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


def main() -> int:
    folder = pathlib.Path(sys.argv[1] if len(sys.argv) > 1 else "test-results")
    keep = int(sys.argv[2]) if len(sys.argv) > 2 else 40
    failed = 0
    for trx in sorted(folder.rglob("*.trx")):
        for result in ET.parse(trx).getroot().iterfind(".//t:UnitTestResult", NS):
            if result.get("outcome") != "Failed":
                continue
            failed += 1
            message = result.findtext(".//t:Message", default="", namespaces=NS).splitlines()
            print(f"FAILED {result.get('testName')}")
            print("\n".join(f"    {line}" for line in message[:keep]))
            if len(message) > keep:
                print(f"    ... {len(message) - keep} more lines in {trx.name}")
    print(f"{failed} failed test(s).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
