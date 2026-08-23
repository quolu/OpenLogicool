using OpenLogicool.Contracts.Shared;

namespace OpenLogicool.Contracts.Research;

/// <summary>Sourceの利用条件をAI入力から独立して決定する純粋な決定表。</summary>
public static class SourcePolicyEvaluator
{
    public const int SummaryOnlyMaxExcerptCount = 3;
    public const int SummaryOnlyMaxExcerptCharacters = 200;

    public static SourcePolicyDecision Evaluate(Uri url, Uri canonicalUrl, SourcePolicyEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(canonicalUrl);
        ArgumentNullException.ThrowIfNull(evidence);
        WebReferenceContractSchema.ValidateSchema(evidence.SchemaVersion, nameof(SourcePolicyEvidence));
        WebReferenceContractSchema.ValidateHttpUri(url, nameof(url));
        WebReferenceContractSchema.ValidateHttpUri(canonicalUrl, nameof(canonicalUrl));

        if (!Enum.IsDefined(evidence.Terms) || !Enum.IsDefined(evidence.Robots))
        {
            throw new ArgumentException("source policy evidenceに未対応値があります。", nameof(evidence));
        }

        var (policy, reason) = (evidence.Terms, evidence.Robots) switch
        {
            (_, RobotsDisposition.Rejected) => (SourcePolicy.Blocked, SourcePolicyReason.RobotsRejected),
            (SourceTermsDisposition.Rejected, _) => (SourcePolicy.Blocked, SourcePolicyReason.TermsRejected),
            (_, RobotsDisposition.Unknown) => (SourcePolicy.LinkOnly, SourcePolicyReason.RobotsUnknown),
            (_, RobotsDisposition.Unavailable) => (SourcePolicy.LinkOnly, SourcePolicyReason.RobotsUnavailable),
            (SourceTermsDisposition.Unknown, _) => (SourcePolicy.LinkOnly, SourcePolicyReason.TermsUnknown),
            (SourceTermsDisposition.Unavailable, _) => (SourcePolicy.LinkOnly, SourcePolicyReason.TermsUnavailable),
            _ when IsGameWith(url) || IsGameWith(canonicalUrl) =>
                (SourcePolicy.SummaryOnly, SourcePolicyReason.GameWithSummaryOnly),
            (SourceTermsDisposition.FullTextAllowed, RobotsDisposition.Allowed) =>
                (SourcePolicy.FullTextAllowed, SourcePolicyReason.ExplicitFullTextPermission),
            (SourceTermsDisposition.SummaryAllowed, RobotsDisposition.Allowed) =>
                (SourcePolicy.SummaryOnly, SourcePolicyReason.ExplicitSummaryPermission),
            _ => throw new ArgumentException("source policy decision tableに未対応の入力があります。", nameof(evidence)),
        };

        return new SourcePolicyDecision(
            ContractSchemaVersions.Revision01,
            policy,
            reason,
            QuoteScopeFor(policy));
    }

    public static ReferenceQuoteScope QuoteScopeFor(SourcePolicy policy) => policy switch
    {
        SourcePolicy.FullTextAllowed => new(
            ContractSchemaVersions.Revision01,
            SummaryOnlyMaxExcerptCount,
            SummaryOnlyMaxExcerptCharacters),
        SourcePolicy.SummaryOnly => new(
            ContractSchemaVersions.Revision01,
            SummaryOnlyMaxExcerptCount,
            SummaryOnlyMaxExcerptCharacters),
        SourcePolicy.LinkOnly or SourcePolicy.Blocked => new(ContractSchemaVersions.Revision01, 0, 0),
        _ => throw new ArgumentOutOfRangeException(nameof(policy)),
    };

    private static bool IsGameWith(Uri canonicalUrl)
    {
        var host = canonicalUrl.IdnHost.TrimEnd('.');
        return string.Equals(host, "gamewith.jp", StringComparison.OrdinalIgnoreCase)
               || host.EndsWith(".gamewith.jp", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>STEP 0 wire contractの閉集合と相互不変条件を検証する。</summary>
public static class WebReferenceContractSchema
{
    public static void Validate(WebReferenceSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        ValidateSchema(source.SchemaVersion, nameof(WebReferenceSource));
        RequireText(source.SourceId, "source id");
        ValidateHttpUri(source.Url, "url");
        ValidateHttpUri(source.CanonicalUrl, "canonical url");
        var expected = SourcePolicyEvaluator.Evaluate(source.Url, source.CanonicalUrl, source.PolicyEvidence);
        if (source.PolicyDecision != expected)
        {
            throw new ArgumentException("source policy decisionはdeterministic decision tableと一致しなければなりません。", nameof(source));
        }

        switch (source)
        {
            case AcquiredWebReferenceSource acquired
                when source.PolicyDecision.Policy is SourcePolicy.FullTextAllowed or SourcePolicy.SummaryOnly:
                RequireText(acquired.Title, "title");
                RequireText(acquired.Publisher, "publisher");
                RequireText(acquired.Locale, "locale");
                EnsureDefined(acquired.SourceKind, nameof(acquired.SourceKind));
                if (acquired.PublishedUtc is not null && acquired.UpdatedUtc < acquired.PublishedUtc)
                {
                    throw new ArgumentException("updated utcをpublished utcより前にできません。", nameof(source));
                }

                Validate(acquired.Provenance);
                break;
            case RestrictedWebReferenceSource restricted
                when source.PolicyDecision.Policy is SourcePolicy.LinkOnly or SourcePolicy.Blocked:
                RequireOptionalText(restricted.Title, "restricted title");
                RequireOptionalText(restricted.Publisher, "restricted publisher");
                RequireOptionalText(restricted.Locale, "restricted locale");
                break;
            default:
                throw new ArgumentException("source stateとpolicy decisionの組合せが不正です。", nameof(source));
        }
    }

    public static void Validate(WebReferenceAcquisitionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ValidateSchema(plan.SchemaVersion, nameof(WebReferenceAcquisitionPlan));
        RequireText(plan.PlanId, "plan id");
        ValidateHttpUri(plan.Url, "plan url");
        ValidateHttpUri(plan.CanonicalUrl, "plan canonical url");
        EnsureDefined(plan.AcquisitionMethod, nameof(plan.AcquisitionMethod));
        var expected = SourcePolicyEvaluator.Evaluate(plan.Url, plan.CanonicalUrl, plan.PolicyEvidence);
        if (plan.PolicyDecision != expected)
        {
            throw new ArgumentException("plan policy decisionはdeterministic decision tableと一致しなければなりません。", nameof(plan));
        }

        var hasProvider = !string.IsNullOrWhiteSpace(plan.SummaryProvider);
        var hasModel = !string.IsNullOrWhiteSpace(plan.SummaryModel);
        if (hasProvider != hasModel)
        {
            throw new ArgumentException("summary providerとmodelは両方設定するか両方省略します。", nameof(plan));
        }

        RequireOptionalText(plan.ExternalDestination, "external destination");
        if (plan.EstimatedCostUsd is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(plan), "estimated costは負にできません。");
        }
    }

    public static void Validate(ReferenceDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        ValidateSchema(document.SchemaVersion, nameof(ReferenceDocument));
        RequireText(document.DocumentId, "document id");
        RequireText(document.SourceId, "source id");
        if (document.Revision <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(document), "document revisionは正でなければなりません。");
        }

        if (document.Revision == 1 && document.ParentDocumentId is not null
            || document.Revision > 1 && string.IsNullOrWhiteSpace(document.ParentDocumentId))
        {
            throw new ArgumentException("document revisionとparent document idの組合せが不正です。", nameof(document));
        }

        ArgumentNullException.ThrowIfNull(document.Body);
        ValidateSchema(document.Body.SchemaVersion, nameof(ReferenceDocumentBody));
        switch (document.Policy, document.Body)
        {
            case (SourcePolicy.FullTextAllowed, FullTextReferenceBody fullText):
                RequireText(fullText.NormalizedMarkdown, "normalized markdown");
                break;
            case (SourcePolicy.SummaryOnly, SummaryReferenceBody summary):
                ValidateSummary(summary);
                break;
            case (SourcePolicy.LinkOnly, LinkOnlyReferenceBody link):
                RequireText(link.Reason, "link-only reason");
                break;
            case (SourcePolicy.Blocked, BlockedReferenceBody blocked):
                RequireText(blocked.Reason, "blocked reason");
                break;
            default:
                throw new ArgumentException("source policyとreference document bodyの組合せが不正です。", nameof(document));
        }
    }

    public static void Validate(ReferenceDocument document, WebReferenceSource source)
    {
        Validate(source);
        Validate(document);
        if (!string.Equals(document.SourceId, source.SourceId, StringComparison.Ordinal)
            || document.Policy != source.PolicyDecision.Policy)
        {
            throw new ArgumentException("reference documentは同じsourceとpolicy decisionへ束縛されなければなりません。", nameof(document));
        }
    }

    public static void Validate(WebReferenceFact fact)
    {
        ArgumentNullException.ThrowIfNull(fact);
        ValidateSchema(fact.SchemaVersion, nameof(WebReferenceFact));
        RequireText(fact.FactId, "fact id");
        RequireRevision(fact.Revision, fact.ParentFactId, "fact");
        EnsureDefined(fact.Kind, nameof(fact.Kind));
        EnsureDefined(fact.Validity, nameof(fact.Validity));
        RequireText(fact.Claim, "claim");
        RequireDistinctText(fact.SourceReferenceIds, "source references", allowEmpty: false);
        if (fact.Confidence is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(fact), "fact confidenceは[0, 1]でなければなりません。");
        }

        Validate(fact.Scope);
        RequireDistinctText(fact.ContradictionIds, "contradiction ids", allowEmpty: true);
        if (fact.Validity == WebReferenceFactValidity.Contradicted && fact.ContradictionIds.Count == 0
            || fact.Validity != WebReferenceFactValidity.Contradicted && fact.ContradictionIds.Count > 0)
        {
            throw new ArgumentException("contradiction状態と参照の組合せが不正です。", nameof(fact));
        }
    }

    public static void Validate(WebReferenceContradiction contradiction)
    {
        ArgumentNullException.ThrowIfNull(contradiction);
        ValidateSchema(contradiction.SchemaVersion, nameof(WebReferenceContradiction));
        RequireText(contradiction.ContradictionId, "contradiction id");
        RequireRevision(contradiction.Revision, contradiction.ParentContradictionId, "contradiction");
        RequireText(contradiction.LeftFactId, "left fact id");
        RequireText(contradiction.RightFactId, "right fact id");
        if (string.Equals(contradiction.LeftFactId, contradiction.RightFactId, StringComparison.Ordinal))
        {
            throw new ArgumentException("contradictionは異なるfactを参照しなければなりません。", nameof(contradiction));
        }

        RequireDistinctText(contradiction.SourceReferenceIds, "contradiction source references", allowEmpty: false);
        RequireText(contradiction.Note, "contradiction note");
    }

    public static void Validate(WebReferenceAcquisitionAttempt attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        ValidateSchema(attempt.SchemaVersion, nameof(WebReferenceAcquisitionAttempt));
        RequireText(attempt.AttemptId, "attempt id");
        ValidateHttpUri(attempt.RequestedUrl, "requested url");
        EnsureDefined(attempt.Status, nameof(attempt.Status));
        if (attempt.CompletedUtc < attempt.StartedUtc)
        {
            throw new ArgumentException("attempt completionを開始時刻より前にできません。", nameof(attempt));
        }

        switch (attempt.Status)
        {
            case WebReferenceAcquisitionStatus.Succeeded:
                RequireText(attempt.SourceId, "succeeded source id");
                RequireText(attempt.NewDocumentId, "new document id");
                RequireNull(attempt.ExistingDocumentId, "succeeded existing document id");
                RequireNull(attempt.Detail, "succeeded detail");
                break;
            case WebReferenceAcquisitionStatus.ReusedExisting:
                RequireText(attempt.SourceId, "reused source id");
                RequireText(attempt.ExistingDocumentId, "existing document id");
                RequireNull(attempt.NewDocumentId, "reused new document id");
                RequireNull(attempt.Detail, "reused detail");
                break;
            case WebReferenceAcquisitionStatus.PolicyLimited
                or WebReferenceAcquisitionStatus.TermsRejected
                or WebReferenceAcquisitionStatus.RobotsRejected:
                RequireText(attempt.SourceId, "policy-limited source id");
                RequireText(attempt.NewDocumentId, "policy-limited document id");
                RequireNull(attempt.ExistingDocumentId, "policy-limited existing document id");
                RequireText(attempt.Detail, "policy-limited detail");
                break;
            default:
                RequireNull(attempt.SourceId, "failed source id");
                RequireNull(attempt.NewDocumentId, "failed new document id");
                RequireNull(attempt.ExistingDocumentId, "failed existing document id");
                RequireText(attempt.Detail, "failure detail");
                break;
        }
    }

    public static void Validate(ResearchRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        ValidateSchema(run.SchemaVersion, nameof(ResearchRun));
        RequireText(run.RunId, "run id");
        RequireText(run.GameId, "game id");
        RequireText(run.ResearchGoal, "research goal");
        EnsureDefined(run.Status, nameof(run.Status));
        ArgumentNullException.ThrowIfNull(run.Attempts);
        if (run.Attempts.Select(attempt => attempt.AttemptId).Distinct(StringComparer.Ordinal).Count() != run.Attempts.Count)
        {
            throw new ArgumentException("research run内のattempt idは重複できません。", nameof(run));
        }

        foreach (var attempt in run.Attempts)
        {
            Validate(attempt);
        }

        var isTerminal = run.Status is ResearchRunStatus.Completed or ResearchRunStatus.Failed or ResearchRunStatus.Cancelled;
        if (isTerminal != (run.CompletedUtc is not null) || run.CompletedUtc < run.CreatedUtc)
        {
            throw new ArgumentException("research run statusとcompletion時刻の組合せが不正です。", nameof(run));
        }
    }

    public static void Validate(WebReferenceDeletionPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ValidateSchema(preview.SchemaVersion, nameof(WebReferenceDeletionPreview));
        RequireText(preview.SourceId, "deletion source id");
        RequireDistinctText(preview.DocumentIds, "deletion document ids", allowEmpty: true);
        RequireDistinctText(preview.FactIds, "deletion fact ids", allowEmpty: true);
        RequireDistinctText(preview.ContradictionIds, "deletion contradiction ids", allowEmpty: true);
        if (preview.PayloadBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(preview), "deletion payload bytesは負にできません。");
        }
    }

    public static void Validate(WebReferenceDeletionTombstone tombstone)
    {
        ArgumentNullException.ThrowIfNull(tombstone);
        ValidateSchema(tombstone.SchemaVersion, nameof(WebReferenceDeletionTombstone));
        RequireText(tombstone.TombstoneId, "tombstone id");
        RequireText(tombstone.SourceId, "tombstone source id");
        RequireText(tombstone.Reason, "deletion reason");
        RequireDistinctText(tombstone.DeletedDocumentIds, "deleted document ids", allowEmpty: true);
        RequireDistinctText(tombstone.DeletedFactIds, "deleted fact ids", allowEmpty: true);
        RequireDistinctText(tombstone.DeletedContradictionIds, "deleted contradiction ids", allowEmpty: true);
    }

    public static void Validate(WebReferenceSourceExclusion exclusion)
    {
        ArgumentNullException.ThrowIfNull(exclusion);
        ValidateSchema(exclusion.SchemaVersion, nameof(WebReferenceSourceExclusion));
        RequireText(exclusion.ExclusionId, "exclusion id");
        ValidateHttpUri(exclusion.Url, "excluded url");
        RequireText(exclusion.Reason, "exclusion reason");
    }

    public static void Validate(WebReferenceReacquisitionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateSchema(request.SchemaVersion, nameof(WebReferenceReacquisitionRequest));
        RequireText(request.RequestId, "reacquisition request id");
        RequireText(request.SourceId, "reacquisition source id");
        RequireText(request.Reason, "reacquisition reason");
    }

    internal static void ValidateSchema(string schemaVersion, string kind)
    {
        if (!string.Equals(schemaVersion, ContractSchemaVersions.Revision01, StringComparison.Ordinal))
        {
            throw new ArgumentException($"{kind} のschema version '{schemaVersion}' は未対応です。", nameof(schemaVersion));
        }
    }

    internal static void ValidateHttpUri(Uri uri, string field)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri || uri.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException($"{field} はabsolute HTTP(S) URIでなければなりません。", nameof(uri));
        }
    }

    private static void Validate(WebReferenceProvenance provenance)
    {
        ArgumentNullException.ThrowIfNull(provenance);
        ValidateSchema(provenance.SchemaVersion, nameof(WebReferenceProvenance));
        RequireText(provenance.ContentDigest, "content digest");
        EnsureDefined(provenance.AcquisitionMethod, nameof(provenance.AcquisitionMethod));
        if (provenance.ExpiresUtc < provenance.RetrievedUtc)
        {
            throw new ArgumentException("expiryを取得時刻より前にできません。", nameof(provenance));
        }

        if (provenance.AiSummary is { } summary)
        {
            ValidateSchema(summary.SchemaVersion, nameof(AiSummaryProvenance));
            RequireText(summary.Provider, "summary provider");
            RequireText(summary.Model, "summary model");
            RequireText(summary.PromptRevision, "summary prompt revision");
            RequireText(summary.ExternalDestination, "summary external destination");
            if (summary.CostUsd is < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(provenance), "summary costは負にできません。");
            }
        }
    }

    private static void ValidateSummary(SummaryReferenceBody summary)
    {
        RequireText(summary.StructuredSummaryMarkdown, "structured summary markdown");
        RequireDistinctText(summary.EvidenceExcerpts, "evidence excerpts", allowEmpty: false);
        RequireDistinctText(summary.Terms, "terms", allowEmpty: false);
        var scope = SourcePolicyEvaluator.QuoteScopeFor(SourcePolicy.SummaryOnly);
        if (summary.EvidenceExcerpts.Count > scope.MaxExcerptCount
            || summary.EvidenceExcerpts.Any(excerpt => excerpt.Length > scope.MaxExcerptCharacters))
        {
            throw new ArgumentException(
                $"SummaryOnlyの根拠断片は{scope.MaxExcerptCharacters}文字×{scope.MaxExcerptCount}件以内です。",
                nameof(summary));
        }
    }

    private static void Validate(WebReferenceFactScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ValidateSchema(scope.SchemaVersion, nameof(WebReferenceFactScope));
        if (scope.ValidUntilUtc < scope.ValidFromUtc)
        {
            throw new ArgumentException("fact validity endをstartより前にできません。", nameof(scope));
        }
    }

    private static void RequireRevision(long revision, string? parentId, string kind)
    {
        if (revision <= 0
            || revision == 1 && parentId is not null
            || revision > 1 && string.IsNullOrWhiteSpace(parentId))
        {
            throw new ArgumentException($"{kind} revisionとparent idの組合せが不正です。", nameof(revision));
        }
    }

    private static void RequireDistinctText(IReadOnlyList<string> values, string field, bool allowEmpty)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (!allowEmpty && values.Count == 0
            || values.Any(string.IsNullOrWhiteSpace)
            || values.Distinct(StringComparer.Ordinal).Count() != values.Count)
        {
            throw new ArgumentException($"{field}は重複なしの{(allowEmpty ? "文字列列" : "非空文字列列")}でなければなりません。", nameof(values));
        }
    }

    private static void RequireText(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{field}が空です。", nameof(value));
        }
    }

    private static void RequireOptionalText(string? value, string field)
    {
        if (value is not null && string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{field}はnullまたは非空文字列でなければなりません。", nameof(value));
        }
    }

    private static void RequireNull(string? value, string field)
    {
        if (value is not null)
        {
            throw new ArgumentException($"{field}は設定できません。", nameof(value));
        }
    }

    private static void EnsureDefined<TEnum>(TEnum value, string field) where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(field, value, "未対応のenum値です。");
        }
    }
}
