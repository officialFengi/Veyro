using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Veyro;

internal sealed class ThemeEditorWindow : Window
{
    private readonly Dictionary<string, string> colors;
    private readonly Action<Dictionary<string, string>> preview;
    private readonly Action<Dictionary<string, string>> save;
    private readonly Slider[] channels = new Slider[3];
    private readonly Dictionary<string, Button> swatches = new();
    private readonly TextBox hex = new();
    private readonly Border sample = new();
    private readonly TextBlock selectedLabel = new();
    private readonly TextBlock feedback = new();
    private string selected = "Accent";
    private bool updating;

    public bool Saved { get; private set; }

    internal ThemeEditorWindow(string[] palette, Action<Dictionary<string,string>> preview, Action<Dictionary<string,string>> save)
    {
        this.preview = preview;
        this.save = save;
        colors = new Dictionary<string,string>();
        for (var i = 0; i < ThemePalette.Keys.Length; i++) colors[ThemePalette.Keys[i]] = palette[i];
        Title = "Veyro · Студия темы";
        Width = 430;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Foreground = Brush("#F0F3FA");
        FontFamily = new FontFamily("Segoe UI");
        Topmost = true;
        ShowInTaskbar = false;
        var root = new StackPanel();
        Content = new Border { Background = Brush("#171B24"), BorderBrush = Brush("#394254"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(22), Padding = new Thickness(24), Child = root };
        var header = new DockPanel { Margin = new Thickness(0,0,0,8) };
        var close = MakeButton("×", () => Close());
        close.Width = 34;
        DockPanel.SetDock(close,Dock.Right);
        header.Children.Add(close);
        var title = new TextBlock { Text = "Студия темы", FontSize = 24, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        title.MouseLeftButtonDown += (_, _) => DragMove();
        header.Children.Add(title);
        root.Children.Add(header);
        root.Children.Add(new TextBlock { Text = "Результат сразу виден в микшере", FontSize = 13, Foreground = Brush("#A5AEC1"), Margin = new Thickness(0,0,0,20) });
        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3 };
        var names = new[] { "Фон", "Карточки", "Разделители", "Текст", "Подписи", "Акцент" };
        for (var i=0;i<names.Length;i++)
        {
            var key=ThemePalette.Keys[i];
            var stack = new StackPanel();
            stack.Children.Add(new Border { Height=23, CornerRadius=new CornerRadius(6), Background=Brush(colors[key]), Margin=new Thickness(0,0,0,8) });
            stack.Children.Add(new TextBlock { Text=names[i], FontSize=12, HorizontalAlignment=HorizontalAlignment.Center });
            var button=MakeButton("",()=>SelectColor(key));
            button.Content=stack;
            button.Margin=new Thickness(0,0,8,8);
            button.Padding=new Thickness(10);
            swatches.Add(key,button);
            grid.Children.Add(button);
        }
        root.Children.Add(grid);
        selectedLabel.FontSize=16;
        selectedLabel.FontWeight=FontWeights.SemiBold;
        selectedLabel.Margin=new Thickness(0,16,0,12);
        root.Children.Add(selectedLabel);
        sample.Height=65;sample.CornerRadius=new CornerRadius(12);sample.Margin=new Thickness(0,0,0,14);
        root.Children.Add(sample);
        var sliderStyle = (Style)System.Windows.Markup.XamlReader.Parse("""
<Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Slider">
 <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Slider">
 <Grid Background="Transparent" Margin="0,2">
 <Border Height="7" CornerRadius="3.5" Background="{TemplateBinding Background}" VerticalAlignment="Center" Margin="8,0"/>
 <Track x:Name="PART_Track">
 <Track.DecreaseRepeatButton><RepeatButton Command="Slider.DecreaseLarge" Opacity="0"/></Track.DecreaseRepeatButton>
 <Track.Thumb><Thumb Width="18" Height="18"><Thumb.Template><ControlTemplate TargetType="Thumb"><Ellipse Fill="#F0F3FA" Stroke="#171B24" StrokeThickness="3"/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
 <Track.IncreaseRepeatButton><RepeatButton Command="Slider.IncreaseLarge" Opacity="0"/></Track.IncreaseRepeatButton>
 </Track>
 </Grid></ControlTemplate></Setter.Value></Setter>
</Style>
""");
        var channelNames=new[]{"Красный","Зелёный","Синий"};
        for(var i=0;i<3;i++)
        {
            root.Children.Add(new TextBlock { Text=channelNames[i], FontSize=12, Foreground=Brush("#A5AEC1"), Margin=new Thickness(0,8,0,3) });
            var slider=new Slider { Minimum=0,Maximum=255,TickFrequency=1,IsSnapToTickEnabled=true,Height=24, IsMoveToPointEnabled=true };
            slider.Style=sliderStyle;
            slider.Background=new LinearGradientBrush(Color.FromRgb(30,35,45),
                i==0?Color.FromRgb(255,100,110):i==1?Color.FromRgb(85,210,150):Color.FromRgb(90,150,255),0);
            slider.ValueChanged+=(_,_)=>UpdateFromSliders();
            channels[i]=slider;root.Children.Add(slider);
        }
        var hexRow=new DockPanel { Margin=new Thickness(0,16,0,0) };
        var use=MakeButton("Применить HEX",ApplyHex);
        DockPanel.SetDock(use,Dock.Right);hexRow.Children.Add(use);
        hex.FontSize=16;hex.FontFamily=new FontFamily("Consolas");hex.MaxLength=7;
        hex.Padding=new Thickness(10,7,10,7);hex.Margin=new Thickness(0,0,10,0);
        hex.Background=Brush("#242C3A");hex.Foreground=Brush("#F0F3FA");hex.CaretBrush=Brushes.White;
        hex.BorderThickness=new Thickness(0);
        hex.KeyDown+=(_,e)=>{if(e.Key==Key.Enter)ApplyHex();};
        hexRow.Children.Add(hex);root.Children.Add(hexRow);
        var presets=new System.Windows.Controls.Primitives.UniformGrid {Columns=8,Margin=new Thickness(0,16,0,0)};
        foreach(var color in new[]{"#3297FF","#9D78EF","#F080A3","#EDAA56","#59B597","#F4F1EC","#292929","#151922"})
        {
            var button=MakeButton("",()=>SetColor(color));
            button.Background=Brush(color);button.Height=28;button.Margin=new Thickness(0,0,6,0);
            presets.Children.Add(button);
        }
        root.Children.Add(presets);
        feedback.FontSize=12;feedback.Foreground=Brush("#B7C2D8");feedback.Margin=new Thickness(0,12,0,12);
        root.Children.Add(feedback);
        var footer=new System.Windows.Controls.Primitives.UniformGrid{Columns=2};
        var cancel=MakeButton("Отмена",()=>Close());cancel.Margin=new Thickness(0,0,10,0);
        footer.Children.Add(cancel);
        var confirm=MakeButton("Сохранить тему",()=>{save(new(colors));Saved=true;Close();});
        confirm.Background=Brush("#315B92");footer.Children.Add(confirm);root.Children.Add(footer);
        SelectColor("Accent");
    }

    private static SolidColorBrush Brush(string value) => (SolidColorBrush)new BrushConverter().ConvertFromString(value)!;

    private static Button MakeButton(string text, Action action)
    {
        var button=new Button {Content=text,Background=Brush("#242C3A"),Foreground=Brush("#F0F3FA"),BorderBrush=Brush("#394254"),
            Padding=new Thickness(12,9,12,9),Cursor=Cursors.Hand,FontSize=13,FocusVisualStyle=null};
        var border=new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty,new CornerRadius(9));
        border.SetValue(Border.BorderThicknessProperty,new Thickness(1));
        border.SetBinding(Border.BackgroundProperty,new System.Windows.Data.Binding("Background"){RelativeSource=System.Windows.Data.RelativeSource.TemplatedParent});
        border.SetBinding(Border.BorderBrushProperty,new System.Windows.Data.Binding("BorderBrush"){RelativeSource=System.Windows.Data.RelativeSource.TemplatedParent});
        border.SetBinding(Border.PaddingProperty,new System.Windows.Data.Binding("Padding"){RelativeSource=System.Windows.Data.RelativeSource.TemplatedParent});
        var presenter=new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);
        border.AppendChild(presenter);
        button.Template=new ControlTemplate(typeof(Button)){VisualTree=border};
        button.MouseEnter+=(_,_)=>button.Opacity=0.8;button.MouseLeave+=(_,_)=>button.Opacity=1;
        button.Click+=(_,_)=>action();
        return button;
    }

    private void SelectColor(string key)
    {
        selected=key;
        foreach(var pair in swatches)pair.Value.BorderBrush=Brush(pair.Key==key?"#90BFFF":"#394254");
        selectedLabel.Text=((TextBlock)((StackPanel)swatches[key].Content).Children[1]).Text;
        SetColor(colors[key],false);
    }

    private void SetColor(string value,bool apply=true)
    {
        updating=true;
        colors[selected]=value;
        var color=(Color)ColorConverter.ConvertFromString(value);
        channels[0].Value=color.R;channels[1].Value=color.G;channels[2].Value=color.B;
        sample.Background=Brush(value);hex.Text=value;
        ((Border)((StackPanel)swatches[selected].Content).Children[0]).Background=Brush(value);
        updating=false;
        feedback.Text="Изменения сохранятся по кнопке ниже";
        if(apply)preview(new(colors));
    }

    private void UpdateFromSliders()
    {
        if(updating)return;
        SetColor($"#{(int)channels[0].Value:X2}{(int)channels[1].Value:X2}{(int)channels[2].Value:X2}");
    }

    private void ApplyHex()
    {
        if(!ThemePalette.Valid(hex.Text)){feedback.Text="Введите шесть символов после #, например #3297FF";return;}
        SetColor(hex.Text.ToUpperInvariant());
    }
}

