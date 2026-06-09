// MIT License - Copyright (c) Malte Rupprecht
// This file is subject to the terms and conditions defined in
// LICENSE, which is part of this source code package

using LibreLancer.Data.Ini;

namespace LibreLancer.Data.Schema.Universe;

[ParsedSection]
public partial class NebulaExclusion
{
    [Entry("exclude")]
    [Entry("exclusion")]
    public string? ZoneName;
    public float? FogFar;
    [Entry("fog_near")]
    public float? FogNear;
    public float? ShellScalar;
    [Entry("max_alpha")]
    public float? MaxAlpha;

    [EntryHandler("fog_far", Multiline = true)]
    private void HandleFogFar(Entry e) => FogFar = e.Count > 0 ? e[0].ToSingle() : null;

    [EntryHandler("shell_scalar", Multiline = true)]
    private void HandleShellScalar(Entry e) => ShellScalar = e.Count > 0 ? e[0].ToSingle() : null;
    [Entry("color")]
    public Color4? Color;
    [Entry("exclusion_tint")]
    public Color3f? Tint;
    [Entry("zone_shell")]
    public string? ZoneShellPath;
}
