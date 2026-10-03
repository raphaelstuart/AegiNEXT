namespace AegiNext.Media;

/// <summary>
/// GPU 上传回环与离屏颜色转换的验证结果；不代表对实际交换链图像或面板亮度的测量。
/// </summary>
public sealed record HdrVerificationResult
{
    internal HdrVerificationResult(NativeHdrVerification result)
    {
        PipelineOk = result.pipelineOk != 0;
        UploadMaxError = result.uploadMaxError;
        PrimariesMaxError = result.primariesMaxError;
        ReferenceWhiteMaxError = result.referenceWhiteMaxError;
        HdrMaxComponent = result.hdrMaxComponent;
    }

    public bool PipelineOk { get; }
    public float UploadMaxError { get; }
    public float PrimariesMaxError { get; }
    public float ReferenceWhiteMaxError { get; }
    public float HdrMaxComponent { get; }
}
