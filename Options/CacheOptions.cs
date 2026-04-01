namespace TestConsole5.Options;

internal sealed class CacheOptions
{
    public const string SectionName = "Cache";

    public int SizeLimit { get; set; }
}
