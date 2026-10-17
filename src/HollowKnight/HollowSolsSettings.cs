using BepInEx.Configuration;

namespace YiGodhome;

// Loader preferences never read or write either game's save data.
internal static class HollowSolsSettings
{
    private static ConfigEntry<bool>? shared, contact, trueContact;
    internal static bool SharedHealth { get => shared?.Value ?? false; set { if(shared != null) shared.Value=value; } }
    internal static bool Contact { get => contact?.Value ?? false; set { if(contact != null) contact.Value=value; } }
    internal static bool TrueContact { get => Contact && (trueContact?.Value ?? false); set { if(trueContact != null) trueContact.Value=value; } }
    internal static bool StoredTrueContact => trueContact?.Value ?? false;
    internal static void Initialize(ConfigFile config) {
        shared=config.Bind("Hollow Sols Configuration","Sisters of Battle/Mantis Lords Shared HP",false,"Apply at the next encounter.");
        contact=config.Bind("Hollow Sols Configuration","Global Contact Damage",false,"Native passive boss contact with one second of retirement. Apply at the next encounter.");
        trueContact=config.Bind("Hollow Sols Configuration","True Global Contact Damage",false,"When global contact is enabled, allow repeated native contacts after native player immunity. Apply at the next encounter.");
    }
}
