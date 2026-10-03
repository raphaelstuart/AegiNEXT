using AegiNext.Media.Preview;

namespace AegiNext.Desktop.Controllers;

/// <summary>
/// 一次经过身份校验的 UI 更新；帧为空时保留画面，ClearFrame 明确要求清空。
/// </summary>
public sealed record VideoPreviewUpdate(VideoPreviewSnapshot Snapshot, SdrVideoFrame? Frame, bool ClearFrame);
