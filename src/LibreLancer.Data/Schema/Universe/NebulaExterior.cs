// MIT License - Copyright (c) Callum McGing
// This file is subject to the terms and conditions defined in
// LICENSE, which is part of this source code package
using System.Collections.Generic;
using LibreLancer.Data.Ini;

namespace LibreLancer.Data.Schema.Universe;

[ParsedSection]
public partial class NebulaExterior
{
    [Entry("shape", Multiline = true)]
    public List<string> Shape = [];
    [Entry("shape_weights")]
    public int[] ShapeWeights = [];
    [Entry("fill_shape")]
    public string? FillShape;
    [Entry("plane_slices")]
    public int? PlaneSlices;
    [Entry("bit_radius")]
    public int? BitRadius;
    public int? BitRadiusRandomVariation;
    [Entry("min_bits")]
    public int? MinBits;
    [Entry("max_bits")]
    public int? MaxBits;
    public float? MoveBitPercent;
    public float? EquatorBias;

    [EntryHandler("bit_radius_random_variation", Multiline = true)]
    private void HandleBitRadiusRandomVariation(Entry e) => BitRadiusRandomVariation = e.Count > 0 ? e[0].ToInt32() : null;

    [EntryHandler("move_bit_percent", Multiline = true)]
    private void HandleMoveBitPercent(Entry e) => MoveBitPercent = e.Count > 0 ? e[0].ToSingle() : null;

    [EntryHandler("equator_bias", Multiline = true)]
    private void HandleEquatorBias(Entry e) => EquatorBias = e.Count > 0 ? e[0].ToSingle() : null;
    [Entry("color")]
    public Color4? Color;
    [Entry("opacity")]
    public float Opacity;
}
