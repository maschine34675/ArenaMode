using EFT;
using EFT.HealthSystem;

namespace ArenaMode.Hud.InfoPanels
{
    internal enum BodyStatusKind
    {
        // Ordered by urgency; the strip shows the worst entries first.
        HeavyBleed = 0,
        Fracture = 1,
        LightBleed = 2
    }

    internal struct BodyStatusEntry
    {
        public BodyStatusKind Kind;
        public EBodyPart BodyPart;
    }

    internal static class BodyStatusCounter
    {
        // Fills buffer with the currently active bleeds and fractures, worst
        // first, and returns how many entries were written.
        //
        // GetAllActiveEffects() with the default EBodyPart.Common returns every
        // active effect across all body parts (verified in the 4.1 sources:
        // HealthHelper.FromBodyPart treats Common as "matches anything"), and
        // each effect carries its own BodyPart — so one pass covers everything.
        // It is LINQ-backed and allocates an iterator, which is why this must
        // only ever run on the throttled refresh tick, not per frame.
        public static int Fill(IHealthController healthController, BodyStatusEntry[] buffer)
        {
            if (healthController == null)
            {
                return 0;
            }

            int count = 0;
            foreach (IHealthEffect effect in healthController.GetAllActiveEffects())
            {
                if (!TryClassify(effect, out BodyStatusKind kind))
                {
                    continue;
                }

                var entry = new BodyStatusEntry { Kind = kind, BodyPart = effect.BodyPart };

                // Insertion sort by urgency, keeping the buffer full of the
                // worst entries when more effects are active than fit.
                int insertAt = count < buffer.Length ? count : -1;
                for (int i = 0; i < count && i < buffer.Length; i++)
                {
                    if (entry.Kind < buffer[i].Kind)
                    {
                        insertAt = i;
                        break;
                    }
                }

                if (insertAt < 0)
                {
                    continue;
                }

                for (int i = System.Math.Min(count, buffer.Length - 1); i > insertAt; i--)
                {
                    buffer[i] = buffer[i - 1];
                }

                buffer[insertAt] = entry;
                if (count < buffer.Length)
                {
                    count++;
                }
            }

            return count;
        }

        private static bool TryClassify(IHealthEffect effect, out BodyStatusKind kind)
        {
            // Heavy is checked first: IHeavyBleeding and ILightBleeding both
            // derive from IBleeding, so order matters if that ever changes.
            if (effect is IHeavyBleeding)
            {
                kind = BodyStatusKind.HeavyBleed;
                return true;
            }

            if (effect is ILightBleeding)
            {
                kind = BodyStatusKind.LightBleed;
                return true;
            }

            if (effect is IFracture)
            {
                kind = BodyStatusKind.Fracture;
                return true;
            }

            kind = default;
            return false;
        }

        public static string GetBodyPartLabel(EBodyPart bodyPart)
        {
            switch (bodyPart)
            {
                case EBodyPart.Head: return "HEAD";
                case EBodyPart.Chest: return "CHEST";
                case EBodyPart.Stomach: return "STOM";
                case EBodyPart.LeftArm: return "L.ARM";
                case EBodyPart.RightArm: return "R.ARM";
                case EBodyPart.LeftLeg: return "L.LEG";
                case EBodyPart.RightLeg: return "R.LEG";
                default: return "BODY";
            }
        }
    }
}
