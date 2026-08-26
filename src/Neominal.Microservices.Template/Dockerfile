# =====================================================================
# Stage 1 - Build
# =====================================================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Önce sadece csproj kopyalanır -> layer caching sayesinde kaynak kod
# değiştiğinde "dotnet restore" tekrar çalışmaz, build hızlanır.
COPY ["Neominal.Microservices.Template.csproj", "./"]
RUN dotnet restore "./Neominal.Microservices.Template.csproj"

COPY . .
RUN dotnet publish "./Neominal.Microservices.Template.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore

# =====================================================================
# Stage 2 - Runtime (minimal, sadece ASP.NET runtime içerir)
# =====================================================================
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Container güvenliği: root olmayan kullanıcı ile çalıştırma
RUN addgroup --system --gid 1000 appgroup && \
    adduser  --system --uid 1000 --ingroup appgroup appuser

COPY --from=build /app/publish .

ENV ASPNETCORE_HTTP_PORTS=8080
ENV ASPNETCORE_ENVIRONMENT=Container
EXPOSE 8080

USER appuser

# Basit container healthcheck (uygulamanın kendi /health endpointini kullanır)
HEALTHCHECK --interval=15s --timeout=5s --start-period=20s --retries=5 \
    CMD ["/bin/sh", "-c", "exec 3<>/dev/tcp/127.0.0.1/8080 || exit 1"]

ENTRYPOINT ["dotnet", "Neominal.Microservices.Template.dll"]
