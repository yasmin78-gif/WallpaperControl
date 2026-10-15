extern alias WallpaperApp;
using App = WallpaperApp::WallpaperControl;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using MailKit.Net.Imap;

/// <summary>Local synthetic TLS inbox. No certificate installation, real account or external network.</summary>
internal static class EmailImapProtocolTests
{
    internal static async Task Run(Action<bool,string> check)
    {
        foreach (bool startTls in new[] { false,true }) {
            await using var server = new InboxServer(startTls);
            var options = new App.EmailAccountSettings { Server = "localhost", Port = server.Port, Security = startTls ? App.EmailTransportSecurity.StartTls : App.EmailTransportSecurity.Tls,
                Username = "synthetic@example.invalid", ProtectedPassword = App.WindowsSecretProtector.Protect("synthetic-secret"), WebmailUrl = "https://webmail.example.invalid" };
            // Trust only this ephemeral localhost certificate using a scoped custom root chain.
            // Production clients retain their normal system certificate verification.
            using var client = new ImapClient { Timeout = 5000 };
            client.ServerCertificateValidationCallback = (_, certificate, _, errors) => Validate(server,certificate,errors);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await using (var mailbox = new App.EmailMailbox(client)) {
                await mailbox.ConnectAsync(options,timeout.Token);
                check(mailbox.Validity == 55 && mailbox.NextUid == 3, "Real TLS adapter opens synthetic INBOX and reads UID epoch: " + startTls);
                check(await mailbox.HighestAsync(timeout.Token)==2,"Initial baseline obtains highest actual UID: "+startTls);
                var headers = await mailbox.FetchAsync(1,2,timeout.Token);
                check(headers.Count == 2 && headers[0].Addresses.Single() == "sender@example.invalid", "Adapter parses MIME sender headers: " + startTls);
                string preview = await mailbox.PreviewAsync(1,timeout.Token);
                check(preview == "Synthetic plain preview", "Adapter decodes plain text section over TLS: " + startTls);
                check(await mailbox.PreviewAsync(2,timeout.Token) == "", "HTML-only message causes no body download: " + startTls);
                await mailbox.WaitAsync(60,timeout.Token);
                check(server.Commands.Any(command => command == "IDLE") && server.Commands.Any(command => command == "DONE"), "IDLE ends with DONE before later commands: " + startTls);
                check((await mailbox.NewUidsAsync(2,timeout.Token)).SequenceEqual(new uint[]{3}),"New UID found even when UIDNEXT remains unchanged after EXISTS: "+startTls);
                check((await mailbox.NewUidsAsync(5,timeout.Token)).Count==0,"Reversed UID range never reimports older message: "+startTls);
            }
            await server.Completion;
            check(server.Commands.Any(command => command.StartsWith("EXAMINE",StringComparison.Ordinal)) && !server.Commands.Any(command=>command.StartsWith("SELECT",StringComparison.Ordinal)), "INBOX opened read-only using EXAMINE: " + startTls);
            check(server.Commands.Any(command => command.Contains("BODY.PEEK[",StringComparison.Ordinal)), "Preview uses actual BODY.PEEK command: " + startTls);
            check(!server.Commands.Any(command => command.Contains("STORE",StringComparison.Ordinal) || command.Contains("EXPUNGE",StringComparison.Ordinal) || command.Contains("APPEND",StringComparison.Ordinal)), "No IMAP flags or messages mutated: " + startTls);
            check(!server.Commands.Any(command => command.Contains("BODY[]",StringComparison.Ordinal)), "No full-message or attachment fetch: " + startTls);
            check(!server.Commands.Any(command => command.Contains("synthetic-secret",StringComparison.Ordinal)), "Protocol diagnostic fixture never records authentication payload: " + startTls);
        }
        await using(var server=new InboxServer(false,false)) {
            using var client=new ImapClient { Timeout=5000 }; client.ServerCertificateValidationCallback=(_,cert,_,errors)=>Validate(server,cert,errors);
            await using var mailbox=new App.EmailMailbox(client);
            var options=new App.EmailAccountSettings { Server="localhost",Port=server.Port,Username="synthetic@example.invalid",ProtectedPassword=App.WindowsSecretProtector.Protect("synthetic-secret"),WebmailUrl="https://webmail.example.invalid" };
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await mailbox.ConnectAsync(options,timeout.Token); await mailbox.WaitAsync(0,timeout.Token);
            check(server.Commands.Any(command=>command=="NOOP")&&!server.Commands.Any(command=>command=="IDLE"),"Server without IDLE uses polling and NOOP fallback");
        }
        await using (var server = new InboxServer(false)) {
            var options = new App.EmailAccountSettings { Server="localhost",Port=server.Port,Username="synthetic@example.invalid",ProtectedPassword=App.WindowsSecretProtector.Protect("synthetic-secret"),WebmailUrl="https://webmail.example.invalid" };
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            string status = await App.EmailMailbox.TestAsync(options,timeout.Token);
            check(status=="EmailCertificateFailed", "Default production client rejects untrusted localhost certificate");
        }
    }
    private static bool Validate(InboxServer server,X509Certificate? certificate,SslPolicyErrors errors)
    {
                if (certificate == null || errors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch)) return false;
                using var cert = new X509Certificate2(certificate); using var chain = new X509Chain();
                chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust; chain.ChainPolicy.CustomTrustStore.Add(server.Certificate);
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                return cert.Thumbprint == server.Certificate.Thumbprint && chain.Build(cert);
    }
    private sealed class InboxServer : IAsyncDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback,0);
        private readonly CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(20));
        private readonly bool startTls;
        private readonly bool idle;
        internal X509Certificate2 Certificate { get; }
        internal ConcurrentQueue<string> Commands { get; } = new();
        internal int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
        internal Task Completion { get; }
        internal InboxServer(bool startTls,bool idle=true)
        {
            this.startTls=startTls; this.idle=idle; using var key=RSA.Create(2048);
            var request=new CertificateRequest("CN=localhost",key,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
            var names=new SubjectAlternativeNameBuilder(); names.AddDnsName("localhost"); names.AddIpAddress(IPAddress.Loopback); request.CertificateExtensions.Add(names.Build());
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true,false,0,true));
            using var generated=request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1),DateTimeOffset.UtcNow.AddHours(1));
            // SChannel requires an imported private key. No trust-store installation; disposal releases the temporary key.
            Certificate=X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pkcs12),null);
            listener.Start(); Completion=Task.Run(Serve);
        }
        private async Task<SslStream> Secure(Stream stream)
        {
            var ssl=new SslStream(stream,false); await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate=Certificate,EnabledSslProtocols=System.Security.Authentication.SslProtocols.Tls12 },cancellation.Token); return ssl;
        }
        private async Task Serve()
        {
            try {
                using var socket=await listener.AcceptTcpClientAsync(cancellation.Token);
                Stream transport=socket.GetStream();
                if(!startTls)transport=await Secure(transport);
                using var reader=new StreamReader(transport,Encoding.ASCII,false,1024,true);
                StreamReader current=reader;
                StreamWriter writer=new(transport,Encoding.ASCII,1024,true) { NewLine="\r\n",AutoFlush=true };
                await writer.WriteLineAsync("* OK [CAPABILITY IMAP4rev1 AUTH=PLAIN"+(idle?" IDLE":"")+(startTls?" STARTTLS":"")+"] Synthetic inbox");
                string? idleTag=null;
                try {
                    while(!cancellation.IsCancellationRequested) {
                        string? line=await current.ReadLineAsync(cancellation.Token); if(line==null)break;
                        if(line=="DONE") { Commands.Enqueue("DONE"); await writer.WriteLineAsync(idleTag+" OK IDLE ended"); continue; }
                        string[] words=line.Split(' ',3); if(words.Length<2)continue; string tag=words[0],verb=words[1].ToUpperInvariant();
                        Commands.Enqueue(verb is "AUTHENTICATE" or "LOGIN" ? verb : line[(tag.Length+1)..]);
                        if(verb=="STARTTLS") {
                            await writer.WriteLineAsync(tag+" OK start TLS"); writer.Dispose();
                            transport=await Secure(transport); current=new StreamReader(transport,Encoding.ASCII,false,1024,true); writer=new StreamWriter(transport,Encoding.ASCII,1024,true) { NewLine="\r\n",AutoFlush=true };
                        } else if(verb=="CAPABILITY") { await writer.WriteLineAsync("* CAPABILITY IMAP4rev1 AUTH=PLAIN"+(idle?" IDLE":"")); await writer.WriteLineAsync(tag+" OK capability"); }
                        else if(verb=="AUTHENTICATE") { await writer.WriteLineAsync("+"); _=await current.ReadLineAsync(cancellation.Token); await writer.WriteLineAsync(tag+" OK authenticated"); }
                        else if(verb=="LOGIN")await writer.WriteLineAsync(tag+" OK authenticated");
                        else if(verb=="EXAMINE") {
                            await writer.WriteLineAsync("* FLAGS (\\Seen)"); await writer.WriteLineAsync("* 2 EXISTS"); await writer.WriteLineAsync("* 0 RECENT");
                            await writer.WriteLineAsync("* OK [UIDVALIDITY 55] validity"); await writer.WriteLineAsync("* OK [UIDNEXT 3] next"); await writer.WriteLineAsync(tag+" OK [READ-ONLY] opened");
                        } else if(verb=="UID"&&line.Contains("BODY.PEEK[",StringComparison.Ordinal)) {
                            string section=line[(line.IndexOf('[')+1)..line.IndexOf(']')]; string body="Synthetic plain preview";
                            await writer.WriteLineAsync("* 1 FETCH (UID 1 BODY["+section+"]<0> {"+body.Length+"}"); await writer.WriteAsync(body); await writer.WriteLineAsync(")"); await writer.WriteLineAsync(tag+" OK fetched");
                        } else if(verb=="UID"&&line.Contains("SEARCH",StringComparison.Ordinal)) {
                            await writer.WriteLineAsync("* SEARCH 3"); await writer.WriteLineAsync(tag+" OK searched");
                        } else if(verb=="FETCH") {
                            await writer.WriteLineAsync("* 2 FETCH (UID 2)"); await writer.WriteLineAsync(tag+" OK latest");
                        } else if(verb=="UID"&&line.Contains("BODYSTRUCTURE",StringComparison.Ordinal)) {
                            bool html=line.Contains("FETCH 2 ",StringComparison.Ordinal); int uid=html?2:1;
                            await writer.WriteLineAsync("* "+uid+" FETCH (UID "+uid+" BODYSTRUCTURE (\"TEXT\" \""+(html?"HTML":"PLAIN")+"\" (\"CHARSET\" \"UTF-8\") NIL NIL \"8BIT\" 23 1 NIL NIL NIL NIL))"); await writer.WriteLineAsync(tag+" OK structure");
                        } else if(verb=="UID"&&line.Contains("FETCH",StringComparison.Ordinal)) {
                            for(int uid=1;uid<=2;uid++)await writer.WriteLineAsync("* "+uid+" FETCH (UID "+uid+" INTERNALDATE \"14-Oct-2026 12:00:00 +0000\" ENVELOPE (\"14-Oct-2026 12:00:00 +0000\" \"Synthetic subject\" ((\"Synthetic sender\" NIL \"sender\" \"example.invalid\")) NIL NIL NIL NIL NIL NIL \"<synthetic@example.invalid>\"))");
                            await writer.WriteLineAsync(tag+" OK headers");
                        } else if(verb=="IDLE") { idleTag=tag; await writer.WriteLineAsync("+ idling"); await writer.WriteLineAsync("* 3 EXISTS"); }
                        else if(verb=="LOGOUT") { await writer.WriteLineAsync("* BYE closing"); await writer.WriteLineAsync(tag+" OK logout"); break; }
                        else await writer.WriteLineAsync(tag+" OK completed");
                    }
                } finally { writer.Dispose(); if(current!=reader)current.Dispose(); transport.Dispose(); }
            } catch(Exception ex) when(ex is IOException or System.Security.Authentication.AuthenticationException or OperationCanceledException or SocketException) { Console.WriteLine("Synthetic TLS fixture: "+ex.GetType().Name+": "+ex.Message); }
        }
        public async ValueTask DisposeAsync() { cancellation.Cancel(); listener.Stop(); try { await Completion; } catch(Exception) {} Certificate.Dispose(); cancellation.Dispose(); }
    }
}
