using System;
using System.Numerics;
using ImGuiNET;

namespace SamplePlugin
{
    // It is good to have this be disposable in general, in case you ever need it
    // to do any cleanup
    internal class PluginUi : IDisposable
    {
        private readonly Configuration _configuration;

        private bool _settingsVisible;

        // this extra bool exists for ImGui, since you can't ref a property
        private bool _visible;

        // passing in the image here just for simplicity
        public PluginUi( Configuration configuration )
        {
            _configuration = configuration;
        }

        public bool Visible
        {
            get => _visible;
            set => _visible = value;
        }

        public bool SettingsVisible
        {
            get => _settingsVisible;
            set => _settingsVisible = value;
        }

        public void Dispose()
        {
        }

        public void Draw()
        {
            // This is our only draw handler attached to UIBuilder, so it needs to be
            // able to draw any windows we might have open.
            // Each method checks its own visibility/state to ensure it only draws when
            // it actually makes sense.
            // There are other ways to do this, but it is generally best to keep the number of
            // draw delegates as low as possible.

            DrawMainWindow();
            DrawSettingsWindow();
        }

        public void DrawMainWindow()
        {
            if ( !Visible ) return;

            ImGui.SetNextWindowSize( new Vector2( 375, 330 ), ImGuiCond.FirstUseEver );
            ImGui.SetNextWindowSizeConstraints( new Vector2( 375, 330 ),
                new Vector2( float.MaxValue, float.MaxValue ) );
            if ( ImGui.Begin( "My Amazing Window", ref _visible,
                ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse ) )
            {
                ImGui.Text( $"The random config bool is {_configuration.SomePropertyToBeSavedAndWithADefault}" );

                if ( ImGui.Button( "Show Settings" ) ) SettingsVisible = true;

                ImGui.Spacing();
            }

            ImGui.End();
        }

        public void DrawSettingsWindow()
        {
            if ( !SettingsVisible ) return;

            ImGui.SetNextWindowSize( new Vector2( 232, 75 ), ImGuiCond.Always );
            if ( ImGui.Begin( "A Wonderful Configuration Window", ref _settingsVisible,
                ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar |
                ImGuiWindowFlags.NoScrollWithMouse ) )
            {
                // can't ref a property, so use a local copy
                var configValue = _configuration.SomePropertyToBeSavedAndWithADefault;
                if ( ImGui.Checkbox( "Random Config Bool", ref configValue ) )
                {
                    _configuration.SomePropertyToBeSavedAndWithADefault = configValue;
                    // can save immediately on change, if you don't want to provide a "Save and Close" button
                    _configuration.Save();
                }
            }

            ImGui.End();
        }
    }
}