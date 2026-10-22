namespace OgameSkaner.Core.Licensing;

/// <summary>The terms the player accepts on the first run. Bump <see cref="Version"/> when the text changes, so they're asked again.</summary>
/// <remarks>A draft: have it checked by a lawyer before selling.</remarks>
public static class LicenseTerms
{
    public const int Version = 1;

    public const string Text =
        """
        OgameSkaner automates actions in the OgameX browser game on your behalf: it reads game pages, builds, sends fleets and spies, using your own game session from your own computer.

        1. You use OgameSkaner at your own risk and responsibility. The OgameX rules may forbid automation; the server's operators can warn, suspend or delete your game account, or change the game so the app stops working. The author of OgameSkaner has no influence on that and is not liable for it, for lost game progress or resources, or for any other damage from using the app.

        2. Your license is personal. It works on the computer it was activated on and for the game accounts bound to it. Sharing, reselling, decompiling, modifying or circumventing the license checks is not allowed and ends the license without a refund.

        3. The subscription is billed monthly by the payment provider and can be cancelled at any time; the app then stops at the end of the paid period. The app checks the license online every few hours and keeps working for a few days without a connection.

        4. The app is provided as it is, without any warranty. Game data and settings stay on your computer.
        """;
}
