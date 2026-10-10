namespace OpenLogicool.Contracts.Audio;

/// <summary>
/// 一つの process（子 process を含む）が鳴らしている音を mono で読む入口。
/// 実装は Capture が所有し、読む側は process ID だけを渡す。
/// </summary>
public interface IProcessAudioSource : IDisposable
{
    int SampleRate { get; }

    /// <summary>届いている分だけを読む（待たない）。戻り値は書いた sample 数。</summary>
    int Read(Span<float> destination);
}
