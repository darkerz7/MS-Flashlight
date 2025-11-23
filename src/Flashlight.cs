using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sharp.Extensions.GameEventManager;
using Sharp.Shared;
using Sharp.Shared.Abstractions;
using Sharp.Shared.Definition;
using Sharp.Shared.Enums;
using Sharp.Shared.GameEntities;
using Sharp.Shared.HookParams;
using Sharp.Shared.Managers;
using Sharp.Shared.Objects;
using Sharp.Shared.Types;
using Sharp.Shared.Units;
using Sharp.Modules.LocalizerManager.Shared;

namespace MS_Flashlight
{
    public class Flashlight : IModSharpModule
    {
        public string DisplayName => "Flashlight";
        public string DisplayAuthor => "DarkerZ[RUS]";

        public Flashlight(ISharedSystem sharedSystem, string dllPath, string sharpPath, Version version, IConfiguration coreConfiguration, bool hotReload)
        {
            _modSharp = sharedSystem.GetModSharp();
            _entityManager = sharedSystem.GetEntityManager();
            _transmits = sharedSystem.GetTransmitManager();
            _clientmanager = sharedSystem.GetClientManager();
            _modules = sharedSystem.GetSharpModuleManager();

            var services = new ServiceCollection();
            services.AddSingleton(sharedSystem);
            services.AddGameEventManager();
            _provider = services.BuildServiceProvider();
            _gameEventManager = _provider.GetRequiredService<IGameEventManager>();

            _hooks = sharedSystem.GetHookManager();
            _hotReload = hotReload;
        }

        private readonly IModSharp _modSharp;
        public static IEntityManager? _entityManager;
        public static ITransmitManager? _transmits;
        private readonly IClientManager _clientmanager;
        private readonly ISharpModuleManager _modules;
        private readonly IServiceProvider _provider;
        private readonly IGameEventManager _gameEventManager;
        private readonly IHookManager _hooks;
        private readonly bool _hotReload;

        private IModSharpModuleInterface<ILocalizerManager>? _localizer;

        static PlayerFlashlight[] g_PF = new PlayerFlashlight[65];
        static float g_fRainbowProgess = 0.0f;

        public bool Init()
        {
            for (int i = 0; i < g_PF.Length; i++) g_PF[i] = new PlayerFlashlight();

            if (_hotReload)
            {
                foreach (var player in GetControllers().ToArray())
                {
                    if (player is { IsValidEntity: true, IsFakeClient: false, IsHltv: false })
                    {
                        g_PF[player.PlayerSlot].SetPlayer(player);
                        if (player.GetPawn()?.LifeState == LifeState.Alive) g_PF[player.PlayerSlot].CanToggle = true;
                    }
                }
            }
            
            _provider.LoadAllSharpExtensions();
            _gameEventManager.HookEvent("player_connect_full", OnPlayerConnectFull);
            _gameEventManager.HookEvent("player_disconnect", OnPlayerDisconnect);
            _gameEventManager.HookEvent("player_spawn", OnPlayerSpawn);
            _gameEventManager.HookEvent("player_death", OnPlayerDeath);

            _hooks.PlayerRunCommand.InstallHookPost(OnPlayerRunCommandPost);

            _clientmanager.InstallCommandCallback("fl_color", OnChangeColor);
            _clientmanager.InstallCommandCallback("fl_rainbow", OnChangeRainbow);

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
            _clientmanager.RemoveCommandCallback("fl_color", OnChangeColor);
            _clientmanager.RemoveCommandCallback("fl_rainbow", OnChangeRainbow);

            _hooks.PlayerRunCommand.RemoveHookPost(OnPlayerRunCommandPost);

            foreach (var pfl in g_PF)
            {
                var player = pfl.GetPlayer();
                if (player != null && player.IsValid())
                {
                    g_PF[player.PlayerSlot].RemoveFlashlight();
                }
            }
        }

        private HookReturnValue<bool> OnPlayerConnectFull(IGameEvent e, ref bool serverOnly)
        {
            if (e.GetPlayerController("userid") is { } player && player is { IsFakeClient: false, IsHltv: false })
            {
                g_PF[player.PlayerSlot].SetPlayer(player);
            }
            return new HookReturnValue<bool>();
        }

        private HookReturnValue<bool> OnPlayerDisconnect(IGameEvent e, ref bool serverOnly)
        {
            if (e.GetPlayerController("userid") is { } player && player is { IsFakeClient: false, IsHltv: false })
            {
                g_PF[player.PlayerSlot].RemovePlayer();
            }
            return new HookReturnValue<bool>();
        }

        private HookReturnValue<bool> OnPlayerSpawn(IGameEvent e, ref bool serverOnly)
        {
            if (e.GetPlayerController("userid") is { } player && player is { IsFakeClient: false, IsHltv: false })
            {
                g_PF[player.PlayerSlot].CanToggle = true;
            }
            return new HookReturnValue<bool>();
        }

        private HookReturnValue<bool> OnPlayerDeath(IGameEvent e, ref bool serverOnly)
        {
            if (e.GetPlayerController("userid") is { } player && player is { IsFakeClient: false, IsHltv: false })
            {
                g_PF[player.PlayerSlot].CanToggle = false;

                g_PF[player.PlayerSlot].RemoveFlashlight();
            }
            return new HookReturnValue<bool>();
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
            foreach (var player in GetControllers().ToArray())
            {
                for (int i = 0; i < g_PF.Length; i++)
                {
                    if (player.PlayerSlot != i)
                    {
                        if (g_PF[i].GetFlashLight() is { } fl)
                        {
                            _transmits!.SetEntityState(fl.Index, player.Index, true, -1);
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

            foreach (var pfl in g_PF)
            {
                var player = pfl.GetPlayer();
                if (player != null && player.IsValid() && g_PF[player.PlayerSlot].Rainbow)
                {
                    if (g_PF[player.PlayerSlot].GetFlashLight() is { } FL)
                    {
                        FL.SetNetVar("m_Color", RainBowColor);
                    }
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

        private IEnumerable<IPlayerController> GetControllers()
        {
            var max = new PlayerSlot((byte)_modSharp!.GetGlobals().MaxClients);

            for (PlayerSlot slot = 0; slot <= max; slot++)
            {
                if (_entityManager!.FindPlayerControllerBySlot(slot) is { } c)
                {
                    yield return c;
                }
            }
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
    }
}
