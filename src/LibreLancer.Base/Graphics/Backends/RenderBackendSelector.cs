using System;

namespace LibreLancer.Graphics.Backends;

public enum RenderBackendKind
{
    OpenGL,
    Vulkan
}

/// <summary>
/// Selects the render backend from the SIRIUS_RENDERER environment variable.
/// Vulkan is the default since passing the GL/VK parity gate; "gl" opts back
/// into OpenGL, and a failed Vulkan init falls back to OpenGL by recreating
/// the window (see SDL3Game.Run).
/// </summary>
public static class RenderBackendSelector
{
    public static readonly RenderBackendKind Kind =
        Environment.GetEnvironmentVariable("SIRIUS_RENDERER")?.ToLowerInvariant() switch
        {
            "gl" or "opengl" => RenderBackendKind.OpenGL,
            _ => RenderBackendKind.Vulkan
        };
}
