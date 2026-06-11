"""Tiny JSON persistence for Sirius Bot (warns, suggestion counter)."""
from __future__ import annotations

import json
import pathlib
import threading

DATA_DIR = pathlib.Path(__file__).resolve().parent / "data"
DATA_DIR.mkdir(exist_ok=True)

_lock = threading.Lock()


def _path(name: str) -> pathlib.Path:
    return DATA_DIR / f"{name}.json"


def load(name: str, default):
    with _lock:
        p = _path(name)
        if not p.exists():
            return default
        try:
            return json.loads(p.read_text(encoding="utf-8"))
        except (json.JSONDecodeError, OSError):
            return default


def save(name: str, data) -> None:
    with _lock:
        tmp = _path(name).with_suffix(".tmp")
        tmp.write_text(json.dumps(data, ensure_ascii=False, indent=2),
                       encoding="utf-8")
        tmp.replace(_path(name))


# -- warns ------------------------------------------------------------------
def add_warn(guild_id: int, user_id: int, by: str, reason: str, ts: float) -> int:
    warns = load("warns", {})
    lst = warns.setdefault(str(guild_id), {}).setdefault(str(user_id), [])
    lst.append({"by": by, "reason": reason, "ts": ts})
    save("warns", warns)
    return len(lst)


def get_warns(guild_id: int, user_id: int) -> list[dict]:
    return load("warns", {}).get(str(guild_id), {}).get(str(user_id), [])


def clear_warns(guild_id: int, user_id: int) -> int:
    warns = load("warns", {})
    lst = warns.get(str(guild_id), {}).pop(str(user_id), [])
    save("warns", warns)
    return len(lst)


# -- suggestion counter -----------------------------------------------------
def next_suggestion_number() -> int:
    data = load("suggestions", {"counter": 0})
    data["counter"] += 1
    save("suggestions", data)
    return data["counter"]
