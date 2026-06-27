namespace AutoOrder.Web.Components.Layout;

using MudBlazor;

public partial class MainLayout
{
    private bool drawerOpen = true;

    private readonly MudTheme theme = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = Colors.Blue.Darken3,
            PrimaryDarken = Colors.Blue.Darken4,
            PrimaryLighten = Colors.Blue.Darken2,
            Secondary = Colors.LightBlue.Darken2,
            AppbarBackground = Colors.Blue.Darken3,
            AppbarText = Colors.Shades.White,
            DrawerBackground = Colors.Gray.Lighten4,
            DrawerText = Colors.Gray.Darken4,
            Background = Colors.Gray.Lighten5,
            Surface = Colors.Shades.White,
        },
    };

    private void ToggleDrawer() => drawerOpen = !drawerOpen;
}
