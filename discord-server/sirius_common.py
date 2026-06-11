"""Shared helpers for Project Sirius Discord kit (setup script + bot)."""
from __future__ import annotations

import json
import os
import pathlib

import discord

ROOT = pathlib.Path(__file__).resolve().parent
VERIFY_CUSTOM_ID = "sirius:verify"
EMBED_COLOR = 0x4DA6FF  # Sirius blue


def load_config() -> dict:
    with open(ROOT / "config.json", encoding="utf-8") as f:
        return json.load(f)


def load_env() -> None:
    """Tiny .env loader (no external deps). Does not override real env vars."""
    env_path = ROOT / ".env"
    if not env_path.exists():
        return
    for line in env_path.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        key, _, value = line.partition("=")
        key, value = key.strip(), value.strip().strip("'\"")
        os.environ.setdefault(key, value)


def get_token() -> str:
    token = os.environ.get("DISCORD_TOKEN", "")
    if not token:
        raise SystemExit("DISCORD_TOKEN is not set. Copy .env.example to .env and fill it in.")
    return token


def get_guild_id() -> int:
    raw = os.environ.get("GUILD_ID", "")
    if not raw.isdigit():
        raise SystemExit("GUILD_ID is not set (right-click your server → Copy Server ID).")
    return int(raw)


def color_from_hex(value: str | None) -> discord.Colour:
    if not value:
        return discord.Colour.default()
    return discord.Colour(int(value.lstrip("#"), 16))


def role_by_key(guild: discord.Guild, cfg: dict, key: str) -> discord.Role | None:
    name = cfg["role_keys"].get(key)
    return discord.utils.get(guild.roles, name=name) if name else None


def channel_name_for_key(cfg: dict, key: str) -> str | None:
    for cat in cfg["categories"]:
        for ch in cat["channels"]:
            if ch.get("key") == key:
                return ch["name"]
    return None


def channel_by_key(guild: discord.Guild, cfg: dict, key: str):
    name = channel_name_for_key(cfg, key)
    if not name:
        return None
    return discord.utils.get(guild.channels, name=name)
