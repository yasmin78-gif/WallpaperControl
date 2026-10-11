using System.Diagnostics;
using System.Net;
using System.Text;

namespace WallpaperControl;

// Only the Windows-supplied executable is used; never PATH, a shell or a curl config.
internal static class LauncherCurlIcons
{
    internal static ProcessStartInfo StartInfo(Uri url,int limit)
    {
        if(!LauncherTargets.IsWebsite(url.AbsoluteUri)||!string.IsNullOrEmpty(url.UserInfo))throw new ArgumentException("Public HTTP icon URL required.");
        var start=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"curl.exe"))
        { UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true };
        foreach(var argument in new[]{"-q","--silent","--http1.1","--include","--suppress-connect-headers","--proto","=http,https","--max-time","3","--max-filesize",limit.ToString(System.Globalization.CultureInfo.InvariantCulture),"--user-agent","WallpaperControl/WebsiteIcons","--header","Accept:","--url",url.AbsoluteUri})start.ArgumentList.Add(argument);
        return start;
    }
    internal static async Task<HttpResponseMessage?> DownloadAsync(Uri url,int limit,CancellationToken cancellation)
    {
        using var process=new Process { StartInfo=StartInfo(url,limit) };
        Task? errors=null;
        if(!System.IO.File.Exists(process.StartInfo.FileName)) { AppLogger.Info("Launcher favicon curl: Windows executable unavailable"); return null; }
        try
        {
            cancellation.ThrowIfCancellationRequested(); process.Start();
            using var cancel=cancellation.Register(()=>Stop(process));
            errors=process.StandardError.BaseStream.CopyToAsync(Stream.Null,cancellation);
            using var output=new MemoryStream(); var buffer=new byte[8192]; int count;
            while((count=await process.StandardOutput.BaseStream.ReadAsync(buffer,cancellation))>0)
            { if(output.Length+count>limit+64*1024)throw new InvalidDataException("Icon response too large."); await output.WriteAsync(buffer.AsMemory(0,count),cancellation); }
            await process.WaitForExitAsync(cancellation); await errors;
            AppLogger.Info($"Launcher favicon curl: host={url.Host}; path={url.AbsolutePath}; exit={process.ExitCode}; bytes={output.Length}");
            return process.ExitCode==0?ParseResponse(output.ToArray(),limit):null;
        }
        finally
        {
            Stop(process);
            if(errors!=null)try { await errors; } catch(Exception ex) when(ex is OperationCanceledException or IOException or ObjectDisposedException) { }
        }
    }
    private static void Stop(Process process)
    { try { if(!process.HasExited)process.Kill(entireProcessTree:true); } catch(Exception ex) when(ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { } }
    internal static HttpResponseMessage? ParseResponse(byte[] data,int limit)
    {
        int offset=0;
        while(offset<Math.Min(data.Length,64*1024))
        {
            int end=data.AsSpan(offset).IndexOf("\r\n\r\n"u8); if(end<0||offset+end>64*1024)return null;
            var lines=Encoding.ASCII.GetString(data,offset,end).Split("\r\n"); var status=lines[0].Split(' ');
            if(status.Length<2||!status[0].StartsWith("HTTP/",StringComparison.Ordinal)||!int.TryParse(status[1],out int code)||code<100||code>599)return null;
            offset+=end+4; if(code<200)continue;
            if(data.Length-offset>limit)return null;
            var response=new HttpResponseMessage((HttpStatusCode)code) { Content=new ByteArrayContent(data[offset..]) };
            foreach(var line in lines.Skip(1))
            {
                int colon=line.IndexOf(':'); if(colon<=0)continue;
                var name=line[..colon]; var value=line[(colon+1)..].Trim();
                if(name.Equals("Location",StringComparison.OrdinalIgnoreCase))
                { if(!Uri.TryCreate(value,UriKind.RelativeOrAbsolute,out var location)) { response.Dispose(); return null; } response.Headers.Location=location; }
            }
            AppLogger.Info($"Launcher favicon curl response: status={code}"); return response;
        }
        return null;
    }
}
