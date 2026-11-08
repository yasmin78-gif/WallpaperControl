using System.Globalization;
namespace WallpaperControl;
internal static class WidgetHeader
{
    internal static void Draw(Graphics graphics,string title,string language,SystemWidgetStyle style,float width,float actionsWidth,Color text,Color accent,float fontSize=11)
    {
        using var font=new Font("Segoe UI Semibold",fontSize,FontStyle.Bold);
        title=title.ToUpper(CultureInfo.GetCultureInfo(language));
        float available=Math.Max(0,width-actionsWidth-32);
        if(graphics.MeasureString(title,font).Width>available)
        {
            var starts=StringInfo.ParseCombiningCharacters(title); int length=starts.Length;
            while(length>0 && graphics.MeasureString(title[..starts[--length]]+"…",font).Width>available) { }
            title=length>0?title[..starts[length]]+"…":"…";
        }
        var state=graphics.Save(); graphics.SetClip(new RectangleF(14,10,available+4,Math.Max(28,font.GetHeight(graphics)+4)));
        if(style==SystemWidgetStyle.Glow)WidgetDrawing.DrawGlowText(graphics,title,font,16,13,accent);
        else { using var brush=new SolidBrush(text); graphics.DrawString(title,font,brush,16,13); }
        graphics.Restore(state);
        if(style!=SystemWidgetStyle.Minimal) { using var brush=new SolidBrush(accent); graphics.FillRectangle(brush,16,Math.Max(40,13+font.GetHeight(graphics)+6),width-32,style==SystemWidgetStyle.Glow?2:1); }
    }
}
