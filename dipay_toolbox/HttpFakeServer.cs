using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;

namespace dipay_toolbox
{
    /// <summary>
    /// 本地假 HTTP/HTTPS 服务器：对任意请求（不限方法/路径/Host）无条件返回固定 JSON。
    /// 与 DnsHijack 配合：spm.auto-pai.com → 本地 IP → 80(HTTP) / 443(HTTPS) 直接应答，不发真实外网。
    /// HTTPS 用自动生成的自签名证书完成 TLS 握手（原车机客户端 ssl._create_unverified_context() 不校验证书，可直接握手）。
    /// 用 TcpListener 手写 HTTP 响应，无 HttpListener 的 URL ACL 依赖，管理员权限即可运行。
    /// </summary>
    public sealed class HttpFakeServer : IDisposable
    {
        private const int MaxRequestBytes = 65536;
        private const string DefaultDnsName = "spm.auto-pai.com";

        private readonly int _port;                   // HTTP 端口，默认 80
        private readonly string _body;                // 无条件返回的响应体
        private readonly string _contentType;
        private readonly Action<string, IPEndPoint> _onRequest;   // 回调：请求行、来源端点
        private readonly int _httpsPort;              // HTTPS 端口，默认 443；0 = 不启用
        private readonly string _tlsDnsName;          // 自签名证书 CN/SAN
        private readonly X509Certificate2 _providedCert;         // 外部传入证书（可空 → 自动生成）

        private TcpListener _listener;                // HTTP
        private TcpListener _httpsListener;           // HTTPS
        private X509Certificate2 _tlsCert;
        private volatile bool _running;
        private Thread _acceptThread;                 // HTTP accept 线程
        private Thread _httpsAcceptThread;            // HTTPS accept 线程
        private long _requests;

        /// <summary>最近一次 TLS 握手错误（诊断用；成功为 null）</summary>
        public volatile string LastTlsError;

        /// <param name="port">HTTP 监听端口，默认 80</param>
        /// <param name="body">无条件返回的响应体（原样字节）</param>
        /// <param name="onRequest">收到请求时的回调（请求行, 来源端点）</param>
        /// <param name="contentType">响应 Content-Type</param>
        /// <param name="httpsPort">HTTPS 监听端口，默认 443；传 0 则不启用 HTTPS</param>
        /// <param name="tlsCertificate">外部证书；为空时自动生成 CN/SAN=spm.auto-pai.com 的自签名证书</param>
        /// <param name="tlsDnsName">自动生成证书时的域名</param>
        public HttpFakeServer(int port, string body,
                              Action<string, IPEndPoint> onRequest = null,
                              string contentType = "application/json; charset=utf-8",
                              int httpsPort = 443,
                              X509Certificate2 tlsCertificate = null,
                              string tlsDnsName = null)
        {
            _port = port;
            _body = body ?? "";
            _contentType = contentType;
            _onRequest = onRequest;
            _httpsPort = httpsPort;
            _providedCert = tlsCertificate;
            _tlsDnsName = string.IsNullOrEmpty(tlsDnsName) ? DefaultDnsName : tlsDnsName;
        }

        /// <summary>已处理的请求总数（HTTP + HTTPS）</summary>
        public long Requests
        {
            get { return Interlocked.Read(ref _requests); }
        }

        /// <summary>绑定 HTTP(+HTTPS) 端口并开始监听。端口被占用时抛 SocketException。</summary>
        public void Start()
        {
            if (_running) return;

            _listener = new TcpListener(IPAddress.Any, _port);
            _listener.Start();
            _running = true;

            _acceptThread = new Thread(delegate () { AcceptLoop(_listener, false); });
            _acceptThread.IsBackground = true;
            _acceptThread.Start();

            if (_httpsPort > 0)
            {
                _tlsCert = _providedCert ?? CreateSelfSignedCertificate(_tlsDnsName);
                _httpsListener = new TcpListener(IPAddress.Any, _httpsPort);
                _httpsListener.Start();
                _httpsAcceptThread = new Thread(delegate () { AcceptLoop(_httpsListener, true); });
                _httpsAcceptThread.IsBackground = true;
                _httpsAcceptThread.Start();
            }
        }

        /// <summary>停止监听并释放端口</summary>
        public void Stop()
        {
            _running = false;
            try { if (_listener != null) _listener.Stop(); } catch { }
            try { if (_httpsListener != null) _httpsListener.Stop(); } catch { }
            try { if (_acceptThread != null) _acceptThread.Join(1000); } catch { }
            try { if (_httpsAcceptThread != null) _httpsAcceptThread.Join(1000); } catch { }

            // 自动生成的证书由本类释放；外部传入的证书归调用方管理
            if (_providedCert == null && _tlsCert != null)
            {
                try { _tlsCert.Dispose(); } catch { }
                _tlsCert = null;
            }
        }

        public void Dispose()
        {
            Stop();
        }

        // ---------- 内部实现 ----------

        private void AcceptLoop(TcpListener listener, bool tls)
        {
            while (_running)
            {
                TcpClient c;
                try
                {
                    c = listener.AcceptTcpClient();
                }
                catch (SocketException)
                {
                    if (!_running) break;
                    continue;
                }
                catch (ObjectDisposedException) { break; }

                Thread t = new Thread(delegate () { HandleClient(c, tls); });
                t.IsBackground = true;
                t.Start();
            }
        }

        private void HandleClient(TcpClient c, bool tls)
        {
            string requestLine = "";
            IPEndPoint remote = null;
            try
            {
                remote = (IPEndPoint)c.Client.RemoteEndPoint;
                c.Client.ReceiveTimeout = 3000;

                using (NetworkStream ns = c.GetStream())
                {
                    Stream io = ns;
                    SslStream ssl = null;

                    // HTTPS：先完成 TLS 握手（自签名证书），再读 HTTP 请求
                    if (tls)
                    {
                        try
                        {
                            ssl = new SslStream(ns, false);
                            // .NET Framework 4.8 必须显式指定协议：用 TLS 1.2（现代客户端均支持，
                            // 避免列出已被 Windows 默认禁用的 TLS 1.0/1.1；SslProtocols.None 在 .NET Framework 不支持）
                            ssl.AuthenticateAsServer(_tlsCert, false, SslProtocols.Tls12, false);
                            LastTlsError = null;
                            io = ssl;
                        }
                        catch (Exception ex)
                        {
                            LastTlsError = ex.ToString();
                            return;   // 握手失败，关闭连接
                        }
                    }

                    byte[] buf = new byte[MaxRequestBytes];
                    int got = 0;
                    int headerEnd = -1;

                    // 读到请求头结束（\r\n\r\n 或 \n\n）
                    while (got < buf.Length)
                    {
                        int n = io.Read(buf, got, buf.Length - got);
                        if (n <= 0) break;
                        got += n;
                        headerEnd = FindHeaderEnd(buf, got);
                        if (headerEnd >= 0) break;
                    }

                    if (got > 0)
                    {
                        string head = Encoding.ASCII.GetString(buf, 0, Math.Min(got, 1024));
                        int nl = head.IndexOf('\r');
                        if (nl < 0) nl = head.IndexOf('\n');
                        requestLine = nl >= 0 ? head.Substring(0, nl) : head;

                        // 若头部声明了 Content-Length（如 POST 提交），把请求体读完再应答，
                        // 避免客户端因发送被截断而丢弃响应。
                        if (headerEnd >= 0)
                        {
                            int cl = ParseContentLength(buf, headerEnd);
                            int need = headerEnd + 4 + cl;
                            if (cl > 0 && need > got && need <= MaxRequestBytes)
                            {
                                while (got < need)
                                {
                                    int n = io.Read(buf, got, need - got);
                                    if (n <= 0) break;
                                    got += n;
                                }
                            }
                        }
                    }

                    Interlocked.Increment(ref _requests);
                    if (_onRequest != null && remote != null) _onRequest(requestLine, remote);

                    // 无条件返回固定 JSON
                    byte[] bodyBytes = Encoding.UTF8.GetBytes(_body);
                    string respHead = "HTTP/1.1 200 OK\r\n"
                        + "Content-Type: " + _contentType + "\r\n"
                        + "Content-Length: " + bodyBytes.Length + "\r\n"
                        + "Connection: close\r\n"
                        + "Server: local-fake\r\n"
                        + "\r\n";
                    byte[] headBytes = Encoding.ASCII.GetBytes(respHead);
                    io.Write(headBytes, 0, headBytes.Length);
                    io.Write(bodyBytes, 0, bodyBytes.Length);
                    io.Flush();

                    if (ssl != null) { try { ssl.Close(); } catch { } }
                }
            }
            catch { }
            finally
            {
                try { c.Close(); } catch { }
            }
        }

        /// <summary>
        /// 生成自签名证书（RSA 2048，有效期 1 年，服务器认证 EKU + SAN=域名/localhost）。
        /// 必须使用持久化密钥容器：.NET Framework 的 CertificateRequest.CreateSelfSigned
        /// 若用临时密钥，SSPI/SslStream 会报 SEC_E_NO_CREDENTIALS（安全包中没有可用的凭证）。
        /// 客户端若不校验证书（如原车机客户端的 unverified context）可直接完成 TLS 握手。
        /// </summary>
        private static X509Certificate2 CreateSelfSignedCertificate(string dnsName)
        {
            // 持久化密钥容器（用户级），保证 SSPI 能取到私钥
            CspParameters csp = new CspParameters();
            csp.KeyContainerName = "DipayFakeTls_" + Guid.NewGuid().ToString("N");

            using (RSACryptoServiceProvider rsa = new RSACryptoServiceProvider(2048, csp))
            {
                CertificateRequest req = new CertificateRequest(
                    "CN=" + dnsName, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

                // 服务器认证（Server Authentication）EKU
                req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
                    new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));

                // SAN：域名 + localhost
                SubjectAlternativeNameBuilder san = new SubjectAlternativeNameBuilder();
                san.AddDnsName(dnsName);
                san.AddDnsName("localhost");
                req.CertificateExtensions.Add(san.Build());

                return req.CreateSelfSigned(
                    DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(1));
            }
            // 密钥容器保留在用户密钥集（GUID 命名，不回收），保证证书私钥在运行期始终可用
        }

        /// <summary>查找请求头结束标记，返回 \r\n\r\n 起始下标；找不到返回 -1</summary>
        private static int FindHeaderEnd(byte[] b, int len)
        {
            for (int i = 0; i + 3 < len; i++)
                if (b[i] == '\r' && b[i + 1] == '\n' && b[i + 2] == '\r' && b[i + 3] == '\n') return i;
            for (int i = 0; i + 1 < len; i++)
                if (b[i] == '\n' && b[i + 1] == '\n') return i;
            return -1;
        }

        /// <summary>从请求头解析 Content-Length；没有返回 0</summary>
        private static int ParseContentLength(byte[] b, int headerEnd)
        {
            string head = Encoding.ASCII.GetString(b, 0, headerEnd);
            int idx = head.IndexOf("content-length:", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return 0;
            idx += "content-length:".Length;
            while (idx < head.Length && (head[idx] == ' ' || head[idx] == '\t' || head[idx] == '\r' || head[idx] == '\n')) idx++;
            int end = idx;
            while (end < head.Length && head[end] >= '0' && head[end] <= '9') end++;
            int val;
            if (!int.TryParse(head.Substring(idx, end - idx), out val)) return 0;
            return val;
        }
    }
}
