"""Moderation slash commands and the mod-log feed."""
from __future__ import annotations

import time
from datetime import timedelta

import discord
from discord import app_commands

from sirius_common import EMBED_COLOR, channel_by_key, role_by_key

from . import storage

MOD_COLOR = 0x3498DB
WARN_COLOR = 0xE67E22
KICKBAN_COLOR = 0xE74C3C


def is_staff(cfg: dict, member: discord.Member) -> bool:
    if member.guild_permissions.administrator:
        return True
    names = {cfg["role_keys"]["admin"], cfg["role_keys"]["moderator"]}
    return any(r.name in names for r in member.roles)


async def modlog(guild: discord.Guild, cfg: dict, title: str,
                 description: str, color: int = MOD_COLOR) -> None:
    ch = channel_by_key(guild, cfg, "modlog")
    if isinstance(ch, discord.TextChannel):
        try:
            await ch.send(embed=discord.Embed(
                title=title, description=description, colour=color,
                timestamp=discord.utils.utcnow()))
        except discord.HTTPException:
            pass


def _members_text_channels(guild: discord.Guild, cfg: dict):
    """Text channels of the member-access categories (lockdown scope)."""
    cat_names = {c["name"] for c in cfg["categories"]
                 if c["access"] == "members"}
    for ch in guild.text_channels:
        if ch.category and ch.category.name in cat_names:
            yield ch


def register(tree: app_commands.CommandTree, client: discord.Client,
             cfg: dict, antispam) -> None:

    def staff_check(interaction: discord.Interaction) -> bool:
        return (isinstance(interaction.user, discord.Member)
                and is_staff(cfg, interaction.user))

    # -- /warn ----------------------------------------------------------------
    warn_group = app_commands.Group(
        name="warn", description="Manage warnings",
        default_permissions=discord.Permissions(moderate_members=True))

    @warn_group.command(name="add", description="Warn a member")
    @app_commands.describe(member="Who to warn", reason="Why")
    async def warn_add(interaction: discord.Interaction,
                       member: discord.Member, reason: str):
        if not staff_check(interaction):
            return await interaction.response.send_message(
                "Station Police only.", ephemeral=True)
        count = storage.add_warn(interaction.guild_id, member.id,
                                 str(interaction.user), reason, time.time())
        try:
            await member.send(f"⚠️ You received a warning on "
                              f"**{interaction.guild.name}**: {reason} "
                              f"(warning #{count})")
        except discord.HTTPException:
            pass
        await modlog(interaction.guild, cfg, "⚠️ Warn",
                     f"{member.mention} warned by {interaction.user.mention} "
                     f"(#{count}): {reason}", WARN_COLOR)
        await interaction.response.send_message(
            f"⚠️ {member.mention} warned (#{count}): {reason}", ephemeral=True)

    @warn_group.command(name="list", description="List a member's warnings")
    async def warn_list(interaction: discord.Interaction,
                        member: discord.Member):
        if not staff_check(interaction):
            return await interaction.response.send_message(
                "Station Police only.", ephemeral=True)
        warns = storage.get_warns(interaction.guild_id, member.id)
        if not warns:
            return await interaction.response.send_message(
                f"{member.mention} has a clean record.", ephemeral=True)
        lines = [f"`{i+1}.` <t:{int(w['ts'])}:R> by {w['by']}: {w['reason']}"
                 for i, w in enumerate(warns[-15:])]
        await interaction.response.send_message(
            embed=discord.Embed(title=f"Warnings — {member}",
                                description="\n".join(lines),
                                colour=WARN_COLOR),
            ephemeral=True)

    @warn_group.command(name="clear", description="Clear a member's warnings")
    async def warn_clear(interaction: discord.Interaction,
                         member: discord.Member):
        if not staff_check(interaction):
            return await interaction.response.send_message(
                "Station Police only.", ephemeral=True)
        n = storage.clear_warns(interaction.guild_id, member.id)
        await modlog(interaction.guild, cfg, "⚠️ Warns cleared",
                     f"{interaction.user.mention} cleared {n} warning(s) "
                     f"of {member.mention}", WARN_COLOR)
        await interaction.response.send_message(
            f"Cleared {n} warning(s) for {member.mention}.", ephemeral=True)

    tree.add_command(warn_group)

    # -- timeouts / kick / ban -------------------------------------------------
    @tree.command(name="timeout", description="Timeout a member")
    @app_commands.default_permissions(moderate_members=True)
    @app_commands.describe(member="Who", minutes="For how many minutes",
                           reason="Why")
    async def timeout_cmd(interaction: discord.Interaction,
                          member: discord.Member,
                          minutes: app_commands.Range[int, 1, 40320],
                          reason: str = "No reason given"):
        if not staff_check(interaction):
            return await interaction.response.send_message(
                "Station Police only.", ephemeral=True)
        await member.timeout(timedelta(minutes=minutes), reason=reason)
        await modlog(interaction.guild, cfg, "⏲️ Timeout",
                     f"{member.mention} → {minutes} min by "
                     f"{interaction.user.mention}: {reason}")
        await interaction.response.send_message(
            f"⏲️ {member.mention} timed out for {minutes} min.", ephemeral=True)

    @tree.command(name="untimeout", description="Lift a member's timeout")
    @app_commands.default_permissions(moderate_members=True)
    async def untimeout_cmd(interaction: discord.Interaction,
                            member: discord.Member):
        if not staff_check(interaction):
            return await interaction.response.send_message(
                "Station Police only.", ephemeral=True)
        await member.timeout(None, reason=f"Lifted by {interaction.user}")
        await modlog(interaction.guild, cfg, "⏲️ Timeout lifted",
                     f"{member.mention} by {interaction.user.mention}")
        await interaction.response.send_message(
            f"{member.mention} can speak again.", ephemeral=True)

    @tree.command(name="kick", description="Kick a member")
    @app_commands.default_permissions(kick_members=True)
    async def kick_cmd(interaction: discord.Interaction,
                       member: discord.Member,
                       reason: str = "No reason given"):
        if not staff_check(interaction):
            return await interaction.response.send_message(
                "Station Police only.", ephemeral=True)
        await member.kick(reason=f"{interaction.user}: {reason}")
        await modlog(interaction.guild, cfg, "👢 Kick",
                     f"{member} by {interaction.user.mention}: {reason}",
                     KICKBAN_COLOR)
        await interaction.response.send_message(
            f"👢 {member} kicked.", ephemeral=True)

    @tree.command(name="ban", description="Ban a user")
    @app_commands.default_permissions(ban_members=True)
    @app_commands.describe(user="Who", reason="Why",
                           delete_hours="Delete their messages from the last N hours")
    async def ban_cmd(interaction: discord.Interaction, user: discord.User,
                      reason: str = "No reason given",
                      delete_hours: app_commands.Range[int, 0, 168] = 0):
        if not staff_check(interaction):
            return await interaction.response.send_message(
                "Station Police only.", ephemeral=True)
        await interaction.guild.ban(
            user, reason=f"{interaction.user}: {reason}",
            delete_message_seconds=delete_hours * 3600)
        await modlog(interaction.guild, cfg, "🔨 Ban",
                     f"{user} by {interaction.user.mention}: {reason}",
                     KICKBAN_COLOR)
        await interaction.response.send_message(
            f"🔨 {user} banned.", ephemeral=True)

    @tree.command(name="unban", description="Unban a user by ID")
    @app_commands.default_permissions(ban_members=True)
    async def unban_cmd(interaction: discord.Interaction, user_id: str):
        if not staff_check(interaction):
            return await interaction.response.send_message(
                "Station Police only.", ephemeral=True)
        try:
            user = await client.fetch_user(int(user_id))
        except (ValueError, discord.NotFound):
            return await interaction.response.send_message(
                "Unknown user ID.", ephemeral=True)
        await interaction.guild.unban(user, reason=str(interaction.user))
        await modlog(interaction.guild, cfg, "🔓 Unban",
                     f"{user} by {interaction.user.mention}")
        await interaction.response.send_message(
            f"🔓 {user} unbanned.", ephemeral=True)

    # -- channel tools -----------------------------------------------------------
    @tree.command(name="purge", description="Delete the last N messages here")
    @app_commands.default_permissions(manage_messages=True)
    async def purge_cmd(interaction: discord.Interaction,
                        count: app_commands.Range[int, 1, 200]):
        if not staff_check(interaction):
            return await interaction.response.send_message(
                "Station Police only.", ephemeral=True)
        await interaction.response.defer(ephemeral=True)
        deleted = await interaction.channel.purge(limit=count)
        await modlog(interaction.guild, cfg, "🧹 Purge",
                     f"{len(deleted)} messages in {interaction.channel.mention} "
                     f"by {interaction.user.mention}")
        await interaction.followup.send(
            f"🧹 Deleted {len(deleted)} messages.", ephemeral=True)

    @tree.command(name="slowmode", description="Set slowmode in this channel")
    @app_commands.default_permissions(manage_channels=True)
    @app_commands.describe(seconds="0 disables slowmode")
    async def slowmode_cmd(interaction: discord.Interaction,
                           seconds: app_commands.Range[int, 0, 21600]):
        if not staff_check(interaction):
            return await interaction.response.send_message(
                "Station Police only.", ephemeral=True)
        await interaction.channel.edit(slowmode_delay=seconds)
        await interaction.response.send_message(
            f"🐌 Slowmode: {seconds}s.", ephemeral=True)

    @tree.command(name="lockdown",
                  description="Lock or unlock all community channels")
    @app_commands.default_permissions(manage_channels=True)
    @app_commands.describe(state="on = nobody can post, off = back to normal")
    @app_commands.choices(state=[
        app_commands.Choice(name="on", value="on"),
        app_commands.Choice(name="off", value="off")])
    async def lockdown_cmd(interaction: discord.Interaction,
                           state: app_commands.Choice[str]):
        if not staff_check(interaction):
            return await interaction.response.send_message(
                "Station Police only.", ephemeral=True)
        await interaction.response.defer(ephemeral=True)
        pilot = role_by_key(interaction.guild, cfg, "verified")
        locked = state.value == "on"
        n = 0
        for ch in _members_text_channels(interaction.guild, cfg):
            ow = ch.overwrites_for(pilot)
            ow.send_messages = False if locked else None
            await ch.set_permissions(pilot, overwrite=ow,
                                     reason=f"Lockdown {state.value}")
            n += 1
        word = "🔒 Lockdown enabled" if locked else "🔓 Lockdown lifted"
        await modlog(interaction.guild, cfg, word,
                     f"{n} channels by {interaction.user.mention}",
                     KICKBAN_COLOR if locked else MOD_COLOR)
        await interaction.followup.send(f"{word} ({n} channels).",
                                        ephemeral=True)

    @tree.command(name="raidmode", description="Manually arm/disarm raid mode")
    @app_commands.default_permissions(moderate_members=True)
    @app_commands.choices(state=[
        app_commands.Choice(name="on", value="on"),
        app_commands.Choice(name="off", value="off")])
    async def raidmode_cmd(interaction: discord.Interaction,
                           state: app_commands.Choice[str]):
        if not staff_check(interaction):
            return await interaction.response.send_message(
                "Station Police only.", ephemeral=True)
        if state.value == "on":
            antispam.arm_raid(antispam.a["raid"]["duration_seconds"],
                              f"manual ({interaction.user})")
            msg = "🛡️ Raid mode armed manually."
        else:
            antispam.disarm_raid()
            msg = "🛡️ Raid mode disarmed."
        await modlog(interaction.guild, cfg, "🛡️ Raid mode", msg)
        await interaction.response.send_message(msg, ephemeral=True)

    # -- mod-log passive feed -----------------------------------------------------
    @client.event
    async def on_message_delete(message: discord.Message):
        if message.guild is None or message.author.bot:
            return
        await modlog(message.guild, cfg, "🗑️ Message deleted",
                     f"**{message.author}** in {message.channel.mention}:\n"
                     f"{(message.content or '*<no text>*')[:800]}")

    @client.event
    async def on_message_edit(before: discord.Message, after: discord.Message):
        if (before.guild is None or before.author.bot
                or before.content == after.content):
            return
        await modlog(before.guild, cfg, "✏️ Message edited",
                     f"**{before.author}** in {before.channel.mention}\n"
                     f"**Before:** {(before.content or '')[:400]}\n"
                     f"**After:** {(after.content or '')[:400]}")
