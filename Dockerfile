# Imagem de produção do Helinho.Web (painel Razor Pages + API do jogo), construída pela Railway.
# Bases fixadas por tag e digest (o digest é o da lista multi-arquitetura: amd64 e arm64).

# --- Build: SDK .NET 10 (global.json pede 10.0.100 com rollForward latestFeature) ---
FROM mcr.microsoft.com/dotnet/sdk:10.0.404-noble@sha256:bef136a371963aaa46ec97882845ef6f54dec15728bb4b128b1c2a400ffbc9b4 AS build
WORKDIR /src

# Restore antes do código-fonte: esta camada só é refeita quando muda um .csproj ou a versão de um pacote.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/Helinho.Domain/Helinho.Domain.csproj src/Helinho.Domain/
COPY src/Helinho.Application/Helinho.Application.csproj src/Helinho.Application/
COPY src/Helinho.Infrastructure/Helinho.Infrastructure.csproj src/Helinho.Infrastructure/
COPY src/Helinho.Web/Helinho.Web.csproj src/Helinho.Web/
RUN dotnet restore src/Helinho.Web/Helinho.Web.csproj

COPY src/ src/
RUN dotnet publish src/Helinho.Web/Helinho.Web.csproj -c Release -o /out --no-restore -p:UseAppHost=false

# --- Runtime: só o ASP.NET Core 10 e a saída do publish (sem SDK, sem código-fonte) ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0.15-noble@sha256:12b1135f5f8b3c0a214f2a680ff2033dff4f684f445d6c697d9524ceeb303586 AS final

# tini como PID 1: no .NET 9+, o dotnet como PID 1 não encerra depois de uma exceção não tratada
# (dotnet/runtime#118049) e fica preso a 100% de CPU em vez de sair com código de erro.
RUN apt-get update \
    && apt-get install -y --no-install-recommends tini=0.19.0-1 \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /out .

# Usuário sem privilégios que já vem na imagem base (APP_UID=1654).
USER $APP_UID
EXPOSE 8080

# A Railway injeta PORT em tempo de execução; sem PORT vale 8080. ASPNETCORE_HTTP_PORTS faz o Kestrel
# escutar em todas as interfaces (0.0.0.0 e [::]). O tini repassa o SIGTERM ao dotnet e "$@" repassa os
# argumentos: `docker run <imagem> seed`. A Railway substitui o ENTRYPOINT no pre-deploy; lá o comando
# é `tini -- dotnet Helinho.Web.dll migrate`.
ENTRYPOINT ["/usr/bin/tini", "--", "/bin/sh", "-c", "ASPNETCORE_HTTP_PORTS=\"${PORT:-8080}\" exec dotnet Helinho.Web.dll \"$@\"", "helinho"]
