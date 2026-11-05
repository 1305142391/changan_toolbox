using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;


namespace dipay_toolbox
{
    public partial class FormMain : Form
    {
        // ---------- 常量与配置 ----------
        private const string AdbPassword = "adb36987";                 // 车机 adb shell 验证密码
        private const string DiPlayPackage = "com.shihab.diplay";      // DiPlay 包名
        private const string DiPlayPackage2 = "com.shihab.diplay.hudtest";      // DiPlay 包名
        private const string ViaPackage = "mark.via";                  // Via 浏览器包名


        /// <summary>无条件返回的固定响应体</summary>
        private const string FakeJson = "{\"code\":\"0000\",\"message\":\"success\",\"data\":1355568945323}";


        private static readonly string[] AdbFiles = new string[]
        {
            "adb.exe", "AdbWinApi.dll", "AdbWinUsbApi.dll"
        };

        private static string AdbPath;          // 程序目录中的 adb.exe 完整路径
        private string _device;                 // 当前设备序列号

        // 应用显示名进程内缓存：包名 → 名称（同一车机会话内刷新不重复拉包/解析）
        private static readonly Dictionary<string, string> LabelCache = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly object LabelCacheLock = new object();
        // 正在后台解析名称的包名集合，避免重复双击并发重复拉包
        private static readonly HashSet<string> ResolvingLabels = new HashSet<string>(StringComparer.Ordinal);


        public FormMain()
        {
            InitializeComponent();
            // 双击某一行才后台解析该应用的真实名称
            listView应用列表.MouseDoubleClick += listView应用列表_MouseDoubleClick;
        }

        // ---------- 日志（输出到窗体日志框，跨线程安全） ----------
        private void Log(string msg)
        {
            if (textBox日志.InvokeRequired)
            {
                textBox日志.BeginInvoke(new MethodInvoker(delegate { Log(msg); }));
                return;
            }
            textBox日志.AppendText(DateTime.Now.ToString("HH:mm:ss  ") + msg.ToString().Trim() + Environment.NewLine);
        }

        private bool EnsureDevice()
        {
            if (string.IsNullOrEmpty(_device))
            {
                Log("未连接设备，请先点击「启动服务」");
                return false;
            }
            return true;
        }

        // ---------- 窗体加载：初始化 ADB 并检测设备 ----------
        private void FormMain_Load(object sender, EventArgs e)
        {
           

            Task.Run(delegate
            {
                

                Log("程序启动，初始化 ADB ...");
                if (!SetupAdb())
                {
                    Log("缺少文件：adb.exe / AdbWinApi.dll / AdbWinUsbApi.dll（请放到程序目录）");
                    return;
                }
                Log("ADB就绪");
                _device = GetDevice();
                if (string.IsNullOrEmpty(_device))
                    Log("未检测到设备，请确认车机已连接ADB");
                else
                    Log("设备: " + _device);
            });
        }

        // ---------- 工具方法 ----------
        private static string Quote(string s)
        {
            return "\"" + s.Replace("\"", "\\\"") + "\"";
        }

        /// <summary>进程执行结果</summary>
        internal class ProcessResult
        {
            public bool Success = false;
            public string Stdout = "";
            public string Stderr = "";
        }

        /// <summary>初始化 ADB：直接使用程序目录下的 adb.exe + 2 个 DLL</summary>
        private static bool SetupAdb()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            foreach (string f in AdbFiles)
            {
                if (!File.Exists(Path.Combine(baseDir, f)))
                    return false;
            }
            AdbPath = Path.Combine(baseDir, "adb.exe");
            return true;
        }

        /// <summary>定位 APK：exe 同目录 → apk_files 子目录</summary>
        private static string ResolveApk(string apkFile)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            string local = Path.Combine(baseDir, apkFile);
            if (System.IO.File.Exists(local)) return local;

            local = Path.Combine(baseDir, "apk_files", apkFile);
            if (File.Exists(local)) return local;

            return null;
        }

        // ---------- 进程执行封装 ----------
        private static ProcessResult RunProcess(string fileName, string arguments, string stdinText, int timeoutSec)
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = fileName;
            psi.Arguments = arguments;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardInput = (stdinText != null);
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;

            ProcessResult r = new ProcessResult();
            try
            {
                using (Process p = Process.Start(psi))
                {
                    StringBuilder so = new StringBuilder();
                    StringBuilder se = new StringBuilder();
                    p.OutputDataReceived += delegate (object sender, DataReceivedEventArgs e)
                    {
                        if (e.Data != null) so.AppendLine(e.Data);
                    };
                    p.ErrorDataReceived += delegate (object sender, DataReceivedEventArgs e)
                    {
                        if (e.Data != null) se.AppendLine(e.Data);
                    };
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();

                    if (stdinText != null)
                    {
                        p.StandardInput.Write(stdinText);
                        p.StandardInput.Flush();
                        p.StandardInput.Close();
                    }

                    if (!p.WaitForExit(timeoutSec * 1000))
                    {
                        try { p.Kill(); } catch { }
                        p.WaitForExit(2000);
                        r.Success = false;
                        r.Stdout = so.ToString();
                        r.Stderr = "命令超时(" + timeoutSec + "s)";
                        return r;
                    }
                    p.WaitForExit(); // 等待异步读取排空

                    r.Success = (p.ExitCode == 0);
                    r.Stdout = so.ToString();
                    r.Stderr = se.ToString();
                    return r;
                }
            }
            catch (Exception ex)
            {
                r.Success = false;
                r.Stderr = ex.Message;
                return r;
            }
        }

        /// <summary>直接运行 adb 参数（无 stdin）</summary>
        private static ProcessResult RunAdbRaw(string[] args, int timeoutSec = 120)
        {
            string arguments = "";
            foreach (string a in args) arguments += Quote(a) + " ";
            return RunProcess(AdbPath, arguments, null, timeoutSec);
        }

        /// <summary>adb shell 封装：把密码写入 stdin 满足车机验证，跑完去掉提示行</summary>
        private static ProcessResult Shell(string device, string command, int timeoutSec = 120)
        {
            string arguments = "-s " + Quote(device) + " shell " + Quote(command);
            ProcessResult r = RunProcess(AdbPath, arguments, AdbPassword + "\n", timeoutSec);

            string outText = r.Stdout;
            r.Stdout = outText.Trim();
            return r;
        }

        // ---------- 设备与应用操作 ----------
        private static string GetDevice()
        {
            ProcessResult r = RunAdbRaw(new string[] { "devices" });
            if (!r.Success) return null;

            string[] lines = r.Stdout.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Contains("\tdevice"))
                    return line.Split('\t')[0].Trim();
            }
            return null;
        }


        private bool InstallApk(string device, string localApk, string name)
        {
            string fname = Path.GetFileName(localApk);
            string[] targets = new string[]
            {
                "/data/local/tmp/" + fname,
                "/storage/emulated/0/" + fname
            };

            string remote = null;
            foreach (string t in targets)
            {
                ProcessResult r = RunAdbRaw(new string[] { "-s", device, "push", localApk, t }, 300);
                if (r.Success) { remote = t; break; }
            }
            if (remote == null)
            {
                Log("  推送失败");
                return false;
            }

            // 白名单伪装，绕过车机安装校验
            Shell(device, "setprop vecentek.model 1");

            ProcessResult ir = Shell(device, "pm install -r " + remote);
            Shell(device, "rm -f " + remote);

            if (ir.Stdout.Contains("Success"))
            {
                Log(" " + name + " 安装成功");
                return true;
            }
            Log( ir.Stdout);
            return false;
        }

        private bool Uninstall(string device, string pkg)
        {
            ProcessResult r = Shell(device, "pm uninstall " + pkg);
            if (r.Stdout.Contains("Success"))
            {
                Log(" " + pkg + " 卸载成功");
                return true;
            }
            Log( r.Stdout);
            return false;
        }


        private static void OpenSettings(string device)
        {
            Shell(device, "am start com.android.settings/com.android.settings.Settings");
        }


        // ---------- 简单输入弹窗 ----------
        private static string Prompt(string title, string prompt, string defaultValue)
        {
            Form f = new Form();
            f.Text = title;
            f.FormBorderStyle = FormBorderStyle.FixedDialog;
            f.StartPosition = FormStartPosition.CenterScreen;
            f.MaximizeBox = false;
            f.MinimizeBox = false;
            f.ClientSize = new Size(360, 110);
            f.AcceptButton = null;
            f.CancelButton = null;

            Label lbl = new Label();
            lbl.Text = prompt;
            lbl.SetBounds(12, 10, 336, 24);

            TextBox tb = new TextBox();
            tb.Text = defaultValue;
            tb.SetBounds(12, 38, 336, 24);

            Button ok = new Button();
            ok.Text = "确定";
            ok.DialogResult = DialogResult.OK;
            ok.SetBounds(264, 72, 84, 28);

            Button cancel = new Button();
            cancel.Text = "取消";
            cancel.DialogResult = DialogResult.Cancel;
            cancel.SetBounds(176, 72, 84, 28);

            f.AcceptButton = ok;
            f.CancelButton = cancel;
            f.Controls.AddRange(new Control[] { lbl, tb, ok, cancel });

            if (f.ShowDialog() != DialogResult.OK) return null;
            return tb.Text.Trim();
        }

        // ---------- 按钮事件 ----------
        /// <summary>启动服务：启动 ADB 服务 + 检测设备 + 车机二级认证（密码验证）测试</summary>
        private void button启动服务_Click(object sender, EventArgs e)
        {
            Task.Run(delegate
            {
                if (!SetupAdb())
                {
                    Log("缺少文件：adb.exe / AdbWinApi.dll / AdbWinUsbApi.dll（请放到程序目录）");
                    return;
                }
                Log("启动 ADB 服务 ...");
                ProcessResult r = RunAdbRaw(new string[] { "start-server" });
                Log(r.Success ? " ADB 服务已启动" : "启动失败: " + r.Stderr.Trim());

                _device = GetDevice();
                if (string.IsNullOrEmpty(_device))
                {
                    Log("未检测到设备，请确认车机已连接ADB并开启二级认证");
                    return;
                }
                Log(" 设备: " + _device);

                ProcessResult auth = Shell(_device, "echo ok");
                Log(auth.Success ? " 认证通过" : "认证失败: " + auth.Stderr.Trim());
            });
        }

        /// <summary>关闭服务：停止 ADB 服务</summary>
        private void button关闭服务_Click(object sender, EventArgs e)
        {
            Task.Run(delegate
            {
                Log("关闭 ADB 服务 ...");
                ProcessResult r = RunAdbRaw(new string[] { "kill-server" });
                Log(r.Success ? " ADB 服务已关闭" : "关闭失败: " + r.Stderr.Trim());
                _device = null;
            });
        }

        /// <summary>Via 浏览器安装：先选择车型系统，再安装对应 APK</summary>
        private void buttonVia浏览器安装_Click(object sender, EventArgs e)
        {
            Task.Run(delegate
            {
                if (!EnsureDevice()) return;
                Log("正在安装 Via ...");

                string apk = ResolveApk("via.apk");
                if (apk == null)
                {
                    Log("缺少 via.apk（请放到程序目录或 apk_files 目录）");
                    return;
                }
                if (InstallApk(_device, apk, "Via"))
                    Log("提示：请到车机桌面点击 via 图标打开");
                Shell(_device, "monkey -p " + ViaPackage + " -c android.intent.category.LAUNCHER 1");
            });
        }


        /// <summary>Via 浏览器卸载</summary>
        private void buttonVia浏览器卸载_Click(object sender, EventArgs e)
        {
            Task.Run(delegate
            {
                if (!EnsureDevice()) return;
                Uninstall(_device, ViaPackage);
            });
        }

        /// <summary>DiPlay 安装</summary>
        private void buttonDiPlay安装_Click(object sender, EventArgs e)
        {
            Task.Run(delegate
            {
                if (!EnsureDevice()) return;
                Log("正在安装 DiPlay ...");

                string apk = ResolveApk("diplay.apk");
                if (apk == null)
                {
                    Log("缺少 diplay.apk（请放到程序目录或 apk_files 目录）");
                    return;
                }
                if (InstallApk(_device, apk, "DiPlay"))
                    Log("提示：请到车机桌面点击 DiPlay 图标打开");
                Shell(_device, "monkey -p " + DiPlayPackage + " -c android.intent.category.LAUNCHER 1");
                Shell(_device, "monkey -p " + DiPlayPackage2 + " -c android.intent.category.LAUNCHER 1");

                
            });
        }

        /// <summary>DiPlay 卸载</summary>
        private void buttonDiPlay卸载_Click(object sender, EventArgs e)
        {
            Task.Run(delegate
            {
                if (!EnsureDevice()) return;
                Uninstall(_device, DiPlayPackage);
                Uninstall(_device, DiPlayPackage2);
            });
        }

        /// <summary>打开安卓系统设置</summary>
        private void button打开安卓设置_Click(object sender, EventArgs e)
        {
            Task.Run(delegate
            {
                if (!EnsureDevice()) return;
                OpenSettings(_device);
            });
        }

        /// <summary>设置APP安装：文件选择器选 APK（可自行改成固定文件名）</summary>
        private void button设置APP安装_Click(object sender, EventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Title = "选择要安装的 APK";
            ofd.Filter = "APK 文件|*.apk";
            string apkDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "apk_files");
            if (Directory.Exists(apkDir)) ofd.InitialDirectory = apkDir;
            if (ofd.ShowDialog(this) != DialogResult.OK) return;

            string apk = ofd.FileName;
            Task.Run(delegate
            {
                if (!EnsureDevice()) return;
                Log("正在安装 " + Path.GetFileName(apk) + " ...");
                InstallApk(_device, apk, Path.GetFileNameWithoutExtension(apk));
            });
        }

        /// <summary>设置APP卸载：弹窗输入包名（可自行改成固定包名）</summary>
       

        /// <summary>清空缓存：清空 DiPlay 与 Via 的应用缓存</summary>
    

        DnsHijack dns = null;
        HttpFakeServer http = null;
        private void button认证服务启动_Click(object sender, EventArgs e){

            IPAddress targetIp;
            string targetDesc;

            if (dns != null || http != null){
                Log("服务已在运行，请先点「关闭」再启动");
                return;
            }

            try{
                if (DnsHijack.TryGetHotspotIp(out targetIp, out targetDesc)) {
                    Log("热点网卡: " + targetDesc + " -> " + targetIp);
                }else{
                    targetIp = IPAddress.Parse("127.0.0.1");
                    Log("未检测到热点网卡 IP，回退 127.0.0.1（仅本机可访问，开热点后再启动会自动取到）");
                }

                dns = new DnsHijack(53, targetIp.ToString(), "spm.auto-pai.com", 
                    delegate (string domain, IPEndPoint src){
                        Log(string.Format("DNS 查询 {0} <- {1}", domain, src));
                    },delegate (IPEndPoint local){
                        string hot = DnsHijack.IsHotspotAddress(local.Address) ? "（热点网卡，已监听）" : "";
                        Log("已监听 " + local.Address + ":53 " + hot);
                    });
                dns.Start();
                //DNS启动
                http = new HttpFakeServer(80, FakeJson,
                    delegate (string requestLine, IPEndPoint src){
                        Log(string.Format("[ HTTP {0} <- {1}  → 已返回固定 JSON", requestLine, src));
                    });
                http.Start();
                //HTTP启动

            }
            catch (Exception ex){
                // 部分启动失败时清理，避免 53/80 端口残留占用
                try { if (dns != null) dns.Stop(); } catch { }
                try { if (http != null) http.Stop(); } catch { }
                Log("启动失败：" + ex.Message);
            }
        }

        private void button认证服务关闭_Click(object sender, EventArgs e)
        {
            try
            {
                if (dns != null) { dns.Stop(); dns = null; }
                if (http != null) { http.Stop(); http = null; }
                Log("认证服务已关闭，53/80 端口已释放");
            }
            catch (Exception ex)
            {
                Log("关闭异常：" + ex.Message);
            }
        }

        private void FormMain_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (dns != null) { dns.Stop(); dns = null; }
            if (http != null) { http.Stop(); http = null; }
            ProcessResult r = RunAdbRaw(new string[] { "kill-server" });
            _device = null;
        }
        private void RefreshAppList()
        {
            if (!EnsureDevice()) return;
            string serial = _device;
            Log("正在读取应用列表 ...");

            Task.Run(delegate
            {
                try
                {
                    List<AppEntry> apps = ListPackages(serial);
                    // 初始只显示包名短名；真实名称由用户双击某一行时再单独解析回填
                    listView应用列表.BeginInvoke(new MethodInvoker(delegate
                    {
                        listView应用列表.Items.Clear();
                        foreach (AppEntry app in apps)
                        {
                            string source = app.System ? "系统" : "第三方";
                            if (app.Disabled) source += "(已停用)";

                            ListViewItem item = new ListViewItem(ShortName(app.Package));
                            item.SubItems.Add(app.Package);
                            item.SubItems.Add(source);
                            item.Tag = app.Package;
                            listView应用列表.Items.Add(item);
                        }
                        Log("共 " + apps.Count + " 个应用，双击某一行可解析该应用名称");
                    }));
                }
                catch (Exception ex)
                {
                    Log("刷新应用列表失败: " + ex.Message);
                }
            });
        }
        private void button应用刷新_Click(object sender, EventArgs e)
        {
            RefreshAppList();
        }
        private string SelectedPackage()
        {
            if (listView应用列表.SelectedItems.Count == 0) return null;
            return (string)listView应用列表.SelectedItems[0].Tag;
        }

        private void button应用停用_Click(object sender, EventArgs e)
        {
            string pkg = SelectedPackage();
            if (pkg == null) { Log("请先在列表中选择一个应用"); return; }

            if (MessageBox.Show(this,
                    "停用后应用不再显示和运行，可随时通过「启用」恢复。\n\n包名: " + pkg + "\n\n确定要停用吗？",
                    "停用应用", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            Task.Run(delegate
            {
                ProcessResult r = Shell(_device, "pm disable-user --user 0 " + pkg);
                string outText = (r.Stdout + " " + r.Stderr).Trim();
                if (outText.Contains("new state:"))
                {
                    Log("已停用: " + pkg);
                    RefreshAppList();
                }
                else
                {
                    Log("停用失败: " + outText);
                }
            });
        }

        private void button应用启用_Click(object sender, EventArgs e)
        {
            string pkg = SelectedPackage();
            if (pkg == null) { Log("请先在列表中选择一个应用"); return; }

            Task.Run(delegate
            {
                ProcessResult r = Shell(_device, "pm enable " + pkg);
                string outText = (r.Stdout + " " + r.Stderr).Trim();
                if (outText.Contains("new state:"))
                {
                    Log("已启用: " + pkg);
                    RefreshAppList();
                }
                else
                {
                    Log("启用失败: " + outText);
                }
            });
        }

        private void button应用卸载_Click(object sender, EventArgs e)
        {
            string pkg = SelectedPackage();
            if (pkg == null) { Log("请先在列表中选择一个应用"); return; }

            if (MessageBox.Show(this,
                    "卸载可能删除此应用的账号和设置；系统保护应用可能无法卸载。\n\n包名: " + pkg + "\n\n确定要卸载吗？",
                    "卸载应用", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            Task.Run(delegate
            {
                ProcessResult r = Shell(_device, "pm uninstall " + pkg);
                string outText = (r.Stdout + " " + r.Stderr).Trim();
                if (r.Stdout.Contains("Success"))
                {
                    Log("已卸载: " + pkg);
                    RefreshAppList();
                }
                else
                {
                    Log("卸载失败: " + outText);
                }
            });
        }
        // ---------- 应用列表数据模型 ----------
        private class AppEntry
        {
            public string Package;
            public bool System;
            public bool Disabled;
            public string Label = "";
        }
        // ---------- 私有工具 ----------
        /// <summary>pm list packages（-s 系统 / -d 停用）三趟解析，返回全部应用。</summary>
        /// <remarks>
        /// 按「来源」排序（上 → 下）：第三方停用 → 第三方 → 系统停用 → 系统（即第三方最上边、
        /// 系统最下边，停用的排在同类最前）；组内按包名排序。
        /// </remarks>
        private static List<AppEntry> ListPackages(string device)
        {
            HashSet<string> all = ParsePkgSet(Shell(device, "pm list packages"));
            HashSet<string> sys = ParsePkgSet(Shell(device, "pm list packages -s"));
            HashSet<string> dis = ParsePkgSet(Shell(device, "pm list packages -d"));

            List<AppEntry> result = new List<AppEntry>();
            foreach (string pkg in all)
            {
                result.Add(new AppEntry
                {
                    Package = pkg,
                    System = sys.Contains(pkg),
                    Disabled = dis.Contains(pkg)
                });
            }
            result.Sort(delegate (AppEntry a, AppEntry b)
            {
                int ra = SourceRank(a);
                int rb = SourceRank(b);
                if (ra != rb) return ra - rb;
                return string.CompareOrdinal(a.Package, b.Package);
            });
            return result;
        }
        /// <summary>在资源管理器中定位并选中文件。</summary>
        private  void OpenInExplorer(string path)
        {
            try
            {
                Process.Start("explorer.exe", "/select,\"" + path + "\"");
            }
            catch (Exception ex)
            {
                Log("打开文件夹失败: " + ex.Message);
            }
        }
        /// <summary>解析 pm path 输出：取第一条 "package:<路径>"。</summary>
        private static string ParseApkPath(string stdout)
        {
            if (string.IsNullOrEmpty(stdout)) return null;
            foreach (string line in stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string s = line.Trim();
                if (s.StartsWith("package:", StringComparison.Ordinal))
                {
                    string p = s.Substring("package:".Length).Trim();
                    if (p.Length > 0) return p;
                }
            }
            return null;
        }

        /// <summary>来源优先级：0=第三方停用 1=第三方 2=系统停用 3=系统（越小越靠上）。</summary>
        private static int SourceRank(AppEntry app)
        {
            if (!app.System) return app.Disabled ? 0 : 1;
            return app.Disabled ? 2 : 3;
        }

        private static HashSet<string> ParsePkgSet(ProcessResult r)
        {
            HashSet<string> set = new HashSet<string>(StringComparer.Ordinal);
            if (!r.Success) return set;
            foreach (string line in r.Stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string s = line.Trim();
                if (s.StartsWith("package:"))
                {
                    int eq = s.IndexOf('=');
                    s = eq >= 0 ? s.Substring(eq + 1) : s.Substring("package:".Length);
                }
                if (s.Length > 0) set.Add(s);
            }
            return set;
        }

        // ---------- 名称解析（设备端硬编码名优先，否则拉本地 APK 解析 manifest/arsc，兼容 Android 8+） ----------
        /// <summary>解析单个应用显示名：设备端 nonLocalizedLabel 优先；否则拉取 APK 按设备语言本地解析。失败返回 null。
        /// 仅在用户双击某一行时调用，不做批量遍历。</summary>
        private static string ResolveAppLabel(string device, string pkg, string wantLang, string wantCountry)
        {
            // 1) 硬编码显示名（不依赖资源表，无需拉包）
            try
            {
                ProcessResult ra = Shell(device, "cmd package resolve-activity " + pkg, 30);
                string nl = ParseNonLocalizedLabel(ra.Stdout);
                if (!string.IsNullOrWhiteSpace(nl)) return nl.Trim();
            }
            catch { }

            // 2) 定位 APK 路径并临时拉取到本地
            try
            {
                ProcessResult rp = Shell(device, "pm path " + pkg, 30);
                string remote = ParseApkPath(rp.Stdout);
                if (string.IsNullOrEmpty(remote)) return null;

                string tempDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                    "data", "car_tool", "temp", "labels");
                Directory.CreateDirectory(tempDir);
                string local = Path.Combine(tempDir, SanitizeFileName(pkg) + ".apk");

                if (!File.Exists(local))
                {
                    ProcessResult pull = RunAdbRaw(new string[] { "-s", device, "pull", remote, local }, 180);
                    if (!pull.Success || !File.Exists(local)) return null;
                }

                string label = ResolveApkLabel(local, wantLang, wantCountry);
                return string.IsNullOrWhiteSpace(label) ? null : label.Trim();
            }
            catch { return null; }
        }

        /// <summary>双击列表某一行：后台解析该应用真实名称并回填到第一列；未解析/失败保持包名短名。</summary>
        private void listView应用列表_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            ListViewHitTestInfo hit = listView应用列表.HitTest(e.Location);
            ListViewItem item = hit.Item;
            if (item == null) return;

            string pkg = item.Tag as string;
            if (string.IsNullOrEmpty(pkg)) return;
            if (!EnsureDevice()) return;
            string serial = _device;

            // 正在解析中则忽略重复双击，避免并发重复拉包
            lock (LabelCacheLock)
            {
                if (ResolvingLabels.Contains(pkg)) return;
                ResolvingLabels.Add(pkg);
            }

            Task.Run(delegate
            {
                try
                {
                    string cached;
                    lock (LabelCacheLock)
                    {
                        if (LabelCache.TryGetValue(pkg, out cached) && !string.IsNullOrWhiteSpace(cached))
                        {
                            SetItemText(item, cached);
                            return;
                        }
                    }

                    string wantLang, wantCountry;
                    ParseDeviceLocale(serial, out wantLang, out wantCountry);

                    string name = ResolveAppLabel(serial, pkg, wantLang, wantCountry);
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        Log("未能解析应用名称: " + pkg);
                        return;
                    }
                    name = name.Trim();

                    lock (LabelCacheLock) LabelCache[pkg] = name;
                    SetItemText(item, name);
                }
                catch (Exception ex)
                {
                    Log("解析应用名称失败: " + pkg + " - " + ex.Message);
                }
                finally
                {
                    lock (LabelCacheLock) ResolvingLabels.Remove(pkg);
                }
            });
        }

        /// <summary>线程安全地回填某一行第一列文本；列表已清空/该行已移除时忽略。</summary>
        private void SetItemText(ListViewItem item, string text)
        {
            if (listView应用列表.IsDisposed) return;
            listView应用列表.BeginInvoke(new MethodInvoker(delegate
            {
                if (!listView应用列表.Items.Contains(item)) return;
                item.Text = text;
            }));
        }

        /// <summary>读取设备当前语言（zh-CN / en-US / zh 等），拆成 language 与 country；取不到两者均为空串。</summary>
        private static void ParseDeviceLocale(string device, out string lang, out string country)
        {
            lang = ""; country = "";
            try
            {
                ProcessResult p = Shell(device, "getprop persist.sys.locale", 10);
                string s = (p.Stdout ?? "").Trim();
                if (s.Length == 0)
                {
                    p = Shell(device, "getprop ro.product.locale", 10);
                    s = (p.Stdout ?? "").Trim();
                }
                if (s.Length == 0) return;

                s = s.Replace('_', '-');
                string[] parts = s.Split('-');
                lang = parts[0].Trim().ToLowerInvariant();
                if (parts.Length >= 2) country = parts[1].Trim().ToUpperInvariant();
            }
            catch { }
        }





        /// <summary>
        /// 读取本地 APK 的应用显示名（manifest 根 application 的 android:label）。
        /// wantLang/wantCountry 为设备当前语言（如 zh / CN）；传空表示取默认 values 配置。
        /// 字面量字符串直接返回；@string 资源引用按目标语言解析 resources.arsc；解析不到返回 null。
        /// 兼容 Android 8+ 的稀疏表/16 位偏移/紧凑条目。
        /// </summary>
        public static string ResolveApkLabel(string apkPath, string wantLang, string wantCountry)
        {
            if (string.IsNullOrEmpty(apkPath) || !File.Exists(apkPath)) return null;
            try
            {
                using (FileStream fs = new FileStream(apkPath, FileMode.Open, FileAccess.Read))
                using (ZipArchive zip = new ZipArchive(fs, ZipArchiveMode.Read))
                {
                    ZipArchiveEntry m = zip.GetEntry("AndroidManifest.xml");
                    if (m == null) return null;
                    byte[] axml;
                    using (MemoryStream ms = new MemoryStream())
                    using (Stream s = m.Open()) { s.CopyTo(ms); axml = ms.ToArray(); }

                    string literal; int vtype; uint refId;
                    if (!ReadManifestLabel(axml, out literal, out vtype, out refId)) return null;
                    if (vtype == 0x03) return literal;            // STRING 字面量
                    if (vtype != 0x01) return null;              // 其它类型（NULL/INT 等）忽略

                    ZipArchiveEntry ar = zip.GetEntry("resources.arsc");
                    if (ar == null) return null;
                    byte[] arsc;
                    using (MemoryStream ms = new MemoryStream())
                    using (Stream s = ar.Open()) { s.CopyTo(ms); arsc = ms.ToArray(); }
                    return ResolveArscString(arsc, refId, wantLang, wantCountry);
                }
            }
            catch { return null; }
        }
        // ResTable_type flags(tr+9): 0x01=FLAG_SPARSE, 0x02=FLAG_OFFSET16
        // ResTable_entry flags(ep+2): 0x0001=FLAG_COMPLEX, 0x0008=FLAG_COMPACT
        /// <summary>
        /// 解析 resources.arsc，按资源 ID 与目标语言取字符串。
        /// 匹配优先级：① 精确 lang+country（zh-CN）→ ② 同语言无地区（zh）→ ③ 默认 values(0/0)；
        /// wantLang 传空则只取默认 values。找不到返回 null。
        /// </summary>
        private static string ResolveArscString(byte[] arsc, uint refId, string wantLang, string wantCountry)
        {
            if (arsc.Length < 12) return null;
            uint typeIdx = (refId >> 16) & 0xFFu;
            uint entryIdx = refId & 0xFFFFu;

            string langWant = (wantLang ?? "").Trim().ToLowerInvariant();
            string ctryWant = (wantCountry ?? "").Trim().ToUpperInvariant();

            bool utf8; uint topSize;
            List<string> top = ReadStringPool(arsc, 12, out topSize, out utf8);

            int q = 12 + (int)topSize;
            string bestVal = null;
            int bestRank = -1;

            while (q + 8 <= arsc.Length)
            {
                ushort ct = LE16(arsc, q);
                uint cs = LE32(arsc, q + 4);
                if (cs < 8 || q + (int)cs > arsc.Length) break;

                if (ct == 0x0200) // RES_TABLE_PACKAGE_TYPE
                {
                    // ResTable_package: id@8, name[128]u16@12(256B), typeStrings@268, keyStrings@276
                    uint typeOff = LE32(arsc, q + 268);
                    uint keyOff = LE32(arsc, q + 276);
                    int p1 = q + (int)typeOff, t1 = (int)LE32(arsc, p1 + 4);
                    int p2 = q + (int)keyOff, t2 = (int)LE32(arsc, p2 + 4);
                    int tr = Math.Max(p1 + t1, p2 + t2);
                    int region = q + (int)cs;

                    while (tr + 8 <= region)
                    {
                        ushort tt = LE16(arsc, tr);
                        uint ts = LE32(arsc, tr + 4);
                        if (ts < 8 || tr + (int)ts > region) break;

                        if (tt == 0x0201) // RES_TABLE_TYPE_TYPE
                        {
                            byte tid = arsc[tr + 8];
                            byte typeFlags = arsc[tr + 9];
                            uint entryCount = LE32(arsc, tr + 12);
                            uint entriesStart = LE32(arsc, tr + 16);
                            uint configSize = LE32(arsc, tr + 20);
                            // 语言/地区为两字节 ASCII（默认块为 NUL NUL）
                            string cfgLang = ((char)arsc[tr + 28]).ToString() + ((char)arsc[tr + 29]);
                            string cfgCtry = ((char)arsc[tr + 30]).ToString() + ((char)arsc[tr + 31]);
                            string cfgLangL = cfgLang.ToLowerInvariant();
                            bool cfgDefault = (cfgLang == "\0\0" && cfgCtry == "\0\0");

                            if (tid == typeIdx)
                            {
                                int? innerOff = null;

                                if ((typeFlags & 0x01) != 0) // FLAG_SPARSE：稀疏表，按 idx 匹配
                                {
                                    int arrayStart = tr + 20 + (int)configSize;
                                    for (uint i = 0; i < entryCount; i++)
                                    {
                                        uint raw = LE32(arsc, arrayStart + (int)i * 4);
                                        if ((raw & 0xFFFFu) == entryIdx)
                                        {
                                            innerOff = (int)((raw >> 16) * 4);
                                            break;
                                        }
                                    }
                                }
                                else if ((typeFlags & 0x02) != 0) // FLAG_OFFSET16
                                {
                                    if (entryIdx < entryCount)
                                    {
                                        int arrayStart = tr + 20 + (int)configSize;
                                        ushort val = LE16(arsc, arrayStart + (int)entryIdx * 2);
                                        if (val != 0xFFFF) innerOff = val * 4;
                                    }
                                }
                                else // 普通稠密 u32 表
                                {
                                    if (entryIdx < entryCount)
                                    {
                                        int offTable = tr + (int)entriesStart - (int)entryCount * 4;
                                        uint off = LE32(arsc, offTable + (int)entryIdx * 4);
                                        if (off != 0xFFFFFFFFu) innerOff = (int)off;
                                    }
                                }

                                if (innerOff.HasValue)
                                {
                                    int ep = tr + (int)entriesStart + innerOff.Value;
                                    if (ep + 16 <= arsc.Length)
                                    {
                                        ushort eflags = LE16(arsc, ep + 2);
                                        if ((eflags & 0x0001) == 0) // 非 complex
                                        {
                                            byte dataType; uint data;
                                            if ((eflags & 0x0008) != 0) // compact entry：Res_value 紧跟 8 字节 entry 头
                                            {
                                                dataType = arsc[ep + 7];
                                                data = LE32(arsc, ep + 8);
                                            }
                                            else
                                            {
                                                dataType = arsc[ep + 11];
                                                data = LE32(arsc, ep + 12);
                                            }
                                            if (dataType == 0x03 && data < (uint)top.Count)
                                            {
                                                // rank: 2=精确语言+地区, 1=同语言无地区, 0=默认 values, -1=其它语言跳过
                                                int rank = -1;
                                                if (langWant.Length == 0)
                                                {
                                                    if (cfgDefault) rank = 0;
                                                }
                                                else if (cfgLangL == langWant && cfgCtry == ctryWant)
                                                {
                                                    rank = 2;
                                                }
                                                else if (cfgLangL == langWant && cfgCtry == "\0\0")
                                                {
                                                    rank = 1;
                                                }
                                                else if (cfgDefault)
                                                {
                                                    rank = 0;
                                                }

                                                if (rank >= 0 && rank > bestRank)
                                                {
                                                    bestRank = rank;
                                                    bestVal = top[(int)data];
                                                    if (rank == 2) return bestVal; // 精确命中直接返回
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        tr += (int)ts;
                    }
                }
                q += (int)cs;
            }
            return bestVal;
        }



        /// <summary>从 resolve-activity 输出提取 nonLocalizedLabel（支持单/双引号包裹含空格名称；null/空跳过）。</summary>
        private static string ParseNonLocalizedLabel(string output)
        {
            if (string.IsNullOrEmpty(output)) return null;
            MatchCollection ms = Regex.Matches(output,
                "nonLocalizedLabel=(?:'([^']*)'|\"([^\"]*)\"|([^\\s]+))");
            foreach (Match m in ms)
            {
                string val = m.Groups[1].Success ? m.Groups[1].Value
                    : m.Groups[2].Success ? m.Groups[2].Value
                    : m.Groups[3].Value;
                if (!string.IsNullOrEmpty(val) && val != "null") return val;
            }
            return null;
        }

        /// <summary>把包名转成合法文件名（包名本身仅含字母/数字/点，这里兜底替换非法字符）。</summary>
        private static string SanitizeFileName(string s)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in s)
                sb.Append((char.IsLetterOrDigit(c) || c == '.' || c == '-') ? c : '_');
            return sb.ToString();
        }


        /// <summary>包名短名：取最后一个点后的片段（com.shihab.diplay → diplay）。</summary>
        private static string ShortName(string pkg)
        {
            int i = pkg.LastIndexOf('.');
            string s = (i >= 0 && i < pkg.Length - 1) ? pkg.Substring(i + 1) : pkg;
            return s.Length > 0 ? s : pkg;
        }

        private void button打开_Click(object sender, EventArgs e)
        {
            string pkg = SelectedPackage();
            if (pkg == null) { Log("请先在列表中选择一个应用"); return; }
            if (!EnsureDevice()) return;
            string serial = _device;

            Task.Run(delegate
            {
                try
                {
                    // 1. 解析启动组件：cmd package resolve-activity --brief
                    //    （输出形如 "com.android.settings/.Settings"，本车机已实测可用）
                    ProcessResult r = Shell(serial, "cmd package resolve-activity --brief " + pkg);
                    string component = ParseLauncherComponent(r.Stdout);

                    ProcessResult start;
                    if (!string.IsNullOrEmpty(component))
                    {
                        Log("启动 " + pkg + "（" + component + "）...");
                        start = Shell(serial, "am start -n " + component);
                    }
                    else
                    {
                        Log("未解析到启动组件，改用 monkey 启动 " + pkg + " ...");
                        start = Shell(serial, "monkey -p " + pkg + " -c android.intent.category.LAUNCHER 1");
                    }

                    string outText = (start.Stdout + " " + start.Stderr).Trim();
                    if (outText.Length > 0)
                        Log(outText.Length > 200 ? outText.Substring(0, 200) : outText);

                    if (outText.IndexOf("Error", StringComparison.OrdinalIgnoreCase) >= 0
                        || outText.IndexOf("denied", StringComparison.OrdinalIgnoreCase) >= 0
                        || outText.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0)
                        Log("启动可能失败，请查看上方输出");
                    else
                        Log("已发送启动指令");
                }
                catch (Exception ex)
                {
                    Log("启动异常: " + ex.Message);
                }
            });
        }
        /// <summary>从 resolve-activity --brief 输出解析启动组件（如 com.android.settings/.Settings，含内部类 $）。</summary>
        private static string ParseLauncherComponent(string stdout)
        {
            if (string.IsNullOrEmpty(stdout)) return null;
            foreach (string line in stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string s = line.Trim();
                if (s.Length == 0) continue;
                // 跳过 priority=... 等属性行与说明行；取第一个 "包名/组件" 行
                if (s.IndexOf('/') > 0
                    && s.IndexOf('/') < s.Length - 1
                    && !s.StartsWith("priority=", StringComparison.Ordinal)
                    && !s.StartsWith("Resolving", StringComparison.Ordinal))
                {
                    return s;
                }
            }
            return null;
        }
        private void button导出_Click(object sender, EventArgs e)
        {
            string pkg = SelectedPackage();
            if (pkg == null) { Log("请先在列表中选择一个应用"); return; }
            if (!EnsureDevice()) return;
            string serial = _device;

            Task.Run(delegate
            {
                try
                {
                    // 1. 查 APK 路径（pm path 可能返回多行，取第一条即 base APK）
                    ProcessResult p = Shell(serial, "pm path " + pkg);
                    string remote = ParseApkPath(p.Stdout);
                    if (string.IsNullOrEmpty(remote))
                    {
                        Log("未找到 " + pkg + " 的 APK 路径: " + p.Stdout.Trim());
                        return;
                    }

                    // 2. 导出到 程序目录\apk_files\export\<包名>.apk
                    string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "apk_files", "export");
                    Directory.CreateDirectory(dir);
                    string local = Path.Combine(dir, pkg + ".apk");

                    Log("导出 " + remote + " → " + local + " ...");
                    ProcessResult r = RunAdbRaw(new string[] { "-s", serial, "pull", remote, local }, 300);
                    if (!r.Success || !File.Exists(local))
                    {
                        Log("导出失败: " + (r.Stdout + " " + r.Stderr).Trim());
                        return;
                    }

                    long size = new FileInfo(local).Length;
                    Log("已导出 " + pkg + ".apk（" + (size / 1024) + " KB）");
                    Log("路径: " + local);

                    // 3. 资源管理器定位该文件
                    OpenInExplorer(local);
                }
                catch (Exception ex)
                {
                    Log("导出异常: " + ex.Message);
                }
            });
        }

        private void button返回_Click(object sender, EventArgs e)
        {
            if (!EnsureDevice()) return;
            string serial = _device;
            Shell(serial, "input keyevent KEYCODE_BACK");
        }

        private void button主页_Click(object sender, EventArgs e)
        {
            if (!EnsureDevice()) return;
            string serial = _device;
            Shell(serial, "input keyevent KEYCODE_HOME");
        }

        private void button指定APP签名_Click(object sender, EventArgs e)
        {

            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Title = "选择要签名的 APK";
            ofd.Filter = "APK 文件|*.apk";
            string apkDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "apk_files");
            if (Directory.Exists(apkDir)) ofd.InitialDirectory = apkDir;
            if (ofd.ShowDialog(this) != DialogResult.OK) return;
            string apk = ofd.FileName;

            ofd.Title = "选择参照序列号APK";
            ofd.Filter = "APK 文件|*.apk";
            if (Directory.Exists(apkDir)) ofd.InitialDirectory = apkDir;
            if (ofd.ShowDialog(this) != DialogResult.OK) return;
            string oldapk = ofd.FileName;

            Task.Run(delegate{
                try{
                    Log("正在为 " + Path.GetFileName(apk) + " 生成兼容签名副本 ...");
                    string dest = CompatSign(oldapk, apk); 
                    long size = new FileInfo(dest).Length;
                    Log("已完成：" + dest + "（" + (size / 1024/1024) + " MB）");
                    Log("提示：请先卸载原应用再安装签名副本，否则可能提示签名不一致");
                    OpenInExplorer(dest);
                }catch (Exception ex){
                    Log("签名失败: " + ex.Message);
                }
            });
        }

        private void button读取APP签名_Click(object sender, EventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Title = "选择要读取的 APK";
            ofd.Filter = "APK 文件|*.apk";
            string apkDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "apk_files");
            if (Directory.Exists(apkDir)) ofd.InitialDirectory = apkDir;
            if (ofd.ShowDialog(this) != DialogResult.OK) return;

            string apk = ofd.FileName;
            Task.Run(delegate
            {
                //if (!EnsureDevice()) return;
                try{
                    Log("正在读取 " + Path.GetFileName(apk) + " 的签名 ...");
                    string info = ReadSignerInfo(apk);   // ← 副类提供，只调一行
                    Log(Environment.NewLine + info.Trim());
                }catch (Exception ex){
                    Log("读取签名失败: " + ex.Message);
                }

            });
        }


    }
}
