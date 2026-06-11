"""Sirius Bot — verification, welcomes, anti-spam, moderation, suggestions.

Run from the discord-server directory:  python -m bot.main
"""
from __future__ import annotations

import sys
from pathlib import Path

# allow `python bot/main.py` as well as `python -m bot.main`
sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

import discord
from discord import app_commands

from sirius_common import (channel_by_key, get_guild_id, get_token,
                           load_config, load_env, role_by_key)

from bot import antispam as antispam_mod
from bot import community, moderation
from bot.views import VerifyView

cfg = load_config()
load_env()

intents = discord.Intents.default()
intents.members = True
intents.message_content = True

client = discord.Client(intents=intents)
tree = app_commands.CommandTree(client)
guard = antispam_mod.AntiSpam(cfg)

moderation.register(tree, client, cfg, guard)
community.register(tree, client, cfg)


def _fill_welcome(template: str, member: discord.Member) -> str:
    guild = member.guild
    text = (template
            .replace("{mention}", member.mention)
            .replace("{count}", str(guild.member_count)))
    for key in ("rules", "sirius_chat", "bugs"):
        ch = channel_by_key(guild, cfg, key)
        text = text.replace("{%s}" % key,
                            ch.mention if ch else f"#{key}")
    return text


@client.event
async def setup_hook():
    client.add_view(VerifyView(cfg))
    guild_obj = discord.Object(id=get_guild_id())
    tree.copy_global_to(guild=guild_obj)
    await tree.sync(guild=guild_obj)


@client.event
async def on_ready():
    print(f"Sirius Bot online as {client.user} "
          f"(guilds: {[g.name for g in client.guilds]})")
    await client.change_presence(activity=discord.CustomActivity(
        name="Watching over the Sirius sector o7"))


@client.event
async def on_member_join(member: discord.Member):
    if member.guild.id != get_guild_id():
        return
    kicked = await antispam_mod.handle_join(client, guard, member)
    if kicked:
        return
    if member.bot:
        bots = role_by_key(member.guild, cfg, "bots")
        if bots:
            try:
                await member.add_roles(bots, reason="Sirius: bot account")
            except discord.HTTPException:
                pass
        return
    docked = role_by_key(member.guild, cfg, "unverified")
    if docked:
        try:
            await member.add_roles(docked, reason="Sirius: new arrival")
        except discord.HTTPException:
            pass
    welcome = channel_by_key(member.guild, cfg, "welcome")
    if isinstance(welcome, discord.TextChannel):
        try:
            await welcome.send(_fill_welcome(cfg["welcome"]["message"], member))
        except discord.HTTPException:
            pass


@client.event
async def on_message(message: discord.Message):
    await antispam_mod.handle_message(client, guard, message)


def main() -> None:
    client.run(get_token())


if __name__ == "__main__":
    main()
