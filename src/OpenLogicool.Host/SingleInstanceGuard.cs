namespace OpenLogicool.Host;

/// <summary>
/// 二重起動防止（計画 §6.2）。named mutex を所有できた instance だけが resident host を開始できる。
/// 既存 instance への UI activation 転送は Desktop UI 実装時に追加する。
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    public const string DefaultName = @"Local\OpenLogicool.Host.SingleInstance";

    private readonly Mutex _mutex;
    private readonly bool _isOwner;

    public SingleInstanceGuard(string name)
    {
        _mutex = new Mutex(initiallyOwned: true, name, out var createdNew);
        _isOwner = createdNew;
    }

    /// <summary>この instance が唯一の所有者なら true。false なら既に別 instance が動いている。</summary>
    public bool IsOwner => _isOwner;

    public void Dispose()
    {
        if (_isOwner)
        {
            _mutex.ReleaseMutex();
        }

        _mutex.Dispose();
    }
}
