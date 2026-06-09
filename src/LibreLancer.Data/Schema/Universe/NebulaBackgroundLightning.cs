// MIT License - Copyright (c) Callum McGing
// This file is subject to the terms and conditions defined in
// LICENSE, which is part of this source code package

using LibreLancer.Data.Ini;

namespace LibreLancer.Data.Schema.Universe;

[ParsedSection]
public partial class NebulaBackgroundLightning
{
    public float Duration;
    public float Gap;

    [EntryHandler("duration", Multiline = true)]
    private void HandleDuration(Entry e) => Duration = e.Count > 0 ? e[0].ToSingle() : 0;

    [EntryHandler("gap", Multiline = true)]
    private void HandleGap(Entry e) => Gap = e.Count > 0 ? e[0].ToSingle() : 0;
    [Entry("color")]
    public Color4 Color;
}