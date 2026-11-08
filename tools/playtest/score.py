#!/usr/bin/env python3
"""Score a Haulout playtest trace. Usage: score.py <trace.jsonl> [--json]"""
import json
import os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__))
DATA = os.path.join(HERE, "..", "..", "src", "Haulout.Sim", "Data", "sloop28.json")

# Per-fault: component ids whose clean/replace/charge is the real fix of the
# first fault. Update when a new fault becomes the playtest target.
FIX_COMPONENT_IDS = ["negative-terminal"]

QUERIES = {"status", "logbook", "aboard", "help", "briefing"}
DECOY_TOOLS = {"clean", "replace", "charge"}


def norm(s):
    return re.sub(r"^(the|a)\s+", "", str(s).strip().lower())


def fix_names():
    names = set(FIX_COMPONENT_IDS)
    try:
        with open(DATA) as f:
            data = json.load(f)
        for c in data.get("components", []):
            if c.get("id") in FIX_COMPONENT_IDS:
                names.add(norm(c.get("name", "")))
                names.update(norm(a) for a in c.get("aliases", []))
    except Exception:
        names.add("negative terminal")
    return {n for n in names if n}


HOME_PORT = "Skjærvik"
OUT_PORT = "Hestholmen"


def trip_fields(rows):
    """Trip outcome from the trace's status fields (port, atSea, sunk). Old traces without them score no trip."""
    departed = sum(1 for r in rows if r.get("tool") == "depart" and r.get("accepted"))
    towed = sum(1 for r in rows if r.get("tool") == "call_tow" and r.get("accepted"))
    sank = any(r.get("sunk") for r in rows)
    arrived_out = arrived_home = goal = False
    been_out = False
    minutes_at_sea = 0
    prev = None
    for r in rows:
        if prev is not None and prev.get("atSea"):
            minutes_at_sea += max(0, r.get("minutesSpent", 0) - prev.get("minutesSpent", 0))
            if not r.get("atSea") and not r.get("sunk"):
                towed_here = r.get("tool") == "call_tow"
                if r.get("port") == OUT_PORT:
                    arrived_out = True
                    if not towed_here:
                        been_out = True
                elif r.get("port") == HOME_PORT:
                    arrived_home = True
                    if been_out and not towed_here:
                        goal = True
        prev = r
    return {
        "departed": departed,
        "arrived_out": "yes" if arrived_out else "no",
        "arrived_home": "yes" if arrived_home else "no",
        "towed": towed,
        "sank": "yes" if sank else "no",
        "hours_at_sea": round(minutes_at_sea / 60, 1),
        "goal": "yes" if goal else "no",
    }


def main(argv):
    args = [a for a in argv if not a.startswith("--")]
    as_json = "--json" in argv
    if len(args) != 1:
        print(__doc__.strip(), file=sys.stderr)
        return 2
    path = args[0]
    if not os.path.exists(path):
        print("no trace: %s (the agent made no tool calls, or never started)" % path, file=sys.stderr)
        return 1
    rows = []
    with open(path) as f:
        for line in f:
            line = line.strip()
            if line:
                try:
                    rows.append(json.loads(line))
                except ValueError:
                    pass
    fixes = fix_names()
    solved = any(r.get("engineRunning") for r in rows)
    cut = len(rows)
    if solved:
        cut = next(i for i, r in enumerate(rows) if r.get("engineRunning")) + 1
    upto = rows[:cut]
    last = upto[-1] if upto else {}
    decoys = 0
    for r in upto:
        if r.get("tool") in DECOY_TOOLS and r.get("accepted"):
            a = r.get("args") or {}
            target = norm(a.get("component", a.get("item", "")))
            if target not in fixes:
                decoys += 1
    m = re.match(r"\d{8}-\d{6}-(.+)\.trace\.jsonl$", os.path.basename(path))
    out = {
        "solved": "yes" if solved else "no",
        "steps": len(upto),
        "actions": sum(1 for r in upto if r.get("tool") not in QUERIES),
        "queries": sum(1 for r in upto if r.get("tool") in QUERIES),
        "minutes": last.get("minutesSpent", 0),
        "money": last.get("moneySpentNok", 0),
        "decoys": decoys,
        "rejected": sum(1 for r in upto if r.get("accepted") is False),
        **trip_fields(rows),
        "model": m.group(1) if m else "unknown",
        "trace": path,
    }
    if as_json:
        print(json.dumps(out))
    else:
        print(" ".join(f"{k}={v}" for k, v in out.items()))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
