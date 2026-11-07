namespace Haulout.Web.Art.CloseUps;

/// <summary>The close-up drawing for each place, by the facade's place name. One file per place; geometry only.</summary>
public static class CloseUps
{
    public static CloseUpScene? For(string placeName) => placeName.ToLowerInvariant() switch
    {
        "cockpit" => CockpitCloseUp.Scene,
        "companionway" => CompanionwayCloseUp.Scene,
        "quarter berth" => QuarterBerthCloseUp.Scene,
        "saloon" => SaloonCloseUp.Scene,
        "forepeak" => ForepeakCloseUp.Scene,
        "on deck" => OnDeckCloseUp.Scene,
        "beside the hull" => BesideTheHullCloseUp.Scene,
        _ => null,
    };
}
