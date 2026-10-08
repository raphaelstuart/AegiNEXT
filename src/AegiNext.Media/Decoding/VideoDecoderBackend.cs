namespace AegiNext.Media.Decoding;

/// <summary>实际交付图像的解码后端。</summary>
public enum VideoDecoderBackend
{
    Software = 0,
    VideoToolbox = 1,
    D3D11VA = 2,
    Vulkan = 3
}
