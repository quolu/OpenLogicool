using OpenLogicool.Contracts.Shared;

namespace OpenLogicool.Contracts.Perception;

public static class VisualPatchSignatureComparer
{
    public static double MeanAbsoluteDifference(
        VisualPatchSignature left,
        VisualPatchSignature right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        if (left.SchemaVersion != ContractSchemaVersions.Revision03
            || right.SchemaVersion != ContractSchemaVersions.Revision03
            || left.SampleWidth != right.SampleWidth
            || left.SampleHeight != right.SampleHeight)
        {
            throw new ArgumentException("比較するvisual patch signatureが互換ではありません。");
        }
        var leftBytes = Convert.FromBase64String(left.LumaBase64);
        var rightBytes = Convert.FromBase64String(right.LumaBase64);
        if (leftBytes.Length != rightBytes.Length)
        {
            throw new ArgumentException("比較するvisual patchの長さが一致しません。");
        }
        return leftBytes.Zip(rightBytes, (leftValue, rightValue) => Math.Abs(leftValue - rightValue)).Average();
    }
}
