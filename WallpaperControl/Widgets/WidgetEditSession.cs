using System.Text.Json;

namespace WallpaperControl
{
    internal enum WidgetEditDecision { Save, Discard, Cancel }

    /// <summary>One rollback snapshot for all widget pages, including positions changed on the desktop.</summary>
    internal sealed class WidgetEditSession
    {
        private readonly WidgetManager manager;
        private readonly Func<bool, WidgetSettings> read;
        private readonly Action<WidgetSettings> load;
        private WidgetSettings? baseline;
        internal bool Active => baseline != null;
        internal bool IsDirty => baseline != null &&
            (!Equal(baseline, read(false)) || !Equal(baseline, manager.Settings));

        internal WidgetEditSession(WidgetManager manager, Func<bool, WidgetSettings> read, Action<WidgetSettings> load)
        {
            this.manager = manager; this.read = read; this.load = load;
        }
        internal void Begin()
        {
            if (Active) return;
            baseline = manager.Settings;
            load(baseline);
            manager.SetEditing(true);
        }
        internal void Preview(WidgetSettings value)
        {
            if (Active) manager.Preview(value);
        }
        internal bool Save()
        {
            if (!Active) return true;
            long revision = SettingsPersistence.FailureRevision;
            WidgetSettings value = read(true);
            if (value.Web.Enabled && !WebWidgetSettings.IsValidUrl(value.Web.Url)) return false;
            manager.CommitPreview(value);
            manager.SetEditing(true);
            if (revision != SettingsPersistence.FailureRevision) return false;
            baseline = manager.Settings;
            load(baseline);
            return true;
        }
        internal void Discard()
        {
            if (baseline == null) return;
            manager.CancelPreview(baseline);
            load(baseline);
            manager.SetEditing(true);
        }
        internal bool TryLeave(Func<WidgetEditDecision> choose)
        {
            if (!Active) return true;
            if (IsDirty)
            {
                switch (choose())
                {
                    case WidgetEditDecision.Cancel: return false;
                    case WidgetEditDecision.Save: if (!Save()) return false; break;
                    case WidgetEditDecision.Discard: Discard(); break;
                }
            }
            manager.SetEditing(false);
            baseline = null;
            return true;
        }
        private static bool Equal(WidgetSettings left, WidgetSettings right) =>
            JsonSerializer.Serialize(left) == JsonSerializer.Serialize(right);
    }
}
