using System;

namespace HollowSols.Foundation;

public enum GameSide : byte { HollowKnight = 1, NineSols = 2 }
public enum GameMode : byte { Classic = 1, SteelSoul = 2, Godhome = 3 }

// Deliberately not convertible to either native game's profile/index.
public readonly struct ModSlot : IEquatable<ModSlot>
{
    public const int Count = 4;
    public int Number { get; }
    public ModSlot(int number)
    {
        if (number < 1 || number > Count) throw new ArgumentOutOfRangeException(nameof(number));
        Number = number;
    }
    internal void Validate() { if (Number < 1 || Number > Count) throw new ArgumentException("Uninitialized mod slot."); }
    public bool Equals(ModSlot other) => Number == other.Number;
    public override bool Equals(object? other) => other is ModSlot slot && Equals(slot);
    public override int GetHashCode() => Number;
    public override string ToString() => "Hollow Sols Slot " + Number;
}

public readonly struct NewGameChoice
{
    public GameMode Mode { get; }
    public bool FullGodhomeAccess { get; }
    public NewGameChoice(GameMode mode, bool experimentalWarningAccepted, bool fullGodhomeAccess)
    {
        if (mode < GameMode.Classic || mode > GameMode.Godhome) throw new ArgumentOutOfRangeException(nameof(mode));
        if (mode != GameMode.Godhome && !experimentalWarningAccepted)
            throw new InvalidOperationException("Accept the experimental campaign warning before creating this save.");
        if (mode != GameMode.Godhome && fullGodhomeAccess) throw new ArgumentException("Full access is a Godhome choice.");
        Mode = mode; FullGodhomeAccess = fullGodhomeAccess;
    }
    internal void Validate()
    {
        if (Mode < GameMode.Classic || Mode > GameMode.Godhome || Mode != GameMode.Godhome && FullGodhomeAccess)
            throw new ArgumentException("Invalid creation choice.");
    }
}

public sealed class SavePair
{
    public ModSlot Slot { get; }
    public Guid Id { get; }
    public NewGameChoice Choice { get; }
    internal SavePair(ModSlot slot, Guid id, NewGameChoice choice)
    { slot.Validate(); choice.Validate(); if (id == Guid.Empty) throw new ArgumentException("Empty pair identity."); Slot = slot; Id = id; Choice = choice; }
}

public enum SlotCondition { Empty, Ready, RecoveryAvailable, Unrecoverable }
public sealed class SlotDeletionConfirmation
{
    public ModSlot Slot { get; }
    internal string Root { get; }
    internal string Fingerprint { get; }
    internal SlotDeletionConfirmation(ModSlot slot, string root, string fingerprint) { Slot = slot; Root = root; Fingerprint = fingerprint; }
}
public sealed class SlotSummary
{
    public ModSlot Slot { get; }
    public SavePair? Pair { get; }
    public SlotCondition Condition { get; }
    public long Generation { get; }
    internal SlotSummary(ModSlot slot, SavePair? pair, SlotCondition condition, long generation)
    { Slot = slot; Pair = pair; Condition = condition; Generation = generation; }
}

public sealed class PairedCheckpoint
{
    public SavePair Pair { get; }
    public long Generation { get; }
    public Guid Transaction { get; }
    public Guid Session { get; }
    public byte[] HollowKnight { get; }
    public byte[] NineSols { get; }
    public bool Recovered { get; internal set; }
    public string? RecoveryNotice => Recovered ? "Your Hollow Sols save was recovered from an earlier paired checkpoint. Progress newer than that checkpoint may have been lost." : null;
    internal PairedCheckpoint(SavePair pair, long generation, Guid transaction, Guid session, byte[] hk, byte[] ns)
    { Pair = pair; Generation = generation; Transaction = transaction; Session = session; HollowKnight = hk; NineSols = ns; }
}

public sealed class SaveUnavailableException : Exception
{
    public SaveUnavailableException() : base("This Hollow Sols save could not be recovered. Its original data has been preserved.") { }
}
