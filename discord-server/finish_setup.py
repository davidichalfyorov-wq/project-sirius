#!/usr/bin/env python3
"""One-shot finisher: remove Discord's default channels, upgrade
announcements to a news channel, post + pin the welcome message."""
from __future__ import annotations

import discord

from sirius_common import (EMBED_COLOR, channel_by_key, get_guild_id,
                           get_token, load_config, load_env)

cfg = load_config()

WELCOME_TITLE = "🛰️ Welcome to Project Sirius"

ABOUT = (
    "**Project Sirius is to Freelancer what Black Mesa is to Half-Life** — "
    "a modern, open-source remake of the 2003 space sim, rebuilt on the "
    "**LibreLancer** engine (C# / .NET) and running **Discovery Freelancer "
    "4.86** content natively.\n\n"
    "One pilot at the helm, one goal: the Sirius sector the way you "
    "remember it — and better than it ever looked."
)

FIELD_TECH = (
    "• Cross-platform: Windows / Linux / macOS\n"
    "• New **Vulkan** renderer with an HDR pipeline\n"
    "• Filmic tonemapping, bloom, god rays, FXAA\n"
    "• Cascaded shadow maps + per-system environment lighting\n"
    "• Original FL data formats stay untouched — **bring your own legal "
    "copy** of Freelancer/Discovery content; no piracy here\n"
    "• Multiplayer with an authoritative server is part of the plan"
)

FIELD_START = (
    "1. Read {rules} and press **🚀 Launch** — you become a 🚀 **Pilot** "
    "and the station opens up.\n"
    "2. Hang out in 🍻 **THE BAR**, project talk lives in 🚀 **PROJECT "
    "SIRIUS**.\n"
    "3. Found a bug? One post per bug in {bugs}.\n"
    "4. Got an idea? Run **/suggest** — the crowd votes, staff answers.\n"
    "5. Want early test builds? Ask in {sirius_chat} for the 🧪 **Test "
    "Pilot** role."
)

FIELD_ROLES = (
    "👑 **Admiral** — the captain of this venture\n"
    "🛡️ **Station Police** — moderators; their word is law\n"
    "⚙️ **Shipwright** — engine developers\n"
    "🎨 **Artist** — art & lookdev contributors\n"
    "🧪 **Test Pilot** — flies the experimental builds\n"
    "💫 **Ace** — distinguished community members\n"
    "🚀 **Pilot** — verified member\n"
    "📡 **Comm Relay** — opt-in announcement pings"
)


def build_embed(guild: discord.Guild) -> discord.Embed:
    def m(key: str) -> str:
        ch = channel_by_key(guild, cfg, key)
        return ch.mention if ch else f"#{key}"

    embed = discord.Embed(title=WELCOME_TITLE, description=ABOUT,
                          colour=EMBED_COLOR)
    embed.add_field(name="🔧 What's under the hood", value=FIELD_TECH,
                    inline=False)
    embed.add_field(
        name="🧭 How to undock",
        value=FIELD_START.format(rules=m("rules"), bugs=m("bugs"),
                                 sirius_chat=m("sirius_chat")),
        inline=False)
    embed.add_field(name="🎖️ Station crew", value=FIELD_ROLES, inline=False)
    if guild.icon:
        embed.set_thumbnail(url=guild.icon.url)
    embed.set_footer(text="Fly safe, pilot. o7")
    return embed


async def run(client: discord.Client) -> None:
    guild = client.get_guild(get_guild_id())

    # 1. drop Discord's default channels (empty leftovers from server creation)
    print("== Default channel cleanup ==")
    default_cats = {"Text channels", "Voice channels"}
    for ch in list(guild.channels):
        is_default_child = (ch.category and ch.category.name in default_cats
                            and ch.name in ("general", "General"))
        is_default_cat = (isinstance(ch, discord.CategoryChannel)
                          and ch.name in default_cats)
        if is_default_child:
            await ch.delete(reason="Sirius: default channel cleanup")
            print(f"  - {ch.name}")
    for ch in list(guild.channels):
        if isinstance(ch, discord.CategoryChannel) and ch.name in default_cats:
            if not ch.channels:
                await ch.delete(reason="Sirius: default channel cleanup")
                print(f"  - category {ch.name}")

    # 2. upgrade announcements to a news channel (script only does it on create)
    print("== Announcements → news ==")
    ann = channel_by_key(guild, cfg, "announcements")
    if isinstance(ann, discord.TextChannel):
        if ann.type is discord.ChannelType.text and "COMMUNITY" in guild.features:
            await ann.edit(type=discord.ChannelType.news,
                           reason="Sirius: followable announcements")
            print("  upgraded")
        else:
            print(f"  already {ann.type}")

    # 3. welcome post (idempotent by pinned title)
    print("== Welcome post ==")
    welcome = channel_by_key(guild, cfg, "welcome")
    if isinstance(welcome, discord.TextChannel):
        existing = None
        async for msg in welcome.pins():
            if msg.embeds and msg.embeds[0].title == WELCOME_TITLE:
                existing = msg
                break
        embed = build_embed(guild)
        if existing:
            await existing.edit(embed=embed)
            print("  = updated")
        else:
            msg = await welcome.send(embed=embed)
            await msg.pin()
            print("  + posted and pinned")


def main() -> None:
    load_env()
    intents = discord.Intents.default()
    client = discord.Client(intents=intents)

    @client.event
    async def on_ready():
        try:
            await run(client)
            print("✅ finish_setup complete")
        except Exception as e:                     # noqa: BLE001
            print(f"ERROR: {e}")
        finally:
            await client.close()

    client.run(get_token())


if __name__ == "__main__":
    main()
