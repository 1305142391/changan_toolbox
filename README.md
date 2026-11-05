# 长安飞鱼车机 APK 兼容签名工具箱(支持车机二级认证自动验证)
<img width="655" height="542" alt="image" src="https://github.com/user-attachments/assets/de543cc4-8292-4112-b28e-944ebfc88146" />

> 
> 长安飞鱼车机 APK 兼容签名工具箱，Windows .NET WinForms 工具
> 用于对安卓 APK 实现**v1 (JAR) 兼容签名**，解决长安飞鱼车机应用覆盖安装签名校验限制，支持读取 APK 信息、解析应用标签、AXML 二进制解析、resources.arsc 资源解析。

## 📖 项目简介

本工具是面向**长安飞鱼系列车机**的桌面辅助工具，核心功能：

1. **兼容签名（Compat‑Sign）**：读取旧参考 APK 的 v1 证书序列号，动态生成拥有**相同证书序列号**的自签名 RSA‑2048 证书，输出仅带 V1 签名的 APK，实现车机允许覆盖安装。

> 
> ⚠️ 车机校验逻辑：允许**证书序列号一致、密钥不同**的 V1 签名包覆盖升级；原生 apksigner/jarsigner 无法做到修改序列号，因此手写 BouncyCastle 签名实现。

2. APK 信息读取：读取包名、版本、minSdk/targetSdk、解析 V1 签名证书信息。
3. 应用名称解析：解析`AndroidManifest.xml`+`resources.arsc`获取应用显示名称，兼容 Android8 + 稀疏资源表、Compact 条目。
4. AXML 二进制解析：不依赖外部 aapt，纯 C# 解析二进制 Manifest。

> 
> ⚠️ 重要限制：本工具**只生成 V1 (JAR) 签名**，不生成 V2/V3/V4 签名块。仅适用于长安飞鱼老车机（只校验 V1 签名）；新款车机启用 apksig 校验会拒绝安装。

## ✨ 主要特性

- ✅ 完全基于 BouncyCastle 实现 V1 签名，不调用 jarsigner/apksigner，无 Java 环境依赖
- ✅ 根据**参考 APK 证书序列号**动态生成自签 RSA‑2048 证书
- ✅ 证书持久化：**不同参照序列号使用独立密钥目录**，相同序列号复用同一套密钥，保证多次升级包签名一致，可覆盖安装
- ✅ 自动清理原始 APK 中旧 META‑INF 签名文件（`.SF`/`.RSA`/`.DSA`/`.EC`/`MANIFEST.MF`）
- ✅ ZIP 重建，`lib/*.so`库 4096 字节内存对齐，符合 Android APK 规范
- ✅ 内置 CRC32、Raw‑Deflate 压缩，AXML、resources.arsc 纯托管解析
- ✅ 签名完成后内置自校验：校验证书序列号、包名、版本码，提前捕获签名异常

## ⚠️ 已知问题 & 限制

1. **仅支持 V1 签名**：新飞鱼车机开启 V2 校验时，输出 APK 会被拒绝安装；仅用于老车机。
2. 不支持 ZIP64、加密 APK、非 Deflate/Store 压缩方式的 APK。
3. 加固过的 APK 二进制 Manifest/arsc 可能解析失败。
4. 不修改 dex 字节码，仅做签名；修改 APK 资源请在签名**之前完成**。
5. .NET 自带`DeflateStream`输出带 ZLIB 头，代码中使用 BouncyCastle Raw‑Deflate，编译需要 BouncyCastle 完整库。

## 🛠️ 编译环境

- IDE：Visual Studio 2022
- Target Framework：**.NET Framework 4.8**
- NuGet 依赖：`BouncyCastle` (BouncyCastle.Crypto 1.8.x 推荐 1.8.9)
- ## 🚀 使用说明

### 功能 1：兼容签名（核心功能）

> 
> 场景：需要新版本 APK 覆盖安装车机上已安装旧版本（旧 APK 作为参照，提取证书序列号）

1. 运行 `dipay_toolbox.exe`
2. **参照 APK**：选择车机上已经安装的原版 APK（用于读取证书序列号）
3. **待签名 APK**：选择你需要给车机安装的新版本 APK
4. 点击【指定 APP 签名】
5. 输出文件路径：`./data/car_tool/signed‑apks/*_compat.apk`

> 
> 证书存储位置：`./data/car_tool/signing/<归一化证书序列号>/`
> 包含：`local‑signing‑key.pem`（PKCS8 私钥）、`local‑signing‑cert.pem`证书、`local‑signing.pfx`备份 pfx。
> 不同参照序列号会生成独立子目录，互不干扰。

### 功能 2：读取 APP 签名信息

1. 选择 APK 文件，读取包名、版本、V1 证书序列号、SHA256 指纹。

> 
> 如果提示：`未发现v1签名证书（可能使用v2/v3签名，或无签名）`，说明该 APK 没有 V1 签名，**不能作为参照 APK**。

### ⚠️ 使用重要提示

1. **参照 APK 必须带有有效的 V1 签名**，否则工具抛出异常；现代应用只有 V2/V3 签名，无法作为参照。
2. 签名是最后一步！修改资源 / 替换文件操作必须**在签名之前执行**；签名完成不要再修改 APK 任何内容。
3. 如果遇到报错：`签名副本证书序列号与参照不一致`
   - 旧版本 bug 原因：旧版全部共用一套证书，更换不同序列号参照 APK 会报错。请拉取最新代码编译；
   - 如果需要重置密钥：直接删除整个`data/car_tool/signing`文件夹，工具下次运行会重新生成密钥。

## 📂 项目目录说明

```
dipay_toolbox
├── FormMain.cs                # WinForms主窗口，全部签名逻辑在这里
├── data/
│   └── car_tool
│       ├── signing/           # 密钥证书存储（每个序列号独立子文件夹）
│       └── signed‑apks/       # 输出签名后的apk
```

## ❗ 常见报错排查

1. **签名副本证书序列号与参照不一致**
   - 旧版本代码 Bug：更新最新代码，重新编译；
   - 确认参照 APK 拥有 V1 签名；删除`data/car_tool/signing`重置证书。
2. APK 解压失败
   - 原 APK 损坏，或者使用 ZIP64，本工具暂不支持 ZIP64 格式。
3. 车机安装提示解析包错误
   - 确认你的车机支持仅 V1 签名；新款飞鱼车机需要 V2 签名本工具不支持；
   - 检查 so 库 4096 对齐；确认使用 BouncyCastle Raw‑Deflate（不要使用.NET 自带 DeflateStream）。
4. 解析应用信息失败
   - APK 被加固，二进制 AXML 被混淆，解析器无法读取 Manifest。

## 📜 许可证

> 
> 仅供个人学习、研究长安车机调试使用，请勿用于商业分发。
> BouncyCastle 遵循 MIT 许可证。

## 📝 补充说明实现原理

1. 读取参考 APK 的 META‑INF 下`.RSA`文件，提取 PKCS7 内证书的**序列号**
2. 使用 BouncyCastle 生成 RSA‑2048 自签名证书，**强制设置为该序列号**
3. 重建 ZIP，删除旧 META‑INF 签名，生成新`MANIFEST.MF`、`CHANGAN.SF`、`CHANGAN.RSA`(Detached PKCS7 签名)
4. 完成后自校验证书序列号、包名版本，输出签名 APK。

> 
> Android JarVerifier (V1 签名) 校验规则：只校验**证书序列号 + 签名有效性**，不校验公钥是否和原包完全一致；本工具正是利用该特性。
