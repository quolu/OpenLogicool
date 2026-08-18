namespace OpenLogicool.Devices.G600;

public interface IG600OnboardBaselineStore
{
    byte[]? LoadF3();

    void SaveF3(byte[] profileF3);
}

/// <summary>
/// 残置前の F3 をファイルへ保持する。crash 後の復元元。DB schema を増やさない。
/// </summary>
public sealed class FileG600OnboardBaselineStore : IG600OnboardBaselineStore
{
    public const string FileName = "g600-onboard-baseline-f3.bin";

    private readonly string _path;

    public FileG600OnboardBaselineStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, FileName);
    }

    public byte[]? LoadF3()
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        var bytes = File.ReadAllBytes(_path);
        if (bytes.Length != G600SideRemap.ReportLength || bytes[0] != G600SideRemap.ProfileReportIdF3)
        {
            throw new InvalidDataException($"baseline F3 at {_path} is not a 154-byte 0xF3 report.");
        }

        return bytes;
    }

    public void SaveF3(byte[] profileF3)
    {
        ArgumentNullException.ThrowIfNull(profileF3);
        if (profileF3.Length != G600SideRemap.ReportLength || profileF3[0] != G600SideRemap.ProfileReportIdF3)
        {
            throw new ArgumentException("baseline must be a 154-byte 0xF3 report.", nameof(profileF3));
        }

        var temp = _path + ".tmp";
        File.WriteAllBytes(temp, profileF3);
        File.Copy(temp, _path, overwrite: true);
        File.Delete(temp);
    }
}
