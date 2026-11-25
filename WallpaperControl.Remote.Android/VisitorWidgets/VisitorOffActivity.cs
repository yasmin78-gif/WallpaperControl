using Android.App;
using Android.Content;
using Android.OS;
using Android.Widget;

namespace WallpaperControl.Remote.AndroidApp;

[Activity(Name = "de.wallpapercontrol.remote.VisitorOffActivity", Exported = false, Theme = "@android:style/Theme.Material.Dialog.Alert", ExcludeFromRecents = true, TaskAffinity = "de.wallpapercontrol.remote.widgetconfirmation")]
public sealed class VisitorOffActivity : Activity
{
    private static int showing;
    private bool ownsDialog;
    private CancellationTokenSource? lifetime;
    protected override void OnCreate(Bundle? state)
    {
        base.OnCreate(state);
        if (Interlocked.CompareExchange(ref showing, 1, 0) != 0) { Finish(); return; }
        ownsDialog = true;
        if (!WidgetRuntime.Controller.CanRequestDisable) { Finish(); return; }
        new AlertDialog.Builder(this)!.SetTitle("Besuchermodus deaktivieren?")!
            .SetMessage("Deine normalen Hintergrundbilder werden wieder sichtbar.")!
            .SetNegativeButton("Abbrechen", (_, _) => Finish())!
            .SetPositiveButton("Deaktivieren", async (_, _) =>
            {
                lifetime = new(TimeSpan.FromSeconds(7));
                bool success = await WidgetRuntime.Controller.DeactivateAsync(true, lifetime.Token);
                if (!IsFinishing) { if (!success) Toast.MakeText(this, "Deaktivierung nicht bestätigt. Zustand oder Freigabe prüfen.", ToastLength.Long)?.Show(); Finish(); }
            })!
            .SetOnCancelListener(new CancelListener(Finish))!.Show();
    }
    protected override void OnDestroy()
    { lifetime?.Cancel(); lifetime?.Dispose(); if (ownsDialog) Interlocked.Exchange(ref showing, 0); base.OnDestroy(); }
    private sealed class CancelListener(Action cancel) : Java.Lang.Object, IDialogInterfaceOnCancelListener
    { public void OnCancel(IDialogInterface? dialog) => cancel(); }
}
