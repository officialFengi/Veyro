using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace Veyro;

internal sealed class TrayMenuWindow : Window
{
    private bool closing;
    private bool actionQueued;
    internal TrayMenuWindow(Preferences preferences, Action open, Action checkUpdates, Action exit)
    {
        var palette=ThemePalette.Resolve(preferences.Theme, preferences.CustomColors);
        SolidColorBrush ColorAt(int index)=>(SolidColorBrush)new BrushConverter().ConvertFromString(palette[index])!;
        Width=228;
        SizeToContent=SizeToContent.Height;
        WindowStyle=WindowStyle.None;
        ResizeMode=ResizeMode.NoResize;
        AllowsTransparency=true;
        Background=Brushes.Transparent;
        ShowInTaskbar=false;
        Topmost=true;
        FontFamily=new FontFamily("Segoe UI");
        var stack=new StackPanel();
        Content=new Border
        {
            Background=ColorAt(0),BorderBrush=ColorAt(2),BorderThickness=new Thickness(1),
            CornerRadius=new CornerRadius(16),Padding=new Thickness(7),Child=stack
        };
        Button Row(string title,string glyph,Action action)
        {
            var button=new Button { Height=44,Background=Brushes.Transparent,BorderThickness=new Thickness(0),Cursor=Cursors.Hand,FocusVisualStyle=null };
            System.Windows.Automation.AutomationProperties.SetName(button, title);
            var border=new FrameworkElementFactory(typeof(Border));
            border.Name="Surface";
            border.SetValue(Border.CornerRadiusProperty,new CornerRadius(10));
            border.SetValue(Border.BackgroundProperty,Brushes.Transparent);
            var content=new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);
            border.AppendChild(content);
            var template=new ControlTemplate(typeof(Button)){VisualTree=border};
            var hover=new Trigger {Property=IsMouseOverProperty,Value=true};
            hover.Setters.Add(new Setter(Border.BackgroundProperty,ColorAt(1),"Surface"));
            template.Triggers.Add(hover);
            var keyboard=new Trigger {Property=IsKeyboardFocusedProperty,Value=true};
            keyboard.Setters.Add(new Setter(Border.BackgroundProperty,ColorAt(1),"Surface"));
            template.Triggers.Add(keyboard);
            button.Template=template;
            var grid=new Grid { Margin=new Thickness(14,0,14,0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(30) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.Children.Add(new TextBlock { Text=glyph,FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=17,
                Foreground=ColorAt(4),VerticalAlignment=VerticalAlignment.Center });
            var text=new TextBlock { Text=title,FontSize=14,Foreground=ColorAt(3),VerticalAlignment=VerticalAlignment.Center };
            Grid.SetColumn(text,1);grid.Children.Add(text);
            button.Content=grid;
            button.Click+=(_,_) =>
            {
                if (actionQueued) return;
                actionQueued = true;
                Close();
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, action);
            };
            return button;
        }
        stack.Children.Add(Row("Открыть Veyro","\uE8A7",open));
        stack.Children.Add(Row("Проверить обновления","\uE895",checkUpdates));
        stack.Children.Add(new Border { Height=1,Background=ColorAt(2),Margin=new Thickness(14,5,14,5) });
        stack.Children.Add(Row("Выйти","\uE7E8",exit));
        Closing += (_, _) => closing = true;
        Deactivated += (_, _) =>
        {
            if (!closing) Close();
        };
        PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape)Close();};
        Loaded+=(_,_)=>
        {
            var cursor=Forms.Cursor.Position;
            var work=Forms.Screen.FromPoint(cursor).WorkingArea;
            var transform=PresentationSource.FromVisual(this)!.CompositionTarget!.TransformFromDevice;
            var point=transform.Transform(new Point(cursor.X,cursor.Y));
            var min=transform.Transform(new Point(work.Left,work.Top));
            var max=transform.Transform(new Point(work.Right,work.Bottom));
            Left=Math.Clamp(point.X-ActualWidth+12,min.X+6,Math.Max(min.X+6,max.X-ActualWidth-6));
            Top=Math.Clamp(point.Y-ActualHeight-8,min.Y+6,Math.Max(min.Y+6,max.Y-ActualHeight-6));
            Activate();
        };
    }

}


