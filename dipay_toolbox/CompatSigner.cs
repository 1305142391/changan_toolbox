using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Utilities.Encoders;
using Org.BouncyCastle.Cms;
using Org.BouncyCastle.Asn1.Cms;
using Org.BouncyCastle.X509.Store;

namespace dipay_toolbox
{
    public partial class FormMain
    {
        // ---------------- 兼容签名常量 ----------------
        private const string CompatCreatedBy = "CHANGAN Car Tool";                // MANIFEST Created-By
        private const string CompatSignerName = "CHANGAN";                     // 签名文件前缀（ANANCAR.SF / ANANCAR.RSA）
        private const string CompatIdentityPwd = "CHANGAN";// local-signing.pfx 口令
        private const string CompatCn = "CHANGAN Car Tool Local Compatibility";   // 本地证书 CN（与参考版一致）

        // ---------------- 主界面唯一入口 ----------------
        /// <summary>
        /// 为指定 APK 生成飞鱼兼容签名副本（ADB 复用 FormMain 现有的 Shell/RunAdbRaw/ParseApkPath）。
        /// 返回签名副本完整路径；失败抛异常（调用方 catch 后 Log）。
        /// </summary>
        public string CompatSign(string oldapk, string sourceApk)
        {
            if (string.IsNullOrEmpty(sourceApk) || !File.Exists(sourceApk)) throw new Exception("找不到 APK 文件：" + sourceApk);
            if (string.IsNullOrEmpty(oldapk) || !File.Exists(oldapk)) throw new Exception("找不到 APK 文件：" + oldapk);

            string dataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "car_tool");
            Directory.CreateDirectory(dataDir);

            // 1) 参照序列号：从旧apk读取
            string serial = GetReferenceSerial(oldapk);

            // 2) 获取本地证书文件
            CompatIdentity id = CompatIdentity.GetOrCreate(serial);

            // 3) 生成 v1 签名副本
            string outDir = Path.Combine(dataDir, "signed-apks");
            Directory.CreateDirectory(outDir);
            string dest = Path.Combine(outDir, Path.GetFileNameWithoutExtension(sourceApk) + "_compat.apk");

            Log("正在重建 APK...");
            BuildSignedApk(sourceApk, dest, id.BcKey, id.BcCert);

            // 4) 自校验：证书序列号、包名/版本
            VerifySignedApk(dest, serial);
            ApkMeta srcMeta = ApkMeta.Read(sourceApk);
            ApkMeta dstMeta = ApkMeta.Read(dest);
            if (!string.Equals(srcMeta.Package, dstMeta.Package, StringComparison.Ordinal)
                || !string.Equals(srcMeta.VersionCode, dstMeta.VersionCode, StringComparison.Ordinal))
            {
                try { File.Delete(dest); } catch { }
                throw new Exception("签名副本的包名或版本发生异常变化，已停止安装");
            }

            return dest;
        }

        // ---------------- 读取 APK 签名信息（供「读取APP签名」按钮调用） ----------------
        /// <summary>读取本地 APK 的应用信息（包名/版本等）与 v1 签名证书信息，返回可显示的多行文本。
        /// 无 v1 证书（可能为 v2/v3 签名或无签名）时返回说明文字。</summary>
        public static string ReadSignerInfo(string apkPath)
        {
            if (string.IsNullOrEmpty(apkPath) || !File.Exists(apkPath))
                return "找不到 APK 文件：" + apkPath;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("文件: " + Path.GetFileName(apkPath));

            // --- 应用信息（解析 AndroidManifest.xml 的包名/版本号） ---
            try
            {
                ApkMeta meta = ApkMeta.Read(apkPath);
                sb.AppendLine("[应用信息]");
                sb.AppendLine("包名 package      : " + (meta.Package.Length > 0 ? meta.Package : "(未设置)"));
                sb.AppendLine("版本名 versionName: " + (meta.VersionName.Length > 0 ? meta.VersionName : "(未设置)"));
                sb.AppendLine("版本码 versionCode: " + (meta.VersionCode.Length > 0 ? meta.VersionCode : "(未设置)"));
                sb.AppendLine("最低SDK minSdk    : " + (meta.MinSdk.Length > 0 ? meta.MinSdk : "(未设置)"));
                sb.AppendLine("目标SDK targetSdk : " + (meta.TargetSdk.Length > 0 ? meta.TargetSdk : "(未设置)"));
            }
            catch (Exception ex)
            {
                sb.AppendLine("[应用信息] 解析失败: " + ex.Message);
            }

            // --- 签名证书 ---
            List<X509Certificate2> certs = ReadApkCertificates(apkPath);
            sb.AppendLine("[签名证书]");
            if (certs.Count == 0)
            {
                sb.AppendLine("未发现 v1 签名证书（可能使用 v2/v3 签名，或无签名）");
            }
            else
            {
                sb.AppendLine("签名方案: v1（META-INF 签名文件）");
                for (int i = 0; i < certs.Count; i++)
                {
                    X509Certificate2 c = certs[i];
                    sb.AppendLine("证书 #" + (i + 1));
                    sb.AppendLine("主 题 Subject : " + c.Subject);
                    sb.AppendLine("颁发者 Issuer : " + c.Issuer);
                    sb.AppendLine("序列号 Serial : " + c.SerialNumber);
                    sb.AppendLine("有效期        : " + c.NotBefore.ToString("yyyy-MM-dd HH:mm") + " ~ " + c.NotAfter.ToString("yyyy-MM-dd HH:mm"));
                    sb.AppendLine("签名算法      : " + c.SignatureAlgorithm.FriendlyName);
                    sb.AppendLine("指纹 SHA256   : " + FingerprintHex(c));
                }
            }
            return sb.ToString();
        }
        // ---------------- 应用显示名解析（供「应用管理」列表取真实标题，兼容 Android 8+） ----------------
        /// <summary>
        /// 读取本地 APK 的应用显示名（manifest 根 application 的 android:label）。
        /// 字面量字符串直接返回；@string 资源引用解析 resources.arsc 默认（空 locale）配置；
        /// 解析不到返回 null（调用方用包名短名兜底）。兼容 Android 8+ 的稀疏表/16 位偏移/紧凑条目。
        /// </summary>
        public static string ResolveApkLabel(string apkPath)
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
                    return ResolveArscString(arsc, refId);
                }
            }
            catch { return null; }
        }

        /// <summary>读取二进制字符串池（AXML / arsc 通用），返回字符串列表与池块大小、是否 UTF-8。</summary>
        private static List<string> ReadStringPool(byte[] b, int pool, out uint poolSize, out bool utf8)
        {
            poolSize = LE32(b, pool + 4);
            uint count = LE32(b, pool + 8);
            uint flags = LE32(b, pool + 16);
            uint stringsStart = LE32(b, pool + 20);
            utf8 = (flags & 0x00000100u) != 0;
            List<string> strings = new List<string>((int)count);
            for (uint i = 0; i < count; i++)
            {
                uint off = LE32(b, pool + 28 + (int)i * 4);
                strings.Add(ApkMeta.ReadPoolString(b, pool + (int)stringsStart + (int)off, utf8));
            }
            return strings;
        }

        /// <summary>解析 manifest 根 &lt;application&gt; 的 label 属性：输出字面量值、值类型与引用资源 ID。</summary>
        private static bool ReadManifestLabel(byte[] b, out string literal, out int vtype, out uint refId)
        {
            literal = null; vtype = 0; refId = 0;
            if (b.Length < 8) return false;

            bool utf8; uint spSize;
            List<string> strings = ReadStringPool(b, 8, out spSize, out utf8);

            int q = 8 + (int)spSize, len = b.Length;
            while (q + 8 <= len)
            {
                uint cType = LE32(b, q);
                uint cSize = LE32(b, q + 4);
                if (cSize < 8 || q + (int)cSize > len) break;

                if ((cType & 0xFFFFu) == 0x0102u) // START_ELEMENT
                {
                    uint nameIdx = LE32(b, q + 20);
                    string elem = nameIdx < strings.Count ? strings[(int)nameIdx] : null;
                    ushort attrStart = LE16(b, q + 24);
                    ushort attrSize = LE16(b, q + 26);
                    ushort attrCount = LE16(b, q + 28);
                    int ap = q + 16 + attrStart;

                    if (elem == "application")
                    {
                        for (int i = 0; i < attrCount; i++)
                        {
                            int rec = ap + i * attrSize;
                            uint an = LE32(b, rec + 4);
                            byte at = b[rec + 15];
                            uint ad = LE32(b, rec + 16);
                            string aname = an < strings.Count ? strings[(int)an] : null;
                            if (aname != "label") continue;

                            vtype = at; refId = ad;
                            if (at == 0x03 && ad < strings.Count) literal = strings[(int)ad];
                            return true;
                        }
                    }
                }
                q += (int)cSize;
            }
            return false;
        }

        // ResTable_type flags(tr+9): 0x01=FLAG_SPARSE, 0x02=FLAG_OFFSET16
        // ResTable_entry flags(ep+2): 0x0001=FLAG_COMPLEX, 0x0008=FLAG_COMPACT
        /// <summary>解析 resources.arsc，按资源 ID 取默认（空 locale）配置下的字符串；找不到返回 null。</summary>
        private static string ResolveArscString(byte[] arsc, uint refId)
        {
            if (arsc.Length < 12) return null;
            uint typeIdx = (refId >> 16) & 0xFFu;
            uint entryIdx = refId & 0xFFFFu;

            bool utf8; uint topSize;
            List<string> top = ReadStringPool(arsc, 12, out topSize, out utf8);

            int q = 12 + (int)topSize;
            string fallback = null;

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
                            ushort lang = LE16(arsc, tr + 28);
                            ushort country = LE16(arsc, tr + 30);

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
                                                if (lang == 0 && country == 0) return top[(int)data];
                                                if (fallback == null) fallback = top[(int)data];
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
            return fallback;
        }


        // ---------------- 参照证书 ----------------
        /// <summary>读取 v1 签名证书；返回唯一证书的序列号。
        /// 复用 FormMain 已有的 Shell / RunAdbRaw / ParseApkPath。</summary>
        private static string GetReferenceSerial(string oldapk)
        {
            List<X509Certificate2> certs = ReadApkCertificates(oldapk);
            if (certs.Count == 0)
                throw new Exception("无法读取APK签名证书（无 v1 证书）");
            if (certs.Count > 1)
                throw new Exception("APK使用多张证书，不能自动确定兼容签名序列号");
            return certs[0].SerialNumber;
        }

        /// <summary>读取 APK 的 v1 证书：META-INF/*.RSA|.DSA|.EC → PKCS7 DER → 证书列表（去重）。</summary>
        private static List<X509Certificate2> ReadApkCertificates(string apkPath)
        {
            List<X509Certificate2> result = new List<X509Certificate2>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using (FileStream fs = new FileStream(apkPath, FileMode.Open, FileAccess.Read))
            using (ZipArchive zip = new ZipArchive(fs, ZipArchiveMode.Read))
            {
                foreach (ZipArchiveEntry e in zip.Entries)
                {
                    string up = e.FullName.ToUpperInvariant();
                    if (!up.StartsWith("META-INF/", StringComparison.Ordinal)) continue;
                    if (!(up.EndsWith(".RSA") || up.EndsWith(".DSA") || up.EndsWith(".EC"))) continue;

                    byte[] der;
                    using (MemoryStream ms = new MemoryStream())
                    {
                        using (Stream s = e.Open()) s.CopyTo(ms);
                        der = ms.ToArray();
                    }
                    try
                    {
                        X509Certificate2Collection col = new X509Certificate2Collection();
                        col.Import(der); // PKCS7 签名数据自动解出内嵌证书
                        foreach (X509Certificate2 c in col)
                            if (seen.Add(c.Thumbprint))
                                result.Add(c);
                    }
                    catch { }
                }
            }
            return result;
        }

        // ---------------- 本地持久化身份（密钥 + 自签证书） ----------------
        /// <summary>本地签名身份：证书（含私钥）与其指纹。
        /// 首次连接时动态生成并永久保存，之后直接复用
        private class CompatIdentity
        {
            public X509Certificate2 Certificate;
            public string SerialHex;
            public string Fingerprint;
            /// <summary>BC 私钥（RsaPrivateCrtKeyParameters）：签名全程用 BC 托管，避免 .NET 密钥集坑</summary>
            public AsymmetricKeyParameter BcKey;
            /// <summary>BC 证书：PKCS7 内嵌与签名用</summary>
            public Org.BouncyCastle.X509.X509Certificate BcCert;

            /// <param name="referenceSerial">当前参照证书序列号</param>
            public static CompatIdentity GetOrCreate( string referenceSerial)
            {
                // 按参照证书序列号分目录：每个参照各持一把钥匙，换参照 APK 不互相覆盖。
                // 同序列号（同一车机/同一参照）复用同一把钥匙，保证后续升级包签名一致、可覆盖安装。
                string sub = NormalizeSerial(referenceSerial);
                if (sub == "0") sub = "default";
                string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data", "car_tool", "signing", sub);
                Directory.CreateDirectory(dir);
                string pfxPath = Path.Combine(dir, "local-signing.pfx");
                string keyPem = Path.Combine(dir, "local-signing-key.pem");
                string certPem = Path.Combine(dir, "local-signing-cert.pem");

                if (File.Exists(keyPem) && File.Exists(certPem))
                {
                    AsymmetricKeyParameter bcKey = ReadBcKey(keyPem);
                    Org.BouncyCastle.X509.X509Certificate bcCert = ReadBcCert(certPem);
                    X509Certificate2 cert = new X509Certificate2(bcCert.GetEncoded());
                    return new CompatIdentity{
                        Certificate = cert,
                        SerialHex = cert.SerialNumber,
                        Fingerprint = FingerprintHex(cert),
                        BcKey = bcKey,
                        BcCert = bcCert
                    };
                }

                // 只有旧 pfx 而没有 PEM（人为删过文件）→ 不覆盖已有钥匙，提示删除后重建
                if (File.Exists(pfxPath))
                    throw new Exception("已有证书文件但缺少 key/cert PEM，" + "请删除目录 " + dir + " 后重试一次");

                // 首次连接该设备 → 动态生成并保存
                return CreateNew(referenceSerial, dir);
            }

            /// <summary>读 PKCS#8 私钥 PEM（local-signing-key.pem）→ BC 私钥。</summary>
            private static AsymmetricKeyParameter ReadBcKey(string keyPemPath)
            {
                return Org.BouncyCastle.Security.PrivateKeyFactory.CreateKey(PemBody(keyPemPath));
            }

            /// <summary>读证书 PEM（local-signing-cert.pem）→ BC 证书。</summary>
            private static Org.BouncyCastle.X509.X509Certificate ReadBcCert(string certPemPath)
            {
                return new Org.BouncyCastle.X509.X509CertificateParser()
                    .ReadCertificate(PemBody(certPemPath));
            }

            /// <summary>提取 PEM 的 base64 主体并解码为 DER。</summary>
            private static byte[] PemBody(string path)
            {
                string pem = File.ReadAllText(path);
                System.Text.RegularExpressions.Match m =
                    System.Text.RegularExpressions.Regex.Match(pem,
                        "-----BEGIN [^-]+-----([A-Za-z0-9+/=\\s]+)-----END",
                        System.Text.RegularExpressions.RegexOptions.Singleline);
                if (!m.Success) throw new Exception("证书文件格式无效：" + path);
                return Convert.FromBase64String(
                    System.Text.RegularExpressions.Regex.Replace(m.Groups[1].Value, "\\s", ""));
            }

            

            /// <summary>BouncyCastle 生成 RSA-2048 自签证书（序列号=参照、CN 固定、SHA256）。
            /// 私钥以 PEM（PKCS#8）持久化，签名时从 PEM 恢复 —— 全程 BC 托管，
            /// 不经过 .NET 密钥集（避免 cert.PrivateKey/Export 导出无私钥 PFX 的坑）。
            /// 另写一份 BC PKCS12 格式的 pfx 副本供其他工具互查（含私钥）。
            /// 注意：X509Name 在 Org.BouncyCastle.Asn1.X509，X509V3CertificateGenerator /
            /// X509CertificateParser 在 Org.BouncyCastle.X509，均用完全限定名。</summary>
            private static CompatIdentity CreateNew(string serialHex, string dir)
            {
                RsaKeyPairGenerator gen = new RsaKeyPairGenerator();
                gen.Init(new KeyGenerationParameters(new SecureRandom(), 2048));
                AsymmetricCipherKeyPair pair = gen.GenerateKeyPair();

                BigInteger serial = new BigInteger(1, Hex.Decode(serialHex));
                Org.BouncyCastle.Asn1.X509.X509Name subject =
                    new Org.BouncyCastle.Asn1.X509.X509Name("CN=" + CompatCn);
                DateTime notBefore = DateTime.UtcNow.AddDays(-1);
                DateTime notAfter = DateTime.UtcNow.AddYears(10);

                // 旧式证书生成 API（兼容所有 BouncyCastle C# 版本）
                Org.BouncyCastle.X509.X509V3CertificateGenerator certGen =
                    new Org.BouncyCastle.X509.X509V3CertificateGenerator();
                certGen.SetSerialNumber(serial);
                certGen.SetIssuerDN(subject);
                certGen.SetSubjectDN(subject);
                certGen.SetNotBefore(notBefore);
                certGen.SetNotAfter(notAfter);
                certGen.SetPublicKey(pair.Public);
                certGen.SetSignatureAlgorithm("SHA256withRSA");

                Org.BouncyCastle.X509.X509Certificate bcCert = certGen.Generate(
                    new Asn1SignatureFactory("SHA256withRSA", pair.Private, new SecureRandom()));

                byte[] derCert = bcCert.GetEncoded();
                X509Certificate2 cert = new X509Certificate2(derCert); // 仅证书（无 .NET 私钥）

                // PEM 副本（签名恢复源；参考版同名文件，可互查/迁移）
                byte[] keyDer = PrivateKeyInfoFactory.CreatePrivateKeyInfo(pair.Private).GetDerEncoded();
                File.WriteAllText(Path.Combine(dir, "local-signing-key.pem"), PemEncode("PRIVATE KEY", keyDer));
                File.WriteAllText(Path.Combine(dir, "local-signing-cert.pem"), PemEncode("CERTIFICATE", derCert));

                // PFX 副本：BC PKCS12 写（含私钥；.NET 4.8 的 cert.PrivateKey=+Export 会丢私钥，弃用）
                Org.BouncyCastle.Pkcs.Pkcs12Store st = new Org.BouncyCastle.Pkcs.Pkcs12Store();
                st.SetKeyEntry("local", new Org.BouncyCastle.Pkcs.AsymmetricKeyEntry(pair.Private),
                    new Org.BouncyCastle.Pkcs.X509CertificateEntry[]
                    { new Org.BouncyCastle.Pkcs.X509CertificateEntry(bcCert) });
                using (FileStream fs = File.Create(Path.Combine(dir, "local-signing.pfx")))
                    st.Save(fs, CompatIdentityPwd.ToCharArray(), new SecureRandom());


                return new CompatIdentity
                {
                    Certificate = cert,
                    SerialHex = cert.SerialNumber,
                    Fingerprint = FingerprintHex(cert),
                    BcKey = pair.Private,
                    BcCert = bcCert
                };
            }

            private static string PemEncode(string label, byte[] der)
            {
                string b64 = Convert.ToBase64String(der);
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("-----BEGIN " + label + "-----");
                for (int i = 0; i < b64.Length; i += 64)
                    sb.AppendLine(b64.Substring(i, Math.Min(64, b64.Length - i)));
                sb.AppendLine("-----END " + label + "-----");
                return sb.ToString();
            }
        }

        /// <summary>序列号归一化：去空格/冒号、去前导零、大写。</summary>
        private static string NormalizeSerial(string s)
        {
            if (string.IsNullOrEmpty(s)) return "0";
            s = s.Trim().Replace(" ", "").Replace(":", "");
            s = s.TrimStart('0');
            return s.Length == 0 ? "0" : s.ToUpperInvariant();
        }

        /// <summary>证书 SHA-256 指纹（hex 小写）。</summary>
        private static string FingerprintHex(X509Certificate2 cert)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] h = sha.ComputeHash(cert.RawData);
                StringBuilder sb = new StringBuilder();
                foreach (byte b in h) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        // ---------------- v1 签名生成（MANIFEST/SF/RSA） ----------------
        private static byte[] BuildManifest(List<ZipEntryInfo> keep, out List<KeyValuePair<string, byte[]>> sections)
        {
            List<byte> main = new List<byte>();
            sections = new List<KeyValuePair<string, byte[]>>();
            AddLine(main, "Manifest-Version: 1.0");
            AddLine(main, "Created-By: " + CompatCreatedBy);
            main.Add(0x0d); main.Add(0x0a);

            foreach (ZipEntryInfo e in keep)
            {
                if (e.Name.EndsWith("/", StringComparison.Ordinal)) continue; // 目录不参与摘要
                byte[] sec = BuildSection(e);
                sections.Add(new KeyValuePair<string, byte[]>(e.Name, sec));
                main.AddRange(sec);
            }
            return main.ToArray();
        }

        private static byte[] BuildSection(ZipEntryInfo e)
        {
            List<byte> sec = new List<byte>();
            AddLine(sec, "Name: " + e.Name);
            AddLine(sec, "SHA-256-Digest: " + Sha256Base64(e.Content));
            sec.Add(0x0d); sec.Add(0x0a);
            return sec.ToArray();
        }

        private static byte[] BuildSf(byte[] manifest, List<KeyValuePair<string, byte[]>> sections)
        {
            byte[] mainAttrs = Encoding.UTF8.GetBytes(
                "Manifest-Version: 1.0\r\nCreated-By: " + CompatCreatedBy + "\r\n\r\n");
            List<byte> sf = new List<byte>();
            AddLine(sf, "Signature-Version: 1.0");
            AddLine(sf, "SHA-256-Digest-Manifest: " + Sha256Base64(manifest));
            AddLine(sf, "SHA-256-Digest-Manifest-Main-Attributes: " + Sha256Base64(mainAttrs));
            sf.Add(0x0d); sf.Add(0x0a);
            foreach (KeyValuePair<string, byte[]> kv in sections)
            {
                AddLine(sf, "Name: " + kv.Key);
                AddLine(sf, "SHA-256-Digest-Manifest: " + Sha256Base64(kv.Value));
                sf.Add(0x0d); sf.Add(0x0a);
            }
            return sf.ToArray();
        }

        /// <summary>分离式 PKCS7（DetachedSignature）：对 .SF 内容做 RSA/SHA-256 签名，内嵌证书。
        /// 用 BouncyCastle 全程托管（不经过 .NET SignedCms/密钥集）：
        /// 从 PFX 加载的证书在 .NET 4.8 中私钥密钥集经常匹配不上，报"密钥集不存在/该项不存在"。</summary>
        private static byte[] BuildPkcs7(byte[] sf, Org.BouncyCastle.X509.X509Certificate bcCert,
            AsymmetricKeyParameter bcKey)
        {
            CmsSignedDataGenerator gen = new CmsSignedDataGenerator();
            gen.AddSigner(bcKey, bcCert, CmsSignedGenerator.DigestSha256,
                (AttributeTable)null, (AttributeTable)null);

            // v1 签名要求证书内嵌在 PKCS7（Android 的 CERT.RSA 校验直接从中取证书）
            System.Collections.IList certList = new System.Collections.ArrayList();
            certList.Add(bcCert);
            IX509Store certStore = X509StoreFactory.Create("Certificate/Collection",
                new X509CollectionStoreParameters(certList));
            gen.AddCertificates(certStore);

            // 分离式(detached) PKCS7：content 不内嵌，与参考版一致；
            // 车机 JarVerifier 取 SignerInfo.messageDigest 属性与 CHANGAN.SF 字节比对，
            // 内嵌式(encapsulate=true)虽摘要相同但会多带一份 SF 副本，不必要。
            CmsSignedData csd = gen.Generate(new CmsProcessableByteArray(sf), false);
            return csd.GetEncoded();
        }

        private static string Sha256Base64(byte[] data)
        {
            using (SHA256 sha = SHA256.Create())
                return Convert.ToBase64String(sha.ComputeHash(data));
        }

        private static void AddLine(List<byte> buf, string line)
        {
            byte[] b = Encoding.UTF8.GetBytes(line);
            buf.AddRange(b);
            buf.Add(0x0d); buf.Add(0x0a);
        }

        // ---------------- ZIP 重建（原样复制条目 + 对齐 + 追加 META-INF） ----------------
        private static void BuildSignedApk(string source, string dest, AsymmetricKeyParameter bcKey,
            Org.BouncyCastle.X509.X509Certificate bcCert)
        {
            if (Path.GetFullPath(source).Equals(Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
                throw new Exception("签名副本不能覆盖原始 APK");

            List<ZipEntryInfo> entries = ReadZipEntries(source);

            // 重复条目防御
            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
            foreach (ZipEntryInfo e in entries)
                if (!names.Add(e.Name))
                    throw new Exception("APK 含有重复 ZIP 条目，不能安全生成签名副本");

            // 剔除旧签名文件（MANIFEST.MF / *.SF / *.RSA|DSA|EC），保留其余 META-INF
            List<ZipEntryInfo> keep = new List<ZipEntryInfo>();
            foreach (ZipEntryInfo e in entries)
                if (!IsOldSignatureEntry(e.Name))
                    keep.Add(e);

            // 生成 v1 三件套
            List<KeyValuePair<string, byte[]>> sections;
            byte[] manifest = BuildManifest(keep, out sections);
            byte[] sf = BuildSf(manifest, sections);
            byte[] rsa = BuildPkcs7(sf, bcCert, bcKey);

            // 新条目追加在最后
            List<ZipEntryInfo> items = new List<ZipEntryInfo>(keep);
            items.Add(MakeMetaEntry("META-INF/MANIFEST.MF", manifest));
            items.Add(MakeMetaEntry("META-INF/" + CompatSignerName + ".SF", sf));
            items.Add(MakeMetaEntry("META-INF/" + CompatSignerName + ".RSA", rsa));

            WriteZip(items, dest);
        }

        private static bool IsOldSignatureEntry(string name)
        {
            if (name.Equals("META-INF/MANIFEST.MF", StringComparison.OrdinalIgnoreCase)) return true;
            if (name.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase))
            {
                string up = name.ToUpperInvariant();
                if (up.EndsWith(".RSA") || up.EndsWith(".DSA") || up.EndsWith(".EC") || up.EndsWith(".SF"))
                    return true;
            }
            return false;
        }

        /// <summary>生成一个 DEFLATE 压缩的 META-INF 新条目。</summary>
        private static ZipEntryInfo MakeMetaEntry(string name, byte[] content)
        {
            byte[] compressed = DeflateRaw(content);
            DateTime now = DateTime.Now;
            ushort dosTime = (ushort)((now.Hour << 11) | (now.Minute << 5) | (now.Second / 2));
            ushort dosDate = (ushort)(((now.Year - 1980) << 9) | (now.Month << 5) | now.Day);
            return new ZipEntryInfo
            {
                Name = name,
                NameBytes = Encoding.UTF8.GetBytes(name),
                Flags = 0,
                Method = 8,
                DosTime = dosTime,
                DosDate = dosDate,
                Crc32 = Crc32.Compute(content),
                CompressedSize = (uint)compressed.Length,
                UncompressedSize = (uint)content.Length,
                Raw = compressed,
                Content = content,
                ExtAttrs = 0
            };
        }

        // ---------- 读 ZIP（EOCD → 中央目录 → 本地头 + 原始压缩数据） ----------
        private class ZipEntryInfo
        {
            public string Name;
            public byte[] NameBytes;
            public ushort Flags;
            public ushort Method;
            public ushort DosTime;
            public ushort DosDate;
            public uint Crc32;
            public uint CompressedSize;
            public uint UncompressedSize;
            public uint ExtAttrs;
            public byte[] Raw;      // 原样压缩字节（复制）
            public byte[] Content;  // 解压后内容（摘要用）
        }

        private static List<ZipEntryInfo> ReadZipEntries(string source)
        {
            byte[] data = File.ReadAllBytes(source);
            int len = data.Length;
            if (len < 22) throw new Exception("APK 缺少 ZIP 结束记录");

            // EOCD（末尾 65557 字节内扫描）
            int eocd = -1;
            int scanStart = Math.Max(0, len - 65557);
            for (int i = len - 22; i >= scanStart; i--)
            {
                if (data[i] == 0x50 && data[i + 1] == 0x4b && data[i + 2] == 0x05 && data[i + 3] == 0x06)
                { eocd = i; break; }
            }
            if (eocd < 0) throw new Exception("APK 缺少 ZIP 结束记录");
            ushort total = LE16(data, eocd + 10);
            uint cdOffset = LE32(data, eocd + 16);

            List<ZipEntryInfo> list = new List<ZipEntryInfo>();
            int p = (int)cdOffset;
            for (int i = 0; i < total; i++)
            {
                if (LE32(data, p) != 0x02014b50) throw new Exception("APK 中央目录损坏");
                ushort flags = LE16(data, p + 8);
                ushort method = LE16(data, p + 10);
                ushort time = LE16(data, p + 12);
                ushort date = LE16(data, p + 14);
                uint crc = LE32(data, p + 16);
                uint csize = LE32(data, p + 20);
                uint usize = LE32(data, p + 24);
                ushort nlen = LE16(data, p + 28);
                ushort elen = LE16(data, p + 30);
                ushort clen = LE16(data, p + 32);
                uint extAttrs = LE32(data, p + 38);
                uint loff = LE32(data, p + 42);

                byte[] nameBytes = new byte[nlen];
                Array.Copy(data, p + 46, nameBytes, 0, nlen);
                p += 46 + nlen + elen + clen;

                if (csize == 0xFFFFFFFFu || usize == 0xFFFFFFFFu || loff == 0xFFFFFFFFu)
                    throw new Exception("APK 使用 ZIP64 扩展，暂不支持");
                if ((flags & 0x0001) != 0)
                    throw new Exception("APK 含加密 ZIP 条目，暂不支持");
                if (method != 0 && method != 8)
                    throw new Exception("不支持的压缩方式(" + method + ")：" + Encoding.UTF8.GetString(nameBytes));
                if (csize > int.MaxValue || usize > int.MaxValue)
                    throw new Exception("APK 条目过大，暂不支持");

                // 本地头 → 原始压缩字节（loff 是 uint，传给 LE16/LE32 前必须转 int）
                if (LE32(data, (int)loff) != 0x04034b50) throw new Exception("APK 本地头损坏");
                ushort lhNlen = LE16(data, (int)loff + 26);
                ushort lhElen = LE16(data, (int)loff + 28);
                int start = (int)(loff + 30 + lhNlen + lhElen);
                byte[] raw = new byte[csize];
                Array.Copy(data, start, raw, 0, (int)csize);

                ZipEntryInfo e = new ZipEntryInfo
                {
                    Name = Encoding.UTF8.GetString(nameBytes),
                    NameBytes = nameBytes,
                    Flags = flags,
                    Method = method,
                    DosTime = time,
                    DosDate = date,
                    Crc32 = crc,
                    CompressedSize = csize,
                    UncompressedSize = usize,
                    ExtAttrs = extAttrs,
                    Raw = raw
                };
                e.Content = (method == 0) ? raw : InflateRaw(raw, (int)usize);
                list.Add(e);
            }
            return list;
        }

        // ---------- 写 ZIP（对齐规则：lib/*.so → 4096；其余 STORED → 4；DEFLATE → 不垫） ----------
        private class CentralRecord
        {
            public byte[] NameBytes;
            public ushort Flags;
            public ushort Method;
            public ushort Time;
            public ushort Date;
            public uint Crc;
            public uint Csize;
            public uint Usize;
            public uint ExtAttrs;
            public uint LocalOffset;
        }

        private static void WriteZip(List<ZipEntryInfo> items, string dest)
        {
            using (FileStream fs = new FileStream(dest, FileMode.Create, FileAccess.Write))
            using (BinaryWriter bw = new BinaryWriter(fs))
            {
                List<CentralRecord> cd = new List<CentralRecord>();

                foreach (ZipEntryInfo e in items)
                {
                    if (fs.Position > 0xFFFFFFFE) throw new Exception("APK 超过 4GB，暂不支持");

                    int align = 1;
                    if (e.Name.StartsWith("lib/", StringComparison.Ordinal) && e.Name.EndsWith(".so", StringComparison.Ordinal))
                        align = 4096;
                    else if (e.Method == 0)
                        align = 4;

                    byte[] extra = BuildAlignmentExtra(align, fs.Position, e.NameBytes.Length);
                    uint localOffset = (uint)fs.Position;
                    ushort outFlags = (ushort)(e.Flags & ~0x0008); // 清 data-descriptor 位，尺寸已知

                    bw.Write((uint)0x04034b50);
                    bw.Write((ushort)20);            // version needed
                    bw.Write(outFlags);
                    bw.Write(e.Method);
                    bw.Write(e.DosTime);
                    bw.Write(e.DosDate);
                    bw.Write(e.Crc32);
                    bw.Write(e.CompressedSize);
                    bw.Write(e.UncompressedSize);
                    bw.Write((ushort)e.NameBytes.Length);
                    bw.Write((ushort)extra.Length);
                    bw.Write(e.NameBytes);
                    bw.Write(extra);
                    bw.Write(e.Raw);

                    cd.Add(new CentralRecord
                    {
                        NameBytes = e.NameBytes,
                        Flags = outFlags,
                        Method = e.Method,
                        Time = e.DosTime,
                        Date = e.DosDate,
                        Crc = e.Crc32,
                        Csize = e.CompressedSize,
                        Usize = e.UncompressedSize,
                        ExtAttrs = e.ExtAttrs,
                        LocalOffset = localOffset
                    });
                }

                long cdStart = fs.Position;
                foreach (CentralRecord r in cd)
                {
                    bw.Write((uint)0x02014b50);
                    bw.Write((ushort)20);            // version made by
                    bw.Write((ushort)20);            // version needed
                    bw.Write(r.Flags);
                    bw.Write(r.Method);
                    bw.Write(r.Time);
                    bw.Write(r.Date);
                    bw.Write(r.Crc);
                    bw.Write(r.Csize);
                    bw.Write(r.Usize);
                    bw.Write((ushort)r.NameBytes.Length);
                    bw.Write((ushort)0);             // extra
                    bw.Write((ushort)0);             // comment
                    bw.Write((ushort)0);             // disk start
                    bw.Write((ushort)0);             // internal attrs
                    bw.Write(r.ExtAttrs);
                    bw.Write(r.LocalOffset);
                    bw.Write(r.NameBytes);
                }
                long cdEnd = fs.Position;

                bw.Write((uint)0x06054b50);
                bw.Write((ushort)0);
                bw.Write((ushort)0);
                bw.Write((ushort)cd.Count);
                bw.Write((ushort)cd.Count);
                bw.Write((uint)(cdEnd - cdStart));
                bw.Write((uint)cdStart);
                bw.Write((ushort)0);                 // comment length
            }
        }

        /// <summary>
        /// 计算 extra-field 填充，使数据区偏移对齐。
        /// extra 记录 = id(0x0001 填充) + size + 零字节；needed &lt; 4 时整体加一个 align，
        /// 既保证 extra 合法（≥4 字节）又不破坏取模同余。
        /// </summary>
        private static byte[] BuildAlignmentExtra(int align, long basePos, int nameLen)
        {
            if (align <= 1) return new byte[0];
            long raw = basePos + 30 + nameLen;
            long needed = (align - (raw % align)) % align;
            if (needed == 0) return new byte[0];

            long extraLen = needed >= 4 ? needed : needed + align;
            if (extraLen > 65535) throw new Exception("对齐填充过大");
            if (extraLen < 4) extraLen = 4;

            byte[] extra = new byte[extraLen];
            extra[0] = 0x01; extra[1] = 0x00;                          // id = 0x0001（ZIP padding）
            extra[2] = (byte)((extraLen - 4) & 0xFF);
            extra[3] = (byte)(((extraLen - 4) >> 8) & 0xFF);            // size
            return extra;
        }

        private static byte[] InflateRaw(byte[] raw, int expected)
        {
            byte[] buf = new byte[expected];
            using (MemoryStream ms = new MemoryStream(raw))
            using (DeflateStream ds = new DeflateStream(ms, CompressionMode.Decompress))
            {
                int n = 0;
                while (n < expected)
                {
                    int r = ds.Read(buf, n, expected - n);
                    if (r <= 0) break;
                    n += r;
                }
                if (n != expected) throw new Exception("APK 解压失败（长度不符）");
            }
            return buf;
        }

        private static byte[] DeflateRaw(byte[] data)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                using (DeflateStream ds = new DeflateStream(ms, CompressionMode.Compress, true))
                    ds.Write(data, 0, data.Length);
                return ms.ToArray();
            }
        }

        // ---------------- 签名副本自校验 ----------------
        private static void VerifySignedApk(string apkPath, string expectedSerial)
        {
            using (FileStream fs = new FileStream(apkPath, FileMode.Open, FileAccess.Read))
            using (ZipArchive zip = new ZipArchive(fs, ZipArchiveMode.Read))
            {
                ZipArchiveEntry e = zip.GetEntry("META-INF/" + CompatSignerName + ".RSA");
                if (e == null) throw new Exception("签名副本缺少 META-INF/" + CompatSignerName + ".RSA");
                byte[] der;
                using (MemoryStream ms = new MemoryStream())
                {
                    using (Stream s = e.Open()) s.CopyTo(ms);
                    der = ms.ToArray();
                }
                X509Certificate2Collection col = new X509Certificate2Collection();
                col.Import(der);
                if (col.Count == 0) throw new Exception("签名副本证书为空");
                if (!NormalizeSerial(col[0].SerialNumber).Equals(
                        NormalizeSerial(expectedSerial), StringComparison.OrdinalIgnoreCase))
                    throw new Exception("签名副本证书序列号与参照不一致");
            }
        }

        // ---------------- 小工具 ----------------
        private static ushort LE16(byte[] b, int p)
        {
            return (ushort)(b[p] | (b[p + 1] << 8));
        }

        private static uint LE32(byte[] b, int p)
        {
            return (uint)(b[p] | (b[p + 1] << 8) | (b[p + 2] << 16) | (b[p + 3] << 24));
        }

        /// <summary>CRC-32（ZIP 标准多项式），用于新条目。</summary>
        private sealed class Crc32
        {
            private static readonly uint[] Table = BuildTable();

            private static uint[] BuildTable()
            {
                uint[] t = new uint[256];
                for (uint i = 0; i < 256; i++)
                {
                    uint c = i;
                    for (int k = 0; k < 8; k++)
                        c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                    t[i] = c;
                }
                return t;
            }

            public static uint Compute(byte[] data)
            {
                uint c = 0xFFFFFFFFu;
                foreach (byte b in data)
                    c = Table[(c ^ b) & 0xFF] ^ (c >> 8);
                return c ^ 0xFFFFFFFFu;
            }
        }

        // ---------------- AndroidManifest.xml 最小解析（包名/版本校验用） ----------------
        private class ApkMeta
        {
            public string Package = "";
            public string VersionCode = "";
            public string VersionName = "";
            public string MinSdk = "";
            public string TargetSdk = "";

            public static ApkMeta Read(string apkPath)
            {
                using (FileStream fs = new FileStream(apkPath, FileMode.Open, FileAccess.Read))
                using (ZipArchive zip = new ZipArchive(fs, ZipArchiveMode.Read))
                {
                    ZipArchiveEntry e = zip.GetEntry("AndroidManifest.xml");
                    if (e == null) throw new Exception("APK 缺少 AndroidManifest.xml");
                    byte[] axml;
                    using (MemoryStream ms = new MemoryStream())
                    {
                        using (Stream s = e.Open()) s.CopyTo(ms);
                        axml = ms.ToArray();
                    }
                    return Parse(axml);
                }
            }

            /// <summary>解析二进制 AXML：字符串池 + XML 树（manifest/uses-sdk/application 属性）。</summary>
            private static ApkMeta Parse(byte[] b)
            {
                int len = b.Length;
                if (len < 8) throw new Exception("AndroidManifest.xml 损坏");

                int pool = 8; // 文件头 8 字节后即字符串池块
                uint spSize = LE32(b, pool + 4);
                uint spCount = LE32(b, pool + 8);
                uint spFlags = LE32(b, pool + 16);
                uint stringsStart = LE32(b, pool + 20);
                bool utf8 = (spFlags & 0x00000100u) != 0;

                List<string> strings = new List<string>();
                for (uint i = 0; i < spCount; i++)
                {
                    uint off = LE32(b, pool + 28 + (int)i * 4);
                    strings.Add(ReadPoolString(b, pool + (int)stringsStart + (int)off, utf8));
                }

                int q = pool + (int)spSize;
                ApkMeta meta = new ApkMeta();
                while (q + 8 <= len)
                {
                    uint cType = LE32(b, q);
                    uint cSize = LE32(b, q + 4);
                    if (cSize < 8 || q + (int)cSize > len) throw new Exception("AXML 块大小无效");

                    // START_ELEMENT（类型带高位标志，掩码匹配）
                    if ((cType & 0xFFFFu) == 0x0102u)
                    {
                        // 布局（从块起点 q 算）：header(8) lineNumber(4) comment(4) ns(4) name(4)
                        //   attributeStart(2) attributeSize(2) attributeCount(2) …
                        uint nameIdx = LE32(b, q + 20);
                        string elem = nameIdx < strings.Count ? strings[(int)nameIdx] : null;
                        ushort attrStart = LE16(b, q + 24);   // 相对 attrExt 起点（q+16）
                        ushort attrSize = LE16(b, q + 26);
                        ushort attrCount = LE16(b, q + 28);
                        int ap = q + 16 + attrStart;
                        for (int i = 0; i < attrCount; i++)
                        {
                            int rec = ap + i * attrSize;
                            uint an = LE32(b, rec + 4);       // 属性名字符串索引
                            byte at = b[rec + 15];            // typed value dataType
                            uint ad = LE32(b, rec + 16);      // typed value data
                            string aname = an < strings.Count ? strings[(int)an] : null;
                            if (aname == null) continue;

                            string val = null;
                            if (at == 0x03 && ad < strings.Count) val = strings[(int)ad];      // STRING
                            else if (at == 0x10 || at == 0x11) val = ad.ToString();            // INT_DEC / INT_HEX
                            if (val == null) continue;

                            if (elem == "manifest")
                            {
                                if (aname == "package") meta.Package = val;
                                else if (aname == "versionCode") meta.VersionCode = val;
                                else if (aname == "versionName") meta.VersionName = val;
                            }
                            else if (elem == "uses-sdk")
                            {
                                if (aname == "minSdkVersion") meta.MinSdk = val;
                                else if (aname == "targetSdkVersion") meta.TargetSdk = val;
                            }
                        }
                    }
                    q += (int)cSize;
                }
                return meta;
            }

            /// <summary>读池内一条字符串：UTF-16 池长度=uint16；UTF-8 池为变长 u16len/u8len。
            /// public：同类的改包名逻辑（RepackageAxml）也要复用池字符串读取。</summary>
            public static string ReadPoolString(byte[] b, int pos, bool utf8)
            {
                if (utf8)
                {
                    int p = pos;
                    int len16 = b[p++] & 0xFF;
                    if ((len16 & 0x80) != 0) len16 = ((len16 & 0x7f) << 8) | (b[p++] & 0xFF);
                    int len8 = b[p++] & 0xFF;
                    if ((len8 & 0x80) != 0) len8 = ((len8 & 0x7f) << 8) | (b[p++] & 0xFF);
                    return Encoding.UTF8.GetString(b, p, len8);
                }
                int l = LE16(b, pos);
                return Encoding.Unicode.GetString(b, pos + 2, l * 2);
            }

            /// <summary>只读 AXML 的 package 属性值（不解析整个树，供改包名前取旧包名）。</summary>
            public static string ReadRawPackage(byte[] b)
            {
                if (b.Length < 8) throw new Exception("AndroidManifest.xml 损坏");
                int pool = 8;
                uint spSize = LE32(b, pool + 4);
                uint spCount = LE32(b, pool + 8);
                uint spFlags = LE32(b, pool + 16);
                uint stringsStart = LE32(b, pool + 20);
                bool utf8 = (spFlags & 0x00000100u) != 0;
                List<string> strings = new List<string>();
                for (uint i = 0; i < spCount; i++)
                {
                    uint off = LE32(b, pool + 28 + (int)i * 4);
                    strings.Add(ReadPoolString(b, pool + (int)stringsStart + (int)off, utf8));
                }
                int len = b.Length;
                int q = pool + (int)spSize;
                while (q + 8 <= len)
                {
                    uint cType = LE32(b, q);
                    uint cSize = LE32(b, q + 4);
                    if (cSize < 8 || q + (int)cSize > len) break;
                    if ((cType & 0xFFFFu) == 0x0102u)
                    {
                        ushort attrStart = LE16(b, q + 24);
                        ushort attrSize = LE16(b, q + 26);
                        ushort attrCount = LE16(b, q + 28);
                        int ap = q + 16 + attrStart;
                        for (int i = 0; i < attrCount; i++)
                        {
                            int rec = ap + i * attrSize;
                            uint an = LE32(b, rec + 4);
                            byte at = b[rec + 15];
                            uint ad = LE32(b, rec + 16);
                            if (at != 0x03 || ad >= strings.Count) continue;
                            if (an < strings.Count && strings[(int)an] == "package")
                                return strings[(int)ad];
                        }
                        break;
                    }
                    q += (int)cSize;
                }
                return "";
            }
        }
      
    }
}
