using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Dalamud.Data;
using Dalamud.Game;
using Dalamud.Game.ClientState.Fates;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.Command;
using Dalamud.Game.Gui;
using Dalamud.Game.Network;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.IoC;
using Dalamud.Logging;
using Dalamud.Plugin;
using SamplePlugin;
using Fate = Lumina.Excel.GeneratedSheets.Fate;

namespace FateNotifier
{
    public sealed class Plugin : IDalamudPlugin
    {
        private const string CommandToggle = "/fate_notifier";
        private const string CommandChatType = "/fate_print_type";

        private readonly IntPtr _mapIdIntPtr1;
        private readonly IntPtr _mapIdIntPtr2;

        private readonly HashSet< int > _skirmishes3 = new() { 1742, 1741, 1740, 1739, 1738, 1737, 1736, 1735, 1734, 1733 };

        private bool _enabled;
        private bool _partyMode;
        private ProcessChatBoxDelegate? _processChatBox;
        private IntPtr _uiModule = IntPtr.Zero;

        public Plugin(
            [RequiredVersion( "1.0" )] DalamudPluginInterface pluginInterface,
            [RequiredVersion( "1.0" )] CommandManager commandManager, GameNetwork network, ChatGui chat,
            DataManager manager, FateTable fateTable, SigScanner scanner, Framework framework, ObjectTable objectTable, GameGui gameGui )
        {
            PluginInterface = pluginInterface;
            CommandManager = commandManager;
            Network = network;
            Chat = chat;
            Manager = manager;
            FateTable = fateTable;
            Scanner = scanner;
            ObjectTable = objectTable;
            GameGui = gameGui;

            Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            Configuration.Initialize( PluginInterface );

            GetAddress();
            _mapIdIntPtr1 = scanner.GetStaticAddressFromSig( "44 8B 3D ?? ?? ?? ?? 45 85 FF" );
            _mapIdIntPtr2 = scanner.GetStaticAddressFromSig( "44 0F 44 3D ?? ?? ?? ??" );

            PluginUi = new PluginUi( Configuration );

            CommandManager.AddHandler( CommandToggle, new CommandInfo( OnToggleCommand )
            {
                HelpMessage = "Toggle on/off the fate notifier"
            } );
            CommandManager.AddHandler( CommandChatType, new CommandInfo( OnPrintTypeCommand )
            {
                HelpMessage = "Print the fate messages as party or echo"
            } );


            PluginInterface.UiBuilder.Draw += DrawUi;
            PluginInterface.UiBuilder.OpenConfigUi += DrawConfigUi;
            Network.NetworkMessage += OnNetworkMessage;
        }

        // ReSharper disable once AutoPropertyCanBeMadeGetOnly.Local
        [PluginService] private GameNetwork Network { get; set; }

        private DalamudPluginInterface PluginInterface { get; }
        private CommandManager CommandManager { get; }
        private Configuration Configuration { get; }
        private PluginUi PluginUi { get; }
        private ChatGui Chat { get; }
        private DataManager Manager { get; }
        private FateTable FateTable { get; }
        private SigScanner Scanner { get; }
        private ObjectTable ObjectTable { get; }
        private GameGui GameGui { get; }
        private unsafe uint MapId => *( uint* )_mapIdIntPtr1 == 0 ? *( uint* )_mapIdIntPtr2 : *( uint* )_mapIdIntPtr1;
        public string Name => "Le Bozja Helper";

        public void Dispose()
        {
            PluginUi.Dispose();
            CommandManager.RemoveHandler( CommandToggle );
            CommandManager.RemoveHandler( CommandChatType );
            Network.NetworkMessage -= OnNetworkMessage;
        }

        // Taken from QOL
        private unsafe void GetAddress()
        {
            try
            {
                var getUiModulePtr = Scanner.ScanText( "E8 ?? ?? ?? ?? 48 83 7F ?? 00 48 8B F0" );
                var easierProcessChatBoxPtr = Scanner.ScanText( "48 89 5C 24 ?? 57 48 83 EC 20 48 8B FA 48 8B D9 45 84 C9" );
                var uiModulePtr = Scanner.GetStaticAddressFromSig( "48 8B 0D ?? ?? ?? ?? 48 8D 54 24 ?? 48 83 C1 10 E8" );

                var getUiModule = Marshal.GetDelegateForFunctionPointer< GetUiModuleDelegate >( getUiModulePtr );

                _uiModule = getUiModule( *( IntPtr* )uiModulePtr );
                _processChatBox = Marshal.GetDelegateForFunctionPointer< ProcessChatBoxDelegate >( easierProcessChatBoxPtr );
            }
            catch
            {
                PluginLog.Error( "Failed loading 'ExecuteCommand'" );
            }
        }

        public void ExecuteCommand( string command )
        {
            try
            {
                var bytes = Encoding.UTF8.GetBytes( command );

                var mem1 = Marshal.AllocHGlobal( 400 );
                var mem2 = Marshal.AllocHGlobal( bytes.Length + 30 );

                Marshal.Copy( bytes, 0, mem2, bytes.Length );
                Marshal.WriteByte( mem2 + bytes.Length, 0 );
                Marshal.WriteInt64( mem1, mem2.ToInt64() );
                Marshal.WriteInt64( mem1 + 8, 64 );
                Marshal.WriteInt64( mem1 + 8 + 8, bytes.Length + 1 );
                Marshal.WriteInt64( mem1 + 8 + 8 + 8, 0 );

                _processChatBox!( _uiModule, mem1, IntPtr.Zero, 0 );

                Marshal.FreeHGlobal( mem1 );
                Marshal.FreeHGlobal( mem2 );
            }
            catch ( Exception err )
            {
                Chat.PrintError( err.Message );
            }
        }

        private void OnToggleCommand( string command, string args )
        {
            _enabled = !_enabled;
            Chat.Print( "[Fate] Print message: " + ( _enabled ? "ON" : "OFF" ) );
        }

        private void OnPrintTypeCommand( string command, string args )
        {
            _partyMode = !_partyMode;
            Chat.Print( "[Fate] Message type: " + ( _partyMode ? "Party" : "Echo" ) );
        }

        private void DrawUi()
        {
            PluginUi.Draw();
        }

        private void DrawConfigUi()
        {
            PluginUi.SettingsVisible = true;
        }

        private static float ToMapCoordinate( float val, float scale )
        {
            var c = scale / 100.0f;

            val *= c;

            return 41.0f / c * ( ( val + 1024.0f ) / 2048.0f ) + 1;
        }

        private void OnNetworkMessage( IntPtr dataPtr, ushort opCode, uint sourceActorId, uint targetActorId,
            NetworkMessageDirection direction )
        {
            if ( !_enabled )
                return;

            if ( direction != NetworkMessageDirection.ZoneDown )
                return;

            // TODO: Get opcode remotely
            if ( opCode != 0x0354 ) // ActorControlSelf CN 5.55
                return;

            var bytes = new byte[ 32 ];
            Marshal.Copy( dataPtr, bytes, 0, 32 );

            var type = BitConverter.ToInt16( bytes, 0 );
            var fateId = BitConverter.ToInt32( bytes, 4 );
            var fateInfo = Manager.GameData.Excel.GetSheet< Fate >()?.GetRow( ( uint )fateId );

            switch ( type )
            {
                // FateInit
                case 2353:
                {
                    foreach ( var fate in FateTable )
                    {
                        // ReSharper disable once ConditionIsAlwaysTrueOrFalse
                        if ( fate is null ) continue;

                        // TODO: Fully support Zadnor and the Bozjan Southern Front
                        if ( fate.Name.TextValue != fateInfo!.Name.RawString && _skirmishes3.Contains( fate.FateId ) == false ) continue;

                        var x = ToMapCoordinate( fate.Position.X, fate.TerritoryType.GameData.Map.Value!.SizeFactor );
                        var y = ToMapCoordinate( fate.Position.Z, fate.TerritoryType.GameData.Map.Value!.SizeFactor );

                        var mapLinkPayload = new MapLinkPayload( fate.TerritoryType.GameData.RowId, fate.TerritoryType.GameData.Map.Row, x, y );

                        GameGui.OpenMapWithMapLink( mapLinkPayload );

                        if ( !_partyMode )
                        {
                            var msg = new SeString(
                                mapLinkPayload,
                                new UIForegroundPayload( 500 ),
                                new UIGlowPayload( 501 ),
                                new TextPayload( $"{( char )SeIconChar.LinkMarker}" ),
                                new UIForegroundPayload( 0 ),
                                new UIGlowPayload( 0 ),
                                new UIForegroundPayload( 502 ),
                                new TextPayload( $"[FateInit] {fate.Name} ==> CoordX: {mapLinkPayload.XCoord} CoordY:{mapLinkPayload.YCoord} | {fate.FateId}" ),
                                new UIForegroundPayload( 0 ),
                                RawPayload.LinkTerminator
                            );

                            XivChatEntry entry = new()
                            {
                                Type = XivChatType.Debug,
                                Message = msg
                            };

                            Chat.PrintChat( entry );
                        }
                        else
                        {
                            ExecuteCommand( $"/p Fate 《{fate.Name.TextValue}》 已刷新 <flag>" );
                        }

                        break;
                    }

                    break;
                }

                // FateProgress
                case 2366:
                {
                    /*
                    var progress = BitConverter.ToInt32( bytes, 8 );

                    foreach ( var fate in FateTable )
                    {
                        // ReSharper disable once ConditionIsAlwaysTrueOrFalse
                        if ( fate is null ) continue;

                        if ( fate.Name.TextValue != fateInfo.Name.RawString ) continue;

                        var x = ToMapCoordinate( fate.Position.X, fate.TerritoryType.GameData.Map.Value!.SizeFactor );
                        var y = ToMapCoordinate( fate.Position.Z, fate.TerritoryType.GameData.Map.Value!.SizeFactor );

                        var mapLinkPayload = new MapLinkPayload( fate.TerritoryType.GameData.RowId, fate.TerritoryType.GameData.Map.Row, x, y );

                        GameGui.OpenMapWithMapLink( mapLinkPayload );

                        var msg = new SeString(
                            mapLinkPayload,
                            new UIForegroundPayload( 500 ),
                            new UIGlowPayload( 501 ),
                            new TextPayload( $"{( char )SeIconChar.LinkMarker}" ),
                            new UIForegroundPayload( 0 ),
                            new UIGlowPayload( 0 ),
                            new UIForegroundPayload( 502 ),
                            new TextPayload( $"[FateProgress] {fate.Name} ==> {fate.Progress}% | RawX: {mapLinkPayload.RawX} RawY: {mapLinkPayload.RawY} CoordX: {mapLinkPayload.XCoord} CorrdY:{mapLinkPayload.YCoord}" ),
                            new UIForegroundPayload( 0 ),
                            RawPayload.LinkTerminator
                        );

                        XivChatEntry entry = new()
                        {
                            Type = _partyMode ? XivChatType.Party : XivChatType.Echo,
                            Message = msg,
                            SenderId = _partyMode ? ObjectTable[ 0 ]!.ObjectId : 0,
                            Name = _partyMode ? ObjectTable[ 0 ]!.Name : "\0"
                        };

                        ExecuteCommand( $"/e Fate 《{fate.Name.TextValue}》 已刷新 <flag>" );

                        Chat.PrintChat( entry );
                    }
                    */


                    break;
                } // FateProgress

                case 2358:
                {
                    // var progress = BitConverter.ToInt32( bytes, 8 );
                    //
                    // Chat.Print( "[FateEnd]" + " Name: " + fateInfo.Name + " | Location: " +
                    //             fateInfo.Location );

                    break;
                }
            }
        }

        private delegate void ProcessChatBoxDelegate( IntPtr uiModule, IntPtr message, IntPtr unused, byte a4 );

        private delegate IntPtr GetUiModuleDelegate( IntPtr basePtr );
    }
}