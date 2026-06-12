using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace LibreLancer.Graphics;

public sealed class ShaderBundle
{
    public uint FeatureMask { get; private set; }

    private Shader[] shaders;

    public Shader Get<T>(T flags) where T : unmanaged, Enum =>
        Get(Unsafe.As<T, uint>(ref flags));

    public Shader Get(uint flags) => shaders[(int)(flags & FeatureMask)];

    private ShaderBundle(RenderContext context, BytecodesBundle bundle)
    {
        shaders = new Shader[bundle.FeatureMask == 0 ? 1 : (bundle.FeatureMask + 1)];
        FeatureMask = bundle.FeatureMask;
        // Vulkan-only permutations carry byte-identical copies of the base
        // permutation's payload for the other backends. Deduplicate by
        // content so each unique payload compiles once - without this a
        // 1024-permutation bundle compiles ~1000 identical GL programs and
        // stalls the load for minutes.
        var byContent = new Dictionary<(ulong, int), Shader>();
        for (int i = 0; i < bundle.ShaderCount; i++)
        {
            var payload = bundle.GetShader(i);
            var key = (Fnv1a64(payload), payload.Length);
            if (!byContent.TryGetValue(key, out var shader))
            {
                shader = new Shader(context, payload);
                byContent.Add(key, shader);
            }
            shaders[(int)bundle.GetFeatures(i)] = shader;
        }
    }

    private static ulong Fnv1a64(ReadOnlySpan<byte> data)
    {
        var hash = 14695981039346656037ul;
        foreach (var b in data)
        {
            hash = (hash ^ b) * 1099511628211ul;
        }
        return hash;
    }

    public static ShaderBundle FromResource<T>(RenderContext context, string resourceName)
    {
        using var stream = typeof(T).Assembly.GetManifestResourceStream(resourceName) ?? throw new FileNotFoundException(resourceName);
        return FromStream(context, stream);
    }

    public static ShaderBundle FromStream(RenderContext context, Stream stream) =>
        new(context, BytecodesBundle.FromStream(stream));
}
