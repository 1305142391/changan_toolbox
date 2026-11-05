using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace dipay_toolbox
{
    /// <summary>
    /// 本地 DNS 劫持服务器（对齐原 Python auth_core.DnsHijack 的电脑端行为）：
    /// 监听 UDP/TCP 指定端口（默认 53），命中目标域名（含子域）的 A 查询直接应答目标 IP（抢答），
    /// 其余查询转发到上游 DNS 并中继回包，保证热点设备上网不中断。
    /// 不解析、不持久化、不影响其他域名；退出时释放端口。
    /// </summary>
    public sealed class DnsHijack : IDisposable
    {
        private const int BufferSize = 4096;
        private const ushort QTypeA = 1;
        private const byte ResponseTtl = 60;          // 应答 TTL（秒）

        private readonly int _port;
        private readonly IPAddress _targetIp;          // 劫持应答地址（抢答返回的 IP）
        private readonly string _targetDomain;         // 劫持域名，如 spm.auto-pai.com（子域也命中）
        private readonly IPAddress _upstream;          // 非劫持域名的上游 DNS，默认阿里 223.5.5.5
        private readonly Action<string, IPEndPoint> _onQuery;   // 回调：查询域名、来源端点
        private readonly Action<IPEndPoint> _onListen;          // 回调：绑定的本地端点

        private Socket _udp;
        private Socket _tcp;
        private volatile bool _running;
        private Thread _udpThread;
        private Thread _tcpThread;
        private long _queries;

        /// <param name="port">监听端口，默认 53</param>
        /// <param name="targetIp">命中后应答的 IP（如验证服务器地址）</param>
        /// <param name="targetDomain">劫持的域名，如 spm.auto-pai.com</param>
        /// <param name="onQuery">收到命中查询时的回调（域名, 来源端点）</param>
        /// <param name="onListen">绑定成功时的回调（本地端点）</param>
        /// <param name="upstream">上游 DNS 地址</param>
        public DnsHijack(int port, string targetIp, string targetDomain,
                         Action<string, IPEndPoint> onQuery = null,
                         Action<IPEndPoint> onListen = null,
                         string upstream = "223.5.5.5")
        {
            _port = port;
            _targetIp = IPAddress.Parse(targetIp);
            _targetDomain = (targetDomain ?? "").TrimEnd('.');
            _upstream = IPAddress.Parse(upstream);
            _onQuery = onQuery;
            _onListen = onListen;
        }

        /// <summary>已收到的查询总数（含转发）</summary>
        public long Queries
        {
            get { return Interlocked.Read(ref _queries); }
        }

        /// <summary>绑定 UDP/TCP 端口并开始监听。端口被占用（如 Windows 热点 ICS）时抛 SocketException。</summary>
        public void Start()
        {
            if (_running) return;

            _udp = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            _udp.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ExclusiveAddressUse, false);
            _udp.Bind(new IPEndPoint(IPAddress.Any, _port));

            _tcp = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _tcp.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ExclusiveAddressUse, false);
            _tcp.Bind(new IPEndPoint(IPAddress.Any, _port));
            _tcp.Listen(8);

            _running = true;
            _udpThread = new Thread(UdpLoop);
            _udpThread.IsBackground = true;
            _udpThread.Start();

            _tcpThread = new Thread(TcpLoop);
            _tcpThread.IsBackground = true;
            _tcpThread.Start();

            if (_onListen != null)
            {
                _onListen((IPEndPoint)_udp.LocalEndPoint);
                _onListen((IPEndPoint)_tcp.LocalEndPoint);
            }
        }

        /// <summary>停止监听并释放端口</summary>
        public void Stop()
        {
            _running = false;
            try { if (_udp != null) _udp.Close(); } catch { }
            try { if (_tcp != null) _tcp.Close(); } catch { }
            try { if (_udpThread != null) _udpThread.Join(1000); } catch { }
            try { if (_tcpThread != null) _tcpThread.Join(1000); } catch { }
        }

        public void Dispose()
        {
            Stop();
        }

        // ---------- 内部实现 ----------

        private void UdpLoop()
        {
            byte[] buf = new byte[BufferSize];
            EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
            while (_running)
            {
                int n;
                try
                {
                    n = _udp.ReceiveFrom(buf, ref remote);
                }
                catch (ObjectDisposedException) { break; }
                catch (SocketException)
                {
                    if (!_running) break;
                    continue;
                }

                IPEndPoint client = (IPEndPoint)remote;
                byte[] resp = HandleQuery(buf, n, client);
                if (resp != null)
                {
                    try { _udp.SendTo(resp, 0, resp.Length, SocketFlags.None, client); } catch { }
                }
            }
        }

        private void TcpLoop()
        {
            while (_running)
            {
                Socket c;
                try
                {
                    c = _tcp.Accept();
                }
                catch (ObjectDisposedException) { break; }
                catch (SocketException)
                {
                    if (!_running) break;
                    continue;
                }

                Thread t = new Thread(delegate () { HandleTcpClient(c); });
                t.IsBackground = true;
                t.Start();
            }
        }

        private void HandleTcpClient(Socket c)
        {
            try
            {
                c.ReceiveTimeout = 3000;
                byte[] hdr = new byte[2];
                if (ReadFull(c, hdr, 2) != 2) return;
                int payloadLen = (hdr[0] << 8) | hdr[1];
                if (payloadLen <= 0 || payloadLen > BufferSize) return;

                byte[] data = new byte[payloadLen];
                if (ReadFull(c, data, payloadLen) != payloadLen) return;

                IPEndPoint client = (IPEndPoint)c.RemoteEndPoint;
                byte[] resp = HandleQuery(data, payloadLen, client);
                if (resp == null) return;

                c.Send(new byte[] { (byte)((resp.Length >> 8) & 0xFF), (byte)(resp.Length & 0xFF) });
                c.Send(resp);
            }
            catch { }
            finally
            {
                try { c.Close(); } catch { }
            }
        }

        private static int ReadFull(Socket s, byte[] buf, int need)
        {
            int got = 0;
            while (got < need)
            {
                int n = s.Receive(buf, got, need - got, SocketFlags.None);
                if (n <= 0) break;
                got += n;
            }
            return got;
        }

        /// <summary>收到查询：命中目标域名 + A 记录 → 抢答；否则转发上游并中继。</summary>
        private byte[] HandleQuery(byte[] data, int len, IPEndPoint client)
        {
            string name;
            ushort qtype;
            int qEnd;
            if (!TryParse(data, len, out name, out qtype, out qEnd)) return null;

            Interlocked.Increment(ref _queries);
            if (_onQuery != null) _onQuery(name, client);

            if (Matches(name) && qtype == QTypeA)
                return BuildResponse(data, qEnd, _targetIp);

            return Forward(data, len);
        }

        /// <summary>转发到上游 DNS 并取回响应（2 秒超时）</summary>
        private byte[] Forward(byte[] data, int len)
        {
            byte[] buf = new byte[BufferSize];
            try
            {
                using (Socket s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    s.ReceiveTimeout = 2000;
                    s.SendTo(data, 0, len, SocketFlags.None, new IPEndPoint(_upstream, 53));
                    int n = s.Receive(buf);
                    byte[] outb = new byte[n];
                    Buffer.BlockCopy(buf, 0, outb, 0, n);
                    return outb;
                }
            }
            catch (SocketException) { return null; }
            catch { return null; }
        }

        private bool Matches(string name)
        {
            if (string.IsNullOrEmpty(_targetDomain) || string.IsNullOrEmpty(name)) return false;
            if (name == _targetDomain) return true;
            return name.EndsWith("." + _targetDomain);
        }

        // ---------- DNS 报文解析与应答构建（静态，可单测） ----------

        /// <summary>
        /// 解析 DNS 查询的问题段：返回域名（小写）、查询类型、问题段结束偏移。
        /// 支持压缩指针（防御性，查询中少见）；格式非法返回 false。
        /// </summary>
        public static bool TryParse(byte[] d, int len, out string name, out ushort qtype, out int qEnd)
        {
            name = null;
            qtype = 0;
            qEnd = 0;
            if (d == null || len < 12) return false;

            StringBuilder sb = new StringBuilder();
            int cursor = 12;   // 原报文游标（决定问题段结束位置）
            int p = 12;        // 标签游标（可能跳向压缩指针）
            int jumps = 0;

            while (true)
            {
                if (p >= len) return false;
                byte b = d[p];

                if (b == 0)
                {
                    cursor = p + 1;
                    break;
                }
                if ((b & 0xC0) == 0xC0)
                {
                    // 压缩指针：低 14 位为偏移
                    if (p + 1 >= len) return false;
                    int off = ((b & 0x3F) << 8) | d[p + 1];
                    if (off >= len || ++jumps > 8) return false;
                    cursor = p + 2;   // 名字在原报文中到此为止
                    p = off;
                    continue;
                }
                if ((b & 0xC0) != 0) return false;
                if (p + 1 + b > len) return false;

                if (sb.Length > 0) sb.Append('.');
                for (int i = 0; i < b; i++) sb.Append((char)d[p + 1 + i]);
                p += 1 + b;
            }

            if (cursor + 4 > len) return false;
            name = sb.ToString().ToLowerInvariant();
            qtype = (ushort)((d[cursor] << 8) | d[cursor + 1]);
            qEnd = cursor + 4;   // 跳过 TYPE + CLASS
            return true;
        }

        /// <summary>
        /// 构建抢答应答：原样复制 ID 与问题段，追加一条 A 记录（名称指针 0xC00C）。
        /// </summary>
        public static byte[] BuildResponse(byte[] query, int qEnd, IPAddress ip)
        {
            int questionLen = qEnd - 12;
            byte[] resp = new byte[qEnd + 16];   // 12 头部 + 问题 + 16 应答RR(A记录)

            Buffer.BlockCopy(query, 0, resp, 0, 12);   // 头部（含原 ID）
            resp[2] = 0x81;                            // QR=1 响应, RD=1, RA=1
            resp[3] = 0x80;
            resp[4] = 0; resp[5] = 1;                  // QDCOUNT=1
            resp[6] = 0; resp[7] = 1;                  // ANCOUNT=1
            resp[8] = 0; resp[9] = 0;                  // NSCOUNT=0
            resp[10] = 0; resp[11] = 0;                // ARCOUNT=0

            Buffer.BlockCopy(query, 12, resp, 12, questionLen);   // 问题段原样复制

            int o = qEnd;
            resp[o++] = 0xC0; resp[o++] = 0x0C;        // 名称压缩指针 → 问题中的域名
            resp[o++] = 0; resp[o++] = 1;              // TYPE = A
            resp[o++] = 0; resp[o++] = 1;              // CLASS = IN
            resp[o++] = 0; resp[o++] = 0; resp[o++] = 0; resp[o++] = ResponseTtl;  // TTL
            resp[o++] = 0; resp[o++] = 4;              // RDLENGTH = 4

            byte[] ipb = ip.GetAddressBytes();
            resp[o++] = ipb[0];
            resp[o++] = ipb[1];
            resp[o++] = ipb[2];
            resp[o++] = ipb[3];
            return resp;
        }

        // ---------- 热点网卡识别（对应原 DnsHijack._is_hotspot） ----------

        /// <summary>返回持有该 IP 的活动网卡描述；找不到返回 null。</summary>
        public static string HotspotDescription(IPAddress addr)
        {
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                foreach (UnicastIPAddressInformation u in ni.GetIPProperties().UnicastAddresses)
                {
                    if (u.Address.AddressFamily == AddressFamily.InterNetwork && u.Address.Equals(addr))
                        return ni.Description;
                }
            }
            return null;
        }

        /// <summary>判断该地址是否属于热点虚拟网卡（Windows 移动热点的网卡描述通常含 Wi-Fi Direct / Virtual）。</summary>
        public static bool IsHotspotAddress(IPAddress addr)
        {
            string desc = HotspotDescription(addr);
            if (string.IsNullOrEmpty(desc)) return false;
            return desc.IndexOf("Wi-Fi Direct", StringComparison.OrdinalIgnoreCase) >= 0
                || desc.IndexOf("Virtual", StringComparison.OrdinalIgnoreCase) >= 0
                || desc.IndexOf("Hosted", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// 自动查找热点网卡的 IPv4 地址（Windows 移动热点 / 托管网络）。
        /// 优先级：
        ///   1. 描述含 "Wi-Fi Direct"（Windows 移动热点标准网卡）
        ///   2. 192.168.137.x 网段（Windows 移动热点 ICS 默认网段）
        ///   3. 描述含 "Hosted"（托管网络）
        ///   4. 描述含 "Virtual" 且非 VMware/VirtualBox（避免误中虚拟机网卡）
        ///   5. 带默认网关的活动网卡（兜底）
        /// 都找不到返回 false（ip/description 为 null）。
        /// </summary>
        public static bool TryGetHotspotIp(out IPAddress ip, out string description)
        {
            ip = null;
            description = null;

            // 1. Wi-Fi Direct（热点已开时的标准描述）
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                string desc = ni.Description ?? "";
                if (desc.IndexOf("Wi-Fi Direct", StringComparison.OrdinalIgnoreCase) < 0) continue;
                IPAddress v4 = FirstIpv4(ni);
                if (v4 != null) { ip = v4; description = desc; return true; }
            }

            // 2. ICS 默认网段 192.168.137.x
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                IPAddress v4 = FirstIpv4(ni);
                if (v4 != null && v4.GetAddressBytes()[0] == 192
                    && v4.GetAddressBytes()[1] == 168
                    && v4.GetAddressBytes()[2] == 137)
                { ip = v4; description = ni.Description; return true; }
            }

            // 3. Hosted（托管网络）
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                string desc = ni.Description ?? "";
                if (desc.IndexOf("Hosted", StringComparison.OrdinalIgnoreCase) < 0) continue;
                IPAddress v4 = FirstIpv4(ni);
                if (v4 != null) { ip = v4; description = desc; return true; }
            }

            // 4. Virtual（排除虚拟机网卡，避免误中 VMware/VirtualBox）
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                string desc = ni.Description ?? "";
                if (desc.IndexOf("Virtual", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (desc.IndexOf("VMware", StringComparison.OrdinalIgnoreCase) >= 0
                    || desc.IndexOf("VirtualBox", StringComparison.OrdinalIgnoreCase) >= 0
                    || desc.IndexOf("Hyper-V", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                IPAddress v4 = FirstIpv4(ni);
                if (v4 != null) { ip = v4; description = desc; return true; }
            }

            // 5. 带默认网关的活动网卡（兜底）
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.GetIPProperties().GatewayAddresses == null
                    || ni.GetIPProperties().GatewayAddresses.Count == 0) continue;
                IPAddress v4 = FirstIpv4(ni);
                if (v4 != null) { ip = v4; description = ni.Description; return true; }
            }

            return false;
        }

        /// <summary>取网卡第一个非回环 IPv4 地址；没有返回 null。</summary>
        private static IPAddress FirstIpv4(NetworkInterface ni)
        {
            foreach (UnicastIPAddressInformation u in ni.GetIPProperties().UnicastAddresses)
            {
                if (u.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(u.Address))
                    return u.Address;
            }
            return null;
        }
    }
}
