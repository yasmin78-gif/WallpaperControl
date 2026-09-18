namespace WallpaperControl;

// A hidden editor contains only a placeholder in its native window. The actual
// multi-line value is assigned to the native editor only after explicit reveal.
internal sealed class PrivateCalendarTextBox : TextBox
{
    private string privateText = string.Empty;
    internal bool SourcesHidden { get; private set; } = true;

    public PrivateCalendarTextBox()
    {
        AutoSize = false;
        HideSources();
    }

    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string Text
    {
        get => SourcesHidden ? privateText : base.Text;
        set
        {
            privateText = value ?? string.Empty;
            base.Text = SourcesHidden ? "********" : privateText;
        }
    }

    internal void RevealSources()
    {
        if (!SourcesHidden) return;
        // Clear before changing native control styles to avoid transient exposure.
        base.Text = string.Empty;
        UseSystemPasswordChar = false;
        Multiline = true;
        AcceptsReturn = true;
        ScrollBars = ScrollBars.Vertical;
        ReadOnly = false;
        SourcesHidden = false;
        base.Text = privateText;
    }

    internal void HideSources()
    {
        if (!SourcesHidden) privateText = base.Text;
        base.Text = string.Empty;
        SourcesHidden = true;
        ReadOnly = true;
        Multiline = false;
        UseSystemPasswordChar = true;
        base.Text = "********";
        ClearUndo();
    }

    protected override AccessibleObject CreateAccessibilityInstance() => new PrivateSourceAccessibility(this);

    private sealed class PrivateSourceAccessibility(PrivateCalendarTextBox owner) : ControlAccessibleObject(owner)
    {
        public override AccessibleRole Role => AccessibleRole.Text;
        public override string? Value
        {
            get => owner.SourcesHidden ? "********" : owner.Text;
            set { if (!owner.SourcesHidden) owner.Text = value; }
        }
    }
}
