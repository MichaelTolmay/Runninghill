using ObjCRuntime;
using UIKit;

namespace Runninghill.Maui;

/// <summary>
/// Provides the native entry point for the Mac Catalyst application.
/// </summary>
public class Program
{
	// This is the main entry point of the application.
	/// <summary>
	/// Starts the UIKit application loop and selects AppDelegate to build the shared MAUI app.
	/// </summary>
	static void Main(string[] args)
	{
		// if you want to use a different Application Delegate class from "AppDelegate"
		// you can specify it here.
		UIApplication.Main(args, null, typeof(AppDelegate));
	}
}
