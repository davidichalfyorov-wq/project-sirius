#!/usr/bin/env python3
"""Project Sirius — one-time Discord server builder.

Creates/updates roles, categories, channels, permissions, AutoMod rules,
server icon and seed messages (rules + verification button).

Usage:
    python setup_server.py            # build / update everything
    python setup_server.py --wipe    # delete existing channels/roles first (asks!)
    python setup_server.py --skip-automod
"""
from __future__ import annotations

import argparse
import sys

import discord

from sirius_common import (EMBED_COLOR, ROOT, VERIFY_CUSTOM_ID, channel_by_key,
                           color_from_hex, get_guild_id, get_token,
                           load_config, load_env, role_by_key)

cfg = load_config()


# --------------------------------------------------------------------------
# permission helpers
# --------------------------------------------------------------------------
def perms_from_list(names: list[str], admin: bool = False) -> discord.Permissions:
    if admin:
        return discord.Permissions(administrator=True)
    return discord.Permissions(**{n: True for n in names})


def category_overwrites(guild: discord.Guild, access: str) -> dict:
    everyone = guild.default_role
    pilot = role_by_key(guild, cfg, "verified")
    mod = role_by_key(guild, cfg, "moderator")
    dev = role_by_key(guild, cfg, "developer")
    bots = role_by_key(guild, cfg, "bots")
    ow: dict = {}
    if access == "public_readonly":
        ow[everyone] = discord.PermissionOverwrite(
            view_channel=True, send_messages=False, add_reactions=True,
            read_message_history=True, create_public_threads=False,
            create_private_threads=False)
        for r in (mod, bots):
            if r:
                ow[r] = discord.PermissionOverwrite(send_messages=True)
    elif access == "members":
        ow[everyone] = discord.PermissionOverwrite(view_channel=False)
        for r in (pilot, mod, dev, bots):
            if r:
                ow[r] = discord.PermissionOverwrite(view_channel=True)
    elif access == "staff":
        ow[everyone] = discord.PermissionOverwrite(view_channel=False)
        for r in (mod, bots):
            if r:
                ow[r] = discord.PermissionOverwrite(view_channel=True)
        admin = role_by_key(guild, cfg, "admin")
        if admin:
            ow[admin] = discord.PermissionOverwrite(view_channel=True)
    return ow


def channel_overwrites(guild: discord.Guild, base: dict, ch_cfg: dict) -> dict:
    ow = dict(base)
    everyone = guild.default_role
    pilot = role_by_key(guild, cfg, "verified")
    bots = role_by_key(guild, cfg, "bots")

    if ch_cfg.get("post_roles"):
        # read-only feed, only the listed role keys may post
        ow[everyone] = discord.PermissionOverwrite(
            view_channel=(base.get(everyone).view_channel
                          if base.get(everyone) else True),
            send_messages=False, add_reactions=True, read_message_history=True)
        if pilot:
            o = ow.get(pilot) or discord.PermissionOverwrite()
            o.send_messages = False
            if ch_cfg.get("threads_for_all"):
                o.send_messages_in_threads = True
                o.create_public_threads = True
            ow[pilot] = o
        for key in ch_cfg["post_roles"]:
            r = role_by_key(guild, cfg, key)
            if r:
                o = ow.get(r) or discord.PermissionOverwrite()
                o.send_messages = True
                ow[r] = o

    if ch_cfg.get("bot_only_posts"):
        ow[everyone] = discord.PermissionOverwrite(view_channel=False)
        if pilot:
            ow[pilot] = discord.PermissionOverwrite(
                view_channel=True, send_messages=False, add_reactions=True,
                send_messages_in_threads=True, create_public_threads=False)
        if bots:
            ow[bots] = discord.PermissionOverwrite(view_channel=True,
                                                   send_messages=True,
                                                   create_public_threads=True)
    return ow


# --------------------------------------------------------------------------
# build steps
# --------------------------------------------------------------------------
async def ensure_roles(guild: discord.Guild) -> None:
    print("== Roles ==")
    for spec in cfg["roles"]:
        perms = perms_from_list(spec.get("perms", []), spec.get("admin", False))
        role = discord.utils.get(guild.roles, name=spec["name"])
        kwargs = dict(colour=color_from_hex(spec["color"]), hoist=spec["hoist"],
                      mentionable=spec["mentionable"], permissions=perms)
        if role is None:
            role = await guild.create_role(name=spec["name"], **kwargs,
                                           reason="Sirius setup")
            print(f"  + created {role.name}")
        else:
            await role.edit(**kwargs, reason="Sirius setup")
            print(f"  = updated {role.name}")

    # order: same as in config, highest first (below the bot's own top role)
    me_top = guild.me.top_role.position
    ordered = [discord.utils.get(guild.roles, name=s["name"]) for s in cfg["roles"]]
    positions = {}
    pos = me_top - 1
    for role in ordered:
        if role and not role.managed:
            positions[role] = max(pos, 1)
            pos -= 1
    try:
        await guild.edit_role_positions(positions, reason="Sirius setup")
    except discord.HTTPException as e:
        print(f"  ! could not reorder roles: {e}")

    bots_role = role_by_key(guild, cfg, "bots")
    if bots_role and bots_role not in guild.me.roles:
        try:
            await guild.me.add_roles(bots_role, reason="Sirius setup")
        except discord.HTTPException:
            pass


async def ensure_channels(guild: discord.Guild) -> None:
    print("== Channels ==")
    community = "COMMUNITY" in guild.features
    for cat_cfg in cfg["categories"]:
        base_ow = category_overwrites(guild, cat_cfg["access"])
        category = discord.utils.get(guild.categories, name=cat_cfg["name"])
        if category is None:
            category = await guild.create_category(cat_cfg["name"],
                                                   overwrites=base_ow,
                                                   reason="Sirius setup")
            print(f"  + category {category.name}")
        else:
            await category.edit(overwrites=base_ow)

        for ch_cfg in cat_cfg["channels"]:
            ow = channel_overwrites(guild, base_ow, ch_cfg)
            name, ctype = ch_cfg["name"], ch_cfg["type"]
            existing = discord.utils.get(guild.channels, name=name)
            if existing:
                # upgrade empty text channel to forum once community is enabled
                if (ctype == "forum" and community
                        and isinstance(existing, discord.TextChannel)):
                    empty = not [m async for m in existing.history(limit=1)]
                    if empty:
                        await existing.delete(reason="Sirius: upgrade to forum")
                        existing = None
                if existing:
                    if existing.category != category:
                        await existing.edit(category=category)
                    await existing.edit(overwrites=ow)
                    print(f"  = {name}")
                    continue
            if ctype == "voice":
                await guild.create_voice_channel(
                    name, category=category, overwrites=ow,
                    user_limit=ch_cfg.get("user_limit", 0), reason="Sirius setup")
            elif ctype == "forum" and community:
                tags = [discord.ForumTag(name=t) for t in ch_cfg.get("tags", [])]
                await guild.create_forum(
                    name, category=category, overwrites=ow,
                    topic=ch_cfg.get("topic"), available_tags=tags,
                    reason="Sirius setup")
            else:  # text (and forum fallback when community is unavailable)
                ch = await guild.create_text_channel(
                    name, category=category, overwrites=ow,
                    topic=ch_cfg.get("topic"),
                    slowmode_delay=ch_cfg.get("slowmode", 0),
                    reason="Sirius setup")
                if ctype == "forum":
                    print(f"    (forum unavailable → created {name} as text)")
                if ch_cfg.get("news") and community:
                    try:
                        await ch.edit(type=discord.ChannelType.news)
                    except discord.HTTPException:
                        pass
            print(f"  + {name}")


async def apply_guild_settings(guild: discord.Guild) -> None:
    print("== Server settings ==")
    s = cfg["server"]
    kwargs: dict = {
        "name": s["name"],
        "verification_level": discord.VerificationLevel[s["verification_level"]],
        "explicit_content_filter": discord.ContentFilter[s["explicit_content_filter"]],
        "default_notifications": discord.NotificationLevel[s["default_notifications"]],
        "reason": "Sirius setup",
    }
    for rel in s.get("icon_paths", []):
        p = (ROOT / rel).resolve()
        if p.exists():
            data = p.read_bytes()
            if len(data) <= 10 * 1024 * 1024:
                kwargs["icon"] = data
                print(f"  icon: {p.name} ({len(data)//1024} KB)")
            else:
                print(f"  ! icon {p} is larger than 10 MB, skipped")
            break
    else:
        print("  ! no icon file found, skipped")
    await guild.edit(**kwargs)

    welcome = channel_by_key(guild, cfg, "welcome")
    afk = channel_by_key(guild, cfg, "afk")
    extra: dict = {}
    if welcome:
        extra["system_channel"] = welcome
        extra["system_channel_flags"] = discord.SystemChannelFlags(
            join_notifications=False, premium_subscriptions=True)
    if afk:
        extra["afk_channel"] = afk
        extra["afk_timeout"] = s.get("afk_timeout", 300)
    if extra:
        await guild.edit(**extra)

    if s.get("try_enable_community") and "COMMUNITY" not in guild.features:
        rules, staff = channel_by_key(guild, cfg, "rules"), channel_by_key(guild, cfg, "staff")
        try:
            await guild.edit(community=True, rules_channel=rules,
                             public_updates_channel=staff)
            print("  community mode: enabled")
        except discord.HTTPException as e:
            print(f"  ! community mode not enabled: {e}")


async def ensure_automod(guild: discord.Guild) -> None:
    print("== AutoMod ==")
    am = cfg["automod"]
    botlog = channel_by_key(guild, cfg, "botlog")
    exempt_roles = [r for k in am["exempt_role_keys"]
                    if (r := role_by_key(guild, cfg, k))]
    try:
        existing = {r.name for r in await guild.fetch_automod_rules()}
    except discord.HTTPException:
        existing = set()

    def actions(timeout: int = 0):
        acts = [discord.AutoModRuleAction(
            custom_message="Blocked by Project Sirius station security.")]
        if botlog:
            acts.append(discord.AutoModRuleAction(channel_id=botlog.id))
        if timeout:
            from datetime import timedelta
            acts.append(discord.AutoModRuleAction(duration=timedelta(seconds=timeout)))
        return acts

    rules = [
        ("Sirius — Mention Spam",
         discord.AutoModTrigger(type=discord.AutoModRuleTriggerType.mention_spam,
                                mention_limit=am["mention_spam_limit"]),
         actions(timeout=60)),
        ("Sirius — Spam",
         discord.AutoModTrigger(type=discord.AutoModRuleTriggerType.spam),
         actions()),
        ("Sirius — Slurs & Profanity",
         discord.AutoModTrigger(
             type=discord.AutoModRuleTriggerType.keyword_preset,
             presets=discord.AutoModPresets(
                 **{p: True for p in am["keyword_presets"]})),
         [discord.AutoModRuleAction(
             custom_message="Watch your language, pilot.")]),
        ("Sirius — Scam Links",
         discord.AutoModTrigger(type=discord.AutoModRuleTriggerType.keyword,
                                regex_patterns=am["scam_regex"]),
         actions(timeout=300)),
    ]
    for name, trigger, acts in rules:
        if name in existing:
            print(f"  = {name}")
            continue
        try:
            await guild.create_automod_rule(
                name=name, event_type=discord.AutoModRuleEventType.message_send,
                trigger=trigger, actions=acts, enabled=True,
                exempt_roles=exempt_roles, reason="Sirius setup")
            print(f"  + {name}")
        except discord.HTTPException as e:
            print(f"  ! {name}: {e}")


class VerifyButton(discord.ui.View):
    def __init__(self):
        super().__init__(timeout=None)
        self.add_item(discord.ui.Button(
            style=discord.ButtonStyle.success,
            label=cfg["seed_messages"]["verify_button_label"],
            custom_id=VERIFY_CUSTOM_ID))


async def _pinned_with_title(channel: discord.TextChannel, title: str):
    for msg in await channel.pins():
        if msg.embeds and msg.embeds[0].title == title:
            return msg
    return None


async def seed_messages(guild: discord.Guild) -> None:
    print("== Seed messages ==")
    sm = cfg["seed_messages"]

    rules_ch = channel_by_key(guild, cfg, "rules")
    if rules_ch:
        title = sm["rules_title"]
        embed = discord.Embed(title=title, colour=EMBED_COLOR,
                              description="\n\n".join(sm["rules"]))
        embed.set_footer(text=sm["rules_footer"])
        old = await _pinned_with_title(rules_ch, title)
        if old:
            await old.edit(embed=embed, view=VerifyButton())
            print("  = rules updated")
        else:
            msg = await rules_ch.send(embed=embed, view=VerifyButton())
            await msg.pin()
            print("  + rules posted")

    for key, title, text in (
        ("suggestions", "💡 Suggestions", sm["suggestions_intro"]),
        ("devblog", "🛠 Dev Blog", sm["devblog_intro"]),
    ):
        ch = channel_by_key(guild, cfg, key)
        if isinstance(ch, discord.TextChannel) and not await _pinned_with_title(ch, title):
            msg = await ch.send(embed=discord.Embed(
                title=title, description=text, colour=EMBED_COLOR))
            await msg.pin()
            print(f"  + intro in {ch.name}")


async def wipe(guild: discord.Guild) -> None:
    print("== WIPE: removing existing channels and roles ==")
    for ch in list(guild.channels):
        try:
            await ch.delete(reason="Sirius wipe")
        except discord.HTTPException:
            pass
    for role in list(guild.roles):
        if role.is_default() or role.managed or role >= guild.me.top_role:
            continue
        try:
            await role.delete(reason="Sirius wipe")
        except discord.HTTPException:
            pass


# --------------------------------------------------------------------------
async def build(client: discord.Client, args) -> None:
    guild = client.get_guild(get_guild_id())
    if guild is None:
        raise SystemExit("Bot is not on that guild (check GUILD_ID / bot invite).")
    if not guild.me.guild_permissions.administrator:
        raise SystemExit("Bot needs the Administrator permission for setup.")

    if args.wipe:
        await wipe(guild)
    await ensure_roles(guild)
    await ensure_channels(guild)
    await apply_guild_settings(guild)
    await ensure_channels(guild)          # second pass: forum/news once community is on
    if not args.skip_automod:
        await ensure_automod(guild)
    await seed_messages(guild)

    welcome = channel_by_key(guild, cfg, "welcome")
    if welcome:
        try:
            inv = await welcome.create_invite(max_age=0, max_uses=0,
                                              reason="Sirius setup")
            print(f"\nPermanent invite: {inv.url}")
        except discord.HTTPException:
            pass
    print("\n✅ Server build complete. Now run the bot:  python -m bot.main")


def main() -> None:
    parser = argparse.ArgumentParser(description="Build the Project Sirius server")
    parser.add_argument("--wipe", action="store_true",
                        help="delete ALL existing channels/roles first")
    parser.add_argument("--skip-automod", action="store_true")
    args = parser.parse_args()

    if args.wipe:
        answer = input("This will DELETE all channels and roles on the guild. "
                       "Type 'yes' to continue: ")
        if answer.strip().lower() != "yes":
            sys.exit("Aborted.")

    load_env()
    intents = discord.Intents.default()
    client = discord.Client(intents=intents)

    @client.event
    async def on_ready():
        try:
            print(f"Logged in as {client.user}")
            await build(client, args)
        except Exception as e:                     # noqa: BLE001
            print(f"ERROR: {e}")
        finally:
            await client.close()

    client.run(get_token())


if __name__ == "__main__":
    main()
