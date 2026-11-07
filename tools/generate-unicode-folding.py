#!/usr/bin/env python3
# SPDX-License-Identifier: Apache-2.0
# Copyright (c) 2026 Jason Furr
"""Generates the pinned Unicode tables of Vericairn.Records.Text.UnicodeFolding.

Reads the Unicode Character Database files of one version, checks each against its pinned SHA-256, and writes
src/Vericairn.Records/Text/UnicodeFolding.Tables.cs (decompositions, combining classes, primary composites, full case
folding and general categories) and tests/fixtures/unicode/NormalizationTest-<version>.txt.gz (the conformance test the
normalizer is checked against). Changing the version changes every search analyzer identity built on these tables.

Usage: python3 -I tools/generate-unicode-folding.py <ucd-directory> [--download]
With --download, the files are fetched from unicode.org into the given (new or empty) directory first.
"""

import gzip
import hashlib
import os
import sys
import urllib.request

VERSION = "16.0.0"
FILES = {
    "UnicodeData.txt": "ff58e5823bd095166564a006e47d111130813dcf8bf234ef79fa51a870edb48f",
    "CaseFolding.txt": "6f1f9c588eb4a5c718d9e8f93b782685e5c7fec872cf05e8e6878053599e09bb",
    "DerivedNormalizationProps.txt": "4d4c03892dea9146d674b686e495df2d55a28d071ac474041d73518f887abddc",
    "NormalizationTest.txt": "d811971453e7075e1ad56fb1b301eece5aa80757b81f6156e74a1bfb3ae5ceb1",
}

# .NET's System.Globalization.UnicodeCategory values for each General_Category.
CATEGORIES = {
    "Lu": 0, "Ll": 1, "Lt": 2, "Lm": 3, "Lo": 4, "Mn": 5, "Mc": 6, "Me": 7, "Nd": 8, "Nl": 9, "No": 10, "Zs": 11, "Zl": 12,
    "Zp": 13, "Cc": 14, "Cf": 15, "Cs": 16, "Co": 17, "Pc": 18, "Pd": 19, "Ps": 20, "Pe": 21, "Pi": 22, "Pf": 23, "Po": 24,
    "Sm": 25, "Sc": 26, "Sk": 27, "So": 28, "Cn": 29,
}

S_BASE, L_BASE, V_BASE, T_BASE = 0xAC00, 0x1100, 0x1161, 0x11A7
L_COUNT, V_COUNT, T_COUNT = 19, 21, 28
S_COUNT = L_COUNT * V_COUNT * T_COUNT


def fail(message):
    sys.stderr.write(message + "\n")
    sys.exit(2)


def read(directory, name):
    path = os.path.join(directory, name)
    with open(path, "rb") as handle:
        data = handle.read()
    digest = hashlib.sha256(data).hexdigest()
    if digest != FILES[name]:
        fail(f"{name} is not the pinned Unicode {VERSION} file (sha256 {digest}).")
    return data


def lines(data):
    for raw in data.decode("utf-8").splitlines():
        line = raw.split("#", 1)[0].strip()
        if line:
            yield [field.strip() for field in line.split(";")]


def code_range(text):
    if ".." in text:
        first, last = text.split("..")
        return range(int(first, 16), int(last, 16) + 1)
    return range(int(text, 16), int(text, 16) + 1)


def hangul(cp):
    index = cp - S_BASE
    result = [L_BASE + index // (V_COUNT * T_COUNT), V_BASE + (index % (V_COUNT * T_COUNT)) // T_COUNT]
    if index % T_COUNT:
        result.append(T_BASE + index % T_COUNT)
    return result


def main():
    if len(sys.argv) < 2:
        fail(__doc__)
    directory = sys.argv[1]
    if "--download" in sys.argv[2:]:
        os.makedirs(directory, exist_ok=True)
        for name in FILES:
            urllib.request.urlretrieve(f"https://www.unicode.org/Public/{VERSION}/ucd/{name}", os.path.join(directory, name))

    unicode_data = read(directory, "UnicodeData.txt")
    case_folding = read(directory, "CaseFolding.txt")
    derived = read(directory, "DerivedNormalizationProps.txt")
    conformance = read(directory, "NormalizationTest.txt")

    category = {}
    ccc = {}
    canonical = {}
    compatibility = {}
    range_start = None
    for fields in lines(unicode_data):
        cp, name, gc = int(fields[0], 16), fields[1], fields[2]
        if name.endswith(", First>"):
            range_start = cp
            continue
        if name.endswith(", Last>"):
            for each in range(range_start, cp + 1):
                category[each] = CATEGORIES[gc]
            range_start = None
            continue
        category[cp] = CATEGORIES[gc]
        if int(fields[3]):
            ccc[cp] = int(fields[3])
        if fields[5]:
            parts = fields[5].split()
            if parts[0].startswith("<"):
                compatibility[cp] = [int(p, 16) for p in parts[1:]]
            else:
                canonical[cp] = [int(p, 16) for p in parts]

    exclusions = set()
    for fields in lines(derived):
        if len(fields) >= 2 and fields[1] == "Full_Composition_Exclusion":
            exclusions.update(code_range(fields[0]))

    folding = {}
    for fields in lines(case_folding):
        if fields[1] in ("C", "F"):
            folding[int(fields[0], 16)] = [int(p, 16) for p in fields[2].split()]

    def expand(cp, compat):
        if S_BASE <= cp < S_BASE + S_COUNT:
            return hangul(cp)
        mapping = canonical.get(cp) or (compatibility.get(cp) if compat else None)
        if mapping is None:
            return [cp]
        return [x for part in mapping for x in expand(part, compat)]

    nfd = {cp: expand(cp, False) for cp in sorted(canonical)}
    nfkd = {}
    for cp in sorted(set(canonical) | set(compatibility)):
        full = expand(cp, True)
        if full != nfd.get(cp, [cp]):
            nfkd[cp] = full

    composites = {}
    for cp, mapping in canonical.items():
        if len(mapping) == 2 and cp not in exclusions:
            composites[(mapping[0] << 21) | mapping[1]] = cp

    # UnicodeFolding's fast paths: below U+0300 every code point is a starter and none is the second of a composite pair.
    if min(ccc) < 0x300 or min(key & 0x1FFFFF for key in composites) < 0x300:
        fail("A combining mark or a composite's second character below U+0300 breaks UnicodeFolding's fast paths.")

    def runs(values, default):
        out, previous = [], None
        for cp in range(0x110000):
            value = values.get(cp, default)
            if value != previous:
                out.append((cp, value))
                previous = value
        return out

    category_runs = runs(category, CATEGORIES["Cn"])
    # Surrogates are listed as ranges in UnicodeData.txt; they keep their category for the lone-surrogate lookups.
    ccc_runs = runs(ccc, 0)

    out = []
    out.append("// <auto-generated>")
    out.append(f"// Generated by tools/generate-unicode-folding.py from the Unicode Character Database {VERSION}; do not edit.")
    for name in ("UnicodeData.txt", "CaseFolding.txt", "DerivedNormalizationProps.txt"):
        out.append(f"// {name} sha256:{FILES[name]}")
    out.append("// Derived from Unicode data files under the Unicode License v3; see licenses/Unicode-3.0.txt.")
    out.append("// </auto-generated>")
    out.append("")
    out.append("namespace Vericairn.Records.Text;")
    out.append("")
    out.append("internal static partial class UnicodeFolding")
    out.append("{")
    out.append(f"    /// <summary>The version of the Unicode Character Database these tables were generated from.</summary>")
    out.append(f"    internal const string UnicodeVersion = \"{VERSION}\";")

    def array(name, kind, values, comment):
        out.append("")
        out.append(f"    // {comment}")
        out.append(f"    private static ReadOnlySpan<{kind}> {name} =>")
        out.append("    [")
        row = []
        for value in values:
            row.append(f"0x{value:X}")
            if len(row) == 16:
                out.append("        " + ", ".join(row) + ",")
                row = []
        if row:
            out.append("        " + ", ".join(row) + ",")
        out.append("    ];")

    def mapping(prefix, table, comment):
        keys = sorted(table)
        offsets, values = [0], []
        for key in keys:
            values.extend(table[key])
            offsets.append(len(values))
        array(prefix + "Keys", "int", keys, comment + ": sorted code points.")
        array(prefix + "Offsets", "int", offsets, "Where each code point's mapping starts in the values; one entry more than the keys.")
        array(prefix + "Values", "int", values, "The mappings, concatenated.")

    mapping("Canonical", nfd, f"Full canonical decompositions ({len(nfd)}), Hangul syllables excepted (they decompose algorithmically)")
    mapping("Compatibility", nfkd, f"Full compatibility decompositions that differ from the canonical one ({len(nfkd)})")
    mapping("Folding", folding, f"Full case folding, statuses C and F of CaseFolding.txt ({len(folding)})")
    keys = sorted(composites)
    array("CompositionKeys", "long", keys, f"Primary composites ({len(keys)}): sorted (first << 21) | second pairs, Hangul excepted.")
    array("CompositionValues", "int", [composites[k] for k in keys], "The composite of each pair.")
    array("CombiningClassRuns", "int", [(cp << 8) | value for cp, value in ccc_runs],
          f"Canonical combining classes ({len(ccc_runs)} runs): (first code point << 8) | class, each run to the next.")
    array("CategoryRuns", "int", [(cp << 5) | value for cp, value in category_runs],
          f"General categories ({len(category_runs)} runs): (first code point << 5) | UnicodeCategory, each run to the next.")
    out.append("}")
    out.append("")

    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    target = os.path.join(root, "src", "Vericairn.Records", "Text", "UnicodeFolding.Tables.cs")
    with open(target, "w", encoding="utf-8", newline="\n") as handle:
        handle.write("\n".join(out))
    fixture = os.path.join(root, "tests", "fixtures", "unicode", f"NormalizationTest-{VERSION}.txt.gz")
    os.makedirs(os.path.dirname(fixture), exist_ok=True)
    with open(fixture, "wb") as handle:
        # A fixed modification time keeps the compressed bytes reproducible.
        with gzip.GzipFile(filename=f"NormalizationTest-{VERSION}.txt", mode="wb", fileobj=handle, compresslevel=9, mtime=0) as compressed:
            compressed.write(conformance)
    print(f"Wrote {os.path.relpath(target, root)} and {os.path.relpath(fixture, root)} from Unicode {VERSION}.")


if __name__ == "__main__":
    main()
