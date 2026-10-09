using Android.App;
using Android.Content.PM;
using Android.OS;

namespace Runninghill.Maui;

/// <summary>
/// Hosts the shared MAUI screen inside the Android activity and declares its launch and
/// configuration behaviour.
/// </summary>
[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
}
