using Android.App;
using Android.Runtime;

namespace Runninghill.Maui;

#if DEBUG
// Needed only for the local emulator's HTTP development service. Release keeps the platform default.
[Application(UsesCleartextTraffic = true)]
#else
[Application]
#endif
public class MainApplication : MauiApplication
{
	public MainApplication(IntPtr handle, JniHandleOwnership ownership)
		: base(handle, ownership)
	{
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
