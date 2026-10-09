namespace WallpaperControl;
internal sealed record SnapTarget(object Identity,Rectangle Bounds);
internal sealed record SnapResult(Point Location,Rectangle? VerticalGuide,Rectangle? HorizontalGuide);
internal sealed class WidgetSnapEngine
{
    private sealed record Match(object Identity,int Relation);
    private sealed record Candidate(Match Match,int Position,Rectangle Bounds);
    private Match? x,y;
    internal void Reset() { x=null; y=null; }
    internal SnapResult Move(Rectangle proposed,IReadOnlyList<SnapTarget> targets,int dpi=96,bool bypass=false)
    {
        if(bypass) { Reset(); return new(proposed.Location,null,null); }
        int radius=Math.Max(1,(int)Math.Round(8*dpi/96d)),release=Math.Max(radius+1,(int)Math.Round(12*dpi/96d)),gap=Math.Max(1,(int)Math.Round(10*dpi/96d));
        Candidate? Axis(bool horizontal,Match? previous)
        {
            int origin=horizontal?proposed.Left:proposed.Top,size=horizontal?proposed.Width:proposed.Height;
            var candidates=new List<Candidate>();
            foreach(var target in targets)
            {
                int start=horizontal?target.Bounds.Left:target.Bounds.Top,end=horizontal?target.Bounds.Right:target.Bounds.Bottom;
                int[] positions=[start,end-size,start+(end-start)/2-size/2,end+gap,start-size-gap,end,start-size];
                for(int i=0;i<positions.Length;i++)candidates.Add(new(new(target.Identity,i),positions[i],target.Bounds));
            }
            var sticky=candidates.FirstOrDefault(c=>c.Match==previous);
            if(sticky!=null&&Math.Abs((long)sticky.Position-origin)<=release)return sticky;
            return candidates.Where(c=>Math.Abs((long)c.Position-origin)<=radius).OrderBy(c=>Math.Abs((long)c.Position-origin)).ThenBy(c=>c.Match.Relation).FirstOrDefault();
        }
        var horizontal=Axis(true,x); var vertical=Axis(false,y); x=horizontal?.Match; y=vertical?.Match;
        var location=new Point(horizontal?.Position??proposed.Left,vertical?.Position??proposed.Top); var snapped=new Rectangle(location,proposed.Size);
        Rectangle Guide(Candidate candidate,bool horizontal)
        {
            var target=candidate.Bounds; int relation=candidate.Match.Relation;
            if(horizontal)
            {
                if(relation is 3 or 4)
                {
                    int first=relation==3?target.Right:snapped.Right,second=relation==3?snapped.Left:target.Left;
                    int gapY=(Math.Max(target.Top,snapped.Top)+Math.Min(target.Bottom,snapped.Bottom))/2;
                    return new(Math.Min(first,second),gapY,Math.Max(1,Math.Abs(second-first)),2);
                }
                int lineX=relation is 1 or 4 or 6?snapped.Right:relation==2?snapped.Left+snapped.Width/2:snapped.Left;
                return new(lineX,Math.Min(target.Top,snapped.Top),2,Math.Max(target.Bottom,snapped.Bottom)-Math.Min(target.Top,snapped.Top));
            }
            if(relation is 3 or 4)
            {
                int first=relation==3?target.Bottom:snapped.Bottom,second=relation==3?snapped.Top:target.Top;
                int lineX=(Math.Max(target.Left,snapped.Left)+Math.Min(target.Right,snapped.Right))/2;
                return new(lineX,Math.Min(first,second),2,Math.Max(1,Math.Abs(second-first)));
            }
            int lineY=relation is 1 or 4 or 6?snapped.Bottom:relation==2?snapped.Top+snapped.Height/2:snapped.Top;
            return new(Math.Min(target.Left,snapped.Left),lineY,Math.Max(target.Right,snapped.Right)-Math.Min(target.Left,snapped.Left),2);
        }
        return new(location,horizontal==null?null:Guide(horizontal,true),vertical==null?null:Guide(vertical,false));
    }
}
