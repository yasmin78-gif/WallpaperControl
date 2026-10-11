using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Drawing.Imaging;
using System.Security.Cryptography;

namespace WallpaperControl;

internal sealed record ClipboardScreenshot(Guid Id,DateTimeOffset Copied,int Width,int Height,string Hash,bool Persistent);
internal sealed class ClipboardScreenshotCapture : IDisposable
{
    internal const int MaximumPngBytes=32*1024*1024,MaximumRawBytes=128*1024*1024;
    internal static bool ValidDimensions(int width,int height)=>width>0&&height>0&&width<=16384&&height<=16384&&(long)width*height<=24_000_000;
    private readonly byte[]? bytes;
    private readonly IReadOnlyList<ClipboardScreenshotCapture>? candidates;
    internal string Format { get; }
    private readonly bool dib;
    private IntPtr bitmap;
    internal DateTimeOffset Copied { get; }
    internal ClipboardScreenshotCapture(byte[] bytes,bool dib=false,DateTimeOffset? copied=null,string? format=null) { this.bytes=bytes; this.dib=dib; Format=format??(dib?"DIB":"PNG"); Copied=copied??DateTimeOffset.UtcNow; }
    internal ClipboardScreenshotCapture(IntPtr ownedBitmap) { Format="Bitmap"; bitmap=ownedBitmap; Copied=DateTimeOffset.UtcNow; }
    internal ClipboardScreenshotCapture(IReadOnlyList<ClipboardScreenshotCapture> candidates) { this.candidates=candidates; Format="multiple formats"; Copied=candidates[0].Copied; }
    internal (Bitmap Image,byte[] Png,string Hash) Decode()
    {
        if(candidates!=null) {
            foreach(var candidate in candidates) {
                try { var decoded=candidate.Decode(); AppLogger.Info($"Clipboard image decoded: format={candidate.Format}; width={decoded.Image.Width}; height={decoded.Image.Height}"); return decoded; }
                catch(Exception ex) { ClipboardDiagnostics.Failure("decode "+candidate.Format,ex); }
            }
            throw new InvalidDataException("None of the available clipboard image formats could be decoded.");
        }
        Bitmap image;
        if(bitmap!=IntPtr.Zero) { using var source=Bitmap.FromHbitmap(bitmap); if(!ValidDimensions(source.Width,source.Height))throw new InvalidDataException(); image=Detach(source); }
        else
        {
            if(bytes==null||bytes.Length>MaximumRawBytes)throw new InvalidDataException();
            byte[] data=dib?DibFile(bytes):bytes;
            if(!dib)ValidatePng(data);
            using var input=new MemoryStream(data); using var source=Image.FromStream(input,false,true);
            if(!ValidDimensions(source.Width,source.Height))throw new InvalidDataException(); image=Detach(source);
        }
        try
        {
            byte[] png;
            if(!dib&&bitmap==IntPtr.Zero)png=bytes!;
            else { using var output=new MemoryStream(); image.Save(output,ImageFormat.Png); png=output.ToArray(); ValidatePng(png); }
            return (image,png,PixelHash(image));
        }
        catch { image.Dispose(); throw; }
    }
    // Bitmap.Clone can retain a DIBV5 decoder's backing-stream dependency. Own the
    // pixels before the source stream is closed, so later PNG encoding stays valid.
    private static Bitmap Detach(Image source)
    {
        var bitmap=new Bitmap(source.Width,source.Height,PixelFormat.Format32bppArgb);
        try {
            if(source is not Bitmap original)throw new InvalidDataException("Clipboard decoder did not return bitmap pixels.");
            var bounds=new Rectangle(0,0,source.Width,source.Height);
            var input=original.LockBits(bounds,ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
            try {
                var output=bitmap.LockBits(bounds,ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);
                try {
                    var row=new byte[source.Width*4];
                    for(int y=0;y<source.Height;y++) { Marshal.Copy(input.Scan0+y*input.Stride,row,0,row.Length); Marshal.Copy(row,0,output.Scan0+y*output.Stride,row.Length); }
                } finally { bitmap.UnlockBits(output); }
            } finally { original.UnlockBits(input); }
            return bitmap;
        } catch { bitmap.Dispose(); throw; }
    }
    internal static (int Width,int Height) ValidatePng(byte[] data)
    {
        if(data.Length<33||data.Length>MaximumPngBytes||!data.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10})||!data.AsSpan(12,4).SequenceEqual("IHDR"u8))throw new InvalidDataException("PNG header is invalid or data exceeds 32 MiB.");
        int width=BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(16,4)),height=BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(20,4));
        if(!ValidDimensions(width,height))throw new InvalidDataException("PNG dimensions exceed the screenshot limits."); return(width,height);
    }
    private static byte[] DibFile(byte[] data)
    {
        if(data.Length<40)throw new InvalidDataException();
        int header=BinaryPrimitives.ReadInt32LittleEndian(data),width=BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4)),rawHeight=BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(8));
        if(header is not (40 or 52 or 56 or 108 or 124)||header>data.Length||rawHeight==int.MinValue||!ValidDimensions(width,Math.Abs(rawHeight)))throw new InvalidDataException();
        int bits=BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(14)),compression=BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(16));
        if(BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(12))!=1||bits is not (1 or 4 or 8 or 16 or 24 or 32)||compression is not (0 or 3 or 6))throw new InvalidDataException();
        uint colors=BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(32)); if(colors>256)throw new InvalidDataException();
        long palette=colors>0?colors:bits<=8?1L<<bits:0;
        long offset=header+palette*4+(header==40&&compression!=0?(compression==6?16:12):0);
        long required=offset+(((long)width*bits+31)/32*4)*Math.Abs(rawHeight);
        if(required>data.Length||offset>int.MaxValue)throw new InvalidDataException();
        var file=new byte[data.Length+14]; file[0]=(byte)'B'; file[1]=(byte)'M'; BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(2),file.Length); BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(10),(int)offset+14); data.CopyTo(file,14); return file;
    }
    private static string PixelHash(Bitmap image)
    {
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(BitConverter.GetBytes(image.Width)); hash.AppendData(BitConverter.GetBytes(image.Height));
        var bits=image.LockBits(new(0,0,image.Width,image.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
        try { var row=new byte[image.Width*4]; for(int y=0;y<image.Height;y++) { Marshal.Copy(bits.Scan0+y*bits.Stride,row,0,row.Length); hash.AppendData(row); } }
        finally { image.UnlockBits(bits); }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
    public void Dispose() { if(candidates!=null)foreach(var candidate in candidates)candidate.Dispose(); if(bitmap!=IntPtr.Zero) { DeleteObject(bitmap); bitmap=IntPtr.Zero; } }
    [DllImport("gdi32.dll")] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(IntPtr value);
}
