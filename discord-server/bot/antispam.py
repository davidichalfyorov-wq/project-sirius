"""Anti-spam, strike escalation and raid protection for Sirius Bot."""
from __future__ import annotations

import re
import time
from collections import defaultdict, deque
from datetime import timedelta

import discord

from sirius_common import channel_by_key, role_by_key

INVITE_RE = re.compile(
    r"(?:discord(?:app)?\.com/invite|discord\.gg)/[\w-]+", re.IGNORECASE)
CUSTOM_EMOJI_RE = re.compile(r"<a?:\w+:\d+>")
UNICODE_EMOJI_RE = re.compile(
    "[\U0001F000-\U0001FAFF\U00002600-\U000027BF\U0001F1E6-\U0001F1FF\U00002190-\U000021FF\U00002B00-\U00002BFF]")


class AntiSpam:
    def __init__(self, cfg: dict):
        self.cfg = cfg
        self.a = cfg["antispam"]
        # per-user sliding windows
        self.messages: dict[int, deque] = defaultdict(deque)      # ts
        self.contents: dict[int, deque] = defaultdict(deque)      # (ts, norm)
        self.strikes: dict[int, deque] = defaultdict(deque)       # ts
        # raid tracking
        self.joins: deque = deque()                               # ts
        self.raid_until: float = 0.0
        self.raid_reason: str = ""

    # -- helpers -------------------------------------------------------------
    def is_exempt(self, member: discord.Member) -> bool:
        if member.bot:
            return True
        names = {self.cfg["role_keys"][k]
                 for k in self.a["exempt_role_keys"]
                 if k in self.cfg["role_keys"]}
        return any(r.name in names for r in member.roles)

    @property
    def raid_active(self) -> bool:
        return time.time() < self.raid_until

    def arm_raid(self, seconds: int, reason: str) -> None:
        self.raid_until = time.time() + seconds
        self.raid_reason = reason

    def disarm_raid(self) -> None:
        self.raid_until = 0.0
        self.raid_reason = ""

    # -- joins ----------------------------------------------------------------
    def register_join(self) -> bool:
        """Track a join; returns True if this join just triggered raid mode."""
        r = self.a["raid"]
        now = time.time()
        self.joins.append(now)
        while self.joins and now - self.joins[0] > r["join_window_seconds"]:
            self.joins.popleft()
        if not self.raid_active and len(self.joins) >= r["join_threshold"]:
            self.arm_raid(r["duration_seconds"],
                          f"{len(self.joins)} joins in {r['join_window_seconds']}s")
            return True
        return False

    # -- message checks --------------------------------------------------------
    def check_message(self, message: discord.Message) -> str | None:
        """Returns a violation label or None."""
        a = self.a
        now = time.time()
        uid = message.author.id
        content = message.content or ""

        msgs = self.messages[uid]
        msgs.append(now)
        while msgs and now - msgs[0] > a["message_window_seconds"]:
            msgs.popleft()
        if len(msgs) > a["max_messages_per_window"]:
            return "message flood"

        norm = " ".join(content.lower().split())
        if norm:
            cont = self.contents[uid]
            cont.append((now, norm))
            while cont and now - cont[0][0] > a["duplicate_window_seconds"]:
                cont.popleft()
            if sum(1 for _, c in cont if c == norm) >= a["max_duplicates"]:
                return "duplicate messages"

        if len(message.mentions) + len(message.role_mentions) > a["max_mentions"]:
            return "mention spam"

        lowered = content.lower()
        if a["block_invites"] and INVITE_RE.search(content):
            return "invite link"
        if any(d in lowered for d in a["scam_domains"]):
            return "scam link"

        letters = [c for c in content if c.isalpha()]
        if len(letters) >= a["caps_min_length"]:
            upper = sum(1 for c in letters if c.isupper())
            if upper / len(letters) >= a["caps_ratio"]:
                return "excessive caps"

        emoji_count = (len(CUSTOM_EMOJI_RE.findall(content))
                       + len(UNICODE_EMOJI_RE.findall(content)))
        if emoji_count > a["max_emojis"]:
            return "emoji flood"
        return None

    def add_strike(self, uid: int) -> tuple[int, str]:
        """Record a strike; returns (strike_no, action) per escalation ladder."""
        now = time.time()
        st = self.strikes[uid]
        st.append(now)
        while st and now - st[0] > self.a["strike_decay_seconds"]:
            st.popleft()
        ladder = self.a["escalation"]
        action = ladder[min(len(st), len(ladder)) - 1]
        return len(st), action


async def apply_action(member: discord.Member, action: str, label: str) -> str:
    """Apply an escalation action; returns human description."""
    reason = f"Sirius anti-spam: {label}"
    if action.startswith("timeout:"):
        seconds = int(action.split(":", 1)[1])
        await member.timeout(timedelta(seconds=seconds), reason=reason)
        return f"timed out for {seconds // 60} min"
    if action == "kick":
        await member.kick(reason=reason)
        return "kicked"
    if action == "ban":
        await member.ban(reason=reason, delete_message_days=0)
        return "banned"
    return "warned"


async def log_event(guild: discord.Guild, cfg: dict, title: str,
                    description: str, color: int = 0xE67E22) -> None:
    ch = channel_by_key(guild, cfg, "botlog")
    if isinstance(ch, discord.TextChannel):
        try:
            await ch.send(embed=discord.Embed(
                title=title, description=description, colour=color))
        except discord.HTTPException:
            pass


async def handle_message(client: discord.Client, antispam: AntiSpam,
                         message: discord.Message) -> None:
    if (message.guild is None or message.author.bot
            or not isinstance(message.author, discord.Member)):
        return
    if antispam.is_exempt(message.author):
        return
    label = antispam.check_message(message)
    if label is None:
        return

    try:
        await message.delete()
    except discord.HTTPException:
        pass

    strike_no, action = antispam.add_strike(message.author.id)
    try:
        outcome = await apply_action(message.author, action, label)
    except discord.HTTPException as e:
        outcome = f"action failed: {e}"

    await log_event(
        message.guild, antispam.cfg, "🚨 Anti-spam",
        f"{message.author.mention} — **{label}** (strike {strike_no}) → {outcome}\n"
        f"Channel: {message.channel.mention}\n"
        f"Content: {discord.utils.escape_markdown((message.content or '')[:300])}")

    if action == "warn":
        try:
            notice = await message.channel.send(
                f"{message.author.mention} ⚠️ {label} — watch it, pilot. "
                "Next stop is a timeout.")
            await notice.delete(delay=10)
        except discord.HTTPException:
            pass


async def handle_join(client: discord.Client, antispam: AntiSpam,
                      member: discord.Member) -> bool:
    """Raid screening. Returns True if the member was kicked."""
    cfg = antispam.cfg
    r = antispam.a["raid"]
    triggered = antispam.register_join()
    if triggered:
        staff = channel_by_key(member.guild, cfg, "staff")
        alert = (f"⚠️ **Raid mode armed** — {antispam.raid_reason}. "
                 f"Accounts younger than {r['min_account_age_hours']}h are "
                 f"turned away for {r['duration_seconds'] // 60} min.")
        for ch in (staff,):
            if isinstance(ch, discord.TextChannel):
                try:
                    await ch.send(alert)
                except discord.HTTPException:
                    pass
        await log_event(member.guild, cfg, "🛡️ Raid mode", alert, 0xE74C3C)

    if antispam.raid_active:
        age_h = (discord.utils.utcnow() - member.created_at).total_seconds() / 3600
        if age_h < r["min_account_age_hours"]:
            try:
                await member.send(
                    "The station is in lockdown due to a raid. Your account is "
                    "too new to dock right now — try again in a little while.")
            except discord.HTTPException:
                pass
            try:
                await member.kick(reason="Sirius raid mode: account too new")
                await log_event(member.guild, cfg, "🛡️ Raid mode",
                                f"Turned away {member} (account {age_h:.1f}h old)",
                                0xE74C3C)
                return True
            except discord.HTTPException:
                pass
    return False
