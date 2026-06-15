// MIT License - Copyright (c) Callum McGing
// This file is subject to the terms and conditions defined in
// LICENSE, which is part of this source code package

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime;
using System.Runtime.InteropServices;
using LibreLancer.Client;
using LibreLancer.Data.GameData;
using LibreLancer.ImUI.NodeEditor;
using LibreLancer.Input;
using LibreLancer.Interface;
using LibreLancer.Items;
using LibreLancer.Net;
using LibreLancer.Net.Protocol;
using LibreLancer.Render;
using LibreLancer.Resources;
using LibreLancer.Thn;
using LiteNetLib;
using WattleScript.Interpreter;
using DisconnectReason = LibreLancer.Net.DisconnectReason;

namespace LibreLancer
{
    public class LuaMenu : GameState
    {
        private UiContext ui;
        private readonly Cursor cur;
        private Cutscene? scene;
        private MenuAPI api;
        private KeyCaptureContext keyCapture = null!;

        private IntroScene intro;

        public LuaMenu(FreelancerGame g) : base(g)
        {
            api = new MenuAPI(this);
            ui = Game.Ui;
            ui.GameApi = api;
            ui.Visible = true;
            // Debug hook (like SIRIUS_INTRO): boot straight into a UI scene,
            // e.g. SIRIUS_OPEN_SCENE=options for settings screenshots.
            var bootScene = Environment.GetEnvironmentVariable("SIRIUS_OPEN_SCENE");
            ui.OpenScene(string.IsNullOrWhiteSpace(bootScene) ? "mainmenu" : bootScene, 0.4);
            g.GameData.PopulateCursors();
            g.CursorKind = CursorKind.None;
            intro = g.GameData.GetIntroScene();
            TryRunScript(intro.Scripts);
            FLLog.Info("Thn", "Playing " + intro.ThnName);
            cur = g.ResourceManager.GetCursor("arrow")!;
            GC.Collect(); // crap
            g.Sound.PlayMusic(intro.Music!, 0);
            g.Keyboard.KeyDown += UiKeyDown;
            g.Keyboard.TextInput += UiTextInput;
#if DEBUG
            g.Keyboard.KeyDown += Keyboard_KeyDown;
#endif
            g.Keyboard.KeyUp += Keyboard_OnKeyUp;
            g.Mouse.MouseUp += Mouse_MouseUp;
            Game.Saves.Selected = -1;

            if (g.LoadTimer != null)
            {
                g.LoadTimer.Stop();
                FLLog.Info("Game", $"Initial load took {g.LoadTimer.Elapsed.TotalSeconds} seconds");
                g.LoadTimer = null;
            }

            // Set low latency GC mode only once everything has been loaded in
            GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
            FadeIn(0.1, 0.3);
        }

        private void TryRunScript(List<ResolvedThn> thnScripts)
        {
            var intro = new List<ThnScript>();
            scene = new Cutscene(new ThnScriptContext(null), Game.GameData, Game.ResourceManager, Game.Sound,
                Game.RenderContext.CurrentViewport, Game);

            foreach (var s in thnScripts)
            {
#if !DEBUG
                try
                {
                    intro.Add(s.LoadScript());
                }
                catch (Exception e)
                {
                    FLLog.Error("Thn", $"Error loading script {s.SourcePath}: {e.Message}\n{e.StackTrace}");
                    scene = null;
                    return;
                }
#else
                intro.Add(s.LoadScript());
#endif
            }

            scene.BeginScene(intro);
        }

        public override void OnSettingsChanged()
        {
            scene?.Renderer?.Settings = Game.Config.Settings;
        }

        private void Mouse_MouseUp(MouseEventArgs e)
        {
            if (e.Buttons != MouseButtons.Left && KeyCaptureContext.Capturing(keyCapture))
            {
                keyCapture.Set(UserInput.FromMouse(e.Buttons));
            }
        }

        private void Keyboard_OnKeyUp(KeyEventArgs e)
        {
            if (!KeyCaptureContext.Capturing(keyCapture))
            {
                return;
            }

            if (e.Key != Keys.Escape &&
                e.Key != Keys.F1)
            {
                keyCapture.Set(UserInput.FromKey(e.Modifiers, e.Key));
            }
            else
            {
                keyCapture.Cancel();
            }
        }

        private void UiTextInput(string text)
        {
            if (!KeyCaptureContext.Capturing(keyCapture))
                ui.OnTextEntry(text);
        }

        private void UiKeyDown(KeyEventArgs e)
        {
            if (KeyCaptureContext.Capturing(keyCapture))
            {
                return;
            }

            if (e.Key == Keys.Escape)
            {
                ui.OnEscapePressed();
            }

            ui.OnKeyDown(e.Key, (e.Modifiers & KeyModifiers.Control) != 0);
        }

        [WattleScriptUserData]
        public class ServerList : ITableData
        {
            public List<LocalServerInfo> Servers = [];
            public int Count => Servers.Count;
            public int Selected { get; set; } = -1;

            public string? GetContentString(int row, string column)
            {
                if (row < 0 || row > Count || string.IsNullOrEmpty(column)) return null;

                switch (column.ToLowerInvariant())
                {
                    case "name":
                        return Servers[row].Name;
                    case "ip":
                        var addr = Servers[row].EndPoint.Address;
                        if (addr.IsIPv4MappedToIPv6)
                            return addr.MapToIPv4().ToString();
                        return addr.ToString();
                    case "visit":
                        return "NO";
                    case "ping":
                        return Servers[row].Ping.ToString();
                    case "players":
                        return $"{Servers[row].CurrentPlayers}/{Servers[row].MaxPlayers}";
                    case "version":
                        return Servers[row].DataVersion;
                    case "lan":
                        return "YES";
                    default:
                        return null;
                }
            }

            public string CurrentDescription()
            {
                if (Selected < 0 || Selected >= Count) return "";
                return Servers[Selected].Description;
            }

            public bool ValidSelection()
            {
                return (Selected >= 0 && Selected < Count);
            }

            public void Reset()
            {
                Selected = -1;
                Servers = [];
            }
        }

        [WattleScript.Interpreter.WattleScriptUserData]
        public class MenuAPI : UiApi
        {
            private LuaMenu state;

            public MenuAPI(LuaMenu m)
            {
                state = m;
            }

            public KeyMapTable GetKeyMap()
            {
                var table = new KeyMapTable(state.Game.InputMap, state.Game.GameData.Items.Ini.Infocards);
                table.OnCaptureInput += (k) => { state.keyCapture = k; };
                return table;
            }

            public GameSettings GetCurrentSettings() => state.Game.Config.Settings.MakeCopy();

            public void ApplySettings(GameSettings settings)
            {
                state.Game.Config.Settings = settings;
                state.Game.Config.Save();
            }

            public SaveGameFolder SaveGames() => state.Game.Saves;
            public void DeleteSelectedGame() => state.Game.Saves.TryDelete(state.Game.Saves.Selected);

            public void LoadSelectedGame()
            {
                state.FadeOut(0.2, () =>
                {
                    var embeddedServer = new EmbeddedServer(state.Game.GameData, state.Game.ResourceManager,
                        state.Game.GetSaveFolder());
                    var session = new CGameSession(state.Game, embeddedServer);
                    embeddedServer.StartFromSave(state.Game.Saves.SelectedFile!,
                        File.ReadAllBytes(state.Game.Saves.SelectedFile!));
                    state.Game.ChangeState(new NetWaitState(session, state.Game));
                });
            }

            public override void NewGame()
            {
                state.FadeOut(0.2, () =>
                {
                    var embeddedServer = new EmbeddedServer(state.Game.GameData, state.Game.ResourceManager,
                        state.Game.GetSaveFolder());
                    var session = new CGameSession(state.Game, embeddedServer);
                    var saveBytes = state.Game.GameData.VFS.ReadAllBytes("EXE\\newplayer.fl");
                    // SIRIUS_SPAWN="system,base" starts the fresh game
                    // elsewhere - multi-system effect captures (pairs with
                    // SIRIUS_TELEPORT for the in-space pose).
                    if (Environment.GetEnvironmentVariable("SIRIUS_SPAWN") is { Length: > 0 } spawn &&
                        spawn.Split(',') is { Length: 2 } spawnParts)
                    {
                        var text = System.Text.Encoding.ASCII.GetString(saveBytes);
                        text = System.Text.RegularExpressions.Regex.Replace(
                            text, @"(?m)^system = .*$", "system = " + spawnParts[0].Trim());
                        text = System.Text.RegularExpressions.Regex.Replace(
                            text, @"(?m)^base = .*$", "base = " + spawnParts[1].Trim());
                        saveBytes = System.Text.Encoding.ASCII.GetBytes(text);
                        FLLog.Info("Autoplay", $"Spawn override: {spawnParts[0]} / {spawnParts[1]}");
                    }
                    // SIRIUS_SHIP="<archetype>" swaps the player ship archetype
                    // in the fresh save so the golden pose frames any hull for
                    // per-ship PBR-wire FLIP tests (e.g. li_elite, li_freighter).
                    if (Environment.GetEnvironmentVariable("SIRIUS_SHIP") is { Length: > 0 } shipOverride)
                    {
                        var text = System.Text.Encoding.ASCII.GetString(saveBytes);
                        text = System.Text.RegularExpressions.Regex.Replace(
                            text, @"(?m)^ship_archetype = .*$", "ship_archetype = " + shipOverride.Trim());
                        saveBytes = System.Text.Encoding.ASCII.GetBytes(text);
                        FLLog.Info("Autoplay", $"Ship override: {shipOverride.Trim()}");
                    }
                    embeddedServer.StartFromSave("EXE\\newplayer.fl", saveBytes);
                    state.Game.ChangeState(new NetWaitState(session, state.Game));
                });
            }

            private UiNewCharacter[] newCharacters = null!;

            public UiNewCharacter[] GetNewCharacters() => newCharacters;

            private void ResolveNicknames(SelectableCharacter c)
            {
                c.Ship = state.Game.GameData.GetString(state.Game.GameData.Items.Ships.Get(c.Ship)!.IdsName);
                c.Location = state.Game.GameData.GetString(state.Game.GameData.Items.Systems.Get(c.Location)!.IdsName);
            }

            internal void _Update()
            {
                if (netClient == null)
                {
                    return;
                }

                while (netClient.PollPacket(out var pkt))
                {
                    switch (pkt)
                    {
                        case OpenCharacterListPacket oclist:
                            FLLog.Info("Net", "Opening Character List");
                            this.cselInfo = oclist.Info;
                            foreach (var sc in oclist.Info.Characters)
                                ResolveNicknames(sc);
                            state.ui.Event("CharacterList");
                            break;
                        case AddCharacterPacket ac:
                            ResolveNicknames(ac.Character);
                            cselInfo.Characters.Add(ac.Character);
                            break;
                        case NewCharacterDBPacket ncdb:
                        {
                            newCharacters = new UiNewCharacter[ncdb.Factions.Count];

                            for (int i = 0; i < ncdb.Factions.Count; i++)
                            {
                                var package = ncdb.Packages.First(x =>
                                    x.Nickname.Equals(ncdb.Factions[i].Package, StringComparison.OrdinalIgnoreCase));
                                var ship = state.Game.GameData.Items.Ships.Get(package.Ship)!;
                                ship.ModelFile!.LoadFile(state.Game.ResourceManager);
                                var loc = state.Game.GameData.GetString(
                                    state.Game.GameData.Items.Bases.Get(ncdb.Factions[i].Base)!.IdsName);
                                newCharacters[i] = new UiNewCharacter()
                                {
                                    Money = package.Money,
                                    StridDesc = package.StridDesc,
                                    StridName = package.StridName,
                                    ShipName = state.Game.GameData.GetString(ship.IdsName),
                                    ShipModel = ship.ModelFile!.ModelFile!,
                                    Location = loc
                                };
                            }

                            state.ui.Event("OpenNewCharacter");
                            break;
                        }
                        default:
                            netSession.HandlePacket(pkt);
                            break;
                    }

                }
            }

            private GameNetClient? netClient;
            private CGameSession netSession = null!;
            private ServerList serverList = new();
            private CharacterSelectInfo cselInfo = null!;

            public CharacterSelectInfo CharacterList() => cselInfo;
            public ServerList ServerList() => serverList;

            public void StartNetworking()
            {
                StopNetworking();
                netClient = new GameNetClient(state.Game);
                netSession = new CGameSession(state.Game, netClient);
                netClient.UUID = state.Game.Config.UUID;
                netClient.ServerFound += info => serverList.Servers.Add(info);
                netClient.Disconnected += NetClientOnDisconnected;
                netClient.AuthenticationRequired += NetClientOnAuthenticationRequired;
                netClient.Start();
                RefreshServers();
            }

            private void NetClientOnAuthenticationRequired(bool retry)
            {
                if (retry) state.ui.Event("IncorrectPassword");
                else state.ui.Event("Login");
            }

            public void Login(string username, string password)
            {
                netClient?.Login(username, password);
            }

            public void RequestNewCharacter()
            {
                netSession.RpcServer.RequestCharacterDB();
            }

            public void LoadCharacter()
            {
                netSession.RpcServer.SelectCharacter(cselInfo.Selected).ContinueWith(x => state.Game.QueueUIThread(() =>
                {
                    if (x.Result)
                    {
                        state.FadeOut(0.2, () =>
                        {
                            netClient!.Disconnected += (reason) => netSession.Disconnected();
                            netClient.Disconnected -= NetClientOnDisconnected;
                            netClient = null;
                            state.Game.ChangeState(new NetWaitState(netSession, state.Game));
                        });
                    }
                    else
                    {
                        state.ui.Event("SelectCharFailure");
                    }
                }));

            }

            private int delIndex = -1;

            public void DeleteCharacter()
            {
                delIndex = cselInfo.Selected;
                netSession.RpcServer.DeleteCharacter(cselInfo.Selected).ContinueWith((t) =>
                {
                    if (t.Result)
                    {
                        state.Game.QueueUIThread(() =>
                        {
                            cselInfo.Characters.RemoveAt(delIndex);
                            delIndex = -1;
                        });
                    }
                });
            }

            private void NetClientOnDisconnected(DisconnectReason reason)
            {
                netClient?.Shutdown();
                netClient = null;
                state.ui.Event("Disconnect", reason.ToString());
            }

            public void RefreshServers()
            {
                serverList.Reset();
                netClient!.DiscoverLocalPeers();
            }

            public void ConnectSelection()
            {
                if (serverList.Selected != -1)
                {
                    netClient!.Connect(serverList.Servers[serverList.Selected].EndPoint);
                }
            }

            public void ConnectAddress(string address) => netClient!.Connect(address);

            public void NewCharacter(string name, int index, Closure onError)
            {
                FLLog.Info("Net", $"Requesting new char: `{name}`");
                netSession.RpcServer.CreateNewCharacter(name, index).ContinueWith((task) =>
                {
                    if (!task.Result) state.Game.QueueUIThread(() => onError.Call());
                });
            }

            public void StopNetworking()
            {
                netClient?.Shutdown();
                netClient = null;
            }

            public override void Exit() => state.FadeOut(0.2, () => state.Game.Exit());
        }

        public override void Draw(double delta)
        {
            RenderMaterial.VertexLighting = true;
            scene?.Draw(delta, Game.Width, Game.Height);
            ui.RenderWidget(delta);
            DoFade(delta);
            var dlist = Game.RenderContext.Renderer2D.CreateDrawList();
            cur.Draw(dlist, Game.Mouse, RenderClock.Get(Game.TotalTime));
            dlist.Render();
        }

        private int uframe = 0;
        private bool newUI = false;
        // Golden runs wait longer: the menu button reveal animation needs
        // several seconds to fully converge - capturing mid-lerp leaves a
        // subpixel clip-edge difference between runs.
        private double autoplayTimer = SiriusAutoplay.GoldenDir != null ? 8.0 : 2.0;
        private bool autoplayStarted;
        private bool autoplayMenuShot;
        private double menuTime;
        // SIRIUS_MENU_SHOT_DELAY=seconds: take menu.png this long after the
        // menu opens instead of at the settled default - lets the harness
        // capture the fly-in animations mid-flight.
        private static readonly double? menuShotDelay =
            double.TryParse(Environment.GetEnvironmentVariable("SIRIUS_MENU_SHOT_DELAY"),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null;
        private readonly SiriusUiAutotest? uiTest = SiriusUiAutotest.CreateMenuWalker();

        public override void Update(double delta)
        {
            ui.Update(Game);
            Game.TextInputEnabled = ui.KeyboardGrabbed;
            scene?.UpdateViewport(Game.RenderContext.CurrentViewport, (float) Game.Width / Game.Height);
            // Golden captures freeze the THN intro at its first frame - the
            // scene animates ships, so any timing jitter between runs would
            // otherwise diff the screenshot.
            scene?.Update(SiriusAutoplay.GoldenDir != null ? 0 : delta);
            api._Update();
            menuTime += delta;
            uiTest?.Update(ui, Game, delta);
            if (SiriusAutoplay.Enabled && !autoplayStarted)
            {
                autoplayTimer -= delta;
                // Let the button reveal play out on live time, then freeze
                // the presentation clock: the shine sweep loops forever and
                // would otherwise never align between runs.
                // No freeze while the click autotest drives the menu: lua
                // Timer() callbacks (scene exit animations) read this clock.
                if (SiriusAutoplay.GoldenDir != null && menuShotDelay == null &&
                    !SiriusUiAutotest.MenuActive && autoplayTimer <= 2.5)
                {
                    RenderClock.Freeze(100.0);
                }
                if (SiriusAutoplay.GoldenDir != null && !autoplayMenuShot &&
                    (menuShotDelay.HasValue ? menuTime >= menuShotDelay.Value : autoplayTimer <= 0.5))
                {
                    autoplayMenuShot = true;
                    Game.Screenshot(System.IO.Path.Combine(SiriusAutoplay.GoldenDir, "menu.png"));
                    FLLog.Info("Autoplay", "golden: menu.png");
                }
                // The click autotest drives the menu itself (and covers
                // NEW GAME/EXIT through real button presses).
                if (autoplayTimer <= 0 && !SiriusUiAutotest.MenuActive)
                {
                    autoplayStarted = true;
                    FLLog.Info("Autoplay", "SIRIUS_AUTOPLAY: starting new game");
                    api.NewGame();
                }
            }
        }
#if DEBUG
        void LoadSpecific(int index)
        {
            intro = Game.GameData.GetIntroSceneSpecific(index);
            scene?.Dispose();
            TryRunScript(intro.Scripts);
            scene?.Update(1 / 60.0); // Do all the setup events - smoother entrance
            Game.Sound.PlayMusic(intro.Music, 0);
        }

        void Keyboard_KeyDown(KeyEventArgs e)
        {
            if ((e.Modifiers & KeyModifiers.LeftControl) == KeyModifiers.LeftControl)
            {
                switch (e.Key)
                {
                    case Keys.D1:
                        LoadSpecific(0);
                        break;
                    case Keys.D2:
                        LoadSpecific(1);
                        break;
                    case Keys.D3:
                        LoadSpecific(2);
                        break;
                }
            }
        }
#endif

        public override void Exiting()
        {
            api.StopNetworking(); // Disconnect
        }

        protected override void OnUnload()
        {
            scene?.Dispose();
            Game.Keyboard.KeyDown -= UiKeyDown;
            Game.Keyboard.TextInput -= UiTextInput;
#if DEBUG
            Game.Keyboard.KeyDown -= Keyboard_KeyDown;
#endif
            Game.Keyboard.KeyUp -= Keyboard_OnKeyUp;
            Game.Mouse.MouseUp -= Mouse_MouseUp;
        }
    }
}
