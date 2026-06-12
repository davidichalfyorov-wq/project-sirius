// MIT License - Copyright (c) Callum McGing
// This file is subject to the terms and conditions defined in
// LICENSE, which is part of this source code package

using System;
using System.Numerics;
using System.Xml.Schema;
using LibreLancer;
using LibreLancer.Graphics;
using LibreLancer.Graphics.Text;
using WattleScript.Interpreter;

namespace LibreLancer.Interface
{
    [UiLoadable]
    [WattleScriptUserData]
    public class Button : UiWidget
    {
        public bool Selected { get; set; }
        public string? Style { get; set; }
        public float TextSize { get; set; }
        public string? FontFamily { get; set; }

        public bool DrawText { get; set; } = true;

        public float MarginLeft { get; set; }

        public float MarginRight { get; set; }

        private InfoTextAccessor txtAccess = new();

        public string? Text
        {
            get => txtAccess.Text;
            set => txtAccess.Text = value;
        }

        public int Strid
        {
            get => txtAccess.Strid;
            set => txtAccess.Strid = value;
        }

        public int InfoId
        {
            get => txtAccess.InfoId;
            set => txtAccess.InfoId = value;
        }

        public string? MouseEnterSound { get; set; }
        public string? MouseDownSound { get; set; }
        public HorizontalAlignment HorizontalAlignment { get; set; }
        public VerticalAlignment VerticalAlignment { get; set; }
        public InterfaceColor? TextColor { get; set; }
        public InterfaceColor? TextShadow { get; set; }

        public bool DebugTextFrame { get; set; }

        private ButtonStyle? style;
        private bool styleSetManual = false;

        public void SetStyle(ButtonStyle? style)
        {
            this.style = style;
            styleSetManual = true;
        }

        private bool lastFrameMouseInside = false;
        private string GetText(UiContext context) => txtAccess.GetText(context);

        private CachedRenderString? textCache;

        internal void Draw(UiContext context, DrawList2D drawList, RectangleF myRectangle, bool hover, bool pressed, bool selected,
            bool enabled)
        {
            ButtonAppearance? activeStyle = null;
            if (selected) activeStyle = style!.Selected;
            if (hover) activeStyle = style?.Hover;
            if (pressed) activeStyle = style?.Pressed ?? style?.Hover;
            if (!enabled) activeStyle = style?.Disabled;
            var bk = Cascade(style?.Normal?.Background, activeStyle?.Background, Background);
            bk?.Draw(context, drawList, myRectangle);
            var border = Cascade(style?.Normal?.Border, activeStyle?.Border, Border);
            border?.Draw(context, drawList, myRectangle);
        }

        internal void Update(UiContext context, RectangleF parentRectangle)
        {
            var myRectangle = GetMyRectangle(context, parentRectangle);

            if (myRectangle.Contains(context.MouseX, context.MouseY))
            {
                Hovered = true;

                if (!lastFrameMouseInside)
                {
                    var sound = MouseEnterSound ?? style?.MouseEnterSound;

                    if (!string.IsNullOrWhiteSpace(sound))
                    {
                        context.PlaySound(sound);
                    }
                }

                lastFrameMouseInside = true;
            }
            else
            {
                Hovered = false;
                lastFrameMouseInside = false;
            }

            if (Dragging)
            {
                DragOffset = new Vector2(context.MouseX, context.MouseY) - DragStart;
            }

            if (HeldDown)
            {
                if (!myRectangle.Contains(context.MouseX, context.MouseY))
                {
                    HeldDown = false;
                }
            }
        }

        public bool Hovered { get; set; }

        // Smooth state transitions: hover crossfades over ~0.12s, presses
        // pop the button down over ~0.06s with a spring back on release.
        private float hoverAmount;
        private float pressAmount;
        private const float HoverFadeTime = 0.12f;
        private const float PressPopTime = 0.06f;
        private const float PressPopScale = 0.962f;
        private InterfaceColor? blendedText;
        private InterfaceColor? blendedShadow;

        private static float MoveToward(float current, float target, float maxDelta)
        {
            if (current < target)
                return MathF.Min(target, current + maxDelta);
            return MathF.Max(target, current - maxDelta);
        }

        private InterfaceColor BlendColor(ref InterfaceColor? cache, InterfaceColor? from, InterfaceColor? to,
            float amount, double time)
        {
            var a = from?.GetColor(time) ?? Color4.White;
            var b = to?.GetColor(time) ?? a;
            cache ??= new InterfaceColor();
            cache.Color = Color4.Lerp(a, b, amount);
            return cache;
        }

        private static RectangleF ScaledAboutCenter(RectangleF rect, float scale)
        {
            var w = rect.Width * scale;
            var h = rect.Height * scale;
            return new RectangleF(
                rect.X + ((rect.Width - w) / 2f),
                rect.Y + ((rect.Height - h) / 2f),
                w, h);
        }

        public override void Render(UiContext context, DrawList2D drawList, RectangleF parentRectangle)
        {
            if (!Visible) return;
            Update(context, parentRectangle);
            ButtonAppearance? activeStyle = null;
            var myRectangle = GetMyRectangle(context, parentRectangle);

            string txt = GetText(context);

            bool mouseInside = myRectangle.Contains(context.MouseX, context.MouseY);
            if (mouseInside)
            {
                activeStyle = style?.Hover;
                if (!DrawText && !string.IsNullOrWhiteSpace(txt))
                {
                    context.SetTooltip(txt, myRectangle);
                }
            }

            if (HeldDown)
            {
                activeStyle = style?.Pressed ?? style?.Hover;
            }
            else if (Hovered && Strid != 0)
            {
                context.SetRollover(Strid);
            }

            var dt = (float)context.DeltaTime;
            hoverAmount = MoveToward(hoverAmount, mouseInside || HeldDown ? 1f : 0f, dt / HoverFadeTime);
            pressAmount = MoveToward(pressAmount, HeldDown ? 1f : 0f, dt / PressPopTime);

            bool instantState = Selected || !Enabled;
            if (Selected) activeStyle = style?.Selected;
            if (!Enabled) activeStyle = style?.Disabled;

            // Press pop: shrink everything slightly around the center.
            if (pressAmount > 0)
                myRectangle = ScaledAboutCenter(myRectangle, MathHelper.Lerp(1f, PressPopScale, pressAmount));

            float stateBlend = instantState ? 1f : MathF.Max(hoverAmount, pressAmount);
            if (Background != null || instantState || activeStyle?.Background == null || stateBlend >= 1f)
            {
                // Widget override, checked/disabled states and the settled
                // end-state draw exactly as before.
                var bk = Cascade(style?.Normal?.Background, activeStyle?.Background, Background);
                bk?.Draw(context, drawList, myRectangle);
            }
            else
            {
                // Crossfade: normal appearance below, active fading in above.
                style?.Normal?.Background?.Draw(context, drawList, myRectangle);
                if (stateBlend > 0)
                    activeStyle.Background.Draw(context, drawList, myRectangle, stateBlend);
            }

            float mLeft = Cascade(style?.Normal?.MarginLeft, activeStyle?.MarginLeft, MarginLeft);
            float mRight = Cascade(style?.Normal?.MarginRight, activeStyle?.MarginRight, MarginRight);

            if (DrawText && !string.IsNullOrWhiteSpace(txt))
            {
                var textRect = myRectangle;
                textRect.X += mLeft;
                textRect.Width -= mRight;

                if (DebugTextFrame)
                {
                    drawList.DrawRectangle(context.PointsToPixels(textRect), Color4.Aqua, 1);
                }

                var normalText = Cascade(style?.Normal?.TextColor, null, TextColor);
                var activeText = Cascade(style?.Normal?.TextColor, activeStyle?.TextColor, TextColor);
                var normalShadow = Cascade(style?.Normal?.TextShadow, null, TextShadow);
                var activeShadow = Cascade(style?.Normal?.TextShadow, activeStyle?.TextShadow, TextShadow);
                var textColor = instantState
                    ? activeText
                    : BlendColor(ref blendedText, normalText, activeText, stateBlend, context.GlobalTime);
                var shadowColor = instantState
                    ? activeShadow
                    : BlendColor(ref blendedShadow, normalShadow, activeShadow, stateBlend, context.GlobalTime);

                RenderText(
                    context,
                    drawList,
                    ref textCache,
                    textRect,
                    Cascade(style?.Normal?.TextSize, activeStyle?.TextSize, TextSize),
                    Cascade(style?.Normal?.FontFamily, activeStyle?.FontFamily, FontFamily),
                    textColor,
                    shadowColor,
                    Cascade(style?.Normal?.HorizontalAlignment, activeStyle?.HorizontalAlignment, HorizontalAlignment),
                    Cascade(style?.Normal?.VerticalAlignment, activeStyle?.VerticalAlignment, VerticalAlignment),
                    true,
                    txt
                );
            }

            var border = Cascade(style?.Normal?.Border, activeStyle?.Border, Border);
            border?.Draw(context, drawList, myRectangle);
        }

        private RectangleF GetMyRectangle(UiContext context, RectangleF parentRectangle)
        {
            var width = Cascade(style?.Width, null, Width);
            var height = Cascade(style?.Height, null, Height);
            var myPos = context.AnchorPosition(parentRectangle, Anchor, X, Y, width, height);
            Update(context, myPos);
            myPos = AnimatedPosition(myPos);
            var myRect = new RectangleF(myPos.X, myPos.Y, width, height);
            return myRect;
        }

        public bool HeldDown;
        public bool Dragging;
        public Vector2 DragStart;
        public Vector2 DragOffset;

        public override void OnMouseDown(UiContext context, RectangleF parentRectangle)
        {
            if (!Visible) return;
            if (CurrentAnimation != null) return;
            var myRect = GetMyRectangle(context, parentRectangle);

            if (myRect.Contains(context.MouseX, context.MouseY))
            {
                // While we don't have better cascade
                var sound = MouseDownSound ?? style?.MouseDownSound ?? "ui_select_item";

                if (!string.IsNullOrWhiteSpace(sound))
                {
                    context.PlaySound(sound);
                }

                HeldDown = true;
                Dragging = true;
                DragStart = new Vector2(context.MouseX, context.MouseY);
            }
        }

        public override void OnMouseUp(UiContext context, RectangleF parentRectangle)
        {
            if (!Visible) return;
            Dragging = false;
            HeldDown = false;
            DragStart = DragOffset = Vector2.Zero;
        }

        private event Action? Clicked;

        public void OnClick(WattleScript.Interpreter.Closure handler)
        {
            Clicked += () => { handler.Call(); };
        }

        public void ClearClick()
        {
            Clicked = null;
        }

        public override void OnMouseClick(UiContext context, RectangleF parentRectangle)
        {
            if (!Visible || !Enabled) return;
            if (CurrentAnimation != null) return;
            var myRect = GetMyRectangle(context, parentRectangle);

            if (myRect.Contains(context.MouseX, context.MouseY))
            {
                Clicked?.Invoke();
            }
        }

        public override Vector2 GetDimensions()
        {
            var width = Cascade(style?.Width, null, Width);
            var height = Cascade(style?.Height, null, Height);
            return new Vector2(width, height);
        }

        public override void ApplyStylesheet(Stylesheet sheet)
        {
            base.ApplyStylesheet(sheet);
            if (!styleSetManual) style = sheet.Lookup<ButtonStyle>(Style)!;
        }
    }
}
