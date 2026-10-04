// A build helper that is not part of any project in SampleShop.slnx.
internal static class Stamp
{
    public static string Now() => DateTime.UtcNow.ToString("O");
}
