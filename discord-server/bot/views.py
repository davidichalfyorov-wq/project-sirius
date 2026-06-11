"""Persistent UI views: the 🚀 Launch verification button."""
from __future__ import annotations

import discord

from sirius_common import VERIFY_CUSTOM_ID, role_by_key


class VerifyView(discord.ui.View):
    """Persistent view bound to the button posted by setup_server.py."""

    def __init__(self, cfg: dict):
        super().__init__(timeout=None)
        self.cfg = cfg
        button = discord.ui.Button(
            style=discord.ButtonStyle.success,
            label=cfg["seed_messages"]["verify_button_label"],
            custom_id=VERIFY_CUSTOM_ID)
        button.callback = self.verify
        self.add_item(button)

    async def verify(self, interaction: discord.Interaction) -> None:
        member = interaction.user
        guild = interaction.guild
        if guild is None or not isinstance(member, discord.Member):
            return
        pilot = role_by_key(guild, self.cfg, "verified")
        docked = role_by_key(guild, self.cfg, "unverified")
        if pilot is None:
            await interaction.response.send_message(
                "Verification role is missing — ping the staff.", ephemeral=True)
            return
        if pilot in member.roles:
            await interaction.response.send_message(
                "You are already cleared for launch, pilot. o7", ephemeral=True)
            return

        roles = [r for r in member.roles if r != docked] + [pilot]
        await member.edit(roles=roles, reason="Sirius verification")
        await interaction.response.send_message(
            self.cfg["welcome"]["dm_on_verify"], ephemeral=True)
        try:
            await member.send(self.cfg["welcome"]["dm_on_verify"])
        except discord.HTTPException:
            pass
