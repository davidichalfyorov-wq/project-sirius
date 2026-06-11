"""Community slash commands: suggestions, polls, info utilities."""
from __future__ import annotations

import discord
from discord import app_commands

from sirius_common import EMBED_COLOR, channel_by_key

from . import storage
from .moderation import is_staff

NUM_EMOJI = ["1️⃣", "2️⃣", "3️⃣", "4️⃣", "5️⃣"]
VERDICT_STYLE = {
    "approved": ("✅ Approved", 0x2ECC71),
    "denied": ("❌ Denied", 0xE74C3C),
    "considering": ("🤔 Under consideration", 0xF1C40F),
    "implemented": ("🚀 Implemented", 0x9B59B6),
}


def register(tree: app_commands.CommandTree, client: discord.Client,
             cfg: dict) -> None:

    # -- suggestions ------------------------------------------------------------
    @tree.command(name="suggest", description="Suggest an idea for Project Sirius")
    @app_commands.describe(idea="One idea per suggestion")
    async def suggest_cmd(interaction: discord.Interaction, idea: str):
        ch = channel_by_key(interaction.guild, cfg, "suggestions")
        if not isinstance(ch, discord.TextChannel):
            return await interaction.response.send_message(
                "Suggestions channel is missing.", ephemeral=True)
        n = storage.next_suggestion_number()
        embed = discord.Embed(
            title=f"💡 Suggestion #{n}", description=idea, colour=EMBED_COLOR)
        embed.set_author(name=str(interaction.user),
                         icon_url=interaction.user.display_avatar.url)
        embed.set_footer(text="Vote with the reactions below")
        msg = await ch.send(embed=embed)
        s = cfg["suggestions"]
        await msg.add_reaction(s["upvote"])
        await msg.add_reaction(s["downvote"])
        if s.get("create_thread"):
            try:
                await msg.create_thread(
                    name=f"Suggestion #{n} discussion"[:100])
            except discord.HTTPException:
                pass
        await interaction.response.send_message(
            f"Suggestion **#{n}** posted in {ch.mention}. o7", ephemeral=True)

    @tree.command(name="verdict",
                  description="Staff: set the verdict on a suggestion")
    @app_commands.default_permissions(manage_messages=True)
    @app_commands.describe(number="Suggestion number",
                           verdict="The decision",
                           comment="Optional staff comment")
    @app_commands.choices(verdict=[
        app_commands.Choice(name=v, value=v) for v in VERDICT_STYLE])
    async def verdict_cmd(interaction: discord.Interaction, number: int,
                          verdict: app_commands.Choice[str],
                          comment: str = ""):
        if not is_staff(cfg, interaction.user):
            return await interaction.response.send_message(
                "Station Police only.", ephemeral=True)
        ch = channel_by_key(interaction.guild, cfg, "suggestions")
        if not isinstance(ch, discord.TextChannel):
            return await interaction.response.send_message(
                "Suggestions channel is missing.", ephemeral=True)
        await interaction.response.defer(ephemeral=True)
        title = f"💡 Suggestion #{number}"
        target = None
        async for msg in ch.history(limit=400):
            if (msg.author == client.user and msg.embeds
                    and msg.embeds[0].title
                    and msg.embeds[0].title.startswith(title)):
                target = msg
                break
        if target is None:
            return await interaction.followup.send(
                f"Could not find suggestion #{number}.", ephemeral=True)
        label, color = VERDICT_STYLE[verdict.value]
        embed = target.embeds[0]
        embed.colour = color
        embed.title = f"💡 Suggestion #{number} — {label}"
        # replace any previous verdict field
        for i, f in enumerate(embed.fields):
            if f.name.startswith("Verdict"):
                embed.remove_field(i)
                break
        embed.add_field(
            name=f"Verdict by {interaction.user.display_name}",
            value=f"{label}" + (f" — {comment}" if comment else ""),
            inline=False)
        await target.edit(embed=embed)
        await interaction.followup.send(
            f"Verdict set on #{number}: {label}", ephemeral=True)

    # -- polls -------------------------------------------------------------------
    @tree.command(name="poll", description="Run a poll (up to 5 options)")
    @app_commands.describe(question="What to ask",
                           option1="Option 1", option2="Option 2",
                           option3="Option 3", option4="Option 4",
                           option5="Option 5")
    async def poll_cmd(interaction: discord.Interaction, question: str,
                       option1: str, option2: str, option3: str = "",
                       option4: str = "", option5: str = ""):
        options = [o for o in (option1, option2, option3, option4, option5) if o]
        lines = [f"{NUM_EMOJI[i]} {o}" for i, o in enumerate(options)]
        embed = discord.Embed(title=f"📊 {question}",
                              description="\n".join(lines),
                              colour=EMBED_COLOR)
        embed.set_footer(text=f"Poll by {interaction.user.display_name}")
        await interaction.response.send_message(embed=embed)
        msg = await interaction.original_response()
        for i in range(len(options)):
            await msg.add_reaction(NUM_EMOJI[i])

    # -- info utilities -------------------------------------------------------------
    @tree.command(name="ping", description="Bot latency")
    async def ping_cmd(interaction: discord.Interaction):
        await interaction.response.send_message(
            f"🏓 Pong! Gateway: {client.latency * 1000:.0f} ms",
            ephemeral=True)

    @tree.command(name="serverinfo", description="About this server")
    async def serverinfo_cmd(interaction: discord.Interaction):
        g = interaction.guild
        embed = discord.Embed(title=g.name, description=g.description or "",
                              colour=EMBED_COLOR)
        if g.icon:
            embed.set_thumbnail(url=g.icon.url)
        embed.add_field(name="Pilots", value=str(g.member_count))
        embed.add_field(name="Channels",
                        value=f"{len(g.text_channels)} text / "
                              f"{len(g.voice_channels)} voice")
        embed.add_field(name="Roles", value=str(len(g.roles)))
        embed.add_field(name="Founded",
                        value=f"<t:{int(g.created_at.timestamp())}:D>")
        embed.add_field(name="Boost", value=f"Level {g.premium_tier}")
        await interaction.response.send_message(embed=embed)

    @tree.command(name="userinfo", description="About a member")
    async def userinfo_cmd(interaction: discord.Interaction,
                           member: discord.Member | None = None):
        m = member or interaction.user
        embed = discord.Embed(title=str(m), colour=m.colour.value or EMBED_COLOR)
        embed.set_thumbnail(url=m.display_avatar.url)
        embed.add_field(name="Account created",
                        value=f"<t:{int(m.created_at.timestamp())}:R>")
        if m.joined_at:
            embed.add_field(name="Docked here",
                            value=f"<t:{int(m.joined_at.timestamp())}:R>")
        roles = [r.mention for r in reversed(m.roles) if not r.is_default()]
        embed.add_field(name="Roles",
                        value=" ".join(roles[:15]) or "*none*", inline=False)
        await interaction.response.send_message(embed=embed, ephemeral=True)

    @tree.command(name="avatar", description="Show a member's avatar")
    async def avatar_cmd(interaction: discord.Interaction,
                         member: discord.Member | None = None):
        m = member or interaction.user
        embed = discord.Embed(title=f"Avatar — {m.display_name}",
                              colour=EMBED_COLOR)
        embed.set_image(url=m.display_avatar.url)
        await interaction.response.send_message(embed=embed, ephemeral=True)

    @tree.command(name="say", description="Staff: make the bot say something")
    @app_commands.default_permissions(manage_messages=True)
    async def say_cmd(interaction: discord.Interaction, text: str):
        if not is_staff(cfg, interaction.user):
            return await interaction.response.send_message(
                "Station Police only.", ephemeral=True)
        await interaction.channel.send(text)
        await interaction.response.send_message("Sent. o7", ephemeral=True)
