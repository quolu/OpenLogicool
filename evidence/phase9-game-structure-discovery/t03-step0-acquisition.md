# t03 STEP 0 acquisition evidence

- 実装commit: `125d5a0`
- HttpClient境界: redirect後URL、content type、取得bytes、4 MiB上限を明示
- 正規化: AngleSharp 1.7.1でHTML5解析し、ReverseMarkdown 6.2.1でHTML tagを残さないMarkdownへ変換
- policy: 取得前とredirect／canonical確定後に`SourcePolicyEvaluator`を再評価
- provenance: raw bytesのSHA-256、取得方式、取得時刻、要約provider／model／送信先／費用／期限
- 明示失敗: provider未選定、network不可、HTTP失敗、parse失敗、取消、timeoutを別状態で返す
- fallback: cache、別source、別providerへの自動切替なし
- GameWith: 成功結果は`SummaryReferenceBody`だけ。raw HTML／全文Markdownは結果wireへ出さない

## 検証

- `dotnet test tests/OpenLogicool.Host.Tests/OpenLogicool.Host.Tests.csproj --filter FullyQualifiedName~WebReferenceAcquisitionServiceTests --no-restore`
  - 8件成功、失敗0
- `dotnet test tests/OpenLogicool.Host.Tests/OpenLogicool.Host.Tests.csproj --no-restore`
  - 126件成功、失敗0
- `dotnet test tests/OpenLogicool.Architecture.Tests/OpenLogicool.Architecture.Tests.csproj --no-restore`
  - 7件成功、失敗0
