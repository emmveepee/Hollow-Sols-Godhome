using UnityEngine;

namespace YiGodhome;

// Native auto-reference resolves the nearest parry context. Forward the native
// owner's response, but identify this attack's own parry effect, not the boss's
// original shared SlashDamage. No defense timing or result is computed here.
internal sealed class NativeAttackOwner : MonoBehaviour, IParriableOwner
{
    public MonsterBase Monster = null!;
    public ParriableAttackEffect Parry = null!;
    internal MonsterBase? InjuryOwner;
    public ParriableAttackEffect GetParriable => Parry;
    public MonsterBase BindMonster => InjuryOwner!;
    public void ParriedByPlayer(ParryResultData result) => ((IParriableOwner)Monster).ParriedByPlayer(result);
}
