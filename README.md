# Javideo Web（Docker 版）

Javideo 桌面版（Tauri + Vue3 + .NET Worker sidecar）的 Docker 复刻版：**单容器**同时托管网页（Vue3 SPA）和后端 API（ASP.NET Core），启动后用浏览器直接访问操作，无需安装任何客户端。

```
浏览器 ──HTTP──▶ Javideo 容器（一个进程）
                 ├─ :8080/        Vue3 静态页面（Worker 直接托管）
                 ├─ :8080/api/*   刮削 / 扫描 / 磁力 / 字幕 / 收藏 / 备份 …
                 ├─ /data         library.db + 头像/预览图/网盘库缓存（挂卷持久化）
                 └─ /media        视频库（bind mount 宿主机目录）
```

## 快速开始

```bash
# 1. 准备视频目录（compose 默认把 ./media 挂到容器 /media）
mkdir -p media && cp -r /你的视频目录/* media/   # 或直接改 compose 里的挂载路径

# 2. 构建并启动
docker compose up -d --build

# 3. 浏览器打开
http://localhost:8080
```

局域网其他设备（手机 / 电视 / 平板）直接访问 `http://<宿主机IP>:8080`。

## 首次使用（全在网页里完成）

1. **设置 → MetaTube**：填写你的 MetaTube 服务地址（如 `http://192.168.1.10:8080`，注意别和本应用端口混淆）并保存，点「测试连接」。
2. **设置 → 媒体库 → 新建**：目录填**容器内路径**（例如 `/media/movies`，即 compose 挂载点），输入后会实时校验目录是否存在；保存。
3. 库页面点「扫描」识别番号，然后逐个刮削入库（或扫描结果里直接入库）。
4. 影片详情抽屉里点「播放」——**浏览器内直接播放**（HTTP Range 流式），预告片同样内嵌播放；字幕按钮走迅雷 GCID 匹配，下载到视频同目录。

> 网络代理（DMM 预告片需要日本 IP）、LLM 翻译等设置与桌面版相同，都在「设置」页配置。

## 与桌面版的差异

| 项目 | 桌面版 | Docker 版 |
|---|---|---|
| 访问方式 | Tauri 桌面窗口 | 浏览器（同源，无 CORS） |
| 播放 | 调用系统外部播放器（`player.path`） | 浏览器内 `<video>` 流式播放（HTTP Range）。mp4/webm/多数 mkv 可直接播；HEVC/WMV/AVI 等浏览器不支持的编码会提示改用本地播放器 |
| 选文件夹 | 原生对话框 | 手动输入**容器内**路径（服务端实时校验存在性） |
| 备份导出 | Tauri 保存对话框 | 浏览器直接下载 zip |
| 打开文件夹 / 关闭最小化到托盘 / 调试模式 | 有 | 已移除（容器内无意义） |
| `/api/backup/export-to` | 写指定路径 | 已移除（任意路径写入在常驻服务里是漏洞面） |
| 字幕 GCID | 仅本地固定盘计算 | Linux 容器内一律计算（bind mount 也算本地） |

数据迁移：桌面版「设置 → 导入导出」导出的 zip 可以直接在网页导入（导入后 `docker restart javideo` 生效）。备份包含数据库和缓存，不包含影片文件；影片路径记录的是导入环境里的路径字符串，跨机器恢复后需要与容器内挂载路径一致（或在数据库中自行修正 `library_directories.path` / `movies.folder_path`）。

## 数据与备份

- 所有可变状态都在 `/data`（SQLite `library.db`、`actors/` 头像、`previews/` 预览图、`covers/` 远程封面缓存、`cache/` 网盘库元数据）。compose 用命名卷 `javideo-data` 持久化，升级镜像不丢数据。
- 定期备份：网页「设置 → 导入导出 → 导出」下载 zip 即可。
- 导入支持最大 1 GB 的 ZIP。上传成功后重启容器，数据库会在启动时恢复；恢复前的数据库快照保存在 `/data/library.db.bak`。
- 如果大文件导入在浏览器网络面板中没有响应码，先确认容器已用新代码重建；若 8085 等访问入口经过反向代理，还需检查代理的请求体大小和上传超时限制。
- 通过局域网 HTTP 地址下载备份时，浏览器可能提示连接不安全。需要在访问入口配置受信任证书的 HTTPS，并从该 HTTPS 地址打开网页和下载备份；修改网页代码不能让 HTTP 连接变成 HTTPS。
- 媒体文件本体永远在你挂载的宿主机目录里；入库生成的 nfo/海报写在影片自己的番号文件夹内。

## 安全提示

- 与桌面版一致，API **没有鉴权**（局域网个人使用的设计前提）。所有能改数据的端点（删除影片可连带删文件、移动文件夹、导入备份覆盖数据库）都暴露在网页里。
- **不要把 8080 端口直接暴露到公网**。如需公网访问，请在前面加反向代理 + 认证（Basic Auth / Authelia 等）。ponytail: 后续如需内建鉴权，可在 Program.cs 加一个 `Javideo__Token` 的 Bearer 中间件。
- compose 映射为 `8080:8080`（监听所有网卡）；只想本机访问可改成 `127.0.0.1:8080:8080`。

## 本地开发（不进容器）

```bash
npm install
npm run worker        # 起 .NET Worker（默认读 %AppData%/Javideo，可用 Javideo__DataDir 覆盖）
ASPNETCORE_URLS=http://127.0.0.1:8080 dotnet run --project worker   # 指定端口时同时托管已构建的 dist
npm run dev           # Vite 开发服务器 :1420，/api 自动代理到 :8080
```

## 目录结构

```
Javideo-docker/
├── Dockerfile            # 三段构建：node 构建前端 → sdk 构建 worker → aspnet 运行时
├── docker-compose.yml
├── src/                  # Vue3 + UnoCSS 前端（与桌面版同源，Web 适配点见上表）
├── worker/               # ASP.NET Core API（托管 SPA + 全部业务端点）
└── media/                # compose 示例挂载点（gitignore）
```
