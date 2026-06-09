// MIT License - Copyright (c) Malte Rupprecht
// This file is subject to the terms and conditions defined in
// LICENSE, which is part of this source code package

using System;
using System.Numerics;
using LibreLancer.Data.Ini;

namespace LibreLancer.Data.Schema.Universe;

[ParsedSection]
public partial class Band
{
    [Entry("zone")]
    public string? Zone; // Unsure what this does, may refer to whole field when null
    [Entry("render_parts")]
    public int? RenderParts;
    [Entry("shape")]
    public string? Shape;
    [Entry("height")]
    public int? Height;
    [Entry("offset_dist")]
    public int? OffsetDist;
    [Entry("fade")]
    public float[]? Fade;
    public float? TextureAspect;

    [EntryHandler("texture_aspect", Multiline = true)]
    private void HandleTextureAspect(Entry e) => TextureAspect = e.Count > 0 ? e[0].ToSingle() : null;
    [Entry("color_shift")]
    public Vector3? ColorShift;
    [Entry("ambient_intensity")]
    public float? AmbientIntensity;
    [Entry("cull_mode")]
    public int? CullMode;
    [Entry("vert_increase")]
    public int? VertIncrease;
}
