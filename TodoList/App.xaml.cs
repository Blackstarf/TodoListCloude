using System.Windows;
using TodoList.Services;

namespace TodoList
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ThemeService.Initialize(); // Должно быть перед инициализацией главного окна
        }
    }
}