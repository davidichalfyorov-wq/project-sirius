// MIT License - Copyright (c) Callum McGing
// This file is subject to the terms and conditions defined in
// LICENSE, which is part of this source code package

using System.Collections.Generic;
using System.Numerics;
using LibreLancer.Data.Ini;

namespace LibreLancer.Data.Schema.Universe;

[ParsedSection]
public partial class NebulaClouds
{
    [Entry("max_distance")]
    public int? MaxDistance;
    [Entry("puff_count")]
    public int? PuffCount;
    [Entry("puff_radius")]
    public int? PuffRadius;
    [Entry("puff_colora")]
    public Color3f? PuffColorA;
    [Entry("puff_colorb")]
    public Color3f? PuffColorB;
    public float? PuffMaxAlpha;
    [Entry("puff_shape", Multiline = true)]
    public List<string> PuffShape = [];
    [Entry("puff_weights")]
    public int[] PuffWeights = [];
    public float? PuffDrift;
    [Entry("near_fade_distance")]
    public Vector2? NearFadeDistance;
    public float? LightningIntensity;
    [Entry("lightning_color")]
    public Color4? LightningColor;
    public float? LightningGap;
    public float? LightningDuration;

    [EntryHandler("puff_max_alpha", Multiline = true)]
    private void HandlePuffMaxAlpha(Entry e) => PuffMaxAlpha = e.Count > 0 ? e[0].ToSingle() : null;

    [EntryHandler("puff_drift", Multiline = true)]
    private void HandlePuffDrift(Entry e) => PuffDrift = e.Count > 0 ? e[0].ToSingle() : null;

    [EntryHandler("lightning_intensity", Multiline = true)]
    private void HandleLightningIntensity(Entry e) => LightningIntensity = e.Count > 0 ? e[0].ToSingle() : null;

    [EntryHandler("lightning_gap", Multiline = true)]
    private void HandleLightningGap(Entry e) => LightningGap = e.Count > 0 ? e[0].ToSingle() : null;

    [EntryHandler("lightning_duration", Multiline = true)]
    private void HandleLightningDuration(Entry e) => LightningDuration = e.Count > 0 ? e[0].ToSingle() : null;
}
