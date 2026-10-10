using System.IO;
using OpenLogicool.Contracts.Playbooks;
using Xunit;

namespace OpenLogicool.Host.Tests;

public sealed class RemoteViewSettingsStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"remote-view-settings-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private RemoteViewSettingsStore Store() => RemoteViewSettingsStore.ForDatabase(Path.Combine(directory, "input-studio.db"));

    private string FilePath => Path.Combine(directory, "remote-view-settings.json");

    [Fact]
    public void 保存した設定は読み直しても同じで_未保存なら既定値を返す()
    {
        var store = Store();
        Assert.Equal(RemoteViewSettings.Default, store.Load());

        var settings = new RemoteViewSettings(
            RemoteViewSettings.CurrentSchemaVersion,
            "https://relay.example/game/whip",
            "https://relay.example/game",
            "publisher",
            RemoteViewQuality.Fine);
        store.Save(settings);

        Assert.Equal(settings, Store().Load());
        Assert.True(File.Exists(FilePath));
    }

    [Theory]
    [InlineData("http://relay.example/game/whip")]
    [InlineData("ftp://relay.example/game/whip")]
    [InlineData("relay.example/game/whip")]
    [InlineData("http://127.0.0.1.example/whip")]
    public void 公開URLはhttpsだけ受け付け_保存も読み込みも拒否する(string publishUrl)
    {
        var settings = RemoteViewSettings.Default with { PublishUrl = publishUrl };

        Assert.Throws<InvalidDataException>(() => Store().Save(settings));
        Assert.False(File.Exists(FilePath));

        Directory.CreateDirectory(directory);
        File.WriteAllText(FilePath,
            $$"""{"SchemaVersion":"1.0","PublishUrl":"{{publishUrl}}","ViewerUrl":null,"PublishUser":null,"Quality":"standard"}""");
        Assert.Throws<InvalidDataException>(() => Store().Load());
    }

    [Theory]
    [InlineData("http://127.0.0.1:8889/game/whip")]
    [InlineData("http://localhost:8889/game/whip")]
    public void 実験用のローカル宛てならhttpも許す(string publishUrl)
    {
        var settings = RemoteViewSettings.Default with { PublishUrl = publishUrl };

        Store().Save(settings);

        Assert.Equal(publishUrl, Store().Load().PublishUrl);
    }

    [Fact]
    public void 未対応のschemaと画質は読み飛ばさず失敗する()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(FilePath,
            """{"SchemaVersion":"9.9","PublishUrl":null,"ViewerUrl":null,"PublishUser":null,"Quality":"standard"}""");
        Assert.Throws<InvalidDataException>(() => Store().Load());

        File.WriteAllText(FilePath,
            """{"SchemaVersion":"1.0","PublishUrl":null,"ViewerUrl":null,"PublishUser":null,"Quality":"ultra"}""");
        Assert.Throws<InvalidDataException>(() => Store().Load());
    }

    [Fact]
    public void 設定を保存してもパスワードはファイルに書かれず_nullなら保存済みのパスワードを変えない()
    {
        var secrets = new FakeSecretStore();
        var intents = Intents(secrets);

        var first = intents.SaveSettings(
            "https://relay.example/game/whip", "https://relay.example/game", "publisher", "secretpw", RemoteViewQuality.Standard);
        Assert.True(first.HasPublishPassword);
        Assert.Equal("secretpw", secrets.Password);

        var second = intents.SaveSettings(
            "https://relay.example/game/whip", "https://relay.example/game", "publisher", null, RemoteViewQuality.Fine);
        Assert.True(second.HasPublishPassword);
        Assert.Equal(RemoteViewQuality.Fine, second.Quality);
        Assert.Equal("secretpw", secrets.Password);

        Assert.DoesNotContain("secretpw", File.ReadAllText(FilePath));
        Assert.DoesNotContain("secretpw", System.Text.Json.JsonSerializer.Serialize(second));
    }

    [Fact]
    public void 不正な設定では秘密も設定も変えない()
    {
        var secrets = new FakeSecretStore();
        var intents = Intents(secrets);

        Assert.Throws<InvalidDataException>(() => intents.SaveSettings(
            "http://relay.example/whip", "https://relay.example/game", "publisher", "secretpw", RemoteViewQuality.Standard));
        Assert.Throws<ArgumentException>(() => intents.SaveSettings(
            "https://relay.example/whip", "https://relay.example/game", "publisher", "", RemoteViewQuality.Standard));

        Assert.Null(secrets.Password);
        Assert.False(File.Exists(FilePath));
    }

    private HostRemoteViewIntents Intents(FakeSecretStore secrets) => new(
        Store(),
        secrets,
        new MacroTargetSettingsStore(directory),
        _ => throw new InvalidOperationException("この test では対象を探しません。"),
        _ => throw new InvalidOperationException("この test では窓の大きさを読みません。"),
        new RemoteViewRuntime(_ => throw new InvalidOperationException("起動しません。"),
            _ => throw new InvalidOperationException("起動しません。")));

    internal sealed class FakeSecretStore : IRemoteViewSecretStore
    {
        public string? Password { get; private set; }

        public void Save(string password) => Password = password;

        public string? Load() => Password;

        public void Delete() => Password = null;
    }
}
