namespace WallpaperControl
{
    /// <summary>
    /// Selects the movement direction; Random is resolved to a concrete direction when an animation starts.
    /// </summary>
    internal enum WallpaperTransitionDirection
    {
        Left,
        Right,
        Up,
        Down,
        Random
    }
}
