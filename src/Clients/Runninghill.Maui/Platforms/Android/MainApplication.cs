using Android.App;
using Android.Runtime;

namespace Runninghill.Maui;

/// <summary>
/// Connects Android application startup to the shared MAUI host.
/// </summary>
#if DEBUG
// Needed only for the local emulator's HTTP development service. Release keeps the platform default.
[Application(UsesCleartextTraffic = true)]
#else
[Application]
#endif
public class MainApplication : MauiApplication
{
	/// <summary>
	/// Wraps the Android application handle and passes its ownership rules to the MAUI base class.
	/// </summary>
	public MainApplication(IntPtr handle, JniHandleOwnership ownership)
		: base(handle, ownership)
	{
	}

	/// <summary>
	/// Builds the shared MAUI host when Android starts the application.
	/// </summary>
	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
