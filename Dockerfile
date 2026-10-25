# MeowDown Web 服务镜像（多阶段构建）
# 构建: docker build -t meowdown .
# 运行: docker run -d -p 23333:23333 \
#         -v ./data/web-data:/data/web-data \
#         -v ./data/downloads:/data/downloads \
#         meowdown
# 推荐配合 docker-compose.yml 使用。

# ---------- 构建阶段：还原依赖并发布 ----------
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# 先复制工程文件与影响构建的 props 单独成层，源码变动时复用 NuGet 还原缓存
COPY Directory.Build.props Directory.Build.props
COPY MeowDown/Directory.Build.props MeowDown/Directory.Build.props
COPY MeowDown/MeowDown.csproj MeowDown/
COPY MeowDown.Core/MeowDown.Core.csproj MeowDown.Core/
RUN dotnet restore MeowDown/MeowDown.csproj

# 复制全部源码并发布；wwwroot 静态前端随 publish 一并复制到输出目录
COPY MeowDown/ MeowDown/
COPY MeowDown.Core/ MeowDown.Core/
RUN dotnet publish MeowDown/MeowDown.csproj -c Release -o /app/publish --no-restore

# ---------- 运行阶段：aspnet 运行时 + ffmpeg ----------
FROM mcr.microsoft.com/dotnet/aspnet:9.0

# ffmpeg 供混流使用（程序经 PATH 查找）；curl 供容器健康检查使用
RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish .

# 数据目录约定：/data/web-data 存 Web 用户账号、B站绑定凭据与运行时配置；
# /data/downloads 为下载根目录（Web 任务按用户存放于 web-users/<userId>/）。
# 部署时请将两者挂载为持久卷（见 docker-compose.yml）。
RUN mkdir -p /data/web-data /data/downloads

EXPOSE 23333

ENTRYPOINT ["/app/MeowDown"]
CMD ["serve", "-l", "http://0.0.0.0:23333", "--web-data-dir", "/data/web-data", "--server-download-root", "/data/downloads"]
