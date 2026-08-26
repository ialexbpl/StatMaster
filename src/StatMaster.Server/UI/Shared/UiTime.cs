namespace StatMaster.Server.UI.Shared;

public static class UiTime
{
    private static readonly TimeZoneInfo CetZone = ResolveCetZone();

    public static string Format(DateTimeOffset? value)
    {
        if (!value.HasValue)
            return "-";

        return Format(value.Value);
    }

    public static string Format(DateTimeOffset value)
    {
        var local = TimeZoneInfo.ConvertTime(value, CetZone);
        var suffix = CetZone.IsDaylightSavingTime(local.DateTime) ? "CEST" : "CET";
        return $"{local:yyyy-MM-dd HH:mm:ss} {suffix}";
    }

    private static TimeZoneInfo ResolveCetZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time");
        }
        catch
        {
            return TimeZoneInfo.Local;
        }
    }
}
