namespace Woodword.Models;

public enum TranslationDirection
{
    CommonToVieran,
    VieranToCommon,
}

public static class TranslationDirectionExtensions
{
    public static string ToWireValue(this TranslationDirection direction) => direction switch
    {
        TranslationDirection.CommonToVieran => "common-to-vieran",
        TranslationDirection.VieranToCommon => "vieran-to-common",
        _ => throw new ArgumentOutOfRangeException(nameof(direction)),
    };
}
