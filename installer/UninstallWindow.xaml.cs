using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
namespace VeyroSetup;
public partial class UninstallWindow : Window
{
    private bool busy;
    private bool removed;
    public UninstallWindow(){InitializeComponent();Closing+=(_,e)=>e.Cancel=busy;}
    private void DragHeader(object sender,MouseButtonEventArgs e)=>DragMove();
    private void Cancel(object sender,RoutedEventArgs e)=>Close();
    private async void Delete(object sender,RoutedEventArgs e)
    {
        if(removed){Close();return;}
        busy=true;CancelButton.IsEnabled=false;DeleteButton.IsEnabled=false;
        Heading.Text="Удаляем Veyro";
        Progress.Visibility=Visibility.Visible;
        try
        {
            await Task.Run(()=>Uninstaller.Remove(new Progress<string>(text=>Dispatcher.Invoke(()=>Status.Text=text))));
            removed=true;
            Heading.Text="Veyro удалён";
            Description.Text="Ваши темы и настройки сохранены. Спасибо, что пользовались Veyro.";
            Status.Text="";CancelButton.Visibility=Visibility.Collapsed;DeleteButton.Content="Закрыть";
        }
        catch(Exception error){Heading.Text="Удаление не завершено";Status.Text=error.Message;}
        finally{busy=false;Progress.Visibility=Visibility.Collapsed;DeleteButton.IsEnabled=true;CancelButton.IsEnabled=true;}
    }
}
