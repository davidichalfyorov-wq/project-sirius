// MIT License - Copyright (c) Malte Rupprecht
// This file is subject to the terms and conditions defined in
// LICENSE, which is part of this source code package

using System;
using LibreLancer.Data.Ini;

namespace LibreLancer.Data.Schema.Universe;

[ParsedSection]
public partial class NebulaLight
{
    [Entry("ambient")]
    public Color4? Ambient;
    public float? SunBurnthroughIntensity;
    public float? SunBurnthroughScaler;

    [EntryHandler("sun_burnthrough_intensity", Multiline = true)]
    private void HandleSunBurnthroughIntensity(Entry e) => SunBurnthroughIntensity = e.Count > 0 ? e[0].ToSingle() : null;

    [EntryHandler("sun_burnthrough_scaler", Multiline = true)]
    private void HandleSunBurnthroughScaler(Entry e) => SunBurnthroughScaler = e.Count > 0 ? e[0].ToSingle() : null;
}