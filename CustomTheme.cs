using System.Windows;

namespace Veyro;

public partial class MainWindow
{
    private bool choosingColor;
    private ThemeEditorWindow? themeStudio;

    private void OpenThemeEditor(object sender, RoutedEventArgs e)
    {
        if(themeStudio is not null){themeStudio.Activate();return;}
        var original=preferences;
        choosingColor=true;
        themeStudio=new ThemeEditorWindow(ThemePalette.Resolve(preferences.Theme,preferences.CustomColors),
            colors=>{
                preferences=preferences with {Theme="Custom",CustomColors=colors};
                ApplyPreferences();
            },
            colors=>{
                preferences=preferences with {Theme="Custom",CustomColors=colors};
                SavePreferences();
            });
        SettingsPanel.Visibility=Visibility.Collapsed;
        MixerHeader.Visibility=Visibility.Visible;
        SessionScroll.Visibility=Visibility.Visible;
        RefreshMixer();
        UpdateLayout();
        var area=SystemParameters.WorkArea;
        themeStudio.Left=Left-themeStudio.Width-12;
        if(themeStudio.Left<area.Left)themeStudio.Left=System.Math.Min(area.Right-themeStudio.Width,Left+ActualWidth+12);
        themeStudio.Top=System.Math.Max(area.Top,Top-80);
        themeStudio.Closed+=(_,_)=>{
            if(!themeStudio.Saved){preferences=original;ApplyPreferences();}
            themeStudio=null;choosingColor=false;
            if(IsVisible)timer.Start();
        };
        themeStudio.Show();
        themeStudio.Top=System.Math.Clamp(themeStudio.Top,area.Top,System.Math.Max(area.Top,area.Bottom-themeStudio.ActualHeight));
    }
}
