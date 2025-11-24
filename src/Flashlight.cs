using Microsoft.Extensions.Configuration;
using Sharp.Modules.LocalizerManager.Shared;
using Sharp.Shared;
using Sharp.Shared.Definition;
using Sharp.Shared.Enums;
using Sharp.Shared.HookParams;
using Sharp.Shared.Listeners;
using Sharp.Shared.Managers;
using Sharp.Shared.Objects;
using Sharp.Shared.Types;
using Sharp.Shared.Units;

namespace MS_Flashlight
{
    public class Flashlight : IModSharpModule, IClientListener
    {
        public string DisplayName => "Flashlight";
        public string DisplayAuthor => "DarkerZ[RUS]";

        public Flashlight(ISharedSystem sharedSystem, string dllPath, string sharpPath, Version version, IConfiguration coreConfiguration, bool hotReload)
        {
            _modSharp = sharedSystem.GetModSharp();
            _entityManager = sharedSystem.GetEntityManager();
            _transmits = sharedSystem.GetTransmitManager();
            _clientManager = sharedSystem.GetClientManager();
            _hooks = sharedSystem.GetHookManager();
            _modules = sharedSystem.GetSharpModuleManager();
            _hotReload = hotReload;
        }

        private readonly IModSharp _modSharp;
        public static IEntityManager? _entityManager;
        public static ITransmitManager? _transmits;
        private readonly IClientManager _clientManager;
        private readonly IHookManager _hooks;
        private readonly ISharpModuleManager _modules;
        private readonly bool _hotReload;

        private IModSharpModuleInterface<ILocalizerManager>? _localizer;

        static PlayerFlashlight[] g_PF = new PlayerFlashlight[PlayerSlot.MaxPlayerCount];
        static float g_fRainbowProgess = 0.0f;

        public bool Init()
        {
            for (int i = 0; i < g_PF.Length; i++) g_PF[i] = new PlayerFlashlight();

            if (_hotReload)
            {
                foreach (var player in _entityManager!.GetPlayerControllers(true).ToArray())
                {
                    if (player is { IsValidEntity: true, IsFakeClient: false, IsHltv: false })
                    {
                        g_PF[player.PlayerSlot].SetPlayer(player);
                        if (player.GetPawn()?.LifeState == LifeState.Alive) g_PF[player.PlayerSlot].CanToggle = true;
                    }
                }
            }

            _clientManager.InstallClientListener(this);
            _clientManager.InstallCommandCallback("fl_color", OnChangeColor);
            _clientManager.InstallCommandCallback("fl_rainbow", OnChangeRainbow);

            _hooks.PlayerSpawnPost.InstallForward(OnPlayerSpawn);
            _hooks.PlayerKilledPre.InstallForward(OnPlayerKilled);
            _hooks.PlayerRunCommand.InstallHookPost(OnPlayerRunCommandPost);

            return true;
        }

        public void PostInit()
        {
            _modSharp.PushTimer(OnTransmit, 0.1f, GameTimerFlags.Repeatable);
            _modSharp.PushTimer(OnTimerRainbow, 0.1f, GameTimerFlags.Repeatable);
        }

        public void OnAllModulesLoaded()
        {
            GetLocalizer()?.LoadLocaleFile("FlashLight");
        }

        public void Shutdown()
        {
            _clientManager.RemoveClientListener(this);
            _clientManager.RemoveCommandCallback("fl_color", OnChangeColor);
            _clientManager.RemoveCommandCallback("fl_rainbow", OnChangeRainbow);

            _hooks.PlayerSpawnPost.RemoveForward(OnPlayerSpawn);
            _hooks.PlayerKilledPre.RemoveForward(OnPlayerKilled);
            _hooks.PlayerRunCommand.RemoveHookPost(OnPlayerRunCommandPost);

            foreach (var pfl in g_PF)
            {
                pfl.RemoveFlashlight();
            }
        }

        public void OnClientPutInServer(IGameClient client)
        {
            var player = client.GetPlayerController();
            if (player != null && player.IsValid())
            {
                g_PF[client.Slot].SetPlayer(player);
            }
        }

        public void OnClientDisconnected(IGameClient client)
        {
            g_PF[client.Slot].RemovePlayer();
        }

        private void OnPlayerSpawn(IPlayerSpawnForwardParams @params)
        {
            if (@params.Client is { } client && client is { IsFakeClient: false, IsHltv: false })
            {
                g_PF[client.Slot].CanToggle = true;
            }
        }

        private void OnPlayerKilled(IPlayerKilledForwardParams @params)
        {
            if (@params.Client is { } client && client is { IsFakeClient: false, IsHltv: false })
            {
                g_PF[client.Slot].CanToggle = false;

                g_PF[client.Slot].RemoveFlashlight();
            }
        }

        private void OnPlayerRunCommandPost(IPlayerRunCommandHookParams @params, HookReturnValue<EmptyHookReturn> @return)
        {
            if (@params.Service.KeyChangedButtons.HasFlag(UserCommandButtons.LookAtWeapon)
                && @params.Service.KeyButtons.HasFlag(UserCommandButtons.LookAtWeapon))
            {
                var slot = @params.Client.Slot;
                if (g_PF[slot].CanToggle && !g_PF[slot].NotSpam)
                {
                    g_PF[slot].NotSpam = true;
                    if (g_PF[slot].HasFlashlight()) g_PF[slot].RemoveFlashlight();
                    else g_PF[slot].SpawnFlashlight();
                    _modSharp.PushTimer(() => { g_PF[slot].NotSpam = false; }, 0.5f);
                }
            }
        }

        private void OnTransmit()
        {
            foreach (var player in _entityManager!.GetPlayerControllers(true).ToArray())
            {
                for (int i = 0; i < g_PF.Length; i++)
                {
                    if (player.PlayerSlot != i)
                    {
                        if (g_PF[i].GetFlashLight() is { } fl)
                        {
                            _transmits!.SetEntityState(fl.Index, player.Index, false, -1);
                        }
                    }
                }
            }
        }

        private void OnTimerRainbow()
        {
            g_fRainbowProgess += 0.005f;
            if (g_fRainbowProgess >= 1.0f) g_fRainbowProgess = 0.0f;
            
            Color32 RainBowColor = Rainbow(g_fRainbowProgess);

            foreach(var client in _clientManager.GetGameClients(true))
            {
                if (g_PF[client.Slot].Rainbow && g_PF[client.Slot].GetFlashLight() is { } FL)
                {
                    FL.SetNetVar("m_Color", RainBowColor);
                }
            }
        }

        private ECommandAction OnChangeColor(IGameClient client, StringCommand command)
        {
            if (client.IsValid)
            {
                var player = client.GetPlayerController()!;
                byte iRed = 255, iGreen = 255, iBlue = 255;
                if (command.ArgCount >= 3)
                {
                    _ = byte.TryParse(command.GetArg(1), out iRed);
                    _ = byte.TryParse(command.GetArg(2), out iGreen);
                    _ = byte.TryParse(command.GetArg(3), out iBlue);
                }

                g_PF[player.PlayerSlot].ColorFL = new(iRed, iGreen, iBlue, 255);

                if (g_PF[player.PlayerSlot].GetFlashLight() is { } FL)
                {
                    FL.SetNetVar("m_Color", g_PF[player.PlayerSlot].ColorFL);
                }
                if (GetLocalizer() is { } lm)
                {
                    var localizer = lm.GetLocalizer(client);
                    if (command.ChatTrigger) player.Print(HudPrintChannel.Chat, $" {ChatColor.Blue}[{ChatColor.Green}Flashlight{ChatColor.Blue}] {ChatColor.White}{localizer.Format("FlashLight_SetColor")} {ChatColor.Red}{iRed} {ChatColor.Green}{iGreen} {ChatColor.DarkBlue}{iBlue}");
                    else player.Print(HudPrintChannel.Console, $"[Flashlight] {localizer.Format("FlashLight_SetColor")} {iRed} {iGreen} {iBlue}");
                }
            }

            return ECommandAction.Stopped;
        }

        private ECommandAction OnChangeRainbow(IGameClient client, StringCommand command)
        {
            if (client.IsValid)
            {
                var player = client.GetPlayerController()!;
                if (g_PF[player.PlayerSlot].Rainbow)
                {
                    g_PF[player.PlayerSlot].Rainbow = false;
                    if (g_PF[player.PlayerSlot].GetFlashLight() is { } FL)
                    {
                        FL.SetNetVar("m_Color", g_PF[player.PlayerSlot].ColorFL);
                    }
                    if (GetLocalizer() is { } lm)
                    {
                        var localizer = lm.GetLocalizer(client);
                        if (command.ChatTrigger) player.Print(HudPrintChannel.Chat, $" {ChatColor.Blue}[{ChatColor.Green}Flashlight{ChatColor.Blue}] {ChatColor.White}{localizer.Format("FlashLight_You")} {ChatColor.Red}{localizer.Format("FlashLight_Disabled")} {ChatColor.Red}r{ChatColor.Gold}a{ChatColor.Yellow}i{ChatColor.Green}n{ChatColor.Blue}b{ChatColor.DarkBlue}o{ChatColor.Purple}w {ChatColor.White}{localizer.Format("FlashLight_Flashlight")}");
                        else player.Print(HudPrintChannel.Console, $"[Flashlight] {localizer.Format("FlashLight_You")} {localizer.Format("FlashLight_Disabled")} rainbow {localizer.Format("FlashLight_Flashlight")}");
                    }  
                }
                else
                {
                    g_PF[player.PlayerSlot].Rainbow = true;
                    if (GetLocalizer() is { } lm)
                    {
                        var localizer = lm.GetLocalizer(client);
                        if (command.ChatTrigger) player.Print(HudPrintChannel.Chat, $" {ChatColor.Blue}[{ChatColor.Green}Flashlight{ChatColor.Blue}] {ChatColor.White}{localizer.Format("FlashLight_You")} {ChatColor.Green}{localizer.Format("FlashLight_Enabled")} {ChatColor.Red}r{ChatColor.Gold}a{ChatColor.Yellow}i{ChatColor.Green}n{ChatColor.Blue}b{ChatColor.DarkBlue}o{ChatColor.Purple}w {ChatColor.White}{localizer.Format("FlashLight_Flashlight")}");
                        else player.Print(HudPrintChannel.Console, $"[Flashlight] {localizer.Format("FlashLight_You")} {localizer.Format("FlashLight_Enabled")} rainbow {localizer.Format("FlashLight_Flashlight")}");
                    }
                }
            }

            return ECommandAction.Stopped;
        }

        private static Color32 Rainbow(float progress)
        {
            float div = (Math.Abs(progress % 1) * 6);
            byte ascending = (byte)((div % 1) * 255);
            byte descending = (byte)(255 - ascending);

            return (byte)div switch
            {
                0 => new Color32(255, 255, ascending, 0),
                1 => new Color32(255, descending, 255, 0),
                2 => new Color32(255, 0, 255, ascending),
                3 => new Color32(255, 0, descending, 255),
                4 => new Color32(255, ascending, 0, 255),
                _ => new Color32(255, 255, 0, descending),
            };
        }

        private ILocalizerManager? GetLocalizer()
        {
            if (_localizer?.Instance is null)
            {
                _localizer = _modules.GetOptionalSharpModuleInterface<ILocalizerManager>(ILocalizerManager.Identity);
            }
            return _localizer?.Instance;
        }

        int IClientListener.ListenerVersion => IClientListener.ApiVersion;
        int IClientListener.ListenerPriority => 0;
    }
}
