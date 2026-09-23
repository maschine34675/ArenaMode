// Explicit native health boundary: clamping and synchronous post-mutation
// callbacks reproduce ActiveHealthController.ChangeHealth, not pickup policy.
namespace EFT
{
    public enum EBodyPart { Head, Chest, Stomach, LeftLeg, RightLeg, LeftArm, RightArm, Common }
}
namespace EFT.HealthSystem
{
    public struct HealthValue { public float Current, Maximum; }
    public class ActiveHealthController : EFT.Health
    {
        public EFT.Player Player;
        public readonly Dictionary<EFT.EBodyPart, HealthValue> Parts = Enum.GetValues<EFT.EBodyPart>().ToDictionary(p=>p,p=>new HealthValue{Current=100,Maximum=100});
        public readonly HashSet<EFT.EBodyPart> Destroyed=[];
        public readonly HashSet<string> Effects=["Bleeding", "Fracture"];
        public Action<EFT.EBodyPart,float> Changed;
        public bool ThrowReads;
        public int Calls;
        public float Requested;
        public HealthValue GetBodyPartHealth(EFT.EBodyPart part)
        { if(ThrowReads)throw new Exception("unreadable health");return Parts[part]; }
        public bool IsBodyPartDestroyed(EFT.EBodyPart part)=>Destroyed.Contains(part);
        public void ChangeHealth(EFT.EBodyPart part,float amount,object info)
        {
            Calls++;Requested+=amount;
            if(!IsAlive||Destroyed.Contains(part))return;
            var value=Parts[part];float before=value.Current;
            value.Current=Math.Clamp(value.Current+amount,0,value.Maximum);Parts[part]=value;
            Changed?.Invoke(part,value.Current-before);
        }
    }
    public static class DamageHelper { public static readonly object Existence=new(); }
}
namespace ArenaMode.Combat
{
    public static class ArenaAdministrativeDeathGate { public static bool Active; public static bool IsActive(string id)=>Active; }
}
