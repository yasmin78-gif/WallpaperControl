extern alias WallpaperApp;
using CandidatePackageValidation = WallpaperApp::WallpaperControl.Video.CandidatePackageValidation;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
internal static class CandidatePackageChecks
{
    internal static void Run(string source)
    {
        int count=0;
        void Check(bool ok,string message) { if(!ok)throw new Exception(message); Console.WriteLine("PASS "+message);count++; }
        string root=Path.Combine(Path.GetTempPath(),"wc-package-check-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string originalHash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(source,"runtime-package.json"))));
        void Reset() { foreach(string p in Directory.GetFiles(source))File.Copy(p,Path.Combine(root,Path.GetFileName(p)),true); }
        bool Reject(string hash) { try { CandidatePackageValidation.Validate(root,hash); return false; } catch(Exception ex) when(ex is IOException or InvalidDataException or BadImageFormatException) { return true; } }
        string Rewrite(Action<JsonObject> mutate) { string path=Path.Combine(root,"runtime-package.json");var node=JsonNode.Parse(File.ReadAllText(path))!.AsObject();mutate(node);File.WriteAllText(path,node.ToJsonString());return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))); }
        try
        {
            Reset();CandidatePackageValidation.Validate(root,originalHash);Check(true,"Complete exact package accepted");
            foreach(string name in new[]{"libmpv-2.dll","libspirv-cross-c-shared.dll","libc++.dll","libunwind.dll"})
            {
                Reset();File.Delete(Path.Combine(root,name));Check(Reject(originalHash),"Missing DLL rejected: "+name);
                Reset();using(var file=File.OpenWrite(Path.Combine(root,name))){file.Position=file.Length-1;file.WriteByte(42);}Check(Reject(originalHash),"Modified DLL rejected: "+name);
            }
            Reset();File.AppendAllText(Path.Combine(root,"runtime-package.json")," ");Check(Reject(originalHash),"Wrong exact manifest rejected");
            foreach(var pair in new[]{("clientApi","2.6"),("architecture","arm64"),("buildIdentity","mpv v0.41.0-UNKNOWN"),("profile","auto"),("packageVersion","2")})
            { Reset();string changedHash=Rewrite(n=>n[pair.Item1]=pair.Item2);Check(Reject(changedHash),"Explicit descriptor field rejected: "+pair.Item1); }
            // Trusted-hash injection here exercises PE defense in depth, not a production acceptance path.
            Reset();string dll=Path.Combine(root,"libmpv-2.dll");byte[] data=File.ReadAllBytes(dll);int pe=BitConverter.ToInt32(data,0x3c);data[pe+4]=0x4c;data[pe+5]=0x01;File.WriteAllBytes(dll,data);
            string altered=Rewrite(n=> { foreach(var file in n["files"]!.AsArray())if(file!["name"]!.GetValue<string>()=="libmpv-2.dll")file["sha256"]=Convert.ToHexString(SHA256.HashData(data)); });
            Check(Reject(altered),"Wrong DLL PE architecture rejected even with test-trusted changed hash");
            CandidatePackageValidation.ValidateNative(0x20005, CandidatePackageValidation.Identity);
            Check(true, "Exact native API/build pair accepted");
            foreach(var pair in new[]{(0x20006u,CandidatePackageValidation.Identity),(0x20005u,"mpv v0.41.0-UNKNOWN")})
            {
                bool rejected=false;
                try { CandidatePackageValidation.ValidateNative(pair.Item1,pair.Item2); } catch(NotSupportedException) { rejected=true; }
                Check(rejected,"Wrong native API/custom build rejected");
            }
            Console.WriteLine("PACKAGE checks="+count);
        }
        finally { Directory.Delete(root,true); }
    }
}
