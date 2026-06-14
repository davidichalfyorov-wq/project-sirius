#ifndef LL_CAMERA_INCLUDED
#define LL_CAMERA_INCLUDED

cbuffer Camera : register(b1, UNIFORM_SPACE)
{
    float4x4 View;
    float4x4 Projection;
    float4x4 ViewProjection;
    float3 CameraPosition;
    float __camera_padding;
    // Previous-frame main-camera VP for motion vectors (graphics phase 0.2);
    // same depth convention as ViewProjection. Zero until the engine sets it.
    float4x4 PrevViewProjection;
}

#endif
