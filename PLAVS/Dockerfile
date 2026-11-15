FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY . .
RUN dotnet restore src/PLAVS.Api/PLAVS.Api.csproj --locked-mode
RUN dotnet publish src/PLAVS.Api/PLAVS.Api.csproj -c Release --no-restore -o /output /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /output .
RUN mkdir -p /app/keys /app/models/onnx && chown -R app:app /app/keys /app/models
ENV ASPNETCORE_HTTP_PORTS=8080
ENV Model__Directory=/app/models/onnx
ENV DataProtection__KeyDirectory=/app/keys
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "PLAVS.Api.dll"]
