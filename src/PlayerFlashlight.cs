using Sharp.Shared.GameEntities;
using Sharp.Shared.Types;
using System.Globalization;

namespace MS_Flashlight
{
    public class PlayerFlashlight
    {
        IPlayerController? Player = null;
        IBaseEntity? Flashlight_Ent = null;

        public Color32 ColorFL = new(255, 255, 255, 255);
        public bool CanToggle = false;
        public bool NotSpam = false;
        public bool Rainbow = false;

        public void SetPlayer(IPlayerController? player)
        {
            CanToggle = false;
            NotSpam = false;
            Rainbow = false;

            RemoveFlashlight();
            Player = player;
        }

        public IPlayerController? GetPlayer()
        {
            return Player;
        }

        public void RemovePlayer()
        {
            CanToggle = false;
            NotSpam = false;
            Rainbow = false;

            RemoveFlashlight();
            Player = null;
        }
        public void RemoveFlashlight()
        {
            if (Flashlight_Ent != null && Flashlight_Ent.IsValid()) Flashlight_Ent.Kill();
            Flashlight_Ent = null;
        }
        public void SpawnFlashlight()
        {
            if (Player == null || !Player.IsValid()) return;
            //RemoveFlashlight();
            var pawn = Player.GetPlayerPawn()!;
            var vecOrigin = pawn.GetAbsOrigin() with { Z = pawn.GetAbsOrigin().Z + pawn.ViewOffset.Z + 0.03f };
            var vecAngles = pawn.GetEyeAngles();
            var kv = new Dictionary<string, KeyValuesVariantValueItem>
            {
                {"directlight", 3},
                {"outer_angle", 45f},
                {"enabled", true},
                {"color", $"{ColorFL.R} {ColorFL.G} {ColorFL.B} {ColorFL.A}"},
                {"colortemperature", 6500},
                {"brightness", 1f},
                {"range", 5000f},
                {"origin", $"{vecOrigin.X.ToString("F6", CultureInfo.InvariantCulture)} {vecOrigin.Y.ToString("F6", CultureInfo.InvariantCulture)} {vecOrigin.Z.ToString("F6", CultureInfo.InvariantCulture)}"},
                {"angles", $"{vecAngles.X.ToString("F6", CultureInfo.InvariantCulture)} {vecAngles.Y.ToString("F6", CultureInfo.InvariantCulture)} {vecAngles.Z.ToString("F6", CultureInfo.InvariantCulture)}"}
            };
            if (Flashlight._entityManager!.SpawnEntitySync<IBaseEntity>("light_omni2", kv) is { } entity)
            {
                entity.AcceptInput("SetParent", pawn, null, "!activator");
                entity.AcceptInput("SetParentAttachmentMaintainOffset", pawn, null, "axis_of_intent");

                Flashlight_Ent = entity;

                Flashlight._transmits!.AddEntityHooks(Flashlight_Ent, true);
            }
        }
        public bool HasFlashlight()
        {
            if (Flashlight_Ent != null && Flashlight_Ent.IsValid()) return true;
            return false;
        }
        public IBaseEntity? GetFlashLight()
        {
            if (HasFlashlight()) return Flashlight_Ent;
            return null;
        }
    }
}
