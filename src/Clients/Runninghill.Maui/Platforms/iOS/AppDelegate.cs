using Foundation;

namespace Runninghill.Maui;

/// <summary>
/// Connects the iPhone or iPad application lifecycle to the shared MAUI host.
/// </summary>
[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
	/// <summary>
	/// Builds the shared app when the Apple platform requests its application host.
	/// </summary>
	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
